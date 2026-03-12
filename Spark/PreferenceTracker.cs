using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Lightweight preference heuristic. Tracks which style tokens correlate with
/// saves/high-ratings vs. deletes/low-ratings, and nudges future prompts.
/// Not a real neural net — just a weighted term frequency tracker that drifts
/// toward what makes you happy. Persisted as Concept\preferences.json.
/// </summary>
sealed class PreferenceTracker
{
    readonly string m_path;
    PreferenceState m_state = new();
    static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Style tokens we track — extracted from the art prompts' style directions
    static readonly string[] s_signalTokens =
    [
        "anime", "realistic", "painterly", "cinematic", "wide", "close-up",
        "bold", "dark", "bright", "saturated", "desaturated", "detailed",
        "minimal", "dramatic", "soft", "hard", "warm", "cool", "purple",
        "amber", "copper", "blue", "motion", "still", "crowds", "solo",
        "scale contrast", "geometric", "organic", "kubrick", "easley",
        "moebius", "studio khara", "concept art", "paperback",
        "poster", "portrait", "landscape", "16:9", "3:2", "1:1",
        "high detail", "low detail", "sharp", "blurry",
    ];

    public PreferenceTracker(string conceptDir)
    {
        Directory.CreateDirectory(conceptDir);
        m_path = Path.Combine(conceptDir, "preferences.json");
        Load();
    }

    void Load()
    {
        if (!File.Exists(m_path))
        {
            m_state = new PreferenceState();
            return;
        }
        string json = File.ReadAllText(m_path);
        m_state = JsonSerializer.Deserialize<PreferenceState>(json, s_jsonOpts) ?? new();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(m_state, s_jsonOpts);
        File.WriteAllText(m_path, json);
    }

    /// <summary>
    /// Records a positive signal (save, high rating) for an image.
    /// Extracts style tokens from the prompt+style and boosts their weights.
    /// </summary>
    public void RecordPositive(ImageRecord record, double strength = 1.0)
    {
        string[] tokens = ExtractTokens(record.PromptText + " " + record.Style);
        foreach (string token in tokens)
        {
            m_state.Weights.TryGetValue(token, out double w);
            m_state.Weights[token] = w + strength;
        }
        m_state.TotalPositive++;

        // Also record the refine preset if one was used
        if (record.RefinePreset != "none")
        {
            m_state.Weights.TryGetValue("preset:" + record.RefinePreset, out double pw);
            m_state.Weights["preset:" + record.RefinePreset] = pw + strength * 0.5;
        }

        record.PositiveSignals = tokens;
        Save();
    }

    /// <summary>
    /// Records a negative signal (delete, low rating) for an image.
    /// </summary>
    public void RecordNegative(ImageRecord record, double strength = 1.0)
    {
        string[] tokens = ExtractTokens(record.PromptText + " " + record.Style);
        foreach (string token in tokens)
        {
            m_state.Weights.TryGetValue(token, out double w);
            m_state.Weights[token] = w - strength;
        }
        m_state.TotalNegative++;
        record.NegativeSignals = tokens;
        Save();
    }

    /// <summary>
    /// Modifies a prompt based on accumulated preferences.
    /// Injects terms you tend to like, softens terms you tend to dislike.
    /// Returns the modified prompt and a description of what changed.
    /// </summary>
    public (string prompt, string explanation) AdjustPrompt(string originalPrompt)
    {
        if (m_state.TotalPositive + m_state.TotalNegative < 3)
            return (originalPrompt, "not enough data yet");

        List<string> boosts = [];
        List<string> suppressions = [];

        foreach (KeyValuePair<string, double> kv in m_state.Weights.OrderByDescending(x => x.Value))
        {
            if (kv.Key.StartsWith("preset:")) continue;
            if (kv.Value > 1.5 && !originalPrompt.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                boosts.Add(kv.Key);
            if (boosts.Count >= 3) break;
        }

        foreach (KeyValuePair<string, double> kv in m_state.Weights.OrderBy(x => x.Value))
        {
            if (kv.Key.StartsWith("preset:")) continue;
            if (kv.Value < -1.5 && originalPrompt.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                suppressions.Add(kv.Key);
            if (suppressions.Count >= 2) break;
        }

        string modified = originalPrompt;
        // Append boosts
        if (boosts.Count > 0)
            modified += ", " + string.Join(", ", boosts);

        // Remove or weaken suppressions from the prompt
        foreach (string s in suppressions)
            modified = Regex.Replace(modified, Regex.Escape(s), "", RegexOptions.IgnoreCase).Trim();

        // Clean up double commas/spaces
        modified = Regex.Replace(modified, @",\s*,", ",");
        modified = Regex.Replace(modified, @"\s{2,}", " ").Trim().TrimEnd(',');

        string explanation = "";
        if (boosts.Count > 0) explanation += $"+[{string.Join(", ", boosts)}]";
        if (suppressions.Count > 0) explanation += $" −[{string.Join(", ", suppressions)}]";
        if (explanation.Length == 0) explanation = "no adjustments";

        return (modified, explanation);
    }

    /// <summary>
    /// Returns the top positive and negative preferences for display.
    /// </summary>
    public (List<(string token, double weight)> likes, List<(string token, double weight)> dislikes) GetTopPreferences(int count = 5)
    {
        List<(string, double)> likes = m_state.Weights
            .Where(kv => kv.Value > 0 && !kv.Key.StartsWith("preset:"))
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        List<(string, double)> dislikes = m_state.Weights
            .Where(kv => kv.Value < 0 && !kv.Key.StartsWith("preset:"))
            .OrderBy(kv => kv.Value)
            .Take(count)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        return (likes, dislikes);
    }

    /// <summary>
    /// Suggests which refine preset to try based on preference history.
    /// </summary>
    public string SuggestedPreset()
    {
        string best = "none";
        double bestW = 0;
        foreach (KeyValuePair<string, double> kv in m_state.Weights)
        {
            if (!kv.Key.StartsWith("preset:")) continue;
            if (kv.Value > bestW) { bestW = kv.Value; best = kv.Key[7..]; }
        }
        return best;
    }

    public int TotalSignals => m_state.TotalPositive + m_state.TotalNegative;

    static string[] ExtractTokens(string text)
    {
        string lower = text.ToLowerInvariant();
        return s_signalTokens.Where(t => lower.Contains(t)).ToArray();
    }
}

sealed class PreferenceState
{
    public Dictionary<string, double> Weights { get; set; } = [];
    public int TotalPositive { get; set; }
    public int TotalNegative { get; set; }
}
