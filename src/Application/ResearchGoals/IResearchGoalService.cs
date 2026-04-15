using AutonomousResearchAgent.Application.Common;

namespace AutonomousResearchAgent.Application.ResearchGoals;

public interface IResearchGoalService
{
    Task<ResearchGoalModel> CreateResearchGoalAsync(CreateResearchGoalCommand command, CancellationToken cancellationToken);
    Task<ResearchGoalModel> GetResearchGoalStatusAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed record CreateResearchGoalCommand(
    string Goal,
    int MaxPapers,
    string? Field,
    string? CreatedBy);

public sealed record ResearchGoalModel(
    Guid JobId,
    string Status,
    List<ResearchGoalStepModel> Steps,
    string? ResultJson);

public sealed record ResearchGoalStepModel(
    string StepType,
    string Description,
    Guid? SubJobId,
    string Status);