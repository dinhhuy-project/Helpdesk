using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using be.Application.Common.Storage;
using be.Application.Features.Tickets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestUser = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestUser;

namespace Helpdesk.Persistence.Tests;

internal sealed class AttachmentTestContext(TicketAccessTestContext data, WebApplicationFactory<be.Program> factory, TestAttachmentStorage storage) : IAsyncDisposable
{
    internal TicketAccessTestContext Data { get; } = data;
    internal WebApplicationFactory<be.Program> Factory { get; } = factory;
    internal TestAttachmentStorage Storage { get; } = storage;
    internal static async Task<AttachmentTestContext> CreateAsync(string connection, Dictionary<string, string?>? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var data = await TicketAccessTestContext.CreateAsync(connection);
        var storage = new TestAttachmentStorage();
        var config = new Dictionary<string, string?>
        {
            ["Minio:Enabled"] = "true", ["Minio:CleanupEnabled"] = "false", ["Minio:Endpoint"] = "127.0.0.1:1",
            ["Minio:AccessKey"] = "test-only", ["Minio:SecretKey"] = "test-only", ["Minio:Bucket"] = storage.Bucket,
            ["Minio:UploadsPerMinute"] = "1000"
        };
        if (options is not null) foreach (var pair in options) config[pair.Key] = pair.Value;
        var factory = data.Accounts.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(config));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAttachmentStorage>();
                services.AddSingleton<IAttachmentStorage>(storage);
                configure?.Invoke(services);
            });
        });
        return new(data, factory, storage);
    }
    internal async Task<HttpClient> ClientAsync(TestUser user)
    {
        var login = await Data.Accounts.LoginAsync(user);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        return client;
    }
    internal static string Path(Guid ticketId) => $"/api/v1/tickets/{ticketId}/attachments";
    internal static async Task<HttpResponseMessage> SendFileAsync(HttpClient client, Guid ticketId, string name = "report.txt", string type = "text/plain",
        byte[]? bytes = null, Guid? commentId = null, bool? isInternal = null, string? extraField = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("Private file body"));
        file.Headers.ContentType = new(type);
        form.Add(file, "file", name);
        if (commentId is not null) form.Add(new StringContent(commentId.Value.ToString()), "commentId");
        if (isInternal is not null) form.Add(new StringContent(isInternal.Value ? "true" : "false"), "isInternal");
        if (extraField is not null) form.Add(new StringContent("forged-marker"), extraField);
        return await client.PostAsync(Path(ticketId), form);
    }
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Data.DisposeAsync();
    }
}

internal sealed class TestAttachmentStorage : IAttachmentStorage
{
    public string Bucket { get; } = "helpdesk-test-" + Guid.NewGuid().ToString("N");
    internal ConcurrentDictionary<string, byte[]> Objects { get; } = new();
    internal Func<Task>? AfterPut { get; set; }
    internal Func<Task>? BeforeCopy { get; set; }
    internal Func<Task>? BeforeDelete { get; set; }
    internal bool FailPut { get; set; }
    internal bool FailDelete { get; set; }
    internal int DownloadCalls { get; private set; }
    public async Task PutAsync(string bucket, string key, Stream source, long length, string contentType, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes, cancellationToken);
        Objects[key] = bytes.ToArray();
        if (AfterPut is not null) await AfterPut();
        if (FailPut) throw new TicketOperationException(TicketError.StorageUnavailable);
    }
    public async Task CopyToAsync(string bucket, string key, Stream destination, long expectedLength, CancellationToken cancellationToken)
    {
        DownloadCalls++;
        if (BeforeCopy is not null) await BeforeCopy();
        if (!Objects.TryGetValue(key, out var bytes)) throw new TicketOperationException(TicketError.NotFound);
        await destination.WriteAsync(bytes, cancellationToken);
    }
    public async Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (BeforeDelete is not null) await BeforeDelete();
        if (FailDelete) throw new TicketOperationException(TicketError.StorageUnavailable);
        Objects.TryRemove(key, out _);
    }
}
