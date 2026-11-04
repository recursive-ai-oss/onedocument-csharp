using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Packaging;
using IPackagePart = RecursiveAI.OneOffice.Packaging.IPackagePart;

namespace RecursiveAI.OneOffice.Tools.PackageTiming;

/// <summary>
/// Indicative timing of the package layer against the Open XML SDK on one file: open and enumerate, and open, touch the main
/// part and save to memory. Medians of N runs after a warm-up, with allocated bytes per run. It is a quick comparison, not
/// a benchmark: run it on an otherwise idle machine and label the numbers indicative.
///   PackageTiming file.docx [runs]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: PackageTiming file.docx [runs]");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(args[0]);
        int runs = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 30;
        Console.WriteLine($"{args[0]}: {bytes.Length / 1024} KiB, {runs} runs, median");
        Row("open + enumerate parts, ours", runs, () =>
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(bytes));
            int n = 0;
            foreach (IPackagePart part in p.Parts)
            {
                n += part.ContentType.Length;
            }

            return n;
        });
        Row("open + enumerate parts, Open XML SDK", runs, () =>
        {
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
            int n = 0;
            foreach (IdPartPair pair in doc.Parts)
            {
                n += pair.OpenXmlPart.ContentType.Length;
            }

            return n + doc.MainDocumentPart!.ContentType.Length;
        });
        Row("open + read every part, ours", runs, () =>
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(bytes));
            long n = 0;
            foreach (IPackagePart part in p.Parts)
            {
                n += part.GetBytes().Length;
            }

            return (int)n;
        });
        Row("open + save untouched to memory, ours", runs, () =>
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(bytes));
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            return (int)ms.Length;
        });
        Row("open + touch main part + save to memory, ours", runs, () =>
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(bytes));
            IPackagePart main = p.FindPart("/word/document.xml")!;
            main.SetBytes(main.GetBytes());
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            return (int)ms.Length;
        });
        Row("open + touch main part + save to memory, Open XML SDK", runs, () =>
        {
            using var ms = new MemoryStream();
            ms.Write(bytes);
            ms.Position = 0;
            using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
            {
                doc.MainDocumentPart!.Document!.Save();
            }

            return (int)ms.Length;
        });
        return 0;
    }

    private static void Row(string label, int runs, Func<int> action)
    {
        action();
        action();
        var times = new double[runs];
        long alloc = 0;
        for (int i = 0; i < runs; i++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        Array.Sort(times);
        Console.WriteLine($"{label,-62} {times[runs / 2],9:F3} ms   {alloc / runs / 1024,9} KiB allocated/run");
    }
}
