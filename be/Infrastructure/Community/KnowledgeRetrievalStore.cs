using be.Application.Features.Community;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Community;

public sealed class KnowledgeRetrievalStore(HelpdeskDbContext database) : IKnowledgeRetrievalStore
{
    public async Task<IReadOnlyList<KnowledgeCandidate>> CandidatesAsync(string tsQuery, CancellationToken ct) =>
        await database.Database.SqlQuery<KnowledgeCandidate>($"""
            SELECT e.id, e.public_title AS title, e.public_summary AS summary, e.public_content AS content,
                   e.public_topic AS topic, e.public_revision AS revision, e.published_at, e.public_guide::text AS guide_json
            FROM helpdesk.community_entries e
            WHERE e.kind = 'Knowledge' AND e.is_published AND e.public_classification = 'Operational' AND e.public_guide IS NOT NULL
              AND helpdesk.knowledge_search_vector(e.public_title, e.public_summary, e.public_content, e.public_topic, e.public_guide)
                  @@ to_tsquery('simple', {tsQuery})
            ORDER BY ts_rank_cd(helpdesk.knowledge_search_vector(e.public_title, e.public_summary, e.public_content, e.public_topic, e.public_guide),
                                to_tsquery('simple', {tsQuery}), 32) DESC, e.published_at DESC, e.id
            LIMIT {KnowledgeRetrievalService.CandidateLimit + 1}
            """).ToArrayAsync(ct);
}
