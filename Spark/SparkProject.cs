using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Represents a <c>spark_project.json</c> file that ties together all project
/// configuration: paths, default settings, and references to the JSON config files.
/// </summary>
sealed class SparkProject
{
    static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Persisted fields ────────────────────────────────────────

    [JsonPropertyName("version")]          public int Version { get; set; } = 1;
    [JsonPropertyName("name")]             public string Name { get; set; } = "Untitled";
    [JsonPropertyName("outputDir")]        public string OutputDir { get; set; } = "Concept";
    [JsonPropertyName("storyFiles")]       public string[] StoryFiles { get; set; } = [];
    [JsonPropertyName("promptsFile")]      public string PromptsFile { get; set; } = "ArtPrompts.txt";
    [JsonPropertyName("universeFile")]     public string UniverseFile { get; set; } = "universe.json";
    [JsonPropertyName("artDirectionsFile")]  public string ArtDirectionsFile { get; set; } = "art_directions.json";
    [JsonPropertyName("creativePoolsFile")]  public string CreativePoolsFile { get; set; } = "creative_pools.json";
    [JsonPropertyName("refinePresetsFile")]  public string RefinePresetsFile { get; set; } = "refine_presets.json";
    [JsonPropertyName("defaultSettings")]    public ProjectSettings? DefaultSettings { get; set; }

    // ── Runtime (not serialized) ────────────────────────────────

    [JsonIgnore] public string ProjectDir { get; internal set; } = "";
    [JsonIgnore] public string ProjectFilePath { get; internal set; } = "";

    // ── Resolved paths ──────────────────────────────────────────

    [JsonIgnore] public string ResolvedOutputDir => Path.Combine(ProjectDir, OutputDir);
    [JsonIgnore] public string ResolvedPromptsFile => Path.Combine(ProjectDir, PromptsFile);
    [JsonIgnore] public string ResolvedUniverseFile => Path.Combine(ProjectDir, UniverseFile);
    [JsonIgnore] public string ResolvedArtDirectionsFile => Path.Combine(ProjectDir, ArtDirectionsFile);
    [JsonIgnore] public string ResolvedCreativePoolsFile => Path.Combine(ProjectDir, CreativePoolsFile);
    [JsonIgnore] public string ResolvedRefinePresetsFile => Path.Combine(ProjectDir, RefinePresetsFile);

    // ── Load / Save / Create ────────────────────────────────────

    public static SparkProject? Load(string projectFilePath)
    {
        if (!File.Exists(projectFilePath)) return null;
        try
        {
            string json = File.ReadAllText(projectFilePath);
            SparkProject? proj = JsonSerializer.Deserialize<SparkProject>(json, s_opts);
            if (proj is null) return null;
            proj.ProjectFilePath = Path.GetFullPath(projectFilePath);
            proj.ProjectDir = Path.GetDirectoryName(proj.ProjectFilePath) ?? "";
            return proj;
        }
        catch { return null; }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, s_opts);
        File.WriteAllText(ProjectFilePath, json);
    }

    /// <summary>
    /// Searches up from <paramref name="startDir"/> looking for <c>spark_project.json</c>.
    /// If not found, looks for the legacy <c>ArtPrompts.txt</c> and auto-generates a project file.
    /// </summary>
    public static SparkProject FindOrCreate(string startDir)
    {
        // Walk up looking for spark_project.json
        string? dir = startDir;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "spark_project.json");
            if (File.Exists(candidate))
            {
                SparkProject? loaded = Load(candidate);
                if (loaded is not null) return loaded;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Legacy: walk up looking for ArtPrompts.txt
        dir = startDir;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir, "ArtPrompts.txt")))
                return CreateFromLegacy(dir);
            dir = Path.GetDirectoryName(dir);
        }

        // Nothing found — create a default in startDir
        return CreateDefault(startDir);
    }

    /// <summary>
    /// Backward compat: generates a project file from a legacy directory that has ArtPrompts.txt.
    /// </summary>
    static SparkProject CreateFromLegacy(string projectDir)
    {
        SparkProject proj = new()
        {
            Name = Path.GetFileName(projectDir) ?? "Untitled",
            ProjectDir = Path.GetFullPath(projectDir),
            ProjectFilePath = Path.Combine(Path.GetFullPath(projectDir), "spark_project.json"),
        };

        // Discover story files
        List<string> storyFiles = [];
        foreach (string ext in new[] { "*.txt", "*.md" })
        {
            foreach (string file in Directory.GetFiles(projectDir, ext))
            {
                string name = Path.GetFileName(file);
                if (!name.Equals("ArtPrompts.txt", StringComparison.OrdinalIgnoreCase))
                    storyFiles.Add(name);
            }
        }
        proj.StoryFiles = [.. storyFiles];

        proj.Save();
        return proj;
    }

    static SparkProject CreateDefault(string projectDir)
    {
        SparkProject proj = new()
        {
            Name = "New Project",
            ProjectDir = Path.GetFullPath(projectDir),
            ProjectFilePath = Path.Combine(Path.GetFullPath(projectDir), "spark_project.json"),
        };
        proj.Save();
        return proj;
    }
}

sealed class ProjectSettings
{
    [JsonPropertyName("width")]     public int? Width { get; set; }
    [JsonPropertyName("height")]    public int? Height { get; set; }
    [JsonPropertyName("steps")]     public int? Steps { get; set; }
    [JsonPropertyName("cfgScale")]  public double? CfgScale { get; set; }
    [JsonPropertyName("sampler")]   public string? Sampler { get; set; }
    [JsonPropertyName("scheduler")] public string? Scheduler { get; set; }
}
