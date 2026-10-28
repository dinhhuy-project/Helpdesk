using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("sla_policies", "helpdesk", table =>
        {
            table.HasCheckConstraint("ck_sla_policies_priority", "priority IN ('Low', 'Normal', 'High', 'Urgent')");
            table.HasCheckConstraint("ck_sla_policies_duration", "first_response_minutes > 0 AND resolution_minutes > 0");
            table.HasCheckConstraint("ck_sla_policies_targets", "first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600");
            table.HasCheckConstraint("ck_sla_policies_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => x.Priority).IsUnique().HasFilter("is_active");
    }
}

