START TRANSACTION;

UPDATE helpdesk.community_entries SET is_published = false WHERE is_published AND public_classification <> 'Operational';

ALTER TABLE helpdesk.community_entries DROP CONSTRAINT ck_community_entries_classification;

ALTER TABLE helpdesk.community_entries DROP CONSTRAINT ck_community_entries_guide;

ALTER TABLE helpdesk.community_entries DROP COLUMN classification;

ALTER TABLE helpdesk.community_entries DROP COLUMN guide;

ALTER TABLE helpdesk.community_entries DROP COLUMN public_classification;

ALTER TABLE helpdesk.community_entries DROP COLUMN public_guide;

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261031160913_KnowledgeStandardization';

COMMIT;

