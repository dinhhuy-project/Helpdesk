using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlaMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "first_response_breached_at",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "first_response_minutes",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_name",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "resolution_breached_at",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "resolution_minutes",
                schema: "helpdesk",
                table: "ticket_slas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "helpdesk",
                table: "sla_policies",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_slas_first_response_due_at1",
                schema: "helpdesk",
                table: "ticket_slas",
                column: "first_response_due_at",
                filter: "first_response_breached_at IS NULL AND first_responded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_slas_resolution_due_at1",
                schema: "helpdesk",
                table: "ticket_slas",
                column: "resolution_due_at",
                filter: "resolution_breached_at IS NULL AND resolved_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ticket_slas_snapshot",
                schema: "helpdesk",
                table: "ticket_slas",
                sql: "(policy_name IS NULL AND priority IS NULL AND first_response_minutes IS NULL AND resolution_minutes IS NULL) OR (policy_name IS NOT NULL AND priority IS NOT NULL AND first_response_minutes IS NOT NULL AND resolution_minutes IS NOT NULL AND length(btrim(policy_name)) > 0 AND priority IN ('Low','Normal','High','Urgent') AND first_response_minutes > 0 AND first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600)");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_priority",
                schema: "helpdesk",
                table: "sla_policies",
                column: "priority",
                unique: true,
                filter: "is_active");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sla_policies_name",
                schema: "helpdesk",
                table: "sla_policies",
                sql: "length(btrim(name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sla_policies_targets",
                schema: "helpdesk",
                table: "sla_policies",
                sql: "first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM helpdesk.ticket_slas WHERE policy_name IS NOT NULL
                        OR first_response_breached_at IS NOT NULL OR resolution_breached_at IS NOT NULL) THEN
                        RAISE EXCEPTION 'Preserve SLA snapshots and breach history before rollback.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "ix_ticket_slas_first_response_due_at1",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropIndex(
                name: "ix_ticket_slas_resolution_due_at1",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ticket_slas_snapshot",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropIndex(
                name: "ix_sla_policies_priority",
                schema: "helpdesk",
                table: "sla_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sla_policies_name",
                schema: "helpdesk",
                table: "sla_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sla_policies_targets",
                schema: "helpdesk",
                table: "sla_policies");

            migrationBuilder.DropColumn(
                name: "first_response_breached_at",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "first_response_minutes",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "policy_name",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "resolution_breached_at",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "resolution_minutes",
                schema: "helpdesk",
                table: "ticket_slas");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "helpdesk",
                table: "sla_policies");
        }
    }
}
