using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutonomousResearchAgent.Application.Jobs;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.BackgroundJobs;
using AutonomousResearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutonomousResearchAgent.Workers;

public sealed class DatabaseJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<DatabaseJobWorker> logger) : BackgroundService
{
    private readonly BackgroundJobOptions _options = options.Value;
    private readonly JobRetryPolicy _retryPolicy = new();
    private static readonly ActivitySource ActivitySource = new("DatabaseJobWorker");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var jobRunner = scope.ServiceProvider.GetRequiredService<IJobRunner>();

                var job = await ClaimNextQueuedJobAsync(dbContext, stoppingToken);
                if (job is null)
                {
                    await ProcessDueSavedSearchesAsync(dbContext, stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                using var activity = ActivitySource.StartActivity("ProcessJob", ActivityKind.Internal);
                activity?.SetTag("job.id", job.Id.ToString());
                activity?.SetTag("job.type", job.Type.ToString());

                await ProcessJobWithRetryAsync(job, dbContext, jobRunner, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database job worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task ProcessJobWithRetryAsync(
        Job job,
        ApplicationDbContext dbContext,
        IJobRunner jobRunner,
        CancellationToken cancellationToken)
    {
        var retryCount = GetRetryCount(job);
        var lastAttemptTime = GetLastAttemptTime(job);

        if (lastAttemptTime.HasValue)
        {
            var delay = _retryPolicy.GetDelayForAttempt(retryCount);
            var timeSinceLastAttempt = DateTimeOffset.UtcNow - lastAttemptTime.Value;
            if (timeSinceLastAttempt < delay)
            {
                var remainingDelay = delay - timeSinceLastAttempt;
                logger.LogInformation(
                    "Job {JobId} is waiting for retry delay ({RemainingSeconds}s remaining)",
                    job.Id, remainingDelay.TotalSeconds);
                await Task.Delay(remainingDelay, cancellationToken);
            }
        }

        try
        {
            await jobRunner.RunAsync(job, cancellationToken);
            if (job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Completed;
            }
            job.RetryCount = 0;
            job.RetryPolicyJson = null;
            job.LastAttemptAt = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background job {JobId} failed (attempt {Attempt})", job.Id, retryCount + 1);

            if (_retryPolicy.ShouldRetry(retryCount, ex))
            {
                var delay = _retryPolicy.GetDelayForAttempt(retryCount);
                var retryData = _retryPolicy.CreateRetryPolicyData(retryCount + 1, ex, delay);

                job.Status = JobStatus.Queued;
                job.ErrorMessage = $"Retry {retryCount + 1}: {ex.Message}";
                job.RetryCount = retryCount + 1;
                job.RetryPolicyJson = JsonSerializer.Serialize(retryData);
                job.LastAttemptAt = DateTimeOffset.UtcNow;

                logger.LogInformation(
                    "Job {JobId} scheduled for retry {RetryCount} after {DelaySeconds}s",
                    job.Id, retryCount + 1, delay.TotalSeconds);
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.RetryCount = retryCount + 1;
                job.LastAttemptAt = DateTimeOffset.UtcNow;

                await MoveToDeadLetterQueueAsync(job, dbContext, ex, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static readonly string QueuedStatusLiteral = JobStatus.Queued.ToString();

    private async Task<Job?> ClaimNextQueuedJobAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var job = await dbContext.Jobs
                .FromSqlRaw(@"SELECT *
FROM jobs
WHERE ""Id"" = (
    SELECT ""Id""
    FROM jobs
    WHERE ""Status"" = {0}
    AND (""RetryCount"" IS NULL OR ""RetryCount"" < {1})
    ORDER BY ""CreatedAt""
    FOR UPDATE SKIP LOCKED
    LIMIT 1)", QueuedStatusLiteral, _retryPolicy.MaxAttemptsValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            job.Status = JobStatus.Running;
            job.ErrorMessage = null;
            job.LastAttemptAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return job;
        }

        var fallbackJob = await dbContext.Jobs
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(j => j.Status == JobStatus.Queued, cancellationToken);

        if (fallbackJob is null)
        {
            return null;
        }

        var updatedRows = await dbContext.Jobs
            .Where(j => j.Id == fallbackJob.Id && j.Status == JobStatus.Queued)
            .ExecuteUpdateAsync(j => j
                .SetProperty(p => p.Status, JobStatus.Running)
                .SetProperty(p => p.ErrorMessage, (string?)null)
                .SetProperty(p => p.LastAttemptAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (updatedRows == 0)
        {
            return null;
        }

        fallbackJob.Status = JobStatus.Running;
        fallbackJob.ErrorMessage = null;
        fallbackJob.LastAttemptAt = DateTimeOffset.UtcNow;
        return fallbackJob;
    }

    private async Task MoveToDeadLetterQueueAsync(
        Job job,
        ApplicationDbContext dbContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var deadLetterJob = new DeadLetterJob
        {
            OriginalJobId = job.Id,
            OriginalJobType = job.Type.ToString(),
            OriginalJobPayload = job.PayloadJson,
            ErrorMessage = exception.Message,
            ExceptionType = exception.GetType().Name,
            FailedAt = DateTimeOffset.UtcNow,
            RetryCount = job.RetryCount ?? 0,
            StackTrace = exception.StackTrace
        };

        dbContext.DeadLetterJobs.Add(deadLetterJob);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Moved job {JobId} to dead letter queue after {RetryCount} attempts",
            job.Id, job.RetryCount);
    }

    private async Task ProcessDueSavedSearchesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var dueSearches = await dbContext.SavedSearches
            .Where(s => s.IsActive && s.Schedule != ScheduleType.Manual)
            .ToListAsync(cancellationToken);

        foreach (var search in dueSearches)
        {
            if (IsDueForPolling(search, now))
            {
                var existingJob = await dbContext.Jobs
                    .AnyAsync(j => j.TargetEntityId == search.Id &&
                                   j.Type == JobType.WatchlistPolling &&
                                   j.Status == JobStatus.Queued, cancellationToken);

                if (!existingJob)
                {
                    var job = new Job
                    {
                        Type = JobType.WatchlistPolling,
                        Status = JobStatus.Queued,
                        TargetEntityId = search.Id,
                        PayloadJson = JsonNode.Parse(JsonSerializer.Serialize(new { savedSearchId = search.Id }))?.ToJsonString() ?? "{}",
                        CreatedBy = "system-watchlist"
                    };
                    dbContext.Jobs.Add(job);
                    logger.LogInformation("Created WatchlistPolling job for saved search {SavedSearchId}", search.Id);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDueForPolling(SavedSearch search, DateTimeOffset now)
    {
        if (!search.LastRunAt.HasValue)
        {
            return true;
        }

        var lastRun = search.LastRunAt.Value;

        return search.Schedule switch
        {
            ScheduleType.Hourly => now - lastRun >= TimeSpan.FromHours(1),
            ScheduleType.Daily => now - lastRun >= TimeSpan.FromDays(1),
            ScheduleType.Weekly => now - lastRun >= TimeSpan.FromDays(7),
            _ => false
        };
    }

    private static int GetRetryCount(Job job) =>
        job.RetryCount ?? 0;

    private static DateTimeOffset? GetLastAttemptTime(Job job) =>
        job.LastAttemptAt;
}

public sealed record RetryPolicyData
{
    public int AttemptNumber { get; init; }
    public string ExceptionType { get; init; } = string.Empty;
    public string ExceptionMessage { get; init; } = string.Empty;
    public int DelaySeconds { get; init; }
    public bool ShouldRetry { get; init; }
    public string Reason { get; init; } = string.Empty;
}
