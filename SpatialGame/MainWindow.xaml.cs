using System.Windows;
using SpatialGame.ViewModels;
//////////////////////
namespace SpatialGame;

public partial class MainWindow : Window
{
    MainViewModel viewmodel;
    public MainWindow()
    {
        InitializeComponent();
        viewmodel = new MainViewModel();
        DataContext = viewmodel;
        Loaded += viewmodel.MainWindow_Loaded;
    }
}