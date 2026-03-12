using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Reads universe configuration from <c>universe.json</c> and scans project
/// story files for proper nouns. The glossary is user-editable JSON.
/// </summary>
static class StoryContext
{
    sealed record UniverseData(
        [property: JsonPropertyName("glossary")] string Glossary,
        [property: JsonPropertyName("properNouns")] string[] ProperNouns,
        [property: JsonPropertyName("storyFilePatterns")] string[] StoryFilePatterns);

    static UniverseData? s_universe;

    public static string UniverseGlossary => LoadUniverse().Glossary;

    static UniverseData LoadUniverse()
    {
        if (s_universe is not null) return s_universe;
        string path = Path.Combine(AppContext.BaseDirectory, "universe.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                UniverseData? data = JsonSerializer.Deserialize<UniverseData>(json);
                if (data is not null) { s_universe = data; return data; }
            }
            catch { /* fall through */ }
        }
        s_universe = new UniverseData("", [], ["*.txt", "*.md"]);
        return s_universe;
    }

    public static void Reload() { s_universe = null; }

    public static string LoadProjectContext(string projectDir)
    {
        UniverseData uni = LoadUniverse();
        if (uni.ProperNouns.Length == 0 && uni.StoryFilePatterns.Length == 0)
            return "";

        List<string> extraTerms = [];

        Regex? nounPattern = uni.ProperNouns.Length > 0
            ? new Regex(@"\b(" + string.Join("|", uni.ProperNouns.Select(Regex.Escape)) + @")\b",
                RegexOptions.Compiled)
            : null;

        foreach (string pattern in uni.StoryFilePatterns)
        {
            foreach (string file in Directory.GetFiles(projectDir, pattern))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("ArtPrompts.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    string text = File.ReadAllText(file);
                    if (nounPattern is null) continue;
                    foreach (Match m in nounPattern.Matches(text))
                    {
                        if (!extraTerms.Contains(m.Value))
                            extraTerms.Add(m.Value);
                    }
                }
                catch { /* non-fatal */ }
            }
        }

        return extraTerms.Count > 0
            ? $"Key characters/places: {string.Join(", ", extraTerms.Take(20))}. "
            : "";
    }
}
