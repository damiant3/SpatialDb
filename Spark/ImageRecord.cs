using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace Spark;

sealed class ImageRecord : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int PromptNumber { get; set; }
    public string Title { get; set; } = "";
    public string Series { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SettingsTag { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string Style { get; set; } = "";
    public long Seed { get; set; } = -1;
    public string RefinePreset { get; set; } = "none";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string PromptAugment { get; set; } = "";
    public string LoraTag { get; set; } = "";
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }

    bool m_seen;
    bool m_saved;
    int m_rating;

    public bool Seen { get => m_seen; set { if (m_seen == value) return; m_seen = value; Notify(); Notify(nameof(StatusIcon)); } }
    public bool Saved { get => m_saved; set { if (m_saved == value) return; m_saved = value; Notify(); Notify(nameof(StatusIcon)); } }
    public int Rating
    {
        get => m_rating;
        set
        {
            if (m_rating == value) return;
            m_rating = value;
            Notify();
            Notify(nameof(RatingStars));
            Notify(nameof(IsRated));
        }
    }

    // Soft delete: records when it was deleted. Null = not deleted.
    // After 24 hours, cleanup on next boot permanently removes the record + file.
    // User can manually restore by editing catalog.json before that window closes.
    public DateTime? DeletedUtc { get; set; }

    [JsonIgnore] public bool Deleted => DeletedUtc.HasValue;

    // Legacy compat: the old "Deleted" bool is mapped to DeletedUtc presence.
    // If old catalog.json has "deleted": true, the JsonStore won't set DeletedUtc,
    // but we handle migration in ImageCatalog.
    bool m_legacyDeleted;
    public bool LegacyDeleted { get => m_legacyDeleted; set => m_legacyDeleted = value; }

    public string[] PositiveSignals { get; set; } = [];
    public string[] NegativeSignals { get; set; } = [];
    public string ModifiedPrompt { get; set; } = "";

    [JsonIgnore] public string DisplayName => $"{PromptNumber:D2} — {Title}";
    [JsonIgnore] public string SettingsDisplay => SettingsTag.Replace('_', ' ');
    [JsonIgnore] public string RatingStars => Rating > 0 ? new string('★', Rating) : "";
    [JsonIgnore] public bool IsRated => Rating > 0;
    [JsonIgnore] public string StatusIcon => Deleted ? "🗑" : Saved ? "💾" : Seen ? "👁" : "🆕";
    [JsonIgnore] public bool HasModifiedPrompt => ModifiedPrompt.Length > 0;

    public void SoftDelete() { DeletedUtc = DateTime.UtcNow; Notify(nameof(Deleted)); Notify(nameof(StatusIcon)); }

    // INotifyPropertyChanged — lets card badges and stars update without full gallery rebuild
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
