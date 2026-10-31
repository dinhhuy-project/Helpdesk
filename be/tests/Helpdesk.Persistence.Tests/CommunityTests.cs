using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Community;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class CommunityTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task PublishedSearchExpressionCanUseThePartialGinIndex()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var selected = await PublishAsync(admin, "knowledge", await CreateAsync(admin, "knowledge"));
        await using var database = test.Database();
        // A tiny table legitimately favors its btree index. Use a selective keyword on a populated corpus.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO helpdesk.community_entries
                (id, kind, title, summary, content, topic, created_by_id, created_at, updated_at, is_published,
                 public_title, public_summary, public_content, public_topic, public_revision, published_at)
            SELECT gen_random_uuid(), 'Knowledge', 'Unrelated draft', 'Unrelated draft', 'Unrelated draft', 'Devices',
                created_by_id, created_at, updated_at, true, 'Printer instructions', 'Paper feed', 'Check paper tray', 'Devices', 1, published_at
            FROM helpdesk.community_entries CROSS JOIN generate_series(1, 1500) WHERE id = {selected.Id}
            """);
        await database.Database.ExecuteSqlRawAsync("ANALYZE helpdesk.community_entries");
        await using var tx = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        var query = database.CommunityEntries.Where(x => x.IsPublished && x.Kind == CommunityKind.Knowledge &&
            EF.Functions.ToTsVector("simple", (x.PublicTitle ?? "") + " " + (x.PublicSummary ?? "") + " " + (x.PublicContent ?? "") + " " + (x.PublicTopic ?? ""))
                .Matches(EF.Functions.PlainToTsQuery("simple", "VPN"))).Select(x => x.Id);
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction(); command.CommandText = "EXPLAIN (COSTS OFF) " + query.ToQueryString();
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>(); while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
        Assert.True(plan.Any(line => line.Contains("ix_community_entries_public_title", StringComparison.Ordinal)), string.Join('\n', plan) + "\n" + query.ToQueryString());
    }
    [PostgresFact]
    public async Task AnonymousReadsOnlyPublishedProjectionAndCannotWriteOrDiscoverDrafts()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var draft = await CreateAsync(admin, "knowledge");
        using var hidden = await guest.GetAsync($"/api/v1/public/knowledge/{draft.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(0, (await guest.GetFromJsonAsync<be.Application.Features.Accounts.PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge"))!.TotalCount);
        foreach (var path in new[] { $"/api/v1/community/knowledge/{draft.Id}", $"/api/v1/community/knowledge/{draft.Id}/participation" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(path)).StatusCode);
        using var rejected = await guest.PostAsJsonAsync($"/api/v1/community/knowledge/{draft.Id}/publish", new PublishCommunityEntry(draft.Version, true));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var unreviewed = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{draft.Id}/publish", new PublishCommunityEntry(draft.Version, false));
        Assert.Equal(HttpStatusCode.BadRequest, unreviewed.StatusCode);
        var published = await PublishAsync(admin, "knowledge", draft);
        using var response = await guest.GetAsync($"/api/v1/public/knowledge/{draft.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Published VPN guide", json.GetProperty("title").GetString());
        foreach (var key in new[] { "sourceTicketId", "createdById", "userId", "version", "publicContent", "passwordHash" }) Assert.False(json.TryGetProperty(key, out _));
        Assert.Equal(1, published.PublicRevision);
        guest.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-expired-token");
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/v1/public/knowledge/{draft.Id}")).StatusCode);
    }

    [PostgresFact]
    public async Task SearchUsesPublishedTextAndDraftEditsConflictWithoutRepublishing()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var guest = test.Factory.CreateClient();
        var published = await PublishAsync(admin, "knowledge", await CreateAsync(admin, "knowledge"));
        using var save = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{published.Id}", new SaveCommunityEntry("secret-draft-marker", "private-summary", "private-body-marker", "Draft topic", Version: published.Version));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var edited = (await save.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        using var conflict = await admin.PutAsJsonAsync($"/api/v1/community/knowledge/{published.Id}", new SaveCommunityEntry("Stale", "Stale", "Stale", "Stale", Version: published.Version));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var visible = (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{published.Id}"))!;
        Assert.Equal("Published VPN guide", visible.Title);
        Assert.Equal(0, (await guest.GetFromJsonAsync<be.Application.Features.Accounts.PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge?search=secret-draft-marker"))!.TotalCount);
        Assert.Equal(1, (await guest.GetFromJsonAsync<be.Application.Features.Accounts.PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge?search=VPN"))!.TotalCount);
        using var withdrawal = await admin.PostAsJsonAsync($"/api/v1/community/knowledge/{edited.Id}/withdraw", new { edited.Version });
        Assert.Equal(HttpStatusCode.OK, withdrawal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{edited.Id}")).StatusCode);
        Assert.Equal(0, (await guest.GetFromJsonAsync<be.Application.Features.Accounts.PagedResult<PublicCommunityEntry>>("/api/v1/public/knowledge?search=VPN"))!.TotalCount);
    }

    [PostgresFact]
    public async Task EditorAndPublisherAreSeparateAndGrantRevocationAppliesToExistingJwt()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var editor = await test.UserAsync(PermissionCodes.KnowledgeManage);
        using var publisher = await test.UserAsync(PermissionCodes.KnowledgePublish);
        var draft = await CreateAsync(editor, "knowledge");
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync($"/api/v1/community/knowledge/{draft.Id}/publish", new PublishCommunityEntry(draft.Version, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await publisher.PostAsJsonAsync("/api/v1/community/knowledge", new SaveCommunityEntry("x", "x", "x", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publisher.GetAsync($"/api/v1/community/knowledge/{draft.Id}")).StatusCode);
        var row = await PublishAsync(publisher, "knowledge", draft);
        await using var database = test.Database();
        var permission = await database.Permissions.SingleAsync(x => x.Code == PermissionCodes.KnowledgePublish);
        database.RolePermissions.RemoveRange(await database.RolePermissions.Where(x => x.PermissionId == permission.Id).ToListAsync());
        await database.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await publisher.PostAsJsonAsync($"/api/v1/community/knowledge/{row.Id}/withdraw", new { row.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync("/api/v1/community/incidents", new SaveCommunityEntry("x", "x", "x", "x", "Investigating"))).StatusCode);
    }

    [PostgresFact]
    public async Task ResolvedOrClosedTicketBecomesPrivateDraftWithoutCopyingDescriptionCommentsOrFiles()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var editor = await test.UserAsync(PermissionCodes.KnowledgeManage, PermissionCodes.TicketReadOwn);
        var actor = TicketAccessTestContext.Actor(editor);
        var ticket = await test.TicketAsync(actor.UserId, TicketStatus.Closed);
        await using (var database = test.Database())
            await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, new string('A', 300)));
        using var response = await editor.PostAsJsonAsync($"/api/v1/community/knowledge/from-ticket/{ticket.Id}", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        Assert.Equal(200, row.Title.Length);
        Assert.Equal(ticket.ResolutionSummary, row.Content); Assert.False(row.IsPublished); Assert.Equal(ticket.Id, row.SourceTicketId);
        Assert.DoesNotContain("private-description", await response.Content.ReadAsStringAsync());
        var hidden = await test.TicketAsync(test.AdminId, TicketStatus.Resolved);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.PostAsJsonAsync($"/api/v1/community/knowledge/from-ticket/{hidden.Id}", new { })).StatusCode);
        var open = await test.TicketAsync(actor.UserId, TicketStatus.New);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/v1/community/knowledge/from-ticket/{open.Id}", new { })).StatusCode);
        using var guest = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/knowledge/{row.Id}")).StatusCode);
    }

    [PostgresFact]
    public async Task IncidentProgressAndAffectedMembershipAreValidatedIdempotentAndPrivate()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var user = await test.UserAsync(); using var guest = test.Factory.CreateClient();
        var row = await PublishAsync(admin, "incidents", await CreateAsync(admin, "incidents"));
        var path = $"/api/v1/community/incidents/{row.Id}/participation";
        for (var repeat = 0; repeat < 2; repeat++) Assert.Equal(HttpStatusCode.OK, (await user.PutAsJsonAsync(path, new { value = true })).StatusCode);
        var visible = (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/incidents/{row.Id}"))!;
        Assert.Equal(1, visible.AffectedCount);
        using var save = await admin.PutAsJsonAsync($"/api/v1/community/incidents/{row.Id}", new SaveCommunityEntry(row.Title, row.Summary, row.Content, row.Topic, "Resolved", row.Version));
        var edited = (await save.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
        Assert.Equal("Investigating", (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/incidents/{row.Id}"))!.Status);
        row = await PublishAsync(admin, "incidents", edited);
        var timeline = (await guest.GetFromJsonAsync<be.Application.Features.Accounts.PagedResult<PublicIncidentUpdate>>($"/api/v1/public/incidents/{row.Id}/updates"))!;
        Assert.Equal(new[] { "Resolved", "Investigating" }, timeline.Items.Select(x => x.Status));
        using var other = await test.UserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync(path, new { value = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PutAsJsonAsync(path, new { value = false })).StatusCode);
        Assert.Equal(0, (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/incidents/{row.Id}"))!.AffectedCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsJsonAsync(path, new { value = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/community/incidents/{row.Id}/withdraw", new { row.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/incidents/{row.Id}/updates")).StatusCode);
    }

    [PostgresFact]
    public async Task IncidentCannotPublishInvalidInitialOrBackwardStatus()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        var draft = await CreateAsync(admin, "incidents", "Resolved");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/community/incidents/{draft.Id}/publish", new PublishCommunityEntry(draft.Version, true))).StatusCode);
        using var bad = await admin.PostAsJsonAsync("/api/v1/community/incidents", new SaveCommunityEntry("x", "x", "x", "x", "MadeUp"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [PostgresFact]
    public async Task HelpfulAndSelfResolvedFeedbackCountsUniqueAccountsAndRejectsImpossibleStates()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync(); using var user = await test.UserAsync(); using var guest = test.Factory.CreateClient();
        var row = await PublishAsync(admin, "knowledge", await CreateAsync(admin, "knowledge"));
        var path = $"/api/v1/community/knowledge/{row.Id}/participation";
        for (var repeat = 0; repeat < 2; repeat++) Assert.Equal(HttpStatusCode.OK, (await user.PutAsJsonAsync(path, new { value = true, solved = true })).StatusCode);
        var visible = (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{row.Id}"))!;
        Assert.Equal(1, visible.HelpfulCount); Assert.Equal(1, visible.SolvedCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsJsonAsync(path, new { value = false, solved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PutAsJsonAsync(path, new { value = false, solved = false })).StatusCode);
        Assert.Equal(0, (await guest.GetFromJsonAsync<PublicCommunityEntry>($"/api/v1/public/knowledge/{row.Id}"))!.SolvedCount);
        var actor = TicketAccessTestContext.Actor(user);
        await using var database = test.Database();
        await database.Users.Where(x => x.Id == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.PutAsJsonAsync(path, new { value = true, solved = true })).StatusCode);
    }

    [PostgresFact]
    public async Task AnonymousSignalRReceivesOnlyCommittedPublicInvalidationsAndCannotJoinPrivateGroups()
    {
        await using var test = await CommunityTestDatabase.CreateAsync(fixture.ConnectionString);
        using var admin = await test.AdminAsync();
        await using var connection = new HubConnectionBuilder().WithUrl(new Uri(test.Factory.Server.BaseAddress, "/hubs/community"), options =>
        { options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => test.Factory.Server.CreateHandler(); }).Build();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("CommunityChanged", payload => received.TrySetResult(payload));
        await connection.StartAsync();
        var draft = await CreateAsync(admin, "knowledge");
        Assert.False(received.Task.IsCompleted);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => connection.InvokeAsync("WatchTicket", Guid.NewGuid()));
        var row = await PublishAsync(admin, "knowledge", draft);
        var changed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(row.Id, changed.GetProperty("id").GetGuid());
        Assert.Equal(new[] { "id", "kind" }, changed.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        using var guest = test.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/v1/public/knowledge/{row.Id}")).StatusCode);
        await connection.StopAsync();
    }

    internal static async Task<ManagedCommunityEntry> CreateAsync(HttpClient client, string kind, string status = "Investigating")
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/community/{kind}", new SaveCommunityEntry("Published VPN guide", "Safe public summary", "Step 1: check VPN settings. Step 2: reconnect.", "Network", kind == "incidents" ? status : null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
    }
    internal static async Task<ManagedCommunityEntry> PublishAsync(HttpClient client, string kind, ManagedCommunityEntry row)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/community/{kind}/{row.Id}/publish", new PublishCommunityEntry(row.Version, true, "Public progress update"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ManagedCommunityEntry>())!;
    }
}

internal sealed class CommunityTestDatabase(string administration, string name, string connectionString) : IAsyncDisposable
{
    private readonly string password = Guid.NewGuid().ToString("N") + "Aa!";
    public string ConnectionString => connectionString;
    public Guid AdminId { get; private set; }
    internal object AdminCredentials => new { email = "community-admin@example.test", password };
    public WebApplicationFactory<be.Program> Factory { get; private set; } = null!;
    public HelpdeskDbContext Database() => new(new DbContextOptionsBuilder<HelpdeskDbContext>().UseNpgsql(connectionString,
        postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk")).Options);
    public static async Task<CommunityTestDatabase> CreateAsync(string administration)
    {
        var name = "helpdesk_community_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(administration); await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)) await command.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(administration) { Database = name, Pooling = false };
        var result = new CommunityTestDatabase(administration, name, settings.ConnectionString);
        try
        {
            await using var database = result.Database(); await database.Database.MigrateAsync();
            var identity = new IdentityInitializer(database, new PasswordHasher<User>()); await identity.SeedAsync();
            await identity.BootstrapAdminAsync(new BootstrapAdminOptions { Email = "community-admin@example.test", DisplayName = "Community editor", Password = result.password });
            result.AdminId = await database.Users.Select(x => x.Id).SingleAsync();
            result.Factory = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Helpdesk"] = settings.ConnectionString, ["Database:HostOverride"] = "", ["Identity:SeedOnStartup"] = "false", ["AuthRateLimit:PermitLimit"] = "100" })));
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }
    public Task<HttpClient> AdminAsync() => LoginAsync("community-admin@example.test");
    public async Task<HttpClient> UserAsync(params string[] codes)
    {
        await using var database = Database();
        var user = new User { Email = Guid.NewGuid().ToString("N") + "@example.test", DisplayName = "Community participant" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password); database.Users.Add(user);
        if (codes.Length > 0)
        {
            var role = new Role { Name = Guid.NewGuid().ToString("N") }; database.Roles.Add(role);
            database.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            var permissions = await database.Permissions.Where(x => codes.Contains(x.Code)).ToListAsync();
            database.RolePermissions.AddRange(permissions.Select(x => new RolePermission { RoleId = role.Id, PermissionId = x.Id }));
        }
        await database.SaveChangesAsync();
        return await LoginAsync(user.Email);
    }
    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<be.Contracts.Responses.LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return client;
    }
    public async Task<Ticket> TicketAsync(Guid requester, TicketStatus status)
    {
        await using var database = Database();
        var category = new TicketCategory { Name = Guid.NewGuid().ToString("N") }; database.TicketCategories.Add(category);
        var ticket = new Ticket { Title = "Source VPN issue", Description = "private-description-must-never-be-copied", CategoryId = category.Id, RequesterId = requester,
            CreatedByUserId = requester, Status = status, ResolutionSummary = status is TicketStatus.Resolved or TicketStatus.Closed ? "Reviewed solution text" : null,
            ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed ? DateTime.UtcNow : null, ClosedAt = status == TicketStatus.Closed ? DateTime.UtcNow : null };
        database.Tickets.Add(ticket); await database.SaveChangesAsync(); return ticket;
    }
    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (!name.StartsWith("helpdesk_community_test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database cleanup.");
        await using var connection = new NpgsqlConnection(administration); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
    }
}
