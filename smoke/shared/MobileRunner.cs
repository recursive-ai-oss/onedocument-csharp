using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>
/// Runs the shared check groups inside a phone or tablet app and writes one line per result through a caller-supplied sink (the iOS
/// host prints to the console, the Android host to the system log). The lines match the browser host: <c>RUNTIME</c>, <c>CRYPTO</c>,
/// <c>GROUP</c>, <c>FAIL</c>, <c>ENCRYPTED</c> and a last <c>SMOKE-RESULT</c> line the runner scripts look for.
/// </summary>
internal static class MobileRunner
{
    /// <summary>Runs every group and returns 0 when none failed.</summary>
    /// <param name="log">Receives each output line.</param>
    public static int Run(Action<string> log)
    {
        log("RUNTIME " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier + " processors=" + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        foreach ((string primitive, string outcome) in PlatformSmoke.CryptoProbe())
        {
            log("CRYPTO " + primitive + " = " + outcome);
        }

        int failed = 0;
        int groups = 0;
        foreach ((string name, Func<List<string>> run) in SmokeChecks.Groups)
        {
            groups++;
            var watch = Stopwatch.StartNew();
            List<string> failures;
            try
            {
                failures = run();
            }
            catch (Exception ex)
            {
                failures = ["exception " + ex.GetType().Name + ": " + ex.Message];
            }

            log("GROUP " + name + " " + (failures.Count == 0 ? "ok" : "failed") + " " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
            foreach (string f in failures)
            {
                log("FAIL " + name + ": " + f);
                failed++;
            }
        }

        foreach ((string file, string outcome) in PlatformSmoke.Results)
        {
            log("ENCRYPTED " + file + " = " + outcome);
        }

        log("SMOKE-RESULT " + (failed == 0 ? "ok" : "failed") + " groups=" + groups.ToString(CultureInfo.InvariantCulture) + " failures=" + failed.ToString(CultureInfo.InvariantCulture));
        return failed == 0 ? 0 : 1;
    }
}
