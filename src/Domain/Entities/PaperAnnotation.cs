namespace AutonomousResearchAgent.Domain.Entities;

public sealed class PaperAnnotation : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid PaperId { get; set; }
    public Guid? DocumentChunkId { get; set; }
    public int? PageNumber { get; set; }
    public int? OffsetStart { get; set; }
    public int? OffsetEnd { get; set; }
    public string HighlightedText { get; set; } = string.Empty;
    public string? Note { get; set; }

    public User User { get; set; } = null!;
    public Paper Paper { get; set; } = null!;
    public DocumentChunk? DocumentChunk { get; set; }
}