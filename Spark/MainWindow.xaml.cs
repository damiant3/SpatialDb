using System.Windows;
using System.Windows.Input;
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

    void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GeneratedImage img)
            m_viewModel.SelectedImage = img;
    }
}
