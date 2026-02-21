using Microsoft.AspNetCore.SignalR;

namespace SyncApp26.API.Hubs;

public class SyncHub : Hub
{
    public async Task JoinGroup(string transferId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, transferId);
    }

    public async Task LeaveGroup(string transferId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, transferId);
    }
}
