using System.Diagnostics;
using System.Globalization;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>
/// Runs one bench command in a fresh process of this assembly and returns its output line. The runtime reports no process
/// high-water mark on macOS, so there the child runs under <c>/usr/bin/time -l</c> and its "maximum resident set size" is read
/// from the report instead; on Linux the child's own figure is used.
/// </summary>
internal static class ChildProcess
{
    /// <summary>Runs <paramref name="args"/> after the assembly path; returns the exit code and the first stdout line, with the peak in megabytes (0 when unknown).</summary>
    public static int Run(string[] args, out string line, out long externalPeakMb)
    {
        string dll = typeof(ChildProcess).Assembly.Location;
        string host = Environment.ProcessPath ?? "dotnet";
        bool mac = OperatingSystem.IsMacOS() && File.Exists("/usr/bin/time");
        var psi = new ProcessStartInfo(mac ? "/usr/bin/time" : host) { RedirectStandardOutput = true, RedirectStandardError = mac, UseShellExecute = false };
        if (mac)
        {
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add(host);
        }

        psi.ArgumentList.Add(dll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process p = Process.Start(psi)!;
        line = p.StandardOutput.ReadToEnd().Trim();
        string err = mac ? p.StandardError.ReadToEnd() : string.Empty;
        p.WaitForExit();
        externalPeakMb = 0;
        foreach (string l in err.Split('\n'))
        {
            // macOS reports the figure in bytes: "  123456789  maximum resident set size".
            string t = l.Trim();
            if (t.EndsWith("maximum resident set size", StringComparison.Ordinal)
                && long.TryParse(t.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
            {
                externalPeakMb = bytes >> 20;
            }
        }

        return p.ExitCode;
    }

    /// <summary>Replaces tab-separated field <paramref name="index"/> of <paramref name="line"/> with the externally measured peak when the child reported none.</summary>
    public static string WithPeak(string line, int index, long externalPeakMb)
    {
        if (externalPeakMb <= 0)
        {
            return line;
        }

        string[] f = line.Split('\t');
        if (f.Length > index && (f[index] == "0" || f[index].Length == 0))
        {
            f[index] = externalPeakMb.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join('\t', f);
    }
}
