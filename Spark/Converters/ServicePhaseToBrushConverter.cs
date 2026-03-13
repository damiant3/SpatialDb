using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.Converters;

/// <summary>
/// Converts a <see cref="ServicePhase"/> to a colored brush for the indicator ellipses.
/// Internal — referenced from XAML in the same assembly.
/// </summary>
[System.Windows.Markup.MarkupExtensionReturnType(typeof(IValueConverter))]
sealed class ServicePhaseToBrushConverter : IValueConverter
{
    static readonly SolidColorBrush s_online = new(Color.FromRgb(0x32, 0xCD, 0x32));  // LimeGreen
    static readonly SolidColorBrush s_offline = new(Color.FromRgb(0xFF, 0x63, 0x47)); // Tomato
    static readonly SolidColorBrush s_probing = new(Color.FromRgb(0xFF, 0xD7, 0x00)); // Gold
    static readonly SolidColorBrush s_starting = new(Color.FromRgb(0x1E, 0x90, 0xFF)); // DodgerBlue
    static readonly SolidColorBrush s_unknown = new(Color.FromRgb(0x80, 0x80, 0x80)); // Gray

    static ServicePhaseToBrushConverter()
    {
        s_online.Freeze();
        s_offline.Freeze();
        s_probing.Freeze();
        s_starting.Freeze();
        s_unknown.Freeze();
    }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ServicePhase phase)
        {
            return phase switch
            {
                ServicePhase.Online => s_online,
                ServicePhase.Offline => s_offline,
                ServicePhase.Probing => s_probing,
                ServicePhase.Starting => s_starting,
                _ => s_unknown,
            };
        }
        return s_unknown;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
