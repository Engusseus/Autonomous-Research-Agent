using AutonomousResearchAgent.Domain.Enums;

namespace AutonomousResearchAgent.Domain.Entities;

public sealed class PotentialDuplicate : AuditableEntity
{
    public Guid PaperAId { get; set; }
    public Guid PaperBId { get; set; }
    public double SimilarityScore { get; set; }
    public DuplicateReviewStatus Status { get; set; } = DuplicateReviewStatus.Pending;
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Notes { get; set; }

    public Paper? PaperA { get; set; }
    public Paper? PaperB { get; set; }
    public User? ReviewedByUser { get; set; }
}
