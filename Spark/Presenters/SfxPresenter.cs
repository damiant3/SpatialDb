using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Common.Wpf.Input;
using NAudio.Wave;
using Spark.Services;
using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark.Presenters;

sealed class SfxPresenter : IDisposable
{
    readonly MusicGenClient m_client;
    readonly LogViewModel m_log;
    readonly SfxViewModel m_vm;
    readonly DispatcherTimer m_playTimer;
    WaveOutEvent? m_player;
    AudioFileReader? m_audioReader;
    CancellationTokenSource? m_cts;
    string m_catalogPath = "";
    int m_nextId = 1;

    public SfxPresenter(MusicGenClient client, LogViewModel log, SfxViewModel vm)
    {
        m_client = client;
        m_log = log;
        m_vm = vm;

        vm.GenerateCommand = new RelayCommand(_ => GenerateSingle(), _ => vm.IsNotGenerating && vm.Prompt.Length > 0);
        vm.GenerateBatchCommand = new RelayCommand(_ => GenerateBatch(), _ => vm.IsNotGenerating && vm.Prompt.Length > 0);
        vm.CancelGenerateCommand = new RelayCommand(_ => m_cts?.Cancel(), _ => vm.IsGenerating);
        vm.PlayPauseCommand = new RelayCommand(_ => TogglePlayPause(), _ => vm.SelectedTrack?.Exists == true);
        vm.StopCommand = new RelayCommand(_ => StopPlayback(), _ => vm.IsPlaying);
        vm.DeleteTrackCommand = new RelayCommand(_ => DeleteTrack(), _ => vm.SelectedTrack is not null);
        vm.RateTrackCommand = new RelayCommand(p => RateTrack(p));
        vm.RegenerateVariantCommand = new RelayCommand(_ => RegenerateVariant(), _ => vm.SelectedTrack is not null && vm.IsNotGenerating);
        vm.ApplyPresetCommand = new RelayCommand(p => ApplyPreset(p as SfxPreset));
        vm.CategoryFilterCommand = new RelayCommand(p => { if (p is string s) vm.SelectedCategory = s; });

        vm.SelectedTrackChanged += OnSelectedTrackChanged;

        m_playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        m_playTimer.Tick += OnPlayTimerTick;

        _ = CheckMusicGen();
    }

