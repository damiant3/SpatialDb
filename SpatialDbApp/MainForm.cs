using HelixToolkit.Wpf;
using SpatialDbLib.Simulation;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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

    public MainForm()
    {
        InitializeComponent();
        Initialize3DViewport();
    }

    private void Initialize3DViewport()
    {
        m_viewport = HelixUtils.CreateViewport();

        // Camera centered along +Z looking to origin
        m_viewport.Camera = HelixUtils.CreateCamera(new Point3D(0, 0, 200));

        m_modelGroup = HelixUtils.CreateModelGroup();
        m_viewport.Children.Add(new ModelVisual3D { Content = m_modelGroup });

        var sphereModel = HelixUtils.CreateSphereModel(new Point3D(0, 0, 0), 18.0, 60, 30);
        m_modelGroup.Children.Add(sphereModel);

        m_elementHost = HelixUtils.CreateElementHost(m_viewport, rtbLog.Top, rtbLog.Right + 3, Height - rtbLog.Top - 45, Width - rtbLog.Width - 25);
        Controls.Add(m_elementHost);
        m_elementHost.SendToBack();
    }

    private void btnRun_Click(object sender, EventArgs e)
    {
        btnRun.Enabled = false;
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
                m_visual.Content = m_modelGroup;
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
}