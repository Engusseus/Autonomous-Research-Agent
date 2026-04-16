using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace AutonomousResearchAgent.Api.Hubs;

[Authorize]
public class JobStatusHub : Hub
{
    private static readonly Dictionary<string, HashSet<string>> _userConnections = new();

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            if (!_userConnections.ContainsKey(userId))
            {
                _userConnections[userId] = new HashSet<string>();
            }
            _userConnections[userId].Add(Context.ConnectionId);
        }

        await base.OnConnectedAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, "Jobs");
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId) && _userConnections.ContainsKey(userId))
        {
            _userConnections[userId].Remove(Context.ConnectionId);
            if (_userConnections[userId].Count == 0)
            {
                _userConnections.Remove(userId);
            }
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Jobs");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinUserGroup()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
        }
    }

    public async Task LeaveUserGroup()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");
        }
    }

    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Job_{jobId}");
    }

    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Job_{jobId}");
    }
}

public static class JobStatusHubExtensions
{
    public static Task NotifyJobStatusChanged(
        this IHubContext<JobStatusHub> hubContext,
        string jobId,
        object jobStatus)
    {
        return hubContext.Clients.Group($"Job_{jobId}")
            .SendAsync("JobStatusChanged", jobStatus);
    }

    public static Task NotifyJobCompleted(
        this IHubContext<JobStatusHub> hubContext,
        string jobId,
        object jobStatus)
    {
        return hubContext.Clients.Group($"Job_{jobId}")
            .SendAsync("JobCompleted", jobStatus);
    }

    public static Task NotifyJobFailed(
        this IHubContext<JobStatusHub> hubContext,
        string jobId,
        object jobStatus)
    {
        return hubContext.Clients.Group($"Job_{jobId}")
            .SendAsync("JobFailed", jobStatus);
    }

    public static Task BroadcastJobStatusChanged(
        this IHubContext<JobStatusHub> hubContext,
        object jobStatus)
    {
        return hubContext.Clients.Group("Jobs")
            .SendAsync("JobStatusChanged", jobStatus);
    }

    public static Task NotifyUserJobCompleted(
        this IHubContext<JobStatusHub> hubContext,
        string userId,
        object jobStatus)
    {
        return hubContext.Clients.Group($"User_{userId}")
            .SendAsync("JobCompleted", jobStatus);
    }
}