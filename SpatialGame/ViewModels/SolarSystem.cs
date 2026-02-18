using System.Numerics;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;

namespace SpatialGame.ViewModels
{
    public static class SolarSystem
    {
        private static MeshMaterial CloneMaterial(MeshMaterial src)
        {
            if (src == null) return new MeshMaterial();
            return new MeshMaterial
            {
                Name = src.Name,
                AmbientColor = src.AmbientColor,
                DiffuseColor = src.DiffuseColor,
                EmissiveColor = src.EmissiveColor,
                SpecularColor = src.SpecularColor,
                SpecularShininess = src.SpecularShininess
            };
        }

        public static MeshGeometryModel3D CreateSunModel()
        {
            var geo = GeometryCatalog.GetOrCreate("Sphere", Resolution.High);
            var baseMat = MaterialCatalog.Compose("Bright_Yellow_Emissive");
            // Clone to avoid mutating shared cached material
            var mat = CloneMaterial(baseMat);
            // Strong emissive/specular to suggest a star
            mat.EmissiveColor = new Color4(1f, 0.95f, 0.6f, 1f);
            mat.SpecularColor = new Color4(1f, 1f, 0.9f, 1f);
            mat.SpecularShininess = MathF.Max(mat.SpecularShininess, 500f);

            return new MeshGeometryModel3D
            {
                Geometry = geo,
                Material = mat,
                Transform = new MatrixTransform3D(Matrix3D.Identity)
            };
        }

        public static readonly (string name, double au, double sizeRatio, string materialDescription)[] Planets =
        [
            ("Mercury", 0.39, 0.383, "Gray"),
            ("Venus", 0.72, 0.949, "White"),
            ("Earth", 1.00, 1.000, "Blue"),
            ("Mars", 1.52, 0.532, "Red"),
            ("Jupiter", 5.20, 11.21, "Gold"),
            ("Saturn", 9.58, 9.45, "Yellow"),
            ("Uranus", 19.2, 4.01, "LightBlue"),
            ("Neptune", 30.05, 3.88, "SkyBlue")
        ];
        public static Dictionary<string, MeshGeometryModel3D> CreatePlanet3DData()
        {
            var dict = new Dictionary<string, MeshGeometryModel3D>();

            // Simplified solar system parameters (relative)
            float sunRadius = 5f;
            double distanceScale = 12.0;
            double sizeScale = 0.06;
            double baseOrbit = sunRadius * 1.6;

            foreach (var p in Planets)
            {
                double radius = sunRadius * p.sizeRatio * sizeScale;
                double z = -(baseOrbit + p.au * distanceScale);

                // Get material from catalog - it already handles everything correctly
                var baseMat = MaterialCatalog.GetOrCompose(p.materialDescription);
                var mat = CloneMaterial(baseMat);

                // Set up transform
                var tg = new Transform3DGroup();
                tg.Children.Add(new ScaleTransform3D(radius, radius, radius));
                tg.Children.Add(new TranslateTransform3D(0, 0, z));

                // Pick appropriate geometry resolution
                var planetGeo = p.sizeRatio > 1.0
                    ? GeometryCatalog.GetOrCreate("Sphere", Resolution.High)
                    : GeometryCatalog.GetOrCreate("Sphere", Resolution.High);

                var model = new MeshGeometryModel3D
                {
                    Geometry = planetGeo,
                    Material = mat,
                    Transform = tg
                };

                dict[p.name] = model;
            }

            return dict;
        }
    }
}
