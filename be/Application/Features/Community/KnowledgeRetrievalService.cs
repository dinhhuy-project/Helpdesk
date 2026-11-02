using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using be.Domain.Entities;

namespace be.Application.Features.Community;

public sealed record KnowledgeRetrievalRequest(string Query, int Limit = 5);
public sealed record KnowledgePassage(Guid EntryId, int Revision, string Title, string Topic, string Section, int? Step,
    string Text, string SourceUrl, string Applicability, string SourceName, string? ReferenceUrl, DateOnly? SourceUpdatedOn, DateTime PublishedAt);
public sealed record KnowledgeRetrievalResult(string Query, IReadOnlyList<KnowledgePassage> Items, bool CandidateLimitReached, string Method = "lexical-v1");
public sealed record KnowledgeCandidate(Guid Id, string Title, string Summary, string Content, string Topic, int Revision, DateTime PublishedAt, string GuideJson);
public interface IKnowledgeRetrievalStore
{
    Task<IReadOnlyList<KnowledgeCandidate>> CandidatesAsync(string tsQuery, CancellationToken ct);
}

// Query expansion is a reviewed vocabulary, not a language model. Unknown concepts remain literal terms.
public static partial class KnowledgeRetrievalText
{
    private static readonly string[][] Concepts =
    [
        ["mat khau", "password", "passwd", "mk"],
        ["dang nhap", "login", "sign in", "signin", "vao tai khoan", "truy cap tai khoan"],
        ["doi mat khau", "thay mat khau", "thay doi mat khau", "change password"],
        ["mfa", "2fa", "otp", "xac thuc hai lop", "xac thuc hai buoc", "ma xac thuc"],
        ["vpn", "mang rieng ao", "ket noi mang noi bo"],
        ["tep dinh kem", "dinh kem", "attachment", "attachments", "file", "tep", "hinh anh"],
        ["tai len", "tai file len", "upload", "gui file", "gui tep", "them tep"],
        ["tai xuong", "download", "luu file"],
        ["ticket", "yeu cau ho tro", "phieu ho tro", "gui yeu cau", "tao yeu cau", "bao loi"],
        ["binh luan", "comment", "trao doi", "phan hoi"],
        ["thong bao", "notification", "notifications", "chuong thong bao"],
        ["dang xuat", "logout", "log out", "ket thuc phien"],
        ["email", "thu dien tu", "hop thu", "mail"],
        ["may in", "printer", "in an"],
        ["tra cuu", "tim huong dan", "tim kiem", "kho tri thuc"],
        ["cong khai", "public"],
        ["noi bo", "internal"]
    ];
    private static readonly HashSet<string> StopWords = new(("toi ban minh em anh chi chung ta muon can nho giup xin lam sao the nao de duoc " +
        "khong co bi la va voi cho mot nhung cac nay do khi thi neu phai dang da se o den tu tren bang ve nhu hay hoi cach " +
        "lam vay trong phan muc luc thuc hien su dung he thong ung dung vi sao lai duoc chua how to can i my the a an is of for please").Split(' '), StringComparer.Ordinal);

    public static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(c is 'đ' ? 'd' : char.IsAsciiLetterOrDigit(c) ? c : ' ');
        }
        return Spaces().Replace(result.ToString(), " ").Trim();
    }

    public static IReadOnlyList<string[]> Groups(string query)
    {
        var remaining = " " + Normalize(query) + " ";
        var groups = new List<string[]>();
        // Longest phrases first, so 'doi mat khau' is not reduced to the generic password concept.
        foreach (var concept in Concepts.OrderByDescending(c => c.Where(a => remaining.Contains(" " + a + " ", StringComparison.Ordinal))
                     .Select(a => a.Length).DefaultIfEmpty(0).Max()).ToArray())
        {
            if (!concept.Any(alias => remaining.Contains(" " + alias + " ", StringComparison.Ordinal))) continue;
            groups.Add(concept);
            foreach (var alias in concept.OrderByDescending(a => a.Length))
                while (remaining.Contains(" " + alias + " ", StringComparison.Ordinal))
                    remaining = remaining.Replace(" " + alias + " ", " ", StringComparison.Ordinal);
        }
        groups.AddRange(remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
            .Where(word => !StopWords.Contains(word)).Select(word => new[] { word }));
        return groups;
    }

    // Every term comes from the ASCII tokenizer or the fixed vocabulary; no tsquery operators from users survive.
    public static string TsQuery(IReadOnlyList<string[]> groups) => string.Join(" & ", groups.Select(group =>
        "(" + string.Join(" | ", group.Select(alias => "(" + string.Join(" <-> ", alias.Split(' ')) + ")")) + ")"));
    public static bool Matches(string normalizedText, string[] group) => group.Any(alias =>
        (" " + normalizedText + " ").Contains(" " + alias + " ", StringComparison.Ordinal));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

