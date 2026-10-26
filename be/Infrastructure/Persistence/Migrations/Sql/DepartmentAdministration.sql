START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026143539_DepartmentAdministration') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM organization.departments WHERE length(btrim(name)) = 0) THEN
            RAISE EXCEPTION 'Blank department names exist. Review departments before applying DepartmentAdministration; no departments were changed.';
        END IF;
        IF EXISTS (SELECT 1 FROM organization.departments GROUP BY lower(btrim(name)) HAVING count(*) > 1) THEN
            RAISE EXCEPTION 'Duplicate normalized department names exist. Review departments before applying DepartmentAdministration; no departments were changed.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026143539_DepartmentAdministration') THEN
    ALTER TABLE organization.departments ADD normalized_name character varying(200) GENERATED ALWAYS AS (lower(btrim(name))) STORED NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026143539_DepartmentAdministration') THEN
    CREATE UNIQUE INDEX ix_departments_normalized_name ON organization.departments (normalized_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026143539_DepartmentAdministration') THEN
    ALTER TABLE organization.departments ADD CONSTRAINT ck_departments_name_not_blank CHECK (length(btrim(name)) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026143539_DepartmentAdministration') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261026143539_DepartmentAdministration', '8.0.32');
    END IF;
END $EF$;
COMMIT;

