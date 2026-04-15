namespace AutonomousResearchAgent.Domain.Entities;

public sealed class Collection : AuditableEntity
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsShared { get; set; }
    public int SortOrder { get; set; }

    public ICollection<CollectionPaper> CollectionPapers { get; set; } = [];
}