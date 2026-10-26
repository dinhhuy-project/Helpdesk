using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class SupportTeamConfiguration : IEntityTypeConfiguration<SupportTeam>
{
    public void Configure(EntityTypeBuilder<SupportTeam> builder)
    {
        builder.ToTable("support_teams", "organization", table =>
            table.HasCheckConstraint("ck_support_teams_name_not_blank", "length(btrim(name)) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired()
            .HasComputedColumnSql("lower(btrim(name))", stored: true);
        builder.HasIndex(x => new { x.DepartmentId, x.NormalizedName }).IsUnique();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired(false);
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

