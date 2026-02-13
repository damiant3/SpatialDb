using HelixToolkit.Wpf;
using SpatialDbLib.Simulation;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.IO;
using SpatialDbApp.Loader;
///////////////////////
namespace SpatialDbApp;

public partial class MainForm : Form
{
    private HelixViewport3D m_viewport = null!;
    private Model3DGroup m_modelGroup = null!;
    private ElementHost m_elementHost = null!;
    private MeshGeometry3D? m_sharedSphereMesh;
    private List<GeometryModel3D> m_spheresFront = new();
    private List<GeometryModel3D> m_spheresBack = new();
    private Model3DGroup m_modelGroupFront = null!;
    private Model3DGroup m_modelGroupBack = null!;
    private ModelVisual3D m_visual = null!;
    private bool m_buffersInitialized = false;
    private EventHandler? m_renderHandler;
    private LatticeRunner? m_latticeRunner;
    private double m_maxComponentAbs = 0;
    private CancellationTokenSource? m_animationCts;
    private bool m_isAnimating = false;
    private static readonly string[] SupportedExtensions = [".csv", ".bmp", ".png", ".jpg", ".jpeg", ".gif"];

    public MainForm()
    {
        InitializeComponent();
        Initialize3DViewport();
        PopulateLoadFiles();
    }

    private void Initialize3DViewport()
    {
        m_viewport = HelixUtils.CreateViewport();
        m_viewport.Camera = HelixUtils.CreateCamera(new Point3D(0, 0, 200)); // centered along +Z looking to origin
        m_modelGroup = HelixUtils.CreateModelGroup();
        m_viewport.Children.Add(new ModelVisual3D { Content = m_modelGroup });
        m_elementHost = HelixUtils.CreateElementHost(
            m_viewport,
            rtbLog.Top,
            rtbLog.Right + 3,
            Height - rtbLog.Top - 45,
            Width - rtbLog.Width - 25);
        Controls.Add(m_elementHost);
        m_elementHost.SendToBack();
    }

    private void PopulateLoadFiles()
    {
        try
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(dataDir))
            {
                cmbLoadFile.Items.Clear();
                cmbLoadFile.Enabled = false;
                return;
            }
            var files = Directory.GetFiles(dataDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(Path.GetFileName)
                .OrderBy(n => n)
                .ToArray();
            cmbLoadFile.Items.Clear();
            foreach (var f in files) cmb_loadfile_additem(f!);
            cmbLoadFile.SelectedIndex = -1;
            cmbLoadFile.Enabled = files.Length > 0;
        }
        catch
        {
            cmbLoadFile.Items.Clear();
            cmbLoadFile.Enabled = false;
        }

