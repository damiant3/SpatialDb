using HelixToolkit.Wpf.SharpDX;
using System.ComponentModel;


namespace SpatialGameTest;



public static class Color4Extensions
{
    public static string ToString2(this HelixToolkit.Maths.Color4 c)
        => $"A={c.Alpha:F3} R={c.Red:F3} G={c.Green:F3} B={c.Blue:F3}";
}
public static class PhongMaterialExtensions
{
    public static string ToString2(this PhongMaterial mat)
    {
        if (mat == null) return "null";
        return
            $"Name: {mat.Name}, " +
            $"Ambient: {ColorToString(mat.AmbientColor)}, " +
            $"Diffuse: {ColorToString(mat.DiffuseColor)}, " +
            $"Specular: {ColorToString(mat.SpecularColor)}, " +
            $"Emissive: {ColorToString(mat.EmissiveColor)}, " +
            $"Shininess: {mat.SpecularShininess}, " +
            $"RenderShadowMap: {mat.RenderShadowMap}, " +
            $"EnableFlatShading: {mat.EnableFlatShading}, " +
            $"EnableTessellation: {mat.EnableTessellation}";
    }

    private static string ColorToString(HelixToolkit.Maths.Color4 c)
        => $"A={c.Alpha:F3} R={c.Red:F3} G={c.Green:F3} B={c.Blue:F3}";
}
