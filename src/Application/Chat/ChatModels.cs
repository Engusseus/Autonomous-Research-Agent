namespace AutonomousResearchAgent.Application.Chat;

public sealed record ChunkSearchResult(
    Guid PaperId,
    string PaperTitle,
    Guid ChunkId,
    string ChunkText,
    double Score);

public sealed record ChatResult(
    string Answer,
    IReadOnlyList<ChunkSearchResult> Sources);
