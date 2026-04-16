using System.Text.Json;
using System.Text.Json.Nodes;
using AutonomousResearchAgent.Application.Jobs;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutonomousResearchAgent.Workers;

public sealed class WorkflowExecutor
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IJobRunner _jobRunner;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(
        ApplicationDbContext dbContext,
        IJobRunner jobRunner,
        ILogger<WorkflowExecutor> logger)
    {
        _dbContext = dbContext;
        _jobRunner = jobRunner;
        _logger = logger;
    }

    public async Task ExecuteWorkflowAsync(Job rootJob, CancellationToken cancellationToken)
    {
        var workflow = BuildDependencyGraph(rootJob);
        var executedJobs = new Dictionary<Guid, Job>();
        var pendingJobs = new Queue<Job>(workflow);

        while (pendingJobs.Count > 0)
        {
            var readyJobs = pendingJobs
                .Where(j => AreDependenciesMet(j, executedJobs))
                .ToList();

            if (readyJobs.Count == 0 && pendingJobs.Count > 0)
            {
                var blockedJob = pendingJobs.Peek();
                throw new InvalidOperationException(
                    $"Circular dependency detected or unreachable job {blockedJob.Id}");
            }

            var parallelTasks = readyJobs
                .Select(job => ExecuteJobAsync(job, cancellationToken))
                .ToList();

            var completedJobs = await Task.WhenAll(parallelTasks);

            foreach (var job in completedJobs)
            {
                executedJobs[job.Id] = job;
                pendingJobs = new Queue<Job>(pendingJobs.Where(j => j.Id != job.Id));
            }
        }

        await UpdateRootJobResultAsync(rootJob, executedJobs, cancellationToken);
    }

    private List<Job> BuildDependencyGraph(Job rootJob)
    {
        var allJobs = new List<Job> { rootJob };
        var childJobs = _dbContext.Jobs
            .Where(j => j.ParentJobId == rootJob.Id)
            .ToList();

        allJobs.AddRange(childJobs);

        foreach (var childJob in childJobs)
        {
            var grandChildJobs = _dbContext.Jobs
                .Where(j => j.ParentJobId == childJob.Id)
                .ToList();
            allJobs.AddRange(grandChildJobs);
        }

        return allJobs;
    }

    private bool AreDependenciesMet(Job job, Dictionary<Guid, Job> executedJobs)
    {
        if (job.ParentJobId == null || job.ParentJobId == Guid.Empty)
        {
            return true;
        }

        return executedJobs.ContainsKey(job.ParentJobId.Value) &&
               executedJobs[job.ParentJobId.Value].Status == JobStatus.Completed;
    }

    private async Task<Job> ExecuteJobAsync(Job job, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new KeyValuePair<string, object?>[]
        {
            new("JobId", job.Id),
            new("JobType", job.Type)
        });

        _logger.LogInformation("Starting workflow job {JobId} of type {JobType}", job.Id, job.Type);

        try
        {
            await _jobRunner.RunAsync(job, cancellationToken);
            if (job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Completed;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Completed workflow job {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow job {JobId} failed", job.Id);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        return job;
    }

    private async Task UpdateRootJobResultAsync(
        Job rootJob,
        Dictionary<Guid, Job> executedJobs,
        CancellationToken cancellationToken)
    {
        var allChildJobs = executedJobs.Values
            .Where(j => j.Id != rootJob.Id)
            .ToList();

        var failedJobs = allChildJobs.Where(j => j.Status == JobStatus.Failed).ToList();
        var completedJobs = allChildJobs.Where(j => j.Status == JobStatus.Completed).ToList();

        rootJob.Status = failedJobs.Count > 0 ? JobStatus.Failed : JobStatus.Completed;
        rootJob.ResultJson = new JsonObject
        {
            ["totalJobs"] = executedJobs.Count,
            ["completedJobs"] = completedJobs.Count,
            ["failedJobs"] = failedJobs.Count,
            ["childJobs"] = new JsonArray(allChildJobs.Select(c => new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = c.Type.ToString(),
                ["status"] = c.Status.ToString(),
                ["result"] = c.ResultJson is null ? null : JsonNode.Parse(c.ResultJson),
                ["errorMessage"] = c.ErrorMessage
            }).ToArray())
        }?.ToJsonString();

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Job>> GetExecutableJobsAsync(CancellationToken cancellationToken)
    {
        var queuedJobs = await _dbContext.Jobs
            .Where(j => j.Status == JobStatus.Queued)
            .ToListAsync(cancellationToken);

        var executableJobs = new List<Job>();

        foreach (var job in queuedJobs)
        {
            var dependsOnJobIds = GetDependsOnJobIds(job);
            if (dependsOnJobIds.Count == 0)
            {
                executableJobs.Add(job);
                continue;
            }

            var dependentJobs = await _dbContext.Jobs
                .Where(j => dependsOnJobIds.Contains(j.Id))
                .ToListAsync(cancellationToken);

            var allDependenciesCompleted = dependentJobs.All(d => d.Status == JobStatus.Completed);

            if (allDependenciesCompleted)
            {
                executableJobs.Add(job);
            }
        }

        return executableJobs;
    }

    private static List<Guid> GetDependsOnJobIds(Job job)
    {
        if (string.IsNullOrWhiteSpace(job.DependsOnJobIds))
        {
            return [];
        }

        try
        {
            var array = JsonNode.Parse(job.DependsOnJobIds)?.AsArray();
            return array?
                .Select(n => n?.GetValue<Guid>())
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<WorkflowValidationResult> ValidateWorkflowAsync(
        Guid rootJobId,
        CancellationToken cancellationToken)
    {
        var jobs = await _dbContext.Jobs
            .Where(j => j.ParentJobId == rootJobId || j.Id == rootJobId)
            .ToListAsync(cancellationToken);

        var jobDict = jobs.ToDictionary(j => j.Id);
        var errors = new List<string>();

        foreach (var job in jobs.Where(j => j.DependsOnJobIds != null))
        {
            var dependsOnIds = GetDependsOnJobIds(job);
            foreach (var depId in dependsOnIds)
            {
                if (!jobDict.ContainsKey(depId))
                {
                    errors.Add($"Job {job.Id} depends on non-existent job {depId}");
                }
            }
        }

        var cycleError = DetectCycle(jobs);
        if (cycleError != null)
        {
            errors.Add(cycleError);
        }

        return new WorkflowValidationResult(errors.Count == 0, errors);
    }

    private static string? DetectCycle(List<Job> jobs)
    {
        var visited = new HashSet<Guid>();
        var recursionStack = new HashSet<Guid>();

        bool DFS(Guid jobId, Dictionary<Guid, Job> jobDict)
        {
            visited.Add(jobId);
            recursionStack.Add(jobId);

            var job = jobDict[jobId];
            var dependsOnIds = string.IsNullOrWhiteSpace(job.DependsOnJobIds)
                ? []
                : JsonNode.Parse(job.DependsOnJobIds)?.AsArray()
                    ?.Select(n => n?.GetValue<Guid>())
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList() ?? [];

            foreach (var depId in dependsOnIds)
            {
                if (!visited.Contains(depId))
                {
                    if (DFS(depId, jobDict))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(depId))
                {
                    return true;
                }
            }

            recursionStack.Remove(jobId);
            return false;
        }

        foreach (var job in jobs)
        {
            if (!visited.Contains(job.Id))
            {
                if (DFS(job.Id, jobs.ToDictionary(j => j.Id)))
                {
                    return $"Circular dependency detected starting from job {job.Id}";
                }
            }
        }

        return null;
    }
}

public sealed record WorkflowValidationResult(bool IsValid, List<string> Errors);
