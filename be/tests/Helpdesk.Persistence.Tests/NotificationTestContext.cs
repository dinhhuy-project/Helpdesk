using System.Collections.Concurrent;
using System.Net.Http.Json;
using be.Infrastructure.Notifications;
using be.Infrastructure.Realtime;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestUser = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestUser;

namespace Helpdesk.Persistence.Tests;

internal sealed class NotificationTestContext(TicketAccessTestContext data, WebApplicationFactory<be.Program> factory, CaptureRealtimeSender sender) : IAsyncDisposable
{
    public TicketAccessTestContext Data { get; } = data;
    public WebApplicationFactory<be.Program> Factory { get; } = factory;
    public CaptureRealtimeSender Sender { get; } = sender;
    public RealtimeConnections Connections => Factory.Services.GetRequiredService<RealtimeConnections>();
    public static async Task<NotificationTestContext> CreateAsync(string connection, bool realSender = false, Action<IServiceCollection>? configure = null)
    {
        var data = await TicketAccessTestContext.CreateAsync(connection);
        var sender = new CaptureRealtimeSender();
        var factory = data.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (!realSender) services.Replace(ServiceDescriptor.Singleton<IRealtimeSender>(sender));
            configure?.Invoke(services);
        }));
        return new(data, factory, sender);
    }
    public async Task<HttpClient> ClientAsync(TestUser user)
    {
        var login = await Data.Accounts.LoginAsync(user);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        return client;
    }
    public async Task<int> DispatchAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(CancellationToken.None);
    }
    public async ValueTask DisposeAsync() { await Factory.DisposeAsync(); await Data.DisposeAsync(); }
    public static Task<HttpResponseMessage> CommentAsync(HttpClient client, Guid ticket, bool isInternal = false) =>
        client.PostAsJsonAsync($"/api/v1/tickets/{ticket}/comments", new { content = "private-comment-marker", isInternal });
}

internal sealed class CaptureRealtimeSender : IRealtimeSender
{
    public ConcurrentQueue<(string Connection, string Event, Guid Id)> Sent { get; } = new();
    public bool Fail { get; set; }
    public Func<Task>? BeforeSend { get; set; }
    public async Task SendAsync(string connectionId, string eventName, Guid resourceId, CancellationToken cancellationToken)
    {
        if (BeforeSend is not null) await BeforeSend();
        if (Fail) throw new IOException("Injected SignalR outage.");
        Sent.Enqueue((connectionId, eventName, resourceId));
    }
}
