using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Cfb;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.PackageSweep;

/// <summary>
/// Sweeps files through the package layer and reports what held.
///   PackageSweep opc  path...          opens each OOXML package, reads every part, rewrites it untouched and edited, and compares per part
///   PackageSweep cfb  path...          the same for compound files (original bytes, then a rewrite after touching a stream)
///   PackageSweep resave outDir path... opens each OOXML package and writes it back untouched into outDir (for tools/RoundTrip to compare)
///   PackageSweep make-large file MB    writes a package of at least MB megabytes (mostly media, one large XML part)
///   PackageSweep open-cost file        opens a package and reports what the open touched
/// A path may be a directory (searched recursively) or a file. Exit code 0 when nothing failed.
/// </summary>
internal static class Program
{
    private static readonly string[] OpcExtensions = [".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlam", ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm", ".ppam", ".vsdx", ".xps"];
    private static readonly string[] CfbExtensions = [".doc", ".xls", ".ppt", ".dot", ".xlt", ".pot", ".pps", ".msg", ".vsd", ".wps"];

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PackageSweep opc|cfb <path>... | make-large <file> <MB> | open-cost <file>");
            return 2;
        }

        return args[0] switch
        {
            "opc" => Sweep(args[1..], OpcExtensions, RoundTripOpc),
            "cfb" => Sweep(args[1..], CfbExtensions, RoundTripCfb),
            "resave" => Resave(args[1], args[2..]),
            "make-large" => MakeLarge(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)),
            "open-cost" => OpenCost(args[1]),
            _ => 2,
        };
    }

    private static int Resave(string outDir, string[] paths)
    {
        Directory.CreateDirectory(outDir);
        int n = 0;
        foreach (string file in paths.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(f => OpcExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal) : new[] { p }))
        {
            try
            {
                using OpcPackage pkg = OpcPackage.Open(file);
                using FileStream output = File.Create(Path.Combine(outDir, Path.GetFileName(file)));
                pkg.WriteTo(output);
                n++;
            }
            catch (OfficeException ex)
            {
                Console.WriteLine($"REJECTED {Path.GetFileName(file)}: {ex.Defect}");
            }
        }

        Console.WriteLine($"resaved {n}");
        return 0;
    }

    private sealed class Tally
    {
        public int Tried;
        public int Opened;
        public int Identical;
        public int Rejected;
        public int Failed;
        public long Parts;
        public long PartsIdentical;
        public Dictionary<string, int> Codes = [];
    }

    private static int Sweep(string[] paths, string[] extensions, Func<string, Tally, string?> roundTrip)
    {
        var tally = new Tally();
        foreach (string path in paths)
        {
            IEnumerable<string> files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)
                : [path];
            foreach (string file in files)
            {
                tally.Tried++;
                string? problem;
#pragma warning disable CA1031 // A sweep exists to find the exceptions that are not typed; anything else escaping is a finding to report, not to crash on.
                try
                {
                    problem = roundTrip(file, tally);
                }
                catch (OfficeException ex)
                {
                    tally.Rejected++;
                    tally.Codes[ex.Defect.Code] = tally.Codes.GetValueOrDefault(ex.Defect.Code) + 1;
                    Console.WriteLine($"REJECTED {Path.GetFileName(file)}: {ex.Defect}");
                    continue;
                }
                catch (Exception ex)
                {
                    tally.Failed++;
                    Console.WriteLine($"UNTYPED  {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
#pragma warning restore CA1031

                if (problem is null)
                {
                    tally.Identical++;
                }
                else
                {
                    tally.Failed++;
                    Console.WriteLine($"DIFFERS  {Path.GetFileName(file)}: {problem}");
                }
            }
        }

        Console.WriteLine($"files tried {tally.Tried}, opened {tally.Opened}, round-tripped identical {tally.Identical}, rejected (typed) {tally.Rejected}, failed {tally.Failed}");
        Console.WriteLine($"parts compared {tally.Parts}, identical {tally.PartsIdentical}");
        foreach ((string code, int n) in tally.Codes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  rejected with {code}: {n}");
        }

        return tally.Failed == 0 ? 0 : 1;
    }

    private static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b));

    // Reads a zip with the BCL reader (an independent implementation), name -> SHA-256 of the decompressed bytes, in stored order.
    private static List<(string Name, string Hash)> IndependentRead(byte[] zip)
    {
        var list = new List<(string, string)>();
        using var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in za.Entries)
        {
            using Stream s = e.Open();
            list.Add((e.FullName, Convert.ToHexString(SHA256.HashData(s))));
        }

        return list;
    }

    private static string? RoundTripOpc(string file, Tally tally)
    {
        byte[] original = File.ReadAllBytes(file);
        var defects = new DefectCollection();
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(original), new OpenContext(OfficeLimits.Large, defects));
        tally.Opened++;
        var hashes = new Dictionary<string, string>();
        foreach (IPackagePart p in pkg.Parts)
        {
            hashes[p.Name] = Sha(p.GetBytes().Span);
        }

        var relCounts = new Dictionary<string, int> { [string.Empty] = pkg.GetRelationships().Count };
        foreach (IPackagePart p in pkg.Parts)
        {
            relCounts[p.Name] = pkg.GetRelationships(p.Name).Count;
        }

        // 1. Untouched rewrite: every stored item has the same decompressed bytes, in the same order.
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        List<(string Name, string Hash)> before = IndependentRead(original), after = IndependentRead(output.ToArray());
        if (!before.SequenceEqual(after))
        {
            return $"untouched rewrite differs ({before.Count} items before, {after.Count} after; first difference at {FirstDifference(before, after)})";
        }

        // 2. Reopen the rewrite: parts, content types and relationships read back the same.
        using OpcPackage back = OpcPackage.Open(new MemoryStream(output.ToArray()), new OpenContext(OfficeLimits.Large));
        if (back.Parts.Count != pkg.Parts.Count)
        {
            return "reopened part count differs";
        }

        foreach (IPackagePart p in back.Parts)
        {
            tally.Parts++;
            if (hashes.TryGetValue(p.Name, out string? h) && h == Sha(p.GetBytes().Span) && p.ContentType == pkg.FindPart(p.Name)!.ContentType && back.GetRelationships(p.Name).Count == relCounts[p.Name])
            {
                tally.PartsIdentical++;
            }
            else
            {
                return $"part {p.Name} differs after reopen";
            }
        }

        // 3. Edit one part: only it changes.
        if (pkg.Parts.Count > 1)
        {
            IPackagePart victim = pkg.Parts[pkg.Parts.Count / 2];
            byte[] edited = [.. victim.GetBytes().ToArray(), .. "\n"u8.ToArray()];
            victim.SetBytes(edited);
            using var output2 = new MemoryStream();
            pkg.WriteTo(output2);
            Dictionary<string, string> a = before.ToDictionary(x => x.Name, x => x.Hash), b = IndependentRead(output2.ToArray()).ToDictionary(x => x.Name, x => x.Hash);
            string victimItem = victim.Name[1..];
            foreach ((string name, string hash) in a)
            {
                if (!b.TryGetValue(name, out string? h2) || (name != victimItem && h2 != hash))
                {
                    return $"editing {victim.Name} changed {name}";
                }
            }

            if (b[victimItem] != Sha(edited))
            {
                return $"the edited part {victim.Name} did not keep its new bytes";
            }
        }

        // 4. In-place save through a file, then reopen.
        string temp = Path.Combine(Path.GetTempPath(), "sweep-" + Environment.ProcessId + Path.GetExtension(file));
        try
        {
            File.WriteAllBytes(temp, original);
            using (OpcPackage inPlace = OpcPackage.Open(temp, new OpenContext(OfficeLimits.Large)))
            {
                if (inPlace.Parts.Count > 0)
                {
                    inPlace.Parts[0].SetBytes(inPlace.Parts[0].GetBytes());
                }

                inPlace.Save();
                _ = inPlace.Parts.Count > 0 ? inPlace.Parts[^1].GetBytes() : default;
            }

            List<(string Name, string Hash)> saved = IndependentRead(File.ReadAllBytes(temp));
            if (!saved.SequenceEqual(before))
            {
                return "in-place save changed the content";
            }
        }
        finally
        {
            File.Delete(temp);
        }

        return null;
    }

    private static string FirstDifference(List<(string Name, string Hash)> a, List<(string Name, string Hash)> b)
    {
        for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            if (i >= a.Count || i >= b.Count || a[i] != b[i])
            {
                return i < a.Count ? a[i].Name : b[i].Name;
            }
        }

        return "?";
    }

    private static string? RoundTripCfb(string file, Tally tally)
    {
        byte[] original = File.ReadAllBytes(file);
        var defects = new DefectCollection();
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(original), new OpenContext(OfficeLimits.Large, defects));
        tally.Opened++;
        var hashes = new Dictionary<string, string>();
        foreach (IPackagePart p in pkg.Parts)
        {
            hashes[p.Name] = Sha(p.GetBytes().Span);
        }

        bool recovered = defects.Any(d => d.Code.StartsWith("packaging.cfb-", StringComparison.Ordinal));
        using var same = new MemoryStream();
        pkg.WriteTo(same);
        if (!recovered && !same.ToArray().AsSpan().SequenceEqual(original))
        {
            return "an unmodified package did not write the original bytes";
        }

        if (pkg.Parts.Count > 0)
        {
            pkg.Parts[0].SetBytes(pkg.Parts[0].GetBytes());
        }

        using var rebuilt = new MemoryStream();
        pkg.WriteTo(rebuilt);
        using CfbPackage back = CfbPackage.Open(new MemoryStream(rebuilt.ToArray()), new OpenContext(OfficeLimits.Large));
        if (back.Parts.Count != pkg.Parts.Count)
        {
            return $"rewrite changed the stream count from {pkg.Parts.Count} to {back.Parts.Count}";
        }

        foreach (IPackagePart p in back.Parts)
        {
            tally.Parts++;
            if (hashes.TryGetValue(p.Name, out string? h) && h == Sha(p.GetBytes().Span))
            {
                tally.PartsIdentical++;
            }
            else
            {
                return $"stream {p.Name} differs after the rewrite";
            }
        }

        return null;
    }

    private static int MakeLarge(string path, int megabytes)
    {
        var rng = new Random(42);
        byte[] chunk = new byte[1 << 20];
        const string Doc = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        using FileStream fs = File.Create(path);
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Text(string name, string text)
            {
                using Stream s = za.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(Encoding.UTF8.GetBytes(text));
            }

            Text("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"bin\" ContentType=\"application/octet-stream\"/>" + $"<Override PartName=\"/word/document.xml\" ContentType=\"{Doc}\"/></Types>");
            Text("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");

            // One large XML part of base64 text (about 1.3:1 under deflate), then media-like random parts of 8 MB each.
            long target = megabytes * 1024L * 1024L;
            long written = 0;
            using (Stream s = za.CreateEntry("word/document.xml", CompressionLevel.Fastest).Open())
            {
                s.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"u8);
                byte[] raw = new byte[3 << 19];
                for (int i = 0; i < 100; i++)
                {
                    rng.NextBytes(raw);
                    s.Write(Encoding.ASCII.GetBytes("<w:p><w:r><w:t>" + Convert.ToBase64String(raw) + "</w:t></w:r></w:p>"));
                    written += (3 << 19) * 4 / 3;
                }

                s.Write("</w:body></w:document>"u8);
            }

            for (int i = 0; written < target; i++)
            {
                using Stream s = za.CreateEntry($"word/media/blob{i:D4}.bin", CompressionLevel.NoCompression).Open();
                for (int k = 0; k < 8; k++)
                {
                    rng.NextBytes(chunk);
                    s.Write(chunk);
                    written += chunk.Length;
                }
            }
        }

        Console.WriteLine($"{path}: {new FileInfo(path).Length / (1024 * 1024)} MB");
        return 0;
    }

    private sealed class CountingSource(IByteSource inner) : IByteSource
    {
        public long BytesRead { get; private set; }

        public int Reads { get; private set; }

        public long Length => inner.Length;

        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination)
        {
            int n = inner.Read(offset, destination);
            BytesRead += n;
            Reads++;
            return n;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) => inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory) => inner.TryGetMemory(out memory);

        public void Dispose() => inner.Dispose();
    }

    private static int OpenCost(string file)
    {
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var counting = new CountingSource(ByteSource.FromFile(file));
        using OpcPackage pkg = OpcPackage.Open(counting, new OpenContext(OfficeLimits.Large));
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Console.WriteLine($"file {counting.Length / (1024 * 1024)} MB, {pkg.Parts.Count} parts");
        Console.WriteLine($"open: {sw.ElapsedMilliseconds} ms, {counting.BytesRead / 1024} KiB read in {counting.Reads} reads ({100.0 * counting.BytesRead / counting.Length:F3}% of the file), {allocated / 1024} KiB allocated, parts whose content was read: {pkg.LoadedPartCount}");
        long beforeRead = counting.BytesRead;
        IPackagePart small = pkg.Parts.First(p => p.Name.EndsWith("document.xml", StringComparison.Ordinal) || p.Length < 1 << 20);
        sw.Restart();
        _ = small.GetBytes();
        Console.WriteLine($"reading {small.Name} ({small.Length / 1024} KiB): {sw.ElapsedMilliseconds} ms, {(counting.BytesRead - beforeRead) / 1024} KiB more read from the file, parts whose content was read: {pkg.LoadedPartCount}");
        Console.WriteLine($"peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
        return 0;
    }
}
