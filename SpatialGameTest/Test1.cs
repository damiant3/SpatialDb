using Microsoft.VisualStudio.TestTools.UnitTesting;
using SpatialGame.ViewModels;
using HelixToolkit.Maths;
using System;
using System.Linq;

namespace SpatialDb.SpatialGameTest;

[TestClass]
public sealed class Test1
{
    private const float EPS = 1e-3f;

    // Pseudocode / Plan:
    // 1. Retrieve low and med sphere geometry from GeometryCatalog.
    // 2. Assert the returned objects are not null.
    // 3. Assert Positions and Indices collections are present and non-empty.
    // 4. Assert Normals collection is present and non-empty (this is the core check).
    // 5. Assert Normals.Count equals Positions.Count for each geometry.
    // 6. Provide descriptive messages for each assertion so failures are diagnostic.
    [TestMethod]
    public void CheckSphereNormals()
    {
        var sphereLow = GeometryCatalog.GetOrCreate("Sphere", Resolution.Low);
        var sphereMed = GeometryCatalog.GetOrCreate("Sphere", Resolution.Medium);

        // Ensure geometry objects exist
        Assert.IsNotNull(sphereLow, "Sphere_LowRes should not be null");
        Assert.IsNotNull(sphereMed, "Sphere_MedRes should not be null");

        // Sphere_LowRes checks
        Assert.IsTrue(sphereLow.Positions != null && sphereLow.Positions.Count > 0, "Sphere_LowRes should contain positions");
        Assert.IsTrue(sphereLow.Indices != null && sphereLow.Indices.Count > 0, "Sphere_LowRes should contain indices");
        Assert.IsTrue(sphereLow.Normals != null && sphereLow.Normals.Count > 0, "Sphere_LowRes should contain normals");
        Assert.AreEqual(sphereLow.Positions.Count, sphereLow.Normals.Count, "Sphere_LowRes should have one normal per position");

        // Sphere_MedRes checks
        Assert.IsTrue(sphereMed.Positions != null && sphereMed.Positions.Count > 0, "Sphere_MedRes should contain positions");
        Assert.IsTrue(sphereMed.Indices != null && sphereMed.Indices.Count > 0, "Sphere_MedRes should contain indices");
        Assert.IsTrue(sphereMed.Normals != null && sphereMed.Normals.Count > 0, "Sphere_MedRes should contain normals");
        Assert.AreEqual(sphereMed.Positions.Count, sphereMed.Normals.Count, "Sphere_MedRes should have one normal per position");
    }

    [TestMethod]
    public void Compose_ReturnsNonNull_Material()
    {
        var m = MaterialCatalog.Compose("Bright_LimeGreen");
        Assert.IsNotNull(m);
    }

    [TestMethod]
    public void Compose_CachesByToken()
    {
        var a = MaterialCatalog.Compose("Bright_LimeGreen_CacheTest");
        var b = MaterialCatalog.Compose("Bright_LimeGreen_CacheTest");
        Assert.IsTrue(ReferenceEquals(a, b), "Compose should return the cached instance for the same token");
    }

    [TestMethod]
    public void Compose_WpfColorLookup_CachesDiffuse()
    {
        // Ensure the WPF named color is resolvable and cached into the diffuse dictionary
        var mat = MaterialCatalog.Compose("LimeGreen_UnitTest");
        Assert.IsNotNull(mat);

        // The diffuse dictionary should contain the entry after Compose
        Assert.IsTrue(MaterialCatalog.DiffuseColors.ContainsKey("LimeGreen") || MaterialCatalog.DiffuseColors.ContainsKey("limegreen"), "DiffuseColors should contain the WPF color after lookup");

        var diffuse = mat.DiffuseColor;
        // LimeGreen is a greenish color; expect green component to be noticeably larger than red/blue
        Assert.IsTrue(diffuse.Green > diffuse.Red && diffuse.Green > diffuse.Blue, "Diffuse.Green should be dominant for LimeGreen");
    }

    [TestMethod]
    public void Compose_Bright_IncreasesEmissive()
    {
        var normal = MaterialCatalog.Compose("LimeGreen_BasicTest");
        var bright = MaterialCatalog.Compose("Bright_LimeGreen_BasicTest");
        Assert.IsNotNull(normal);
        Assert.IsNotNull(bright);

        var eNormal = normal.EmissiveColor;
        var eBright = bright.EmissiveColor;

        // Bright should have higher emissive channel(s) than normal (or at least one channel)
        bool anyHigher = eBright.Red > eNormal.Red + EPS || eBright.Green > eNormal.Green + EPS || eBright.Blue > eNormal.Blue + EPS;
        Assert.IsTrue(anyHigher, "Bright token should increase emissive over the plain token");
    }