public sealed partial class KnowledgeRetrievalService(IKnowledgeRetrievalStore store)
{
    public const int CandidateLimit = 60;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<KnowledgeRetrievalResult> SearchAsync(KnowledgeRetrievalRequest request, CancellationToken ct)
    {
        if (request.Limit is < 1 or > 8) throw new CommunityOperationException(CommunityError.InvalidInput);
        var query = ValidateQuery(request.Query);
        var groups = KnowledgeRetrievalText.Groups(query);
        if (groups.Count == 0) return new(query, [], false);
        var candidates = await store.CandidatesAsync(KnowledgeRetrievalText.TsQuery(groups), ct);
        var ranked = new List<(KnowledgePassage Passage, double Score)>();
        foreach (var row in candidates.Take(CandidateLimit))
        {
            ct.ThrowIfCancellationRequested();
            KnowledgeGuide? guide;
            try { guide = System.Text.Json.JsonSerializer.Deserialize<KnowledgeGuide>(row.GuideJson, JsonOptions); }
            catch (System.Text.Json.JsonException) { continue; } // Ineligible legacy metadata must not break all search results.
            // Fail closed if legacy/corrupt snapshots bypassed the publishing rules.
            if (!KnowledgeGuideRules.Complete(guide, row.Content) || guide is null) continue;
            var title = KnowledgeRetrievalText.Normalize(row.Title);
            var context = KnowledgeRetrievalText.Normalize(row.Summary + " " + guide.Applicability);
            foreach (var part in Sections(row.Content, guide))
            {
                var text = KnowledgeRetrievalText.Normalize(part.Text);
                var direct = groups.Count(g => KnowledgeRetrievalText.Matches(text, g));
                if (direct == 0) continue;
                var score = direct * 4d + groups.Count(g => KnowledgeRetrievalText.Matches(title, g)) * 1.5 +
                    groups.Count(g => KnowledgeRetrievalText.Matches(context, g)) * .3 + (part.Step is null ? 0 : .2);
                var passage = new KnowledgePassage(row.Id, row.Revision, row.Title, row.Topic, part.Section, part.Step, part.Text,
                    $"/knowledge/{row.Id}", guide.Applicability, guide.SourceName, guide.SourceUrl, guide.SourceUpdatedOn, row.PublishedAt);
                ranked.Add((passage, score));
            }
        }
        var items = ranked.OrderByDescending(x => x.Score).ThenByDescending(x => x.Passage.PublishedAt).ThenBy(x => x.Passage.EntryId)
            .ThenBy(x => x.Passage.Step ?? int.MaxValue).GroupBy(x => x.Passage.EntryId)
            .SelectMany(group => group.Take(2)).OrderByDescending(x => x.Score).ThenByDescending(x => x.Passage.PublishedAt)
            .ThenBy(x => x.Passage.EntryId).ThenBy(x => x.Passage.Step ?? int.MaxValue).Take(request.Limit).Select(x => x.Passage).ToArray();
        return new(query, items, candidates.Count > CandidateLimit);
    }

    public static string ValidateQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Trim().Length < 2 || value.Any(char.IsControl) || HasInvalidUnicode(value))
            throw new CommunityOperationException(CommunityError.InvalidInput);
        return value.Trim();
    }

    private static bool HasInvalidUnicode(string value)
    {
        try { _ = value.Normalize(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static IEnumerable<(string Section, int? Step, string Text)> Sections(string content, KnowledgeGuide guide)
    {
        var lines = content.Replace("\r\n", "\n").Trim();
        var matches = StepStart().Matches(lines);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : lines.Length;
            foreach (var fragment in Fragments(lines[start..end].Trim())) yield return ("Các bước thực hiện", i + 1, fragment);
        }
        foreach (var (name, text) in new[] { ("Phạm vi áp dụng", guide.Applicability), ("Điều kiện trước khi thực hiện", guide.Prerequisites),
                     ("Kết quả cần đạt", guide.ExpectedOutcome), ("Khi nào cần gửi Ticket", guide.Escalation) })
            foreach (var fragment in Fragments(text)) yield return (name, null, fragment);
    }

    private static IEnumerable<string> Fragments(string text)
    {
        // Bounded excerpts remain verbatim; split long steps without inventing or summarizing instructions.
        for (var start = 0; start < text.Length;)
        {
            var length = Math.Min(1000, text.Length - start);
            if (start + length < text.Length)
            {
                var boundary = text.LastIndexOf(' ', start + length - 1, length);
                if (boundary > start + 500) length = boundary - start;
                else if (char.IsHighSurrogate(text[start + length - 1])) length--;
            }
            var fragment = text.Substring(start, length).Trim();
            if (fragment.Length > 0) yield return fragment;
            start += length;
        }
    }

    [GeneratedRegex(@"(?m)^[0-9]+[.)][ \t]+")]
    private static partial Regex StepStart();
}
