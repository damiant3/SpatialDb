using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel : ObservableObject, IDisposable
{
    Viewport3DX? m_viewport;
    FlyCamera? m_flyCamera;
    SparseLattice.Gguf.GgufReader? m_reader;
    string? m_ggufPath;
    float[]? m_embeddings;
    int m_dims;
    int m_vocabSize;
    string[]? m_tokenLabels;
    System.Numerics.Vector3[]? m_projected;
    MeshGeometryModel3D? m_pointCloudModel;
    MeshGeometryModel3D? m_highlightModel;
    MeshGeometryModel3D? m_vectorLineModel;
    MeshGeometryModel3D? m_traceModel;
    MeshGeometryModel3D? m_weightModel;
    int m_selectedTokenIdx = -1;
    int m_compareTokenIdx = -1;

    SparseLattice.Gguf.IntegerCausalSource? m_causalSource;
    float[][]? m_traceStates;
    System.Numerics.Vector3[]? m_traceProjected;
    string m_tracePrompt = "The capital of France is";
    string m_traceStatus = "";
    bool m_hasTrace;
    int m_selectedLayerIndex;
    int m_maxLayerIndex;

    string m_selectedWeightTensor = "";
    string m_weightStatus = "";
    ObservableCollection<string> m_weightTensorNames = [];

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
    public Camera Camera { get; }

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
        "PCA (dims 0,1,2)", "PCA (dims 0,1,3)", "PCA (dims 0,2,3)",
        "Raw (dims 0,1,2)", "Raw (dims 1,2,3)",
    ];
    public string[] ColorModes { get; } = ["Token ID", "Magnitude", "Cluster"];

    public string TracePrompt { get => m_tracePrompt; set => SetField(ref m_tracePrompt, value); }
    public string TraceStatus { get => m_traceStatus; set => SetField(ref m_traceStatus, value); }
    public bool HasTrace { get => m_hasTrace; set => SetField(ref m_hasTrace, value); }
    public int SelectedLayerIndex
    {
        get => m_selectedLayerIndex;
        set { if (SetField(ref m_selectedLayerIndex, value)) OnLayerSelected(); }
    }
    public int MaxLayerIndex { get => m_maxLayerIndex; set => SetField(ref m_maxLayerIndex, value); }

    public ObservableCollection<string> WeightTensorNames { get => m_weightTensorNames; set => SetField(ref m_weightTensorNames, value); }
    public string SelectedWeightTensor { get => m_selectedWeightTensor; set => SetField(ref m_selectedWeightTensor, value); }
    public string WeightStatus { get => m_weightStatus; set => SetField(ref m_weightStatus, value); }

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
    public FXAALevel FxaaLevel => m_enableFxaa ? FXAALevel.Low : FXAALevel.None;

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
        m_flyCamera.ResetView += OnResetView;

        LoadModelCommand = new RelayCommand(_ => LoadModel());
        SearchCommand = new RelayCommand(_ => SearchToken(), _ => m_tokenLabels is not null);
        ResetViewCommand = new RelayCommand(_ => OnResetView());
        TracePromptCommand = new RelayCommand(_ => RunTrace(), _ => m_reader is not null);
        ClearTraceCommand = new RelayCommand(_ => ClearTrace(), _ => m_hasTrace);
        ShowWeightsCommand = new RelayCommand(_ => ShowWeights(), _ => m_reader is not null && m_selectedWeightTensor.Length > 0);
        ContextSeeNeighborsCommand = new RelayCommand(_ => ContextSeeNeighbors(), _ => m_selectedTokenIdx >= 0);
        ContextShowDimsCommand = new RelayCommand(_ => ContextShowInOtherDims(), _ => m_selectedTokenIdx >= 0);
        ContextFindClusterCommand = new RelayCommand(_ => ContextFindCluster(), _ => m_selectedTokenIdx >= 0);
        ContextCompareToCommand = new RelayCommand(_ => ContextCompareTo(), _ => m_selectedTokenIdx >= 0);
        ShowOptionsCommand = new RelayCommand(_ => ShowOptions());
    }

    void OnResetView()
    {
        if (Camera is not PerspectiveCamera cam) return;
        cam.Position = new Point3D(0, 0, 200);
        cam.LookDirection = new Vector3D(0, 0, -200);
        cam.UpDirection = new Vector3D(0, 1, 0);
        m_flyCamera?.SyncFromCamera();
    }

    void ShowOptions()
    {
        ViewportOptionsWindow win = new() { DataContext = this, Owner = Application.Current.MainWindow };
        win.ShowDialog();
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
