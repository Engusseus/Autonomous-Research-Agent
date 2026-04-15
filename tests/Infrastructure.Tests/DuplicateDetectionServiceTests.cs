using System.Text.Json;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.Persistence;
using AutonomousResearchAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Infrastructure.Tests;

public sealed class DuplicateDetectionServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task StartDuplicateDetectionJobAsync_CreatesJobWithDefaultThreshold()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var options = Options.Create(new DuplicateDetectionOptions());
        var logger = NullLogger<DuplicateDetectionService>.Instance;
        var service = new DuplicateDetectionService(dbContext, options, logger);

        // Act
        var jobId = await service.StartDuplicateDetectionJobAsync();

        // Assert
        var job = await dbContext.Jobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(JobType.DuplicateDetection, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);

        var payload = JsonSerializer.Deserialize<JsonElement>(job.PayloadJson);
        Assert.True(payload.TryGetProperty("threshold", out var thresholdProperty));
        Assert.Equal(0.95, thresholdProperty.GetDouble());
    }

    [Fact]
    public async Task StartDuplicateDetectionJobAsync_CreatesJobWithSpecifiedThreshold()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var options = Options.Create(new DuplicateDetectionOptions());
        var logger = NullLogger<DuplicateDetectionService>.Instance;
        var service = new DuplicateDetectionService(dbContext, options, logger);
        double expectedThreshold = 0.85;

        // Act
        var jobId = await service.StartDuplicateDetectionJobAsync(threshold: expectedThreshold);

        // Assert
        var job = await dbContext.Jobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(JobType.DuplicateDetection, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);

        var payload = JsonSerializer.Deserialize<JsonElement>(job.PayloadJson);
        Assert.True(payload.TryGetProperty("threshold", out var thresholdProperty));
        Assert.Equal(expectedThreshold, thresholdProperty.GetDouble());
    }
}
