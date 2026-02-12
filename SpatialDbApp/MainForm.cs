namespace SpatialDbApp;
using System.Windows.Forms;
using HelixToolkit.Wpf;
using System.Windows.Media.Media3D;
using System.Windows.Forms.Integration;

public partial class MainForm : Form
{
    private HelixViewport3D m_viewport = null!;
    private Model3DGroup m_modelGroup = null!;
    private ElementHost m_elementHost = null!;

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

        m_elementHost = HelixUtils.CreateElementHost(m_viewport);
        Controls.Add(m_elementHost);
        m_elementHost.SendToBack();
    }
}
