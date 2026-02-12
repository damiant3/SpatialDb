using HelixToolkit.Wpf;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace SpatialDbApp;
public static class HelixUtils
{
    public static HelixViewport3D CreateViewport()
    {
        return new HelixViewport3D
        {
            Background = Brushes.Black, // Changed from LightGray to Black
            ShowCoordinateSystem = true,
            ShowViewCube = true,
            ShowFrameRate = true,
            IsHitTestVisible = true
        };
    }

    public static PerspectiveCamera CreateCamera(Point3D position)
    {
        return new PerspectiveCamera
        {
            Position = position,
            LookDirection = new Vector3D(0 - position.X, 0 - position.Y, 0 - position.Z),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45
        };
    }

    public static Model3DGroup CreateModelGroup()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Colors.Gray));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.5, -1, -0.75)));
        group.Children.Add(new PointLight(Color.FromRgb(0, 200, 0), new Point3D(60, 60, 120)));
        return group;
    }

    public static GeometryModel3D CreateSphereModel(Point3D center, double radius, int thetaDiv, int phiDiv)
    {
        var mesh = CreateSphereMesh(center, radius, thetaDiv, phiDiv);

        var materials = new MaterialGroup();
        materials.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 180, 0))));
        materials.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(160, 0, 220, 0))));

        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = materials,
            BackMaterial = materials
        };
    }

    public static ElementHost CreateElementHost(HelixViewport3D viewport, int top, int left, int height, int width)
    {
        return new ElementHost
        {
            Left = left,
            Top = top,
            Height = height,
            Width = width,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
            Child = viewport
        };
    }

    public static MeshGeometry3D CreateSphereMesh(Point3D center, double radius, int slices, int stacks)
    {
        MeshGeometry3D mesh = new();

        for (int i = 0; i <= stacks; i++)
        {
            double phi = (Math.PI / stacks) * i;
            double y = radius * Math.Cos(phi);
            double r = radius * Math.Sin(phi);

            for (int j = 0; j <= slices; j++)
            {
                double theta = (2 * Math.PI / slices) * j;
                double x = r * Math.Cos(theta);
                double z = r * Math.Sin(theta);

                mesh.Positions.Add(new Point3D(center.X + x, center.Y + y, center.Z + z));
                mesh.Normals.Add(new Vector3D(x, y, z)); // Normals for lighting
                mesh.TextureCoordinates.Add(new System.Windows.Point((int)((double)j / slices), (int)((double)i / stacks)));
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int first = (i * (slices + 1)) + j;
                int second = first + slices + 1;

                // First triangle
                mesh.TriangleIndices.Add(first);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(first + 1);

                // Second triangle
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(second + 1);
            }
        }

        return mesh;
    }

    public static Material DefaultMaterial { get; }
        = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(40, 120, 255))), // bright blue
                new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(80, 180, 255))), // self-illuminating
                new SpecularMaterial(new SolidColorBrush(Colors.White), 100.0) // shiny
            }
        };
}