using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Common.Wpf.Input;
using Common.Wpf.ViewModels;
using NAudio.Wave;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// ViewModel for the Sound FX tab. Short-clip generation with categories,
/// batch variants, A/B comparison, and a waveform visualizer.
/// Uses the same MusicGen backend as the Music tab (short durations + SFX prompts).
/// </summary>
sealed class SfxViewModel : ObservableObject, IDisposable
{
    readonly MusicGenClient m_client;
    readonly LogViewModel m_log;
    readonly DispatcherTimer m_playTimer;
    WaveOutEvent? m_player;
    AudioFileReader? m_audioReader;
    CancellationTokenSource? m_cts;

    // ── Track library ───────────────────────────────────────────

    public ObservableCollection<SfxTrack> Tracks { get; } = [];
    public ObservableCollection<SfxTrack> FilteredTracks { get; } = [];
    string m_catalogPath = "";
    int m_nextId = 1;

    // ── Generation settings ─────────────────────────────────────

    string m_prompt = "";
    double m_duration = 2.0;
    double m_temperature = 1.0;
    double m_cfgCoefficient = 3.5;
    int m_batchCount = 3;
    bool m_isGenerating;
    string m_selectedCategory = "All";

    public string Prompt { get => m_prompt; set => SetField(ref m_prompt, value); }
    public double Duration { get => m_duration; set => SetField(ref m_duration, Math.Clamp(value, 0.5, 10)); }
    public double Temperature { get => m_temperature; set => SetField(ref m_temperature, Math.Clamp(value, 0.1, 2.0)); }
    public double CfgCoefficient { get => m_cfgCoefficient; set => SetField(ref m_cfgCoefficient, Math.Clamp(value, 1, 10)); }
    public int BatchCount { get => m_batchCount; set => SetField(ref m_batchCount, Math.Clamp(value, 1, 8)); }
    public bool IsGenerating { get => m_isGenerating; set { SetField(ref m_isGenerating, value); OnPropertyChanged(nameof(IsNotGenerating)); } }
    public bool IsNotGenerating => !m_isGenerating;

    public string SelectedCategory
    {
        get => m_selectedCategory;
        set { if (SetField(ref m_selectedCategory, value)) RebuildFilteredTracks(); }
    }

    // ── Playback state ──────────────────────────────────────────

    SfxTrack? m_selectedTrack;
    MusicAnalysis? m_analysis;
    double m_playbackPosition;
    bool m_isPlaying;
    string m_statusText = "MusicGen not checked yet.";
    bool m_musicGenAvailable;

    public SfxTrack? SelectedTrack
    {
        get => m_selectedTrack;
        set
        {
            if (!SetField(ref m_selectedTrack, value)) return;
            StopPlayback();
            if (value is not null && value.Exists)
                LoadAnalysis(value);
            else
                Analysis = null;
        }
    }

    public MusicAnalysis? Analysis { get => m_analysis; set => SetField(ref m_analysis, value); }
    public double PlaybackPosition { get => m_playbackPosition; set => SetField(ref m_playbackPosition, value); }
    public bool IsPlaying { get => m_isPlaying; set => SetField(ref m_isPlaying, value); }
    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool MusicGenAvailable { get => m_musicGenAvailable; set => SetField(ref m_musicGenAvailable, value); }

    // ── Categories + Presets ────────────────────────────────────

    public string[] Categories { get; } =
        ["All", "UI", "Combat", "Nature", "Movement", "Magic", "Mechanical", "Ambient", "Creature", "Impact", "Voice"];

