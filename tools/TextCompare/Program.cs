using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.TextExtraction;

namespace RecursiveAI.OneOffice.Tools.TextCompare;

/// <summary>
/// Produces the text files the similarity comparison reads, and measures peak memory.
///
///   TextCompare extract &lt;in dir&gt; &lt;out dir&gt; [--shard i/n]   for every .docx: &lt;name&gt;.ours.txt, &lt;name&gt;.sdk.txt (and &lt;name&gt;.ours.err when ours fails)
///   TextCompare peak &lt;file&gt;                              extract one file to a null writer and print the peak working set
///
/// The comparator's text is the raw concatenation of every w:t of the main document part, through its public API.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "extract")
        {
            return Extract(args);
        }

        if (args.Length == 2 && args[0] == "peak")
        {
            return Peak(args[1]);
        }

        Console.Error.WriteLine("usage: TextCompare extract <in dir> <out dir> [--shard i/n] | TextCompare peak <file>");
        return 2;
    }

    private static int Extract(string[] args)
    {
        int shard = 0;
        int shards = 1;
        for (int i = 3; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shard")
            {
                string[] p = args[i + 1].Split('/');
                shard = int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                shards = int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Directory.CreateDirectory(args[2]);
        string[] files = [.. Directory.EnumerateFiles(args[1], "*.docx").Order(StringComparer.Ordinal)];
        long oursTicks = 0;
        long sdkTicks = 0;
        int done = 0;
        for (int i = shard; i < files.Length; i += shards)
        {
            string name = Path.GetFileNameWithoutExtension(files[i]);
            byte[] bytes = File.ReadAllBytes(files[i]);
            var sw = Stopwatch.StartNew();
            try
            {
                using IByteSource source = ByteSource.FromMemory(bytes);
                File.WriteAllText(Path.Combine(args[2], name + ".fmt"), FormatSniffer.Detect(source).ToString());
                using var w = new StreamWriter(Path.Combine(args[2], name + ".ours.txt"), false, new UTF8Encoding(false));
                OoxmlTextExtractor.Extract(source, w);
            }
            catch (OfficeException ex)
            {
                File.WriteAllText(Path.Combine(args[2], name + ".ours.err"), ex.Defect.ToString());
            }
#pragma warning disable CA1031 // Anything but a typed rejection is a finding: record it and go on with the other files.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                File.WriteAllText(Path.Combine(args[2], name + ".ours.crash"), ex.ToString());
            }

            oursTicks += sw.ElapsedTicks;
            sw.Restart();
            try
            {
                using var input = new MemoryStream(bytes, writable: false);
                using WordprocessingDocument doc = WordprocessingDocument.Open(input, false);
                var sb = new StringBuilder();
                using DocumentFormat.OpenXml.OpenXmlReader reader = DocumentFormat.OpenXml.OpenXmlReader.Create(doc.MainDocumentPart!);
                while (reader.Read())
                {
                    if (reader.ElementType == typeof(DocumentFormat.OpenXml.Wordprocessing.Text) && reader.IsStartElement
                        && reader.LoadCurrentElement() is DocumentFormat.OpenXml.Wordprocessing.Text t)
                    {
                        sb.Append(t.Text);
                    }
                }

                File.WriteAllText(Path.Combine(args[2], name + ".sdk.txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or System.Xml.XmlException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
            {
                File.WriteAllText(Path.Combine(args[2], name + ".sdk.err"), ex.GetType().Name + ": " + ex.Message);
            }

            sdkTicks += sw.ElapsedTicks;
            done++;
        }

        Console.WriteLine($"{{\"files\":{done},\"oursMs\":{oursTicks * 1000 / Stopwatch.Frequency},\"sdkMs\":{sdkTicks * 1000 / Stopwatch.Frequency}}}");
        return 0;
    }

    private static int Peak(string path)
    {
        var sw = Stopwatch.StartNew();
        long chars = 0;
        using IByteSource source = ByteSource.FromFile(path);
        var counter = new CountingWriter();
        OoxmlTextExtractor.Extract(source, counter);
        chars = counter.Count;
        using Process me = Process.GetCurrentProcess();
        Console.WriteLine($"{{\"file\":\"{Path.GetFileName(path)}\",\"bytes\":{new FileInfo(path).Length},\"chars\":{chars},\"ms\":{sw.ElapsedMilliseconds},\"peakWorkingSetBytes\":{me.PeakWorkingSet64},\"gcHeapBytes\":{GC.GetTotalMemory(false)}}}");
        return 0;
    }

    private sealed class CountingWriter : TextWriter
    {
        public long Count { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => Count++;

        public override void Write(ReadOnlySpan<char> buffer) => Count += buffer.Length;

        public override void Write(string? value) => Count += value?.Length ?? 0;
    }
}
