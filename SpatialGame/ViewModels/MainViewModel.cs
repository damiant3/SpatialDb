using System.Windows.Media;
using System.Windows;
using HelixToolkit.SharpDX;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
////////////////////////////////
namespace SpatialGame.ViewModels
{
    public class MainViewModel : ViewModel
    {
        public MainViewModel()
        {
            Planets = SolarSystem.CreatePlanet3DData();
            SunModel = SolarSystem.CreateSunModel();
            EffectsManager = new DefaultEffectsManager();
            LightDetailsVisible = true;
            ShadowMapDistance = 2000;
            DirectionalLight = new DirectionalLight(new(0, 0, -1), Colors.White);
            PointLight = new PointLight(new(0, 10, 0), Colors.White, 200);
            SpotLight = new SpotLight(new(0, 0, 0), new(0, 0, -1), Colors.White, 500);
        }
        public MeshGeometryModel3D SunModel { get; }
        public Dictionary<string, MeshGeometryModel3D> Planets { get; }
        public IEffectsManager EffectsManager { get; }

        public DirectionalLight DirectionalLight { get; set; }
        public PointLight PointLight { get; set; }
        public SpotLight SpotLight { get; set; }

        public bool LightDetailsVisible { get; set; }
        public int ShadowMapDistance { get; set; }

        public void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow mainWindow) throw new InvalidOperationException("Expected sender to be the main window");
            if (mainWindow.UniverseView == null) throw new InvalidOperationException("UniverseView is not initialized in the main window");
            mainWindow.UniverseView.Items.Add(SunModel);
            foreach (var kv in Planets)
                mainWindow.UniverseView.Items.Add(kv.Value);
        }

    }
}