using System.IO;
using System.Text.RegularExpressions;
///////////////////////////////////////////////
namespace Spark;

sealed class ArtPrompt
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string FullText { get; init; } = "";
    public string Scene { get; init; } = "";
    public string Style { get; init; } = "";
    public string Series { get; init; } = "";

    public string Filename => $"prompt_{Number:D2}_{SanitizeFilename(Title)}";

    static string SanitizeFilename(string name)
        => Regex.Replace(name.ToLowerInvariant().Replace(' ', '_'), @"[^a-z0-9_]", "");
}

static class PromptParser
{
    public static List<ArtPrompt> Parse(string filePath)
    {
        string text = File.ReadAllText(filePath);
        List<ArtPrompt> prompts = [];
        string currentSeries = "";

        // Match series headers like "SERIES ONE: THE FRONTIER"
        Regex seriesRx = new(@"^SERIES\s+\w+:\s+(.+)$", RegexOptions.Multiline);
        Dictionary<int, string> seriesMap = [];
        foreach (Match sm in seriesRx.Matches(text))
        {
            int pos = sm.Index;
            seriesMap[pos] = sm.Groups[1].Value.Trim();
        }

        // Match prompts: PROMPT 01 — "First Light on Brennan-Yee"
        // \p{Pd} matches any Unicode dash (em-dash, en-dash, hyphen).
        Regex promptRx = new(
            @"PROMPT\s+(\d+)\s*\p{Pd}\s*""([^""]+)""",
            RegexOptions.Multiline);

        MatchCollection matches = promptRx.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            Match m = matches[i];
            int promptPos = m.Index;

            // Find which series this prompt belongs to
            foreach (KeyValuePair<int, string> kv in seriesMap.OrderByDescending(k => k.Key))
            {
                if (kv.Key < promptPos)
                {
                    currentSeries = kv.Value;
                    break;
                }
            }

            int number = int.Parse(m.Groups[1].Value);
            string title = m.Groups[2].Value.Trim();

            // Extract body text: from end of this match to start of next PROMPT or SERIES or PRODUCTION
            int bodyStart = m.Index + m.Length;
            int bodyEnd = i + 1 < matches.Count
                ? matches[i + 1].Index
                : text.Length;

            // Also stop at PRODUCTION NOTES or next SERIES header
            int prodIdx = text.IndexOf("PRODUCTION NOTES", bodyStart, StringComparison.Ordinal);
            if (prodIdx >= 0 && prodIdx < bodyEnd) bodyEnd = prodIdx;

            int nextSeriesIdx = text.IndexOf("SERIES ", bodyStart, StringComparison.Ordinal);
            if (nextSeriesIdx >= 0 && nextSeriesIdx < bodyEnd) bodyEnd = nextSeriesIdx;

            string body = text[bodyStart..bodyEnd].Trim();

            // Split into scene description and style directions.
            // The style block is typically the last paragraph — starts with a visual direction keyword.
            string[] paragraphs = Regex.Split(body, @"\n\s*\n")
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();

            string scene;
            string style;
            if (paragraphs.Length >= 2)
            {
                scene = string.Join("\n\n", paragraphs[..^1]);
                style = paragraphs[^1];
            }
            else
            {
                scene = body;
                style = "";
            }

            string fullPrompt = scene;
            if (style.Length > 0)
                fullPrompt += "\n\n" + style;

            prompts.Add(new ArtPrompt
            {
                Number = number,
                Title = title,
                FullText = fullPrompt,
                Scene = scene,
                Style = style,
                Series = currentSeries,
            });
        }

        return prompts;
    }
}
