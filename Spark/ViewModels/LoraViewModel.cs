using Common.Wpf.Input;
using Common.Wpf.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// Owns all LoRA-related state: selection, weight, trigger words,
/// download URL, browser integration.
/// </summary>
sealed class LoraViewModel : ObservableObject
{
    readonly LoraService m_loraService;
    readonly LogViewModel m_log;
    string m_loraUrl = "";

    public LoraViewModel(LoraService loraService, LogViewModel log)
    {
        m_loraService = loraService;
        m_log = log;

        RefreshLorasCommand = new RelayCommand(_ => m_loraService.LoadLoras(m_log.Log));
        BrowseLoraSiteCommand = new RelayCommand(_ => BrowseLoras());
        DownloadLoraCommand = new RelayCommand(_ => m_loraService.DownloadLora(m_loraUrl, m_log.Log), _ => m_loraUrl.Length > 0);
        InjectTriggerWordsCommand = new RelayCommand(_ => InjectTriggerWords(), _ => HasLoraTriggerWords);
    }

    public ObservableCollection<string> AvailableLoras => m_loraService.AvailableLoras;

    public string SelectedLora
    {
        get => m_loraService.SelectedLora;
        set
        {
            m_loraService.SelectedLora = value;
            m_loraService.RefreshTriggerWords();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoraTriggerWords));
            OnPropertyChanged(nameof(HasLoraTriggerWords));
        }
    }

    public double LoraWeight
    {
        get => m_loraService.LoraWeight;
        set { m_loraService.LoraWeight = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
    }

    public string LoraUrl { get => m_loraUrl; set => SetField(ref m_loraUrl, value); }

    public string LoraTriggerWords
    {
        get => m_loraService.CurrentTriggerWords;
        set { m_loraService.CurrentTriggerWords = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLoraTriggerWords)); }
    }
    public bool HasLoraTriggerWords => LoraTriggerWords.Length > 0;

    public ICommand RefreshLorasCommand { get; }
    public ICommand BrowseLoraSiteCommand { get; }
    public ICommand DownloadLoraCommand { get; }
    public ICommand InjectTriggerWordsCommand { get; }

    /// <summary>Raised when trigger words should be injected into the prompt augment.</summary>
    public event Action<string>? TriggerWordsInjected;

    public string? BuildLoraTag() => m_loraService.BuildLoraTag();

    public void LoadLoras() => m_loraService.LoadLoras(m_log.Log);

    void BrowseLoras()
    {
        (string? loraName, string? triggerWords) = m_loraService.BrowseAndInstall(m_log.Log);
        if (loraName is null) return;

        if (AvailableLoras.Contains(loraName))
        {
            SelectedLora = loraName;
            OnPropertyChanged(nameof(SelectedLora));
        }
        if (triggerWords is { Length: > 0 })
            InjectTriggerWords();
    }

    void InjectTriggerWords()
    {
        string tw = LoraTriggerWords;
        if (tw.Length == 0) return;
        TriggerWordsInjected?.Invoke(tw);
        m_log.Log($"LoRA trigger words injected: {tw}");
    }
}
