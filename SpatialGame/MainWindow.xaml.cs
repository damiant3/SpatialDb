using System.Windows;
using SpatialGame.ViewModels;
using System.Diagnostics;
//////////////////////
namespace SpatialGame;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
        InitializeComponent();
        viewModel = new MainViewModel();
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {

    }
}