    public SfxPreset[] SfxPresets { get; } =
    [
        new("🖱 Button Click", "UI", "short UI button click, clean digital interface beep, crisp and satisfying"),
        new("🖱 Menu Open", "UI", "UI menu sliding open, soft whoosh with digital chime, polished interface"),
        new("🖱 Error Buzz", "UI", "UI error buzzer, short harsh digital rejection sound, wrong action"),
        new("🖱 Notification", "UI", "gentle notification chime, pleasant digital ping, soft and clear"),
        new("🖱 Coin Collect", "UI", "coin pickup sound, bright metallic clink, rewarding collect jingle"),
        new("⚔ Sword Swing", "Combat", "sword swing whoosh, fast sharp blade cutting through air, metallic"),
        new("⚔ Sword Clash", "Combat", "metal sword clash impact, ringing steel on steel, sparks"),
        new("⚔ Arrow Fire", "Combat", "bow arrow release, taut string snap followed by arrow whistle"),
        new("⚔ Explosion", "Combat", "large fiery explosion, deep boom with debris scatter, powerful blast"),
        new("⚔ Shield Block", "Combat", "shield block impact, heavy metallic thud with reverb, defensive"),
        new("⚔ Gunshot", "Combat", "single gunshot, sharp crack with echo, distant reverb"),
        new("🌿 Wind Gust", "Nature", "wind gust blowing through trees, rustling leaves, natural breeze"),
        new("🌿 Rain Drops", "Nature", "gentle rain drops on stone, natural rainfall, calming patter"),
        new("🌿 Thunder", "Nature", "distant thunder rumble, deep rolling boom, ominous storm"),
        new("🌿 Campfire", "Nature", "campfire crackling, warm wood popping and fire snapping"),
        new("🌿 Water Splash", "Nature", "water splash, object falling into pond, liquid impact with ripples"),
        new("👣 Footstep Stone", "Movement", "single footstep on stone floor, hard sole boot, dungeon echo"),
        new("👣 Footstep Grass", "Movement", "footstep on grass, soft compression of vegetation, outdoor"),
        new("👣 Door Open", "Movement", "heavy wooden door creaking open slowly, old hinges, dungeon"),
        new("👣 Jump Land", "Movement", "character landing from jump, impact on ground, slight grunt"),
        new("✨ Magic Cast", "Magic", "magic spell casting, rising arcane energy whoosh with sparkle"),
        new("✨ Heal Spell", "Magic", "healing spell sound, warm glowing chime, gentle restorative magic"),
        new("✨ Dark Magic", "Magic", "dark magic spell, ominous low rumble with crackling energy"),
        new("✨ Teleport", "Magic", "teleport whoosh, reality warping zap, instant displacement sound"),
        new("✨ Power Up", "Magic", "power up ascending chime, energy building to climax, buff activated"),
        new("⚙ Gear Turn", "Mechanical", "mechanical gear clicking and turning, clockwork mechanism"),
        new("⚙ Lever Pull", "Mechanical", "heavy stone lever being pulled, grinding mechanism activating"),
        new("⚙ Steam Hiss", "Mechanical", "steam valve releasing, pressurized hiss, industrial"),
        new("🌊 Ocean Wave", "Ambient", "ocean wave crashing on shore, sea foam, coastal ambience"),
        new("🌊 Cave Drip", "Ambient", "water dripping in cave, echo, underground atmosphere"),
        new("🌊 Night Crickets", "Ambient", "nighttime crickets chirping, peaceful summer evening ambient"),
        new("🐉 Monster Roar", "Creature", "large monster roar, deep guttural growl, terrifying beast"),
        new("🐉 Wolf Howl", "Creature", "wolf howling at moon, lonely distant canine cry, nighttime"),
        new("🐉 Dragon Fire", "Creature", "dragon breathing fire, deep inhale then rushing flame blast"),
        new("🐉 Insect Buzz", "Creature", "large insect buzzing wings, menacing flying creature approach"),
        new("💥 Punch Hit", "Impact", "fist punch body impact, meaty hit with slight crunch"),
        new("💥 Glass Break", "Impact", "glass shattering, window breaking into fragments, sharp crash"),
        new("💥 Wood Break", "Impact", "wooden object breaking, splintering wood, crate smashing"),
        new("💥 Heavy Thud", "Impact", "heavy object hitting ground, massive deep thud with shake"),
        new("🗣 Pain Grunt", "Voice", "short male pain grunt, combat hit reaction, quick and sharp"),
        new("🗣 Death Cry", "Voice", "dramatic death cry, falling in defeat, final gasp"),
        new("🗣 Victory Shout", "Voice", "triumphant victory shout, excited celebration, energetic"),
    ];

    // ── Commands ────────────────────────────────────────────────

