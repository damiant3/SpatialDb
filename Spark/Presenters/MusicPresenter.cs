using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Common.Wpf.Input;
using NAudio.Wave;
using Spark.Services;
using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark.Presenters;

sealed class MusicPresenter : IDisposable
{
    readonly MusicGenClient m_client;
    readonly LogViewModel m_log;
    readonly MusicViewModel m_vm;
    readonly DispatcherTimer m_playTimer;
    WaveOutEvent? m_player;
    AudioFileReader? m_audioReader;
    CancellationTokenSource? m_cts;
    string m_catalogPath = "";
    int m_nextId = 1;

    public MusicPresenter(MusicGenClient client, LogViewModel log, MusicViewModel vm)
    {
        m_client = client;
        m_log = log;
        m_vm = vm;

        vm.GenerateCommand = new RelayCommand(_ => Generate(), _ => vm.IsNotGenerating && vm.Prompt.Length > 0);
        vm.CancelGenerateCommand = new RelayCommand(_ => m_cts?.Cancel(), _ => vm.IsGenerating);
        vm.PlayPauseCommand = new RelayCommand(_ => TogglePlayPause(), _ => vm.SelectedTrack?.Exists == true);
        vm.StopCommand = new RelayCommand(_ => StopPlayback(), _ => vm.IsPlaying);
        vm.DeleteTrackCommand = new RelayCommand(_ => DeleteTrack(), _ => vm.SelectedTrack is not null);
        vm.RateTrackCommand = new RelayCommand(p => RateTrack(p));
        vm.RegenerateVariantCommand = new RelayCommand(_ => RegenerateVariant(), _ => vm.SelectedTrack is not null && vm.IsNotGenerating);
        vm.ApplyMoodPresetCommand = new RelayCommand(p => { if (p is string s) vm.Prompt = s; });
        vm.InjectTagCommand = new RelayCommand(p => InjectTag(p as string));
        vm.SelectFamilyCommand = new RelayCommand(p => { if (p is string s) vm.SelectedFamily = s; });

        vm.SelectedTrackChanged += OnSelectedTrackChanged;

        m_playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        m_playTimer.Tick += OnPlayTimerTick;

        _ = CheckMusicGen();
    }

