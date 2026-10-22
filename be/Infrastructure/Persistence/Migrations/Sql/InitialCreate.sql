DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'helpdesk') THEN
        CREATE SCHEMA helpdesk;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS helpdesk.__ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'organization') THEN
            CREATE SCHEMA organization;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
            CREATE SCHEMA identity;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'communication') THEN
            CREATE SCHEMA communication;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'helpdesk') THEN
            CREATE SCHEMA helpdesk;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE organization.departments (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        description character varying(2000),
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_departments PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.permissions (
        id uuid NOT NULL,
        code character varying(150) NOT NULL,
        description character varying(1000),
        CONSTRAINT pk_permissions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.roles (
        id uuid NOT NULL,
        name character varying(100) NOT NULL,
        description character varying(1000),
        CONSTRAINT pk_roles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.sla_policies (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        priority character varying(20) NOT NULL,
        first_response_minutes integer NOT NULL,
        resolution_minutes integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_sla_policies PRIMARY KEY (id),
        CONSTRAINT ck_sla_policies_duration CHECK (first_response_minutes > 0 AND resolution_minutes > 0),
        CONSTRAINT ck_sla_policies_priority CHECK (priority IN ('Low', 'Normal', 'High', 'Urgent'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.ticket_categories (
        id uuid NOT NULL,
        parent_id uuid,
        name character varying(200) NOT NULL,
        description character varying(2000),
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_ticket_categories PRIMARY KEY (id),
        CONSTRAINT fk_ticket_categories_ticket_categories_parent_id FOREIGN KEY (parent_id) REFERENCES helpdesk.ticket_categories (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE organization.support_teams (
        id uuid NOT NULL,
        department_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        description character varying(2000),
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_support_teams PRIMARY KEY (id),
        CONSTRAINT fk_support_teams_departments_department_id FOREIGN KEY (department_id) REFERENCES organization.departments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.users (
        id uuid NOT NULL,
        email character varying(320) NOT NULL,
        display_name character varying(200) NOT NULL,
        password_hash character varying(1024),
        avatar_url character varying(2048),
        department_id uuid,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_users PRIMARY KEY (id),
        CONSTRAINT fk_users_departments_department_id FOREIGN KEY (department_id) REFERENCES organization.departments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.role_permissions (
        role_id uuid NOT NULL,
        permission_id uuid NOT NULL,
        CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
        CONSTRAINT fk_role_permissions_permissions_permission_id FOREIGN KEY (permission_id) REFERENCES identity.permissions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_role_permissions_roles_role_id FOREIGN KEY (role_id) REFERENCES identity.roles (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.external_identities (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        provider character varying(100) NOT NULL,
        subject character varying(255) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_external_identities PRIMARY KEY (id),
        CONSTRAINT fk_external_identities_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.refresh_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(512) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        replaced_by_token_id uuid,
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_refresh_tokens_replaced_by_token_id FOREIGN KEY (replaced_by_token_id) REFERENCES identity.refresh_tokens (id) ON DELETE RESTRICT,
        CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE organization.support_team_members (
        support_team_id uuid NOT NULL,
        user_id uuid NOT NULL,
        joined_at timestamp with time zone NOT NULL,
        is_active boolean NOT NULL,
        CONSTRAINT pk_support_team_members PRIMARY KEY (support_team_id, user_id),
        CONSTRAINT fk_support_team_members_support_teams_support_team_id FOREIGN KEY (support_team_id) REFERENCES organization.support_teams (id) ON DELETE RESTRICT,
        CONSTRAINT fk_support_team_members_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.tickets (
        id uuid NOT NULL,
        ticket_number bigint GENERATED ALWAYS AS IDENTITY,
        title character varying(300) NOT NULL,
        description text NOT NULL,
        requester_id uuid NOT NULL,
        created_by_user_id uuid NOT NULL,
        category_id uuid NOT NULL,
        assigned_team_id uuid,
        assigned_agent_id uuid,
        status character varying(30) NOT NULL,
        priority character varying(20) NOT NULL,
        resolution_summary text,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        resolved_at timestamp with time zone,
        closed_at timestamp with time zone,
        CONSTRAINT pk_tickets PRIMARY KEY (id),
        CONSTRAINT ck_tickets_assigned_team CHECK (assigned_agent_id IS NULL OR assigned_team_id IS NOT NULL),
        CONSTRAINT ck_tickets_closed_at CHECK (status <> 'Closed' OR closed_at IS NOT NULL),
        CONSTRAINT ck_tickets_priority CHECK (priority IN ('Low', 'Normal', 'High', 'Urgent')),
        CONSTRAINT ck_tickets_resolution CHECK (status NOT IN ('Resolved', 'Closed') OR (resolution_summary IS NOT NULL AND length(btrim(resolution_summary)) > 0 AND resolved_at IS NOT NULL)),
        CONSTRAINT ck_tickets_status CHECK (status IN ('New', 'InProgress', 'WaitingForRequester', 'Resolved', 'Closed', 'Cancelled', 'Reopened')),
        CONSTRAINT fk_tickets_support_teams_assigned_team_id FOREIGN KEY (assigned_team_id) REFERENCES organization.support_teams (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tickets_ticket_categories_category_id FOREIGN KEY (category_id) REFERENCES helpdesk.ticket_categories (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tickets_users_assigned_agent_id FOREIGN KEY (assigned_agent_id) REFERENCES identity.users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tickets_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tickets_users_requester_id FOREIGN KEY (requester_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE identity.user_roles (
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        CONSTRAINT pk_user_roles PRIMARY KEY (user_id, role_id),
        CONSTRAINT fk_user_roles_roles_role_id FOREIGN KEY (role_id) REFERENCES identity.roles (id) ON DELETE RESTRICT,
        CONSTRAINT fk_user_roles_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE communication.notifications (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        ticket_id uuid,
        title character varying(300) NOT NULL,
        content text NOT NULL,
        created_at timestamp with time zone NOT NULL,
        read_at timestamp with time zone,
        CONSTRAINT pk_notifications PRIMARY KEY (id),
        CONSTRAINT fk_notifications_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_notifications_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.ticket_activities (
        id uuid NOT NULL,
        ticket_id uuid NOT NULL,
        actor_id uuid,
        action character varying(100) NOT NULL,
        old_value jsonb,
        new_value jsonb,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_ticket_activities PRIMARY KEY (id),
        CONSTRAINT fk_ticket_activities_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_ticket_activities_users_actor_id FOREIGN KEY (actor_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.ticket_comments (
        id uuid NOT NULL,
        ticket_id uuid NOT NULL,
        author_id uuid NOT NULL,
        content text NOT NULL,
        is_internal boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_ticket_comments PRIMARY KEY (id),
        CONSTRAINT ak_ticket_comments_id_ticket_id UNIQUE (id, ticket_id),
        CONSTRAINT fk_ticket_comments_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_ticket_comments_users_author_id FOREIGN KEY (author_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.ticket_slas (
        ticket_id uuid NOT NULL,
        sla_policy_id uuid NOT NULL,
        applied_at timestamp with time zone NOT NULL,
        first_response_due_at timestamp with time zone NOT NULL,
        resolution_due_at timestamp with time zone NOT NULL,
        first_responded_at timestamp with time zone,
        resolved_at timestamp with time zone,
        CONSTRAINT pk_ticket_slas PRIMARY KEY (ticket_id),
        CONSTRAINT ck_ticket_slas_deadlines CHECK (first_response_due_at >= applied_at AND resolution_due_at >= applied_at),
        CONSTRAINT fk_ticket_slas_sla_policies_sla_policy_id FOREIGN KEY (sla_policy_id) REFERENCES helpdesk.sla_policies (id) ON DELETE RESTRICT,
        CONSTRAINT fk_ticket_slas_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE TABLE helpdesk.ticket_attachments (
        id uuid NOT NULL,
        ticket_id uuid NOT NULL,
        comment_id uuid,
        uploaded_by_user_id uuid NOT NULL,
        bucket character varying(63) NOT NULL,
        object_key character varying(1024) NOT NULL,
        file_name character varying(255) NOT NULL,
        content_type character varying(255) NOT NULL,
        size_bytes bigint NOT NULL,
        is_internal boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_ticket_attachments PRIMARY KEY (id),
        CONSTRAINT ck_ticket_attachments_size CHECK (size_bytes >= 0),
        CONSTRAINT fk_ticket_attachments_ticket_comments_comment_id_ticket_id FOREIGN KEY (comment_id, ticket_id) REFERENCES helpdesk.ticket_comments (id, ticket_id) ON DELETE RESTRICT,
        CONSTRAINT fk_ticket_attachments_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES helpdesk.tickets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_ticket_attachments_users_uploaded_by_user_id FOREIGN KEY (uploaded_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_external_identities_provider_subject ON identity.external_identities (provider, subject);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_external_identities_user_id ON identity.external_identities (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_notifications_ticket_id ON communication.notifications (ticket_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_notifications_user_id_created_at ON communication.notifications (user_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_notifications_user_id_created_at1 ON communication.notifications (user_id, created_at) WHERE read_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_permissions_code ON identity.permissions (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_refresh_tokens_replaced_by_token_id ON identity.refresh_tokens (replaced_by_token_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON identity.refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_refresh_tokens_user_id_expires_at ON identity.refresh_tokens (user_id, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_role_permissions_permission_id ON identity.role_permissions (permission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_roles_name ON identity.roles (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_support_team_members_user_id ON organization.support_team_members (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_support_teams_department_id ON organization.support_teams (department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_activities_actor_id ON helpdesk.ticket_activities (actor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_activities_ticket_id_created_at ON helpdesk.ticket_activities (ticket_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_ticket_attachments_bucket_object_key ON helpdesk.ticket_attachments (bucket, object_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_attachments_comment_id_ticket_id ON helpdesk.ticket_attachments (comment_id, ticket_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_attachments_ticket_id ON helpdesk.ticket_attachments (ticket_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_attachments_uploaded_by_user_id ON helpdesk.ticket_attachments (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_categories_parent_id ON helpdesk.ticket_categories (parent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_comments_author_id ON helpdesk.ticket_comments (author_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_comments_ticket_id_created_at ON helpdesk.ticket_comments (ticket_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_slas_first_response_due_at ON helpdesk.ticket_slas (first_response_due_at) WHERE first_responded_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_slas_resolution_due_at ON helpdesk.ticket_slas (resolution_due_at) WHERE resolved_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_ticket_slas_sla_policy_id ON helpdesk.ticket_slas (sla_policy_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_assigned_agent_id_status_created_at ON helpdesk.tickets (assigned_agent_id, status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_assigned_team_id_status_created_at ON helpdesk.tickets (assigned_team_id, status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_category_id ON helpdesk.tickets (category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_created_by_user_id ON helpdesk.tickets (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_requester_id_created_at ON helpdesk.tickets (requester_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_tickets_status_created_at ON helpdesk.tickets (status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_tickets_ticket_number ON helpdesk.tickets (ticket_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_user_roles_role_id ON identity.user_roles (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE INDEX ix_users_department_id ON identity.users (department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_users_email ON identity.users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM helpdesk.__ef_migrations_history WHERE "migration_id" = '20261022150705_InitialCreate') THEN
    INSERT INTO helpdesk.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261022150705_InitialCreate', '8.0.32');
    END IF;
END $EF$;
COMMIT;

