namespace AutonomousResearchAgent.Application.Chat;

public interface IChatService
{
    IAsyncEnumerable<string> StreamChatAsync(string question, int topK, CancellationToken cancellationToken);
    Task<ChatResult> ChatAsync(string question, int topK, CancellationToken cancellationToken);
}
