using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketCreationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task CreateUsesSessionIdentityDatabaseNumberAndAtomicWhitelistedActivity()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.Requester);
        using var client = await test.Accounts.ClientAsync(user);
        client.DefaultRequestHeaders.Add("X-User-Id", test.Accounts.AdminUser.Id.ToString());
        var title = test.Prefix + "Title";
        using var response = await client.PostAsJsonAsync("/api/v1/tickets", new { Title = "  " + title + "  ", Description = "  private-description-marker  ", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        Assert.True(receipt.TicketNumber > 0 && receipt.Version > 0);
        Assert.Equal(user.Id, receipt.RequesterId);
        Assert.Equal(user.Id, receipt.CreatedByUserId);
        Assert.Equal(TicketStatus.New, receipt.Status);
        Assert.Equal(TicketPriority.Normal, receipt.Priority);
        Assert.EndsWith($"/api/v1/tickets/{receipt.Id}", response.Headers.Location?.ToString());
        var ticket = await test.StoredAsync(receipt.Id);
        Assert.Equal(title, ticket.Title);
        Assert.Equal("private-description-marker", ticket.Description);
        Assert.Null(ticket.AssignedTeamId);
        Assert.Null(ticket.AssignedAgentId);
        Assert.Null(ticket.ResolutionSummary);
        Assert.Null(ticket.UpdatedAt);
        await using var database = test.Accounts.Database();
        var activity = await database.TicketActivities.SingleAsync(x => x.TicketId == receipt.Id);
        Assert.Equal("TicketCreated", activity.Action);
        Assert.Equal(user.Id, activity.ActorId);
        Assert.Equal(ticket.CreatedAt, activity.CreatedAt);
        Assert.Null(activity.OldValue);
        var data = JsonDocument.Parse(activity.NewValue!).RootElement;
        Assert.Equal(receipt.TicketNumber, data.GetProperty("TicketNumber").GetInt64());
        Assert.Equal(user.Id, data.GetProperty("RequesterId").GetGuid());
        Assert.Equal(user.Id, data.GetProperty("CreatedByUserId").GetGuid());
        Assert.Equal(test.Category, data.GetProperty("CategoryId").GetGuid());
        Assert.Equal("New", data.GetProperty("Status").GetString());
        Assert.Equal("Normal", data.GetProperty("Priority").GetString());
        Assert.DoesNotContain("private-description-marker", activity.NewValue);
        Assert.DoesNotContain(title, activity.NewValue);
        Assert.False(await database.TicketSlas.AnyAsync(x => x.TicketId == receipt.Id));
        using var detail = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(receipt.Id, (await detail.Content.ReadFromJsonAsync<TicketDetails>())!.Id);
        var list = await client.GetFromJsonAsync<PagedResult<TicketSummary>>($"/api/v1/tickets?ticketNumber={receipt.TicketNumber}");
        Assert.Equal(receipt.Id, Assert.Single(list!.Items).Id);
        var flags = await client.GetFromJsonAsync<TicketAccess>($"/api/v1/tickets/{receipt.Id}/access");
        Assert.Equal(new TicketAccess(true, false, false), flags);
    }

    [PostgresTheory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("inactive-ancestor")]
    [InlineData("cycle")]
    public async Task CreationRequiresCategoryAndAllAncestorsToBeAvailable(string failure)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        await using var database = test.Accounts.Database();
        var selected = test.Category;
        if (failure == "missing") selected = Guid.NewGuid();
        else if (failure == "inactive") await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else
        {
            var child = new TicketCategory { Name = test.Prefix + "Child", ParentId = test.Category };
            database.TicketCategories.Add(child);
            await database.SaveChangesAsync();
            test.OwnCategory(child.Id);
            selected = child.Id;
            if (failure == "inactive-ancestor") await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            else await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, child.Id));
        }
        try
        {
            using var response = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "Description", CategoryId = selected });
            await ErrorAsync(response, HttpStatusCode.BadRequest, "InvalidCategory");
            Assert.False(await database.Tickets.AnyAsync(x => x.Title == test.Prefix));
        }
        finally
        {
            if (failure == "cycle") await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, (Guid?)null));
        }
    }

    [PostgresFact]
    public async Task ActiveChildCanBeCreatedIntoAndLookupCannotAuthorizeAfterAncestorDeactivation()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.Requester);
        using var client = await test.Accounts.ClientAsync(user);
        await using var database = test.Accounts.Database();
        var child = new TicketCategory { Name = test.Prefix + "Child", ParentId = test.Category };
        database.TicketCategories.Add(child);
        await database.SaveChangesAsync();
        test.OwnCategory(child.Id);
        using var created = await client.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix + "Before", Description = "Description", CategoryId = child.Id, Priority = TicketPriority.High });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        Assert.Equal(TicketPriority.High, receipt.Priority);
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var request = client.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix + "After", Description = "Description", CategoryId = child.Id });
        await transaction.CommitAsync();
        using var denied = await request;
        await ErrorAsync(denied, HttpStatusCode.BadRequest, "InvalidCategory");
        using var history = await client.GetAsync($"/api/v1/tickets/{receipt.Id}");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.False(await database.Tickets.AnyAsync(x => x.Title == test.Prefix + "After"));
    }

    [PostgresTheory]
    [InlineData("grant")]
    [InlineData("session")]
    [InlineData("inactive")]
    public async Task ServiceRechecksCallerInsideTransaction(string change)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.CustomUserAsync(PermissionCodes.TicketCreate);
        using var client = await test.Accounts.ClientAsync(user);
        var actor = Actor(client);
        await using var database = test.Accounts.Database();
        if (change == "grant") await database.UserRoles.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
        else if (change == "session") await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TicketCreateService>();
        var error = await Assert.ThrowsAsync<TicketOperationException>(() => service.CreateAsync(actor, new(test.Prefix, "Description", test.Category), CancellationToken.None));
        Assert.Equal(change == "grant" ? TicketError.Forbidden : TicketError.Unauthenticated, error.Error);
        Assert.False(await database.Tickets.AnyAsync(x => x.Title == test.Prefix));
    }

    [PostgresFact]
    public async Task CreationPermissionDoesNotImplicitlyGrantReadAndReadPermissionDoesNotGrantCreation()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var creator = await test.CustomUserAsync(PermissionCodes.TicketCreate);
        using var client = await test.Accounts.ClientAsync(creator);
        using var create = await client.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "Description", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var receipt = (await create.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        using var read = await client.GetAsync($"/api/v1/tickets/{receipt.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        var reader = await test.CustomUserAsync(PermissionCodes.TicketReadAll);
        using var readerClient = await test.Accounts.ClientAsync(reader);
        using var denied = await readerClient.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "Description", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = test.Accounts.Factory.CreateClient();
        using var noToken = await anonymous.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "Description", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentCreationGetsDistinctDatabaseNumbersAndOneActivityForEveryTicket()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.Requester);
        using var client = await test.Accounts.ClientAsync(user);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync("/api/v1/tickets",
            new { Title = test.Prefix + i, Description = "Description", CategoryId = test.Category })));
        var created = new List<TicketCreated>();
        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var receipt = (await response.Content.ReadFromJsonAsync<TicketCreated>())!;
                test.OwnTicket(receipt.Id);
                created.Add(receipt);
            }
            Assert.Equal(8, created.Select(x => x.TicketNumber).Distinct().Count());
            Assert.All(created, x => Assert.True(x.TicketNumber > 0));
            await using var database = test.Accounts.Database();
            var ids = created.Select(x => x.Id).ToArray();
            Assert.Equal(8, await database.TicketActivities.CountAsync(x => ids.Contains(x.TicketId) && x.Action == "TicketCreated"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresTheory]
    [InlineData("blank-title")]
    [InlineData("long-title")]
    [InlineData("blank-description")]
    [InlineData("long-description")]
    [InlineData("missing-category")]
    [InlineData("empty-category")]
    [InlineData("invalid-priority")]
    [InlineData("requesterId")]
    [InlineData("createdByUserId")]
    [InlineData("ticketNumber")]
    [InlineData("status")]
    [InlineData("assignedTeamId")]
    [InlineData("assignedAgentId")]
    [InlineData("createdAt")]
    [InlineData("version")]
    [InlineData("resolutionSummary")]
    public async Task InvalidAndServerOwnedFieldsAreRejectedEvenForAdmin(string input)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var body = new Dictionary<string, object?> { ["title"] = test.Prefix, ["description"] = "private-description-marker", ["categoryId"] = test.Category };
        switch (input)
        {
            case "blank-title": body["title"] = "   "; break;
            case "long-title": body["title"] = new string('x', 301); break;
            case "blank-description": body["description"] = "   "; break;
            case "long-description": body["description"] = new string('x', 20_001); break;
            case "missing-category": body.Remove("categoryId"); break;
            case "empty-category": body["categoryId"] = Guid.Empty; break;
            case "invalid-priority": body["priority"] = 999; break;
            default: body[input] = "forged-server-owned-value"; break;
        }
        using var response = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/tickets", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-description-marker", problem);
        Assert.DoesNotContain("forged-server-owned-value", problem);
        await using var database = test.Accounts.Database();
        Assert.False(await database.Tickets.AnyAsync(x => x.CategoryId == test.Category));
    }

    [PostgresFact]
    public async Task MaximumTitleAndDescriptionLengthsAreAccepted()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var title = new string('T', 300);
        var description = new string('D', 20_000);
        using var response = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/tickets", new { Title = title, Description = description, CategoryId = test.Category, Priority = TicketPriority.Low });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        var ticket = await test.StoredAsync(receipt.Id);
        Assert.Equal(title, ticket.Title);
        Assert.Equal(description, ticket.Description);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
    }

    [PostgresFact]
    public async Task ActivityFailureAfterTicketInsertRollsBackTicketAndKeepsErrorSanitized()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var failure = new FailCreationActivity();
        await using var factory = test.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(failure).Options)))));
        var user = await test.UserAsync(RoleNames.Requester);
        var login = await test.Accounts.LoginAsync(user);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        using var response = await client.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "private-description-marker", CategoryId = test.Category });
        // Keep cleanup safe even if a regression commits unexpectedly; do not hide the assertion.
        if (failure.TicketId != Guid.Empty) test.OwnTicket(failure.TicketId);
        if (response.StatusCode == HttpStatusCode.Created) test.OwnTicket((await response.Content.ReadFromJsonAsync<TicketCreated>())!.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(failure.SawPersistedTicket);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-marker", body);
        Assert.DoesNotContain("private-description-marker", body);
        await using var database = test.Accounts.Database();
        Assert.False(await database.Tickets.AnyAsync(x => x.Id == failure.TicketId));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == failure.TicketId));
        using var next = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix + "Next", Description = "Description", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        var receipt = (await next.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        Assert.True(receipt.TicketNumber > failure.Number); // PostgreSQL identity sequences are not rolled back.
    }

    private sealed class FailCreationActivity : SaveChangesInterceptor
    {
        public bool SawPersistedTicket { get; private set; }
        public Guid TicketId { get; private set; }
        public long Number { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var database = eventData.Context!;
            if (database.ChangeTracker.Entries<TicketActivity>().Any(x => x.State == EntityState.Added && x.Entity.Action == "TicketCreated"))
            {
                var ticket = database.ChangeTracker.Entries<Ticket>().Single(x => x.State == EntityState.Unchanged && x.Entity.TicketNumber > 0).Entity;
                SawPersistedTicket = true;
                TicketId = ticket.Id;
                Number = ticket.TicketNumber;
                throw new InvalidOperationException("secret-marker: activity insert failed");
            }
            return ValueTask.FromResult(result);
        }
    }
}
