using System.Text.Json;

namespace be.Infrastructure.Community;

public sealed record KnowledgeIndexDelta(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Updated,
    IReadOnlyList<Guid> Removed, IReadOnlyList<Guid> UnversionedChanges, IReadOnlyList<Guid> RevisionRegressions);

// A local operator checkpoint, not a second searchable copy. Contains no article bodies or identities.
public sealed class KnowledgeIndexManifest : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string path;
    private readonly FileStream gate;
    public KnowledgeIndexSnapshot? Previous { get; private set; }

    private KnowledgeIndexManifest(string path, FileStream gate, KnowledgeIndexSnapshot? previous)
    { this.path = path; this.gate = gate; Previous = previous; }

    public static async Task<KnowledgeIndexManifest> OpenAsync(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a dedicated .json manifest path.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            KnowledgeIndexSnapshot? previous = null;
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Manifest is too large.");
                await using var input = File.OpenRead(path);
                previous = await JsonSerializer.DeserializeAsync<KnowledgeIndexSnapshot>(input, Json, ct)
                    ?? throw new InvalidOperationException("Manifest is empty.");
                Validate(previous);
            }
            return new(path, gate, previous);
        }
        catch { await gate.DisposeAsync(); throw; }
    }

    public KnowledgeIndexDelta Compare(KnowledgeIndexSnapshot current)
    {
        Validate(current);
        if (Previous is not null && (Previous.DatabaseName != current.DatabaseName || Previous.Pipeline != current.Pipeline))
            throw new InvalidOperationException("Use a separate manifest for each database and retrieval pipeline.");
        var before = (Previous?.Documents ?? []).ToDictionary(x => x.EntryId);
        var after = current.Documents.ToDictionary(x => x.EntryId);
        return new(after.Keys.Except(before.Keys).Order().ToArray(),
            after.Values.Where(x => before.TryGetValue(x.EntryId, out var old) && (old.Revision != x.Revision || old.Fingerprint != x.Fingerprint)).Select(x => x.EntryId).Order().ToArray(),
            before.Keys.Except(after.Keys).Order().ToArray(),
            after.Values.Where(x => before.TryGetValue(x.EntryId, out var old) && old.Revision == x.Revision && old.Fingerprint != x.Fingerprint).Select(x => x.EntryId).Order().ToArray(),
            after.Values.Where(x => before.TryGetValue(x.EntryId, out var old) && x.Revision < old.Revision).Select(x => x.EntryId).Order().ToArray());
    }

    public async Task SaveAsync(KnowledgeIndexSnapshot current, CancellationToken ct)
    {
        var delta = Compare(current);
        if (!current.Healthy || delta.UnversionedChanges.Count != 0 || delta.RevisionRegressions.Count != 0)
            throw new InvalidOperationException("Unhealthy index, revision regression or snapshot changed without a new revision; checkpoint was not advanced.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, current, Json, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            Previous = current;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(KnowledgeIndexSnapshot snapshot)
    {
        if (snapshot.Format != 1 || snapshot.Pipeline != "lexical-v1" || snapshot.IndexName != KnowledgeIndexMaintenance.IndexName ||
            string.IsNullOrWhiteSpace(snapshot.DatabaseName) || snapshot.Documents is null || snapshot.Documents.Count > KnowledgeIndexMaintenance.MaximumDocuments ||
            snapshot.Documents.Any(x => x is null || x.EntryId == Guid.Empty || x.Revision < 1 || x.Fingerprint is null || x.Fingerprint.Length != 64 || x.Fingerprint.Any(c => !char.IsAsciiHexDigit(c))) ||
            snapshot.Documents.Select(x => x.EntryId).Distinct().Count() != snapshot.Documents.Count)
            throw new InvalidOperationException("Unsupported or invalid knowledge manifest; preserve it and investigate before retrying.");
    }

    public ValueTask DisposeAsync() => gate.DisposeAsync();
}
