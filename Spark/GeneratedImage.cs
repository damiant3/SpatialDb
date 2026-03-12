///////////////////////////////////////////////
namespace Spark;

sealed class GeneratedImage
{
    public int PromptNumber { get; init; }
    public string Title { get; init; } = "";
    public string Series { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string SettingsTag { get; init; } = "";
    public string PromptText { get; init; } = "";
    public string Style { get; init; } = "";

    public string DisplayName => $"{PromptNumber:D2} — {Title}";
    public string SettingsDisplay => SettingsTag.Replace('_', ' ');
}
