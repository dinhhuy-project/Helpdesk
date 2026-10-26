using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments", "organization", table =>
            table.HasCheckConstraint("ck_departments_name_not_blank", "length(btrim(name)) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired()
            .HasComputedColumnSql("lower(btrim(name))", stored: true);
        builder.HasIndex(x => x.NormalizedName).IsUnique();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired(false);
    }
}

