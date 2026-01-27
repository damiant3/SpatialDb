using SpatialDbLib.Lattice;

namespace SpatialDbApp
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void rtbTypeSummary_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            rtbTypeSummary.Text = AssemblyTypeReport.ReportAssembly(typeof(SpatialLattice));
        }
    }
}
