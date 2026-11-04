using System.Diagnostics;
using System.Globalization;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>
/// The shape of the memory-per-unit measurement later stages use for the full models (bytes per cell, bytes per node): build
/// something of a known size, and report what staying alive costs per unit. Nothing calls it for the document rows yet.
/// </summary>
public static class Footprint
{
    /// <summary>
    /// Measures the bytes retained by the object <paramref name="build"/> returns, per unit.
    /// </summary>
    /// <param name="build">Builds the model and returns it; it must keep everything it built reachable from the result.</param>
    /// <param name="units">How many cells (or nodes) the model holds.</param>
    /// <returns>Retained bytes per unit, and bytes allocated per unit while building.</returns>
    public static (double RetainedPerUnit, double AllocatedPerUnit) Measure(Func<object> build, long units)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(units);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        object model = build();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(model);
        return ((heapAfter - heapBefore) / (double)units, allocated / (double)units);
    }

    /// <summary>Formats a measurement as one TSV line for the results directory.</summary>
    /// <param name="name">The model being measured.</param>
    /// <param name="units">Units it held.</param>
    /// <param name="result">The values from <see cref="Measure"/>.</param>
    /// <returns>A tab-separated line.</returns>
    public static string Line(string name, long units, (double RetainedPerUnit, double AllocatedPerUnit) result) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}\t{units}\t{result.RetainedPerUnit:F1}\t{result.AllocatedPerUnit:F1}");

    /// <summary>The current process's working set in bytes, for rows that report it.</summary>
    /// <returns>Bytes.</returns>
    public static long WorkingSet()
    {
        using Process p = Process.GetCurrentProcess();
        return p.WorkingSet64;
    }
}
