using System.Windows.Controls;
///////////////////////////////////////////////
namespace Spark.Controls;

/// <summary>
/// Sound FX tab — prompt-based SFX generation with categories, batch variants,
/// waveform visualizer, and filtered library.
/// Inherits DataContext from the parent (MainViewModel).
/// </summary>
partial class SfxPanel : UserControl
{
    public SfxPanel()
    {
        InitializeComponent();
    }
}
