using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
//using MeshGeometry3D IS NOT HelixToolkit.Geometry.MeshGeometry3D;
//using MeshGeometry3D IS NOT HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
////////////////////////////////
namespace SpatialGame.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
        {
            // Use shared geometry from the catalog
            SharedSphereGeometry = GeometryCatalog.GetOrCreate("Sphere", Resolution.Medium);
            SharedMaterial = MaterialCatalog.Compose("Bright_LimeGreen");

            latticeMesh = SharedSphereGeometry;
            meshMaterial = SharedMaterial;
            meshTransform = new MatrixTransform3D(Matrix3D.Identity);

            MeshModel = new MeshGeometryModel3D
            {
                Geometry = latticeMesh,
                Material = meshMaterial,
                Transform = meshTransform
            };

            // Solar system models are produced by helper
            Planets = SolarSystem.CreatePlanet3DData();
            SunModel = SolarSystem.CreateSunModel();
        }

        public MeshGeometryModel3D SunModel { get; }
        public Dictionary<string, MeshGeometryModel3D> Planets { get; }

        public MeshGeometry3D SharedSphereGeometry { get; }
        public MeshMaterial SharedMaterial { get; }
        public MeshGeometryModel3D MeshModel { get; }

        private MeshMaterial meshMaterial = default!;
        public MeshMaterial MeshMaterial { get => meshMaterial; set => SetMaterial(value); }

        private MeshGeometry3D latticeMesh = default!;
        public MeshGeometry3D LatticeMesh
        {
            get => latticeMesh;
            set { SetGeometry(value ?? new MeshGeometry3D()); }
        }

        private Transform3D meshTransform = new MatrixTransform3D(Matrix3D.Identity);
        public Transform3D MeshTransform
        {
            get => meshTransform;
            set { SetTransform(value ?? new MatrixTransform3D(Matrix3D.Identity)); }
        }

        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();

        public void SetGeometry(MeshGeometry3D geo)
        {
            if (geo == null) return;
            latticeMesh = geo;
            MeshModel.Geometry = latticeMesh;
            OnPropertyChanged(nameof(LatticeMesh));
        }

        public void SetMaterial(MeshMaterial mat)
        {
            if (mat == null) return;
            meshMaterial = mat;
            MeshModel.Material = meshMaterial;
            OnPropertyChanged(nameof(MeshMaterial));
        }

        public void SetTransform(Transform3D t)
        {
            if (t == null) return;
            meshTransform = t;
            MeshModel.Transform = meshTransform;
            OnPropertyChanged(nameof(MeshTransform));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string propertyName = "")
        {
            if (!Equals(field, newValue))
            {
                field = newValue;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }
            return false;
        }

    }

    static class ReflectionExtensions
    {
        public static IEnumerable<Type> GetTypesSafe(this Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }
    }
}