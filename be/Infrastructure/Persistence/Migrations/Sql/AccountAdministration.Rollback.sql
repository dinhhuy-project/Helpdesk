START TRANSACTION;

DROP TABLE identity.identity_audit_logs;

DROP INDEX identity.ix_users_normalized_email;

ALTER TABLE identity.users DROP COLUMN normalized_email;

ALTER TABLE identity.roles DROP COLUMN updated_at;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261026134032_AccountAdministration';

COMMIT;

