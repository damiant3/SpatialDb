using System.Numerics;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.Reflection;
using System.Windows.Media;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshMaterial = HelixToolkit.Wpf.SharpDX.PhongMaterial;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;

namespace SpatialGame.ViewModels
{
    public class SpecularProfile(Color4 color, float? shininess = null)
    {
        public Color4 SpecularColor { get; init; } = color;
        public float? ShininessOverride { get; init; } = shininess;
    }

    public class MaterialModifier(float diffuseMul, float emissiveMul, float specularMul, float shininessMul, float? shininessOverride = null)
    {
        public float DiffuseMul { get; init; } = diffuseMul;
        public float EmissiveMul { get; init; } = emissiveMul;
        public float SpecularMul { get; init; } = specularMul;
        public float ShininessMul { get; init; } = shininessMul;
        public float? ShininessOverride { get; init; } = shininessOverride;
    }

    public static class SpecularCatalog
    {
        public static readonly SpecularProfileDictionary Profiles;

        static SpecularCatalog()
        {
            Profiles = new SpecularProfileDictionary
            {
                ["Default"] = new SpecularProfile(new Color4(1f, 1f, 1f, 1f), null),
                ["Chrome"] = new SpecularProfile(new Color4(1f, 1f, 1f, 1f), 200f),
            };
        }

        public static bool TryGet(string key, out SpecularProfile profile)
            => Profiles.TryGetValue(key, out profile!);
    }

    public static class ModifierCatalog
    {
        // Material property modifiers follow physical lighting principles:
        // - Bright/Dim/Glowing: Control emissive (how much the material glows/radiates light)
        // - Shiny/Dull/Matte/Reflective: Control specular reflections (how mirror-like the surface is)
        // - Dark/Light: Should be part of color name itself (e.g., DarkBlue vs LightBlue)
        //
        // Examples:
        //   "Bright_Pink" = Pink material that glows
        //   "Shiny_Red" = Red material with mirror-like reflections
        //   "Bright_Shiny_DarkPink" = DarkPink that both glows AND reflects
        //   "Dull_LightBlue" = Light blue with minimal reflections

        public static readonly MaterialModifierDictionary Modifiers;

        static ModifierCatalog()
        {
            Modifiers = new MaterialModifierDictionary
            {
                // Bright/Dim/Glowing affect emissive (glow) only
                ["Bright"] = new MaterialModifier(1.0f, 2.5f, 1.0f, 1.0f),
                ["Brilliant"] = new MaterialModifier(1.0f, 3.5f, 1.0f, 1.0f),
                ["Dim"] = new MaterialModifier(1.0f, 0.1f, 1.0f, 1.0f),
                ["Glowing"] = new MaterialModifier(1.0f, 3.0f, 1.0f, 1.0f),

                // Shiny/Dull affect specular (reflectivity)
                ["Shiny"] = new MaterialModifier(1.0f, 1.0f, 1.5f, 2.0f, 80f),
                ["Dull"] = new MaterialModifier(1.0f, 1.0f, 0.6f, 0.5f, 10f),
                ["Matte"] = new MaterialModifier(1.0f, 1.0f, 0.5f, 0.5f, 5f),
                ["Reflective"] = new MaterialModifier(1.0f, 1.0f, 1.8f, 2.5f, 200f),
            };
        }

        public static bool TryGet(string key, out MaterialModifier modifier)
            => Modifiers.TryGetValue(key, out modifier!);
    }

    public static class MaterialCatalog
    {
        // Component libraries
        public static readonly ColorDictionary DiffuseColors = default!;
        public static readonly ColorDictionary AmbientColors = default!;
        public static readonly ColorDictionary SpecularColors = default!;
        public static readonly ColorDictionary EmissiveColors = default!;
        public static readonly FloatDictionary Shininess = default!;

        // Simple preset cache for convenience
        public static readonly MeshMaterialDictionary Materials = default!;

