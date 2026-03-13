using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Common.Wpf.Input;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.Wizard;

/// <summary>
/// Drives the New Project wizard. Accumulates state across steps and writes
/// all project files on finish.
/// </summary>
sealed class WizardViewModel : INotifyPropertyChanged
{
    readonly OllamaClient m_ollama;
    CancellationTokenSource? m_cts;

    // ── Step tracking ───────────────────────────────────────────

    int m_currentStep;
    const int TotalSteps = 5;

    public int CurrentStep { get => m_currentStep; set { SetField(ref m_currentStep, value); OnPropertyChanged(nameof(StepTitle)); OnPropertyChanged(nameof(StepSubtitle)); OnPropertyChanged(nameof(CanGoBack)); OnPropertyChanged(nameof(NextLabel)); OnPropertyChanged(nameof(ProgressText)); } }
    public bool CanGoBack => m_currentStep > 0;
    public string NextLabel => m_currentStep == TotalSteps - 1 ? "✨ Create Project" : "Next →";
    public string ProgressText => $"Step {m_currentStep + 1} of {TotalSteps}";

    public string StepTitle => m_currentStep switch
    {
        0 => "🔥 Project Setup",
        1 => "📖 Story & Setting",
        2 => "🎨 Art Style",
        3 => "📋 Art Prompts",
        4 => "⚙ Generation Settings",
        _ => ""
    };

    public string StepSubtitle => m_currentStep switch
    {
        0 => "Name your project and choose where to save it.",
        1 => "Describe your game world. Paste a synopsis, or let Ollama generate one.",
        2 => "Choose an art style template or describe your own.",
        3 => "Generate art prompts from your story, or write them manually.",
        4 => "Review generation settings and create your project.",
        _ => ""
    };

    // ── Step 0: Project ─────────────────────────────────────────

    string m_projectName = "My Game";
    string m_projectFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SparkProjects");

    public string ProjectName { get => m_projectName; set => SetField(ref m_projectName, value); }
    public string ProjectFolder { get => m_projectFolder; set => SetField(ref m_projectFolder, value); }

    // ── Step 1: Story ───────────────────────────────────────────

    string m_storySynopsis = "";
    string m_glossaryTerms = "";

    public string StorySynopsis { get => m_storySynopsis; set => SetField(ref m_storySynopsis, value); }
    public string GlossaryTerms { get => m_glossaryTerms; set => SetField(ref m_glossaryTerms, value); }

    // ── Step 2: Art Style ───────────────────────────────────────

    string m_selectedStyleTemplate = "Sci-Fi Concept Art";
    string m_customStyleNotes = "";

    public string SelectedStyleTemplate { get => m_selectedStyleTemplate; set => SetField(ref m_selectedStyleTemplate, value); }
    public string CustomStyleNotes { get => m_customStyleNotes; set => SetField(ref m_customStyleNotes, value); }

    public string[] StyleTemplates { get; } =
    [
        "Sci-Fi Concept Art",
        "High Fantasy",
        "Cyberpunk",
        "Historical / Period",
        "Horror / Dark",
        "Anime / Manga",
        "Painterly / Fine Art",
        "Pixel Art / Retro",
        "Custom (describe below)",
    ];

    // ── Step 3: Prompts ─────────────────────────────────────────

    string m_generatedPrompts = "";
    int m_promptCount = 10;

    public string GeneratedPrompts { get => m_generatedPrompts; set => SetField(ref m_generatedPrompts, value); }
    public int PromptCount { get => m_promptCount; set => SetField(ref m_promptCount, Math.Clamp(value, 1, 50)); }

    // ── Step 4: Settings ────────────────────────────────────────

    int m_width = 1344;
    int m_height = 768;
    int m_steps = 20;
    double m_cfgScale = 7.0;

    public int Width { get => m_width; set => SetField(ref m_width, value); }
    public int Height { get => m_height; set => SetField(ref m_height, value); }
    public int Steps { get => m_steps; set => SetField(ref m_steps, value); }
    public double CfgScale { get => m_cfgScale; set => SetField(ref m_cfgScale, value); }

    public string[] ResolutionPresets { get; } =
    [
        "1344 × 768  (16:9 Landscape)",
        "1024 × 1024 (1:1 Square)",
        "768 × 1344  (9:16 Portrait)",
        "1216 × 832  (3:2 Landscape)",
        "832 × 1216  (2:3 Portrait)",
    ];

