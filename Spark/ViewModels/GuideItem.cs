using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
///////////////////////////////////
namespace Spark.ViewModels;

enum GuideItemKind { Step, Warning, Hint }

sealed class GuideItem
{
    static readonly Brush s_doneBg = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0x32, 0xCD, 0x32)));
    static readonly Brush s_doneBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x44, 0x32, 0xCD, 0x32)));
    static readonly Brush s_errorBg = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0xCC, 0x33, 0x33)));
    static readonly Brush s_errorBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x44, 0xCC, 0x33, 0x33)));
    static readonly Brush s_normalBg = Freeze(new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)));
    static readonly Brush s_green = Freeze(new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32)));
    static readonly Brush s_red = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)));
    static readonly Brush s_gold = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)));

    public GuideItemKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string StatusNote { get; init; } = "";
    public bool HasStatus => StatusNote.Length > 0;
    public bool IsDone => StatusNote.StartsWith("✓");
    public bool IsError => StatusNote.StartsWith("❌");
    public Brush Background => IsDone ? s_doneBg : IsError ? s_errorBg : s_normalBg;
    public Brush BorderColor => IsDone ? s_doneBorder : IsError ? s_errorBorder : Brushes.Transparent;
    public Thickness BorderWidth => IsDone || IsError ? new(1) : new(0);
    public Brush StatusColor => IsDone ? s_green : IsError ? s_red : s_gold;
    public FontFamily? BodyFont => Body.Contains("    ") ? new FontFamily("Consolas") : null;

    public static GuideItem Step(string number, string title, string body, string statusNote = "") => new()
    {
        Kind = GuideItemKind.Step,
        Label = $"Step {number}: ",
        Title = title,
        Body = body,
        StatusNote = statusNote,
    };

    public static GuideItem Warning(string body) => new()
    {
        Kind = GuideItemKind.Warning,
        Body = body,
    };

    public static GuideItem Hint(string body) => new()
    {
        Kind = GuideItemKind.Hint,
        Body = "💡 " + body,
    };

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

sealed class GuideTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StepTemplate { get; set; }
    public DataTemplate? WarningTemplate { get; set; }
    public DataTemplate? HintTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not GuideItem gi) return base.SelectTemplate(item, container);
        return gi.Kind switch
        {
            GuideItemKind.Step => StepTemplate,
            GuideItemKind.Warning => WarningTemplate,
            GuideItemKind.Hint => HintTemplate,
            _ => StepTemplate,
        };
    }
}
