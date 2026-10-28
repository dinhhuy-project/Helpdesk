using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Persistence;

public class HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IdentityAuditLog> IdentityAuditLogs => Set<IdentityAuditLog>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<SupportTeam> SupportTeams => Set<SupportTeam>();
    public DbSet<SupportTeamMember> SupportTeamMembers => Set<SupportTeamMember>();
    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<AttachmentUploadIntent> AttachmentUploadIntents => Set<AttachmentUploadIntent>();
    public DbSet<TicketActivity> TicketActivities => Set<TicketActivity>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<TicketSla> TicketSlas => Set<TicketSla>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp with time zone");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);
    }
}
