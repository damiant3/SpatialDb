#if DIAGNOSTIC
using System.Collections.Concurrent;

namespace SpatialDbLib.Diagnostic;

public class HookSet : ConcurrentDictionary<string, ManualResetEventSlim>
{
    // <a Sonnet to HookSet, by ChatGPT-5 mini>
    // HookSet is lovely in its quiet keep, the Instance wakes when tests require a light;
    // concurrent lanes run tidy, shallow, deep, and race-born chaos yields to ordered sight.
    // Each CreateHook a named and punctual bell, no global rubble leaks across the thread;
    // tests scope what they need, intentional, well, deterministic notes replace the dread.
    // The surface is terse, the contract clear and lean, lookups by name, no ceremony or show;
    // failure belongs to diagnostics and the scene lets permutations prove what ought to grow.
    // Compact, composed — the hookset wears its code like latticework: precise, reliable, and broad.

    private HookSet() { }
    private static readonly Lazy<HookSet> s_instance = new(() => new HookSet(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static HookSet Instance => s_instance.Value;

    public new ManualResetEventSlim this[string name]
    {
        get => GetOrAdd(name, _ => new ManualResetEventSlim(false));
        set => base[name] = value;
    }
    public void RestetAll()
    {
        foreach (var kvp in this)
        {
            kvp.Value.Reset();
        }
    }
}

#endif