using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeStandardization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "classification",
                schema: "helpdesk",
                table: "community_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unclassified");

            migrationBuilder.AddColumn<string>(
                name: "guide",
                schema: "helpdesk",
                table: "community_entries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_classification",
                schema: "helpdesk",
                table: "community_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unclassified");

            migrationBuilder.AddColumn<string>(
                name: "public_guide",
                schema: "helpdesk",
                table: "community_entries",
                type: "jsonb",
                nullable: true);

            // Only deterministic community-demo-v1 records are classified automatically.
            // Every other existing draft/snapshot remains Unclassified pending human review.
            for (var index = 0; index < 6; index++)
            {
                var id = DemoId($"article:{index}");
                migrationBuilder.Sql($"""
                    UPDATE helpdesk.community_entries
                    SET classification = 'Demo', public_classification = 'Demo',
                        guide = jsonb_build_object(
                            'applicability', 'Tình huống minh họa; đối chiếu hệ thống của đơn vị trước khi áp dụng.',
                            'prerequisites', 'Chỉ thực hành trên môi trường thử nghiệm, không chia sẻ bí mật.',
                            'expectedOutcome', 'Hiểu quy trình tự kiểm tra và cách yêu cầu hỗ trợ.',
                            'escalation', 'Đăng nhập Helpdesk và tạo Ticket nếu cần hỗ trợ trong phạm vi được phép.',
                            'sourceName', 'Bộ mẫu community-demo-v1, biên soạn phục vụ nghiệm thu',
                            'sourceUrl', NULL, 'sourceUpdatedOn', NULL),
                        public_guide = jsonb_build_object(
                            'applicability', 'Tình huống minh họa; đối chiếu hệ thống của đơn vị trước khi áp dụng.',
                            'prerequisites', 'Chỉ thực hành trên môi trường thử nghiệm, không chia sẻ bí mật.',
                            'expectedOutcome', 'Hiểu quy trình tự kiểm tra và cách yêu cầu hỗ trợ.',
                            'escalation', 'Đăng nhập Helpdesk và tạo Ticket nếu cần hỗ trợ trong phạm vi được phép.',
                            'sourceName', 'Bộ mẫu community-demo-v1, biên soạn phục vụ nghiệm thu',
                            'sourceUrl', NULL, 'sourceUpdatedOn', NULL)
                    WHERE id = '{id}' AND kind = 'Knowledge';
                    """);
            }
            foreach (var key in new[] { "active", "resolved" })
                migrationBuilder.Sql($"UPDATE helpdesk.community_entries SET classification = 'Demo', public_classification = 'Demo' WHERE id = '{DemoId("incident:" + key)}' AND kind = 'Incident';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_community_entries_classification",
                schema: "helpdesk",
                table: "community_entries",
                sql: "classification IN ('Unclassified','Operational','Demo') AND public_classification IN ('Unclassified','Operational','Demo')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_community_entries_guide",
                schema: "helpdesk",
                table: "community_entries",
                sql: "(guide IS NULL OR jsonb_typeof(guide) = 'object') AND (public_guide IS NULL OR jsonb_typeof(public_guide) = 'object') AND (kind = 'Knowledge' OR (guide IS NULL AND public_guide IS NULL)) AND (NOT is_published OR public_classification = 'Unclassified' OR kind <> 'Knowledge' OR public_guide IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Older code has no dataset filter. Keep demo/unclassified snapshots private on rollback.
            migrationBuilder.Sql("UPDATE helpdesk.community_entries SET is_published = false WHERE is_published AND public_classification <> 'Operational';");
            migrationBuilder.DropCheckConstraint(
                name: "ck_community_entries_classification",
                schema: "helpdesk",
                table: "community_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_community_entries_guide",
                schema: "helpdesk",
                table: "community_entries");

            migrationBuilder.DropColumn(
                name: "classification",
                schema: "helpdesk",
                table: "community_entries");

            migrationBuilder.DropColumn(
                name: "guide",
                schema: "helpdesk",
                table: "community_entries");

            migrationBuilder.DropColumn(
                name: "public_classification",
                schema: "helpdesk",
                table: "community_entries");

            migrationBuilder.DropColumn(
                name: "public_guide",
                schema: "helpdesk",
                table: "community_entries");
        }

        private static Guid DemoId(string key) => new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("community-demo-v1:" + key))[..16]);
    }
}
