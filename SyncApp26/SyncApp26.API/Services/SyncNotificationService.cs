using Microsoft.AspNetCore.SignalR;
using SyncApp26.API.Hubs;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs;

namespace SyncApp26.API.Services;

public class SyncNotificationService : ISyncNotificationService
{
    private readonly IHubContext<SyncHub> _hubContext;

    public SyncNotificationService(IHubContext<SyncHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendProgress(string connectionId, string message, int percent)
    {
        if (string.IsNullOrEmpty(connectionId)) return;
        
        await _hubContext.Clients.Client(connectionId).SendAsync("UploadProgress", new 
        { 
            message, 
            percent 
        });
    }

    public async Task SendComparison(string connectionId, UserComparisonDTO comparison)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        await _hubContext.Clients.Client(connectionId).SendAsync("ComparisonResult", comparison);
    }

    public async Task SendSyncProgress(string connectionId, int processed, int failed, int skipped)
    {
        if (string.IsNullOrEmpty(connectionId)) return;

        await _hubContext.Clients.Client(connectionId).SendAsync("SyncProgress", new
        {
            processed,
            failed,
            skipped
        });
    }
}
