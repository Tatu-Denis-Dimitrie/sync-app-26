using SyncApp26.Shared.DTOs;

namespace SyncApp26.Application.IServices;

public interface ISyncNotificationService
{
    Task SendProgress(string connectionId, string message, int percent);
    Task SendComparison(string connectionId, UserComparisonDTO comparison);
    Task SendSyncProgress(string connectionId, int processed, int failed, int skipped);
}
