using System.Numerics;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.Reflection;
using System.Windows.Media;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
using System.Windows.Media.Media3D;
/////////////////////////////////
namespace SpatialGame.ViewModels;
public enum Resolution { Low, Medium, High }
public static class GeometryCatalog
{
    public static readonly Dictionary<string, MeshGeometry3D> Geometries = [];

    public static MeshGeometry3D GetOrCreate(string key, Resolution resolution = Resolution.Low)
        => Geometries.TryGetValue(key, out var g) ? g : Create(key, resolution);

    public static MeshGeometry3D Create(string key, Resolution resolution = Resolution.Low)
    {
        key = string.IsNullOrWhiteSpace(key) ? "Sphere" : key.Trim().ToLowerInvariant();
        var b = new MeshBuilder();
        var buildAction = GetPrimitiveBuilder(key, resolution);
        buildAction(b);
        var mesh = b.ToMeshGeometry3D();
        Geometries[key] = mesh;
        return mesh;
    }

    private static Action<MeshBuilder> GetPrimitiveBuilder(string key, Resolution resolution)
    {
        if (key.Contains("sphere") || key.Contains("globe") || key.Contains("ball"))
        {
            // center at origin, radius 5, medium resolution
            int divisions = resolution switch
            {
                Resolution.Low => 16,
                Resolution.Medium => 32,
                Resolution.High => 64,
                _ => 32
            };
            return builder => builder.AddSphere(new Vector3(0f, 0f, 0f), 5f, divisions, divisions / 2);
        }

        // Cube / Box variants
        if (key.Contains("cube") || key.Contains("box") || key.Contains("square"))
        {
            // no resolution variants for a cube, it's just 6 faces - but we could consider subdivisions if desired
            // cube centered at origin, 10 units per side
            return builder => builder.AddBox(new Vector3(0f, 0f, 0f), 10f, 10f, 10f);
        }

        // Plane / Quad variants (represented as a thin box)
        if (key.Contains("plane") || key.Contains("quad") || key.Contains("floor"))
        {
            // wide, flat box
            return builder => builder.AddBox(new Vector3(0f, 0f, 0f), 10f, 0.1f, 10f);
        }

        // Cylinder variants
        if (key.Contains("cylinder") || key.Contains("tube"))
        {
            // like a sphere, do the resolution variants by adjusting the number of segments around the circumference
            var segments = resolution switch
            {
                Resolution.Low => 16,
                Resolution.Medium => 32,
                Resolution.High => 64,
                _ => 32
            };
            // vertical cylinder from y=-5 to y=5, radius 5
            return builder => builder.AddCylinder(new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), 5f, segments);
        }

        // Ellipsoid / oval: approximate with a sphere as a safe fallback (1-2 synonyms only)
        if (key.Contains("ellip") || key.Contains("oval"))
        {
            int divisions = resolution switch
            {
                Resolution.Low => 16,
                Resolution.Medium => 32,
                Resolution.High => 64,
                _ => 32
            };
            return builder => builder.AddSphere(new Vector3(0f, 0f, 0f), 5f, divisions, divisions / 2);
        }

        throw new ArgumentException($"Unrecognized geometry key: '{key}'");
    }
}