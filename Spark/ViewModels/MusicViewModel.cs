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
/// ViewModel for the Music Director tab. Owns prompt-based music generation,
/// playback, track library, analysis feedback, and visualizer state.
/// </summary>
sealed class MusicViewModel : ObservableObject, IDisposable
{
    readonly MusicGenClient m_client = new();
    readonly LogViewModel m_log;
    readonly DispatcherTimer m_playTimer;
    WaveOutEvent? m_player;
    AudioFileReader? m_audioReader;
    CancellationTokenSource? m_cts;

    // ── Track library ───────────────────────────────────────────

    public ObservableCollection<MusicTrack> Tracks { get; } = [];
    string m_catalogPath = "";
    int m_nextId = 1;

    // ── Generation settings ─────────────────────────────────────

    string m_prompt = "";
    double m_duration = 10.0;
    double m_temperature = 1.0;
    double m_cfgCoefficient = 3.0;
    bool m_isGenerating;

    public string Prompt { get => m_prompt; set => SetField(ref m_prompt, value); }
    public double Duration { get => m_duration; set => SetField(ref m_duration, Math.Clamp(value, 1, 60)); }
    public double Temperature { get => m_temperature; set => SetField(ref m_temperature, Math.Clamp(value, 0.1, 2.0)); }
    public double CfgCoefficient { get => m_cfgCoefficient; set => SetField(ref m_cfgCoefficient, Math.Clamp(value, 1, 10)); }
    public bool IsGenerating { get => m_isGenerating; set { SetField(ref m_isGenerating, value); OnPropertyChanged(nameof(IsNotGenerating)); } }
    public bool IsNotGenerating => !m_isGenerating;

    // ── Playback state ──────────────────────────────────────────

    MusicTrack? m_selectedTrack;
    MusicAnalysis? m_analysis;
    double m_playbackPosition;
    bool m_isPlaying;
    string m_statusText = "MusicGen not checked yet.";
    bool m_musicGenAvailable;

    public MusicTrack? SelectedTrack
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

    // ── Prompt helpers ──────────────────────────────────────────

    public string[] MoodPresets { get; } =
    [
        "epic orchestral battle music, intense and heroic",
        "calm ambient exploration, mysterious and serene",
        "dark foreboding atmosphere, low brass and strings",
        "upbeat adventure theme, cheerful and energetic",
        "sad emotional piano, melancholic and reflective",
        "tense stealth music, minimal and suspenseful",
        "triumphant victory fanfare, brass and percussion",
        "eerie horror ambient, unsettling and atmospheric",
        "celtic folk tavern music, lively fiddle and flute",
        "electronic cyberpunk, pulsing synths and beats",
    ];

    // ── Commands ────────────────────────────────────────────────

    public ICommand GenerateCommand { get; }
    public ICommand CancelGenerateCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DeleteTrackCommand { get; }
    public ICommand RateTrackCommand { get; }
    public ICommand RegenerateVariantCommand { get; }
    public ICommand ApplyMoodPresetCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MusicViewModel(LogViewModel log)
    {
        m_log = log;

        GenerateCommand = new RelayCommand(_ => Generate(), _ => IsNotGenerating && m_prompt.Length > 0);
        CancelGenerateCommand = new RelayCommand(_ => m_cts?.Cancel(), _ => IsGenerating);
        PlayPauseCommand = new RelayCommand(_ => TogglePlayPause(), _ => m_selectedTrack?.Exists == true);
        StopCommand = new RelayCommand(_ => StopPlayback(), _ => m_isPlaying);
        DeleteTrackCommand = new RelayCommand(_ => DeleteTrack(), _ => m_selectedTrack is not null);
        RateTrackCommand = new RelayCommand(p => RateTrack(p));
        RegenerateVariantCommand = new RelayCommand(_ => RegenerateVariant(), _ => m_selectedTrack is not null && IsNotGenerating);
        ApplyMoodPresetCommand = new RelayCommand(p => { if (p is string s) Prompt = s; });

        m_playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        m_playTimer.Tick += OnPlayTimerTick;

        _ = CheckMusicGen();
    }

    // ── Project loading ─────────────────────────────────────────

