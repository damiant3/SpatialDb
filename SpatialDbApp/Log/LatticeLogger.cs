///////////////////////////
namespace SpatialDbApp.Log;
internal sealed class LatticeLogger(ILogSink sink)
{
    readonly ILogSink m_sink = sink;

    public void LogLine(string message)
    {
        if (!m_sink.IsAlive) return;
        try
        {
            m_sink.AppendLine(message);
        }
        catch { }
    }

    public void Log(string message)
    {
        if (!m_sink.IsAlive) return;
        try
        {
            m_sink.Append(message);
        }
        catch { }
    }

    public void ScrollToEnd()
    {
        if (!m_sink.IsAlive) return;
        try
        {
            m_sink.ScrollToEnd();
        }
        catch { }
    }
}