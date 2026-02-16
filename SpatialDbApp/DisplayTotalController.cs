namespace SpatialDbApp;

internal sealed class DisplayTotalController : IDisposable
{
    readonly NumericUpDown _nudTotal;
    readonly NumericUpDown _nudDisplay;
    int _lastTotal;
    bool _disposed;

    public event Action<int>? TotalChanged;
    public event Action<int>? DisplayChanged;

    public DisplayTotalController(NumericUpDown nudTotal, NumericUpDown nudDisplay)
    {
        _nudTotal = nudTotal ?? throw new ArgumentNullException(nameof(nudTotal));
        _nudDisplay = nudDisplay ?? throw new ArgumentNullException(nameof(nudDisplay));

        _lastTotal = (int)_nudTotal.Value;

        // Ensure display maximum is consistent with total at startup
        try
        {
            _nudDisplay.Maximum = _nudTotal.Maximum;
            if (_nudDisplay.Value > _nudTotal.Value)
                _nudDisplay.Value = _nudTotal.Value;
        }
        catch { /* best-effort */ }
    }

    public int InitialDisplayCount => (int)(_nudDisplay?.Value ?? 0);

    // Called by the UI numeric-up-down display ValueChanged handler
    public void HandleDisplayValueChanged()
    {
        if (_disposed) return;
        try
        {
            // Clamp to current total (safety)
            var total = (int)_nudTotal.Value;
            if (_nudDisplay.Value > _nudTotal.Value)
                _nudDisplay.Value = _nudTotal.Value;

            // Keep maximum in sync
            _nudDisplay.Maximum = _nudTotal.Value;

            // Notify subscribers (e.g. active runner)
            DisplayChanged?.Invoke((int)_nudDisplay.Value);
        }
        catch
        {
            // best-effort; swallow to avoid crashing UI
        }
    }

    // Called by the UI numeric-up-down total ValueChanged handler
    public void HandleTotalValueChanged()
    {
        if (_disposed) return;
        try
        {
            var newTotal = (int)_nudTotal.Value;
            var oldTotal = _lastTotal;
            var displayVal = (int)_nudDisplay.Value;

            var wasTracking = (displayVal == oldTotal);

            // update display NUD maximum first
            _nudDisplay.Maximum = newTotal;

            if (wasTracking)
            {
                // follow the total
                _nudDisplay.Value = newTotal;
            }
            else
            {
                // clamp down if necessary
                if (_nudDisplay.Value > newTotal)
                    _nudDisplay.Value = newTotal;
            }

            // update stored last total
            _lastTotal = newTotal;

            // Notify subscribers
            TotalChanged?.Invoke(newTotal);
            DisplayChanged?.Invoke((int)_nudDisplay.Value);
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        TotalChanged = null;
        DisplayChanged = null;
        _disposed = true;
    }
}