        void cmb_loadfile_additem(string f)
            => cmbLoadFile.Items.Add(f);
    }

    private void btnRun_Click(object sender, EventArgs e)
    {
        if (m_isAnimating && m_animationCts != null)
        {
            try { m_animationCts.Cancel(); } catch { }
            return;
        }
        btnRun.Enabled = false;
        cmbLoadFile.Enabled = false;
        m_latticeRunner = new LatticeRunner(this, rtbLog);

#if RenderHandler
        m_renderHandler = (s, e) => m_latticeRunner?.Update3D();
        CompositionTarget.Rendering += m_renderHandler;
#endif

        Task.Run(() =>
        {
            m_latticeRunner?.RunGrandSimulation((int)nudObjCount.Value, (int)(nudTime.Value * 1000));
            BeginInvoke(new Action(() =>
            {
#if RenderHandler
                CompositionTarget.Rendering -= m_renderHandler;
#endif
                Cleanup3DView();
                PopulateLoadFiles();
                cmbLoadFile.Enabled = true;
                btnRun.Enabled = true;
            }));
            m_latticeRunner = null;
        });
    }

    public void Cleanup3DView()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(Cleanup3DView));
            return;
        }
        m_buffersInitialized = false;
        try
        {
            var displayed = m_visual?.Content as Model3DGroup;
            if (displayed != null && ReferenceEquals(displayed, m_modelGroupFront))
            {
                m_modelGroupBack?.Children.Clear();
                m_spheresBack.Clear();
                m_modelGroupBack = null!;
            }
            else if (displayed != null && ReferenceEquals(displayed, m_modelGroupBack))
            {
                m_modelGroupFront?.Children.Clear();
                m_spheresFront.Clear();
                m_modelGroupFront = null!;
            }
            else
            {
                m_modelGroupFront?.Children.Clear();
                m_modelGroupBack?.Children.Clear();
                m_spheresFront.Clear();
                m_spheresBack.Clear();
                m_modelGroupFront = null!;
                m_modelGroupBack = null!;
            }

            if (m_visual?.Content is not Model3DGroup currentContent || (currentContent.Children.Count == 0 && m_modelGroup != null))
                m_visual!.Content = m_modelGroup;
        }
        catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Setup3DView(List<TickableSpatialObject> objects)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => Setup3DView(objects)));
            return;
        }
        if (objects.Count == 0) return;
        double maxDist = 0;
        foreach (var obj in objects)
        {
            var pos = obj.LocalPosition;
            double dist = Math.Sqrt((double)pos.X * pos.X + (double)pos.Y * pos.Y + (double)pos.Z * pos.Z);
            if (dist > maxDist) maxDist = dist;
            double compAbs = Math.Max(Math.Abs(pos.X), Math.Max(Math.Abs(pos.Y), Math.Abs(pos.Z)));
            if (compAbs > m_maxComponentAbs) m_maxComponentAbs = compAbs;
        }
        double sphereRadius = maxDist / 500.0;
        if (sphereRadius < 1.0) sphereRadius = 1.0;
        double cameraDist = maxDist * 4.0;
        m_viewport!.Camera!.Position = new Point3D(0, 0, cameraDist);
        m_viewport!.CameraController!.CameraTarget = new Point3D(0, 0, 0);
        m_sharedSphereMesh = HelixUtils.CreateSphereMesh(new Point3D(0, 0, 0), sphereRadius, 24, 16);
        m_modelGroupFront = HelixUtils.CreateModelGroup();
        m_modelGroupBack = HelixUtils.CreateModelGroup();
        m_spheresFront.Clear();
        m_spheresBack.Clear();

        foreach (var obj in objects)
        {
            var pos = obj.LocalPosition;
            var brush = HelixUtils.GetPositionBrush(obj, m_maxComponentAbs);
            var sphereFront = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = new DiffuseMaterial(brush),
                Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
            };
            var sphereBack = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = new DiffuseMaterial(brush),
                Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
            };
            m_modelGroupFront.Children.Add(sphereFront);
            m_modelGroupBack.Children.Add(sphereBack);
            m_spheresFront.Add(sphereFront);
            m_spheresBack.Add(sphereBack);
        }
        if (m_visual == null)
        {
            m_visual = new ModelVisual3D { Content = m_modelGroupFront };
            m_viewport.Children.Add(m_visual);
        }
        else m_visual.Content = m_modelGroupFront;
        m_buffersInitialized = true;
    }

    public void Setup3DViewFromPoints(List<PointData> points)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => Setup3DViewFromPoints(points)));
            return;
        }
        if (points == null || points.Count == 0) return;
        long minX = long.MaxValue, minY = long.MaxValue, minZ = long.MaxValue;
        long maxX = long.MinValue, maxY = long.MinValue, maxZ = long.MinValue;
        foreach (var p in points)
        {
            var pos = p.Position;
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Z < minZ) minZ = pos.Z;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
            if (pos.Z > maxZ) maxZ = pos.Z;
        }

        var sizeX = Math.Max(1, maxX - minX);
        var sizeY = Math.Max(1, maxY - minY);
        var sizeZ = Math.Max(1, maxZ - minZ);
        var maxExtent = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;
        var centerZ = (minZ + maxZ) / 2.0;
        double cameraDist = Math.Max(100.0, maxExtent * 2.5);
        m_viewport.Camera!.Position = new Point3D(centerX, centerY, centerZ + cameraDist);
        m_viewport.CameraController!.CameraTarget = new Point3D(centerX, centerY, centerZ);

        int minToken = int.MaxValue, maxToken = int.MinValue;
        foreach (var p in points)
            if (p.SizeToken.HasValue)
            {
                minToken = Math.Min(minToken, p.SizeToken.Value);
                maxToken = Math.Max(maxToken, p.SizeToken.Value);
            }
        double baseRadius;
        if (minToken == int.MaxValue) baseRadius = Math.Max(1.0, maxExtent / 200.0);
        else baseRadius = Math.Max(1.0, maxExtent / 200.0);
        m_sharedSphereMesh = HelixUtils.CreateSphereMesh(new Point3D(0, 0, 0), baseRadius, 24, 16);
        m_modelGroupFront = HelixUtils.CreateModelGroup();
        m_modelGroupBack = HelixUtils.CreateModelGroup();
        m_spheresFront.Clear();
        m_spheresBack.Clear();
        foreach (var pd in points)
        {
            var pos = pd.Position;
            System.Windows.Media.Color wcolor = Colors.Gray;
            if (pd.ColorRgb.HasValue)
            {
                var (r, g, b) = pd.ColorRgb.Value;
                r = Math.Clamp(r, 0, 255);
                g = Math.Clamp(g, 0, 255);
                b = Math.Clamp(b, 0, 255);
                wcolor = System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            var hex = $"#{(byte)255:X2}{wcolor.R:X2}{wcolor.G:X2}{wcolor.B:X2}";
            var brush = new SolidColorBrush(wcolor);
            brush.Freeze();
            var matGroup = new MaterialGroup();
            matGroup.Children.Add(new EmissiveMaterial(brush));
            matGroup.Children.Add(new DiffuseMaterial(brush));
            var sphereFront = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = matGroup,
                Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
            };
            var sphereBack = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = matGroup,
                Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
            };
            m_modelGroupFront.Children.Add(sphereFront);
            m_modelGroupBack.Children.Add(sphereBack);
            m_spheresFront.Add(sphereFront);
            m_spheresBack.Add(sphereBack);
        }
        if (m_visual == null)
        {
            m_visual = new ModelVisual3D { Content = m_modelGroupFront };
            m_viewport.Children.Add(m_visual);
        }
        else m_visual.Content = m_modelGroupFront;
        m_buffersInitialized = true;
    }

    public void Update3DView(List<TickableSpatialObject> objects, bool useFrontBuffer)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Update3DView(objects, useFrontBuffer)));
            return;
        }
        if (!m_buffersInitialized) return;
        var spheres = useFrontBuffer ? m_spheresFront : m_spheresBack;
        var nextGroup = useFrontBuffer ? m_modelGroupFront : m_modelGroupBack;

        for (int i = 0; i < objects.Count && i < spheres.Count; i++)
        {
            var pos = objects[i].LocalPosition;
            var brush = HelixUtils.GetPositionBrush(objects[i], m_maxComponentAbs);
            if (spheres[i].Transform is TranslateTransform3D tt)
            {
                tt.OffsetX = pos.X;
                tt.OffsetY = pos.Y;
                tt.OffsetZ = pos.Z;
            }
            else spheres[i].Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z);
            if (spheres[i].Material is DiffuseMaterial mat)
                mat.Brush = brush;
            else spheres[i].Material = new DiffuseMaterial(brush);
        }
        if (nextGroup != null)
            m_visual.Content = nextGroup;
    }

    private async void cmbLoadFile_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbLoadFile.SelectedItem == null) return;
        var fileName = cmbLoadFile.SelectedItem.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName)) return;
        cmbLoadFile.Enabled = false;
        btnRun.Text = "Stop Animation";
        btnRun.Enabled = true;
        m_animationCts?.Cancel();
        m_animationCts = new CancellationTokenSource();
        m_isAnimating = true;
        var token = m_animationCts.Token;
        var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", fileName);
        var ext = Path.GetExtension(dataPath).ToLowerInvariant();
        try
        {
            if (ext == ".csv")
            {
                var frames = LatticeDataLoader.ParseFrames(dataPath).OrderBy(kvp => kvp.Key).ToList();
                if (frames.Count > 1)
                {
                    var orderedFrames = frames.OrderBy(kvp => kvp.Key).ToList();
                    if (orderedFrames.Count == 0)
                        MessageBox.Show(this, "No frame points were parsed from the file.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        while (!token.IsCancellationRequested)
                            foreach (var kvp in orderedFrames)
                            {
                                if (token.IsCancellationRequested) break;
                                var pts = kvp.Value;
                                if (pts == null || pts.Count == 0) continue;
                                var objs = pts.Select(p => new TickableSpatialObject(p.Position)).ToList();
                                Setup3DView(objs);
                                await Task.Delay(250, token).ContinueWith(_ => { }, TaskScheduler.Default);
                            }
                }
                else
                {
                    var points = LatticeDataLoader.ParsePoints(dataPath);
                    if (points == null || points.Count == 0)
                        MessageBox.Show(this, "No points were parsed from the file. Lines that do not begin with a digit or '-' are skipped.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else Setup3DViewFromPoints(points);
                }
            }
            else if (SupportedExtensions.Contains(ext))
            {
                int sample = 2;
                double spacing = 1.0;
                var pts = ImageDataLoader.LoadBitmapAsPoints(dataPath, sample, spacing, ignoreTransparent: true);
                if (pts == null || pts.Count == 0)
                    MessageBox.Show(this, "No points parsed from image (maybe fully transparent).", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else Setup3DViewFromPoints(pts);
            }
            else MessageBox.Show(this, $"Unsupported file type: {ext}", "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load/animate file: {ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            m_animationCts = null;
            m_isAnimating = false;
            btnRun.Text = "Run Grand Sim";
            btnRun.Enabled = true;
            cmbLoadFile.Enabled = true;
        }
    }
}