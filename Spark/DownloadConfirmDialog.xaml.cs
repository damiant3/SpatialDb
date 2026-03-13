using Common.Wpf.Input;
using Spark.Presenters;
using Spark.Services;
using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark;

partial class DownloadConfirmDialog
{
    public DownloadConfirmDialog(string serviceName, ServiceEndpointConfig config)
    {
        InitializeComponent();

        SetupGuideViewModel vm = new();
        SetupGuidePresenter presenter = new(serviceName, config, vm);

        vm.RescanCommand = new RelayCommand(async _ => await presenter.RescanAsync());
        vm.OpenPageCommand = new RelayCommand(_ => presenter.OpenDownloadPage());
        vm.OpenTerminalCommand = new RelayCommand(_ => presenter.OpenTerminal());
        vm.AutoRepairCommand = new RelayCommand(async _ => await presenter.AutoRepairAsync());
        vm.ResetVenvCommand = new RelayCommand(async _ => await presenter.ResetVenvAsync());

        DataContext = vm;
        Title = vm.HeaderText;

        Loaded += async (_, _) => await presenter.ScanAsync();
    }
}
