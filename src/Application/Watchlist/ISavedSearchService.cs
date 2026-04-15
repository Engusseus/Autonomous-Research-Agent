using AutonomousResearchAgent.Application.Common;

namespace AutonomousResearchAgent.Application.Watchlist;

public interface ISavedSearchService
{
    Task<PagedResult<SavedSearchModel>> ListAsync(SavedSearchQuery query, CancellationToken cancellationToken);
    Task<SavedSearchModel> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SavedSearchModel> CreateAsync(CreateSavedSearchCommand command, CancellationToken cancellationToken);
    Task<SavedSearchModel> UpdateAsync(Guid id, UpdateSavedSearchCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<RunSavedSearchResult> RunAsync(Guid id, CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<PagedResult<NotificationModel>> ListAsync(NotificationQuery query, CancellationToken cancellationToken);
    Task<NotificationModel> MarkAsReadAsync(Guid id, CancellationToken cancellationToken);
    Task<int> MarkAllAsReadAsync(int userId, CancellationToken cancellationToken);
    Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken);
}
