using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class TicketSlaConfiguration : IEntityTypeConfiguration<TicketSla>
{
    public void Configure(EntityTypeBuilder<TicketSla> builder)
    {
        builder.ToTable("ticket_slas", "helpdesk", table =>
            table.HasCheckConstraint("ck_ticket_slas_deadlines", "first_response_due_at >= applied_at AND resolution_due_at >= applied_at"));
        builder.HasKey(x => x.TicketId);
        builder.Property(x => x.TicketId).ValueGeneratedNever();
        builder.HasOne<Ticket>().WithOne().HasForeignKey<TicketSla>(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SlaPolicy>().WithMany().HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FirstResponseDueAt).HasFilter("first_responded_at IS NULL");
        builder.HasIndex(x => x.ResolutionDueAt).HasFilter("resolved_at IS NULL");
    }
}

