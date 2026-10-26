START TRANSACTION;

DROP INDEX helpdesk.ix_ticket_categories_normalized_name;

DROP INDEX helpdesk.ix_ticket_categories_parent_id_normalized_name;

ALTER TABLE helpdesk.ticket_categories DROP CONSTRAINT ck_ticket_categories_name_not_blank;

ALTER TABLE helpdesk.ticket_categories DROP CONSTRAINT ck_ticket_categories_parent_not_self;

ALTER TABLE helpdesk.ticket_categories DROP COLUMN normalized_name;

CREATE INDEX ix_ticket_categories_parent_id ON helpdesk.ticket_categories (parent_id);

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261026151829_TicketCategoryAdministration';

COMMIT;

