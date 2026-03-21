using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutonomousResearchAgent.Infrastructure.Services;

public interface IEmbeddingIndexingService
{
    Task UpsertPaperAbstractAsync(Paper paper, CancellationToken cancellationToken);
    Task UpsertSummaryAsync(PaperSummary summary, CancellationToken cancellationToken);
}

public sealed class EmbeddingIndexingService(
    ApplicationDbContext dbContext,
    ILocalEmbeddingClient embeddingClient,
    IOptions<LocalEmbeddingOptions> options,
    ILogger<EmbeddingIndexingService> logger) : IEmbeddingIndexingService
{
    private readonly LocalEmbeddingOptions _options = options.Value;

    public Task UpsertPaperAbstractAsync(Paper paper, CancellationToken cancellationToken)
        => UpsertAsync(paper, null, EmbeddingType.PaperAbstract, paper.Abstract, cancellationToken);

    public async Task UpsertSummaryAsync(PaperSummary summary, CancellationToken cancellationToken)
    {
        var paper = summary.Paper;
        if (paper is null && summary.PaperId != Guid.Empty)
        {
            paper = await dbContext.Papers.FirstOrDefaultAsync(p => p.Id == summary.PaperId, cancellationToken);
        }

        await UpsertAsync(paper, summary, EmbeddingType.Summary, summary.SearchText, cancellationToken);
    }

    private async Task UpsertAsync(
        Paper? paper,
        PaperSummary? summary,
        EmbeddingType embeddingType,
        string? content,
        CancellationToken cancellationToken)
    {
        var paperId = paper?.Id ?? summary?.PaperId;
        var summaryId = summary?.Id;

        var existing = await dbContext.PaperEmbeddings
            .FirstOrDefaultAsync(e =>
                e.PaperId == paperId &&
                e.SummaryId == summaryId &&
                e.EmbeddingType == embeddingType,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            if (existing is not null)
            {
                dbContext.PaperEmbeddings.Remove(existing);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Removed embedding for {EmbeddingType} {TargetId}", embeddingType, paperId ?? summaryId);
            }

            return;
        }

        var vector = await embeddingClient.GenerateEmbeddingAsync(content.Trim(), cancellationToken);

        if (existing is null)
        {
            existing = new PaperEmbedding
            {
                PaperId = paperId,
                SummaryId = summaryId,
                EmbeddingType = embeddingType,
                Paper = paper,
                Summary = summary
            };
            dbContext.PaperEmbeddings.Add(existing);
        }

        existing.PaperId = paperId;
        existing.SummaryId = summaryId;
        existing.EmbeddingType = embeddingType;
        existing.Paper = paper;
        existing.Summary = summary;
        existing.ModelName = _options.ModelName;
        existing.Vector = vector;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Upserted embedding for {EmbeddingType} {TargetId}", embeddingType, paperId ?? summaryId);
    }
}
