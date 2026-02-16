///////////////////////////
namespace SpatialDbApp.Log;

internal interface ILogSink
{
    bool IsAlive { get; }
    void Append(string text);
    void AppendLine(string text);
    void ScrollToEnd();
}

internal sealed class UiLogSink : ILogSink
{
    readonly RichTextBox _rtb;

    private UiLogSink(RichTextBox rtb) => _rtb = rtb ?? throw new ArgumentNullException(nameof(rtb));

    public static ILogSink CreateFor(RichTextBox rtb) => new UiLogSink(rtb);

    public bool IsAlive => _rtb != null && !_rtb.IsDisposed && !_rtb.Disposing;

    public void Append(string text)
    {
        if (!IsAlive) return;

        try
        {
            if (_rtb.InvokeRequired)
            {
                try
                {
                    _rtb.BeginInvoke(() =>
                    {
                        if (!_rtb.IsDisposed && !_rtb.Disposing)
                            _rtb.AppendText(text);
                    });
                }
                catch { }
                return;
            }

            if (!_rtb.IsDisposed && !_rtb.Disposing)
                _rtb.AppendText(text);
        }
        catch { }
    }

    public void AppendLine(string text)
    {
        if (!IsAlive) return;

        try
        {
            if (_rtb.InvokeRequired)
            {
                try
                {
                    _rtb.BeginInvoke((() =>
                    {
                        if (!_rtb.IsDisposed && !_rtb.Disposing)
                        {
                            _rtb.AppendText(text + Environment.NewLine);
                            _rtb.ScrollToCaret();
                            _rtb.Refresh();
                        }
                    }));
                }
                catch { }
                return;
            }

            if (!_rtb.IsDisposed && !_rtb.Disposing)
            {
                _rtb.AppendText(text + Environment.NewLine);
                _rtb.ScrollToCaret();
                _rtb.Refresh();
            }
        }
        catch { }
    }

    public void ScrollToEnd()
    {
        if (!IsAlive) return;

        try
        {
            if (_rtb.InvokeRequired)
            {
                try
                {
                    _rtb.BeginInvoke(() =>
                    {
                        if (!_rtb.IsDisposed && !_rtb.Disposing)
                        {
                            _rtb.ScrollToCaret();
                            _rtb.Refresh();
                        }
                    });
                }
                catch { }
                return;
            }

            if (!_rtb.IsDisposed && !_rtb.Disposing)
            {
                _rtb.ScrollToCaret();
                _rtb.Refresh();
            }
        }
        catch { }
    }
}