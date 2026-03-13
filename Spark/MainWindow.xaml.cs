using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark;

partial class MainWindow : Window
{
    DispatcherTimer? m_clickTimer;
    string? m_pendingClickService;

    public MainWindow()
    {
        InitializeComponent();
        ServiceHost host = AppBootstrap.Configure();
        MainViewModel vm = host.Require<MainViewModel>();
        DataContext = vm;

        // Wire up blinking animation for probe phase
        vm.ServiceManager.Ollama.PropertyChanged += OnServicePhaseChanged;
        vm.ServiceManager.StableDiffusion.PropertyChanged += OnServicePhaseChanged;
        vm.ServiceManager.MusicGen.PropertyChanged += OnServicePhaseChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ServiceHost.IsInitialized)
            ServiceHost.Instance.Dispose();
        base.OnClosed(e);
    }

    // ── Blink animation management ─────────────────────────────

    void OnServicePhaseChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServiceStatus.Phase)) return;
        UpdateBlinkAnimation();
    }

    void UpdateBlinkAnimation()
    {
        MainViewModel? vm = DataContext as MainViewModel;
        if (vm is null) return;

        ApplyBlink(OllamaIndicator, vm.ServiceManager.Ollama.Phase is ServicePhase.Probing or ServicePhase.Starting);
        ApplyBlink(SdIndicator, vm.ServiceManager.StableDiffusion.Phase is ServicePhase.Probing or ServicePhase.Starting);
        ApplyBlink(MusicGenIndicator, vm.ServiceManager.MusicGen.Phase is ServicePhase.Probing or ServicePhase.Starting);
    }

    void ApplyBlink(System.Windows.Shapes.Ellipse indicator, bool shouldBlink)
    {
        if (shouldBlink)
        {
            DoubleAnimation anim = new()
            {
                From = 1.0,
                To = 0.2,
                Duration = TimeSpan.FromMilliseconds(400),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            indicator.BeginAnimation(OpacityProperty, anim);
        }
        else
        {
            indicator.BeginAnimation(OpacityProperty, null);
            indicator.Opacity = 1.0;
        }
    }

    // ── Service indicator event handlers ────────────────────────

    async void OnServiceIndicatorClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string serviceName) return;

        MainViewModel? vm = DataContext as MainViewModel;
        if (vm is null) return;

        LocalServiceManager mgr = vm.ServiceManager;

        if (e.ClickCount >= 2)
        {
            // Cancel pending single-click
            m_clickTimer?.Stop();
            m_pendingClickService = null;

            // Double-click: try to start the service
            ServiceStatus status = mgr.ResolveStatus(serviceName);
            if (status.IsAvailable) return;

            ServiceEndpoint? ep = mgr.ResolveEndpoint(serviceName);
            if (ep is null) return;
            if (string.IsNullOrWhiteSpace(ep.ExecutablePath))
            {
                DownloadConfirmDialog dlg = new(serviceName, mgr.Config) { Owner = this };
                dlg.ShowDialog();
                return;
            }

            string result = await mgr.TryStartServiceAsync(serviceName);
            if (result.Contains("No executable") || result.Contains("not configured"))
            {
                DownloadConfirmDialog dlg = new(serviceName, mgr.Config) { Owner = this };
                dlg.ShowDialog();
            }
            vm.Log.Log($"🔌 {result}");
        }
        else if (e.ClickCount == 1)
        {
            // Defer single-click so double-click can cancel it
            m_pendingClickService = serviceName;
            m_clickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            m_clickTimer.Stop();
            m_clickTimer.Tick += OnSingleClickTimer;
            m_clickTimer.Start();
        }
    }

    void OnSingleClickTimer(object? sender, EventArgs e)
    {
        m_clickTimer?.Stop();
        if (m_clickTimer is not null)
            m_clickTimer.Tick -= OnSingleClickTimer;

        string? serviceName = m_pendingClickService;
        m_pendingClickService = null;
        if (serviceName is null) return;

        MainViewModel? vm = DataContext as MainViewModel;
        if (vm is null) return;

        DownloadConfirmDialog dlg = new(serviceName, vm.ServiceManager.Config) { Owner = this };
        dlg.ShowDialog();
    }

    // kept for backward compatibility if wired elsewhere
    async void OnStartService(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string serviceName) return;
        MainViewModel? vm = DataContext as MainViewModel;
        if (vm is null) return;

        string result = await vm.ServiceManager.TryStartServiceAsync(serviceName);
        vm.Log.Log($"🔌 {result}");
    }

    async void OnRefreshServices(object sender, RoutedEventArgs e)
    {
        MainViewModel? vm = DataContext as MainViewModel;
        if (vm is null) return;
        await vm.ServiceManager.ProbeAllAsync();
        vm.Log.Log("🔌 Service status refreshed.");
    }
}
