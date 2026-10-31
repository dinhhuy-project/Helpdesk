START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE TABLE helpdesk.community_entries (
        id uuid NOT NULL,
        kind character varying(20) NOT NULL,
        title character varying(200) NOT NULL,
        summary character varying(1000) NOT NULL,
        content character varying(30000) NOT NULL,
        topic character varying(100) NOT NULL,
        status character varying(30),
        source_ticket_id uuid,
        created_by_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        is_published boolean NOT NULL,
        public_title character varying(200),
        public_summary character varying(1000),
        public_content character varying(30000),
        public_topic character varying(100),
        public_status character varying(30),
        public_revision integer NOT NULL,
        published_at timestamp with time zone,
        CONSTRAINT pk_community_entries PRIMARY KEY (id),
        CONSTRAINT ck_community_entries_kind CHECK ((kind = 'Knowledge' AND status IS NULL) OR (kind = 'Incident' AND status IN ('Investigating','Identified','Monitoring','Resolved') AND status IS NOT NULL AND source_ticket_id IS NULL)),
        CONSTRAINT ck_community_entries_published CHECK (NOT is_published OR (public_revision > 0 AND published_at IS NOT NULL AND public_title IS NOT NULL AND public_summary IS NOT NULL AND public_content IS NOT NULL AND public_topic IS NOT NULL AND ((kind = 'Knowledge' AND public_status IS NULL) OR (kind = 'Incident' AND public_status IN ('Investigating','Identified','Monitoring','Resolved') AND public_status IS NOT NULL)))),
        CONSTRAINT fk_community_entries_tickets_source_ticket_id FOREIGN KEY (source_ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_community_entries_users_created_by_id FOREIGN KEY (created_by_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE TABLE helpdesk.incident_affected_users (
        entry_id uuid NOT NULL,
        user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_incident_affected_users PRIMARY KEY (entry_id, user_id),
        CONSTRAINT fk_incident_affected_users_community_entries_entry_id FOREIGN KEY (entry_id) REFERENCES helpdesk.community_entries (id) ON DELETE RESTRICT,
        CONSTRAINT fk_incident_affected_users_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE TABLE helpdesk.incident_updates (
        id uuid NOT NULL,
        entry_id uuid NOT NULL,
        message character varying(1000) NOT NULL,
        status character varying(30) NOT NULL,
        revision integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_incident_updates PRIMARY KEY (id),
        CONSTRAINT ck_incident_updates_status CHECK (status IN ('Investigating','Identified','Monitoring','Resolved')),
        CONSTRAINT fk_incident_updates_community_entries_entry_id FOREIGN KEY (entry_id) REFERENCES helpdesk.community_entries (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE TABLE helpdesk.knowledge_feedback (
        entry_id uuid NOT NULL,
        user_id uuid NOT NULL,
        helpful boolean NOT NULL,
        solved boolean NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_knowledge_feedback PRIMARY KEY (entry_id, user_id),
        CONSTRAINT ck_knowledge_feedback_solved CHECK (NOT solved OR helpful),
        CONSTRAINT fk_knowledge_feedback_community_entries_entry_id FOREIGN KEY (entry_id) REFERENCES helpdesk.community_entries (id) ON DELETE RESTRICT,
        CONSTRAINT fk_knowledge_feedback_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_community_entries_created_by_id ON helpdesk.community_entries (created_by_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_community_entries_kind_published_at_id ON helpdesk.community_entries (kind, published_at, id) WHERE is_published;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_community_entries_kind_updated_at_id ON helpdesk.community_entries (kind, updated_at, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_community_entries_public_title_public_summary_public_conten ON helpdesk.community_entries USING GIN (to_tsvector('simple', coalesce(public_title, '') || ' ' || coalesce(public_summary, '') || ' ' || coalesce(public_content, '') || ' ' || coalesce(public_topic, ''))) WHERE is_published;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_community_entries_source_ticket_id ON helpdesk.community_entries (source_ticket_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_incident_affected_users_user_id ON helpdesk.incident_affected_users (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE UNIQUE INDEX ix_incident_updates_entry_id_revision ON helpdesk.incident_updates (entry_id, revision);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    CREATE INDEX ix_knowledge_feedback_user_id ON helpdesk.knowledge_feedback (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261031095438_PublicCommunity') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261031095438_PublicCommunity', '8.0.32');
    END IF;
END $EF$;
COMMIT;

