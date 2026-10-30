using System.Net;
using System.Net.Http.Json;
using be.Application.Features.Accounts;
using be.Domain.Constants;
using Xunit;
using static Helpdesk.Persistence.Tests.AccountAdministrationTests;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class AccountAdministrationAccessTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task RoleOnlyManagerGetsOnlyAssignableTargetsAndRolesWithoutUserManagement()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        var manager = await test.NewUserAsync();
        await test.AssignAsync(manager.Id, await test.CustomRoleAsync(PermissionCodes.RoleManage, PermissionCodes.TicketReadOwn));
        var ordinaryRole = await test.CustomRoleAsync(PermissionCodes.TicketReadOwn);
        var outsideRole = await test.CustomRoleAsync(PermissionCodes.TicketReadAll);
        var ordinary = await test.NewUserAsync();
        await test.AssignAsync(ordinary.Id, ordinaryRole);
        var outside = await test.NewUserAsync();
        await test.AssignAsync(outside.Id, outsideRole);
        using var client = await test.ClientAsync(manager);
        using var users = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        var access = await client.GetFromJsonAsync<RoleAdministrationAccess>("/api/v1/identity/administration-access");
        Assert.NotNull(access);
        Assert.False(access.CanManageGrants);
        Assert.False(access.CanReadAudit);
        Assert.Contains(ordinaryRole, access.AssignableRoleIds);
        Assert.DoesNotContain(outsideRole, access.AssignableRoleIds);
        Assert.DoesNotContain(await test.RoleIdAsync(RoleNames.SystemAdmin), access.AssignableRoleIds);
        var page = await client.GetFromJsonAsync<PagedResult<AccountUser>>($"/api/v1/identity/assignment-users?search={ordinary.Email}&pageSize=1");
        Assert.NotNull(page);
        Assert.Equal(ordinary.Id, Assert.Single(page.Items).Id);
        var target = await client.GetFromJsonAsync<AccountUser>($"/api/v1/identity/assignment-users/{ordinary.Id}");
        Assert.NotNull(target);
        Assert.True(target.Version > 0);
        using var changed = await client.PutAsJsonAsync($"/api/v1/users/{ordinary.Id}/roles", new { RoleIds = new[] { ordinaryRole }, target.Version });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        foreach (var id in new[] { outside.Id, manager.Id, test.AdminUser.Id, Guid.NewGuid() })
        {
            using var hidden = await client.GetAsync($"/api/v1/identity/assignment-users/{id}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        var all = await client.GetFromJsonAsync<PagedResult<AccountUser>>("/api/v1/identity/assignment-users?pageSize=100");
        Assert.NotNull(all);
        Assert.DoesNotContain(all.Items, x => x.Id == outside.Id || x.Id == manager.Id || x.Id == test.AdminUser.Id);
        Assert.Equal(all.Items.Count, all.TotalCount); // Fixture stays below one page.
        using var invalid = await client.GetAsync("/api/v1/identity/assignment-users?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [PostgresFact]
    public async Task CapabilitiesReflectProtectedTargetsLastAdminAndCurrentDatabaseGrants()
    {
        await using var test = await TestAccounts.CreateAsync(fixture.ConnectionString);
        await test.SuspendOtherAdminsAsync();
        var manager = await test.NewUserAsync();
        var role = await test.CustomRoleAsync(PermissionCodes.UserManage);
        await test.AssignAsync(manager.Id, role);
        var ordinary = await test.NewUserAsync();
        using var client = await test.ClientAsync(manager);
        var access = await client.GetFromJsonAsync<AccountAdministrationAccess>($"/api/v1/users/{ordinary.Id}/administration-access");
        Assert.NotNull(access);
        Assert.True(access.CanUpdateProfile && access.CanChangeStatus);
        var protectedAccess = await client.GetFromJsonAsync<AccountAdministrationAccess>($"/api/v1/users/{test.AdminUser.Id}/administration-access");
        Assert.NotNull(protectedAccess);
        Assert.False(protectedAccess.CanUpdateProfile || protectedAccess.CanChangeStatus);
        Assert.True(protectedAccess.IsLastActiveAdmin);
        var adminAccess = await test.Admin.GetFromJsonAsync<AccountAdministrationAccess>($"/api/v1/users/{test.AdminUser.Id}/administration-access");
        Assert.NotNull(adminAccess);
        Assert.True(adminAccess.CanUpdateProfile);
        Assert.False(adminAccess.CanChangeStatus);
        var roleAccess = await test.Admin.GetFromJsonAsync<RoleAdministrationAccess>("/api/v1/identity/administration-access");
        Assert.NotNull(roleAccess);
        Assert.True(roleAccess.CanManageGrants && roleAccess.CanReadAudit);
        using var denied = await client.GetAsync("/api/v1/identity/assignment-users");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missing = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}/administration-access");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // Same access JWT, changed database permission: the read endpoint must reject it.
        var currentRole = Assert.Single(await test.RolesAsync(), x => x.Id == role);
        using var removed = await test.Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}/permissions", new { PermissionIds = Array.Empty<Guid>(), currentRole.Version });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var revoked = await client.GetAsync($"/api/v1/users/{ordinary.Id}/administration-access");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
}
