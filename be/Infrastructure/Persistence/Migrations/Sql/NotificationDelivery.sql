START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    ALTER TABLE communication.notifications ADD dispatch_claim_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    ALTER TABLE communication.notifications ADD dispatch_lease_until timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    ALTER TABLE communication.notifications ADD dispatched_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    ALTER TABLE communication.notifications ADD ticket_activity_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    CREATE INDEX ix_notifications_dispatch_lease_until_created_at ON communication.notifications (dispatch_lease_until, created_at) WHERE ticket_activity_id IS NOT NULL AND dispatched_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    CREATE INDEX ix_notifications_ticket_activity_id ON communication.notifications (ticket_activity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    CREATE UNIQUE INDEX ix_notifications_user_id_ticket_activity_id ON communication.notifications (user_id, ticket_activity_id) WHERE ticket_activity_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    ALTER TABLE communication.notifications ADD CONSTRAINT fk_notifications_ticket_activities_ticket_activity_id FOREIGN KEY (ticket_activity_id) REFERENCES helpdesk.ticket_activities (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028115316_NotificationDelivery') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261028115316_NotificationDelivery', '8.0.32');
    END IF;
END $EF$;
COMMIT;

