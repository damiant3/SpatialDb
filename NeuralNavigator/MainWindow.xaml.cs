using System.Windows;
using System.Windows.Input;
///////////////////////////////////////////////
namespace NeuralNavigator;

partial class MainWindow : Window
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

    void OnGeneratedTokenClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GenerationTokenInfo token)
            m_viewModel.GenerationTokenClickCommand.Execute(token);
    }
}
