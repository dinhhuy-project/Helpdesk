START TRANSACTION;

DROP TABLE identity.external_identities;

DROP TABLE communication.notifications;

DROP TABLE identity.refresh_tokens;

DROP TABLE identity.role_permissions;

DROP TABLE organization.support_team_members;

DROP TABLE helpdesk.ticket_activities;

DROP TABLE helpdesk.ticket_attachments;

DROP TABLE helpdesk.ticket_slas;

DROP TABLE identity.user_roles;

DROP TABLE identity.permissions;

DROP TABLE helpdesk.ticket_comments;

DROP TABLE helpdesk.sla_policies;

DROP TABLE identity.roles;

DROP TABLE helpdesk.tickets;

DROP TABLE organization.support_teams;

DROP TABLE helpdesk.ticket_categories;

DROP TABLE identity.users;

DROP TABLE organization.departments;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261022150705_InitialCreate';

COMMIT;

