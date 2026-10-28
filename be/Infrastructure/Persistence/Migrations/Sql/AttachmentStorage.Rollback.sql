START TRANSACTION;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM helpdesk.attachment_upload_intents) THEN
        RAISE EXCEPTION 'Drain attachment upload intents before rollback; their objects may still need cleanup.';
    END IF;
END $$;

DROP TABLE helpdesk.attachment_upload_intents;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261026171234_AttachmentStorage';

COMMIT;

