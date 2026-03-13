using System.Globalization;
using System.Windows;
using System.Windows.Data;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the value is non-null,
/// <see cref="Visibility.Collapsed"/> when null.
/// </summary>
sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
