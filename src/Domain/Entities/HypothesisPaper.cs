using AutonomousResearchAgent.Domain.Enums;

namespace AutonomousResearchAgent.Domain.Entities;

public sealed class HypothesisPaper
{
    public Guid Id { get; set; }
    public Guid HypothesisId { get; set; }
    public Guid PaperId { get; set; }
    public EvidenceType EvidenceType { get; set; }
    public string? EvidenceText { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Hypothesis Hypothesis { get; set; } = null!;
    public Paper Paper { get; set; } = null!;
}
