START TRANSACTION;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM helpdesk.ticket_slas WHERE policy_name IS NOT NULL
        OR first_response_breached_at IS NOT NULL OR resolution_breached_at IS NOT NULL) THEN
        RAISE EXCEPTION 'Preserve SLA snapshots and breach history before rollback.';
    END IF;
END $$;

DROP INDEX helpdesk.ix_ticket_slas_first_response_due_at1;

DROP INDEX helpdesk.ix_ticket_slas_resolution_due_at1;

ALTER TABLE helpdesk.ticket_slas DROP CONSTRAINT ck_ticket_slas_snapshot;

DROP INDEX helpdesk.ix_sla_policies_priority;

ALTER TABLE helpdesk.sla_policies DROP CONSTRAINT ck_sla_policies_name;

ALTER TABLE helpdesk.sla_policies DROP CONSTRAINT ck_sla_policies_targets;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN first_response_breached_at;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN first_response_minutes;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN policy_name;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN priority;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN resolution_breached_at;

ALTER TABLE helpdesk.ticket_slas DROP COLUMN resolution_minutes;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261028122927_SlaMvp';

COMMIT;

