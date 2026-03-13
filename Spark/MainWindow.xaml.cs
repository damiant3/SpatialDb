using System.Windows;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark;

partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ServiceHost host = AppBootstrap.Configure();
        DataContext = host.Require<MainViewModel>();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ServiceHost.IsInitialized)
            ServiceHost.Instance.Dispose();
        base.OnClosed(e);
    }
}
