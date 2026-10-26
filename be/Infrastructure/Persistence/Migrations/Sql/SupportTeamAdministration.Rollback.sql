START TRANSACTION;

DROP INDEX organization.ix_support_teams_department_id_normalized_name;

ALTER TABLE organization.support_teams DROP CONSTRAINT ck_support_teams_name_not_blank;

ALTER TABLE organization.support_teams DROP COLUMN normalized_name;

ALTER TABLE organization.support_team_members DROP COLUMN is_leader;

ALTER TABLE organization.support_team_members DROP COLUMN updated_at;

CREATE INDEX ix_support_teams_department_id ON organization.support_teams (department_id);

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261026145925_SupportTeamAdministration';

COMMIT;

