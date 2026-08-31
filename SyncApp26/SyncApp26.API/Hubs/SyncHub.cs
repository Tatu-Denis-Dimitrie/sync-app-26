using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SyncApp26.API.Hubs;

[Authorize]
public class SyncHub : Hub
{
    // Role groups let broadcasts target e.g. Admin without reaching every connected BasicUser.
    public override async Task OnConnectedAsync()
    {
        var roles = Context.User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
        foreach (var role in roles)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinGroup(string transferId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, transferId);
    }

    public async Task LeaveGroup(string transferId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, transferId);
    }
}
