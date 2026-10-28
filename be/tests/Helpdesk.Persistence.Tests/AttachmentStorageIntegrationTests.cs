using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using be.Application.Features.Tickets;
using be.Domain.Entities;
using be.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio.DataModel.Args;
using Npgsql;
using Xunit;
using static Helpdesk.Persistence.Tests.AttachmentTestContext;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class AttachmentStorageIntegrationTests(PostgresFixture fixture)
{
    [MinioFact]
    public async Task HttpApiUsesRealMinioWithPrivateBucketAndBinaryDownloadContract()
    {
        await using var data = await TicketAccessTestContext.CreateAsync(fixture.ConnectionString);
        var settings = MinioStorageTests.Settings();
        using var storageAdmin = MinioStorageTests.Client(settings);
        await using var factory = data.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Minio:Enabled"] = "true", ["Minio:CleanupEnabled"] = "false", ["Minio:Endpoint"] = settings.Endpoint,
                ["Minio:AccessKey"] = settings.AccessKey, ["Minio:SecretKey"] = settings.SecretKey,
                ["Minio:Bucket"] = settings.Bucket, ["Minio:HostOverride"] = "", ["Minio:UseSsl"] = "false"
            })));
        var ticket = await data.TicketAsync(data.Accounts.AdminUser.Id);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = data.Accounts.Admin.DefaultRequestHeaders.Authorization;
        try
        {
            await factory.Services.GetRequiredService<MinioAttachmentStorage>().InitializeAsync(CancellationToken.None);
            var bytes = Encoding.UTF8.GetBytes("Real PostgreSQL + MinIO roundtrip");
            using var response = await SendFileAsync(client, ticket.Id, bytes: bytes);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var file = (await response.Content.ReadFromJsonAsync<TicketAttachmentDetails>())!;
            using var download = await client.GetAsync(Path(ticket.Id) + "/" + file.Id + "/download");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
            await using var database = data.Accounts.Database();
            var row = await database.TicketAttachments.SingleAsync(x => x.Id == file.Id);
            using var anonymous = new HttpClient();
            using var denied = await anonymous.GetAsync($"http://{settings.Endpoint}/{settings.Bucket}/{row.ObjectKey}");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
            var operation = swagger.GetProperty("paths").GetProperty("/api/v1/tickets/{ticketId}/attachments/{attachmentId}/download").GetProperty("get");
            Assert.Equal("binary", operation.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/octet-stream").GetProperty("schema").GetProperty("format").GetString());
        }
        finally
        {
            // Only this GUID-named test bucket is touched; there is no volume or production key.
            await foreach (var item in storageAdmin.ListObjectsEnumAsync(new ListObjectsArgs().WithBucket(settings.Bucket).WithRecursive(true)))
                await storageAdmin.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(settings.Bucket).WithObject(item.Key));
            await storageAdmin.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(settings.Bucket));
        }
    }

    [PostgresFact]
    public async Task MigrationRollbackRefusesToDiscardPendingObjectCleanup()
    {
        await using var data = await TicketAccessTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await data.TicketAsync(data.Accounts.AdminUser.Id);
        await using var database = data.Accounts.Database();
        database.AttachmentUploadIntents.Add(new AttachmentUploadIntent
        {
            TicketId = ticket.Id, UploadedByUserId = data.Accounts.AdminUser.Id, Bucket = "helpdesk-test", ObjectKey = "attachments/test",
            FileName = "report.txt", ContentType = "text/plain", SizeBytes = 1, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        await database.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.GetService<IMigrator>().MigrateAsync("20261026151829_TicketCategoryAdministration"));
        Assert.Equal("P0001", error.SqlState);
        Assert.EndsWith("_AttachmentStorage", (await database.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Single(await database.AttachmentUploadIntents.Where(x => x.TicketId == ticket.Id).ToListAsync());
    }
}
