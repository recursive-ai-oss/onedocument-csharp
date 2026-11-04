using System;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>
/// Runs the shared checks in a browser WebAssembly host. The page's console shows <c>ok</c> or the names of the failed checks; a
/// headless browser reads that line.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var failures = SmokeChecks.Run();
        Console.WriteLine(failures.Count == 0 ? "smoke: ok" : "smoke: failed " + string.Join(",", failures));
        return failures.Count == 0 ? 0 : 1;
    }
}
