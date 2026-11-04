using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RecursiveAI.OneOffice.Tools.ViewGenerator;

/// <summary>
/// Reads the pinned OOXML schemas listed in schemas/views.manifest and writes the typed-view source committed under
/// src/OneOffice.Schema/Generated. The output is a pure function of the schemas, the two manifests and this program, so
/// running it twice changes nothing; --check compares instead of writing.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? root = null, schemas = null, manifest = null, names = null, output = null;
        var check = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root": root = args[++i]; break;
                case "--out": output = args[++i]; break;
                case "--check": check = true; break;
                default:
                    Console.Error.WriteLine("usage: ViewGenerator [--root <repo>] [--out <dir>] [--check]");
                    return 2;
            }
        }
        root ??= FindRoot();
        schemas = Path.Combine(root, "schemas");
        manifest = Path.Combine(schemas, "views.manifest");
        names = Path.Combine(schemas, "names.manifest");
        output ??= Path.Combine(root, "src", "OneOffice.Schema");

        var lib = new Library();
        lib.Load(schemas, Library.ReadManifest(manifest));
        var sem = new Semantics(lib);
        sem.BuildRoots();
        var em = new Emitter(lib, sem, names);
        em.Emit();
        em.Files["Generated/GenerationNotes.txt"] = Notes(lib, sem, em);

        var changed = 0;
        foreach (var (rel, text) in em.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(output, rel);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            changed++;
            if (check) { Console.Error.WriteLine("differs: " + rel); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        var gen = Path.Combine(output, "Generated");
        if (Directory.Exists(gen))
            foreach (var f in Directory.GetFiles(gen, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(output, f).Replace('\\', '/');
                if (em.Files.ContainsKey(rel)) continue;
                changed++;
                if (check) Console.Error.WriteLine("stale: " + rel); else File.Delete(f);
            }
        Console.WriteLine($"{em.Files.Count} files, {sem.TypeList.Count} views, {sem.Enums.Count} enums, {changed} {(check ? "differ" : "written")}");
        return check && changed > 0 ? 1 : 0;
    }

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "schemas", "views.manifest"))) return d.FullName;
        throw new InvalidOperationException("run inside the repository or pass --root");
    }

    private static string Notes(Library lib, Semantics sem, Emitter em)
    {
        var sb = new StringBuilder();
        sb.Append("Typed-view generation notes. Written by tools/ViewGenerator; do not edit.\n\n");
        sb.Append("views: ").Append(sem.TypeList.Count).Append("\nenumerations: ").Append(sem.Enums.Count).Append('\n');
        sb.Append("attributes: ").Append(sem.TypeList.Sum(t => t.Attrs.Count)).Append("\nchild slots: ").Append(sem.TypeList.Sum(t => t.Slots.Count)).Append("\n\n");
        sb.Append("Views per namespace:\n");
        foreach (var g in sem.TypeList.GroupBy(t => t.Doc.EnumName).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.Append("  ").Append(g.Key).Append(": ").Append(g.Count()).Append('\n');
        sb.Append("\nUnresolved or ambiguous references:\n");
        foreach (var n in sem.Notes.Distinct().OrderBy(n => n, StringComparer.Ordinal)) sb.Append("  ").Append(n).Append('\n');
        sb.Append("\nStrict differences (types in a Strict schema whose declared child or attribute names differ from the Transitional type):\n");
        foreach (var line in StrictDiff.Report(lib, sem)) sb.Append("  ").Append(line).Append('\n');
        return sb.ToString();
    }
}
