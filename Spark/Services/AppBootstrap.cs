using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark.Services;

/// <summary>
/// Configures the ServiceHost with all services and view models at startup.
/// Call <see cref="Configure"/> once from App or MainWindow before any UI is built.
/// </summary>
static class AppBootstrap
{
    public static ServiceHost Configure()
    {
        ServiceHost host = ServiceHost.Initialize();

        // ── Core services (singletons) ─────────────────────────
        host.Register(new GenerationService());
        host.RegisterFactory(() => new LoraService(host.Require<GenerationService>().Generator));

        // ── View models ────────────────────────────────────────
        host.RegisterType<LogViewModel>();
        host.RegisterType<GenerationSettingsViewModel>();
        host.RegisterFactory(() => new LoraViewModel(
            host.Require<LoraService>(),
            host.Require<LogViewModel>()));
        host.RegisterFactory(() => new DetailViewModel(host.Require<LogViewModel>()));
        host.RegisterFactory(() => new GalleryViewModel(host.Require<DetailViewModel>()));
        host.RegisterType<StatusViewModel>();
        host.RegisterFactory(() => new MusicViewModel(host.Require<LogViewModel>()));
        host.RegisterFactory(() => new SfxViewModel(host.Require<LogViewModel>()));

        // MainViewModel is the root coordinator — resolves everything
        host.RegisterFactory(() => new MainViewModel(
            host.Require<GenerationService>(),
            host.Require<LoraService>(),
            host.Require<LogViewModel>(),
            host.Require<GenerationSettingsViewModel>(),
            host.Require<LoraViewModel>(),
            host.Require<DetailViewModel>(),
            host.Require<GalleryViewModel>(),
            host.Require<StatusViewModel>(),
            host.Require<MusicViewModel>(),
            host.Require<SfxViewModel>()));

        return host;
    }
}
