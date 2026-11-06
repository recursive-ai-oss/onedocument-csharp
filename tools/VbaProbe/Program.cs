using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Vba;

// VbaProbe: reads and removes VBA projects. Nothing in a project is ever run.
//   VbaProbe scan <file|dir>...           one JSON line per file that holds a project: container, modules, signed, locked, p-code and
//                                         stomping flags, defect codes; files without one are counted in the last line
//   VbaProbe list <file>                  the modules of one file, with kind, size and first source line
//   VbaProbe extract <file> <outdir>      writes every module as .bas/.cls/.frm
//   VbaProbe remove <file> <out>          writes a copy without the project and prints the report
RecursiveAI.OneDocument.Presentations.PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: VbaProbe scan|list|extract|remove <file>...");
        return 2;
    }

    switch (args[0])
    {
        case "scan":
            return Scan(Expand(args[1..]));
        case "list":
            return List(args[1]);
        case "extract" when args.Length == 3:
            {
                VbaProject? p = VbaProject.Open(args[1]);
                if (p is null)
                {
                    Console.WriteLine("no VBA project");
                    return 1;
                }

                foreach (string f in p.ExportModules(args[2]))
                {
                    Console.WriteLine(f);
                }

                return 0;
            }

        case "remove" when args.Length == 3:
            {
                VbaRemovalReport r = VbaRemoval.Remove(args[1], args[2]);
                Console.WriteLine(r.SourceFormat + " -> " + r.OutputFormat + ", project: " + r.HadProject + ", modules: " + r.ModulesRemoved + ", bytes removed: " + r.BytesRemoved + ", signatures: " + r.SignaturesRemoved);
                foreach (VbaRemovedItem i in r.Removed)
                {
                    Console.WriteLine("  removed " + i.Kind + " " + i.Location + " (" + i.Bytes + " bytes): " + i.Description);
                }

                foreach (VbaRemovalNote n in r.NotDone)
                {
                    Console.WriteLine("  left " + n.Code + ": " + n.Message);
                }

                return 0;
            }

        default:
            Console.Error.WriteLine("usage: VbaProbe scan|list|extract|remove <file>...");
            return 2;
    }
}

static int List(string file)
{
    var defects = new DefectCollection();
    VbaProject? p = VbaProject.Open(file, new OpenContext(defects: defects));
    if (p is null)
    {
        Console.WriteLine("no VBA project");
        return 1;
    }

    Console.WriteLine(p + "; code page " + p.CodePage + "; signed " + p.Signatures + "; locked " + p.IsLocked + "; p-code " + p.HasPCode + "; stomping flag " + p.SourceAndPCodeMayDisagree);
    foreach (VbaReference r in p.References)
    {
        Console.WriteLine("  reference " + r.Kind + " " + r.Name + " " + r.Libid);
    }

    foreach (VbaModule m in p.Modules)
    {
        string first = m.Source.Split('\n')[0].Trim();
        Console.WriteLine("  " + m.Kind + " " + m.Name + " (" + m.SourceBytes.Length + " bytes, cache " + m.TextOffset + ", available " + m.SourceAvailable + "): " + first);
    }

    foreach (Defect d in defects)
    {
        Console.WriteLine("  defect " + d);
    }

    return 0;
}

static int Scan(IEnumerable<string> files)
{
    int with = 0, without = 0, failed = 0;
    foreach (string f in files)
    {
        var defects = new DefectCollection();
        try
        {
            VbaProject? p = VbaProject.Open(f, new OpenContext(defects: defects));
            if (p is null)
            {
                without++;
                continue;
            }

            with++;
            var sb = new StringBuilder();
            sb.Append("{\"file\":").Append(Json(f)).Append(",\"container\":").Append(Json(p.Container))
              .Append(",\"modules\":").Append(p.Modules.Count)
              .Append(",\"withSource\":").Append(p.Modules.Count(m => m.SourceAvailable))
              .Append(",\"sourceChars\":").Append(p.Modules.Sum(m => (long)m.SourceBytes.Length))
              .Append(",\"references\":").Append(p.References.Count)
              .Append(",\"forms\":").Append(p.DesignerStorages.Count)
              .Append(",\"signed\":").Append(Json(p.Signatures.ToString()))
              .Append(",\"locked\":").Append(p.IsLocked ? "true" : "false")
              .Append(",\"pcode\":").Append(p.HasPCode ? "true" : "false")
              .Append(",\"stomped\":").Append(p.SourceAndPCodeMayDisagree ? "true" : "false")
              .Append(",\"defects\":[").Append(string.Join(",", defects.Select(d => Json(d.Code)).Distinct())).Append("]}");
            Console.WriteLine(sb);
        }
        catch (OfficeException ex)
        {
            failed++;
            Console.WriteLine("{\"file\":" + Json(f) + ",\"error\":" + Json(ex.Defect.Code) + "}");
        }
    }

    Console.WriteLine("{\"summary\":{\"withProject\":" + with + ",\"withoutProject\":" + without + ",\"unreadable\":" + failed + "}}");
    return 0;
}

static string Json(string s)
{
    var sb = new StringBuilder("\"");
    foreach (char c in s)
    {
        sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", < ' ' => "\\u" + ((int)c).ToString("X4"), _ => c.ToString() });
    }

    return sb.Append('"').ToString();
}

static IEnumerable<string> Expand(string[] paths)
{
    foreach (string p in paths)
    {
        if (Directory.Exists(p))
        {
            foreach (string f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
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
