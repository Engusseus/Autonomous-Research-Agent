using AutonomousResearchAgent.Api.Authorization;
using AutonomousResearchAgent.Api.Contracts.Common;
using AutonomousResearchAgent.Api.Contracts.Watchlist;
using AutonomousResearchAgent.Api.Extensions;
using AutonomousResearchAgent.Application.Watchlist;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutonomousResearchAgent.Api.Controllers;

[ApiController]
[Route($"{ApiConstants.ApiPrefix}/notifications")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PolicyNames.ReadAccess)]
    [ProducesResponseType(typeof(PagedResponse<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<NotificationDto>>> GetNotifications(
        [FromQuery] NotificationQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return Unauthorized();
        var result = await notificationService.ListAsync(request.ToApplicationModel(int.Parse(userId.Value.ToString())), cancellationToken);
        return Ok(result.ToPagedResponse(item => item.ToDto()));
    }

    [HttpGet("unread-count")]
    [Authorize(Policy = PolicyNames.ReadAccess)]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return Unauthorized();
        var count = await notificationService.GetUnreadCountAsync(int.Parse(userId.Value.ToString()), cancellationToken);
        return Ok(new UnreadCountResponse(count));
    }

    [HttpPut("{id:guid}/read")]
    [Authorize(Policy = PolicyNames.EditAccess)]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await notificationService.MarkAsReadAsync(id, cancellationToken);
        return Ok(notification.ToDto());
    }

    [HttpPut("read-all")]
    [Authorize(Policy = PolicyNames.EditAccess)]
    [ProducesResponseType(typeof(MarkAllReadResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarkAllReadResponse>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return Unauthorized();
        var count = await notificationService.MarkAllAsReadAsync(int.Parse(userId.Value.ToString()), cancellationToken);
        return Ok(new MarkAllReadResponse(count));
    }

    private Guid? GetUserId() => User.GetUserId();
}
