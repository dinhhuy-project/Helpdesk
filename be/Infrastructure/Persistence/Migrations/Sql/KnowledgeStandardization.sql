START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD classification character varying(20) NOT NULL DEFAULT 'Unclassified';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD guide jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD public_classification character varying(20) NOT NULL DEFAULT 'Unclassified';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD public_guide jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = 'ab671fb0-959d-5f20-b3a2-6e5f2385a2b9' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = '1e6810f9-aa15-1eb3-cc2e-d50a9e9c9c77' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = '61365a03-5bfe-6d4c-0068-8fd102ba86a7' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = 'ee5f65b3-7004-ccbf-cca1-59989e501f17' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = '2e6b3df3-bdb5-409f-cfc8-d44591e1096d' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
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
    WHERE id = 'f106a7d6-92b5-a1d8-7dc4-170ff3ec58f8' AND kind = 'Knowledge';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    UPDATE helpdesk.community_entries SET classification = 'Demo', public_classification = 'Demo' WHERE id = '99775243-ca08-b128-c4fd-36497bbe114b' AND kind = 'Incident';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    UPDATE helpdesk.community_entries SET classification = 'Demo', public_classification = 'Demo' WHERE id = 'f52ec1e1-28ec-4393-6928-a96d3367a776' AND kind = 'Incident';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD CONSTRAINT ck_community_entries_classification CHECK (classification IN ('Unclassified','Operational','Demo') AND public_classification IN ('Unclassified','Operational','Demo'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    ALTER TABLE helpdesk.community_entries ADD CONSTRAINT ck_community_entries_guide CHECK ((guide IS NULL OR jsonb_typeof(guide) = 'object') AND (public_guide IS NULL OR jsonb_typeof(public_guide) = 'object') AND (kind = 'Knowledge' OR (guide IS NULL AND public_guide IS NULL)) AND (NOT is_published OR public_classification = 'Unclassified' OR kind <> 'Knowledge' OR public_guide IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031160913_KnowledgeStandardization') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261031160913_KnowledgeStandardization', '8.0.32');
    END IF;
END $EF$;
COMMIT;

