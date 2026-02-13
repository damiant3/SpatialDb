using HelixToolkit.Wpf;
using SpatialDbLib.Simulation;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.IO;
using SpatialDbApp.Loader;
using System.Diagnostics;

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

    // Data loader/animation state
    private CancellationTokenSource? m_animationCts;
    private bool m_isAnimating = false;

    public MainForm()
    {
        InitializeComponent();
        Initialize3DViewport();
        PopulateLoadFiles();
    }

    private void Initialize3DViewport()
    {
        m_viewport = HelixUtils.CreateViewport();

        // Camera centered along +Z looking to origin
        m_viewport.Camera = HelixUtils.CreateCamera(new Point3D(0, 0, 200));

        m_modelGroup = HelixUtils.CreateModelGroup();
        m_viewport.Children.Add(new ModelVisual3D { Content = m_modelGroup });
        m_elementHost = HelixUtils.CreateElementHost(m_viewport, rtbLog.Top, rtbLog.Right + 3, Height - rtbLog.Top - 45, Width - rtbLog.Width - 25);
        Controls.Add(m_elementHost);
        m_elementHost.SendToBack();
    }

    // Populate the cmbLoadFile with CSV files found in the appbin/Data directory.
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

            var files = Directory.GetFiles(dataDir, "*.csv")
                .Select(Path.GetFileName)
                .OrderBy(n => n)
                .ToArray();

            cmbLoadFile.Items.Clear();
            foreach (var f in files) cmbLoadFile.Items.Add(f!);

            cmbLoadFile.SelectedIndex = -1;
            cmbLoadFile.Enabled = files.Length == 0 || !m_latticeRunner?.Equals(null) != true || !m_latticeRunner!.Equals(null);
            // If a grand simulation is running, the runner will call Cleanup3DView which will keep UI consistent.
        }
        catch
        {
            // best-effort
            cmbLoadFile.Items.Clear();
            cmbLoadFile.Enabled = false;
        }
    }

    private void btnRun_Click(object sender, EventArgs e)
    {
        // If an animation is running, this button acts as "Stop Animation"
        if (m_isAnimating && m_animationCts != null)
        {
            try
            {
                m_animationCts.Cancel();
            }
            catch { }
            // UI will be restored when the animation task observes cancellation and exits.
            return;
        }

        // Otherwise start Grand Simulation
        btnRun.Enabled = false;
        // Disable file loading while grand sim runs
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
                // Re-populate files and re-enable combo after run finishes
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

        // Stop using the buffers until a new setup occurs
        m_buffersInitialized = false;

        // If the visual is currently showing one of the model groups, preserve that displayed group
        // so the scene remains visible. Clear the opposite/back buffer so it doesn't hold stale references.
        try
        {
            var displayed = m_visual?.Content as Model3DGroup;

            // If displayed is the front group, clear/back the back buffer; otherwise clear front.
            if (displayed != null && ReferenceEquals(displayed, m_modelGroupFront))
            {
                // Preserve front (visible), clear back
                m_modelGroupBack?.Children.Clear();
                m_spheresBack.Clear();
                m_modelGroupBack = null!;
            }
            else if (displayed != null && ReferenceEquals(displayed, m_modelGroupBack))
            {
                // Preserve back (visible), clear front
                m_modelGroupFront?.Children.Clear();
                m_spheresFront.Clear();
                m_modelGroupFront = null!;
            }
            else
            {
                // Unknown visual content (or none) — clear both to be safe.
                m_modelGroupFront?.Children.Clear();
                m_modelGroupBack?.Children.Clear();
                m_spheresFront.Clear();
                m_spheresBack.Clear();
                m_modelGroupFront = null!;
                m_modelGroupBack = null!;
            }

            // Do not leave the viewport pointing at a cleared/empty group; if visual is null or now invalid,
            // reset to the base model group so UI still shows something until Setup3DView runs.
            if (m_visual?.Content is not Model3DGroup currentContent || (currentContent.Children.Count == 0 && m_modelGroup != null))
            {
                m_visual!.Content = m_modelGroup;
            }
        }
        catch
        {
            // Best-effort cleanup only — ignore UI race conditions.
        }

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

        // Find the farthest object from the origin for use in scaling the sphere radius and camera distance, and also for colors
        double maxDist = 0;
        foreach (var obj in objects)
        {
            var pos = obj.LocalPosition;
            double dist = Math.Sqrt((double)pos.X * pos.X + (double)pos.Y * pos.Y + (double)pos.Z * pos.Z);
            if (dist > maxDist) maxDist = dist;
            double compAbs = Math.Max(Math.Abs(pos.X), Math.Max(Math.Abs(pos.Y), Math.Abs(pos.Z)));
            if (compAbs > m_maxComponentAbs) m_maxComponentAbs = compAbs;
        }

        // Set sphere radius and camera distance based on maxDist
        double sphereRadius = maxDist / 500.0;
        if (sphereRadius < 1.0) sphereRadius = 1.0;
        double cameraDist = maxDist * 4.0;

        // Set camera position
        m_viewport!.Camera!.Position = new Point3D(0, 0, cameraDist);
        m_viewport!.CameraController!.CameraTarget = new Point3D(0, 0, 0);

        // Always create and assign the shared mesh for the current radius
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
        else
        {
            m_visual.Content = m_modelGroupFront;
        }
        m_buffersInitialized = true;
    }

    // New overload: accept PointData with optional color and size tokens.
    public void Setup3DViewFromPoints(List<PointData> points)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => Setup3DViewFromPoints(points)));
            return;
        }

        if (points == null || points.Count == 0) return;

        // Compute bounding box for camera placement
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

        // Compute a scale metric (max dimension)
        var sizeX = Math.Max(1, maxX - minX);
        var sizeY = Math.Max(1, maxY - minY);
        var sizeZ = Math.Max(1, maxZ - minZ);
        var maxExtent = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;
        var centerZ = (minZ + maxZ) / 2.0;

        // Choose camera distance proportional to extent
        double cameraDist = Math.Max(100.0, maxExtent * 2.5);
        m_viewport.Camera!.Position = new Point3D(centerX, centerY, centerZ + cameraDist);
        m_viewport.CameraController!.CameraTarget = new Point3D(centerX, centerY, centerZ);

        // Decide uniform sphere radius. If any point supplies SizeToken, map min->max token to a radius range.
        int minToken = int.MaxValue, maxToken = int.MinValue;
        foreach (var p in points)
        {
            if (p.SizeToken.HasValue)
            {
                minToken = Math.Min(minToken, p.SizeToken.Value);
                maxToken = Math.Max(maxToken, p.SizeToken.Value);
            }
        }
        double baseRadius;
        if (minToken == int.MaxValue)
        {
            // no size tokens: choose radius relative to extent
            baseRadius = Math.Max(1.0, maxExtent / 200.0);
        }
        else
        {
            // map token -> radius in [maxExtent/400, maxExtent/40]
            baseRadius = Math.Max(1.0, maxExtent / 200.0);
        }

        // Shared mesh for uniform appearance
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
            // Log the exact parsed color (ARGB hex) for debugging
            var hex = $"#{(byte)255:X2}{wcolor.R:X2}{wcolor.G:X2}{wcolor.B:X2}";
            Debug.WriteLine($"Setup3DViewFromPoints: point={pos.X},{pos.Y},{pos.Z} color={wcolor.R},{wcolor.G},{wcolor.B} hex={hex} size={pd.SizeToken}");

            // Use the parsed color (no hard-coded override). Keep Emissive+Diffuse so small values remain visible.
            var brush = new SolidColorBrush(wcolor);
            brush.Freeze();
            var matGroup = new MaterialGroup();
            matGroup.Children.Add(new EmissiveMaterial(brush)); // shows raw color independent of lighting
            matGroup.Children.Add(new DiffuseMaterial(brush));  // still responds to scene lighting

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
        else
        {
            m_visual.Content = m_modelGroupFront;
        }
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
            else
            {
                spheres[i].Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z);
            }
            // Update color/material
            if (spheres[i].Material is DiffuseMaterial mat)
            {
                mat.Brush = brush;
            }
            else
            {
                spheres[i].Material = new DiffuseMaterial(brush);
            }
        }
        if (nextGroup != null)
            m_visual.Content = nextGroup;
    }

    // Handler for the combo selection — loads a static or frame-based CSV from Data directory.
    private async void cmbLoadFile_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbLoadFile.SelectedItem == null) return;
        var fileName = cmbLoadFile.SelectedItem.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName)) return;

        // Disable UI while loading/playing
        cmbLoadFile.Enabled = false;
        // keep btnRun enabled so user can stop animation; change label to indicate stop action
        btnRun.Text = "Stop Animation";
        btnRun.Enabled = true;

        m_animationCts?.Cancel();
        m_animationCts = new CancellationTokenSource();
        m_isAnimating = true;
        var token = m_animationCts.Token;

        var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", fileName);

        try
        {
            // Try frames (frame-based file) first; fall back to static points.
            var frames = LatticeDataLoader.ParseFrames(dataPath)
                .OrderBy(kvp => kvp.Key)
                .ToList();

            if (frames.Count > 1)
            {
                // Play frames as animation on the UI by projecting PointData -> TickableSpatialObject per frame
                Debug.WriteLine($"cmbLoadFile: parsed {frames.Count} frames.");
                var orderedFrames = frames.OrderBy(kvp => kvp.Key).ToList();
                if (orderedFrames.Count == 0)
                {
                    MessageBox.Show(this, "No frame points were parsed from the file.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // Loop the animation until cancelled so user can observe it.
                    while (!token.IsCancellationRequested)
                    {
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
                }
            }
            else
            {
                // Static points (supports header-aware PointData)
                var points = LatticeDataLoader.ParsePoints(dataPath);
                Debug.WriteLine($"cmbLoadFile: parsed {points?.Count ?? 0} static points from {fileName}");
                if (points == null || points.Count == 0)
                {
                    // No points parsed — give the user feedback instead of silently doing nothing.
                    MessageBox.Show(this, "No points were parsed from the file. Lines that do not begin with a digit or '-' are skipped.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // Convert PointData -> UI helper that respects color/size tokens
                    Setup3DViewFromPoints(points);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled — ignore
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load/animate file: {ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            m_animationCts = null;
            m_isAnimating = false;
            // restore UI state
            btnRun.Text = "Run Grand Sim";
            btnRun.Enabled = true;
            // Keep combo enabled unless a grand sim is running (btnRun disabled by grand sim)
            cmbLoadFile.Enabled = true;
        }
    }
}