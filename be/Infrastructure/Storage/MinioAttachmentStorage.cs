using System.Text.Json;
using be.Application.Common.Storage;
using be.Application.Features.Tickets;
using be.Configuration;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace be.Infrastructure.Storage;

public sealed class MinioAttachmentStorage(IOptions<MinioOptions> settings) : IAttachmentStorage, IDisposable
{
    private IMinioClient? _client;
    private readonly object _clientLock = new();
    public string Bucket => settings.Value.Bucket;
    private IMinioClient Client
    {
        get
        {
            if (!settings.Value.Enabled) throw new TicketOperationException(TicketError.StorageUnavailable);
            lock (_clientLock)
            {
                if (_client is not null) return _client;
                var endpoint = new UriBuilder((settings.Value.UseSsl ? "https://" : "http://") + settings.Value.Endpoint);
                if (!string.IsNullOrWhiteSpace(settings.Value.HostOverride)) endpoint.Host = settings.Value.HostOverride;
                _client = new MinioClient().WithEndpoint(endpoint.Host, endpoint.Port).WithCredentials(settings.Value.AccessKey, settings.Value.SecretKey)
                    .WithSSL(settings.Value.UseSsl).WithTimeout(120_000).Build();
                return _client;
            }
        }
    }
    public Task InitializeAsync(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        if (!await Client.BucketExistsAsync(new BucketExistsArgs().WithBucket(Bucket), cancellationToken))
            await Client.MakeBucketAsync(new MakeBucketArgs().WithBucket(Bucket), cancellationToken);
        await CheckPrivateAsync(Bucket, cancellationToken);
    }, cancellationToken);
    public Task PutAsync(string bucket, string key, Stream source, long length, string contentType, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await CheckPrivateAsync(bucket, cancellationToken);
            await Client.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(key).WithStreamData(source)
                .WithObjectSize(length).WithContentType(contentType), cancellationToken);
        }, cancellationToken);
    public Task CopyToAsync(string bucket, string key, Stream destination, long expectedLength, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await CheckPrivateAsync(bucket, cancellationToken);
            if (expectedLength is <= 0 or > 10 * 1024 * 1024) throw new TicketOperationException(TicketError.InvalidFile);
            try
            {
                await Client.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key).WithCallbackStream(
                    (source, token) => AttachmentBuffers.CopyBoundedAsync(source, destination, expectedLength, token)), cancellationToken);
            }
            catch (TicketOperationException error) when (error.Error == TicketError.InvalidFile)
            { throw new TicketOperationException(TicketError.StorageUnavailable); }
        }, cancellationToken);
    public Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        await CheckPrivateAsync(bucket, cancellationToken);
        await Client.RemoveIncompleteUploadAsync(new RemoveIncompleteUploadArgs().WithBucket(bucket).WithObject(key), cancellationToken);
        await Client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(key), cancellationToken);
    }, cancellationToken);
    private async Task CheckPrivateAsync(string bucket, CancellationToken cancellationToken)
    {
        if (bucket != Bucket) throw new TicketOperationException(TicketError.StorageUnavailable);
        string? policy;
        try { policy = await Client.GetPolicyAsync(new GetPolicyArgs().WithBucket(bucket), cancellationToken); }
        catch (MinioException error) when (error.Response?.Code == "NoSuchBucketPolicy") { policy = null; }
        if (!string.IsNullOrWhiteSpace(policy))
        {
            using var json = JsonDocument.Parse(policy);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                json.RootElement.EnumerateObject().Any(property => property.Name is not ("Version" or "Statement")) ||
                (json.RootElement.TryGetProperty("Statement", out var statements) &&
                 (statements.ValueKind != JsonValueKind.Array || statements.GetArrayLength() > 0)))
                throw new TicketOperationException(TicketError.StorageUnavailable);
        }
        var versioning = await Client.GetVersioningAsync(new GetVersioningArgs().WithBucket(bucket), cancellationToken);
        if (versioning.Status is "Enabled" or "Suspended") throw new TicketOperationException(TicketError.StorageUnavailable);
    }
    private static async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try { await action(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TicketOperationException) { throw; }
        catch (ObjectNotFoundException) { throw new TicketOperationException(TicketError.NotFound); }
        catch (Exception error) when (error is MinioException or HttpRequestException or IOException or JsonException or OperationCanceledException)
        { throw new TicketOperationException(TicketError.StorageUnavailable); }
    }
    public void Dispose() => _client?.Dispose();
}
