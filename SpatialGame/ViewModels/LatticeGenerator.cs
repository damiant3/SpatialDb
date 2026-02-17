using HelixToolkit.Wpf;
using System.Windows.Media.Media3D;

namespace SpatialGame.ViewModels
{
    internal static class LatticeGenerator
    {
        //public static MeshGeometry3D GenerateLattice(int nx, int ny, int nz, double spacing = 1.0, double sphereRadius = 0.1)
        //{
        //    var builder = new MeshBuilder(false, false);

        //    var offsetX = (nx - 1) * spacing / 2.0;
        //    var offsetY = (ny - 1) * spacing / 2.0;
        //    var offsetZ = (nz - 1) * spacing / 2.0;

        //    for (int ix = 0; ix < nx; ix++)
        //    {
        //        for (int iy = 0; iy < ny; iy++)
        //        {
        //            for (int iz = 0; iz < nz; iz++)
        //            {
        //                var x = ix * spacing - offsetX;
        //                var y = iy * spacing - offsetY;
        //                var z = iz * spacing - offsetZ;

        //                builder.AddSphere(new Point3D(x, y, z), sphereRadius, 10, 10);
        //            }
        //        }
        //    }

        //    return builder.ToMesh(true);
        //}
    }
}