using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM organization.departments WHERE length(btrim(name)) = 0) THEN
                        RAISE EXCEPTION 'Blank department names exist. Review departments before applying DepartmentAdministration; no departments were changed.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM organization.departments GROUP BY lower(btrim(name)) HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate normalized department names exist. Review departments before applying DepartmentAdministration; no departments were changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "departments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "organization",
                table: "departments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "lower(btrim(name))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_normalized_name",
                schema: "organization",
                table: "departments",
                column: "normalized_name",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_departments_name_not_blank",
                schema: "organization",
                table: "departments",
                sql: "length(btrim(name)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_departments_normalized_name",
                schema: "organization",
                table: "departments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_departments_name_not_blank",
                schema: "organization",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "organization",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "departments");
        }
    }
}
