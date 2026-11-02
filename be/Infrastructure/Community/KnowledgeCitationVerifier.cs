using be.Application.Features.Community;
using be.Domain.Entities;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Community;

public sealed class KnowledgeCitationVerifier(HelpdeskDbContext database) : IKnowledgeCitationVerifier
{
    public async Task<bool> AreCurrentAsync(IReadOnlyList<KnowledgeSourceVersion> sources, CancellationToken ct)
    {
        if (sources.Count is < 1 or > 3) return false;
        var ids = sources.Select(source => source.EntryId).ToArray();
        var current = await database.CommunityEntries.AsNoTracking()
            .Where(entry => ids.Contains(entry.Id) && entry.Kind == CommunityKind.Knowledge && entry.IsPublished &&
                entry.PublicClassification == CommunityClassification.Operational && entry.PublicGuide != null)
            .Select(entry => new { entry.Id, entry.PublicRevision }).ToArrayAsync(ct);
        return sources.All(source => current.Any(entry => entry.Id == source.EntryId && entry.PublicRevision == source.Revision));
    }
}
