using System.Globalization;
using System.Text.RegularExpressions;
using be.Domain.Entities;

namespace be.Application.Features.Community;

public static partial class KnowledgeGuideRules
{
    [GeneratedRegex(@"(?m)^([0-9]+)[.)]([ \t]*)", RegexOptions.CultureInvariant)]
    private static partial Regex StepMarker();

    public static CommunityClassification Classification(string? value) => value switch
    {
        null or "Unclassified" => CommunityClassification.Unclassified,
        "Operational" => CommunityClassification.Operational,
        "Demo" => CommunityClassification.Demo,
        _ => throw new CommunityOperationException(CommunityError.InvalidInput)
    };
    public static string Dataset(string? value) => value is null or "operational" ? "operational" : value == "demo" ? "demo" :
        throw new CommunityOperationException(CommunityError.InvalidInput);
    public static KnowledgeGuide? Normalize(KnowledgeGuide? guide, DateOnly today)
    {
        if (guide is null) return null;
        if (guide.SourceUpdatedOn > today || (guide.SourceUpdatedOn is { Year: < 1900 }) ||
            (!string.IsNullOrWhiteSpace(guide.SourceUrl) && (!Uri.TryCreate(guide.SourceUrl, UriKind.Absolute, out var url) ||
                url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) || guide.SourceUrl.Length > 1000))) Invalid();
        return guide with { Applicability = Field(guide.Applicability, 2000), Prerequisites = Field(guide.Prerequisites, 2000),
            ExpectedOutcome = Field(guide.ExpectedOutcome, 2000), Escalation = Field(guide.Escalation, 2000),
            SourceName = Field(guide.SourceName, 500), SourceUrl = string.IsNullOrWhiteSpace(guide.SourceUrl) ? null : guide.SourceUrl.Trim() };
    }
    public static bool Complete(KnowledgeGuide? guide, string content) => guide is not null &&
        new[] { guide.Applicability, guide.Prerequisites, guide.ExpectedOutcome, guide.Escalation, guide.SourceName }.All(x => !string.IsNullOrWhiteSpace(x)) && NumberedSteps(content);
    public static bool NumberedSteps(string content)
    {
        var matches = StepMarker().Matches(content.Replace("\r\n", "\n").Trim());
        var text = content.Replace("\r\n", "\n").Trim();
        if (matches.Count is < 1 or > 20 || matches[0].Index != 0) return false;
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Groups[1].Value != (i + 1).ToString(CultureInfo.InvariantCulture) || matches[i].Groups[2].Length == 0) return false;
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var body = text[start..end].Trim();
            if (body.Length is < 1 or > 4000) return false;
        }
        return true;
    }
    private static string Field(string? value, int max)
    {
        if (value?.Length > max || value?.Contains('\0') == true) Invalid();
        return value?.Trim() ?? "";
    }
    private static void Invalid() => throw new CommunityOperationException(CommunityError.InvalidInput);
}