    public void LoadProject(string projectDir)
    {
        StopPlayback();
        m_vm.Tracks.Clear();
        m_vm.FilteredTracks.Clear();

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
                    foreach (SfxTrack? t in tracks.Where(t => !t.Deleted))
                    {
                        m_vm.Tracks.Add(t);
                        if (t.Id >= m_nextId) m_nextId = t.Id + 1;
                    }
            }
            catch (Exception ex)
            {
                m_log.Log($"🔊 Catalog load error: {ex.Message}");
            }
        }

        RebuildFilteredTracks();
        m_log.Log($"🔊 SFX: loaded {m_vm.Tracks.Count} clips from {sfxDir}");
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
            m_log.Log($"🔊 Catalog save error: {ex.Message}");
        }
    }

    void RebuildFilteredTracks()
    {
        m_vm.FilteredTracks.Clear();
        foreach (SfxTrack t in m_vm.Tracks)
            if (m_vm.SelectedCategory == "All" || t.Category == m_vm.SelectedCategory)
                m_vm.FilteredTracks.Add(t);
    }

    async Task CheckMusicGen()
    {
        m_vm.MusicGenAvailable = await m_client.IsAvailableAsync();
        m_vm.StatusText = m_vm.MusicGenAvailable
            ? "✓ MusicGen online — ready for SFX generation"
            : "MusicGen not found — start: python -m audiocraft.demos.musicgen_app --server_port 7860";
    }

    void ApplyPreset(SfxPreset? preset)
    {
        if (preset is null) return;
        m_vm.Prompt = preset.Prompt;
        m_vm.SelectedCategory = preset.Category;

        SfxConfig.CategoryDefault? defaults = SfxConfig.DefaultsFor(preset.Category);
        if (defaults is not null)
        {
            m_vm.Duration = defaults.Duration;
            m_vm.Temperature = defaults.Temperature;
            m_vm.CfgCoefficient = defaults.Cfg;
        }
    }

    async void GenerateSingle()
    {
        if (m_vm.Prompt.Length == 0 || m_catalogPath.Length == 0) return;
        await GenerateOne(m_vm.Prompt, m_vm.Temperature, 0);
    }

    async void GenerateBatch()
    {
        if (m_vm.Prompt.Length == 0 || m_catalogPath.Length == 0) return;

        m_vm.IsGenerating = true;
        m_cts = new CancellationTokenSource();

        try
        {
            for (int i = 0; i < m_vm.BatchCount; i++)
            {
                if (m_cts.IsCancellationRequested) break;
                double tempVariance = m_vm.Temperature + (i * 0.15) - (m_vm.BatchCount * 0.075);
                double t = Math.Clamp(tempVariance, 0.2, 1.9);
                m_vm.StatusText = $"Generating variant {i + 1} of {m_vm.BatchCount}…";
                await GenerateOne(m_vm.Prompt, t, 0, skipBusyFlag: true);
            }
            m_vm.StatusText = $"✓ Batch complete — {m_vm.BatchCount} variants generated";
        }
        catch (OperationCanceledException)
        {
            m_vm.StatusText = "Batch cancelled.";
        }
        finally
        {
            m_vm.IsGenerating = false;
        }
    }

    async Task GenerateOne(string prompt, double temperature, int parentId, bool skipBusyFlag = false)
    {
        if (!skipBusyFlag) m_vm.IsGenerating = true;
        m_cts ??= new CancellationTokenSource();
        string sfxDir = Path.GetDirectoryName(m_catalogPath) ?? "";

        int id = m_nextId++;
        string title = ExtractTitle(prompt);
        string category = DetectCategory(prompt);
        string fileName = $"sfx_{id:D3}_{SanitizeFileName(title)}.wav";

        MusicGenSettings settings = new()
        {
            Prompt = prompt,
            Duration = m_vm.Duration,
            Temperature = temperature,
            CfgCoefficient = m_vm.CfgCoefficient,
        };

        try
        {
            m_log.Log($"🔊 Generating: {prompt[..Math.Min(60, prompt.Length)]}… ({m_vm.Duration}s)");

            MusicGenResult result = await Task.Run(() =>
                m_client.GenerateAsync(settings, sfxDir, fileName,
                    onStatus: msg => Dispatch(() => m_vm.StatusText = msg),
                    ct: m_cts.Token));

            if (result.Success && result.FilePath is not null)
            {
                MusicAnalysis analysis = await Task.Run(() => MusicAnalyzer.Analyze(result.FilePath));

                SfxTrack track = new()
                {
                    Id = id,
                    Title = title,
                    Prompt = prompt,
                    Category = category.Length > 0 ? category : m_vm.SelectedCategory != "All" ? m_vm.SelectedCategory : "",
                    FilePath = result.FilePath,
                    Duration = analysis.DurationSeconds,
                    Temperature = temperature,
                    CfgCoefficient = m_vm.CfgCoefficient,
                    VibeTag = DescribeSfx(analysis),
                    ParentId = parentId,
                };

                Dispatch(() =>
                {
                    m_vm.Tracks.Insert(0, track);
                    if (m_vm.SelectedCategory == "All" || track.Category == m_vm.SelectedCategory)
                        m_vm.FilteredTracks.Insert(0, track);
                    m_vm.SelectedTrack = track;
                    SaveCatalog();
                    m_log.Log($"🔊 Done: {title} ({analysis.DurationSeconds:F1}s) — {track.VibeTag}");
                });
            }
            else
            {
                m_vm.StatusText = $"Generation failed: {result.Error}";
                m_log.Log($"🔊 Error: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            m_vm.StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            m_vm.StatusText = $"Error: {ex.Message}";
            m_log.Log($"🔊 Error: {ex.Message}");
        }
        finally
        {
            if (!skipBusyFlag) m_vm.IsGenerating = false;
        }
    }

    void RegenerateVariant()
    {
        if (m_vm.SelectedTrack is null) return;
        m_vm.Prompt = m_vm.SelectedTrack.Prompt;
        m_vm.Temperature = Math.Clamp(m_vm.SelectedTrack.Temperature + (Random.Shared.NextDouble() * 0.4 - 0.2), 0.3, 1.8);
        GenerateSingle();
    }

    void OnSelectedTrackChanged(SfxTrack? track)
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
            m_log.Log($"🔊 Playback error: {ex.Message}");
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

    void LoadAnalysis(SfxTrack track)
    {
        try
        {
            m_vm.Analysis = MusicAnalyzer.Analyze(track.FilePath);
        }
        catch (Exception ex)
        {
            m_log.Log($"🔊 Analysis error: {ex.Message}");
            m_vm.Analysis = null;
        }
    }

    void RateTrack(object? param)
    {
        if (m_vm.SelectedTrack is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;
        m_vm.SelectedTrack.Rating = Math.Clamp(rating, 1, 5);
        SaveCatalog();
        m_log.Log($"🔊 Rated {m_vm.SelectedTrack.DisplayName}: {m_vm.SelectedTrack.RatingStars}");

        if (rating <= 2)
        {
            m_log.Log("🔊 Low rating — regenerating variant…");
            m_vm.SelectedTrack.Deleted = true;
            m_vm.Tracks.Remove(m_vm.SelectedTrack);
            m_vm.FilteredTracks.Remove(m_vm.SelectedTrack);
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
        m_vm.FilteredTracks.Remove(m_vm.SelectedTrack);
        m_vm.SelectedTrack = m_vm.FilteredTracks.FirstOrDefault();
        SaveCatalog();
        m_log.Log($"🔊 Deleted: {name}");
    }

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

    static string DetectCategory(string prompt)
    {
        string p = prompt.ToLowerInvariant();
        if (p.Contains("ui") || p.Contains("button") || p.Contains("click") || p.Contains("menu") ||
            p.Contains("notification") || p.Contains("interface") || p.Contains("coin") ||
            p.Contains("level up") || p.Contains("inventory") || p.Contains("quest") ||
            p.Contains("purchase") || p.Contains("checkbox") || p.Contains("hover") || p.Contains("tab switch")) return "UI";
        if (p.Contains("gun") || p.Contains("pistol") || p.Contains("rifle") || p.Contains("shotgun") ||
            p.Contains("sniper") || p.Contains("bullet") || p.Contains("reload") || p.Contains("ammo") ||
            p.Contains("laser blaster") || p.Contains("plasma cannon") || p.Contains("machine gun")) return "Firearms";
        if (p.Contains("sword") || p.Contains("arrow") || p.Contains("explosion") || p.Contains("shield") ||
            p.Contains("combat") || p.Contains("weapon") || p.Contains("battle") || p.Contains("axe") ||
            p.Contains("mace") || p.Contains("whip") || p.Contains("knife") || p.Contains("parry") ||
            p.Contains("armor")) return "Combat";
        if (p.Contains("wind") || p.Contains("rain") || p.Contains("thunder") || p.Contains("fire") ||
            p.Contains("water") || p.Contains("tree") || p.Contains("nature") || p.Contains("bird") ||
            p.Contains("owl") || p.Contains("crow") || p.Contains("earthquake") || p.Contains("avalanche") ||
            p.Contains("volcano") || p.Contains("waterfall") || p.Contains("river") || p.Contains("campfire")) return "Nature";
        if (p.Contains("footstep") || p.Contains("door") || p.Contains("jump") || p.Contains("walk") ||
            p.Contains("run") || p.Contains("climb") || p.Contains("ladder") || p.Contains("rope") ||
            p.Contains("swimming") || p.Contains("sliding") || p.Contains("rolling")) return "Movement";
        if (p.Contains("magic") || p.Contains("spell") || p.Contains("heal") || p.Contains("teleport") ||
            p.Contains("arcane") || p.Contains("power up") || p.Contains("enchant") || p.Contains("summon") ||
            p.Contains("dispel") || p.Contains("mana") || p.Contains("resurrect") || p.Contains("curse") ||
            p.Contains("potion")) return "Magic";
        if (p.Contains("gear") || p.Contains("machine") || p.Contains("steam") || p.Contains("lever") ||
            p.Contains("mechanical") || p.Contains("clockwork") || p.Contains("chain") || p.Contains("drawbridge") ||
            p.Contains("trap") || p.Contains("portcullis") || p.Contains("lock") || p.Contains("key turn") ||
            p.Contains("elevator") || p.Contains("conveyor") || p.Contains("pressure plate")) return "Mechanical";
        if (p.Contains("ocean") || p.Contains("cave") || p.Contains("ambient") || p.Contains("cricket") ||
            p.Contains("atmosphere") || p.Contains("swamp") || p.Contains("desert") || p.Contains("tavern") ||
            p.Contains("dungeon ambient") || p.Contains("space station") || p.Contains("library") ||
            p.Contains("marketplace") || p.Contains("church interior") || p.Contains("battlefield")) return "Ambient";
        if (p.Contains("monster") || p.Contains("wolf") || p.Contains("dragon") || p.Contains("insect") ||
            p.Contains("creature") || p.Contains("beast") || p.Contains("growl") || p.Contains("roar") ||
            p.Contains("snake") || p.Contains("bear") || p.Contains("horse") || p.Contains("zombie") ||
            p.Contains("ghoul") || p.Contains("vampire") || p.Contains("giant") || p.Contains("bat") ||
            p.Contains("spider") || p.Contains("goblin") || p.Contains("orc") || p.Contains("skeleton") ||
            p.Contains("werewolf")) return "Creature";
        if (p.Contains("punch") || p.Contains("glass") || p.Contains("break") || p.Contains("impact") ||
            p.Contains("crash") || p.Contains("smash") || p.Contains("thud") || p.Contains("hit") ||
            p.Contains("slap") || p.Contains("headbutt") || p.Contains("wall smash") || p.Contains("table flip")) return "Impact";
        if (p.Contains("voice") || p.Contains("grunt") || p.Contains("cry") || p.Contains("shout") ||
            p.Contains("scream") || p.Contains("gasp") || p.Contains("laugh") || p.Contains("whisper") ||
            p.Contains("cough") || p.Contains("sigh") || p.Contains("snoring") || p.Contains("eating") ||
            p.Contains("crowd cheer") || p.Contains("crowd boo")) return "Voice";
        if (p.Contains("horror") || p.Contains("creepy") || p.Contains("ghost") || p.Contains("demon") ||
            p.Contains("jumpscare") || p.Contains("haunted") || p.Contains("coffin") || p.Contains("eerie children") ||
            p.Contains("midnight") || p.Contains("squelch") || p.Contains("heartbeat") || p.Contains("scratching")) return "Horror";
        if (p.Contains("sci-fi") || p.Contains("laser") || p.Contains("hologram") || p.Contains("warp") ||
            p.Contains("robot") || p.Contains("emp") || p.Contains("cryopod") || p.Contains("lightsaber") ||
            p.Contains("alien") || p.Contains("scanner") || p.Contains("forcefield") || p.Contains("hydraulic")) return "Sci-Fi";
        if (p.Contains("engine") || p.Contains("car") || p.Contains("helicopter") || p.Contains("spaceship") ||
            p.Contains("train") || p.Contains("bicycle") || p.Contains("motorcycle") || p.Contains("boat") ||
            p.Contains("jet") || p.Contains("tank") || p.Contains("tires") || p.Contains("vehicle") ||
            p.Contains("horse cart")) return "Vehicle";
        if (p.Contains("door knock") || p.Contains("phone") || p.Contains("alarm clock") || p.Contains("doorbell") ||
            p.Contains("toilet") || p.Contains("faucet") || p.Contains("microwave") || p.Contains("key jingle") ||
            p.Contains("light switch") || p.Contains("typing") || p.Contains("paper") || p.Contains("zipper")) return "Household";
        if (p.Contains("drum roll") || p.Contains("cymbal") || p.Contains("gong") || p.Contains("horn blast") ||
            p.Contains("bell toll") || p.Contains("harp") || p.Contains("string pluck") || p.Contains("piano chord") ||
            p.Contains("record scratch") || p.Contains("tuning fork")) return "Musical";
        if (p.Contains("hail") || p.Contains("blizzard") || p.Contains("tornado") || p.Contains("sandstorm") ||
            p.Contains("lightning strike")) return "Weather";
        if (p.Contains("dripping") || p.Contains("pouring") || p.Contains("bubbling") || p.Contains("underwater") ||
            p.Contains("lava") || p.Contains("acid") || p.Contains("liquid")) return "Liquid";
        if (p.Contains("collapse") || p.Contains("demolition") || p.Contains("wrecking") || p.Contains("timber") ||
            p.Contains("bridge break") || p.Contains("car crash") || p.Contains("ground crack")) return "Destruction";
        if (p.Contains("radio") || p.Contains("walkie") || p.Contains("morse") || p.Contains("sonar") ||
            p.Contains("distress signal")) return "Communication";
        if (p.Contains("kick") || p.Contains("bounce") || p.Contains("bat hit") || p.Contains("whistle") ||
            p.Contains("boxing") || p.Contains("diving")) return "Sports";
        if (p.Contains("sizzling") || p.Contains("cork") || p.Contains("can open") || p.Contains("chopping") ||
            p.Contains("ice cubes") || p.Contains("cooking")) return "Food";
        if (p.Contains("cloth") || p.Contains("leather") || p.Contains("velcro") || p.Contains("flag") ||
            p.Contains("cape") || p.Contains("fabric")) return "Textile";
        if (p.Contains("siren") || p.Contains("alarm") || p.Contains("klaxon") || p.Contains("alert")) return "Alarm";
        return "";
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

    public void Dispose()
    {
        StopPlayback();
        m_client.Dispose();
    }
}
