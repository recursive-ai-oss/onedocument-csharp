using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpFuzz;

namespace RecursiveAI.OneDocument.Fuzz;

// One fuzz executable, one target per parser. The target is chosen by the ONEDOCUMENT_FUZZ_TARGET environment variable because
// libFuzzer's .NET host starts the program without command-line arguments.
//
// To add a target: write a static method `Run(ReadOnlySpan<byte> data)` that lets only OfficeException escape (a typed rejection
// of the input is the correct outcome; anything else is a finding), add one line to `Targets` below, and a line for its maximum
// input length to run.sh.
//
// Without a libFuzzer host there are two local modes:
//   replay <target> <file>...          runs saved inputs once each (reproduces a finding)
//   seeds <target> <out dir>           writes starting inputs for a target (used by run.sh)
internal static class Program
{
    private delegate void Target(ReadOnlySpan<byte> data);

    private static readonly Dictionary<string, Target> Targets = new()
    {
        ["sniff"] = SniffTarget.Run,
        ["opc"] = OpcTarget.Run,
        ["cfb"] = CfbTarget.Run,
        ["xml"] = XmlTarget.RunTokenizer,
        ["markup"] = XmlTarget.RunTree,
        ["fieldinstr"] = FieldsTarget.RunInstruction,
        ["fields"] = FieldsTarget.RunHub,
        ["text"] = TextTarget.Run,
        ["revisions"] = RevisionTarget.Run,
        ["stories"] = StoriesTarget.Run,
        ["pptx"] = PptxTarget.Run,
        ["sdt"] = SdtTarget.RunDocument,
        ["sdtpath"] = SdtTarget.RunPath,
        ["numfmt"] = NumFmtTarget.RunParse,
        ["numfmtfmt"] = NumFmtTarget.RunFormat,
        ["sheet"] = SheetTarget.RunSheet,
        ["sst"] = SheetTarget.RunSst,
        ["wbpart"] = SheetTarget.RunWorkbookPart,
        ["stream"] = StreamTarget.RunRead,
        ["streamwrite"] = StreamTarget.RunWrite,
        ["csv"] = CsvTarget.Run,
        ["styles"] = StyleTarget.RunStyles,
        ["sheetfeat"] = StyleTarget.RunSheetFeatures,
        ["table"] = StyleTarget.RunTable,
        ["pivot"] = PivotTarget.Run,
        ["formula"] = FormulaTarget.RunParse,
        ["formulaeval"] = FormulaTarget.RunEval,
        ["finfuncs"] = FinFuncsTarget.Run,
        ["statfuncs"] = StatFuncsTarget.Run,
        ["funcs"] = FuncsTarget.Run,
        ["lookups"] = LookupTarget.Run,
        ["template"] = TemplateTarget.Run,
        ["mailmerge"] = MailMergeTarget.Run,
    };

    private static int Main(string[] args)
    {
        if (args.Length > 2 && args[0] == "replay")
        {
            return Replay(args);
        }

        if (args.Length > 2 && args[0] == "seeds")
        {
            return args[1] switch
            {
                "sniff" => SniffTarget.WriteSeeds(args[2]),
                "opc" => OpcTarget.WriteSeeds(args[2]),
                "cfb" => CfbTarget.WriteSeeds(args[2]),
                "xml" or "markup" => XmlTarget.WriteSeeds(args[2]),
                "fieldinstr" or "fields" => FieldsTarget.WriteSeeds(args[2]),
                "text" => TextTarget.WriteSeeds(args[2]),
                "revisions" => RevisionTarget.WriteSeeds(args[2]),
                "stories" => StoriesTarget.WriteSeeds(args[2]),
                "pptx" => PptxTarget.WriteSeeds(args[2]),
                "sdt" or "sdtpath" => SdtTarget.WriteSeeds(args[2]),
                "numfmt" or "numfmtfmt" => NumFmtTarget.WriteSeeds(args[2]),
                "sheet" or "sst" or "wbpart" => SheetTarget.WriteSeeds(args[2]),
                "stream" or "streamwrite" => StreamTarget.WriteSeeds(args[2]),
                "csv" => CsvTarget.WriteSeeds(args[2]),
                "styles" or "sheetfeat" or "table" => StyleTarget.WriteSeeds(args[2]),
                "pivot" => PivotTarget.WriteSeeds(args[2]),
                "formula" or "formulaeval" => FormulaTarget.WriteSeeds(args[2]),
                "finfuncs" => FinFuncsTarget.WriteSeeds(args[2]),
                "statfuncs" => StatFuncsTarget.WriteSeeds(args[2]),
                "funcs" => FuncsTarget.WriteSeeds(args[2]),
                "lookups" => LookupTarget.WriteSeeds(args[2]),
                "template" => TemplateTarget.WriteSeeds(args[2]),
                "mailmerge" => MailMergeTarget.WriteSeeds(args[2]),
                _ => 2,
            };
        }

        string? name = Environment.GetEnvironmentVariable("ONEDOCUMENT_FUZZ_TARGET") ?? (args.Length > 0 ? args[0] : null);
        if (name is null || !Targets.TryGetValue(name, out Target? target))
        {
            Console.Error.WriteLine("usage: set ONEDOCUMENT_FUZZ_TARGET to one of: " + string.Join(", ", Targets.Keys));
            return 2;
        }

        Fuzzer.LibFuzzer.Run(data => target(data));
        return 0;
    }

    private static int Replay(string[] args)
    {
        if (!Targets.TryGetValue(args[1], out Target? target))
        {
            Console.Error.WriteLine("unknown target " + args[1]);
            return 2;
        }

        int failures = 0;
        foreach (string f in args.Skip(2))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                target(File.ReadAllBytes(f));
                Console.WriteLine($"ok {f} ({sw.Elapsed.TotalSeconds:F2}s)");
            }
#pragma warning disable CA1031 // Replay reports any escaping exception as a failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures++;
                Console.WriteLine($"FAIL {f}: {ex}");
            }
        }

        return failures == 0 ? 0 : 1;
    }
}
