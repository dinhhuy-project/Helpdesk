using System.Net;
using System.Net.Http.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketQueryTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task FiltersUtcDateBoundsAndRankedSortKeepScopeBeforeCountAndPaging()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var other = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        using var client = await test.Accounts.ClientAsync(owner);
        var created = new List<TicketCreated>();
        foreach (var priority in new[] { TicketPriority.Low, TicketPriority.High, TicketPriority.Urgent })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/tickets", new { Title = test.Prefix + priority, Description = "Description", CategoryId = test.Category, Priority = priority });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var receipt = (await response.Content.ReadFromJsonAsync<TicketCreated>())!;
            test.OwnTicket(receipt.Id);
            created.Add(receipt);
        }
        var low = created[0];
        var high = created[1];
        var urgent = created[2];
        var assigned = await test.TicketAsync(owner.Id, test.TeamA, agent.Id, status: TicketStatus.InProgress);
        var hidden = await test.TicketAsync(other.Id, test.TeamB);
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var database = test.Accounts.Database();
        for (var i = 0; i < created.Count; i++)
        {
            var id = created[i].Id;
            var at = epoch.AddDays(i);
            await database.Tickets.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, at));
        }
        await database.Tickets.Where(x => x.Id == assigned.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, epoch.AddDays(3)));
        await database.Tickets.Where(x => x.Id == high.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, epoch.AddDays(4)));

        async Task<PagedResult<TicketSummary>> Query(string query) =>
            (await client.GetFromJsonAsync<PagedResult<TicketSummary>>("/api/v1/tickets?" + query))!;
        async Task Only(string query, params Guid[] expected)
        {
            var result = await Query(query);
            Assert.Equal(expected.Length, result.TotalCount);
            Assert.Equal(expected.Order(), result.Items.Select(x => x.Id).Order());
        }
        await Only("status=New", low.Id, high.Id, urgent.Id);
        await Only("priority=High", high.Id);
        await Only($"teamId={test.TeamA}", assigned.Id);
        await Only($"categoryId={test.Category}&requesterId={owner.Id}", low.Id, high.Id, urgent.Id, assigned.Id);
        await Only($"requesterId={other.Id}");
        await Only($"agentId={agent.Id}", assigned.Id);
        await Only("unassigned=true", low.Id, high.Id, urgent.Id);
        await Only("unassigned=false", assigned.Id);
        await Only($"ticketNumber={high.TicketNumber}", high.Id);
        await Only($"ticketNumber={hidden.TicketNumber}");
        await Only($"teamId={test.TeamB}&unassigned=true");
        await Only("search=" + Uri.EscapeDataString(test.Prefix + "HIGH"), high.Id);
        await Only("createdFrom=" + Uri.EscapeDataString("2026-01-03T07:00:00+07:00") + "&createdTo=2026-01-04T00:00:00Z", urgent.Id);

        var ascending = await Query("sortBy=priority&descending=false");
        Assert.Equal(new[] { low.Id, assigned.Id, high.Id, urgent.Id }, ascending.Items.Select(x => x.Id));
        var descending = await Query("sortBy=priority");
        Assert.Equal(ascending.Items.Reverse().Select(x => x.Id), descending.Items.Select(x => x.Id));
        Assert.Equal(high.Id, (await Query("sortBy=updatedAt")).Items[0].Id);
        Assert.Equal(assigned.Id, (await Query("")).Items[0].Id);
        var first = await Query("sortBy=ticketNumber&descending=false&pageSize=2");
        var second = await Query("sortBy=ticketNumber&descending=false&pageSize=2&page=2");
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        var all = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(4, all.Select(x => x.Id).Distinct().Count());
        Assert.Equal(all.Select(x => x.TicketNumber).Order(), all.Select(x => x.TicketNumber));
        var empty = await Query("page=3&pageSize=2");
        Assert.Equal(4, empty.TotalCount);
        Assert.Empty(empty.Items);
    }

    [PostgresTheory]
    [InlineData("ticketNumber=0")]
    [InlineData("ticketNumber=-1")]
    [InlineData("requesterId=00000000-0000-0000-0000-000000000000")]
    [InlineData("agentId=00000000-0000-0000-0000-000000000000")]
    [InlineData("sortBy=description")]
    [InlineData("createdFrom=2026-01-02T00:00:00Z&createdTo=2026-01-01T00:00:00Z")]
    [InlineData("createdFrom=2026-01-01T00:00:00Z&createdTo=2026-01-01T07:00:00%2B07:00")]
    [InlineData("createdFrom=not-a-date")]
    public async Task InvalidQueryIsRejected(string query)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        using var response = await test.Accounts.Admin.GetAsync("/api/v1/tickets?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
