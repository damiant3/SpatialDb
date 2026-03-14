using System.Collections.ObjectModel;
using System.Windows.Input;
using Common.Wpf.ViewModels;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.ViewModels;

sealed class MusicViewModel : ObservableObject
{
    public ObservableCollection<MusicTrack> Tracks { get; } = [];

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
            SelectedTrackChanged?.Invoke(value);
        }
    }

    public event Action<MusicTrack?>? SelectedTrackChanged;

    public MusicAnalysis? Analysis { get => m_analysis; set => SetField(ref m_analysis, value); }
    public double PlaybackPosition { get => m_playbackPosition; set => SetField(ref m_playbackPosition, value); }
    public bool IsPlaying { get => m_isPlaying; set => SetField(ref m_isPlaying, value); }
    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool MusicGenAvailable { get => m_musicGenAvailable; set => SetField(ref m_musicGenAvailable, value); }

    public string[] MoodPresets => MusicConfig.MoodPresets;
    public string[] InstrumentFamilies => MusicConfig.InstrumentFamilies;
    public string[] Composers => MusicConfig.Composers;
    public string[] Artists => MusicConfig.Artists;
    public string[] Genres => MusicConfig.Genres;
    public string[] TempoLabels => MusicConfig.TempoLabels;
    public string[] Keys => MusicConfig.Keys;
    public string[] Scales => MusicConfig.Scales;

    string m_selectedFamily = "";
    public string SelectedFamily
    {
        get => m_selectedFamily;
        set
        {
            if (!SetField(ref m_selectedFamily, value)) return;
            OnPropertyChanged(nameof(FamilyInstruments));
        }
    }

    public string[] FamilyInstruments
        => m_selectedFamily.Length > 0 ? MusicConfig.InstrumentsIn(m_selectedFamily) : [];

    public ICommand GenerateCommand { get; set; } = null!;
    public ICommand CancelGenerateCommand { get; set; } = null!;
    public ICommand PlayPauseCommand { get; set; } = null!;
    public ICommand StopCommand { get; set; } = null!;
    public ICommand DeleteTrackCommand { get; set; } = null!;
    public ICommand RateTrackCommand { get; set; } = null!;
    public ICommand RegenerateVariantCommand { get; set; } = null!;
    public ICommand ApplyMoodPresetCommand { get; set; } = null!;
    public ICommand InjectTagCommand { get; set; } = null!;
    public ICommand SelectFamilyCommand { get; set; } = null!;
}
