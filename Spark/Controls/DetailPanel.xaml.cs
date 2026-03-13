using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
///////////////////////////////////////////////
namespace Spark.Controls;

partial class DetailPanel : UserControl
{
    public DetailPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireRatingUpdate();
    }

    void WireRatingUpdate()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.DetailRating))
                    UpdateStarDisplay(vm.DetailRating);
            };
            UpdateStarDisplay(vm.DetailRating);
        }
    }

    void UpdateStarDisplay(int rating)
    {
        foreach (var child in StarPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int starNum))
            {
                btn.Foreground = starNum <= rating
                    ? (Brush)FindResource("GoldBrush")
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            }
        }
    }

    void OnPreviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DataContext is MainViewModel vm)
            vm.ShowLightboxCommand.Execute(null);
    }
}
