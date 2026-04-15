namespace AutonomousResearchAgent.Domain.Entities;

public sealed class TrendResult : AuditableEntity
{
    public string Field { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
}