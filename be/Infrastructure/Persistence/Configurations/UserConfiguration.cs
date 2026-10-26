using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired()
            .HasComputedColumnSql("lower(btrim(email))", stored: true);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(1024).IsRequired(false);
        builder.Property(x => x.AvatarUrl).HasMaxLength(2048).IsRequired(false);
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

