using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using HelixToolkit;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;
using SparseLattice.Gguf;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

/// <summary>
/// Main view model for the Neural Navigator 3D embedding explorer.
/// Loads a GGUF model, projects token embeddings to 3D via PCA, and renders
/// them as a navigable point cloud in the HelixToolkit viewport.
/// </summary>
sealed class MainViewModel : ViewModelBase, IDisposable
{
    Viewport3DX? m_viewport;
    FlyCamera? m_flyCamera;
    GgufReader? m_reader;
    float[]? m_embeddings;
    int m_dims;
    int m_vocabSize;
    string[]? m_tokenLabels;
    Vector3[]? m_projected;
    MeshGeometryModel3D? m_pointCloudModel;
    MeshGeometryModel3D? m_highlightModel;

    string m_statusText = "No model loaded. Click 'Load Model...' to begin.";
    string m_searchText = "";
    string m_selectedTokenText = "";
    string m_selectedTokenId = "";
    string m_hoverText = "";
    bool m_hasSelection;
    double m_pointSize = 2.0;
    double m_visibleTokenCount = 10000;
    string m_selectedProjection = "PCA (dims 0,1,2)";
    string m_selectedColorMode = "Token ID";

    public IEffectsManager EffectsManager { get; }
    public HelixToolkit.Wpf.SharpDX.Camera Camera { get; }

    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public string SearchText { get => m_searchText; set => SetField(ref m_searchText, value); }
    public string SelectedTokenText { get => m_selectedTokenText; set => SetField(ref m_selectedTokenText, value); }
    public string SelectedTokenId { get => m_selectedTokenId; set => SetField(ref m_selectedTokenId, value); }
    public string HoverText { get => m_hoverText; set => SetField(ref m_hoverText, value); }
    public bool HasSelection { get => m_hasSelection; set => SetField(ref m_hasSelection, value); }
    public double PointSize { get => m_pointSize; set { if (SetField(ref m_pointSize, value)) RebuildPointCloud(); } }
    public double VisibleTokenCount { get => m_visibleTokenCount; set { if (SetField(ref m_visibleTokenCount, value)) RebuildPointCloud(); } }

    public string SelectedProjection
    {
        get => m_selectedProjection;
        set { if (SetField(ref m_selectedProjection, value)) ReprojectAndRebuild(); }
    }

    public string SelectedColorMode
    {
        get => m_selectedColorMode;
        set { if (SetField(ref m_selectedColorMode, value)) RebuildPointCloud(); }
    }

    public ObservableCollection<NeighborInfo> Neighbors { get; } = [];
    public string[] ProjectionModes { get; } =
    [
        "PCA (dims 0,1,2)",
        "PCA (dims 0,1,3)",
        "PCA (dims 0,2,3)",
        "Raw (dims 0,1,2)",
        "Raw (dims 1,2,3)",
    ];
    public string[] ColorModes { get; } = ["Token ID", "Magnitude", "Cluster"];

    public ICommand LoadModelCommand { get; }
    public ICommand SearchCommand { get; }

    public MainViewModel(Viewport3DX viewport)
    {
        m_viewport = viewport;
        EffectsManager = new DefaultEffectsManager();

        PerspectiveCamera camera = new()
        {
            Position = new Point3D(0, 0, 200),
            LookDirection = new Vector3D(0, 0, -200),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 60,
            FarPlaneDistance = 10000,
            NearPlaneDistance = 0.1,
        };
        Camera = camera;
        m_flyCamera = new FlyCamera(viewport, camera);

        LoadModelCommand = new RelayCommand(_ => LoadModel());
        SearchCommand = new RelayCommand(_ => SearchToken(), _ => m_tokenLabels is not null);
    }

    // ------------------------------------------------------------------
    // Model loading
    // ------------------------------------------------------------------

