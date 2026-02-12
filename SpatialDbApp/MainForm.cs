namespace SpatialDbApp;
using HelixToolkit.Wpf;
using SpatialDbLib.Simulation;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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

        m_elementHost = HelixUtils.CreateElementHost(m_viewport, rtbLog.Top, rtbLog.Right+3, Height-rtbLog.Top-45, Width-rtbLog.Width-25);
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
        m_buffersInitialized = false;
        
        m_spheresFront.Clear();
        m_modelGroupFront?.Children.Clear();
        m_modelGroupFront = null!;

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

        // Find the farthest object from the origin
        double maxDist = 0;
        foreach (var obj in objects)
        {
            var pos = obj.LocalPosition;
            double dist = Math.Sqrt((double)pos.X * pos.X + (double)pos.Y * pos.Y + (double)pos.Z * pos.Z);
            if (dist > maxDist) maxDist = dist;
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
            var sphereFront = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = HelixUtils.DefaultMaterial,
                Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
            };
            var sphereBack = new GeometryModel3D
            {
                Geometry = m_sharedSphereMesh,
                Material = HelixUtils.DefaultMaterial,
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
        }
        m_visual.Content = nextGroup;
    }
}
