using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeRetrieval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL-owned expression index: no new business entity, persisted corpus or external extension.
            migrationBuilder.Sql("""
                CREATE FUNCTION helpdesk.knowledge_search_normalize(value text) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE SET search_path = pg_catalog
                AS $fn$
                    SELECT btrim(regexp_replace(
                        regexp_replace(
                            regexp_replace(lower(translate(normalize(coalesce(value, ''), NFD), 'Đđ', 'Dd') COLLATE "C"),
                                           U&'[\0300-\036f]', '', 'g'),
                            '[^a-z0-9]+', ' ', 'g'),
                        ' +', ' ', 'g'))
                $fn$;

                CREATE FUNCTION helpdesk.knowledge_search_vector(title text, summary text, content text, topic text, guide jsonb) RETURNS tsvector
                LANGUAGE sql IMMUTABLE PARALLEL SAFE SET search_path = pg_catalog
                AS $fn$
                    SELECT setweight(to_tsvector('simple', helpdesk.knowledge_search_normalize(title)), 'A') ||
                           setweight(to_tsvector('simple', helpdesk.knowledge_search_normalize(summary || ' ' || topic)), 'B') ||
                           setweight(to_tsvector('simple', helpdesk.knowledge_search_normalize(content)), 'C') ||
                           setweight(to_tsvector('simple', helpdesk.knowledge_search_normalize(
                               coalesce(guide->>'applicability', '') || ' ' || coalesce(guide->>'prerequisites', '') || ' ' ||
                               coalesce(guide->>'expectedOutcome', '') || ' ' || coalesce(guide->>'escalation', ''))), 'B')
                $fn$;

                CREATE INDEX ix_community_knowledge_retrieval ON helpdesk.community_entries
                USING gin (helpdesk.knowledge_search_vector(public_title, public_summary, public_content, public_topic, public_guide))
                WHERE kind = 'Knowledge' AND is_published AND public_classification = 'Operational' AND public_guide IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX helpdesk.ix_community_knowledge_retrieval;
                DROP FUNCTION helpdesk.knowledge_search_vector(text, text, text, text, jsonb);
                DROP FUNCTION helpdesk.knowledge_search_normalize(text);
                """);
        }
    }
}
