START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD first_response_breached_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD first_response_minutes integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD policy_name character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD priority character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD resolution_breached_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD resolution_minutes integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    CREATE INDEX ix_ticket_slas_first_response_due_at1 ON helpdesk.ticket_slas (first_response_due_at) WHERE first_response_breached_at IS NULL AND first_responded_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    CREATE INDEX ix_ticket_slas_resolution_due_at1 ON helpdesk.ticket_slas (resolution_due_at) WHERE resolution_breached_at IS NULL AND resolved_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.ticket_slas ADD CONSTRAINT ck_ticket_slas_snapshot CHECK ((policy_name IS NULL AND priority IS NULL AND first_response_minutes IS NULL AND resolution_minutes IS NULL) OR (policy_name IS NOT NULL AND priority IS NOT NULL AND first_response_minutes IS NOT NULL AND resolution_minutes IS NOT NULL AND length(btrim(policy_name)) > 0 AND priority IN ('Low','Normal','High','Urgent') AND first_response_minutes > 0 AND first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    CREATE UNIQUE INDEX ix_sla_policies_priority ON helpdesk.sla_policies (priority) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.sla_policies ADD CONSTRAINT ck_sla_policies_name CHECK (length(btrim(name)) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    ALTER TABLE helpdesk.sla_policies ADD CONSTRAINT ck_sla_policies_targets CHECK (first_response_minutes <= resolution_minutes AND resolution_minutes <= 525600);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261028122927_SlaMvp') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261028122927_SlaMvp', '8.0.32');
    END IF;
END $EF$;
COMMIT;

