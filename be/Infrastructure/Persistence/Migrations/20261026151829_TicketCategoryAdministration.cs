using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketCategoryAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM helpdesk.ticket_categories WHERE length(btrim(name)) = 0) THEN
                        RAISE EXCEPTION 'Blank category names exist. Review categories before applying TicketCategoryAdministration; no categories were changed.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM helpdesk.ticket_categories GROUP BY parent_id, lower(btrim(name)) HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate normalized category names exist among siblings. Review categories before applying TicketCategoryAdministration; no categories were changed.';
                    END IF;
                    IF EXISTS (
                        WITH RECURSIVE reachable(id) AS (
                            SELECT id FROM helpdesk.ticket_categories WHERE parent_id IS NULL
                            UNION
                            SELECT child.id FROM helpdesk.ticket_categories child JOIN reachable parent ON child.parent_id = parent.id
                        ) SELECT 1 FROM helpdesk.ticket_categories WHERE id NOT IN (SELECT id FROM reachable)
                    ) THEN
                        RAISE EXCEPTION 'Category cycles or unreachable parents exist. Review the tree before applying TicketCategoryAdministration; no categories were changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "ix_ticket_categories_parent_id",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "helpdesk",
                table: "ticket_categories",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "helpdesk",
                table: "ticket_categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "lower(btrim(name))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_categories_normalized_name",
                schema: "helpdesk",
                table: "ticket_categories",
                column: "normalized_name",
                unique: true,
                filter: "parent_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_categories_parent_id_normalized_name",
                schema: "helpdesk",
                table: "ticket_categories",
                columns: new[] { "parent_id", "normalized_name" },
                unique: true,
                filter: "parent_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ticket_categories_name_not_blank",
                schema: "helpdesk",
                table: "ticket_categories",
                sql: "length(btrim(name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ticket_categories_parent_not_self",
                schema: "helpdesk",
                table: "ticket_categories",
                sql: "parent_id IS NULL OR parent_id <> id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_categories_normalized_name",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.DropIndex(
                name: "ix_ticket_categories_parent_id_normalized_name",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ticket_categories_name_not_blank",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ticket_categories_parent_not_self",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "helpdesk",
                table: "ticket_categories");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_categories_parent_id",
                schema: "helpdesk",
                table: "ticket_categories",
                column: "parent_id");
        }
    }
}
