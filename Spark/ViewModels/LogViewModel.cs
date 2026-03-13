using System.Collections.ObjectModel;
using Common.Wpf.ViewModels;
///////////////////////////////////////////////
namespace Spark.ViewModels;

/// <summary>
/// Manages the scrolling log output. Caps at MaxLines to prevent
/// unbounded memory growth and UI layout crowding.
/// </summary>
sealed class LogViewModel : ObservableObject
{
    const int MaxLines = 200;

    public ObservableCollection<string> LogLines { get; } = [];

    public void Log(string message)
    {
        Dispatch(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogLines.Count > MaxLines) LogLines.RemoveAt(0);
        });
    }
}
