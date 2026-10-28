using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_claim_id",
                schema: "communication",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "dispatch_lease_until",
                schema: "communication",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "dispatched_at",
                schema: "communication",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ticket_activity_id",
                schema: "communication",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_dispatch_lease_until_created_at",
                schema: "communication",
                table: "notifications",
                columns: new[] { "dispatch_lease_until", "created_at" },
                filter: "ticket_activity_id IS NOT NULL AND dispatched_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_ticket_activity_id",
                schema: "communication",
                table: "notifications",
                column: "ticket_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_ticket_activity_id",
                schema: "communication",
                table: "notifications",
                columns: new[] { "user_id", "ticket_activity_id" },
                unique: true,
                filter: "ticket_activity_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_ticket_activities_ticket_activity_id",
                schema: "communication",
                table: "notifications",
                column: "ticket_activity_id",
                principalSchema: "helpdesk",
                principalTable: "ticket_activities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM communication.notifications WHERE ticket_activity_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Preserve or archive managed notifications before rollback; activity visibility and delivery state must not be discarded.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_notifications_ticket_activities_ticket_activity_id",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_dispatch_lease_until_created_at",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_ticket_activity_id",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_ticket_activity_id",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dispatch_claim_id",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dispatch_lease_until",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dispatched_at",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ticket_activity_id",
                schema: "communication",
                table: "notifications");
        }
    }
}
