using System.Windows;
///////////////////////////////////////////////
namespace NeuralNavigator;

public partial class MainWindow : Window
{
    MainViewModel m_viewModel;

    public MainWindow()
    {
        InitializeComponent();
        m_viewModel = new MainViewModel(Viewport);
        DataContext = m_viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        m_viewModel.Dispose();
        base.OnClosed(e);
    }
}
