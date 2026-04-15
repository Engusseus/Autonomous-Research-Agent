using AutonomousResearchAgent.Domain.Enums;

namespace AutonomousResearchAgent.Domain.Entities;

public sealed class SavedSearch : AuditableEntity
{
    public int UserId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? Field { get; set; }
    public ScheduleType Schedule { get; set; } = ScheduleType.Manual;
    public DateTimeOffset? LastRunAt { get; set; }
    public bool IsActive { get; set; } = true;
}
