using AutonomousResearchAgent.Domain.Entities;

namespace AutonomousResearchAgent.Domain.Entities;

public sealed class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    public User User { get; set; } = null!;
}
