START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM organization.support_teams WHERE length(btrim(name)) = 0) THEN
            RAISE EXCEPTION 'Blank support team names exist. Review teams before applying SupportTeamAdministration; no teams were changed.';
        END IF;
        IF EXISTS (SELECT 1 FROM organization.support_teams GROUP BY department_id, lower(btrim(name)) HAVING count(*) > 1) THEN
            RAISE EXCEPTION 'Duplicate normalized team names exist within a department. Review teams before applying SupportTeamAdministration; no teams were changed.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    DROP INDEX organization.ix_support_teams_department_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    ALTER TABLE organization.support_team_members ADD is_leader boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    ALTER TABLE organization.support_team_members ADD updated_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    ALTER TABLE organization.support_teams ADD normalized_name character varying(200) GENERATED ALWAYS AS (lower(btrim(name))) STORED NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    CREATE UNIQUE INDEX ix_support_teams_department_id_normalized_name ON organization.support_teams (department_id, normalized_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    ALTER TABLE organization.support_teams ADD CONSTRAINT ck_support_teams_name_not_blank CHECK (length(btrim(name)) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026145925_SupportTeamAdministration') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261026145925_SupportTeamAdministration', '8.0.32');
    END IF;
END $EF$;
COMMIT;

