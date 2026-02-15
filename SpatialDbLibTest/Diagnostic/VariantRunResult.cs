#if DIAGNOSTIC

namespace SpatialDbLibTest.Diagnostic;

public sealed class VariantRunResult
{
    public string VariantName { get; init; } = "";
    public string Permutation { get; init; } = "";
    public bool Completed { get; init; }
    public bool Detected { get; init; }
    public bool Faulted { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string Diagnostics { get; init; } = "";
    public string LockDump { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public string StackTrace { get; init; } = "";

    // Short, human readable one-line summary used by tests/logs.
    public string ShortSummary()
    {
        if (Faulted) return $"DETECTED: faulted ({VariantName}) - {ErrorMessage ?? "exception"}";
        if (Detected) return $"DETECTED: {ErrorMessage ?? VariantName}";
        if (!Completed) return $"DETECTED: timed out ({VariantName}, {Permutation})";
        return $"NOT_DETECTED: variant={VariantName} {Permutation}";
    }
}
#endif
