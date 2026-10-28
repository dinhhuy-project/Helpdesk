using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketActivityVisibilityTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task UnknownActionsAndExtraPayloadFieldsNeverEscapeThroughActivityApi()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(owner.Id);
        var commentId = Guid.NewGuid();
        await using var database = test.Accounts.Database();
        var known = new TicketActivity { TicketId = ticket.Id, ActorId = owner.Id, Action = TicketActivityActions.PublicCommentAdded,
            NewValue = JsonSerializer.Serialize(new { CommentId = commentId, Content = "private-audit-marker", ResolutionSummary = "private-audit-marker", IsInternal = true }),
            OldValue = "{\"Content\":\"private-audit-marker\"}" };
        var unknown = new TicketActivity { TicketId = ticket.Id, Action = "FuturePrivateAction", NewValue = "{\"Content\":\"private-audit-marker\"}" };
        var malformed = new TicketActivity { TicketId = ticket.Id, Action = TicketActivityActions.Assigned, NewValue = "[\"private-audit-marker\"]" };
        database.TicketActivities.AddRange(known, unknown, malformed);
        await database.SaveChangesAsync();
        using var requester = await test.Accounts.ClientAsync(owner);
        foreach (var client in new[] { requester, test.Accounts.Admin })
        {
            using var list = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/activities");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var json = await list.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-audit-marker", json);
            Assert.DoesNotContain(unknown.Id.ToString(), json);
            var page = JsonSerializer.Deserialize<PagedResult<TicketActivityDetails>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(2, page.TotalCount);
            var projected = page.Items.Single(x => x.Id == known.Id);
            Assert.Equal(commentId, projected.NewValue!.CommentId);
            Assert.False(projected.NewValue.IsInternal);
            Assert.Null(projected.NewValue.ResolutionSummary);
            Assert.Null(page.Items.Single(x => x.Id == malformed.Id).NewValue);
            using var hidden = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/activities/{unknown.Id}");
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "NotFound");
        }
    }

    [PostgresFact]
    public async Task TimelineProjectsKnownCreationAssignmentAndPublicResolutionHistory()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var requester = await test.Accounts.ClientAsync(owner);
        using var support = await test.Accounts.ClientAsync(agent);
        using var create = await requester.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix, Description = "Private description", CategoryId = test.Category });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var receipt = (await create.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.OwnTicket(receipt.Id);
        using var assign = await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/assign", new { TeamId = test.TeamA, AgentId = agent.Id, receipt.Version });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assigned = (await assign.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        using var start = await support.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/transition", new { Status = 2, assigned.Version });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = (await start.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        using var resolve = await support.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/transition", new { Status = 4, ResolutionSummary = "Public result", started.Version });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var resolved = (await resolve.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        using var reopen = await requester.PostAsJsonAsync($"/api/v1/tickets/{receipt.Id}/transition", new { Status = 7, resolved.Version });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        var page = (await requester.GetFromJsonAsync<PagedResult<TicketActivityDetails>>($"/api/v1/tickets/{receipt.Id}/activities"))!;
        Assert.Equal(5, page.TotalCount);
        var creation = Assert.Single(page.Items, x => x.Action == TicketActivityActions.Created);
        Assert.Equal(receipt.TicketNumber, creation.NewValue!.TicketNumber);
        Assert.Equal(owner.Id, creation.NewValue.RequesterId);
        Assert.Equal("New", creation.NewValue.Status);
        var transfer = Assert.Single(page.Items, x => x.Action == TicketActivityActions.Assigned);
        Assert.Equal(agent.Id, transfer.NewValue!.AssignedAgentId);
        Assert.Equal(test.TeamA, transfer.NewValue.AssignedTeamId);
        var reopened = Assert.Single(page.Items, x => x.NewValue?.Status == "Reopened");
        Assert.Equal("Public result", reopened.OldValue!.ResolutionSummary);
        Assert.Null(reopened.NewValue!.ResolutionSummary);
        using var get = await requester.GetAsync($"/api/v1/tickets/{receipt.Id}/activities/{reopened.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(reopened, await get.Content.ReadFromJsonAsync<TicketActivityDetails>());
        using var anonymous = test.Accounts.Factory.CreateClient();
        var swagger = await anonymous.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        foreach (var path in new[] { "/api/v1/tickets/{ticketId}/comments", "/api/v1/tickets/{ticketId}/activities" })
        {
            var operation = swagger.GetProperty("paths").GetProperty(path).GetProperty("get");
            Assert.True(operation.TryGetProperty("security", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        }
        var comments = swagger.GetProperty("paths").GetProperty("/api/v1/tickets/{ticketId}/comments");
        Assert.True(comments.GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.False(comments.TryGetProperty("patch", out _));
        Assert.False(comments.TryGetProperty("delete", out _));
    }
}
