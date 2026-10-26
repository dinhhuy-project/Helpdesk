using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportTeamAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM organization.support_teams WHERE length(btrim(name)) = 0) THEN
                        RAISE EXCEPTION 'Blank support team names exist. Review teams before applying SupportTeamAdministration; no teams were changed.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM organization.support_teams GROUP BY department_id, lower(btrim(name)) HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate normalized team names exist within a department. Review teams before applying SupportTeamAdministration; no teams were changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "ix_support_teams_department_id",
                schema: "organization",
                table: "support_teams");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "support_teams",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "is_leader",
                schema: "organization",
                table: "support_team_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                schema: "organization",
                table: "support_team_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "support_team_members",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "organization",
                table: "support_teams",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "lower(btrim(name))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_teams_department_id_normalized_name",
                schema: "organization",
                table: "support_teams",
                columns: new[] { "department_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_support_teams_name_not_blank",
                schema: "organization",
                table: "support_teams",
                sql: "length(btrim(name)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_teams_department_id_normalized_name",
                schema: "organization",
                table: "support_teams");

            migrationBuilder.DropCheckConstraint(
                name: "ck_support_teams_name_not_blank",
                schema: "organization",
                table: "support_teams");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "organization",
                table: "support_teams");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "support_teams");

            migrationBuilder.DropColumn(
                name: "is_leader",
                schema: "organization",
                table: "support_team_members");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "organization",
                table: "support_team_members");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "support_team_members");

            migrationBuilder.CreateIndex(
                name: "ix_support_teams_department_id",
                schema: "organization",
                table: "support_teams",
                column: "department_id");
        }
    }
}
