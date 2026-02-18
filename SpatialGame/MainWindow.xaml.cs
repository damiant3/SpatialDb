using System.Windows;
using SpatialGame.ViewModels;
using System.Diagnostics;
using System.Linq;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Media.Media3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
using Color4 = HelixToolkit.Maths.Color4;
using System.Reflection;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
//////////////////////
namespace SpatialGame;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewmodel;

    public MainWindow()
    {
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
        InitializeComponent();
        viewmodel = new MainViewModel();
        DataContext = viewmodel;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        UniverseView.Items.Add(viewmodel.SunModel);
        foreach (var kv in viewmodel.Planets)
            UniverseView.Items.Add(kv.Value);
        var jupiter = SolarSystem.Planets.First(p => p.name == "Jupiter");
        // Position camera: above Jupiter looking inward so you see Jupiter in front and sun at center
        var camZ = -jupiter.au * 8.0; // Jupiter z from SolarSystem distances
        var camPos = new Point3D(0, 20, camZ + 20); // above and slightly forward
        var lookDir = new Vector3D(0 - camPos.X, 0 - camPos.Y, 0 - camPos.Z);
        UniverseView.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(camPos.X, camPos.Y, camPos.Z),
            LookDirection = new Vector3D(lookDir.X, lookDir.Y, lookDir.Z),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45
        };
    }
}