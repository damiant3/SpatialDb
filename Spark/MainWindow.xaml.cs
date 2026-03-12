using System.Windows;
///////////////////////////////////////////////
namespace Spark;

partial class MainWindow : Window
{
    MainViewModel m_viewModel;

    public MainWindow()
    {
        InitializeComponent();
        m_viewModel = new MainViewModel();
        DataContext = m_viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        m_viewModel.Dispose();
        base.OnClosed(e);
    }
}
