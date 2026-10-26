using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class TicketCategoryConfiguration : IEntityTypeConfiguration<TicketCategory>
{
    public void Configure(EntityTypeBuilder<TicketCategory> builder)
    {
        builder.ToTable("ticket_categories", "helpdesk", table =>
        {
            table.HasCheckConstraint("ck_ticket_categories_name_not_blank", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_ticket_categories_parent_not_self", "parent_id IS NULL OR parent_id <> id");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired()
            .HasComputedColumnSql("lower(btrim(name))", stored: true);
        builder.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("parent_id IS NULL");
        builder.HasIndex(x => new { x.ParentId, x.NormalizedName }).IsUnique().HasFilter("parent_id IS NOT NULL");
        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired(false);
        builder.HasOne<TicketCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

