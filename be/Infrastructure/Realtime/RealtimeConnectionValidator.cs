using be.Application.Features.Auth;
using be.Application.Features.Tickets;
using be.Infrastructure.Tickets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Realtime;

public sealed class RealtimeConnectionValidator(RealtimeConnections connections, IAuthSessionStore sessions,
    TicketScope scope, IHubContext<NotificationsHub> hub, TimeProvider clock)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var connection in connections.Snapshot())
        {
            if (connection.ExpiresAt <= clock.GetUtcNow().UtcDateTime ||
                !await sessions.IsActiveAsync(connection.Actor.UserId, connection.Actor.SessionId, cancellationToken))
            { connections.Remove(connection.Id); connection.Abort(); continue; }
            foreach (var ticket in connection.Tickets)
                if (!await scope.For(connection.Actor, TicketOperation.Read).AnyAsync(x => x.Id == ticket, cancellationToken))
                {
                    connection.Unwatch(ticket);
                    await hub.Groups.RemoveFromGroupAsync(connection.Id, RealtimeConnections.TicketGroup(ticket), cancellationToken);
                }
        }
    }
}
