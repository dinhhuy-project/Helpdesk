using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM identity.users GROUP BY lower(btrim(email)) HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate normalized emails exist. Review conflicting accounts before applying AccountAdministration; no users were changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "users",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                schema: "identity",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "roles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                schema: "identity",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                computedColumnSql: "lower(btrim(email))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "identity_audit_logs",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_audit_logs_roles_target_role_id",
                        column: x => x.target_role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_identity_audit_logs_users_actor_id",
                        column: x => x.actor_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_identity_audit_logs_users_target_user_id",
                        column: x => x.target_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                schema: "identity",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_audit_logs_actor_id",
                schema: "identity",
                table: "identity_audit_logs",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_audit_logs_created_at",
                schema: "identity",
                table: "identity_audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_identity_audit_logs_target_role_id",
                schema: "identity",
                table: "identity_audit_logs",
                column: "target_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_audit_logs_target_user_id",
                schema: "identity",
                table: "identity_audit_logs",
                column: "target_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_audit_logs",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "identity",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "roles");
        }
    }
}