    [TestMethod]
    public void Compose_SpecularProfile_ChromeHasHighShininess()
    {
        var m = MaterialCatalog.Compose("Chrome_Red_Test");
        Assert.IsNotNull(m);
        var spec = m.SpecularColor;
        var shininess = m.SpecularShininess;

        // Chrome profile sets specular to white
        Assert.IsTrue(Math.Abs(spec.Red - 1f) < 1e-3f && Math.Abs(spec.Green - 1f) < 1e-3f && Math.Abs(spec.Blue - 1f) < 1e-3f,
            "Chrome specular should be (1,1,1)");

        Assert.IsTrue(shininess >= 150f, "Chrome/Reflective should set a high shininess value");
    }

    [TestMethod]
    public void Compose_UnknownToken_UsesDefaults()
    {
        var m = MaterialCatalog.Compose("TotallyUnknownToken_xyz");
        Assert.IsNotNull(m);
        // Defaults in MaterialCatalog are white diffuse and zero emissive (or defined defaults)
        var d = m.DiffuseColor;
        Assert.IsTrue(Math.Abs(d.Red - 1f) < EPS && Math.Abs(d.Green - 1f) < EPS && Math.Abs(d.Blue - 1f) < EPS,
            "Unknown token should leave diffuse at default white");
    }

    [TestMethod]
    public void GetOrCompose_UsesCacheOrComposes()
    {
        var t = "GetOrCompose_TestToken";
        // Ensure not present

        var m1 = MaterialCatalog.GetOrCompose(t);
        Assert.IsNotNull(m1);
        // subsequent GetOrCompose should return same instance from cache
        var m2 = MaterialCatalog.GetOrCompose(t);
        Assert.IsTrue(ReferenceEquals(m1, m2));
    }

    [TestMethod]
    public void Compose_EquivalentForms_ProduceSameMaterial()
    {
        var forms = new[]
        {
            "BrightLimeGreenTest",
            "Bright_LimeGreen_Test",
            "Bright-LimeGreen-Test",
            "Bright LimeGreen Test"
        };
        var mats = forms.Select(MaterialCatalog.Compose).ToArray();
        for (int i = 1; i < mats.Length; i++)
            Assert.IsTrue(ReferenceEquals(mats[0], mats[i]), $"All forms should resolve to the same cached material instance: {forms[i]}");
    }

    [TestMethod]
    public void Compose_GlowsColor_OverridesEmissive()
    {
        var m = MaterialCatalog.Compose("ShinyRedGlowsWhite");
        Assert.IsNotNull(m);
        // Emissive should be white, not red
        Assert.IsTrue(Math.Abs(m.EmissiveColor.Red - 1f) < EPS && Math.Abs(m.EmissiveColor.Green - 1f) < EPS && Math.Abs(m.EmissiveColor.Blue - 1f) < EPS,
            "GlowsWhite should set emissive to white");
    }

    [TestMethod]
    public void Compose_EmissiveColor_OverridesEmissive()
    {
        var m = MaterialCatalog.Compose("MatteBlueEmissiveYellow");
        Assert.IsNotNull(m);
        // Emissive should be yellow
        Assert.IsTrue(m.EmissiveColor.Red > 0.9f && m.EmissiveColor.Green > 0.9f && m.EmissiveColor.Blue < 0.2f,
            "EmissiveYellow should set emissive to yellow");
    }

    [TestMethod]
    public void Compose_GreedyColorMatch_LongColorNames()
    {
        var m = MaterialCatalog.Compose("DullLightSkyBlue");
        Assert.IsNotNull(m);
        // LightSkyBlue is a valid WPF color
        Assert.IsTrue(MaterialCatalog.DiffuseColors.ContainsKey("LightSkyBlue"), "DiffuseColors should contain LightSkyBlue");
        var d = m.DiffuseColor;
        Assert.IsTrue(d.Blue > d.Red && d.Blue > d.Green, "LightSkyBlue should be blue-dominant");
    }

    [TestMethod]
    public void Compose_CatalogsContainNormalizedKeys()
    {
        var keys = new[]
        {
            "BrightLimeGreenTest",
            "ShinyRedGlowsWhite",
            "MatteBlueEmissiveYellow",
            "DullLightSkyBlue"
        };
        foreach (var k in keys)
        {
            var m = MaterialCatalog.Compose(k);
            // Compose should add to Materials with the original string as key
            Assert.IsTrue(MaterialCatalog.Materials.ContainsKey(k), $"Materials should contain the original key: {k}");
            // DiffuseColors should contain the normalized color name
            if (k.Contains("LimeGreen"))
                Assert.IsTrue(MaterialCatalog.DiffuseColors.ContainsKey("LimeGreen"), "DiffuseColors should contain LimeGreen");
            if (k.Contains("Red"))
                Assert.IsTrue(MaterialCatalog.DiffuseColors.ContainsKey("Red"), "DiffuseColors should contain Red");
            if (k.Contains("Blue"))
                Assert.IsTrue(MaterialCatalog.DiffuseColors.ContainsKey("Blue") || MaterialCatalog.DiffuseColors.ContainsKey("LightSkyBlue"), "DiffuseColors should contain Blue or LightSkyBlue");
        }
    }
}

