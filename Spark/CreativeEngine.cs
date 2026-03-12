using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// "Creative mode" — injects controlled randomness into generation parameters
/// to maximize variance. Pools are loaded from <c>creative_pools.json</c>.
/// </summary>
static class CreativeEngine
{
    static readonly Random s_rng = new();

    sealed record PoolData(
        [property: JsonPropertyName("colorThemes")] string[] ColorThemes,
        [property: JsonPropertyName("compositions")] string[] Compositions,
        [property: JsonPropertyName("inspirations")] string[] Inspirations,
        [property: JsonPropertyName("moods")] string[] Moods,
        [property: JsonPropertyName("lightingSetups")] string[] LightingSetups,
        [property: JsonPropertyName("samplerHints")] string[] SamplerHints);

    static PoolData? s_pools;

    static PoolData Pools => s_pools ??= Load();

    static PoolData Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "creative_pools.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                PoolData? data = JsonSerializer.Deserialize<PoolData>(json);
                if (data is not null) return data;
            }
            catch { /* fall through to defaults */ }
        }
        return new PoolData(
            ["bold color palette"], ["wide composition"], ["concept art style"],
            ["atmospheric mood"], ["dramatic lighting"], [""]);
    }

    public static void Reload() { s_pools = null; }

    public static (string augment, double cfgNudge, int stepsNudge, string samplerHint) Generate()
    {
        PoolData p = Pools;
        string color = Pick(p.ColorThemes);
        string comp = Pick(p.Compositions);
        string insp = Pick(p.Inspirations);
        string mood = Pick(p.Moods);
        string light = Pick(p.LightingSetups);

        string augment = $"{comp}, {color}, {light}, {mood}, {insp}";

        double cfgNudge = s_rng.NextDouble() * 4.0 - 2.0;
        int stepsNudge = s_rng.Next(-5, 8);

        string samplerHint = Pick(p.SamplerHints);

        return (augment, cfgNudge, stepsNudge, samplerHint);
    }

    public static string PickOne()
    {
        PoolData p = Pools;
        string[][] pools = [p.ColorThemes, p.Compositions, p.Inspirations, p.Moods, p.LightingSetups];
        string[] pool = pools[s_rng.Next(pools.Length)];
        return Pick(pool);
    }

    static string Pick(string[] arr) => arr.Length > 0 ? arr[s_rng.Next(arr.Length)] : "";
}
