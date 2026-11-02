using System.Text.Json;
using be.Domain.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace be.Infrastructure.Persistence.Configurations;

internal static class KnowledgeGuideJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Encode(KnowledgeGuide? value) => JsonSerializer.Serialize(value, Options);
    public static KnowledgeGuide? Decode(string value) => JsonSerializer.Deserialize<KnowledgeGuide>(value, Options);
    public static readonly ValueConverter<KnowledgeGuide?, string> Converter = new(v => Encode(v), v => Decode(v));
    public static readonly ValueComparer<KnowledgeGuide?> Comparer = new((a, b) => Encode(a) == Encode(b), v => Encode(v).GetHashCode(), v => Decode(Encode(v)));
}
