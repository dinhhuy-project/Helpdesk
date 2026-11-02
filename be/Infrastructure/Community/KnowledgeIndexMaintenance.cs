using System.Data;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Community;

// The GIN index is a transactional projection of the public snapshot, not an external corpus.
public sealed record KnowledgeIndexDocument(Guid EntryId, int Revision, string Fingerprint);
public sealed record KnowledgeIndexSnapshot(int Format, string Pipeline, string DatabaseName,
    DateTime ObservedAt, string IndexName, bool Healthy, IReadOnlyList<KnowledgeIndexDocument> Documents);

public sealed class KnowledgeIndexMaintenance(HelpdeskDbContext database, TimeProvider clock)
{
    public const string IndexName = "helpdesk.ix_community_knowledge_retrieval";
    public const int MaximumDocuments = 100_000;
    private const string Expression = "helpdesk.knowledge_search_vector((public_title)::text, (public_summary)::text, (public_content)::text, (public_topic)::text, public_guide)";
    private const string Predicate = "(((kind)::text = 'Knowledge'::text) AND is_published AND ((public_classification)::text = 'Operational'::text) AND (public_guide IS NOT NULL))";

    public async Task<KnowledgeIndexSnapshot> InspectAsync(bool rebuild, CancellationToken ct)
    {
        var previousTimeout = database.Database.GetCommandTimeout();
        database.Database.SetCommandTimeout(130);
        try { return await InspectCoreAsync(rebuild, ct); }
        finally { database.Database.SetCommandTimeout(previousTimeout); }
    }

    private async Task<KnowledgeIndexSnapshot> InspectCoreAsync(bool rebuild, CancellationToken ct)
    {
        // Never migrate, publish or seed from maintenance commands.
        if ((await database.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Apply reviewed migrations before checking the knowledge index.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await database.Database.ExecuteSqlRawAsync("SET LOCAL search_path = pg_catalog; SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '120s';", ct);
        if (rebuild)
        {
            // Same lock as community publication. Try rather than queue behind another operator.
            var acquired = await database.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(734205001) AS \"Value\"").SingleAsync(ct);
            if (!acquired) throw new InvalidOperationException("Community publication or index maintenance is busy; retry later.");
        }
        const string stateSql = """
            SELECT i.indisvalid AS valid, i.indisready AS ready, i.indislive AS live,
                   a.amname::text AS method, t.relname::text AS table_name, n.nspname::text AS table_schema,
                   pg_catalog.pg_get_expr(i.indexprs,i.indrelid) AS expression,
                   pg_catalog.pg_get_expr(i.indpred,i.indrelid) AS predicate
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid=i.indexrelid
            JOIN pg_catalog.pg_am a ON a.oid=c.relam
            JOIN pg_catalog.pg_class t ON t.oid=i.indrelid
            JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            WHERE i.indexrelid=pg_catalog.to_regclass('helpdesk.ix_community_knowledge_retrieval')
            """;
        var index = await database.Database.SqlQueryRaw<IndexState>(stateSql).SingleOrDefaultAsync(ct);
        var definitionMatches = index is { Method: "gin", TableName: "community_entries", TableSchema: "helpdesk" }
            && index.Expression == Expression && index.Predicate == Predicate;
        if (rebuild)
        {
            if (!definitionMatches) throw new InvalidOperationException("Index is missing or its definition differs from the reviewed migration; repair the schema through review first.");
            // Ordinary REINDEX is atomic and can roll back. This deliberate maintenance operation
            // may briefly block community reads/writes; it is not a background startup task.
            await database.Database.ExecuteSqlRawAsync("REINDEX INDEX helpdesk.ix_community_knowledge_retrieval;", ct);
            index = await database.Database.SqlQueryRaw<IndexState>(stateSql).SingleOrDefaultAsync(ct);
        }
        var rows = await database.Database.SqlQueryRaw<KnowledgeIndexDocument>($"""
            SELECT id AS entry_id, public_revision AS revision,
                   encode(sha256(convert_to(jsonb_build_array(public_title,public_summary,public_content,
                       public_topic,public_guide)::text,'UTF8')),'hex') AS fingerprint
            FROM helpdesk.community_entries
            WHERE kind='Knowledge' AND is_published AND public_classification='Operational' AND public_guide IS NOT NULL
            ORDER BY id LIMIT {MaximumDocuments + 1}
            """).ToArrayAsync(ct);
        if (rows.Length > MaximumDocuments)
            throw new InvalidOperationException("Manifest document limit exceeded; do not advance the checkpoint.");
        var name = await database.Database.SqlQueryRaw<string>("SELECT current_database()::text AS \"Value\"").SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(1, "lexical-v1", name, clock.GetUtcNow().UtcDateTime, IndexName,
            definitionMatches && index is { Valid: true, Ready: true, Live: true }, rows);
    }

    private sealed record IndexState(bool Valid, bool Ready, bool Live, string Method, string TableName,
        string TableSchema, string? Expression, string? Predicate);
}
