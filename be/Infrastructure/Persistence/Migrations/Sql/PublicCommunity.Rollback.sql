START TRANSACTION;

DROP TABLE helpdesk.incident_affected_users;

DROP TABLE helpdesk.incident_updates;

DROP TABLE helpdesk.knowledge_feedback;

DROP TABLE helpdesk.community_entries;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261031095438_PublicCommunity';

COMMIT;

