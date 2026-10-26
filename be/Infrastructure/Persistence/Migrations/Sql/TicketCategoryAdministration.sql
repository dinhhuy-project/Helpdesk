START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM helpdesk.ticket_categories WHERE length(btrim(name)) = 0) THEN
            RAISE EXCEPTION 'Blank category names exist. Review categories before applying TicketCategoryAdministration; no categories were changed.';
        END IF;
        IF EXISTS (SELECT 1 FROM helpdesk.ticket_categories GROUP BY parent_id, lower(btrim(name)) HAVING count(*) > 1) THEN
            RAISE EXCEPTION 'Duplicate normalized category names exist among siblings. Review categories before applying TicketCategoryAdministration; no categories were changed.';
        END IF;
        IF EXISTS (
            WITH RECURSIVE reachable(id) AS (
                SELECT id FROM helpdesk.ticket_categories WHERE parent_id IS NULL
                UNION
                SELECT child.id FROM helpdesk.ticket_categories child JOIN reachable parent ON child.parent_id = parent.id
            ) SELECT 1 FROM helpdesk.ticket_categories WHERE id NOT IN (SELECT id FROM reachable)
        ) THEN
            RAISE EXCEPTION 'Category cycles or unreachable parents exist. Review the tree before applying TicketCategoryAdministration; no categories were changed.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    DROP INDEX helpdesk.ix_ticket_categories_parent_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    ALTER TABLE helpdesk.ticket_categories ADD normalized_name character varying(200) GENERATED ALWAYS AS (lower(btrim(name))) STORED NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    CREATE UNIQUE INDEX ix_ticket_categories_normalized_name ON helpdesk.ticket_categories (normalized_name) WHERE parent_id IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    CREATE UNIQUE INDEX ix_ticket_categories_parent_id_normalized_name ON helpdesk.ticket_categories (parent_id, normalized_name) WHERE parent_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    ALTER TABLE helpdesk.ticket_categories ADD CONSTRAINT ck_ticket_categories_name_not_blank CHECK (length(btrim(name)) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    ALTER TABLE helpdesk.ticket_categories ADD CONSTRAINT ck_ticket_categories_parent_not_self CHECK (parent_id IS NULL OR parent_id <> id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261026151829_TicketCategoryAdministration') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261026151829_TicketCategoryAdministration', '8.0.32');
    END IF;
END $EF$;
COMMIT;

