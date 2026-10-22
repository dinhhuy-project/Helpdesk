using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Trait("Category", "Integration")]
[Collection("Postgres")]
public class PostgresTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task RoundTripsIdentityEnumsJsonAndUtc()
    {
        await using var context = CreateContext();
        var first = await AddTicketAsync(context);
        var second = await AddTicketAsync(context);
        Assert.True(first.TicketNumber > 0);
        Assert.True(second.TicketNumber > first.TicketNumber);
        context.TicketActivities.Add(new TicketActivity
        {
            TicketId = first.Id, Action = "Created", NewValue = "{\"status\":\"New\"}"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var loaded = await context.Tickets.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(TicketStatus.New, loaded.Status);
        Assert.Equal(TicketPriority.Normal, loaded.Priority);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.NotNull((await context.TicketActivities.SingleAsync(x => x.TicketId == first.Id)).NewValue);
    }

    [PostgresFact]
    public async Task StaleTicketUpdateRaisesConcurrencyConflict()
    {
        await using var firstContext = CreateContext();
        var ticket = await AddTicketAsync(firstContext);
        await using var secondContext = CreateContext();
        var stale = await secondContext.Tickets.SingleAsync(x => x.Id == ticket.Id);
        ticket.Title = "Updated by first agent";
        await firstContext.SaveChangesAsync();
        stale.Title = "Updated by second agent";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task AttachmentCannotReferenceCommentFromAnotherTicket()
    {
        await using var context = CreateContext();
        var first = await AddTicketAsync(context);
        var second = await AddTicketAsync(context);
        var comment = new TicketComment { TicketId = first.Id, AuthorId = first.RequesterId, Content = "Comment" };
        context.TicketComments.Add(comment);
        await context.SaveChangesAsync();
        context.TicketAttachments.Add(new TicketAttachment
        {
            TicketId = second.Id, CommentId = comment.Id, UploadedByUserId = first.RequesterId,
            Bucket = "helpdesk-attachments", ObjectKey = Guid.NewGuid().ToString(),
            FileName = "test.txt", ContentType = "text/plain", SizeBytes = 1
        });
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgresFact]
    public async Task UserWithTicketHistoryCannotBeDeleted()
    {
        await using var context = CreateContext();
        var ticket = await AddTicketAsync(context);
        context.ChangeTracker.Clear();
        context.Users.Remove(new User { Id = ticket.RequesterId, Email = "unused", DisplayName = "unused" });
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgresFact]
    public async Task UndefinedEnumIsRejectedByDatabase()
    {
        await using var context = CreateContext();
        var ticket = await AddTicketAsync(context);
        ticket.Status = (TicketStatus)999;
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.CheckViolation);
    }

    [PostgresFact]
    public async Task SlaDurationMustBePositive()
    {
        await using var context = CreateContext();
        context.SlaPolicies.Add(new SlaPolicy { Name = "Invalid", FirstResponseMinutes = 0, ResolutionMinutes = 60 });
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.CheckViolation);
    }

    private HelpdeskDbContext CreateContext() => ModelTests.CreateContext(fixture.ConnectionString);

    [PostgresFact]
    public async Task ReviewedScriptCanBeReplayedWithoutChangingExistingData()
    {
        await using var context = CreateContext();
        var ticket = await AddTicketAsync(context);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(ModelTests.ReadSql("InitialCreate.sql"), connection);
        await command.ExecuteNonQueryAsync();
        await command.ExecuteNonQueryAsync();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        context.ChangeTracker.Clear();
        var loaded = await context.Tickets.SingleAsync(x => x.Id == ticket.Id);
        Assert.Equal(ticket.TicketNumber, loaded.TicketNumber);
        Assert.Equal(ticket.Title, loaded.Title);
    }

    [PostgresFact]
    public async Task DuplicateEmailIsRejectedByDatabase()
    {
        await using var context = CreateContext();
        var ticket = await AddTicketAsync(context);
        var original = await context.Users.SingleAsync(x => x.Id == ticket.RequesterId);
        context.Users.Add(new User { Email = original.Email, DisplayName = "Duplicate" });
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.UniqueViolation);
    }

    [PostgresFact]
    public async Task ResolvedTicketRequiresSummaryAndTimestamp()
    {
        await using var context = CreateContext();
        var ticket = await AddTicketAsync(context);
        ticket.Status = TicketStatus.Resolved;
        await AssertPostgresErrorAsync(context, PostgresErrorCodes.CheckViolation);
    }

    private static async Task<Ticket> AddTicketAsync(HelpdeskDbContext context)
    {
        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Test user" };
        var category = new TicketCategory { Name = "Test category" };
        context.Users.Add(user);
        context.TicketCategories.Add(category);
        var ticket = new Ticket
        {
            Title = "Test ticket", Description = "Test description", RequesterId = user.Id,
            CreatedByUserId = user.Id, CategoryId = category.Id
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();
        return ticket;
    }

    private static async Task AssertPostgresErrorAsync(HelpdeskDbContext context, string expectedCode)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(expectedCode, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
