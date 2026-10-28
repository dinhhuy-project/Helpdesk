using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachment_upload_intents",
                schema: "helpdesk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleting = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment_upload_intents", x => x.id);
                    table.CheckConstraint("ck_attachment_upload_intents_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_attachment_upload_intents_size", "size_bytes > 0 AND size_bytes <= 10485760");
                    table.ForeignKey(
                        name: "fk_attachment_upload_intents_ticket_comments_comment_id_ticket",
                        columns: x => new { x.comment_id, x.ticket_id },
                        principalSchema: "helpdesk",
                        principalTable: "ticket_comments",
                        principalColumns: new[] { "id", "ticket_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachment_upload_intents_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "helpdesk",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachment_upload_intents_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_upload_intents_bucket_object_key",
                schema: "helpdesk",
                table: "attachment_upload_intents",
                columns: new[] { "bucket", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachment_upload_intents_comment_id_ticket_id",
                schema: "helpdesk",
                table: "attachment_upload_intents",
                columns: new[] { "comment_id", "ticket_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_upload_intents_expires_at",
                schema: "helpdesk",
                table: "attachment_upload_intents",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_attachment_upload_intents_ticket_id_is_internal",
                schema: "helpdesk",
                table: "attachment_upload_intents",
                columns: new[] { "ticket_id", "is_internal" });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_upload_intents_uploaded_by_user_id",
                schema: "helpdesk",
                table: "attachment_upload_intents",
                column: "uploaded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM helpdesk.attachment_upload_intents) THEN
                        RAISE EXCEPTION 'Drain attachment upload intents before rollback; their objects may still need cleanup.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "attachment_upload_intents",
                schema: "helpdesk");
        }
    }
}
