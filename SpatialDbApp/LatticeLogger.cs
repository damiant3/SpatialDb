///////////////////////////////
namespace SpatialDbApp.Logging;

internal sealed class LatticeLogger
{
    readonly RichTextBox m_rtb;

    public LatticeLogger(RichTextBox rtb) => m_rtb = rtb ?? throw new ArgumentNullException(nameof(rtb));

    public void LogLine(string message)
    {
        if (m_rtb.InvokeRequired)
        {
            try
            {
                m_rtb.Invoke(() =>
                {
                    m_rtb.AppendText(message + Environment.NewLine);
                    m_rtb.ScrollToCaret();
                    m_rtb.Refresh();
                });
            }
            catch { }
            return;
        }
        m_rtb.AppendText(message + Environment.NewLine);
        m_rtb.ScrollToCaret();
        m_rtb.Refresh();
    }

    public void Log(string message)
    {
        if (m_rtb.InvokeRequired)
        {
            try
            {
                m_rtb.Invoke(() => { m_rtb.AppendText(message); });
            }
            catch { }
            return;
        }
        m_rtb.AppendText(message);
    }

    public void ScrollToEnd()
    {
        try
        {
            if (m_rtb.InvokeRequired)
            {
                m_rtb.Invoke(() => { m_rtb.ScrollToCaret(); m_rtb.Refresh(); });
                return;
            }
            m_rtb.ScrollToCaret();
            m_rtb.Refresh();
        }
        catch { }
    }
}