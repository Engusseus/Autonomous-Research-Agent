using AutonomousResearchAgent.Application.Search;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.Persistence;
using AutonomousResearchAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task SemanticSearchAsync_ranks_papers_using_best_embedding_across_abstract_and_summary_vectors()
    {
        await using var dbContext = CreateDbContext();
        var queryEmbeddingService = new FakeEmbeddingService(new[] { 1f, 0f });
        var service = CreateSearchService(dbContext, queryEmbeddingService);

        var paperWithStrongSummary = CreatePaper(
            "Paper with strong summary",
            abstractEmbedding: new[] { 0.25f, 0.97f },
            summaryEmbedding: new[] { 1f, 0f });

        var paperWithModerateAbstract = CreatePaper(
            "Paper with moderate abstract",
            abstractEmbedding: new[] { 0.8f, 0.6f });

        dbContext.Papers.AddRange(paperWithStrongSummary, paperWithModerateAbstract);
        await dbContext.SaveChangesAsync();

        var result = await service.SemanticSearchAsync(
            new SemanticSearchRequestModel("semantic query", PageNumber: 1, PageSize: 10, MaxCandidates: 10),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(paperWithStrongSummary.Id, result.Items.First().PaperId);
        Assert.Equal("semantic", result.Items.First().MatchType);
        Assert.Equal(1.0, result.Items.First().Score, 5);
        Assert.Equal(paperWithModerateAbstract.Id, result.Items.Last().PaperId);
        Assert.Equal(0.8, result.Items.Last().Score, 5);
    }

    [Fact]
    public async Task HybridSearchAsync_blends_keyword_and_semantic_scores_into_one_result_per_paper()
    {
        await using var dbContext = CreateDbContext();
        var queryEmbeddingService = new FakeEmbeddingService(new[] { 1f, 0f });
        var service = CreateSearchService(dbContext, queryEmbeddingService);

        var keywordAndSemanticPaper = CreatePaper(
            "Quantum paper",
            abstractEmbedding: new[] { 0.2f, 0.98f },
            abstractText: "General relativity abstract");

        var semanticOnlyPaper = CreatePaper(
            "Unrelated paper",
            abstractEmbedding: new[] { 1f, 0f },
            abstractText: "Completely unrelated abstract");

        dbContext.Papers.AddRange(keywordAndSemanticPaper, semanticOnlyPaper);
        await dbContext.SaveChangesAsync();

        var result = await service.HybridSearchAsync(
            new HybridSearchRequestModel(
                Query: "Quantum",
                KeywordWeight: 0.25,
                SemanticWeight: 0.75,
                PageNumber: 1,
                PageSize: 10,
                MaxCandidates: 10),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(semanticOnlyPaper.Id, result.Items.First().PaperId);
        Assert.Equal("hybrid", result.Items.First().MatchType);
        Assert.Equal(0.75, result.Items.First().Score, 5);

        var blended = result.Items.Last();
        Assert.Equal(keywordAndSemanticPaper.Id, blended.PaperId);
        Assert.Equal("hybrid", blended.MatchType);
        Assert.Equal(0.39997, blended.Score, 5);

        var highlights = Assert.IsType<System.Text.Json.Nodes.JsonObject>(blended.Highlights);
        Assert.Equal(1.0, highlights["keywordScore"]!.GetValue<double>(), 5);
        Assert.Equal(0.19996, highlights["semanticScore"]!.GetValue<double>(), 5);
    }

    private static SearchService CreateSearchService(ApplicationDbContext dbContext, IEmbeddingService embeddingService)
    {
        return new SearchService(dbContext, embeddingService, NullLogger<SearchService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Paper CreatePaper(
        string title,
        float[] abstractEmbedding,
        float[]? summaryEmbedding = null,
        string? abstractText = null)
    {
        var paper = new Paper
        {
            Id = Guid.NewGuid(),
            Title = title,
            Abstract = abstractText ?? $"{title} abstract",
            Authors = ["Test Author"],
            Year = 2025,
            Venue = "Test Venue"
        };

        paper.Embeddings.Add(new PaperEmbedding
        {
            Id = Guid.NewGuid(),
            Paper = paper,
            EmbeddingType = EmbeddingType.PaperAbstract,
            Vector = abstractEmbedding,
            ModelName = "test-model"
        });

        if (summaryEmbedding is not null)
        {
            var summary = new PaperSummary
            {
                Id = Guid.NewGuid(),
                Paper = paper,
                SearchText = $"{title} summary"
            };

            paper.Summaries.Add(summary);
            paper.Embeddings.Add(new PaperEmbedding
            {
                Id = Guid.NewGuid(),
                Paper = paper,
                Summary = summary,
                EmbeddingType = EmbeddingType.Summary,
                Vector = summaryEmbedding,
                ModelName = "test-model"
            });
        }

        return paper;
    }

    private sealed class FakeEmbeddingService(float[] queryEmbedding) : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string content, EmbeddingType embeddingType, CancellationToken cancellationToken)
            => Task.FromResult(queryEmbedding);

        public Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(queryEmbedding);
    }
}
