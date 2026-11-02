START TRANSACTION;

DROP INDEX helpdesk.ix_community_knowledge_retrieval;
DROP FUNCTION helpdesk.knowledge_search_vector(text, text, text, text, jsonb);
DROP FUNCTION helpdesk.knowledge_search_normalize(text);

DELETE FROM helpdesk.__ef_migrations_history
WHERE migration_id = '20261031163221_KnowledgeRetrieval';

COMMIT;

