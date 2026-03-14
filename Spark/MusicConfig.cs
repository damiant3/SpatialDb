using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

static class MusicConfig
{
    sealed class Root
    {
        [JsonPropertyName("instruments")]    public Dictionary<string, string[]> Instruments { get; set; } = [];
        [JsonPropertyName("composers")]      public string[] Composers { get; set; } = [];
        [JsonPropertyName("artists")]        public string[] Artists { get; set; } = [];
        [JsonPropertyName("genres")]         public string[] Genres { get; set; } = [];
        [JsonPropertyName("tempoMarkings")]  public TempoEntry[] TempoMarkings { get; set; } = [];
        [JsonPropertyName("keys")]           public string[] Keys { get; set; } = [];
        [JsonPropertyName("scales")]         public string[] Scales { get; set; } = [];
        [JsonPropertyName("moodPresets")]    public string[] MoodPresets { get; set; } = [];
    }

    public sealed record TempoEntry(
        [property: JsonPropertyName("label")]   string Label,
        [property: JsonPropertyName("bpmHint")] int BpmHint);

    static readonly JsonSerializerOptions s_opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    static Root? s_root;
    static Root Data => s_root ??= Load();

    static Root Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "music_config.json");
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

    public static Dictionary<string, string[]> Instruments => Data.Instruments;
    public static string[] InstrumentFamilies => [.. Data.Instruments.Keys];
    public static string[] AllInstruments => [.. Data.Instruments.Values.SelectMany(v => v)];

    public static string[] InstrumentsIn(string family)
        => Data.Instruments.TryGetValue(family, out string[]? list) ? list : [];

    public static string[] Composers => Data.Composers;
    public static string[] Artists => Data.Artists;
    public static string[] Genres => Data.Genres;
    public static TempoEntry[] TempoMarkings => Data.TempoMarkings;
    public static string[] TempoLabels => [.. Data.TempoMarkings.Select(t => t.Label)];
    public static string[] Keys => Data.Keys;
    public static string[] Scales => Data.Scales;
    public static string[] MoodPresets => Data.MoodPresets;
}
