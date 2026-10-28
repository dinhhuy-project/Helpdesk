using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class TicketSlaConfiguration : IEntityTypeConfiguration<TicketSla>
{
    public void Configure(EntityTypeBuilder<TicketSla> builder)
    {
        builder.ToTable("ticket_slas", "helpdesk", table =>
        {
            table.HasCheckConstraint("ck_ticket_slas_deadlines", "first_response_due_at >= applied_at AND resolution_due_at >= applied_at");
            table.HasCheckConstraint("ck_ticket_slas_snapshot", "(policy_name IS NULL AND priority IS NULL AND first_response_minutes IS NULL AND resolution_minutes IS NULL) OR (policy_name IS NOT NULL AND priority IS NOT NULL AND first_response_minutes IS NOT NULL AND resolution_minutes IS NOT NULL AND length(btrim(policy_name)) > 0 AND priority IN ('Low','Normal','High','Urgent') AND first_response_minutes > 0 AND first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600)");
        });
        builder.HasKey(x => x.TicketId);
        builder.Property(x => x.TicketId).ValueGeneratedNever();
        builder.Property(x => x.PolicyName).HasMaxLength(200);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Ticket>().WithOne().HasForeignKey<TicketSla>(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SlaPolicy>().WithMany().HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FirstResponseDueAt).HasFilter("first_responded_at IS NULL");
        builder.HasIndex(x => x.ResolutionDueAt).HasFilter("resolved_at IS NULL");
        builder.HasIndex(x => x.FirstResponseDueAt, "ix_ticket_slas_response_pending").HasFilter("first_response_breached_at IS NULL AND first_responded_at IS NULL");
        builder.HasIndex(x => x.ResolutionDueAt, "ix_ticket_slas_resolution_pending").HasFilter("resolution_breached_at IS NULL AND resolved_at IS NULL");
    }
}

