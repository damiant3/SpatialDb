using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
//using MeshGeometry3D IS NOT HelixToolkit.Geometry.MeshGeometry3D;
//using MeshGeometry3D IS NOT HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
////////////////////////////////
namespace SpatialGame.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
        {
            Regenerate(1, 1, 1, 1.0f, 5.0f); // Example: single sphere, radius 5
        }

        private MeshMaterial meshMaterial = default!;
        public MeshMaterial MeshMaterial { get => meshMaterial; set => SetProperty(ref meshMaterial, value); }

        private MeshGeometry3D latticeMesh = default!;
        public MeshGeometry3D LatticeMesh
        {
            get => latticeMesh;
            set { latticeMesh = value ?? new MeshGeometry3D(); OnPropertyChanged(); }
        }

        private Transform3D meshTransform = new MatrixTransform3D(Matrix3D.Identity);
        public Transform3D MeshTransform
        {
            get => meshTransform;
            set { meshTransform = value ?? new MatrixTransform3D(Matrix3D.Identity); OnPropertyChanged(); }
        }
        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();
        public void Regenerate(int nx, int ny, int nz, float spacing, float radius)
        {
            var builder = new MeshBuilder();
            builder.Reset();
            builder.AddSphere(new Vector3(0f, 0f, 0f), 5f, 32, 16);

            LatticeMesh = builder.ToMeshGeometry3D();

            MeshMaterial = new MeshMaterial
            {
                Name = "Dull White",
                AmbientColor = new Color4(0.5f, 0.5f, 0.5f, 1f),
                DiffuseColor = new Color4(1f, 1f, 1f, 1f),
                SpecularColor = new Color4(1f, 1f, 1f, 1f),
                EmissiveColor = new Color4(0.4f, 0.4f, 0.4f, 1f),
                SpecularShininess = 100f,
            };
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
}