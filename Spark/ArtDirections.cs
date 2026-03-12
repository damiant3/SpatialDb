using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Loads art direction nudges from <c>art_directions.json</c>.
/// Falls back to an empty set if the file is missing.
/// </summary>
static class ArtDirections
{
    public sealed record Direction(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("promptAdd")] string PromptAdd,
        [property: JsonPropertyName("negativeAdd")] string NegativeAdd = "",
        [property: JsonPropertyName("cfgNudge")] double? CfgNudge = null,
        [property: JsonPropertyName("stepsNudge")] int? StepsNudge = null);

    public sealed record DirectionGroup(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("emoji")] string Emoji,
        [property: JsonPropertyName("items")] Direction[] Items);

    sealed record Root([property: JsonPropertyName("groups")] DirectionGroup[] Groups);

    static readonly JsonSerializerOptions s_opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    static DirectionGroup[]? s_groups;

    public static DirectionGroup[] Groups => s_groups ??= Load();

    static DirectionGroup[] Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "art_directions.json");
        if (!File.Exists(path)) return [];
        try
        {
            string json = File.ReadAllText(path);
            Root? root = JsonSerializer.Deserialize<Root>(json, s_opts);
            return root?.Groups ?? [];
        }
        catch { return []; }
    }

    public static void Reload() { s_groups = null; }

    public static IEnumerable<(string category, Direction dir)> All()
    {
        foreach (DirectionGroup g in Groups)
            foreach (Direction d in g.Items)
                yield return (g.Category, d);
    }
}
