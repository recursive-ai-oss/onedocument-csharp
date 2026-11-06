using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Tools.DocProbe;

/// <summary>
/// Opens every Word binary file in a directory and records what happened, for the corpus read and the text comparison.
///
///   DocProbe read &lt;in dir&gt; &lt;out dir&gt; [--shard i/n] [--hub]
///
/// Per file it writes &lt;name&gt;.ours.txt (the text extractor), with --hub also &lt;name&gt;.hub.txt (text of the converted document), and one
/// line to &lt;out dir&gt;/summary.tsv: name, status, defect codes, characters, milliseconds. A typed failure is a status, never a crash.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "open")
        {
            DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
            var d = new DefectCollection();
            try
            {
                using IByteSource src = ByteSource.FromFile(args[1]);
                var f = RecursiveAI.OneDocument.Doc.Binary.DocFile.Open(src, new OpenContext(defects: d));
                Console.WriteLine($"nFib={f.Fib.NFib:X} ident={f.Fib.WIdent:X} legacy={f.Fib.IsLegacyWord6} complex={f.Fib.IsComplex} pieces={f.Pieces.Pieces.Count} text={f.Text.Length} ccp={f.Fib.CcpText},{f.Fib.CcpFtn},{f.Fib.CcpHdd},{f.Fib.CcpAtn},{f.Fib.CcpEdn},{f.Fib.CcpTxbx},{f.Fib.CcpHdrTxbx} streams={string.Join(",", f.StreamNames)}");
            }
            catch (OfficeException ex)
            {
                Console.WriteLine(ex.Defect);
            }

            foreach (Defect x in d)
            {
                Console.WriteLine(x);
            }

            return 0;
        }

        if (args.Length >= 3 && args[0] == "convert")
        {
            // convert <in dir> <out dir> [--limit n] [--stride k]: every k-th .doc to .docx through the model; one line per file to out/convert.tsv.
            DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
            int limit = int.MaxValue;
            int stride = 1;
            for (int i = 3; i + 1 < args.Length; i++)
            {
                if (args[i] == "--limit")
                {
                    limit = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                }
                else if (args[i] == "--stride")
                {
                    stride = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                }
            }

            Directory.CreateDirectory(args[2]);
            using var log = new StreamWriter(Path.Combine(args[2], "convert.tsv"), false, new UTF8Encoding(false));
            using var sprmLog = new StreamWriter(Path.Combine(args[2], "unmapped.tsv"), false, new UTF8Encoding(false));
            int done = 0;
            foreach (string file in Directory.GetFiles(args[1], "*.doc").Order(StringComparer.Ordinal).Where((_, i) => i % stride == 0))
            {
                if (done++ >= limit)
                {
                    break;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                var defects = new DefectCollection();
                try
                {
                    using Document doc = Document.Open(file, new OpenContext(defects: defects));
                    doc.SaveAs(Path.Combine(args[2], name + ".docx"));
                    foreach (Defect x in defects.Where(d => d.Code == "doc.sprm-not-mapped" || d.Code == "doc.picture-unreadable"))
                    {
                        sprmLog.WriteLine(name + "\t" + x.Code + "\t" + x.Message);
                    }
                    string loss = doc.ImportLoss is { } l ? string.Join(',', l.Items.Select(x => x.Code + "=" + x.Count)) : "-";
                    log.WriteLine(string.Join('\t', name, "ok", loss, string.Join(',', defects.Select(d => d.Code).Distinct().Order(StringComparer.Ordinal))));
                }
                catch (OfficeException ex)
                {
                    log.WriteLine(string.Join('\t', name, ex.Defect.Code, "-", "-"));
                }
                catch (Exception ex)
                {
                    log.WriteLine(string.Join('\t', name, "UNTYPED:" + ex.GetType().Name + ":" + ex.Message.Replace('\t', ' ').Replace('\n', ' '), "-", "-"));
                }
            }

            return 0;
        }

        if (args.Length < 3 || args[0] != "read")
        {
            Console.Error.WriteLine("usage: DocProbe read <in dir> <out dir> [--shard i/n] [--hub]");
            return 2;
        }

        DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        int shard = 0;
        int shards = 1;
        bool hub = false;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--hub")
            {
                hub = true;
            }
            else if (args[i] == "--shard" && i + 1 < args.Length)
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0], CultureInfo.InvariantCulture);
                shards = int.Parse(p[1], CultureInfo.InvariantCulture);
            }
        }

        Directory.CreateDirectory(args[2]);
        string[] files = Directory.GetFiles(args[1], "*.doc").Order(StringComparer.Ordinal).Where((_, i) => i % shards == shard).ToArray();
        using var summary = new StreamWriter(Path.Combine(args[2], shards == 1 ? "summary.tsv" : $"summary-{shard}.tsv"), false, new UTF8Encoding(false));
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var defects = new DefectCollection();
            var ctx = new OpenContext(defects: defects);
            var sw = Stopwatch.StartNew();
            string status = "ok";
            int chars = -1;
            try
            {
                string text = Office.ExtractText(file, ctx);
                chars = text.Length;
                File.WriteAllText(Path.Combine(args[2], name + ".ours.txt"), text, new UTF8Encoding(false));
            }
            catch (OfficeException ex)
            {
                status = ex.Defect.Code;
            }
            catch (Exception ex)
            {
                status = "UNTYPED:" + ex.GetType().Name + ":" + ex.Message.Replace('\t', ' ').Replace('\n', ' ');
            }

            string hubStatus = "-";
            if (hub)
            {
                try
                {
                    using Document doc = Document.Open(file, new OpenContext());
                    File.WriteAllText(Path.Combine(args[2], name + ".hub.txt"), doc.ExtractText(), new UTF8Encoding(false));
                    hubStatus = "ok";
                }
                catch (OfficeException ex)
                {
                    hubStatus = ex.Defect.Code;
                }
                catch (Exception ex)
                {
                    hubStatus = "UNTYPED:" + ex.GetType().Name + ":" + ex.Message.Replace('\t', ' ').Replace('\n', ' ');
                }
            }

            string codes = string.Join(',', defects.Select(d => d.Code).Distinct().Order(StringComparer.Ordinal));
            summary.WriteLine(string.Join('\t', name, status, hubStatus, codes, chars.ToString(CultureInfo.InvariantCulture), sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)));
        }

        return 0;
    }
}
