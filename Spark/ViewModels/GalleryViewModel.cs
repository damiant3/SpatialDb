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
        int? selectedPrompt = m_selectedStack?.PromptNumber;
        Stacks.Clear();
        if (catalog is null) return;

        foreach (ArtPrompt prompt in prompts)
        {
            PromptStack stack = new(prompt.Number, prompt.Title, prompt.Series, OnStackSelected);
            stack.Cards = catalog.GetStack(prompt.Number);
            stack.RefreshCards();
            Stacks.Add(stack);
        }

        if (selectedPrompt.HasValue)
            SelectedStack = Stacks.FirstOrDefault(s => s.PromptNumber == selectedPrompt.Value);
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
        stack.Cards = catalog.GetStack(promptNumber);
        stack.RefreshCards();
    }

    void OnStackSelected(PromptStack stack)
    {
        SelectedStack = stack;
        m_detail.DetailImage = stack.TopCard;
    }
}
