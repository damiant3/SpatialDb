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
    string? m_ggufPath;
    float[]? m_embeddings;
    int m_dims;
    int m_vocabSize;
    string[]? m_tokenLabels;
    Vector3[]? m_projected;
    MeshGeometryModel3D? m_pointCloudModel;
    MeshGeometryModel3D? m_highlightModel;
    MeshGeometryModel3D? m_vectorLineModel;
    MeshGeometryModel3D? m_traceModel;
    MeshGeometryModel3D? m_weightModel;
    int m_selectedTokenIdx = -1;
    int m_compareTokenIdx = -1;

    // Phase 4: layer trace
    IntegerCausalSource? m_causalSource;
    float[][]? m_traceStates;       // [layer+1][dims] — hidden states per layer
    Vector3[]? m_traceProjected;    // projected 3D positions for trace
    string m_tracePrompt = "The capital of France is";
    string m_traceStatus = "";
    bool m_hasTrace;
    int m_selectedLayerIndex;
    int m_maxLayerIndex;

    // Phase 6: weight explorer
    string m_selectedWeightTensor = "";
    string m_weightStatus = "";
    ObservableCollection<string> m_weightTensorNames = [];

    // Viewport options
    bool m_showModelInfo = true;
    bool m_showCoordinateSystem = true;
    bool m_showViewCube;
    bool m_showCameraInfo;
    bool m_showFrameRate;
    bool m_enableFxaa = true;
    bool m_enableShadows;

    string m_statusText = "No model loaded.";
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
    public string HoverText
    {
        get => m_hoverText;
        set { SetField(ref m_hoverText, value); OnPropertyChanged(nameof(HasHoverText)); }
    }
    public bool HasHoverText => m_hoverText.Length > 0;
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
    public ObservableCollection<LayerMovementInfo> LayerMovements { get; } = [];
    public string[] ProjectionModes { get; } =
    [
        "PCA (dims 0,1,2)",
        "PCA (dims 0,1,3)",
        "PCA (dims 0,2,3)",
        "Raw (dims 0,1,2)",
        "Raw (dims 1,2,3)",
    ];
    public string[] ColorModes { get; } = ["Token ID", "Magnitude", "Cluster"];

    // Phase 4 properties
    public string TracePrompt { get => m_tracePrompt; set => SetField(ref m_tracePrompt, value); }
    public string TraceStatus { get => m_traceStatus; set => SetField(ref m_traceStatus, value); }
    public bool HasTrace { get => m_hasTrace; set => SetField(ref m_hasTrace, value); }
    public int SelectedLayerIndex
    {
        get => m_selectedLayerIndex;
        set { if (SetField(ref m_selectedLayerIndex, value)) OnLayerSelected(); }
    }
    public int MaxLayerIndex { get => m_maxLayerIndex; set => SetField(ref m_maxLayerIndex, value); }

    // Phase 6 properties
    public ObservableCollection<string> WeightTensorNames { get => m_weightTensorNames; set => SetField(ref m_weightTensorNames, value); }
    public string SelectedWeightTensor { get => m_selectedWeightTensor; set => SetField(ref m_selectedWeightTensor, value); }
    public string WeightStatus { get => m_weightStatus; set => SetField(ref m_weightStatus, value); }

    // Viewport options properties
    public bool ShowModelInfo
    {
        get => m_showModelInfo;
        set { if (SetField(ref m_showModelInfo, value)) OnPropertyChanged(nameof(ShowModelInfoVisibility)); }
    }
    public Visibility ShowModelInfoVisibility => m_showModelInfo ? Visibility.Visible : Visibility.Collapsed;
    public bool ShowCoordinateSystem { get => m_showCoordinateSystem; set => SetField(ref m_showCoordinateSystem, value); }
    public bool ShowViewCube { get => m_showViewCube; set => SetField(ref m_showViewCube, value); }
    public bool ShowCameraInfo { get => m_showCameraInfo; set => SetField(ref m_showCameraInfo, value); }
    public bool ShowFrameRate { get => m_showFrameRate; set => SetField(ref m_showFrameRate, value); }
    public bool EnableFxaa
    {
        get => m_enableFxaa;
        set { if (SetField(ref m_enableFxaa, value)) OnPropertyChanged(nameof(FxaaLevel)); }
    }
    public bool EnableShadows { get => m_enableShadows; set => SetField(ref m_enableShadows, value); }
    public HelixToolkit.SharpDX.FXAALevel FxaaLevel => m_enableFxaa ? HelixToolkit.SharpDX.FXAALevel.Low : HelixToolkit.SharpDX.FXAALevel.None;

    public ICommand LoadModelCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ResetViewCommand { get; }
    public ICommand ShowOptionsCommand { get; }
    public ICommand TracePromptCommand { get; }
    public ICommand ClearTraceCommand { get; }
    public ICommand ShowWeightsCommand { get; }
    public ICommand ContextSeeNeighborsCommand { get; }
    public ICommand ContextShowDimsCommand { get; }
    public ICommand ContextFindClusterCommand { get; }
    public ICommand ContextCompareToCommand { get; }

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
        m_flyCamera.HoverMove += OnHoverMove;
        m_flyCamera.DoubleClick += OnDoubleClick;
        m_flyCamera.RightClick += OnRightClick;
        m_flyCamera.ResetView += ResetView;

        LoadModelCommand = new RelayCommand(_ => LoadModel());
        SearchCommand = new RelayCommand(_ => SearchToken(), _ => m_tokenLabels is not null);
        ResetViewCommand = new RelayCommand(_ => ResetView());
        TracePromptCommand = new RelayCommand(_ => RunTrace(), _ => m_reader is not null);
        ClearTraceCommand = new RelayCommand(_ => ClearTrace(), _ => m_hasTrace);
        ShowWeightsCommand = new RelayCommand(_ => ShowWeights(), _ => m_reader is not null && m_selectedWeightTensor.Length > 0);
        ContextSeeNeighborsCommand = new RelayCommand(_ => ContextSeeNeighbors(), _ => m_selectedTokenIdx >= 0);
        ContextShowDimsCommand = new RelayCommand(_ => ContextShowInOtherDims(), _ => m_selectedTokenIdx >= 0);
        ContextFindClusterCommand = new RelayCommand(_ => ContextFindCluster(), _ => m_selectedTokenIdx >= 0);
        ContextCompareToCommand = new RelayCommand(_ => ContextCompareTo(), _ => m_selectedTokenIdx >= 0);
        ShowOptionsCommand = new RelayCommand(_ => ToggleOptions());
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
                m_ggufPath = dlg.FileName;

                m_dims = m_reader.EmbeddingLength;
                m_vocabSize = m_reader.Tokens.Count;

                // Read token embeddings
                m_embeddings = m_reader.ReadTensorF32("token_embd.weight");

                // Clamp vocab size to actual tensor rows — tokenizer metadata
                // can report more tokens than the embedding tensor contains
                int tensorRows = m_embeddings.Length / Math.Max(m_dims, 1);
                if (m_vocabSize > tensorRows)
                    m_vocabSize = tensorRows;

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
                    PopulateWeightTensors();
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
            int tokenIdx = (int)Math.Min((long)colors.Count * count / Math.Max(mesh.Positions.Count, 1), count - 1);
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
            m_flyCamera?.SyncFromCamera();
        }
    }

    void SelectToken(int tokenIdx)
    {
        if (m_tokenLabels is null || m_embeddings is null || m_projected is null) return;

        SelectedTokenText = $"\"{m_tokenLabels[tokenIdx]}\"";
        SelectedTokenId = $"ID: {tokenIdx}";
        HasSelection = true;

        m_selectedTokenIdx = tokenIdx;

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
    // Hover and double-click hit testing
    // ------------------------------------------------------------------

    void OnHoverMove(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx < 0)
        {
            HoverText = "";
            return;
        }
        string label = m_tokenLabels![idx];
        HoverText = $"\"{label}\"  (ID {idx})";
    }

    void OnDoubleClick(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx >= 0)
            SelectToken(idx);
    }

    void OnRightClick(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx >= 0)
        {
            SelectToken(idx);
            ShowContextMenu(screenPos);
        }
    }

    void ShowContextMenu(Point screenPos)
    {
        if (m_viewport is null) return;

        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = null,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252526")!),
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#d4d4d4")!),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3e3e42")!),
        };

        var seeNeighbors = new System.Windows.Controls.MenuItem { Header = "See Neighbors", Command = ContextSeeNeighborsCommand };
        var showDims = new System.Windows.Controls.MenuItem { Header = "Show in Other Dimensions", Command = ContextShowDimsCommand };
        var findCluster = new System.Windows.Controls.MenuItem { Header = "Find Cluster (radius)", Command = ContextFindClusterCommand };
        var compareTo = new System.Windows.Controls.MenuItem { Header = m_compareTokenIdx >= 0 && m_compareTokenIdx != m_selectedTokenIdx
            ? $"Compare to \"{m_tokenLabels?[m_compareTokenIdx] ?? "?"}\"..."
            : "Mark for Compare", Command = ContextCompareToCommand };

        menu.Items.Add(seeNeighbors);
        menu.Items.Add(showDims);
        menu.Items.Add(findCluster);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(compareTo);

        menu.PlacementTarget = m_viewport;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------
    // Phase 3: Context menu actions
    // ------------------------------------------------------------------

    void ContextSeeNeighbors()
    {
        if (m_selectedTokenIdx >= 0)
            SelectToken(m_selectedTokenIdx);
    }

    void ContextShowInOtherDims()
    {
        if (m_embeddings is null || m_selectedTokenIdx < 0) return;

        // Cycle to next projection mode
        int current = Array.IndexOf(ProjectionModes, m_selectedProjection);
        int next = (current + 1) % ProjectionModes.Length;
        SelectedProjection = ProjectionModes[next];

        StatusText += $"\nProjection → {SelectedProjection}";
    }

    void ContextFindCluster()
    {
        if (m_embeddings is null || m_tokenLabels is null || m_selectedTokenIdx < 0 || m_projected is null) return;

        // Find all tokens within a radius in the full embedding space
        // Radius = mean distance of the top-8 neighbors (adaptive)
        float radius = ComputeAdaptiveRadius(m_selectedTokenIdx, 8);

        int baseA = m_selectedTokenIdx * m_dims;
        List<(int idx, float dist)> cluster = [];

        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == m_selectedTokenIdx) continue;
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            float dist = MathF.Sqrt(sum);
            if (dist <= radius)
                cluster.Add((i, dist));
        }

        cluster.Sort((a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        int shown = Math.Min(cluster.Count, 50);
        for (int i = 0; i < shown; i++)
            Neighbors.Add(new NeighborInfo(m_tokenLabels[cluster[i].idx], cluster[i].idx, cluster[i].dist));

        HighlightCluster(m_selectedTokenIdx, cluster.Select(c => c.idx).Take(shown).ToList());

        StatusText += $"\nCluster: {cluster.Count} tokens within radius {radius:F2}";
    }

    float ComputeAdaptiveRadius(int tokenIdx, int k)
    {
        if (m_embeddings is null) return 1f;

        int baseA = tokenIdx * m_dims;
        float[] dists = new float[m_vocabSize];
        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == tokenIdx) { dists[i] = float.MaxValue; continue; }
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            dists[i] = MathF.Sqrt(sum);
        }

        Array.Sort(dists);
        float meanDist = 0f;
        for (int i = 0; i < k && i < dists.Length; i++)
            meanDist += dists[i];
        return (meanDist / k) * 2f; // 2x the mean K-neighbor distance
    }

    void HighlightCluster(int centerIdx, List<int> memberIndices)
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

        // Cluster members in green
        foreach (int idx in memberIndices)
        {
            if (idx < m_projected.Length)
                builder.AddSphere(m_projected[idx], radius * 0.6f, 6, 6);
        }

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.2f, 1f, 0.3f, 1f),
                EmissiveColor = new Color4(0f, 0.3f, 0f, 0f),
            },
        };

        m_viewport.Items.Add(m_highlightModel);
    }

    void ContextCompareTo()
    {
        if (m_embeddings is null || m_tokenLabels is null || m_projected is null || m_selectedTokenIdx < 0) return;

        if (m_compareTokenIdx < 0 || m_compareTokenIdx == m_selectedTokenIdx)
        {
            // Mark current selection as the "compare from" token
            m_compareTokenIdx = m_selectedTokenIdx;
            StatusText += $"\nMarked \"{m_tokenLabels[m_compareTokenIdx]}\" for comparison. Right-click another token → Compare.";
            return;
        }

        // We have two tokens: m_compareTokenIdx (A) and m_selectedTokenIdx (B)
        int idxA = m_compareTokenIdx;
        int idxB = m_selectedTokenIdx;

        // Compute the direction vector A → B in the full embedding space
        int baseA = idxA * m_dims;
        int baseB = idxB * m_dims;
        float[] direction = new float[m_dims];
        for (int d = 0; d < m_dims; d++)
            direction[d] = m_embeddings[baseB + d] - m_embeddings[baseA + d];

        // Find tokens that lie along this direction (dot product with direction / magnitude)
        float dirMag = 0f;
        for (int d = 0; d < m_dims; d++)
            dirMag += direction[d] * direction[d];
        dirMag = MathF.Sqrt(dirMag);
        if (dirMag < 1e-8f) return;

        // Normalize direction
        for (int d = 0; d < m_dims; d++)
            direction[d] /= dirMag;

        // For each token, compute how well it aligns with the A→B direction
        // (project token-A vector onto direction, find tokens near the B endpoint)
        float[] midpoint = new float[m_dims];
        for (int d = 0; d < m_dims; d++)
            midpoint[d] = (m_embeddings[baseA + d] + m_embeddings[baseB + d]) / 2f;

        List<(int idx, float alignment, float dist)> candidates = [];
        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == idxA || i == idxB) continue;
            int bi = i * m_dims;

            // Vector from A to token i
            float dot = 0f;
            float distSq = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[bi + d] - m_embeddings[baseA + d];
                dot += diff * direction[d];
                distSq += diff * diff;
            }
            float dist = MathF.Sqrt(distSq);

            // Cosine alignment with the direction
            float cosine = dist > 1e-8f ? dot / dist : 0f;

            // We want tokens near the B endpoint (projection close to dirMag)
            // and well-aligned with the direction
            if (cosine > 0.5f && MathF.Abs(dot - dirMag) < dirMag * 0.5f)
                candidates.Add((i, cosine, MathF.Abs(dot - dirMag)));
        }

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        Neighbors.Add(new NeighborInfo($"[A] {m_tokenLabels[idxA]}", idxA, 0f));
        Neighbors.Add(new NeighborInfo($"[B] {m_tokenLabels[idxB]}", idxB, dirMag));

        int shown = Math.Min(candidates.Count, 20);
        for (int i = 0; i < shown; i++)
        {
            var c = candidates[i];
            Neighbors.Add(new NeighborInfo(m_tokenLabels[c.idx], c.idx, c.dist));
        }

        // Draw a line from A to B in 3D space
        DrawVectorLine(idxA, idxB);

        // Highlight both tokens and the analogous results
        HighlightComparison(idxA, idxB, candidates.Take(shown).Select(c => c.idx).ToList());

        StatusText += $"\nCompare: \"{m_tokenLabels[idxA]}\" → \"{m_tokenLabels[idxB]}\" " +
                      $"(distance {dirMag:F2}, {candidates.Count} aligned tokens)";

        // Reset compare token for next use
        m_compareTokenIdx = -1;
    }

    void DrawVectorLine(int idxA, int idxB)
    {
        if (m_viewport is null || m_projected is null) return;

        ClearVectorLine();

        Vector3 posA = m_projected[idxA];
        Vector3 posB = m_projected[idxB];
        float tubeRadius = (float)m_pointSize * 0.05f;

        MeshBuilder builder = new();
        builder.AddCylinder(posA, posB, tubeRadius, 6);

        // Arrowhead at B: a larger sphere to indicate direction
        builder.AddSphere(posB, tubeRadius * 4f, 6, 6);

        m_vectorLineModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1f, 0.5f, 0f, 1f),
                EmissiveColor = new Color4(0.3f, 0.1f, 0f, 0f),
            },
        };

        m_viewport.Items.Add(m_vectorLineModel);
    }

    void ClearVectorLine()
    {
        if (m_vectorLineModel is not null && m_viewport is not null)
        {
            m_viewport.Items.Remove(m_vectorLineModel);
            m_vectorLineModel.Dispose();
            m_vectorLineModel = null;
        }
    }

    void HighlightComparison(int idxA, int idxB, List<int> analogous)
    {
        if (m_viewport is null || m_projected is null) return;

        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        MeshBuilder builder = new();
        float radius = (float)m_pointSize * 0.3f;

        // Token A in red
        builder.AddSphere(m_projected[idxA], radius, 8, 8);
        // Token B in blue
        builder.AddSphere(m_projected[idxB], radius, 8, 8);

        // Analogous tokens in purple
        foreach (int idx in analogous)
        {
            if (idx < m_projected.Length)
                builder.AddSphere(m_projected[idx], radius * 0.6f, 6, 6);
        }

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.8f, 0.3f, 1f, 1f),
                EmissiveColor = new Color4(0.2f, 0.05f, 0.3f, 0f),
            },
        };

        m_viewport.Items.Add(m_highlightModel);
    }

    int HitTestTokenAtScreen(Point screenPos)
    {
        if (m_projected is null || m_tokenLabels is null || m_viewport is null)
            return -1;
        if (Camera is not PerspectiveCamera cam)
            return -1;

        Point3D camPos = cam.Position;
        Vector3D lookDir = cam.LookDirection;
        Vector3D upDir = cam.UpDirection;

        double fovRad = cam.FieldOfView * (Math.PI / 180.0);
        double aspect = m_viewport.ActualWidth / Math.Max(m_viewport.ActualHeight, 1);

        Vector3D rightDir = Vector3D.CrossProduct(lookDir, upDir);
        rightDir.Normalize();
        Vector3D trueUp = Vector3D.CrossProduct(rightDir, lookDir);
        trueUp.Normalize();
        Vector3D fwd = lookDir;
        fwd.Normalize();

        double halfH = Math.Tan(fovRad / 2.0);
        double halfW = halfH * aspect;

        double nx = (screenPos.X / m_viewport.ActualWidth) * 2.0 - 1.0;
        double ny = 1.0 - (screenPos.Y / m_viewport.ActualHeight) * 2.0;

        Vector3D rayDir = fwd + rightDir * (nx * halfW) + trueUp * (ny * halfH);
        rayDir.Normalize();

        Vector3 origin = new((float)camPos.X, (float)camPos.Y, (float)camPos.Z);
        Vector3 direction = new((float)rayDir.X, (float)rayDir.Y, (float)rayDir.Z);

        int count = Math.Min((int)m_visibleTokenCount, m_projected.Length);
        float hitRadius = (float)m_pointSize * 0.5f;
        return TokenSpatialIndex.FindNearestToRay(m_projected, count, origin, direction, hitRadius);
    }

    // ------------------------------------------------------------------
    // Phase 4: Layer-by-Layer Trace
    // ------------------------------------------------------------------

    void RunTrace()
    {
        if (m_reader is null || string.IsNullOrWhiteSpace(m_tracePrompt)) return;

        string arch = m_reader.Architecture;
        bool isCausal = arch.StartsWith("gemma", StringComparison.OrdinalIgnoreCase);

        if (!isCausal)
        {
            TraceStatus = $"Architecture \"{arch}\" is not a causal model.\nTracing uses token embedding lookup instead.";
            RunEmbeddingTrace();
            return;
        }

        TraceStatus = "Loading causal model for trace...";

        string ggufPath = m_ggufPath!;
        Task.Run(() =>
        {
            try
            {
                if (m_causalSource is null)
                {
                    m_causalSource = IntegerCausalSource.Load(ggufPath, onProgress: (step, total, name) =>
                    {
                        if (step % 10 == 0)
                            Application.Current.Dispatcher.Invoke(() =>
                                TraceStatus = $"Loading layers... {step}/{total}");
                    });
                }

                Application.Current.Dispatcher.Invoke(() =>
                    TraceStatus = "Running forward pass...");

                int[] tokens = m_causalSource.Tokenizer.Encode(m_tracePrompt, addSpecialTokens: true);
                float[][] trace = m_causalSource.ForwardCausalWithTrace(tokens);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    m_traceStates = trace;
                    ProjectAndRenderTrace();
                    BuildLayerMovements();
                    MaxLayerIndex = trace.Length - 1;
                    SelectedLayerIndex = 0;
                    HasTrace = true;
                    TraceStatus = $"Traced {tokens.Length} tokens through {trace.Length - 1} layers.";
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    TraceStatus = $"Trace error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// For non-causal models (BERT, etc.), trace by tokenizing the prompt and
    /// looking up each token's embedding. Shows how prompt tokens are distributed
    /// in the embedding space — not a true forward pass, but still visual and useful.
    /// </summary>
    void RunEmbeddingTrace()
    {
        if (m_reader is null || m_embeddings is null || m_tokenLabels is null) return;

        // Simple tokenization: find matching tokens by substring
        string prompt = m_tracePrompt.Trim();
        List<int> tokenIds = [];

        // Try to find whole-word or subword matches
        int pos = 0;
        while (pos < prompt.Length)
        {
            int bestLen = 0;
            int bestIdx = -1;
            // Greedy longest match
            for (int i = 0; i < m_vocabSize; i++)
            {
                string tok = m_tokenLabels[i];
                if (tok.Length > 0 && tok.Length > bestLen && pos + tok.Length <= prompt.Length)
                {
                    // Case-insensitive match, handle leading space/underscore
                    string candidate = prompt.Substring(pos, tok.Length);
                    if (candidate.Equals(tok, StringComparison.OrdinalIgnoreCase) ||
                        candidate.Equals(tok.TrimStart('▁', '_', ' '), StringComparison.OrdinalIgnoreCase))
                    {
                        bestLen = tok.Length;
                        bestIdx = i;
                    }
                }
            }

            if (bestIdx >= 0)
            {
                tokenIds.Add(bestIdx);
                pos += bestLen;
            }
            else
            {
                pos++; // skip unmatched character
            }

            if (tokenIds.Count > 50) break; // safety limit
        }

        if (tokenIds.Count == 0)
        {
            TraceStatus = "No tokens matched from prompt.";
            return;
        }

        // Build "trace" from token embeddings — each token's embedding is a "layer"
        float[][] trace = new float[tokenIds.Count][];
        for (int i = 0; i < tokenIds.Count; i++)
        {
            int id = tokenIds[i];
            trace[i] = new float[m_dims];
            int bi = id * m_dims;
            for (int d = 0; d < m_dims; d++)
                trace[i][d] = m_embeddings[bi + d];
        }

        m_traceStates = trace;
        ProjectAndRenderTrace();
        BuildLayerMovements();
        MaxLayerIndex = trace.Length - 1;
        SelectedLayerIndex = 0;
        HasTrace = true;

        string tokenList = string.Join(" → ", tokenIds.Select(id => $"\"{m_tokenLabels[id]}\""));
        TraceStatus = $"Embedding trace: {tokenIds.Count} tokens\n{tokenList}";
    }

    void ProjectAndRenderTrace()
    {
        if (m_traceStates is null || m_projected is null || m_viewport is null) return;

        // Project trace states to 3D using the same projection as tokens
        int dims = m_traceStates[0].Length;
        int n = m_traceStates.Length;
        m_traceProjected = new Vector3[n];

        // We need to project each hidden state into the same 3D space as the tokens.
        // Use the same dimension indices the token projector uses.
        int d0, d1, d2;
        if (m_selectedProjection.StartsWith("PCA") && m_embeddings is not null)
            (d0, d1, d2) = FindTopVarianceDims(m_embeddings, m_vocabSize, m_dims);
        else
            (d0, d1, d2) = m_selectedProjection switch
            {
                "Raw (dims 1,2,3)" => (1, 2, 3),
                _ => (0, 1, 2),
            };

        // Compute projection ranges from the token embeddings for consistent scaling
        float min0 = float.MaxValue, max0 = float.MinValue;
        float min1 = float.MaxValue, max1 = float.MinValue;
        float min2 = float.MaxValue, max2 = float.MinValue;
        if (m_embeddings is not null)
        {
            for (int i = 0; i < m_vocabSize; i++)
            {
                int bi = i * m_dims;
                if (d0 < m_dims) { float v = m_embeddings[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
                if (d1 < m_dims) { float v = m_embeddings[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
                if (d2 < m_dims) { float v = m_embeddings[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
            }
        }
        float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
        float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
        float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;
        const float Scale = 100f;

        for (int i = 0; i < n; i++)
        {
            float[] state = m_traceStates[i];
            float x = d0 < dims ? (state[d0] - min0) / range0 * Scale - Scale / 2f : 0f;
            float y = d1 < dims ? (state[d1] - min1) / range1 * Scale - Scale / 2f : 0f;
            float z = d2 < dims ? (state[d2] - min2) / range2 * Scale - Scale / 2f : 0f;
            m_traceProjected[i] = new Vector3(x, y, z);
        }

        RenderTrace();
    }

    void RenderTrace()
    {
        if (m_traceProjected is null || m_viewport is null) return;

        ClearTraceModel();

        MeshBuilder builder = new();
        float tubeRadius = (float)m_pointSize * 0.08f;
        float nodeRadius = (float)m_pointSize * 0.2f;

        // Draw line segments between consecutive states
        for (int i = 0; i < m_traceProjected.Length - 1; i++)
        {
            builder.AddCylinder(m_traceProjected[i], m_traceProjected[i + 1], tubeRadius, 6);
        }

        // Draw spheres at each state with color gradient (blue → red)
        for (int i = 0; i < m_traceProjected.Length; i++)
        {
            float t = m_traceProjected.Length > 1 ? (float)i / (m_traceProjected.Length - 1) : 0f;
            builder.AddSphere(m_traceProjected[i], nodeRadius * (1f + t * 0.5f), 8, 8);
        }

        // Build color array for the mesh
        MeshGeometry3D mesh = builder.ToMeshGeometry3D();
        List<Color4> colors = new(mesh.Positions.Count);
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            // Find nearest trace point to color by layer progression
            float minDist = float.MaxValue;
            float bestT = 0f;
            for (int j = 0; j < m_traceProjected.Length; j++)
            {
                float d = Vector3.Distance(new Vector3(mesh.Positions[i].X, mesh.Positions[i].Y, mesh.Positions[i].Z), m_traceProjected[j]);
                if (d < minDist) { minDist = d; bestT = m_traceProjected.Length > 1 ? (float)j / (m_traceProjected.Length - 1) : 0f; }
            }
            // Blue (start) → Cyan → Yellow → Red (end)
            colors.Add(TraceGradientColor(bestT));
        }
        mesh.Colors = new Color4Collection(colors);

        m_traceModel = new MeshGeometryModel3D
        {
            Geometry = mesh,
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1, 1, 1, 1),
                EmissiveColor = new Color4(0.15f, 0.15f, 0.15f, 0f),
            },
        };

        m_viewport.Items.Add(m_traceModel);
    }

    static Color4 TraceGradientColor(float t)
    {
        // Blue → Cyan → Green → Yellow → Red
        if (t < 0.25f) { float s = t / 0.25f; return new Color4(0, s, 1, 1); }
        if (t < 0.5f) { float s = (t - 0.25f) / 0.25f; return new Color4(0, 1, 1 - s, 1); }
        if (t < 0.75f) { float s = (t - 0.5f) / 0.25f; return new Color4(s, 1, 0, 1); }
        { float s = (t - 0.75f) / 0.25f; return new Color4(1, 1 - s, 0, 1); }
    }

    void BuildLayerMovements()
    {
        LayerMovements.Clear();
        if (m_traceStates is null || m_traceStates.Length < 2) return;

        int dims = m_traceStates[0].Length;
        float[] distances = new float[m_traceStates.Length - 1];
        float maxDist = 0f;

        for (int i = 0; i < distances.Length; i++)
        {
            float sum = 0f;
            for (int d = 0; d < dims; d++)
            {
                float diff = m_traceStates[i + 1][d] - m_traceStates[i][d];
                sum += diff * diff;
            }
            distances[i] = MathF.Sqrt(sum);
            if (distances[i] > maxDist) maxDist = distances[i];
        }

        for (int i = 0; i < distances.Length; i++)
            LayerMovements.Add(new LayerMovementInfo($"L{i}", distances[i], maxDist));
    }

    void OnLayerSelected()
    {
        if (m_traceProjected is null || m_selectedLayerIndex < 0 || m_selectedLayerIndex >= m_traceProjected.Length) return;

        // Fly camera to the selected layer's position
        Vector3 pos = m_traceProjected[m_selectedLayerIndex];
        if (Camera is PerspectiveCamera cam)
        {
            cam.Position = new Point3D(pos.X, pos.Y, pos.Z + 20);
            cam.LookDirection = new Vector3D(0, 0, -20);
            m_flyCamera?.SyncFromCamera();
        }
    }

    void ClearTrace()
    {
        ClearTraceModel();
        m_traceStates = null;
        m_traceProjected = null;
        LayerMovements.Clear();
        HasTrace = false;
        TraceStatus = "";
    }

    void ClearTraceModel()
    {
        if (m_traceModel is not null && m_viewport is not null)
        {
            m_viewport.Items.Remove(m_traceModel);
            m_traceModel.Dispose();
            m_traceModel = null;
        }
    }

    // ------------------------------------------------------------------
    // Phase 6: Weight Explorer
    // ------------------------------------------------------------------

    void PopulateWeightTensors()
    {
        WeightTensorNames = [];
        if (m_reader is null) return;

        foreach (GgufTensorInfo ti in m_reader.TensorInfos)
        {
            if (ti.Shape.Length >= 2 &&
                (ti.Name.Contains("attn") || ti.Name.Contains("ffn") || ti.Name.Contains("weight") || ti.Name.Contains("embd")))
                WeightTensorNames.Add(ti.Name);
        }

        if (WeightTensorNames.Count > 0)
            SelectedWeightTensor = WeightTensorNames[0];
    }

    void ShowWeights()
    {
        if (m_reader is null || string.IsNullOrEmpty(m_selectedWeightTensor) || m_projected is null) return;

        WeightStatus = $"Loading {m_selectedWeightTensor}...";

        string tensorName = m_selectedWeightTensor;
        Task.Run(() =>
        {
            try
            {
                float[] weights = m_reader.ReadTensorF32(tensorName);
                GgufTensorInfo info = m_reader.GetTensorInfo(tensorName);
                int[] shape = info.Shape;

                if (shape.Length < 2)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        WeightStatus = $"{tensorName}: 1D tensor ({shape[0]} elements), skipped.");
                    return;
                }

                // GGUF stores [cols, rows] (ggml column-major convention)
                int cols = shape[0];
                int rows = shape[1];

                // Project weight rows to 3D using same top-variance dims as embeddings
                int d0, d1, d2;
                if (cols >= 3)
                {
                    (d0, d1, d2) = FindTopVarianceDims(weights, rows, cols);
                }
                else
                {
                    d0 = 0; d1 = Math.Min(1, cols - 1); d2 = Math.Min(2, cols - 1);
                }

                // Normalize into the same coordinate space
                float min0 = float.MaxValue, max0 = float.MinValue;
                float min1 = float.MaxValue, max1 = float.MinValue;
                float min2 = float.MaxValue, max2 = float.MinValue;
                for (int i = 0; i < rows; i++)
                {
                    int bi = i * cols;
                    { float v = weights[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
                    { float v = weights[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
                    { float v = weights[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
                }
                float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
                float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
                float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;
                const float Scale = 100f;

                int maxRows = Math.Min(rows, 5000); // limit for rendering performance
                Vector3[] positions = new Vector3[maxRows];
                for (int i = 0; i < maxRows; i++)
                {
                    int bi = i * cols;
                    float x = (weights[bi + d0] - min0) / range0 * Scale - Scale / 2f;
                    float y = (weights[bi + d1] - min1) / range1 * Scale - Scale / 2f;
                    float z = (weights[bi + d2] - min2) / range2 * Scale - Scale / 2f;
                    positions[i] = new Vector3(x, y, z);
                }

                // Compute effective rank (ratio of top singular value to sum)
                // Approximate via variance across dimensions
                double totalVar = 0;
                double[] colVar = new double[cols];
                for (int c = 0; c < cols; c++)
                {
                    double mean = 0;
                    for (int r = 0; r < rows; r++) mean += weights[r * cols + c];
                    mean /= rows;
                    double v = 0;
                    for (int r = 0; r < rows; r++) { double diff = weights[r * cols + c] - mean; v += diff * diff; }
                    colVar[c] = v;
                    totalVar += v;
                }
                Array.Sort(colVar);
                Array.Reverse(colVar);
                // Count dimensions explaining 90% of variance
                double cumVar = 0;
                int effectiveRank = 0;
                for (int c = 0; c < cols; c++)
                {
                    cumVar += colVar[c];
                    effectiveRank++;
                    if (cumVar >= totalVar * 0.9) break;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    RenderWeightPoints(positions);
                    WeightStatus = $"{tensorName}: {rows}×{cols}\n" +
                                   $"Showing {maxRows} rows, effective rank ≈ {effectiveRank}/{cols} (90% variance)";
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    WeightStatus = $"Error: {ex.Message}");
            }
        });
    }

    void RenderWeightPoints(Vector3[] positions)
    {
        if (m_viewport is null) return;

        ClearWeightModel();

        MeshBuilder builder = new();
        float sphereRadius = (float)m_pointSize * 0.06f;

        for (int i = 0; i < positions.Length; i++)
            builder.AddSphere(positions[i], sphereRadius, 3, 3);

        m_weightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.3f, 0.7f, 1f, 0.6f),
                EmissiveColor = new Color4(0.05f, 0.1f, 0.2f, 0f),
            },
        };

        m_viewport.Items.Add(m_weightModel);
    }

    void ClearWeightModel()
    {
        if (m_weightModel is not null && m_viewport is not null)
        {
            m_viewport.Items.Remove(m_weightModel);
            m_weightModel.Dispose();
            m_weightModel = null;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    void ResetView()
    {
        if (Camera is PerspectiveCamera cam)
        {
            cam.Position = new Point3D(0, 0, 200);
            cam.LookDirection = new Vector3D(0, 0, -200);
            cam.UpDirection = new Vector3D(0, 1, 0);
            m_flyCamera?.SyncFromCamera();
        }
    }

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

    void ToggleOptions()
    {
        var win = new ViewportOptionsWindow
        {
            DataContext = this,
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
    }

    public void Dispose()
    {
        m_flyCamera?.Dispose();
        m_pointCloudModel?.Dispose();
        m_highlightModel?.Dispose();
        m_vectorLineModel?.Dispose();
        m_traceModel?.Dispose();
        m_weightModel?.Dispose();
        m_causalSource?.Dispose();
        m_reader?.Dispose();
        (EffectsManager as IDisposable)?.Dispose();
    }
}
