using Common.Core.Net;
using Spark.Presenters;
using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark.Services;

static class AppBootstrap
{
    public static ServiceHost Configure()
    {
        ServiceHost host = ServiceHost.Initialize();

        // ── Service configuration ──────────────────────────────
        ServiceEndpointConfig serviceConfig = ServiceEndpointConfig.Load();
        host.Register(serviceConfig);

        // ── Typed endpoints (one per service, resolved by type) ─
        host.Register(new ServiceUri<OllamaApi>(serviceConfig.Ollama.BaseUrl));
        host.Register(new ServiceUri<StableDiffusionApi>(serviceConfig.StableDiffusion.BaseUrl));
        host.Register(new ServiceUri<MusicGenApi>(serviceConfig.MusicGen.BaseUrl));

        // ── Service clients ────────────────────────────────────
        host.RegisterFactory(() => new OllamaClient(host.Require<ServiceUri<OllamaApi>>()));
        host.RegisterFactory(() => new MusicGenClient(host.Require<ServiceUri<MusicGenApi>>()));
        host.RegisterFactory(() => new GenerationService(host.Require<ServiceUri<StableDiffusionApi>>()));
        host.RegisterFactory(() => new LoraService(host.Require<GenerationService>().Generator));

        // ── Local service manager (probes via the service clients) ─
        host.RegisterFactory(() => new LocalServiceManager(
            host.Require<ServiceEndpointConfig>(),
            host.Require<OllamaClient>(),
            host.Require<GenerationService>().Generator,
            host.Require<MusicGenClient>()));

        // ── View models ────────────────────────────────────────
        host.RegisterType<LogViewModel>();
        host.RegisterType<GenerationSettingsViewModel>();
        host.RegisterFactory(() => new LoraViewModel(
            host.Require<LoraService>(),
            host.Require<LogViewModel>()));
        host.RegisterFactory(() => new DetailViewModel(host.Require<LogViewModel>()));
        host.RegisterFactory(() => new GalleryViewModel(host.Require<DetailViewModel>()));
        host.RegisterType<StatusViewModel>();
        host.RegisterType<MusicViewModel>();
        host.RegisterType<SfxViewModel>();

        // ── Presenters ────────────────────────────────────────
        host.RegisterFactory(() => new MusicPresenter(
            host.Require<MusicGenClient>(),
            host.Require<LogViewModel>(),
            host.Require<MusicViewModel>()));
        host.RegisterFactory(() => new SfxPresenter(
            host.Require<MusicGenClient>(),
            host.Require<LogViewModel>(),
            host.Require<SfxViewModel>()));

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
            host.Require<SfxViewModel>(),
            host.Require<MusicPresenter>(),
            host.Require<SfxPresenter>(),
            host.Require<LocalServiceManager>()));

        return host;
    }
}
