using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

static class SfxConfig
{
    sealed class Root
    {
        [JsonPropertyName("minDuration")]      public double MinDuration { get; set; } = 1.0;
        [JsonPropertyName("maxDuration")]      public double MaxDuration { get; set; } = 10.0;
        [JsonPropertyName("categories")]       public string[] Categories { get; set; } = ["All"];
        [JsonPropertyName("categoryDefaults")] public Dictionary<string, CategoryDefault> CategoryDefaults { get; set; } = [];
        [JsonPropertyName("presets")]          public SfxPresetEntry[] Presets { get; set; } = [];
    }

    public sealed record CategoryDefault(
        [property: JsonPropertyName("duration")]    double Duration,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("cfg")]         double Cfg);

    public sealed record SfxPresetEntry(
        [property: JsonPropertyName("label")]    string Label,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("prompt")]   string Prompt);

    static readonly JsonSerializerOptions s_opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    static Root? s_root;
    static Root Data => s_root ??= Load();

    static Root Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "sfx_config.json");
        if (!File.Exists(path)) return new Root();
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Root>(json, s_opts) ?? new Root();
        }
        catch (Exception)
        {
            return new Root();
        }
    }

    public static void Reload() { s_root = null; }

    public static double MinDuration => Data.MinDuration;
    public static double MaxDuration => Data.MaxDuration;
    public static string[] Categories => Data.Categories;

    public static CategoryDefault? DefaultsFor(string category)
        => Data.CategoryDefaults.TryGetValue(category, out CategoryDefault? d) ? d : null;

    public static SfxPresetEntry[] Presets => Data.Presets;
}
