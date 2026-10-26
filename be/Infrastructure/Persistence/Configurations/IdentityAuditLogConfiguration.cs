using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public sealed class IdentityAuditLogConfiguration : IEntityTypeConfiguration<IdentityAuditLog>
{
    public void Configure(EntityTypeBuilder<IdentityAuditLog> builder)
    {
        builder.ToTable("identity_audit_logs", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.TargetRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