    string m_selectedResolution = "1344 × 768  (16:9 Landscape)";
    public string SelectedResolution
    {
        get => m_selectedResolution;
        set
        {
            if (!SetField(ref m_selectedResolution, value)) return;
            // Parse "W × H" from the label
            string[] parts = value.Split('×', '(');
            if (parts.Length >= 2
                && int.TryParse(parts[0].Trim(), out int w)
                && int.TryParse(parts[1].Trim(), out int h))
            {
                Width = w;
                Height = h;
            }
        }
    }

    // ── Ollama ──────────────────────────────────────────────────

    bool m_ollamaAvailable;
    string m_selectedModel = "";
    string m_ollamaStatus = "Checking Ollama…";
    bool m_isBusy;

    public bool OllamaAvailable { get => m_ollamaAvailable; set => SetField(ref m_ollamaAvailable, value); }
    public string SelectedModel { get => m_selectedModel; set => SetField(ref m_selectedModel, value); }
    public string OllamaStatus { get => m_ollamaStatus; set => SetField(ref m_ollamaStatus, value); }
    public bool IsBusy { get => m_isBusy; set { SetField(ref m_isBusy, value); OnPropertyChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !m_isBusy;
    public ObservableCollection<string> AvailableModels { get; } = [];

    // ── Commands ────────────────────────────────────────────────

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand GenerateStoryCommand { get; }
    public ICommand GeneratePromptsCommand { get; }
    public ICommand CancelGenerationCommand { get; }

    /// <summary>Raised when the wizard is complete and the project has been created.</summary>
    public event Action<string>? ProjectCreated;

    // ── Constructor ─────────────────────────────────────────────

    public WizardViewModel(OllamaClient ollama)
    {
        m_ollama = ollama;

        NextCommand = new RelayCommand(_ => GoNext(), _ => IsNotBusy);
        BackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack && IsNotBusy);
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        GenerateStoryCommand = new RelayCommand(_ => GenerateStory(), _ => OllamaAvailable && IsNotBusy);
        GeneratePromptsCommand = new RelayCommand(_ => GeneratePrompts(), _ => IsNotBusy && m_storySynopsis.Length > 0);
        CancelGenerationCommand = new RelayCommand(_ => CancelGeneration(), _ => IsBusy);

        _ = CheckOllama();
    }

    // ── Navigation ──────────────────────────────────────────────

    void GoNext()
    {
        if (m_currentStep < TotalSteps - 1)
            CurrentStep++;
        else
            CreateProject();
    }

    void GoBack()
    {
        if (m_currentStep > 0)
            CurrentStep--;
    }

    void BrowseFolder()
    {
        Microsoft.Win32.OpenFolderDialog dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose Project Folder",
            InitialDirectory = m_projectFolder,
        };
        if (dlg.ShowDialog() == true)
            ProjectFolder = dlg.FolderName;
    }

    // ── Ollama integration ──────────────────────────────────────

    async Task CheckOllama()
    {
        OllamaAvailable = await m_ollama.IsAvailableAsync();
        if (OllamaAvailable)
        {
            List<string> models = await m_ollama.ListModelsAsync();
            Dispatch(() =>
            {
                AvailableModels.Clear();
                foreach (string m in models) AvailableModels.Add(m);
                SelectedModel = models.FirstOrDefault(m => m.Contains("llama", StringComparison.OrdinalIgnoreCase))
                    ?? models.FirstOrDefault(m => m.Contains("mistral", StringComparison.OrdinalIgnoreCase))
                    ?? models.FirstOrDefault() ?? "";
                OllamaStatus = $"✓ Ollama online — {models.Count} model{(models.Count == 1 ? "" : "s")}";
            });
        }
        else
        {
            OllamaStatus = "Ollama not found at localhost:11434 — manual entry mode";
        }
    }

