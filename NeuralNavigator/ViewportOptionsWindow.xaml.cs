using System.Windows;
///////////////////////////////////////////////
namespace NeuralNavigator;

public partial class ViewportOptionsWindow : Window
{
    public ViewportOptionsWindow()
    {
        InitializeComponent();
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
