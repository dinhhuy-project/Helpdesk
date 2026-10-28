using be.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace be.Infrastructure.Persistence.Configurations;

public sealed class AttachmentUploadIntentConfiguration : IEntityTypeConfiguration<AttachmentUploadIntent>
{
    public void Configure(EntityTypeBuilder<AttachmentUploadIntent> builder)
    {
        builder.ToTable("attachment_upload_intents", "helpdesk", table =>
        {
            table.HasCheckConstraint("ck_attachment_upload_intents_size", "size_bytes > 0 AND size_bytes <= 10485760");
            table.HasCheckConstraint("ck_attachment_upload_intents_expiry", "expires_at > created_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Bucket).HasMaxLength(63).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.Bucket, x.ObjectKey }).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => new { x.TicketId, x.IsInternal });
        builder.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketComment>().WithMany().HasForeignKey(x => new { x.CommentId, x.TicketId })
            .HasPrincipalKey(x => new { x.Id, x.TicketId }).OnDelete(DeleteBehavior.Restrict);
    }
}