    public ICommand GenerateCommand { get; }
    public ICommand GenerateBatchCommand { get; }
    public ICommand CancelGenerateCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DeleteTrackCommand { get; }
    public ICommand RateTrackCommand { get; }
    public ICommand RegenerateVariantCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand CategoryFilterCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public SfxViewModel(MusicGenClient client, LogViewModel log)
    {
        m_client = client;
        m_log = log;

        GenerateCommand = new RelayCommand(_ => GenerateSingle(), _ => IsNotGenerating && m_prompt.Length > 0);
        GenerateBatchCommand = new RelayCommand(_ => GenerateBatch(), _ => IsNotGenerating && m_prompt.Length > 0);
        CancelGenerateCommand = new RelayCommand(_ => m_cts?.Cancel(), _ => IsGenerating);
        PlayPauseCommand = new RelayCommand(_ => TogglePlayPause(), _ => m_selectedTrack?.Exists == true);
        StopCommand = new RelayCommand(_ => StopPlayback(), _ => m_isPlaying);
        DeleteTrackCommand = new RelayCommand(_ => DeleteTrack(), _ => m_selectedTrack is not null);
        RateTrackCommand = new RelayCommand(p => RateTrack(p));
        RegenerateVariantCommand = new RelayCommand(_ => RegenerateVariant(), _ => m_selectedTrack is not null && IsNotGenerating);
        ApplyPresetCommand = new RelayCommand(p => ApplyPreset(p as SfxPreset));
        CategoryFilterCommand = new RelayCommand(p => { if (p is string s) SelectedCategory = s; });

        m_playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        m_playTimer.Tick += OnPlayTimerTick;

        _ = CheckMusicGen();
    }

    // ── Project loading ─────────────────────────────────────────

    public void LoadProject(string projectDir)
    {
        StopPlayback();
        Tracks.Clear();
        FilteredTracks.Clear();

        string sfxDir = Path.Combine(projectDir, "SoundFX");
        Directory.CreateDirectory(sfxDir);
        m_catalogPath = Path.Combine(sfxDir, ".sfx_catalog.json");

        if (File.Exists(m_catalogPath))
        {
            try
            {
                string json = File.ReadAllText(m_catalogPath);
                List<SfxTrack>? tracks = JsonSerializer.Deserialize<List<SfxTrack>>(json);
                if (tracks is not null)
                {
                    foreach (SfxTrack? t in tracks.Where(t => !t.Deleted))
                    {
                        Tracks.Add(t);
                        if (t.Id >= m_nextId) m_nextId = t.Id + 1;
                    }
                }
            }
            catch { /* start fresh */ }
        }

        RebuildFilteredTracks();
        m_log.Log($"🔊 SFX: loaded {Tracks.Count} clips from {sfxDir}");
    }

