using System.Windows;
using SpatialGame.ViewModels;
//////////////////////
namespace SpatialGame;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewmodel;

    public MainWindow()
    {
        InitializeComponent();
        viewmodel = new MainViewModel();
        DataContext = viewmodel;
        Loaded += viewmodel.MainWindow_Loaded;
        Closed += (s, e) => (DataContext as IDisposable)?.Dispose();
    }
}