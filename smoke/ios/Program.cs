using System.Threading;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>
/// Runs the shared checks as soon as the process starts, before any UI exists, and exits with the result. The checks need no screen,
/// and current iOS releases stop an app that starts UIKit without scene lifecycle support, which a smoke host has no use for.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        int code = 1;

        // Layout and calc recurse deeply; the main thread's stack is small, so the work runs on its own thread.
        var worker = new Thread(() => code = MobileRunner.Run(Console.WriteLine), 64 * 1024 * 1024);
        worker.Start();
        worker.Join();
        Console.Out.Flush();
        return code;
    }
}