        static MaterialCatalog()
        {
            // Diffuse color palette (simple RGB entries)
            DiffuseColors = new ColorDictionary
            {
                ["White"] = new Color4(1f, 1f, 1f, 1f),
                ["Gray"] = new Color4(0.6f, 0.6f, 0.6f, 1f),
                ["Blue"] = new Color4(0f, 0f, 1f, 1f),
                ["Red"] = new Color4(1f, 0f, 0f, 1f),
                ["Green"] = new Color4(0f, 1f, 0f, 1f),
                ["Chrome"] = new Color4(0.9f, 0.9f, 0.95f, 1f),
            };

            // Ambient entries; if not found we'll derive from diffuse (scaled)
            AmbientColors = new ColorDictionary
            {
                ["Default"] = new Color4(0.5f, 0.5f, 0.5f, 1f)
            };

            // Specular colors (usually white)
            SpecularColors = new ColorDictionary
            {
                ["White"] = new Color4(1f, 1f, 1f, 1f),
                ["DimWhite"] = new Color4(0.8f, 0.8f, 0.8f, 1f)
            };

            // Emissive (glow) entries
            EmissiveColors = new ColorDictionary
            {
                ["None"] = new Color4(0f, 0f, 0f, 1f),
            };

            // Shininess presets
            Shininess = new FloatDictionary
            {
                ["Dull"] = 10f,
                ["Default"] = 30f,
                ["Shiny"] = 80f,
                ["Reflective"] = 200f
            };

            // Initialize Materials dictionary
            Materials = new MeshMaterialDictionary();

            Materials["Dull_White"] = Compose("Dull_White");
            Materials["Dull_Red"] = Compose("Dull_Red");
            Materials["Shiny_Blue_Emissive"] = Compose("Shiny_Blue_Emissive");
            Materials["Shiny_Red_Emissive_Red"] = Compose("Shiny_Red_Emissive_Red");
        }

