START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE TABLE helpdesk.attachment_upload_intents (
        id uuid NOT NULL,
        ticket_id uuid NOT NULL,
        comment_id uuid,
        uploaded_by_user_id uuid NOT NULL,
        bucket character varying(63) NOT NULL,
        object_key character varying(1024) NOT NULL,
        file_name character varying(255) NOT NULL,
        content_type character varying(255) NOT NULL,
        size_bytes bigint NOT NULL,
        is_internal boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        is_deleting boolean NOT NULL,
        CONSTRAINT pk_attachment_upload_intents PRIMARY KEY (id),
        CONSTRAINT ck_attachment_upload_intents_expiry CHECK (expires_at > created_at),
        CONSTRAINT ck_attachment_upload_intents_size CHECK (size_bytes > 0 AND size_bytes <= 10485760),
        CONSTRAINT fk_attachment_upload_intents_ticket_comments_comment_id_ticket FOREIGN KEY (comment_id, ticket_id) REFERENCES helpdesk.ticket_comments (id, ticket_id) ON DELETE RESTRICT,
        CONSTRAINT fk_attachment_upload_intents_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_attachment_upload_intents_users_uploaded_by_user_id FOREIGN KEY (uploaded_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE UNIQUE INDEX ix_attachment_upload_intents_bucket_object_key ON helpdesk.attachment_upload_intents (bucket, object_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE INDEX ix_attachment_upload_intents_comment_id_ticket_id ON helpdesk.attachment_upload_intents (comment_id, ticket_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE INDEX ix_attachment_upload_intents_expires_at ON helpdesk.attachment_upload_intents (expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE INDEX ix_attachment_upload_intents_ticket_id_is_internal ON helpdesk.attachment_upload_intents (ticket_id, is_internal);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    CREATE INDEX ix_attachment_upload_intents_uploaded_by_user_id ON helpdesk.attachment_upload_intents (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026171234_AttachmentStorage') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261026171234_AttachmentStorage', '8.0.32');
    END IF;
END $EF$;
COMMIT;

