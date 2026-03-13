using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
//////////////////////////////////////////
namespace Common.Wpf.Converters;

/// <summary>
/// Converts a boolean to a brush — green when true (service online), red when false (offline).
/// Used for service status indicator lights.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.LimeGreen;
    public Brush FalseBrush { get; set; } = Brushes.Tomato;

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueBrush : FalseBrush;
        return FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
