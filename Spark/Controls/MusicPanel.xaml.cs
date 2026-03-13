using System.Windows.Controls;
///////////////////////////////////////////////
namespace Spark.Controls;

/// <summary>
/// Music Director tab — prompt-based generation, playback, visualizer, and track library.
/// Inherits DataContext from the parent (MainViewModel).
/// </summary>
partial class MusicPanel : UserControl
{
    public MusicPanel()
    {
        InitializeComponent();
    }
}
