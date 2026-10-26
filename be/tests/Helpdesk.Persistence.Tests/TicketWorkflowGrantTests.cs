using System.Net.Http.Json;
using be.Domain.Constants;
using be.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;
using TestAccounts = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestAccounts;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketWorkflowGrantTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task SeedPreservesOldRoleGrantsAndExplicitWorkflowGrantIsAuditedAndIdempotent()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        await using var database = test.Accounts.Database();
        string[] codes = [PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel];
        var permissions = await database.Permissions.Where(x => codes.Contains(x.Code)).Select(x => x.Id).ToArrayAsync();
        var roleIds = new List<Guid>();
        foreach (var role in new[] { RoleNames.Requester, RoleNames.SupportAgent, RoleNames.TeamLeader, RoleNames.SystemAdmin })
        {
            var id = await test.Accounts.RoleIdAsync(role);
            roleIds.Add(id);
            await test.Accounts.SnapshotAsync(id);
        }
        var customRole = await test.Accounts.CustomRoleAsync(PermissionCodes.TicketReadOwn);
        var priorAudits = await database.IdentityAuditLogs.Where(x => x.Action == "TicketWorkflowEnabled").Select(x => x.Id).ToArrayAsync();
        try
        {
            await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && permissions.Contains(x.PermissionId)).ExecuteDeleteAsync();
            var otherGrants = await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).CountAsync();
            await new IdentityInitializer(database, TestAccounts.Hasher()).SeedAsync();
            Assert.False(await database.RolePermissions.AnyAsync(x => roleIds.Contains(x.RoleId) && permissions.Contains(x.PermissionId)));
            Assert.Equal(otherGrants, await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).CountAsync());
            // New scopes must fail closed until the operator explicitly enables the grants.
            var requester = await test.UserAsync(RoleNames.Requester);
            using var client = await test.Accounts.ClientAsync(requester);
            var ticket = await test.TicketAsync(requester.Id);
            using var denied = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/transition", new { Status = 6, ticket.Version });
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
            await using (var grant = test.Accounts.Database())
                await new IdentityInitializer(grant, TestAccounts.Hasher()).GrantTicketWorkflowAsync();
            await using (var rerun = test.Accounts.Database())
                await new IdentityInitializer(rerun, TestAccounts.Hasher()).GrantTicketWorkflowAsync();
            Assert.Equal(12, await database.RolePermissions.CountAsync(x => roleIds.Contains(x.RoleId) && permissions.Contains(x.PermissionId)));
            Assert.Equal(otherGrants + 12, await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).CountAsync());
            Assert.False(await database.RolePermissions.AnyAsync(x => x.RoleId == customRole && permissions.Contains(x.PermissionId)));
            Assert.Equal(4, await database.IdentityAuditLogs.CountAsync(x => x.Action == "TicketWorkflowEnabled" && !priorAudits.Contains(x.Id)));
            using var allowed = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/transition", new { Status = 6, ticket.Version });
            Assert.Equal(System.Net.HttpStatusCode.OK, allowed.StatusCode);
        }
        finally
        {
            await database.IdentityAuditLogs.Where(x => x.Action == "TicketWorkflowEnabled" && !priorAudits.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
