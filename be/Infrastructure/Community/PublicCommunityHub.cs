using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace be.Infrastructure.Community;

// Read-only anonymous broadcast. No client-controlled group names and no private session data.
[AllowAnonymous]
public sealed class PublicCommunityHub : Hub
{
    public const string Path = "/hubs/community";
    public const string Group = "published-community";
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await base.OnConnectedAsync();
    }
}
