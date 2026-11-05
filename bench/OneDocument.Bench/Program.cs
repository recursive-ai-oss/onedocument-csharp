using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Running;
using RecursiveAI.OneDocument.Bench.Inputs;

namespace RecursiveAI.OneDocument.Bench;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Our own commands come first; anything else goes to BenchmarkDotNet unchanged (--filter, --job, ...).
        switch (args.FirstOrDefault())
        {
            case "gen":
                return Gen(args.Skip(1).ToArray());
            case "peak":
                return Peak(args.Skip(1).ToArray());
            case "peakall":
                return PeakAll(args.Skip(1).ToArray());
            case "bpc":
                return CellFootprint.Run(args.Skip(1).ToArray());
            case "bpcall":
                return CellFootprint.RunAll(args.Skip(1).ToArray());
            case "xlsxstream":
                return XlsxStreamBench.Run(args.Skip(1).ToArray());
            case "poco":
                return PocoBench.Run(args.Skip(1).ToArray());
            case "report":
                return Report.Run(args.Skip(1).ToArray());
            case "versions":
                Console.Write(Versions.Describe());
                return 0;
            case "rows":
                foreach (string name in Rows.All.Keys.Order(StringComparer.Ordinal))
                {
                    Console.WriteLine(name);
                }

                foreach (string pending in Rows.Pending)
                {
                    Console.WriteLine("pending: " + pending);
                }

                return 0;
            default:
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
                return 0;
        }
    }

    /// <summary>gen &lt;directory&gt;: writes the standard inputs and a manifest with their sizes and SHA-256 hashes.</summary>
    private static int Gen(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: gen <directory>");
            return 2;
        }

        Directory.CreateDirectory(args[0]);
        using var manifest = new StreamWriter(Path.Combine(args[0], "manifest.tsv"));
        manifest.WriteLine("name\tbytes\tsha256");
        foreach (DocxSpec spec in DocxGenerator.Standard)
        {
            string path = Path.Combine(args[0], spec.Name + ".docx");
            string hash = DocxGenerator.Write(spec, path);
            manifest.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{spec.Name}\t{new FileInfo(path).Length}\t{hash}"));
            Console.WriteLine($"{spec.Name}: {new FileInfo(path).Length} bytes, sha256 {hash}");
        }

        return 0;
    }

    /// <summary>peak &lt;row&gt; &lt;file&gt;: runs one row once in this fresh process and prints one TSV line of memory figures.</summary>
    private static int Peak(string[] args)
    {
        if (args.Length != 2 || !Rows.All.TryGetValue(args[0], out Func<byte[], long>? row))
        {
            Console.Error.WriteLine("usage: peak <row> <file>   (rows: see the 'rows' command)");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(args[1]);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process self = Process.GetCurrentProcess();
        self.Refresh();
        long baseline = self.WorkingSet64;
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        long sink = row(bytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        self.Refresh();
        string input = Path.GetFileNameWithoutExtension(args[1]);
        // PeakWorkingSet64 is the process high-water mark (VmHWM on Linux); baseline is after loading the input bytes.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{args[0]}\t{input}\t{baseline}\t{self.PeakWorkingSet64}\t{allocated}\t{sink}"));
        return 0;
    }

    /// <summary>peakall &lt;inputsDir&gt; &lt;out.tsv&gt; [rowPrefix]: one fresh process per row and input, so peaks never mix.</summary>
    private static int PeakAll(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("usage: peakall <inputsDir> <out.tsv> [rowPrefix]");
            return 2;
        }

        Dictionary<string, string> inputs = DocxGenerator.Ensure(args[0]);
        using var output = new StreamWriter(args[1]);
        output.WriteLine("row\tinput\tbaseline_bytes\tpeak_bytes\tallocated_bytes\tresult");
        foreach (string row in Rows.All.Keys.Order(StringComparer.Ordinal).Where(r => args.Length < 3 || r.StartsWith(args[2], StringComparison.Ordinal)))
        {
            foreach ((string name, string path) in inputs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                int exit = ChildProcess.Run(["peak", row, path], out string line, out long peakMb);
                // Columns: row, input, baseline, peak (index 3), allocated, result. Where the runtime reports no peak, the external figure fills it.
                line = ChildProcess.WithPeak(line, 3, peakMb << 20);
                output.WriteLine(exit == 0 ? line : $"{row}\t{name}\tFAILED\tFAILED\tFAILED\texit {exit}");
                Console.WriteLine(exit == 0 ? line : $"{row} {name}: exit {exit}");
            }
        }

        return 0;
    }
}
