START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM identity.users GROUP BY lower(btrim(email)) HAVING count(*) > 1) THEN
            RAISE EXCEPTION 'Duplicate normalized emails exist. Review conflicting accounts before applying AccountAdministration; no users were changed.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    ALTER TABLE identity.roles ADD updated_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    ALTER TABLE identity.users ADD normalized_email character varying(320) GENERATED ALWAYS AS (lower(btrim(email))) STORED NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE TABLE identity.identity_audit_logs (
        id uuid NOT NULL,
        actor_id uuid,
        target_user_id uuid,
        target_role_id uuid,
        action character varying(100) NOT NULL,
        data jsonb,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_identity_audit_logs PRIMARY KEY (id),
        CONSTRAINT fk_identity_audit_logs_roles_target_role_id FOREIGN KEY (target_role_id) REFERENCES identity.roles (id) ON DELETE RESTRICT,
        CONSTRAINT fk_identity_audit_logs_users_actor_id FOREIGN KEY (actor_id) REFERENCES identity.users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_identity_audit_logs_users_target_user_id FOREIGN KEY (target_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE UNIQUE INDEX ix_users_normalized_email ON identity.users (normalized_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE INDEX ix_identity_audit_logs_actor_id ON identity.identity_audit_logs (actor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE INDEX ix_identity_audit_logs_created_at ON identity.identity_audit_logs (created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE INDEX ix_identity_audit_logs_target_role_id ON identity.identity_audit_logs (target_role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    CREATE INDEX ix_identity_audit_logs_target_user_id ON identity.identity_audit_logs (target_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026134032_AccountAdministration') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261026134032_AccountAdministration', '8.0.32');
    END IF;
END $EF$;
COMMIT;

