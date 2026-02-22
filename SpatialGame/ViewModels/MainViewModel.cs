using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows;
using HelixToolkit.SharpDX;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
////////////////////////////////
namespace SpatialGame.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
        {
            Planets = SolarSystem.CreatePlanet3DData();
            SunModel = SolarSystem.CreateSunModel();
            EffectsManager = new DefaultEffectsManager();
            LightDetailsVisible = true;
            // Default light values
            DirectionalLightOn = true;
            DirectionalLightDirection = new Vector3D(0, 0, -1);
            DirectionalLightColor = Colors.White;
            PointLightOn = false;
            PointLightPosition = new Point3D(0, 10, 0);
            PointLightColor = Colors.White;
            PointLightRange = 200;
            PointLightRangeMin = 10;
            PointLightRangeMax = 1000;
            SpotLightOn = true;
            SpotLightPosition = new Point3D(0, 0, 0);
            SpotLightDirection = new Vector3D(0, 0, -1);
            SpotLightColor = Colors.White;
            SpotLightRange = 500;
            SpotLightRangeMin = 10;
            SpotLightRangeMax = 2000;
            SpotLightOuterAngle = 60;
            SpotLightOuterAngleMin = 10;
            SpotLightOuterAngleMax = 120;
            SpotLightInnerAngle = 30;
            SpotLightInnerAngleMin = 0;
            SpotLightInnerAngleMax = 120;
            SpotLightFalloff = 1.0;
            SpotLightFalloffMin = 0.1;
            SpotLightFalloffMax = 5.0;
        }
        public MeshGeometryModel3D SunModel { get; }
        public Dictionary<string, MeshGeometryModel3D> Planets { get; }
        public IEffectsManager EffectsManager { get; }

        // Directional Light
        private bool _directionalLightOn;
        public bool DirectionalLightOn { get => _directionalLightOn; set { _directionalLightOn = value; OnPropertyChanged(); } }
        private Vector3D _directionalLightDirection;
        public Vector3D DirectionalLightDirection { get => _directionalLightDirection; set { _directionalLightDirection = value; OnPropertyChanged(); } }
        public double DirectionalLightDirectionX { get => DirectionalLightDirection.X; set { DirectionalLightDirection = new Vector3D(value, DirectionalLightDirection.Y, DirectionalLightDirection.Z); OnPropertyChanged(); OnPropertyChanged(nameof(DirectionalLightDirection)); } }
        public double DirectionalLightDirectionY { get => DirectionalLightDirection.Y; set { DirectionalLightDirection = new Vector3D(DirectionalLightDirection.X, value, DirectionalLightDirection.Z); OnPropertyChanged(); OnPropertyChanged(nameof(DirectionalLightDirection)); } }
        public double DirectionalLightDirectionZ { get => DirectionalLightDirection.Z; set { DirectionalLightDirection = new Vector3D(DirectionalLightDirection.X, DirectionalLightDirection.Y, value); OnPropertyChanged(); OnPropertyChanged(nameof(DirectionalLightDirection)); } }
        private Color _directionalLightColor;
        public Color DirectionalLightColor { get => _directionalLightColor; set { _directionalLightColor = value; OnPropertyChanged(); } }

        // Point Light
        private bool _pointLightOn;
        public bool PointLightOn { get => _pointLightOn; set { _pointLightOn = value; OnPropertyChanged(); } }
        private Point3D _pointLightPosition;
        public Point3D PointLightPosition { get => _pointLightPosition; set { _pointLightPosition = value; OnPropertyChanged(); } }
        public double PointLightPositionX { get => PointLightPosition.X; set { PointLightPosition = new Point3D(value, PointLightPosition.Y, PointLightPosition.Z); OnPropertyChanged(); OnPropertyChanged(nameof(PointLightPosition)); } }
        public double PointLightPositionY { get => PointLightPosition.Y; set { PointLightPosition = new Point3D(PointLightPosition.X, value, PointLightPosition.Z); OnPropertyChanged(); OnPropertyChanged(nameof(PointLightPosition)); } }
        public double PointLightPositionZ { get => PointLightPosition.Z; set { PointLightPosition = new Point3D(PointLightPosition.X, PointLightPosition.Y, value); OnPropertyChanged(); OnPropertyChanged(nameof(PointLightPosition)); } }
        private Color _pointLightColor;
        public Color PointLightColor { get => _pointLightColor; set { _pointLightColor = value; OnPropertyChanged(); } }
        private double _pointLightRange;
        public double PointLightRange { get => _pointLightRange; set { _pointLightRange = value; OnPropertyChanged(); } }
        public double PointLightRangeMin { get; set; }
        public double PointLightRangeMax { get; set; }

        // Spot Light
        private bool _spotLightOn;
        public bool SpotLightOn { get => _spotLightOn; set { _spotLightOn = value; OnPropertyChanged(); } }
        private Point3D _spotLightPosition;
        public Point3D SpotLightPosition { get => _spotLightPosition; set { _spotLightPosition = value; OnPropertyChanged(); } }
        public double SpotLightPositionX { get => SpotLightPosition.X; set { SpotLightPosition = new Point3D(value, SpotLightPosition.Y, SpotLightPosition.Z); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightPosition)); } }
        public double SpotLightPositionY { get => SpotLightPosition.Y; set { SpotLightPosition = new Point3D(SpotLightPosition.X, value, SpotLightPosition.Z); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightPosition)); } }
        public double SpotLightPositionZ { get => SpotLightPosition.Z; set { SpotLightPosition = new Point3D(SpotLightPosition.X, SpotLightPosition.Y, value); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightPosition)); } }
        private Vector3D _spotLightDirection;
        public Vector3D SpotLightDirection { get => _spotLightDirection; set { _spotLightDirection = value; OnPropertyChanged(); } }
        public double SpotLightDirectionX { get => SpotLightDirection.X; set { SpotLightDirection = new Vector3D(value, SpotLightDirection.Y, SpotLightDirection.Z); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightDirection)); } }
        public double SpotLightDirectionY { get => SpotLightDirection.Y; set { SpotLightDirection = new Vector3D(SpotLightDirection.X, value, SpotLightDirection.Z); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightDirection)); } }
        public double SpotLightDirectionZ { get => SpotLightDirection.Z; set { SpotLightDirection = new Vector3D(SpotLightDirection.X, SpotLightDirection.Y, value); OnPropertyChanged(); OnPropertyChanged(nameof(SpotLightDirection)); } }
        private Color _spotLightColor;
        public Color SpotLightColor { get => _spotLightColor; set { _spotLightColor = value; OnPropertyChanged(); } }
        private double _spotLightRange;
        public double SpotLightRange { get => _spotLightRange; set { _spotLightRange = value; OnPropertyChanged(); } }
        public double SpotLightRangeMin { get; set; }
        public double SpotLightRangeMax { get; set; }
        private double _spotLightOuterAngle;
        public double SpotLightOuterAngle { get => _spotLightOuterAngle; set { _spotLightOuterAngle = value; OnPropertyChanged(); } }
        public double SpotLightOuterAngleMin { get; set; }
        public double SpotLightOuterAngleMax { get; set; }
        private double _spotLightInnerAngle;
        public double SpotLightInnerAngle { get => _spotLightInnerAngle; set { _spotLightInnerAngle = value; OnPropertyChanged(); } }
        public double SpotLightInnerAngleMin { get; set; }
        public double SpotLightInnerAngleMax { get; set; }
        private double _spotLightFalloff;
        public double SpotLightFalloff { get => _spotLightFalloff; set { _spotLightFalloff = value; OnPropertyChanged(); } }
        public double SpotLightFalloffMin { get; set; }
        public double SpotLightFalloffMax { get; set; }

        private bool _lightDetailsVisible;
        public bool LightDetailsVisible { get => _lightDetailsVisible; set { _lightDetailsVisible = value; OnPropertyChanged(); } }

        public void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow mainWindow ) throw new InvalidOperationException("Expected sender to be the main window");
            if (mainWindow.UniverseView == null) throw new InvalidOperationException("UniverseView is not initialized in the main window");
            mainWindow.UniverseView.Items.Add(SunModel);
            foreach (var kv in Planets)
                mainWindow.UniverseView.Items.Add(kv.Value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}