using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Spark.ViewModels;
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
            vm.Detail.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DetailViewModel.DetailRating))
                    UpdateStarDisplay(vm.Detail.DetailRating);
            };
            UpdateStarDisplay(vm.Detail.DetailRating);
        }
    }

    void UpdateStarDisplay(int rating)
    {
        foreach (object? child in StarPanel.Children)
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
            vm.Detail.ShowLightboxCommand.Execute(null);
    }
}
