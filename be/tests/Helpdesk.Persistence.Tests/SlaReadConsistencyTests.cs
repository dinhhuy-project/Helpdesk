using System.Data.Common;
using System.Net.Http.Json;
using be.Application.Features.Sla;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class SlaReadConsistencyTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task NullSnapshotIncludesServerStatusAndUtcEvaluationTime()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, status: TicketStatus.WaitingForRequester);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        var before = DateTime.UtcNow;
        var result = (await client.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!;
        Assert.Null(result.Sla);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(TicketStatus.WaitingForRequester, result.Status);
        Assert.Equal(DateTimeKind.Utc, result.AsOf.Kind);
        Assert.InRange(result.AsOf, before, DateTime.UtcNow);
    }

    [PostgresFact]
    public async Task ConcurrentCancellationCannotMixTicketStatusAndSlaFromDifferentSnapshots()
    {
        var probe = new BeforeSlaRead();
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options))));
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        await using var database = test.Data.Accounts.Database();
        var policy = new SlaPolicy { Name = "Read consistency", IsActive = false, FirstResponseMinutes = 60, ResolutionMinutes = 90 };
        database.AddRange(policy, new TicketSla { TicketId = ticket.Id, SlaPolicyId = policy.Id, AppliedAt = DateTime.UtcNow.AddHours(-2),
            FirstResponseDueAt = DateTime.UtcNow.AddHours(-1), ResolutionDueAt = DateTime.UtcNow.AddMinutes(-30) });
        await database.SaveChangesAsync();
        test.Data.OwnSlaPolicy(policy.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        probe.Callback = async () => await database.Tickets.Where(x => x.Id == ticket.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, TicketStatus.Cancelled));
        var result = (await client.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!;
        Assert.True(probe.Fired);
        Assert.Equal(TicketStatus.New, result.Status);
        Assert.True(result.Sla!.FirstResponseBreached);
        Assert.True(result.Sla.ResolutionBreached);
        Assert.Null(result.Sla.FirstResponseBreachedAt);
        var next = (await client.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!;
        Assert.Equal(TicketStatus.Cancelled, next.Status);
        Assert.False(next.Sla!.FirstResponseBreached);
        Assert.False(next.Sla.ResolutionBreached);
    }

    private sealed class BeforeSlaRead : DbCommandInterceptor
    {
        public Func<Task>? Callback { get; set; }
        public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Callback is not null && command.CommandText.Contains("FROM helpdesk.ticket_slas", StringComparison.Ordinal))
            {
                var callback = Callback; Callback = null; Fired = true;
                await callback();
            }
            return result;
        }
    }
}
