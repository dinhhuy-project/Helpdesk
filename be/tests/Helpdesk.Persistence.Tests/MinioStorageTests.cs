using System.Net;
using System.Text;
using be.Configuration;
using be.Infrastructure.Storage;
using be.Application.Features.Tickets;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public sealed class MinioFactAttribute : FactAttribute
{
    public MinioFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPDESK_TEST_MINIO_ENDPOINT")))
            Skip = "Run Run-PersistenceTests.ps1 to test against isolated MinIO.";
    }
}

[Collection("Postgres")]
public class MinioStorageTests
{
    internal static MinioOptions Settings()
    {
        var endpoint = Environment.GetEnvironmentVariable("HELPDESK_TEST_MINIO_ENDPOINT") ?? throw new InvalidOperationException("Missing isolated MinIO endpoint.");
        if (!Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("MinIO tests require an explicit loopback test endpoint.");
        return new MinioOptions { Enabled = true, CleanupEnabled = false, Endpoint = endpoint,
            AccessKey = Environment.GetEnvironmentVariable("HELPDESK_TEST_MINIO_ACCESS_KEY") ?? "",
            SecretKey = Environment.GetEnvironmentVariable("HELPDESK_TEST_MINIO_SECRET_KEY") ?? "",
            Bucket = "helpdesk-test-" + Guid.NewGuid().ToString("N") };
    }
    internal static IMinioClient Client(MinioOptions options) => new MinioClient().WithEndpoint(options.Endpoint)
        .WithCredentials(options.AccessKey, options.SecretKey).Build();

    [MinioFact]
    public async Task RealMinioRoundTripPrivateBucketAndIdempotentDelete()
    {
        var options = Settings();
        using var client = Client(options);
        using var storage = new MinioAttachmentStorage(Options.Create(options));
        var key = "attachments/" + Guid.NewGuid().ToString("N");
        try
        {
            await storage.InitializeAsync(CancellationToken.None);
            var bytes = Encoding.UTF8.GetBytes("Private UTF8 text Tiếng Việt");
            await storage.PutAsync(options.Bucket, key, new MemoryStream(bytes), bytes.Length, "text/plain", CancellationToken.None);
            using var anonymous = new HttpClient();
            using var response = await anonymous.GetAsync($"http://{options.Endpoint}/{options.Bucket}/{key}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var output = new MemoryStream();
            await storage.CopyToAsync(options.Bucket, key, output, bytes.Length, CancellationToken.None);
            Assert.Equal(bytes, output.ToArray());
            await storage.DeleteAsync(options.Bucket, key, CancellationToken.None);
            await storage.DeleteAsync(options.Bucket, key, CancellationToken.None);
        }
        finally
        {
            await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(options.Bucket).WithObject(key));
            await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(options.Bucket));
        }
    }

    [MinioFact]
    public async Task PublicBucketIsRefusedWithoutChangingItsPolicy()
    {
        var options = Settings();
        using var client = Client(options);
        using var storage = new MinioAttachmentStorage(Options.Create(options));
        try
        {
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(options.Bucket));
            var policy = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::" + options.Bucket + "/*\"]}]}";
            await client.SetPolicyAsync(new SetPolicyArgs().WithBucket(options.Bucket).WithPolicy(policy));
            var error = await Assert.ThrowsAsync<TicketOperationException>(() => storage.InitializeAsync(CancellationToken.None));
            Assert.Equal(TicketError.StorageUnavailable, error.Error);
            Assert.Contains("s3:GetObject", await client.GetPolicyAsync(new GetPolicyArgs().WithBucket(options.Bucket)));
        }
        finally { await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(options.Bucket)); }
    }
}
