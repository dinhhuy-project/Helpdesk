using Microsoft.AspNetCore.SignalR;

namespace be.Infrastructure.Realtime;

public interface IRealtimeSender
{
    Task SendAsync(string connectionId, string eventName, Guid resourceId, CancellationToken cancellationToken);
}
public sealed class RealtimeSender(IHubContext<NotificationsHub> hub) : IRealtimeSender
{
    public Task SendAsync(string connectionId, string eventName, Guid resourceId, CancellationToken cancellationToken) =>
        hub.Clients.Client(connectionId).SendAsync(eventName, new { id = resourceId }, cancellationToken);
}