        // Compose material from a token string like "Shiny_Red_Emissive" or "Bright_Pink"
        public static MeshMaterial Compose(string materialDescription)
        {
            if (string.IsNullOrWhiteSpace(materialDescription))
                materialDescription = "Default";

            // Check cache first
            if (Materials != null && Materials.TryGetValue(materialDescription, out var cached))
                return cached;

            var tokens = materialDescription.Split(['_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();

            // Defaults
            Color4 diffuse = new Color4(1f, 1f, 1f, 1f);
            Color4 ambient = AmbientColors.TryGetValue("Default", out var aval) ? aval : new Color4(0.5f, 0.5f, 0.5f, 1f);
            Color4 specular = SpecularColors.TryGetValue("White", out var sval) ? sval : new Color4(1f, 1f, 1f, 1f);
            Color4 emissive = EmissiveColors.TryGetValue("None", out var eval) ? eval : new Color4(0f, 0f, 0f, 1f);
            float shininess = Shininess.TryGetValue("Default", out var sh) ? sh : 30f;

            // Modifiers
            float diffuseMul = 1f;
            float emissiveMul = 1f;
            float specularMul = 1f;
            float shininessMul = 1f;

            // Process all tokens in order
            foreach (var t in tokens)
            {
                // Ambient explicit
                if (AmbientColors.TryGetValue(t, out var avalExplicit))
                {
                    ambient = avalExplicit;
                    continue;
                }

                // Emissive explicit
                if (EmissiveColors.TryGetValue(t, out var emissiveExplicit))
                {
                    emissive = emissiveExplicit;
                    continue;
                }

                // Specular profile (prioritize before other lookups)
                if (SpecularCatalog.TryGet(t, out var profile))
                {
                    specular = profile.SpecularColor;
                    if (profile.ShininessOverride.HasValue)
                        shininess = profile.ShininessOverride.Value;
                    continue;
                }

                // Specular explicit (color map)
                if (SpecularColors.TryGetValue(t, out var specExplicit))
                {
                    specular = specExplicit;
                    continue;
                }

                // Shininess token
                if (Shininess.TryGetValue(t, out var s))
                {
                    shininess = s;
                    continue;
                }

                // Modifier tokens
                if (ModifierCatalog.TryGet(t, out var mod))
                {
                    diffuseMul *= mod.DiffuseMul;
                    emissiveMul *= mod.EmissiveMul;
                    specularMul *= mod.SpecularMul;
                    shininessMul *= mod.ShininessMul;
                    if (mod.ShininessOverride.HasValue)
                        shininess = mod.ShininessOverride.Value;
                    continue;
                }

                // Diffuse explicit
                if (DiffuseColors.TryGetValue(t, out var col))
                {
                    diffuse = col;
                    continue;
                }

                // Try WPF named colors for diffuse (e.g. Pink, LimeGreen)
                if (TryLookupWpfColor(t, out var wpfCol))
                {
                    diffuse = wpfCol;
                    continue;
                }

                // Unknown token - ignore
            }

            // DEBUG: Let's see what we got before applying multipliers
            System.Diagnostics.Debug.WriteLine($"Material '{materialDescription}': diffuse={diffuse.Red},{diffuse.Green},{diffuse.Blue} muls={diffuseMul},{emissiveMul},{specularMul}");

            // Derive emissive base if needed (when modifiers want to boost it but it's zero)
            bool emissiveIsZero = emissive.Red == 0f && emissive.Green == 0f && emissive.Blue == 0f;
            if (emissiveIsZero && emissiveMul > 1f)
            {
                emissive = new Color4(
                    diffuse.Red * 0.25f,
                    diffuse.Green * 0.25f,
                    diffuse.Blue * 0.25f,
                    1f);
            }

            // Apply multipliers and clamp to [0,1]
            var finalDiffuse = new Color4(
                Math.Clamp(diffuse.Red * diffuseMul, 0f, 1f),
                Math.Clamp(diffuse.Green * diffuseMul, 0f, 1f),
                Math.Clamp(diffuse.Blue * diffuseMul, 0f, 1f),
                diffuse.Alpha);

            // Derive ambient from final diffuse if it wasn't explicitly set
            // Ambient should match the hue of the diffuse color
            var defaultAmbient = new Color4(0.5f, 0.5f, 0.5f, 1f);
            if (ambient.Red == defaultAmbient.Red && ambient.Green == defaultAmbient.Green && ambient.Blue == defaultAmbient.Blue)
            {
                // Ambient wasn't explicitly set, so derive it from diffuse
                ambient = new Color4(finalDiffuse.Red, finalDiffuse.Green, finalDiffuse.Blue, 1f);
            }

            var finalEmissive = new Color4(
                Math.Clamp(emissive.Red * emissiveMul, 0f, 1f),
                Math.Clamp(emissive.Green * emissiveMul, 0f, 1f),
                Math.Clamp(emissive.Blue * emissiveMul, 0f, 1f),
                emissive.Alpha);

            var finalSpecular = new Color4(
                Math.Clamp(specular.Red * specularMul, 0f, 1f),
                Math.Clamp(specular.Green * specularMul, 0f, 1f),
                Math.Clamp(specular.Blue * specularMul, 0f, 1f),
                specular.Alpha);

            var finalShininess = MathF.Max(1f, shininess * shininessMul);

            // DEBUG: Let's see what we got after applying multipliers
            System.Diagnostics.Debug.WriteLine($"  -> final diffuse={finalDiffuse.Red},{finalDiffuse.Green},{finalDiffuse.Blue}");

            var result = new MeshMaterial
            {
                Name = materialDescription,
                AmbientColor = ambient,
                DiffuseColor = finalDiffuse,
                SpecularColor = finalSpecular,
                EmissiveColor = finalEmissive,
                SpecularShininess = finalShininess
            };

            // DEBUG: Complete material summary
            System.Diagnostics.Debug.WriteLine($"  -> FULL MATERIAL:");
            System.Diagnostics.Debug.WriteLine($"     Ambient:   R={result.AmbientColor.Red:F3} G={result.AmbientColor.Green:F3} B={result.AmbientColor.Blue:F3}");
            System.Diagnostics.Debug.WriteLine($"     Diffuse:   R={result.DiffuseColor.Red:F3} G={result.DiffuseColor.Green:F3} B={result.DiffuseColor.Blue:F3}");
            System.Diagnostics.Debug.WriteLine($"     Specular:  R={result.SpecularColor.Red:F3} G={result.SpecularColor.Green:F3} B={result.SpecularColor.Blue:F3}");
            System.Diagnostics.Debug.WriteLine($"     Emissive:  R={result.EmissiveColor.Red:F3} G={result.EmissiveColor.Green:F3} B={result.EmissiveColor.Blue:F3}");
            System.Diagnostics.Debug.WriteLine($"     Shininess: {result.SpecularShininess:F1}");

            // Cache the composed material
            if (Materials != null)
                Materials[materialDescription] = result;

            return result;
        }

        private static bool TryLookupWpfColor(string name, out Color4 c)
        {
            c = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var key = name.Trim();

            // Check if we already cached it
            if (DiffuseColors.TryGetValue(key, out c))
                return true;

            // Use reflection to find matching property on System.Windows.Media.Colors
            var prop = typeof(Colors).GetProperty(key, BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop == null)
                return false;

            var val = prop.GetValue(null);
            if (val is System.Windows.Media.Color wc)
            {
                c = new Color4(wc.R / 255f, wc.G / 255f, wc.B / 255f, wc.A / 255f);
                // Cache for future lookups
                DiffuseColors[key] = c;
                return true;
            }

            return false;
        }

        public static MeshMaterial GetOrCompose(string key)
            => Materials.TryGetValue(key, out var m) ? m : Compose(key);
    }

    public class ColorDictionary : Dictionary<string, Color4>
    {
        public ColorDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class FloatDictionary : Dictionary<string, float>
    {
        public FloatDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class MeshMaterialDictionary : Dictionary<string, MeshMaterial>
    {
        public MeshMaterialDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class MaterialModifierDictionary : Dictionary<string, MaterialModifier>
    {
        public MaterialModifierDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class SpecularProfileDictionary : Dictionary<string, SpecularProfile>
    {
        public SpecularProfileDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }
}