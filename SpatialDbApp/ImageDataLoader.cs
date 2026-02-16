using SpatialDbLib.Math;
using System.IO;

namespace SpatialDbApp.Loader;

public static class ImageDataLoader
{
    /// <summary>
    /// Convert a bitmap image into a list of PointData.
    /// Each sampled pixel becomes a PointData at Z=0. Coordinates are centered around origin.
    /// </summary>
    /// <param name="path">Image file path (.bmp, .png, .jpg, etc.)</param>
    /// <param name="sample">Pixel sampling step; 1 = every pixel, 2 = every other pixel, etc.</param>
    /// <param name="pixelSpacing">World units per pixel (default 1.0)</param>
    /// <param name="ignoreTransparent">If true skips fully transparent pixels (A==0)</param>
    /// <returns>List of PointData</returns>
    public static List<PointData> LoadBitmapAsPoints(string path, int sample = 1, double pixelSpacing = 1.0, bool ignoreTransparent = true)
    {
        if (sample <= 0) sample = 1;
        if (!File.Exists(path)) throw new FileNotFoundException("Image file not found", path);

        var list = new List<PointData>();

        using var bmp = new Bitmap(path);
        int width = bmp.Width;
        int height = bmp.Height;
        double cx = (width - 1) / 2.0;
        double cy = (height - 1) / 2.0;

        for (int y = 0; y < height; y += sample)
        {
            for (int x = 0; x < width; x += sample)
            {
                var px = bmp.GetPixel(x, y);
                if (ignoreTransparent && px.A == 0) continue;

                // Position: center image at origin, invert Y so image top maps to positive Y
                long worldX = (long)Math.Round((x - cx) * pixelSpacing);
                long worldY = (long)Math.Round((cy - y) * pixelSpacing);
                long worldZ = 0L;

                var color = (px.R, px.G, px.B);
                list.Add(new PointData(new LongVector3(worldX, worldY, worldZ), color, null));
            }
        }

        return list;
    }
}