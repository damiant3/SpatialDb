///////////////////////////////////////////////
namespace NeuralNavigator;

sealed class LayerMovementInfo(string label, float distance, float maxDistance)
{
    public string Label { get; } = label;
    public float Distance { get; } = distance;
    public double BarWidth { get; } = maxDistance > 0 ? Math.Max(2, distance / maxDistance * 180.0) : 0;
    public System.Windows.Media.SolidColorBrush BarColor { get; } = new(
        System.Windows.Media.Color.FromRgb(
            (byte)(Math.Min(distance / Math.Max(maxDistance, 1e-8f), 1f) * 255),
            (byte)((1f - Math.Min(distance / Math.Max(maxDistance, 1e-8f), 1f)) * 200),
            100));
}
