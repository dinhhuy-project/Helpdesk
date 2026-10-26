using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class SupportTeamMemberConfiguration : IEntityTypeConfiguration<SupportTeamMember>
{
    public void Configure(EntityTypeBuilder<SupportTeamMember> builder)
    {
        builder.ToTable("support_team_members", "organization");
        builder.HasKey(x => new { x.SupportTeamId, x.UserId });
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasOne<SupportTeam>().WithMany().HasForeignKey(x => x.SupportTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

