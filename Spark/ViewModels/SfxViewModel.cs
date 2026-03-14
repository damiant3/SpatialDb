using System.Collections.ObjectModel;
using System.Windows.Input;
using Common.Wpf.ViewModels;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.ViewModels;

sealed class SfxViewModel : ObservableObject
{
    public ObservableCollection<SfxTrack> Tracks { get; } = [];
    public ObservableCollection<SfxTrack> FilteredTracks { get; } = [];

    string m_prompt = "";
    double m_duration = 1.0;
    double m_temperature = 1.0;
    double m_cfgCoefficient = 3.5;
    int m_batchCount = 3;
    bool m_isGenerating;
    string m_selectedCategory = "All";

    public string Prompt { get => m_prompt; set => SetField(ref m_prompt, value); }
    public double Duration { get => m_duration; set => SetField(ref m_duration, Math.Clamp(value, SfxConfig.MinDuration, SfxConfig.MaxDuration)); }
    public double Temperature { get => m_temperature; set => SetField(ref m_temperature, Math.Clamp(value, 0.1, 2.0)); }
    public double CfgCoefficient { get => m_cfgCoefficient; set => SetField(ref m_cfgCoefficient, Math.Clamp(value, 1, 10)); }
    public int BatchCount { get => m_batchCount; set => SetField(ref m_batchCount, Math.Clamp(value, 1, 8)); }
    public bool IsGenerating { get => m_isGenerating; set { SetField(ref m_isGenerating, value); OnPropertyChanged(nameof(IsNotGenerating)); } }
    public bool IsNotGenerating => !m_isGenerating;

    public string SelectedCategory
    {
        get => m_selectedCategory;
        set => SetField(ref m_selectedCategory, value);
    }

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
            SelectedTrackChanged?.Invoke(value);
        }
    }

    public event Action<SfxTrack?>? SelectedTrackChanged;

    public MusicAnalysis? Analysis { get => m_analysis; set => SetField(ref m_analysis, value); }
    public double PlaybackPosition { get => m_playbackPosition; set => SetField(ref m_playbackPosition, value); }
    public bool IsPlaying { get => m_isPlaying; set => SetField(ref m_isPlaying, value); }
    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool MusicGenAvailable { get => m_musicGenAvailable; set => SetField(ref m_musicGenAvailable, value); }

    public string[] Categories => SfxConfig.Categories;

    public SfxPreset[] SfxPresets => [.. SfxConfig.Presets.Select(p => new SfxPreset(p.Label, p.Category, p.Prompt))];

    public ICommand GenerateCommand { get; set; } = null!;
    public ICommand GenerateBatchCommand { get; set; } = null!;
    public ICommand CancelGenerateCommand { get; set; } = null!;
    public ICommand PlayPauseCommand { get; set; } = null!;
    public ICommand StopCommand { get; set; } = null!;
    public ICommand DeleteTrackCommand { get; set; } = null!;
    public ICommand RateTrackCommand { get; set; } = null!;
    public ICommand RegenerateVariantCommand { get; set; } = null!;
    public ICommand ApplyPresetCommand { get; set; } = null!;
    public ICommand CategoryFilterCommand { get; set; } = null!;
}

sealed record SfxPreset(string Label, string Category, string Prompt);
