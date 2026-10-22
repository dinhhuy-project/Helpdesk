using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", "helpdesk", table =>
        {
            table.HasCheckConstraint("ck_tickets_status", "status IN ('New', 'InProgress', 'WaitingForRequester', 'Resolved', 'Closed', 'Cancelled', 'Reopened')");
            table.HasCheckConstraint("ck_tickets_priority", "priority IN ('Low', 'Normal', 'High', 'Urgent')");
            table.HasCheckConstraint("ck_tickets_resolution", "status NOT IN ('Resolved', 'Closed') OR (resolution_summary IS NOT NULL AND length(btrim(resolution_summary)) > 0 AND resolved_at IS NOT NULL)");
            table.HasCheckConstraint("ck_tickets_closed_at", "status <> 'Closed' OR closed_at IS NOT NULL");
            table.HasCheckConstraint("ck_tickets_assigned_team", "assigned_agent_id IS NULL OR assigned_team_id IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TicketNumber).UseIdentityAlwaysColumn();
        builder.HasIndex(x => x.TicketNumber).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text").IsRequired();
        builder.Property(x => x.ResolutionSummary).HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsRowVersion().HasColumnName("xmin");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedAgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupportTeam>().WithMany().HasForeignKey(x => x.AssignedTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RequesterId, x.CreatedAt });
        builder.HasIndex(x => new { x.AssignedTeamId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.AssignedAgentId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

