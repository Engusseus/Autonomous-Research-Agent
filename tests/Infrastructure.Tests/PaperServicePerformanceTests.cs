using System.Diagnostics;
using System.Text.Json.Nodes;
using AutonomousResearchAgent.Application.Common;
using AutonomousResearchAgent.Application.Jobs;
using AutonomousResearchAgent.Application.Papers;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Infrastructure.Persistence;
using AutonomousResearchAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Infrastructure.Tests;

public sealed class PaperServicePerformanceTests
{
    private const int SimulatedDelayMs = 100;
    private const int BatchSize = 10;

    [Fact]
    public async Task ImportFromDoiAsync_SequentialBaseline()
    {
        var mockCrossRefClient = new Mock<ICrossRefClient>();
        mockCrossRefClient
            .Setup(x => x.GetByDoiAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string doi, CancellationToken ct) =>
            {
                await Task.Delay(SimulatedDelayMs, ct);
                return new CrossRefPaper(doi, "Title", "Abstract", new[] { "Author" }, DateTimeOffset.Now, "Publisher", new[] { "Journal" }, "Type");
            });

        var service = CreatePaperService(mockCrossRefClient.Object, Mock.Of<IArxivClient>());

        var queries = Enumerable.Range(1, BatchSize).Select(i => $"10.1000/{i}").ToList();
        var command = new ImportPapersCommand(queries, BatchSize, false, "doi");

        var sw = Stopwatch.StartNew();
        await service.ImportAsync(command, CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"ImportFromDoiAsync took {sw.ElapsedMilliseconds}ms for {BatchSize} items.");

        // Sequential should take at least BatchSize * SimulatedDelayMs
        // Parallel should take closer to SimulatedDelayMs
    }

    [Fact]
    public async Task ImportFromArxivAsync_SequentialBaseline()
    {
        var mockArxivClient = new Mock<IArxivClient>();
        mockArxivClient
            .Setup(x => x.GetPaperAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string id, CancellationToken ct) =>
            {
                await Task.Delay(SimulatedDelayMs, ct);
                return new ArxivPaper(id, "Title", "Summary", new[] { "Author" }, DateTimeOffset.Now, DateTimeOffset.Now, new[] { "Cat" }, "url", "doi");
            });

        var service = CreatePaperService(Mock.Of<ICrossRefClient>(), mockArxivClient.Object);

        var queries = Enumerable.Range(1, BatchSize).Select(i => $"arxiv:{i}").ToList();
        var command = new ImportPapersCommand(queries, BatchSize, false, "arxiv");

        var sw = Stopwatch.StartNew();
        await service.ImportAsync(command, CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"ImportFromArxivAsync took {sw.ElapsedMilliseconds}ms for {BatchSize} items.");
    }

    private static PaperService CreatePaperService(ICrossRefClient crossRefClient, IArxivClient arxivClient)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new ApplicationDbContext(options);
        var jobService = new JobService(dbContext, NullLogger<JobService>.Instance);

        return new PaperService(
            dbContext,
            Mock.Of<ISemanticScholarClient>(),
            arxivClient,
            crossRefClient,
            jobService,
            new NoOpEmbeddingIndexingService(),
            NullLogger<PaperService>.Instance);
    }

    private sealed class NoOpEmbeddingIndexingService : IEmbeddingIndexingService
    {
        public Task UpsertPaperAbstractAsync(Paper paper, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertPaperAbstractAsync(IEnumerable<Paper> papers, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertSummaryAsync(PaperSummary summary, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertDocumentChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertDocumentChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
