using System.Collections.Concurrent;
using be.Application.Features.Accounts;

namespace be.Infrastructure.Realtime;

public sealed class RealtimeConnection(string id, AccountActor actor, DateTime expiresAt, Action abort)
{
    private readonly HashSet<Guid> _tickets = [];
    public string Id { get; } = id;
    public AccountActor Actor { get; } = actor;
    public DateTime ExpiresAt { get; } = expiresAt;
    public Action Abort { get; } = abort;
    public Guid[] Tickets { get { lock (_tickets) return _tickets.ToArray(); } }
    public bool Watch(Guid id) { lock (_tickets) { if (_tickets.Count >= 20 && !_tickets.Contains(id)) return false; _tickets.Add(id); return true; } }
    public void Unwatch(Guid id) { lock (_tickets) _tickets.Remove(id); }
    public bool IsWatching(Guid id) { lock (_tickets) return _tickets.Contains(id); }
}

public sealed class RealtimeConnections
{
    private readonly ConcurrentDictionary<string, RealtimeConnection> _connections = new();
    public RealtimeConnection[] Snapshot() => _connections.Values.ToArray();
    public void Add(RealtimeConnection connection)
    {
        // No public user-id/group selector. Identity is bound by the validated connection principal.
        _connections[connection.Id] = connection;
    }
    public RealtimeConnection? Find(string id) => _connections.GetValueOrDefault(id);
    public void Remove(string id) => _connections.TryRemove(id, out _);
    public static string TicketGroup(Guid id) => "ticket:" + id.ToString("N");
    public static string UserGroup(Guid id) => "user:" + id.ToString("N");
}
