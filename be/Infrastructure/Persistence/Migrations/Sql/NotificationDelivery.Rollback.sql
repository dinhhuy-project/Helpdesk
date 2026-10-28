START TRANSACTION;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM communication.notifications WHERE ticket_activity_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Preserve or archive managed notifications before rollback; activity visibility and delivery state must not be discarded.';
    END IF;
END $$;

ALTER TABLE communication.notifications DROP CONSTRAINT fk_notifications_ticket_activities_ticket_activity_id;

DROP INDEX communication.ix_notifications_dispatch_lease_until_created_at;

DROP INDEX communication.ix_notifications_ticket_activity_id;

DROP INDEX communication.ix_notifications_user_id_ticket_activity_id;

ALTER TABLE communication.notifications DROP COLUMN dispatch_claim_id;

ALTER TABLE communication.notifications DROP COLUMN dispatch_lease_until;

ALTER TABLE communication.notifications DROP COLUMN dispatched_at;

ALTER TABLE communication.notifications DROP COLUMN ticket_activity_id;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261028115316_NotificationDelivery';

COMMIT;

