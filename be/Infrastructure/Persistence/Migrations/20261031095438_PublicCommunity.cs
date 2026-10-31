using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicCommunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "community_entries",
                schema: "helpdesk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    content = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    public_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    public_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    public_content = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: true),
                    public_topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    public_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    public_revision = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_community_entries", x => x.id);
                    table.CheckConstraint("ck_community_entries_kind", "(kind = 'Knowledge' AND status IS NULL) OR (kind = 'Incident' AND status IN ('Investigating','Identified','Monitoring','Resolved') AND status IS NOT NULL AND source_ticket_id IS NULL)");
                    table.CheckConstraint("ck_community_entries_published", "NOT is_published OR (public_revision > 0 AND published_at IS NOT NULL AND public_title IS NOT NULL AND public_summary IS NOT NULL AND public_content IS NOT NULL AND public_topic IS NOT NULL AND ((kind = 'Knowledge' AND public_status IS NULL) OR (kind = 'Incident' AND public_status IN ('Investigating','Identified','Monitoring','Resolved') AND public_status IS NOT NULL)))");
                    table.ForeignKey(
                        name: "fk_community_entries_tickets_source_ticket_id",
                        column: x => x.source_ticket_id,
                        principalSchema: "helpdesk",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_community_entries_users_created_by_id",
                        column: x => x.created_by_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_affected_users",
                schema: "helpdesk",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_affected_users", x => new { x.entry_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_incident_affected_users_community_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "helpdesk",
                        principalTable: "community_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_affected_users_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_updates",
                schema: "helpdesk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_updates", x => x.id);
                    table.CheckConstraint("ck_incident_updates_status", "status IN ('Investigating','Identified','Monitoring','Resolved')");
                    table.ForeignKey(
                        name: "fk_incident_updates_community_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "helpdesk",
                        principalTable: "community_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_feedback",
                schema: "helpdesk",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    helpful = table.Column<bool>(type: "boolean", nullable: false),
                    solved = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_feedback", x => new { x.entry_id, x.user_id });
                    table.CheckConstraint("ck_knowledge_feedback_solved", "NOT solved OR helpful");
                    table.ForeignKey(
                        name: "fk_knowledge_feedback_community_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "helpdesk",
                        principalTable: "community_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_knowledge_feedback_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_community_entries_created_by_id",
                schema: "helpdesk",
                table: "community_entries",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_community_entries_kind_published_at_id",
                schema: "helpdesk",
                table: "community_entries",
                columns: new[] { "kind", "published_at", "id" },
                filter: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_community_entries_kind_updated_at_id",
                schema: "helpdesk",
                table: "community_entries",
                columns: new[] { "kind", "updated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_community_entries_public_title_public_summary_public_conten",
                schema: "helpdesk",
                table: "community_entries",
                columns: new[] { "public_title", "public_summary", "public_content", "public_topic" },
                filter: "is_published")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "simple");

            migrationBuilder.CreateIndex(
                name: "ix_community_entries_source_ticket_id",
                schema: "helpdesk",
                table: "community_entries",
                column: "source_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_affected_users_user_id",
                schema: "helpdesk",
                table: "incident_affected_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_updates_entry_id_revision",
                schema: "helpdesk",
                table: "incident_updates",
                columns: new[] { "entry_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_feedback_user_id",
                schema: "helpdesk",
                table: "knowledge_feedback",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_affected_users",
                schema: "helpdesk");

            migrationBuilder.DropTable(
                name: "incident_updates",
                schema: "helpdesk");

            migrationBuilder.DropTable(
                name: "knowledge_feedback",
                schema: "helpdesk");

            migrationBuilder.DropTable(
                name: "community_entries",
                schema: "helpdesk");
        }
    }
}