    void SaveCatalog()
    {
        if (m_catalogPath.Length == 0) return;
        try
        {
            string json = JsonSerializer.Serialize(Tracks.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(m_catalogPath, json);
        }
        catch { /* non-fatal */ }
    }

    void RebuildFilteredTracks()
    {
        FilteredTracks.Clear();
        foreach (SfxTrack t in Tracks)
        {
            if (m_selectedCategory == "All" || t.Category == m_selectedCategory)
                FilteredTracks.Add(t);
        }
    }

    // ── MusicGen check ──────────────────────────────────────────

    async Task CheckMusicGen()
    {
        MusicGenAvailable = await m_client.IsAvailableAsync();
        StatusText = MusicGenAvailable
            ? "✓ MusicGen online — ready for SFX generation"
            : "MusicGen not found — start: python -m audiocraft.demos.musicgen_app --server_port 7860";
    }

    // ── Preset application ──────────────────────────────────────

    void ApplyPreset(SfxPreset? preset)
    {
        if (preset is null) return;
        Prompt = preset.Prompt;
        SelectedCategory = preset.Category;
    }

    // ── Generation ──────────────────────────────────────────────

    async void GenerateSingle()
    {
        if (m_prompt.Length == 0 || m_catalogPath.Length == 0) return;
        await GenerateOne(m_prompt, m_temperature, 0);
    }

    async void GenerateBatch()
    {
        if (m_prompt.Length == 0 || m_catalogPath.Length == 0) return;

        IsGenerating = true;
        m_cts = new CancellationTokenSource();

        try
        {
            for (int i = 0; i < m_batchCount; i++)
            {
                if (m_cts.IsCancellationRequested) break;
                // Nudge temperature for each variant
                double tempVariance = m_temperature + (i * 0.15) - (m_batchCount * 0.075);
                double t = Math.Clamp(tempVariance, 0.2, 1.9);
                StatusText = $"Generating variant {i + 1} of {m_batchCount}…";
                await GenerateOne(m_prompt, t, 0, skipBusyFlag: true);
            }
            StatusText = $"✓ Batch complete — {m_batchCount} variants generated";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Batch cancelled.";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    async Task GenerateOne(string prompt, double temperature, int parentId, bool skipBusyFlag = false)
    {
        if (!skipBusyFlag) IsGenerating = true;
        m_cts ??= new CancellationTokenSource();
        string sfxDir = Path.GetDirectoryName(m_catalogPath) ?? "";

        int id = m_nextId++;
        string title = ExtractTitle(prompt);
        string category = DetectCategory(prompt);
        string fileName = $"sfx_{id:D3}_{SanitizeFileName(title)}.wav";

        MusicGenSettings settings = new()
        {
            Prompt = prompt,
            Duration = m_duration,
            Temperature = temperature,
            CfgCoefficient = m_cfgCoefficient,
        };

        try
        {
            m_log.Log($"🔊 Generating: {prompt[..Math.Min(60, prompt.Length)]}… ({m_duration}s)");

            MusicGenResult result = await Task.Run(() =>
                m_client.GenerateAsync(settings, sfxDir, fileName,
                    onStatus: msg => Dispatch(() => StatusText = msg),
                    ct: m_cts.Token));

            if (result.Success && result.FilePath is not null)
            {
                MusicAnalysis analysis = await Task.Run(() => MusicAnalyzer.Analyze(result.FilePath));

                SfxTrack track = new()
                {
                    Id = id,
                    Title = title,
                    Prompt = prompt,
                    Category = category.Length > 0 ? category : m_selectedCategory != "All" ? m_selectedCategory : "",
                    FilePath = result.FilePath,
                    Duration = analysis.DurationSeconds,
                    Temperature = temperature,
                    CfgCoefficient = m_cfgCoefficient,
                    VibeTag = DescribeSfx(analysis),
                    ParentId = parentId,
                };

                Dispatch(() =>
                {
                    Tracks.Insert(0, track);
                    if (m_selectedCategory == "All" || track.Category == m_selectedCategory)
                        FilteredTracks.Insert(0, track);
                    SelectedTrack = track;
                    SaveCatalog();
                    m_log.Log($"🔊 Done: {title} ({analysis.DurationSeconds:F1}s) — {track.VibeTag}");
                });
            }
            else
            {
                StatusText = $"Generation failed: {result.Error}";
                m_log.Log($"🔊 Error: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            m_log.Log($"🔊 Error: {ex.Message}");
        }
        finally
        {
            if (!skipBusyFlag) IsGenerating = false;
        }
    }

    void RegenerateVariant()
    {
        if (m_selectedTrack is null) return;
        Prompt = m_selectedTrack.Prompt;
        Temperature = Math.Clamp(m_selectedTrack.Temperature + (Random.Shared.NextDouble() * 0.4 - 0.2), 0.3, 1.8);
        GenerateSingle();
    }

    // ── Playback ────────────────────────────────────────────────

    void TogglePlayPause()
    {
        if (m_isPlaying)
        {
            m_player?.Pause();
            IsPlaying = false;
            m_playTimer.Stop();
        }
        else
        {
            if (m_player is null && m_selectedTrack?.Exists == true)
                StartPlayback();
            else
                m_player?.Play();
            IsPlaying = true;
            m_playTimer.Start();
        }
    }

    void StartPlayback()
    {
        if (m_selectedTrack is null || !m_selectedTrack.Exists) return;
        StopPlayback();

        try
        {
            m_audioReader = new AudioFileReader(m_selectedTrack.FilePath);
            m_player = new WaveOutEvent();
            m_player.Init(m_audioReader);
            m_player.PlaybackStopped += (_, _) => Dispatch(() =>
            {
                IsPlaying = false;
                m_playTimer.Stop();
                PlaybackPosition = 0;
            });
            m_player.Play();
            IsPlaying = true;
            m_playTimer.Start();
        }
        catch (Exception ex)
        {
            m_log.Log($"🔊 Playback error: {ex.Message}");
            StatusText = $"Playback error: {ex.Message}";
        }
    }

    void StopPlayback()
    {
        m_playTimer.Stop();
        m_player?.Stop();
        m_player?.Dispose();
        m_player = null;
        m_audioReader?.Dispose();
        m_audioReader = null;
        IsPlaying = false;
        PlaybackPosition = 0;
    }

    void OnPlayTimerTick(object? sender, EventArgs e)
    {
        if (m_audioReader is null) return;
        double total = m_audioReader.TotalTime.TotalSeconds;
        if (total > 0)
            PlaybackPosition = m_audioReader.CurrentTime.TotalSeconds / total;
    }

    // ── Analysis ────────────────────────────────────────────────

    void LoadAnalysis(SfxTrack track)
    {
        try { Analysis = MusicAnalyzer.Analyze(track.FilePath); }
        catch { Analysis = null; }
    }

    // ── Track actions ───────────────────────────────────────────

    void RateTrack(object? param)
    {
        if (m_selectedTrack is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;
        m_selectedTrack.Rating = Math.Clamp(rating, 1, 5);
        SaveCatalog();
        m_log.Log($"🔊 Rated {m_selectedTrack.DisplayName}: {m_selectedTrack.RatingStars}");

        if (rating <= 2)
        {
            m_log.Log("🔊 Low rating — regenerating variant…");
            m_selectedTrack.Deleted = true;
            Tracks.Remove(m_selectedTrack);
            FilteredTracks.Remove(m_selectedTrack);
            SaveCatalog();
            RegenerateVariant();
        }
    }

    void DeleteTrack()
    {
        if (m_selectedTrack is null) return;
        StopPlayback();
        m_selectedTrack.Deleted = true;
        string name = m_selectedTrack.DisplayName;
        Tracks.Remove(m_selectedTrack);
        FilteredTracks.Remove(m_selectedTrack);
        SelectedTrack = FilteredTracks.FirstOrDefault();
        SaveCatalog();
        m_log.Log($"🔊 Deleted: {name}");
    }

    // ── Helpers ─────────────────────────────────────────────────

    static string ExtractTitle(string prompt)
    {
        string[] words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string title = string.Join(" ", words.Take(4));
        return title.Length > 35 ? title[..35] : title;
    }

    static string SanitizeFileName(string name)
        => string.Concat(name.ToLowerInvariant().Replace(' ', '_')
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));

    static string DescribeSfx(MusicAnalysis a)
    {
        string attack = a.EnvelopeDb.Length > 1 && a.EnvelopeDb[0] > a.RmsDb + 3 ? "sharp attack" : "soft onset";
        string body = a.RmsDb switch
        {
            > -10 => "loud",
            > -20 => "moderate",
            _ => "quiet"
        };
        string tone = a.SpectralCentroid switch
        {
            > 4000 => "bright/crispy",
            > 2000 => "mid-range",
            _ => "deep/bassy"
        };
        return $"{attack}, {body}, {tone}";
    }

    /// <summary>Auto-detect category from prompt keywords.</summary>
    static string DetectCategory(string prompt)
    {
        string p = prompt.ToLowerInvariant();
        if (p.Contains("ui") || p.Contains("button") || p.Contains("click") || p.Contains("menu") ||
            p.Contains("notification") || p.Contains("interface") || p.Contains("coin")) return "UI";
        if (p.Contains("sword") || p.Contains("arrow") || p.Contains("explosion") || p.Contains("shield") ||
            p.Contains("combat") || p.Contains("gun") || p.Contains("weapon") || p.Contains("battle")) return "Combat";
        if (p.Contains("wind") || p.Contains("rain") || p.Contains("thunder") || p.Contains("fire") ||
            p.Contains("water") || p.Contains("tree") || p.Contains("nature")) return "Nature";
        if (p.Contains("footstep") || p.Contains("door") || p.Contains("jump") || p.Contains("walk") ||
            p.Contains("run") || p.Contains("climb")) return "Movement";
        if (p.Contains("magic") || p.Contains("spell") || p.Contains("heal") || p.Contains("teleport") ||
            p.Contains("arcane") || p.Contains("power up") || p.Contains("enchant")) return "Magic";
        if (p.Contains("gear") || p.Contains("machine") || p.Contains("steam") || p.Contains("lever") ||
            p.Contains("mechanical") || p.Contains("clockwork")) return "Mechanical";
        if (p.Contains("ocean") || p.Contains("cave") || p.Contains("ambient") || p.Contains("cricket") ||
            p.Contains("atmosphere")) return "Ambient";
        if (p.Contains("monster") || p.Contains("wolf") || p.Contains("dragon") || p.Contains("insect") ||
            p.Contains("creature") || p.Contains("beast") || p.Contains("growl") || p.Contains("roar")) return "Creature";
        if (p.Contains("punch") || p.Contains("glass") || p.Contains("break") || p.Contains("impact") ||
            p.Contains("crash") || p.Contains("smash") || p.Contains("thud") || p.Contains("hit")) return "Impact";
        if (p.Contains("voice") || p.Contains("grunt") || p.Contains("cry") || p.Contains("shout") ||
            p.Contains("scream") || p.Contains("gasp")) return "Voice";
        return "";
    }

    public void Dispose()
    {
        StopPlayback();
        m_client.Dispose();
    }
}

/// <summary>Named preset for quick SFX generation.</summary>
sealed record SfxPreset(string Label, string Category, string Prompt);