    async void GenerateStory()
    {
        if (!OllamaAvailable || SelectedModel.Length == 0) return;
        IsBusy = true;
        OllamaStatus = "Generating story synopsis…";

        string basePrompt = m_storySynopsis.Length > 0
            ? $"Expand and enrich this game setting synopsis into a detailed 2-3 paragraph description suitable for concept art direction. Keep all existing names and details, add vivid visual descriptions:\n\n{m_storySynopsis}"
            : $"Write a 2-3 paragraph game setting synopsis for a project called \"{m_projectName}\". Include visual descriptions of the world, key characters, and locations. Make it suitable for guiding concept art generation.";

        string system = "You are a creative director helping design game concept art. Write vivid, visual descriptions that would guide an artist. Focus on the look and feel — colors, materials, lighting, mood, architecture, character design.";

        try
        {
            m_cts = new CancellationTokenSource();
            string result = "";
            await Task.Run(async () =>
            {
                result = await m_ollama.GenerateAsync(SelectedModel, basePrompt, system,
                    onToken: token => Dispatch(() =>
                    {
                        StorySynopsis += token;
                        OllamaStatus = $"Generating… ({StorySynopsis.Length} chars)";
                    }),
                    ct: m_cts.Token);
            });

            // Auto-extract glossary terms
            if (StorySynopsis.Length > 0)
                ExtractGlossaryTerms();

            OllamaStatus = $"✓ Synopsis generated ({StorySynopsis.Length} chars)";
        }
        catch (OperationCanceledException)
        {
            OllamaStatus = "Generation cancelled.";
        }
        catch (Exception ex)
        {
            OllamaStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    async void GeneratePrompts()
    {
        if (m_storySynopsis.Length == 0)
        {
            OllamaStatus = "Write or generate a story synopsis first.";
            return;
        }

        IsBusy = true;
        OllamaStatus = "Generating art prompts…";

        string styleGuide = m_selectedStyleTemplate == "Custom (describe below)"
            ? m_customStyleNotes
            : m_selectedStyleTemplate;

        string prompt = $"""
            Based on this game setting:

            {m_storySynopsis}

            Generate exactly {m_promptCount} concept art prompts. Art style: {styleGuide}.
            {(m_customStyleNotes.Length > 0 && m_selectedStyleTemplate != "Custom (describe below)" ? $"Additional style notes: {m_customStyleNotes}" : "")}

            Use this exact format for each prompt:

            PROMPT 01 — "Title of the Scene"
            Detailed visual description of the scene. Describe the composition, foreground, midground, background, lighting, color palette, mood, time of day.

            Style direction paragraph describing artistic approach, rendering style, reference artists or aesthetics.

            PROMPT 02 — "Next Title"
            ...

            Make each prompt unique — vary locations, characters, times of day, moods, and compositions.
            Focus on painterly, cinematic compositions that would make stunning concept art.
            Number them sequentially from 01 to {m_promptCount:D2}.
            """;

        string system = "You are an expert concept art director. Generate detailed, vivid art prompts in the exact format requested. Each prompt should be 3-5 sentences of scene description followed by 1-2 sentences of style direction. Do not add any preamble or explanation — just the prompts.";

        try
        {
            m_cts = new CancellationTokenSource();
            GeneratedPrompts = "";
            await Task.Run(async () =>
            {
                await m_ollama.GenerateAsync(SelectedModel, prompt, system,
                    onToken: token => Dispatch(() =>
                    {
                        GeneratedPrompts += token;
                        OllamaStatus = $"Generating prompts… ({GeneratedPrompts.Split("PROMPT", StringSplitOptions.None).Length - 1} found)";
                    }),
                    ct: m_cts.Token);
            });
            OllamaStatus = $"✓ Prompts generated";
        }
        catch (OperationCanceledException)
        {
            OllamaStatus = "Generation cancelled.";
        }
        catch (Exception ex)
        {
            OllamaStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    void CancelGeneration()
    {
        m_cts?.Cancel();
    }

    void ExtractGlossaryTerms()
    {
        // Simple heuristic: find capitalized multi-word names and standalone capitalized words
        // that appear to be proper nouns
        HashSet<string> terms = [];
        string[] words = m_storySynopsis.Split([' ', '\n', '\r', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i].Trim('\"', '\'', '(', ')');
            if (w.Length < 2) continue;
            if (char.IsUpper(w[0]) && !IsCommonWord(w))
            {
                // Check for multi-word names (e.g., "New Tokyo")
                if (i + 1 < words.Length)
                {
                    string next = words[i + 1].Trim('\"', '\'', '(', ')', ',', '.', '!', '?');
                    if (next.Length > 1 && char.IsUpper(next[0]) && !IsCommonWord(next))
                    {
                        terms.Add($"{w} {next}");
                        i++; // skip next
                        continue;
                    }
                }
                terms.Add(w);
            }
        }
        GlossaryTerms = string.Join(", ", terms.Take(30));
    }

    static bool IsCommonWord(string w)
    {
        string[] common = ["The", "This", "That", "These", "Those", "A", "An", "In", "On",
            "At", "To", "For", "With", "From", "By", "Is", "Are", "Was", "Were", "Has",
            "Have", "Had", "But", "And", "Or", "Not", "No", "It", "Its", "As", "If",
            "Each", "Every", "All", "Any", "Both", "Few", "More", "Most", "Other",
            "Some", "Such", "Than", "Too", "Very", "Can", "Will", "Just", "Should",
            "Now", "Also", "Into", "Over", "After", "Before", "Between", "Under",
            "Through", "During", "Without", "Within", "Along", "Following",
            "Across", "Behind", "Beyond", "Plus", "Except", "Up", "Out", "Around",
            "Down", "Off", "Above", "Near", "Here", "There", "Where", "When",
            "While", "They", "Them", "Their", "She", "He", "Her", "His", "We",
            "Our", "You", "Your", "Who", "What", "Which", "How", "Why",
            "Write", "Generate", "Create", "Make", "Style", "Scene", "Art"];
        return common.Contains(w, StringComparer.OrdinalIgnoreCase);
    }

    // ── Create project ──────────────────────────────────────────

    void CreateProject()
    {
        string projectDir = Path.Combine(m_projectFolder, SanitizeFolderName(m_projectName));
        Directory.CreateDirectory(projectDir);

        // Write universe.json
        var universe = new
        {
            glossary = m_storySynopsis.Length > 200
                ? m_storySynopsis[..200] + "…"
                : m_storySynopsis,
            properNouns = m_glossaryTerms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            storyFilePatterns = new[] { "*.txt", "*.md" },
        };
        File.WriteAllText(
            Path.Combine(projectDir, "universe.json"),
            JsonSerializer.Serialize(universe, new JsonSerializerOptions { WriteIndented = true }));

        // Write story file
        if (m_storySynopsis.Length > 0)
            File.WriteAllText(Path.Combine(projectDir, "Story.md"), $"# {m_projectName}\n\n{m_storySynopsis}");

        // Write art_directions.json from template
        string artDirections = BuildArtDirectionsJson();
        File.WriteAllText(Path.Combine(projectDir, "art_directions.json"), artDirections);

        // Write creative_pools.json from template
        string creativePools = BuildCreativePoolsJson();
        File.WriteAllText(Path.Combine(projectDir, "creative_pools.json"), creativePools);

        // Write ArtPrompts.txt
        string prompts = m_generatedPrompts.Length > 0
            ? m_generatedPrompts
            : BuildTemplatePrompts();
        File.WriteAllText(Path.Combine(projectDir, "ArtPrompts.txt"), prompts);

        // Write spark_project.json
        SparkProject proj = new()
        {
            Name = m_projectName,
            OutputDir = "Concept",
            StoryFiles = m_storySynopsis.Length > 0 ? ["Story.md"] : [],
            PromptsFile = "ArtPrompts.txt",
            UniverseFile = "universe.json",
            ArtDirectionsFile = "art_directions.json",
            CreativePoolsFile = "creative_pools.json",
            DefaultSettings = new ProjectSettings
            {
                Width = m_width,
                Height = m_height,
                Steps = m_steps,
                CfgScale = m_cfgScale,
                Sampler = "DPM++ 2M SDE",
                Scheduler = "karras",
            },
        };
        string projectFilePath = Path.Combine(projectDir, "spark_project.json");
        proj.ProjectDir = projectDir;
        proj.ProjectFilePath = projectFilePath;
        proj.Save();

        ProjectCreated?.Invoke(projectFilePath);
    }

    string BuildTemplatePrompts()
    {
        string style = m_selectedStyleTemplate == "Custom (describe below)"
            ? m_customStyleNotes.Length > 0 ? m_customStyleNotes : "cinematic concept art"
            : m_selectedStyleTemplate.ToLowerInvariant();

        return $"""
            PROMPT 01 — "The World at First Glance"
            A sweeping establishing shot of the world of {m_projectName}. Show the environment from a high vantage point, revealing the scale and mood of the setting. Include key architectural or natural landmarks.

            Rendered in {style} style with dramatic lighting and rich atmospheric perspective.

            PROMPT 02 — "The Protagonist"
            A full character portrait of the main character. Show them in their signature outfit or armor, with characteristic pose and expression. The background hints at their origin or motivation.

            {style} style, painterly rendering with attention to material textures and lighting.

            PROMPT 03 — "A Moment of Conflict"
            A dynamic action scene capturing a pivotal confrontation. Multiple characters or forces clash in a visually striking composition. Include environmental storytelling details.

            High-energy composition with dramatic camera angle, {style} style, cinematic color grading.
            """;
    }

    string BuildArtDirectionsJson()
    {
        string style = m_selectedStyleTemplate;
        // Build style-appropriate direction groups
        var groups = new[]
        {
            new { category = "Mood", emoji = "🌙", items = new[]
            {
                new { label = "Dark & Ominous", promptAdd = "dark atmosphere, ominous mood, deep shadows, foreboding", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Bright & Hopeful", promptAdd = "bright atmosphere, hopeful mood, warm golden light, uplifting", negativeAdd = "dark, gloomy", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Mysterious", promptAdd = "mysterious atmosphere, ethereal fog, hidden details, enigmatic", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Epic & Grand", promptAdd = "epic scale, grand vista, sweeping composition, awe-inspiring", negativeAdd = "small, cramped", cfgNudge = (double?)1.0, stepsNudge = (int?)5 },
            }},
            new { category = "Lighting", emoji = "💡", items = new[]
            {
                new { label = "Golden Hour", promptAdd = "golden hour lighting, warm sunset tones, long shadows", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Blue Hour", promptAdd = "blue hour, twilight, cool ambient light, serene", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Dramatic Rim Light", promptAdd = "dramatic rim lighting, backlit, silhouette edges, high contrast", negativeAdd = "flat lighting", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Neon / Artificial", promptAdd = "neon lighting, artificial light sources, colorful reflections", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
            }},
            new { category = "Composition", emoji = "📐", items = new[]
            {
                new { label = "Wide Establishing", promptAdd = "wide establishing shot, panoramic composition, environmental storytelling", negativeAdd = "close-up", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Intimate Close-up", promptAdd = "close-up shot, intimate framing, detailed textures, shallow depth of field", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Low Angle Hero", promptAdd = "low angle shot, heroic perspective, imposing, powerful", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
                new { label = "Bird's Eye", promptAdd = "bird's eye view, top-down perspective, map-like composition", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
            }},
            new { category = style, emoji = "🎨", items = new[]
            {
                new { label = "More Detail", promptAdd = "highly detailed, intricate details, fine textures, sharp focus", negativeAdd = "blurry, simple", cfgNudge = (double?)1.5, stepsNudge = (int?)5 },
                new { label = "Painterly Loose", promptAdd = "painterly style, loose brushstrokes, impressionistic, artistic", negativeAdd = "photorealistic", cfgNudge = (double?)-1.0, stepsNudge = (int?)null },
                new { label = "Photorealistic", promptAdd = "photorealistic, hyperrealistic, real photography, 8k", negativeAdd = "painting, illustration, cartoon", cfgNudge = (double?)2.0, stepsNudge = (int?)5 },
                new { label = "Stylized", promptAdd = "stylized, graphic design, bold shapes, strong silhouettes", negativeAdd = "", cfgNudge = (double?)null, stepsNudge = (int?)null },
            }},
        };

        var root = new { groups };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    string BuildCreativePoolsJson()
    {
        var pools = new
        {
            colorThemes = new[] { "bold saturated colors", "muted earth tones", "monochromatic palette", "complementary color scheme", "warm analogous palette", "cool blue-green tones", "high contrast black and gold", "pastel dreamlike colors" },
            compositions = new[] { "rule of thirds", "centered symmetrical", "diagonal dynamic", "framing within frame", "leading lines", "wide panoramic", "intimate close crop", "layered depth planes" },
            inspirations = new[] { "concept art style", "matte painting look", "digital illustration", "oil painting aesthetic", "watercolor wash", "graphic novel style", "studio ghibli inspired", "art nouveau influenced" },
            moods = new[] { "atmospheric and moody", "bright and optimistic", "dark and mysterious", "serene and peaceful", "tense and dramatic", "whimsical and playful", "melancholic and reflective", "epic and awe-inspiring" },
            lightingSetups = new[] { "dramatic side lighting", "soft ambient occlusion", "volumetric god rays", "neon rim lighting", "candlelight warmth", "overcast diffused", "harsh noon sun", "bioluminescent glow" },
            samplerHints = new[] { "", "", "", "DPM++ 2M SDE", "Euler a", "" },
        };
        return JsonSerializer.Serialize(pools, new JsonSerializerOptions { WriteIndented = true });
    }

    static string SanitizeFolderName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ── INPC ────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);
}
