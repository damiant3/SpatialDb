using System.Windows;
using System.Windows.Input;
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
        Viewport.PreviewMouseLeftButtonDown += (_, _) => Viewport.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        m_viewModel.Dispose();
        base.OnClosed(e);
    }
}
