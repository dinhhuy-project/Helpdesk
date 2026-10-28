using System.Globalization;
using be.Api.Authorization;
using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Infrastructure.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Realtime;

[Authorize]
public sealed class NotificationsHub(RealtimeConnections connections, IAuthSessionStore sessions, TicketScope scope, TimeProvider clock) : Hub
{
    public const string Path = "/hubs/notifications";
    public override async Task OnConnectedAsync()
    {
        var actor = CurrentAccountActor.From(Context.User!);
        if (!long.TryParse(Context.User?.FindFirst("exp")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires))
        { Context.Abort(); throw new HubException("Unauthenticated"); }
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime;
        if (expiresAt <= clock.GetUtcNow().UtcDateTime || !await sessions.IsActiveAsync(actor.UserId, actor.SessionId, Context.ConnectionAborted))
        { Context.Abort(); throw new HubException("Unauthenticated"); }
        connections.Add(new(Context.ConnectionId, actor, expiresAt, Context.Abort));
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeConnections.UserGroup(actor.UserId), Context.ConnectionAborted);
            // Offline/reconnected clients reload durable state; this event contains no resource data.
            await Clients.Caller.SendAsync("NotificationsChanged", Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        catch { connections.Remove(Context.ConnectionId); throw; }
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
    public async Task WatchTicket(Guid ticketId)
    {
        var connection = await RequireLiveAsync();
        if (ticketId == Guid.Empty || !await scope.For(connection.Actor, TicketOperation.Read).AnyAsync(x => x.Id == ticketId, Context.ConnectionAborted))
            throw new HubException("NotFound");
        if (!connection.Watch(ticketId)) throw new HubException("WatchLimitExceeded");
        try { await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeConnections.TicketGroup(ticketId), Context.ConnectionAborted); }
        catch { connection.Unwatch(ticketId); throw; }
    }
    public async Task UnwatchTicket(Guid ticketId)
    {
        var connection = await RequireLiveAsync();
        connection.Unwatch(ticketId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeConnections.TicketGroup(ticketId), Context.ConnectionAborted);
    }
    public async Task Ping() { await RequireLiveAsync(); }
    private async Task<RealtimeConnection> RequireLiveAsync()
    {
        var connection = connections.Find(Context.ConnectionId);
        if (connection is null || connection.ExpiresAt <= clock.GetUtcNow().UtcDateTime ||
            !await sessions.IsActiveAsync(connection.Actor.UserId, connection.Actor.SessionId, Context.ConnectionAborted))
        { Context.Abort(); throw new HubException("Unauthenticated"); }
        return connection;
    }
}
