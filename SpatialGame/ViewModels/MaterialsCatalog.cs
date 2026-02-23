using System.Collections.Concurrent;
using System.Numerics;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.Reflection;
using System.Windows.Media;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
using System.Drawing;
using HelixToolkit.Wpf.SharpDX;

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
                ["Reflective"] = new MaterialModifier(1.0f, 1.0f, 1.8f, 2.5f, 200f),
                ["Shiny"] = new MaterialModifier(1.0f, 1.0f, 1.5f, 2.0f, 80f),
                ["Dull"] = new MaterialModifier(1.0f, 1.0f, 0.6f, 0.5f, 10f),
                ["Matte"] = new MaterialModifier(1.0f, 1.0f, 0.5f, 0.5f, 5f),

                // Stellar: for sun/star-like materials (very strong emissive and specular)
                ["Stellar"] = new MaterialModifier(1.0f, 6.0f, 2.0f, 6.0f, 500f),

                // Chrome: high specular and shininess, specular color white
                ["Chrome"] = new MaterialModifier(1.0f, 1.0f, 2.0f, 3.0f, 200f)
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
            DiffuseColors = new ColorDictionary { ["Default"] = Color4.White };
            AmbientColors = new ColorDictionary { ["Default"] = Color4.White };
            SpecularColors = new ColorDictionary { ["Default"] = Color4.White };
            EmissiveColors = new ColorDictionary { ["Default"] = Color4.Black };
            Shininess = new FloatDictionary
            {
                ["Dull"] = 10f,
                ["Default"] = 30f,
                ["Shiny"] = 80f,
                ["VeryShiny"] = 100f,
                ["Mirror"] = 200f
            };
            Materials = new MeshMaterialDictionary
            {
                ["Default"] = Compose("Default")
            };
        }

        // Helper: Split on separators and CamelCase
        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var buffer = new List<char>();
            void Flush()
            {
                if (buffer.Count > 0)
                {
                    tokens.Add(new string(buffer.ToArray()));
                    buffer.Clear();
                }
            }
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '_' || c == '-' || c == ' ')
                {
                    Flush();
                }
                else if (i > 0 && char.IsUpper(c) && (char.IsLower(input[i - 1]) || (i + 1 < input.Length && char.IsLower(input[i + 1]))))
                {
                    Flush();
                    buffer.Add(c);
                }
                else
                {
                    buffer.Add(c);
                }
            }
            Flush();
            return tokens.Where(t => t.Length > 0).ToList();
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            if (token.Length == 1) return char.ToUpper(token[0]).ToString();
            return char.ToUpper(token[0]) + token.Substring(1).ToLower();
        }

        private static string JoinPascalCase(IEnumerable<string> toks) => string.Concat(toks.Select(NormalizeToken));

        public static PhongMaterial Compose_(string materialDescription)
        {
            // For testing: always return the Indigo material from the HelixToolkit library
            // and mutate it directly as the demo does.
            var mat = PhongMaterials.Indigo;
            // Optionally mutate properties here if needed for further testing
            // e.g. mat.RenderShadowMap = true;
            mat.RenderShadowMap = true; // Ensure shadows are enabled for testing
            return mat;
        }
        public static PhongMaterial Compose(string materialDescription)
        {
            if (string.IsNullOrWhiteSpace(materialDescription)) materialDescription = "Default";

            var rawTokens = Tokenize(materialDescription);
            var tokens = rawTokens.Select(NormalizeToken).ToList();
            var normalizedKey = JoinPascalCase(tokens);

            // Check cache using normalized key
            if (Materials != null && Materials.TryGetValue(normalizedKey, out var cached))
                return cached;

            // Greedy color match helper
            bool TryGreedyColor(List<string> toks, int start, out int length, out Color4 color)
            {
                for (int len = toks.Count - start; len > 0; len--)
                {
                    var candidate = JoinPascalCase(toks.Skip(start).Take(len));
                    if (TryLookupWpfColor(candidate, out color))
                    {
                        length = len;
                        return true;
                    }
                }
                length = 0;
                color = default;
                return false;
            }

            // Parse for Glows/Emissive color
            Color4? emissiveOverride = null;
            bool chromePresent = false;
            int i = 0;
            while (i < tokens.Count)
            {
                if (tokens[i].Equals("Glows", StringComparison.OrdinalIgnoreCase) || tokens[i].Equals("Emissive", StringComparison.OrdinalIgnoreCase))
                {
                    int len;
                    Color4 col;
                    if (TryGreedyColor(tokens, i + 1, out len, out col))
                    {
                        emissiveOverride = col;
                        tokens.RemoveRange(i, len + 1); // Remove Glows/Emissive + color tokens
                        continue;
                    }
                }
                if (tokens[i].Equals("Chrome", StringComparison.OrdinalIgnoreCase))
                    chromePresent = true;
                i++;
            }

            // Greedy color match for diffuse
            Color4 diffuse = DiffuseColors["Default"];
            int colorStart = -1, colorLen = 0;
            for (int start = 0; start < tokens.Count; start++)
            {
                int len;
                Color4 col;
                if (TryGreedyColor(tokens, start, out len, out col))
                {
                    if (len > colorLen)
                    {
                        colorStart = start;
                        colorLen = len;
                        diffuse = col;
                    }
                }
            }
            // Remove color tokens
            if (colorStart >= 0 && colorLen > 0)
                tokens.RemoveRange(colorStart, colorLen);

            // Ambient is a simple transform of diffuse (library uses ~0.8)
            Color4 ambient = new Color4(0.349f, 0.349f, 0.349f, 1.0f);
            // Specular: use a low gray value unless overridden
            Color4 specular = chromePresent ? new Color4(1f, 1f, 1f, 1f) : new Color4(0.161f, 0.161f, 0.161f, 1.0f);
            // Emissive: default to black unless overridden
            Color4 emissive = emissiveOverride ?? new Color4(0f, 0f, 0f, 1.0f);
            // Shininess: use a lower value unless overridden
            float shininess = chromePresent ? 200f : 12.8f;

            var result = new PhongMaterial
            {
                Name = materialDescription,
                AmbientColor = ambient,
                DiffuseColor = diffuse,
                SpecularColor = specular,
                EmissiveColor = emissive,
                SpecularShininess = shininess,
                RenderShadowMap = true
            };

            if (Materials != null)
                Materials[normalizedKey] = result;

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

        public static PhongMaterial GetOrCompose(string key)
        {
            // Always normalize the key for lookup
            var rawTokens = Tokenize(key);
            var tokens = rawTokens.Select(NormalizeToken).ToList();
            var normalizedKey = JoinPascalCase(tokens);
            return Materials.TryGetValue(normalizedKey, out var m) ? m : Compose(key);
        }
    }

    public class ColorDictionary : ConcurrentDictionary<string, Color4>
    {
        public ColorDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class FloatDictionary : ConcurrentDictionary<string, float>
    {
        public FloatDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class MeshMaterialDictionary : ConcurrentDictionary<string, PhongMaterial>
    {
        public MeshMaterialDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class MaterialModifierDictionary : ConcurrentDictionary<string, MaterialModifier>
    {
        public MaterialModifierDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class SpecularProfileDictionary : ConcurrentDictionary<string, SpecularProfile>
    {
        public SpecularProfileDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }
}