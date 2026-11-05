namespace RecursiveAI.OneDocument;

/// <summary>
/// Shared by every allocation-count test. Counts the bytes the calling thread allocates while a body runs, after warming the body up.
/// </summary>
/// <remarks>
/// Why the tests flaked: the runtime starts methods in an unoptimised tier and promotes hot ones after a 100 ms delay that restarts
/// whenever new methods are compiled. In a full parallel run other tests keep compiling, so the promotion never arrived during a
/// fixed sleep and the measured loop ran unoptimised code, which boxes and copies where the final code does not. The test projects
/// therefore turn tiered compilation off (every method is compiled optimised on first call), and this helper warms the body until
/// two runs in a row allocate the same amount, which also covers one-time lazy initialisation.
/// </remarks>
internal static class AllocationProbe
{
    /// <summary>Runs <paramref name="body"/> until its allocation is steady (at most 50 runs), then returns the bytes one more run allocates.</summary>
    public static long Measure(Action body)
    {
        long last = -1;
        int steady = 0;
        for (int i = 0; i < 50 && steady < 2; i++)
        {
            long used = Run(body);
            steady = used == last ? steady + 1 : 0;
            last = used;
        }

        return Run(body);
    }

    private static long Run(Action body)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
