namespace AutonomousResearchAgent.Domain.Entities;

public sealed class CollectionPaper
{
    public Guid CollectionId { get; set; }
    public Guid PaperId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public Collection Collection { get; set; } = null!;
    public Paper Paper { get; set; } = null!;
}