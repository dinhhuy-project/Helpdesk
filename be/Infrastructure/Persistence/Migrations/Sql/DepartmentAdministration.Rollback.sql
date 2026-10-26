START TRANSACTION;

DROP INDEX organization.ix_departments_normalized_name;

ALTER TABLE organization.departments DROP CONSTRAINT ck_departments_name_not_blank;

ALTER TABLE organization.departments DROP COLUMN normalized_name;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261026143539_DepartmentAdministration';

COMMIT;

