namespace AutonomousResearchAgent.Domain.Entities;

public sealed class PaperCitation : AuditableEntity
{
    public Guid SourcePaperId { get; set; }
    public Guid TargetPaperId { get; set; }
    public string? CitationContext { get; set; }
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    public Paper SourcePaper { get; set; } = null!;
    public Paper TargetPaper { get; set; } = null!;
}
