using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public class CommunityConfiguration : IEntityTypeConfiguration<CommunityEntry>, IEntityTypeConfiguration<IncidentUpdate>,
    IEntityTypeConfiguration<IncidentAffectedUser>, IEntityTypeConfiguration<KnowledgeFeedback>
{
    public void Configure(EntityTypeBuilder<CommunityEntry> b)
    {
        b.ToTable("community_entries", "helpdesk", t =>
        {
            t.HasCheckConstraint("ck_community_entries_kind", "(kind = 'Knowledge' AND status IS NULL) OR (kind = 'Incident' AND status IN ('Investigating','Identified','Monitoring','Resolved') AND status IS NOT NULL AND source_ticket_id IS NULL)");
            t.HasCheckConstraint("ck_community_entries_published", "NOT is_published OR (public_revision > 0 AND published_at IS NOT NULL AND public_title IS NOT NULL AND public_summary IS NOT NULL AND public_content IS NOT NULL AND public_topic IS NOT NULL AND ((kind = 'Knowledge' AND public_status IS NULL) OR (kind = 'Incident' AND public_status IN ('Investigating','Identified','Monitoring','Resolved') AND public_status IS NOT NULL)))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsRowVersion();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PublicStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Content).HasMaxLength(30000).IsRequired();
        b.Property(x => x.Topic).HasMaxLength(100).IsRequired();
        b.Property(x => x.PublicTitle).HasMaxLength(200);
        b.Property(x => x.PublicSummary).HasMaxLength(1000);
        b.Property(x => x.PublicContent).HasMaxLength(30000);
        b.Property(x => x.PublicTopic).HasMaxLength(100);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ticket>().WithMany().HasForeignKey(x => x.SourceTicketId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Kind, x.PublishedAt, x.Id }).HasFilter("is_published");
        b.HasIndex(x => new { x.Kind, x.UpdatedAt, x.Id });
        b.HasIndex(x => new { x.PublicTitle, x.PublicSummary, x.PublicContent, x.PublicTopic })
            .HasMethod("GIN").IsTsVectorExpressionIndex("simple").HasFilter("is_published");
    }
    public void Configure(EntityTypeBuilder<IncidentUpdate> b)
    {
        b.ToTable("incident_updates", "helpdesk", t => t.HasCheckConstraint("ck_incident_updates_status", "status IN ('Investigating','Identified','Monitoring','Resolved')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.HasOne<CommunityEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EntryId, x.Revision }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<IncidentAffectedUser> b)
    {
        b.ToTable("incident_affected_users", "helpdesk");
        b.HasKey(x => new { x.EntryId, x.UserId });
        b.HasOne<CommunityEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<KnowledgeFeedback> b)
    {
        b.ToTable("knowledge_feedback", "helpdesk", t => t.HasCheckConstraint("ck_knowledge_feedback_solved", "NOT solved OR helpful"));
        b.HasKey(x => new { x.EntryId, x.UserId });
        b.HasOne<CommunityEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
