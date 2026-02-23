using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
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
            DirectionalLight = new DirectionalLight(new(0, 0, -1), Colors.White);
            PointLight = new PointLight(new(0, 0, 0), Colors.White, 200);
            SpotLight = new SpotLight(new(0, 0, 0), new(0, 0, -1), Colors.White, 500);
            AmbientLight = new AmbientLight(Colors.Black);
            Camera1 = new PerspectiveCamera
            {
                Position = new(0, 50, 100),
                LookDirection = new(0, -50, -100),
                UpDirection = new(0, 1, 0),
                FieldOfView = 45
            };
            ShadowMap = new ShadowMapViewModel
            {
                LightCamera = Camera1
            };

        }
        public Camera Camera1 { get; }
        public MeshGeometryModel3D SunModel { get; }
        public Dictionary<string, MeshGeometryModel3D> Planets { get; }
        public IEffectsManager EffectsManager { get; }
        public DirectionalLight DirectionalLight { get; set; }
        public PointLight PointLight { get; set; }
        public SpotLight SpotLight { get; set; }
        public AmbientLight AmbientLight { get; set; }
        public bool LightDetailsVisible { get; set; }
        public ShadowMapViewModel ShadowMap { get; set; }
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