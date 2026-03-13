using Common.Wpf.ViewModels;
using System.Collections.ObjectModel;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// Owns status bar text, queue items display, generating flag, and preferences summary.
/// </summary>
sealed class StatusViewModel : ObservableObject
{
    string m_statusText = "Ready.";
    string m_preferencesSummary = "";
    bool m_isGenerating;

    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool IsGenerating { get => m_isGenerating; set => SetField(ref m_isGenerating, value); }
    public string PreferencesSummary { get => m_preferencesSummary; set => SetField(ref m_preferencesSummary, value); }
    public ObservableCollection<string> QueueItems { get; } = [];

    public void UpdateStatus(ImageCatalog? catalog, int queuedCount)
    {
        if (catalog is null) return;
        int total = catalog.All.Count(r => !r.Deleted);
        string queueStr = queuedCount > 0 ? $"  |  ⏳ {queuedCount} queued" : "";
        StatusText = $"{total} images  |  🆕 {catalog.UnseenCount} unseen  |  💾 {catalog.SavedCount} saved{queueStr}";
    }

    public void UpdatePreferencesSummary(PreferenceTracker? preferences)
    {
        if (preferences is null || preferences.TotalSignals == 0)
        {
            PreferencesSummary = "No preference data yet.";
            return;
        }
        (List<(string token, double weight)>? likes, List<(string token, double weight)>? dislikes) = preferences.GetTopPreferences();
        string likesStr = likes.Count > 0
            ? string.Join(", ", likes.Select(l => $"{l.token}({l.weight:+0.0})"))
            : "—";
        string dislikesStr = dislikes.Count > 0
            ? string.Join(", ", dislikes.Select(d => $"{d.token}({d.weight:+0.0;-0.0})"))
            : "—";
        PreferencesSummary = $"👍 {likesStr}\n👎 {dislikesStr}\nPreset: {preferences.SuggestedPreset()}  |  Signals: {preferences.TotalSignals}";
    }
}
