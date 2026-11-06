using System;
using System.Diagnostics;
using System.Globalization;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>
/// Runs the shared checks in a browser WebAssembly host. Every line is prefixed so the runner can read them from the page's console:
/// <c>GROUP</c> (one per check group, with its time), <c>FAIL</c>, <c>CRYPTO</c> (the cryptography probe), <c>ENCRYPTED</c> (what each
/// password-protected fixture did) and a last <c>SMOKE-RESULT</c> line.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        // A browser has no native cryptography for some primitives; the library must say so with a typed exception.
        PlatformSmoke.PlatformGapAllowed = true;
        Console.WriteLine("RUNTIME " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier + " processors=" + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        foreach ((string primitive, string outcome) in PlatformSmoke.CryptoProbe())
        {
            Console.WriteLine("CRYPTO " + primitive + " = " + outcome);
        }

        int failed = 0;
        int groups = 0;
        foreach ((string name, Func<System.Collections.Generic.List<string>> run) in SmokeChecks.Groups)
        {
            groups++;
            var watch = Stopwatch.StartNew();
            System.Collections.Generic.List<string> failures;
            try
            {
                failures = run();
            }
            catch (Exception ex)
            {
                failures = ["exception " + ex.GetType().Name + ": " + ex.Message];
            }

            Console.WriteLine("GROUP " + name + " " + (failures.Count == 0 ? "ok" : "failed") + " " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
            foreach (string f in failures)
            {
                Console.WriteLine("FAIL " + name + ": " + f);
                failed++;
            }
        }

        foreach ((string file, string outcome) in PlatformSmoke.Results)
        {
            Console.WriteLine("ENCRYPTED " + file + " = " + outcome);
        }

        Console.WriteLine("SMOKE-RESULT " + (failed == 0 ? "ok" : "failed") + " groups=" + groups.ToString(CultureInfo.InvariantCulture) + " failures=" + failed.ToString(CultureInfo.InvariantCulture));
        return failed == 0 ? 0 : 1;
    }
}