    public void LoadProject(string projectDir)
    {
        StopPlayback();
        m_vm.Tracks.Clear();

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
                    foreach (MusicTrack? t in tracks.Where(t => !t.Deleted))
                    {
                        m_vm.Tracks.Add(t);
                        if (t.Id >= m_nextId) m_nextId = t.Id + 1;
                    }
            }
            catch (Exception ex)
            {
                m_log.Log($"♫ Catalog load error: {ex.Message}");
            }
        }

        m_log.Log($"♫ Music: loaded {m_vm.Tracks.Count} tracks from {musicDir}");
    }

    void SaveCatalog()
    {
        if (m_catalogPath.Length == 0) return;
        try
        {
            string json = JsonSerializer.Serialize(m_vm.Tracks.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(m_catalogPath, json);
        }
        catch (Exception ex)
        {
            m_log.Log($"♫ Catalog save error: {ex.Message}");
        }
    }

    async Task CheckMusicGen()
    {
        m_vm.MusicGenAvailable = await m_client.IsAvailableAsync();
        m_vm.StatusText = m_vm.MusicGenAvailable
            ? "✓ MusicGen online at localhost:7860"
            : "MusicGen not found — start: python demos/musicgen_app.py --server_port 7860";
    }

    async void Generate()
    {
        if (m_vm.Prompt.Length == 0 || m_catalogPath.Length == 0) return;

        m_vm.IsGenerating = true;
        m_cts = new CancellationTokenSource();
        string musicDir = Path.GetDirectoryName(m_catalogPath) ?? "";

        int id = m_nextId++;
        string title = ExtractTitle(m_vm.Prompt);
        string fileName = $"track_{id:D3}_{SanitizeFileName(title)}.wav";

        MusicGenSettings settings = new()
        {
            Prompt = m_vm.Prompt,
            Duration = m_vm.Duration,
            Temperature = m_vm.Temperature,
            CfgCoefficient = m_vm.CfgCoefficient,
        };

        try
        {
            m_vm.StatusText = $"Generating {title} ({m_vm.Duration}s)…";
            m_log.Log($"♫ Generating: {m_vm.Prompt[..Math.Min(80, m_vm.Prompt.Length)]}…");

            MusicGenResult result = await Task.Run(() =>
                m_client.GenerateAsync(settings, musicDir, fileName,
                    onStatus: msg => Dispatch(() => m_vm.StatusText = msg),
                    ct: m_cts.Token));

            if (result.Success && result.FilePath is not null)
            {
                MusicAnalysis analysis = await Task.Run(() => MusicAnalyzer.Analyze(result.FilePath));

                MusicTrack track = new()
                {
                    Id = id,
                    Title = title,
                    Prompt = m_vm.Prompt,
                    FilePath = result.FilePath,
                    Duration = analysis.DurationSeconds,
                    Temperature = m_vm.Temperature,
                    CfgCoefficient = m_vm.CfgCoefficient,
                    VibeTag = analysis.VibeDescription(),
                    Bpm = analysis.EstimatedBpm,
                };

                Dispatch(() =>
                {
                    m_vm.Tracks.Insert(0, track);
                    m_vm.SelectedTrack = track;
                    SaveCatalog();
                    m_vm.StatusText = $"✓ Generated: {title} ({analysis.DurationSeconds:F1}s, ~{analysis.EstimatedBpm:F0} BPM)";
                    m_log.Log($"♫ Done: {title} — {analysis.VibeDescription()}");
                });
            }
            else
            {
                m_vm.StatusText = $"Generation failed: {result.Error}";
                m_log.Log($"♫ Error: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            m_vm.StatusText = "Generation cancelled.";
            m_log.Log("♫ Generation cancelled.");
        }
        catch (Exception ex)
        {
            m_vm.StatusText = $"Error: {ex.Message}";
            m_log.Log($"♫ Error: {ex.Message}");
        }
        finally
        {
            m_vm.IsGenerating = false;
        }
    }

    void RegenerateVariant()
    {
        if (m_vm.SelectedTrack is null) return;
        m_vm.Prompt = m_vm.SelectedTrack.Prompt;
        m_vm.Temperature = Math.Clamp(m_vm.SelectedTrack.Temperature + (Random.Shared.NextDouble() * 0.4 - 0.2), 0.3, 1.8);
        Generate();
    }

    void OnSelectedTrackChanged(MusicTrack? track)
    {
        StopPlayback();
        if (track is not null && track.Exists)
            LoadAnalysis(track);
        else
            m_vm.Analysis = null;
    }

    void TogglePlayPause()
    {
        if (m_vm.IsPlaying)
        {
            m_player?.Pause();
            m_vm.IsPlaying = false;
            m_playTimer.Stop();
        }
        else
        {
            if (m_player is null && m_vm.SelectedTrack?.Exists == true)
                StartPlayback();
            else
                m_player?.Play();
            m_vm.IsPlaying = true;
            m_playTimer.Start();
        }
    }

    void StartPlayback()
    {
        if (m_vm.SelectedTrack is null || !m_vm.SelectedTrack.Exists) return;
        StopPlayback();

        try
        {
            m_audioReader = new AudioFileReader(m_vm.SelectedTrack.FilePath);
            m_player = new WaveOutEvent();
            m_player.Init(m_audioReader);
            m_player.PlaybackStopped += (_, _) => Dispatch(() =>
            {
                m_vm.IsPlaying = false;
                m_playTimer.Stop();
                m_vm.PlaybackPosition = 0;
            });
            m_player.Play();
            m_vm.IsPlaying = true;
            m_playTimer.Start();
        }
        catch (Exception ex)
        {
            m_log.Log($"♫ Playback error: {ex.Message}");
            m_vm.StatusText = $"Playback error: {ex.Message}";
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
        m_vm.IsPlaying = false;
        m_vm.PlaybackPosition = 0;
    }

    void OnPlayTimerTick(object? sender, EventArgs e)
    {
        if (m_audioReader is null) return;
        double total = m_audioReader.TotalTime.TotalSeconds;
        if (total > 0)
            m_vm.PlaybackPosition = m_audioReader.CurrentTime.TotalSeconds / total;
    }

    void LoadAnalysis(MusicTrack track)
    {
        try
        {
            m_vm.Analysis = MusicAnalyzer.Analyze(track.FilePath);
        }
        catch (Exception ex)
        {
            m_log.Log($"♫ Analysis error: {ex.Message}");
            m_vm.Analysis = null;
        }
    }

    void RateTrack(object? param)
    {
        if (m_vm.SelectedTrack is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;
        m_vm.SelectedTrack.Rating = Math.Clamp(rating, 1, 5);
        SaveCatalog();
        m_log.Log($"♫ Rated {m_vm.SelectedTrack.DisplayName}: {m_vm.SelectedTrack.RatingStars}");

        if (rating <= 2)
        {
            m_log.Log("♫ Low rating — removing track and generating variant…");
            m_vm.SelectedTrack.Deleted = true;
            m_vm.Tracks.Remove(m_vm.SelectedTrack);
            SaveCatalog();
            RegenerateVariant();
        }
    }

    void DeleteTrack()
    {
        if (m_vm.SelectedTrack is null) return;
        StopPlayback();
        m_vm.SelectedTrack.Deleted = true;
        string name = m_vm.SelectedTrack.DisplayName;
        m_vm.Tracks.Remove(m_vm.SelectedTrack);
        m_vm.SelectedTrack = m_vm.Tracks.FirstOrDefault();
        SaveCatalog();
        m_log.Log($"♫ Deleted: {name}");
    }

    void InjectTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (m_vm.Prompt.Length > 0 && !m_vm.Prompt.EndsWith(", ") && !m_vm.Prompt.EndsWith(","))
            m_vm.Prompt = m_vm.Prompt.TrimEnd() + ", " + tag;
        else
            m_vm.Prompt = m_vm.Prompt + tag;
    }

    static string ExtractTitle(string prompt)
    {
        string[] words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string title = string.Join(" ", words.Take(5));
        return title.Length > 40 ? title[..40] : title;
    }

    static string SanitizeFileName(string name)
        => string.Concat(name.ToLowerInvariant().Replace(' ', '_')
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

    public void Dispose()
    {
        StopPlayback();
        m_client.Dispose();
    }
}
