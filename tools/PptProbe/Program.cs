using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Presentations;

// PptProbe: opens PowerPoint 97-2003 files through the presentation hub and reports what came out.
//   PptProbe open <file|dir>...        one line per file: slides, shapes, text length, defect and loss counts, or the typed exception
//   PptProbe convert <file|dir>... <outdir>  writes each file as PresentationML
//   PptProbe dump <file.ppt> [out.pptx] the text of every slide, the loss report, the defects; optionally the converted package
//   PptProbe text <file>...            the extracted text only (for comparing with another extractor)
RecursiveAI.OneDocument.Presentations.PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: PptProbe open|dump|text <file>...");
        return 2;
    }

    switch (args[0])
    {
        case "open":
            return Open(Expand(args[1..]));
        case "text":
            foreach (string f in Expand(args[1..]))
            {
                try
                {
                    Console.Write(Office.ExtractText(f));
                }
                catch (OfficeException ex)
                {
                    Console.Error.WriteLine(f + ": " + ex.Defect.Code);
                }
            }

            return 0;
        case "streams":
            // streams <file>: the compound file's stream names and sizes, or why it cannot be listed
            foreach (string f in Expand(args[1..]))
            {
                try
                {
                    using RecursiveAI.OneDocument.Cfb.CfbPackage? cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(f);
                    Console.WriteLine(f + ": " + string.Join(", ", cfb.Streams.Select(x => x.Name + "(" + x.Length + ")")));
                }
                catch (OfficeException ex)
                {
                    Console.WriteLine(f + ": " + ex.Defect);
                }
            }

            return 0;
        case "textdir":
            // textdir <dir|file>... <outdir>: the extracted text of each file into outdir/<name>.txt
            Directory.CreateDirectory(args[^1]);
            foreach (string f in Expand(args[1..^1]))
            {
                try
                {
                    File.WriteAllText(Path.Combine(args[^1], Path.GetFileNameWithoutExtension(f) + ".txt"), Office.ExtractText(f));
                }
                catch (OfficeException)
                {
                }
            }

            return 0;
        case "slim":
            // slim <in.ppt> <out.ppt>: keeps the Current User, PowerPoint Document and Pictures streams byte for byte and drops the rest
            // (thumbnails and summary information), so converter output can be committed as a small fixture
            using (RecursiveAI.OneDocument.Cfb.CfbPackage cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(args[1]))
            {
                foreach (string name in cfb.Streams.Select(x => x.Name).Where(n => n is not ("/Current User" or "/PowerPoint Document" or "/Pictures")).ToList())
                {
                    cfb.RemovePart(name);
                }

                cfb.SaveAs(args[2]);
            }

            return 0;
        case "convert":
            // convert <dir|file>... <outdir>: writes each file as PresentationML into outdir; one status line per file
            return Convert(Expand(args[1..^1]), args[^1]);
        case "dump":
            return Dump(args[1], args.Length > 2 ? args[2] : null);
        default:
            return 2;
    }
}

static IEnumerable<string> Expand(IEnumerable<string> paths)
{
    foreach (string p in paths)
    {
        if (Directory.Exists(p))
        {
            foreach (string f in Directory.EnumerateFiles(p, "*.ppt").OrderBy(x => x, StringComparer.Ordinal))
            {
                yield return f;
            }
        }
        else
        {
            yield return p;
        }
    }
}

static int Open(IEnumerable<string> files)
{
    int ok = 0;
    int typed = 0;
    int untyped = 0;
    var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var messages = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (string file in files)
    {
        try
        {
            using Presentation p = Presentation.Open(file);
            int shapes = 0;
            int chars = 0;
            foreach (Slide s in p.Slides)
            {
                chars += s.Text.Length;
                shapes += s.Shapes.Count;
            }

            foreach (Defect d in p.Defects)
            {
                codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
                if (d.Code is "ppt.shape-approximated" or "ppt.path-escape" or "ppt.transition-dropped" or "ppt.fill-approximated")
                {
                    messages[d.Message] = messages.GetValueOrDefault(d.Message) + 1;
                }
            }

            Console.WriteLine($"{Path.GetFileName(file)}\tok\tslides={p.Slides.Count}\tshapes={shapes}\tchars={chars}\tdefects={p.Defects.Count}\tloss={p.ConversionLoss?.Items.Count ?? 0}");
            ok++;
        }
        catch (OfficeException ex)
        {
            Console.WriteLine($"{Path.GetFileName(file)}\ttyped\t{ex.Defect.Code}");
            codes[ex.Defect.Code] = codes.GetValueOrDefault(ex.Defect.Code) + 1;
            typed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Path.GetFileName(file)}\tUNTYPED\t{ex.GetType().Name}: {ex.Message}");
            untyped++;
        }
    }

    Console.WriteLine($"opened={ok} typed={typed} untyped={untyped}");
    foreach ((string code, int n) in codes)
    {
        Console.WriteLine($"  {code}\t{n}");
    }

    foreach ((string message, int n) in messages.OrderByDescending(m => m.Value).Take(25))
    {
        Console.WriteLine($"  [{n}] {message}");
    }

    return untyped == 0 ? 0 : 1;
}

static int Dump(string file, string? output)
{
    using Presentation p = Presentation.Open(file);
    for (int i = 0; i < p.Slides.Count; i++)
    {
        Console.WriteLine($"--- slide {i + 1} layout={p.Slides[i].Layout?.Name}");
        Console.WriteLine(p.Slides[i].Text);
    }

    Console.WriteLine("--- loss");
    Console.WriteLine(p.ConversionLoss);
    Console.WriteLine("--- defects");
    foreach (Defect d in p.Defects.Take(60))
    {
        Console.WriteLine(d);
    }

    if (output is not null)
    {
        p.SaveAs(output);
    }

    return 0;
}

static int Convert(IEnumerable<string> files, string outDir)
{
    Directory.CreateDirectory(outDir);
    int ok = 0;
    int failed = 0;
    foreach (string file in files)
    {
        try
        {
            using Presentation p = Presentation.Open(file);
            p.SaveAs(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".pptx"));
            ok++;
        }
        catch (Exception ex) when (ex is OfficeException)
        {
            Console.WriteLine($"{Path.GetFileName(file)}\ttyped\t{((OfficeException)ex).Defect.Code}");
            failed++;
        }
    }

    Console.WriteLine($"converted={ok} typed={failed}");
    return 0;
}
