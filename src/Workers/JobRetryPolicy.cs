using System.Text.Json;
using AutonomousResearchAgent.Application.Jobs;

namespace AutonomousResearchAgent.Workers;

public sealed class JobRetryPolicy
{
    private const int BaseDelaySeconds = 30;
    private const int MaxDelaySeconds = 3600;
    private const int MaxAttempts = 5;

    public int BaseDelay { get; init; } = BaseDelaySeconds;
    public int MaxDelay { get; init; } = MaxDelaySeconds;
    public int MaxAttemptsValue { get; init; } = MaxAttempts;

    public TimeSpan GetDelayForAttempt(int attempt)
    {
        var delaySeconds = Math.Min(Math.Pow(2, attempt) * BaseDelay, MaxDelay);
        return TimeSpan.FromSeconds(delaySeconds);
    }

    public bool ShouldRetry(int currentAttempt, Exception exception)
    {
        if (currentAttempt >= MaxAttemptsValue)
        {
            return false;
        }

        return exception switch
        {
            HttpRequestException httpEx when httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests => true,
            HttpRequestException httpEx when httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable => true,
            TaskCanceledException => true,
            OperationCanceledException => false,
            InvalidOperationException => false,
            _ => currentAttempt < 3
        };
    }

    public bool IsRateLimitException(Exception exception) =>
        exception is HttpRequestException httpEx && httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

    public bool IsServiceUnavailableException(Exception exception) =>
        exception is HttpRequestException httpEx && httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable;

    public bool IsNetworkError(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException;

    public RetryPolicyData CreateRetryPolicyData(int attempt, Exception exception, TimeSpan delay)
    {
        return new RetryPolicyData
        {
            AttemptNumber = attempt,
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            DelaySeconds = (int)delay.TotalSeconds,
            ShouldRetry = ShouldRetry(attempt, exception),
            Reason = GetRetryReason(exception)
        };
    }

    private static string GetRetryReason(Exception exception) => exception switch
    {
        HttpRequestException httpEx when httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests => "Rate limit exceeded (429)",
        HttpRequestException httpEx when httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable => "Service unavailable (503)",
        HttpRequestException => "Network error",
        TaskCanceledException => "Request timeout",
        _ => "Transient error"
    };

    public string Serialize() => JsonSerializer.Serialize(new
    {
        baseDelay = BaseDelay,
        maxDelay = MaxDelay,
        maxAttempts = MaxAttemptsValue
    });
}
