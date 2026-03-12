using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// One card-stack slot in the gallery. Represents a single prompt with all its
/// generated images stacked behind the visible top card. The user can cycle
/// through the stack and rate inline.
/// </summary>
sealed class PromptStack : INotifyPropertyChanged
{
    readonly Action<PromptStack> m_onSelect;
    int m_topIndex;

    public int PromptNumber { get; }
    public string Title { get; }
    public string Series { get; }
    public List<ImageRecord> Cards { get; set; } = [];

    public PromptStack(int promptNumber, string title, string series,
        Action<PromptStack> onSelect)
    {
        PromptNumber = promptNumber;
        Title = title;
        Series = series;
        m_onSelect = onSelect;
        CycleForwardCommand = new RelayCommand(_ => CycleForward(), _ => Cards.Count > 1);
        CycleBackCommand = new RelayCommand(_ => CycleBack(), _ => Cards.Count > 1);
        SelectCommand = new RelayCommand(_ => m_onSelect(this));
    }

    public ImageRecord? TopCard => Cards.Count > 0 && m_topIndex < Cards.Count ? Cards[m_topIndex] : null;
    public string TopImage => TopCard?.FilePath ?? "";
    public string DisplayName => $"{PromptNumber:D2} — {Title}";
    public string StackLabel => Cards.Count > 1 ? $"{m_topIndex + 1}/{Cards.Count}" : Cards.Count == 1 ? "1" : "—";
    public string TopRating => TopCard?.RatingStars ?? "";
    public bool IsTopRated => TopCard?.IsRated ?? false;
    public string TopStatus => TopCard?.StatusIcon ?? "";
    public int CardCount => Cards.Count;
    public bool HasMultiple => Cards.Count > 1;

    public ICommand CycleForwardCommand { get; }
    public ICommand CycleBackCommand { get; }
    public ICommand SelectCommand { get; }

    void CycleForward()
    {
        if (Cards.Count < 2) return;
        m_topIndex = (m_topIndex + 1) % Cards.Count;
        NotifyAll();
    }

    void CycleBack()
    {
        if (Cards.Count < 2) return;
        m_topIndex = (m_topIndex - 1 + Cards.Count) % Cards.Count;
        NotifyAll();
    }

    public void SetTopIndex(int index)
    {
        m_topIndex = Math.Clamp(index, 0, Math.Max(0, Cards.Count - 1));
        NotifyAll();
    }

    public void RefreshCards()
    {
        m_topIndex = Math.Clamp(m_topIndex, 0, Math.Max(0, Cards.Count - 1));
        NotifyAll();
    }

    void NotifyAll()
    {
        OnPropertyChanged(nameof(TopCard));
        OnPropertyChanged(nameof(TopImage));
        OnPropertyChanged(nameof(StackLabel));
        OnPropertyChanged(nameof(TopRating));
        OnPropertyChanged(nameof(IsTopRated));
        OnPropertyChanged(nameof(TopStatus));
        OnPropertyChanged(nameof(CardCount));
        OnPropertyChanged(nameof(HasMultiple));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
