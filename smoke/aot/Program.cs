using System;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Runs the shared checks under NativeAOT with trimming.</summary>
internal static class Program
{
    private static int Main()
    {
        var failures = SmokeChecks.Run();
        foreach (string f in failures)
        {
            Console.Error.WriteLine("FAIL " + f);
        }

        Console.WriteLine(failures.Count == 0 ? "ok" : "failed");
        return failures.Count == 0 ? 0 : 1;
    }
}
