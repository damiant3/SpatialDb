using System.Collections.ObjectModel;
using Common.Wpf.ViewModels;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// Owns the gallery grid of prompt stacks and selection state.
/// </summary>
sealed class GalleryViewModel : ObservableObject
{
    readonly DetailViewModel m_detail;
    PromptStack? m_selectedStack;

    public GalleryViewModel(DetailViewModel detail)
    {
        m_detail = detail;
    }

    public ObservableCollection<PromptStack> Stacks { get; } = [];

    public PromptStack? SelectedStack
    {
        get => m_selectedStack;
        set
        {
            if (!SetField(ref m_selectedStack, value)) return;
            m_detail.DetailImage = value?.TopCard;
        }
    }

    public void RebuildStacks(List<ArtPrompt> prompts, ImageCatalog? catalog)
    {
        if (catalog is null) return;

        int? selectedPrompt = m_selectedStack?.PromptNumber;
        Dictionary<int, int> topIndexes = [];
        foreach (PromptStack existing in Stacks)
            topIndexes[existing.PromptNumber] = existing.TopIndex;

        HashSet<int> promptNumbers = new(prompts.Select(p => p.Number));

        for (int i = Stacks.Count - 1; i >= 0; i--)
            if (!promptNumbers.Contains(Stacks[i].PromptNumber))
                Stacks.RemoveAt(i);

        Dictionary<int, PromptStack> byNumber = [];
        foreach (PromptStack s in Stacks)
            byNumber[s.PromptNumber] = s;

        for (int i = 0; i < prompts.Count; i++)
        {
            ArtPrompt prompt = prompts[i];
            List<ImageRecord> cards = catalog.GetStack(prompt.Number);

            if (byNumber.TryGetValue(prompt.Number, out PromptStack? existing))
            {
                existing.Cards = cards;
                if (topIndexes.TryGetValue(prompt.Number, out int savedIdx))
                    existing.SetTopIndex(savedIdx);
                else
                    existing.RefreshCards();

                int currentIdx = Stacks.IndexOf(existing);
                if (currentIdx != i)
                {
                    Stacks.RemoveAt(currentIdx);
                    Stacks.Insert(Math.Min(i, Stacks.Count), existing);
                }
            }
            else
            {
                PromptStack stack = new(prompt.Number, prompt.Title, prompt.Series, OnStackSelected);
                stack.Cards = cards;
                stack.RefreshCards();
                Stacks.Insert(Math.Min(i, Stacks.Count), stack);
            }
        }

        if (selectedPrompt.HasValue)
        {
            PromptStack? restored = Stacks.FirstOrDefault(s => s.PromptNumber == selectedPrompt.Value);
            if (restored is not null && m_selectedStack != restored)
                m_selectedStack = restored;
        }
    }

    public void AddResultToStacks(GenerateResult result, ArtPrompt prompt,
        ImageGeneratorSettings settings, string preset, string? loraTag, string? augment,
        ImageCatalog? catalog, string? modifiedPrompt = null)
    {
        if (!result.Success || result.FilePath is null || catalog is null) return;
        catalog.Add(new ImageRecord
        {
            PromptNumber = prompt.Number, Title = prompt.Title, Series = prompt.Series,
            FilePath = result.FilePath, SettingsTag = settings.SettingsTag,
            PromptText = modifiedPrompt ?? prompt.FullText, Style = prompt.Style,
            Seed = result.ActualSeed, RefinePreset = preset,
            ModifiedPrompt = modifiedPrompt ?? "",
            LoraTag = loraTag ?? "", PromptAugment = augment ?? "",
            SourceWidth = settings.Width, SourceHeight = settings.Height,
        });
    }

    public void SelectStack(int promptNumber)
    {
        PromptStack? stack = Stacks.FirstOrDefault(s => s.PromptNumber == promptNumber);
        if (stack is not null)
        {
            stack.SetTopIndex(0);
            SelectedStack = stack;
            m_detail.DetailImage = stack.TopCard;
        }
    }

    public void RefreshStack(int promptNumber, ImageCatalog? catalog)
    {
        if (catalog is null) return;
        PromptStack? stack = Stacks.FirstOrDefault(s => s.PromptNumber == promptNumber);
        if (stack is null) return;
        int prevCount = stack.Cards.Count;
        stack.Cards = catalog.GetStack(promptNumber);
        stack.RefreshCards();
        if (stack.Cards.Count > prevCount)
            FlashStack(stack);
    }

    static void FlashStack(PromptStack stack)
    {
        stack.JustUpdated = true;
        System.Windows.Threading.DispatcherTimer flashTimer = new()
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            stack.JustUpdated = false;
        };
        flashTimer.Start();
    }

    void OnStackSelected(PromptStack stack)
    {
        SelectedStack = stack;
        m_detail.DetailImage = stack.TopCard;
    }
}