    public void LoadProject(string projectDir)
    {
        StopPlayback();
        Tracks.Clear();

        string musicDir = Path.Combine(projectDir, "Music");
        Directory.CreateDirectory(musicDir);
        m_catalogPath = Path.Combine(musicDir, ".music_catalog.json");

        if (File.Exists(m_catalogPath))
        {
            try
            {
                string json = File.ReadAllText(m_catalogPath);
                List<MusicTrack>? tracks = JsonSerializer.Deserialize<List<MusicTrack>>(json);
                if (tracks is not null)
                {
                    foreach (MusicTrack? t in tracks.Where(t => !t.Deleted))
                    {
                        Tracks.Add(t);
                        if (t.Id >= m_nextId) m_nextId = t.Id + 1;
                    }
                }
            }
            catch { /* start fresh */ }
        }

        m_log.Log($"♫ Music: loaded {Tracks.Count} tracks from {musicDir}");
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

    // ── MusicGen check ──────────────────────────────────────────

    async Task CheckMusicGen()
    {
        MusicGenAvailable = await m_client.IsAvailableAsync();
        StatusText = MusicGenAvailable
            ? "✓ MusicGen online at localhost:7860"
            : "MusicGen not found — start: python -m audiocraft.demos.musicgen_app --server_port 7860";
    }

    // ── Generation ──────────────────────────────────────────────

    async void Generate()
    {
        if (m_prompt.Length == 0 || m_catalogPath.Length == 0) return;

        IsGenerating = true;
        m_cts = new CancellationTokenSource();
        string musicDir = Path.GetDirectoryName(m_catalogPath) ?? "";

        int id = m_nextId++;
        string title = ExtractTitle(m_prompt);
        string fileName = $"track_{id:D3}_{SanitizeFileName(title)}.wav";

        MusicGenSettings settings = new()
        {
            Prompt = m_prompt,
            Duration = m_duration,
            Temperature = m_temperature,
            CfgCoefficient = m_cfgCoefficient,
        };

        try
        {
            StatusText = $"Generating {title} ({m_duration}s)…";
            m_log.Log($"♫ Generating: {m_prompt[..Math.Min(80, m_prompt.Length)]}…");

            MusicGenResult result = await Task.Run(() =>
                m_client.GenerateAsync(settings, musicDir, fileName,
                    onStatus: msg => Dispatch(() => StatusText = msg),
                    ct: m_cts.Token));

            if (result.Success && result.FilePath is not null)
            {
                // Analyze the generated track
                MusicAnalysis analysis = await Task.Run(() => MusicAnalyzer.Analyze(result.FilePath));

                MusicTrack track = new()
                {
                    Id = id,
                    Title = title,
                    Prompt = m_prompt,
                    FilePath = result.FilePath,
                    Duration = analysis.DurationSeconds,
                    Temperature = m_temperature,
                    CfgCoefficient = m_cfgCoefficient,
                    VibeTag = analysis.VibeDescription(),
                    Bpm = analysis.EstimatedBpm,
                };

                Dispatch(() =>
                {
                    Tracks.Insert(0, track);
                    SelectedTrack = track;
                    SaveCatalog();
                    StatusText = $"✓ Generated: {title} ({analysis.DurationSeconds:F1}s, ~{analysis.EstimatedBpm:F0} BPM)";
                    m_log.Log($"♫ Done: {title} — {analysis.VibeDescription()}");
                });
            }
            else
            {
                StatusText = $"Generation failed: {result.Error}";
                m_log.Log($"♫ Error: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Generation cancelled.";
            m_log.Log("♫ Generation cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            m_log.Log($"♫ Error: {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    void RegenerateVariant()
    {
        if (m_selectedTrack is null) return;
        // Use the selected track's prompt but nudge temperature for variety
        Prompt = m_selectedTrack.Prompt;
        Temperature = Math.Clamp(m_selectedTrack.Temperature + (Random.Shared.NextDouble() * 0.4 - 0.2), 0.3, 1.8);
        Generate();
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
            m_log.Log($"♫ Playback error: {ex.Message}");
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

    void LoadAnalysis(MusicTrack track)
    {
        try
        {
            Analysis = MusicAnalyzer.Analyze(track.FilePath);
        }
        catch
        {
            Analysis = null;
        }
    }

    // ── Track actions ───────────────────────────────────────────

    void RateTrack(object? param)
    {
        if (m_selectedTrack is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;
        m_selectedTrack.Rating = Math.Clamp(rating, 1, 5);
        SaveCatalog();
        m_log.Log($"♫ Rated {m_selectedTrack.DisplayName}: {m_selectedTrack.RatingStars}");

        // Low-rated tracks get deleted
        if (rating <= 2)
        {
            m_log.Log($"♫ Low rating — removing track and generating variant…");
            m_selectedTrack.Deleted = true;
            Tracks.Remove(m_selectedTrack);
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
        SelectedTrack = Tracks.FirstOrDefault();
        SaveCatalog();
        m_log.Log($"♫ Deleted: {name}");
    }

    // ── Helpers ─────────────────────────────────────────────────

    static string ExtractTitle(string prompt)
    {
        // Take first few meaningful words as title
        string[] words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string title = string.Join(" ", words.Take(5));
        return title.Length > 40 ? title[..40] : title;
    }

    static string SanitizeFileName(string name)
        => string.Concat(name.ToLowerInvariant().Replace(' ', '_')
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));

    public void Dispose()
    {
        StopPlayback();
        m_client.Dispose();
    }
}