    void LoadModel()
    {
        OpenFileDialog dlg = new()
        {
            Title = "Select a GGUF model file",
            Filter = "GGUF files|*.gguf;sha256-*|All files|*.*",
            InitialDirectory = @"D:\AI\OllamaModels\blobs"
        };

        // Also allow picking from TestData
        string? testData = FindTestDataDir();
        if (testData is not null && !Directory.Exists(dlg.InitialDirectory))
            dlg.InitialDirectory = testData;

        if (dlg.ShowDialog() != true) return;

        StatusText = "Loading model...";

        Task.Run(() =>
        {
            try
            {
                m_reader?.Dispose();
                m_reader = GgufReader.Open(dlg.FileName);

                m_dims = m_reader.EmbeddingLength;
                m_vocabSize = m_reader.Tokens.Count;

                // Read token embeddings
                m_embeddings = m_reader.ReadTensorF32("token_embd.weight");

                // Build token labels
                m_tokenLabels = new string[m_vocabSize];
                for (int i = 0; i < m_vocabSize; i++)
                    m_tokenLabels[i] = m_reader.Tokens[i];

                // Project to 3D
                m_projected = ProjectEmbeddings(m_embeddings, m_vocabSize, m_dims);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"Loaded: {m_reader.ModelName}\n" +
                                 $"Arch: {m_reader.Architecture}\n" +
                                 $"Vocab: {m_vocabSize:N0} tokens\n" +
                                 $"Dims: {m_dims}\n" +
                                 $"Layers: {m_reader.LayerCount}";
                    RebuildPointCloud();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"Error: {ex.Message}");
            }
        });
    }

    // ------------------------------------------------------------------
    // Projection: high-dim embeddings → 3D
    // ------------------------------------------------------------------

    Vector3[] ProjectEmbeddings(float[] embeddings, int vocabSize, int dims)
    {
        // Parse the selected projection mode to get 3 dimension indices
        int d0, d1, d2;
        if (m_selectedProjection.StartsWith("PCA"))
        {
            // Simple PCA: compute mean, subtract, pick top-3 variance dimensions
            (d0, d1, d2) = FindTopVarianceDims(embeddings, vocabSize, dims);
        }
        else
        {
            // Raw dimension extraction
            (d0, d1, d2) = m_selectedProjection switch
            {
                "Raw (dims 1,2,3)" => (1, 2, 3),
                _ => (0, 1, 2),
            };
        }

        Vector3[] result = new Vector3[vocabSize];
        const float Scale = 100f; // Scale to make the viewport navigable

        // Find range for normalization
        float min0 = float.MaxValue, max0 = float.MinValue;
        float min1 = float.MaxValue, max1 = float.MinValue;
        float min2 = float.MaxValue, max2 = float.MinValue;

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            if (d0 < dims) { float v = embeddings[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
            if (d1 < dims) { float v = embeddings[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
            if (d2 < dims) { float v = embeddings[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
        }

        float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
        float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
        float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            float x = d0 < dims ? (embeddings[bi + d0] - min0) / range0 * Scale - Scale / 2f : 0f;
            float y = d1 < dims ? (embeddings[bi + d1] - min1) / range1 * Scale - Scale / 2f : 0f;
            float z = d2 < dims ? (embeddings[bi + d2] - min2) / range2 * Scale - Scale / 2f : 0f;
            result[i] = new Vector3(x, y, z);
        }

        return result;
    }

    /// <summary>
    /// Finds the 3 dimensions with highest variance across all token embeddings.
    /// This is a fast approximation to PCA — picks the 3 axes with most spread.
    /// </summary>
    static (int, int, int) FindTopVarianceDims(float[] embeddings, int vocabSize, int dims)
    {
        double[] mean = new double[dims];
        double[] variance = new double[dims];

        // Compute mean per dimension
        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            for (int d = 0; d < dims; d++)
                mean[d] += embeddings[bi + d];
        }
        for (int d = 0; d < dims; d++) mean[d] /= vocabSize;

        // Compute variance per dimension
        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            for (int d = 0; d < dims; d++)
            {
                double diff = embeddings[bi + d] - mean[d];
                variance[d] += diff * diff;
            }
        }

        // Find top-3 by variance
        int[] indices = Enumerable.Range(0, dims).ToArray();
        Array.Sort(variance, indices);
        // Sort is ascending — take last 3
        return (indices[dims - 1], indices[dims - 2], indices[dims - 3]);
    }

    void ReprojectAndRebuild()
    {
        if (m_embeddings is null || m_vocabSize == 0) return;
        m_projected = ProjectEmbeddings(m_embeddings, m_vocabSize, m_dims);
        RebuildPointCloud();
    }

    // ------------------------------------------------------------------
    // Point cloud rendering
    // ------------------------------------------------------------------

    void RebuildPointCloud()
    {
        if (m_viewport is null || m_projected is null || m_tokenLabels is null) return;

        // Remove old models
        if (m_pointCloudModel is not null)
        {
            m_viewport.Items.Remove(m_pointCloudModel);
            m_pointCloudModel.Dispose();
        }
        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        int count = Math.Min((int)m_visibleTokenCount, m_projected.Length);
        float sphereRadius = (float)m_pointSize * 0.1f;

        MeshBuilder builder = new();
        List<Color4> colors = [];

        for (int i = 0; i < count; i++)
        {
            builder.AddSphere(m_projected[i], sphereRadius, 4, 4);
            colors.Add(GetTokenColor(i));
        }

        MeshGeometry3D mesh = builder.ToMeshGeometry3D();

        // Pad colors to match actual vertex count (spheres produce varying vertex counts)
        while (colors.Count < mesh.Positions.Count)
        {
            int tokenIdx = Math.Min(colors.Count * count / Math.Max(mesh.Positions.Count, 1), count - 1);
            colors.Add(GetTokenColor(tokenIdx));
        }

        mesh.Colors = new Color4Collection(colors);

        m_pointCloudModel = new MeshGeometryModel3D
        {
            Geometry = mesh,
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1, 1, 1, 1),
                EnableFlatShading = true,
            },
            IsHitTestVisible = true,
        };

        m_viewport.Items.Add(m_pointCloudModel);
    }

    Color4 GetTokenColor(int tokenIdx)
    {
        return m_selectedColorMode switch
        {
            "Magnitude" => GetMagnitudeColor(tokenIdx),
            "Cluster" => GetClusterColor(tokenIdx),
            _ => GetTokenIdColor(tokenIdx),
        };
    }

    Color4 GetTokenIdColor(int tokenIdx)
    {
        // HSV-based coloring: spread token IDs across the hue wheel
        float hue = (tokenIdx * 137.508f) % 360f; // golden angle spacing
        return HsvToColor4(hue, 0.7f, 0.9f);
    }

    Color4 GetMagnitudeColor(int tokenIdx)
    {
        if (m_embeddings is null) return new Color4(0.5f, 0.5f, 0.5f, 1f);
        int bi = tokenIdx * m_dims;
        float mag = 0f;
        for (int d = 0; d < m_dims; d++)
            mag += m_embeddings[bi + d] * m_embeddings[bi + d];
        mag = MathF.Sqrt(mag);

        // Normalize to [0,1] range using a rough heuristic
        float t = MathF.Min(mag / 10f, 1f);
        return new Color4(t, 0.3f, 1f - t, 1f);
    }

    Color4 GetClusterColor(int tokenIdx)
    {
        // Simple clustering by first character of token
        if (m_tokenLabels is null) return new Color4(0.5f, 0.5f, 0.5f, 1f);
        string token = m_tokenLabels[tokenIdx];
        if (token.Length == 0) return new Color4(0.3f, 0.3f, 0.3f, 1f);

        char c = char.ToLower(token[0]);
        float hue = c switch
        {
            >= 'a' and <= 'z' => (c - 'a') * (360f / 26f),
            >= '0' and <= '9' => 60f, // yellow for digits
            _ => 0f, // red for symbols/special
        };
        return HsvToColor4(hue, 0.8f, 0.9f);
    }

    static Color4 HsvToColor4(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new Color4(r + m, g + m, b + m, 1f);
    }

    // ------------------------------------------------------------------
    // Search and selection
    // ------------------------------------------------------------------

    void SearchToken()
    {
        if (m_tokenLabels is null || m_projected is null || string.IsNullOrWhiteSpace(SearchText)) return;

        string query = SearchText.Trim();
        int foundIdx = -1;

        // Exact match first
        for (int i = 0; i < m_tokenLabels.Length; i++)
        {
            if (m_tokenLabels[i].Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                foundIdx = i;
                break;
            }
        }

        // Substring match
        if (foundIdx < 0)
        {
            for (int i = 0; i < m_tokenLabels.Length; i++)
            {
                if (m_tokenLabels[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    foundIdx = i;
                    break;
                }
            }
        }

        if (foundIdx < 0)
        {
            StatusText += $"\nNot found: \"{query}\"";
            return;
        }

        SelectToken(foundIdx);

        // Fly camera to the selected token
        if (Camera is PerspectiveCamera cam)
        {
            Vector3 pos = m_projected[foundIdx];
            cam.Position = new Point3D(pos.X, pos.Y, pos.Z + 30);
            cam.LookDirection = new Vector3D(0, 0, -30);
        }
    }

    void SelectToken(int tokenIdx)
    {
        if (m_tokenLabels is null || m_embeddings is null || m_projected is null) return;

        SelectedTokenText = $"\"{m_tokenLabels[tokenIdx]}\"";
        SelectedTokenId = $"ID: {tokenIdx}";
        HasSelection = true;

        // Find K nearest neighbors by L2 distance in the full embedding space
        FindNeighbors(tokenIdx, 16);

        // Highlight the selected token and neighbors in the viewport
        HighlightTokens(tokenIdx);
    }

    void FindNeighbors(int tokenIdx, int k)
    {
        if (m_embeddings is null || m_tokenLabels is null) return;

        int baseA = tokenIdx * m_dims;
        (int idx, float dist)[] distances = new (int, float)[m_vocabSize];

        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == tokenIdx) { distances[i] = (i, float.MaxValue); continue; }
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            distances[i] = (i, MathF.Sqrt(sum));
        }

        Array.Sort(distances, (a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        for (int i = 0; i < Math.Min(k, distances.Length); i++)
        {
            (int idx, float dist) = distances[i];
            if (dist >= float.MaxValue) break;
            Neighbors.Add(new NeighborInfo(m_tokenLabels[idx], idx, dist));
        }
    }

    void HighlightTokens(int centerIdx)
    {
        if (m_viewport is null || m_projected is null) return;

        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        MeshBuilder builder = new();
        float radius = (float)m_pointSize * 0.3f;

        // Center token in bright yellow
        builder.AddSphere(m_projected[centerIdx], radius, 8, 8);

        // Neighbors in cyan
        foreach (NeighborInfo n in Neighbors)
        {
            if (n.TokenId < m_projected.Length)
                builder.AddSphere(m_projected[n.TokenId], radius * 0.7f, 6, 6);
        }

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1f, 1f, 0f, 1f),
                EmissiveColor = new Color4(0.3f, 0.3f, 0f, 0f),
            },
        };

        m_viewport.Items.Add(m_highlightModel);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static string? FindTestDataDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public void Dispose()
    {
        m_flyCamera?.Dispose();
        m_pointCloudModel?.Dispose();
        m_highlightModel?.Dispose();
        m_reader?.Dispose();
        (EffectsManager as IDisposable)?.Dispose();
    }
}
