using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// Owns detail-panel state: the selected image, rating, actions (save/delete/regen),
/// prompt augment, and variant commands.
/// </summary>
sealed class DetailViewModel : ViewModelBase
{
    readonly LogViewModel m_log;
    ImageRecord? m_detailImage;
    string m_detailPromptText = "";
    string m_detailInfoLine = "";
    string m_promptAugment = "";

    public DetailViewModel(LogViewModel log)
    {
        m_log = log;
    }

    public ImageRecord? DetailImage
    {
        get => m_detailImage;
        set
        {
            if (!SetField(ref m_detailImage, value)) return;
            DetailPromptText = value?.PromptText ?? "";
            UpdateDetailInfoLine();
            if (value is not null) ImageSeen?.Invoke(value);
            OnPropertyChanged(nameof(DetailRating));
        }
    }

    public string DetailPromptText { get => m_detailPromptText; set => SetField(ref m_detailPromptText, value); }
    public string DetailInfoLine { get => m_detailInfoLine; set => SetField(ref m_detailInfoLine, value); }
    public string PromptAugment { get => m_promptAugment; set => SetField(ref m_promptAugment, value); }
    public int DetailRating => m_detailImage?.Rating ?? 0;

    public ArtDirections.DirectionGroup[] DirectionGroups { get; } = ArtDirections.Groups;

    // ── Commands wired by MainViewModel after construction ──────

    public ICommand SaveDetailCommand { get; set; } = RelayCommand.Empty;
    public ICommand DeleteDetailCommand { get; set; } = RelayCommand.Empty;
    public ICommand RegenDetailCommand { get; set; } = RelayCommand.Empty;
    public ICommand RateDetailCommand { get; set; } = RelayCommand.Empty;
    public ICommand VariantBiggerCommand { get; set; } = RelayCommand.Empty;
    public ICommand VariantSmallerCommand { get; set; } = RelayCommand.Empty;
    public ICommand CreativeRegenCommand { get; set; } = RelayCommand.Empty;
    public ICommand DirectedRegenCommand { get; set; } = RelayCommand.Empty;
    public ICommand ShowLightboxCommand { get; set; } = RelayCommand.Empty;

    /// <summary>Raised so the catalog can mark the image as seen.</summary>
    public event Action<ImageRecord>? ImageSeen;

    /// <summary>
    /// Injects trigger words into the prompt augment, avoiding duplicates.
    /// </summary>
    public void InjectTriggerWords(string triggerWords)
    {
        if (triggerWords.Length == 0) return;
        if (m_promptAugment.Contains(triggerWords, StringComparison.OrdinalIgnoreCase)) return;
        string existing = m_promptAugment.Trim();
        PromptAugment = existing.Length > 0
            ? triggerWords + ", " + existing
            : triggerWords;
    }

    public void UpdateDetailInfoLine()
    {
        if (m_detailImage is null) { DetailInfoLine = ""; OnPropertyChanged(nameof(DetailRating)); return; }
        ImageRecord r = m_detailImage;
        string seed = r.Seed > 0 ? r.Seed.ToString() : "random";
        string size = r.SourceWidth > 0 ? $"{r.SourceWidth}×{r.SourceHeight}" : "";
        string preset = r.RefinePreset != "none" ? r.RefinePreset : "";
        string lora = r.LoraTag.Length > 0 ? r.LoraTag : "";
        string parts = string.Join("  •  ",
            new[] { $"Seed: {seed}", preset, size, lora }
            .Where(s => s.Length > 0));
        DetailInfoLine = parts;
        OnPropertyChanged(nameof(DetailRating));
    }

    public void NotifyRatingChanged() => OnPropertyChanged(nameof(DetailRating));
}
