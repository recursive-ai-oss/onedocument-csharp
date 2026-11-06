using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.SmartArt;

namespace RecursiveAI.OneDocument.Tools.SmartArtProbe;

/// <summary>Prints, for each diagram of one file, the stored shapes next to the shapes the engine makes. Usage: SmartArtProbe dump &lt;file&gt; [layoutId].</summary>
internal static class Dump
{
    public static int Run(string[] args)
    {
        if (!OpcPackage.TryOpen(args[1], out OpcPackage? package, null) || package is null)
        {
            Console.Error.WriteLine("cannot open");
            return 1;
        }

        using (package)
        {
            foreach (DiagramLocation loc in DiagramHost.Find(package))
            {
                IReadOnlyList<DiagramShape>? stored = DiagramHost.ReadStored(package, loc.HostPart, loc.References);
                double w = loc.WidthPoints ?? 480, h = loc.HeightPoints ?? 360;
                if (w < 20 && stored is { Count: > 0 })
                {
                    w = stored.Max(s => s.X + s.Width);
                    h = stored.Max(s => s.Y + s.Height);
                }

                DiagramLayoutResult? made = DiagramHost.Generate(package, loc.HostPart, loc.References, w, h);
                Console.WriteLine($"== {loc.HostPart} {loc.References.Data} frame {w:0.#}x{h:0.#}");
                Console.WriteLine("-- stored");
                foreach (DiagramShape s in stored ?? [])
                {
                    Console.WriteLine($"  {s.Geometry,-14} x={s.X,7:0.0} y={s.Y,7:0.0} w={s.Width,7:0.0} h={s.Height,7:0.0} rot={s.Rotation,5:0} f={s.FontSize,4:0} '{s.Text.Replace('\n', '|')}'");
                }

                if (Environment.GetEnvironmentVariable("OUTLINE") is { Length: > 0 })
                {
                    Console.WriteLine(made?.Outline ?? "(no layout)");
                }

                Console.WriteLine("-- made");
                foreach (DiagramShape s in made?.Shapes ?? [])
                {
                    Console.WriteLine($"  {s.Geometry,-14} x={s.X,7:0.0} y={s.Y,7:0.0} w={s.Width,7:0.0} h={s.Height,7:0.0} rot={s.Rotation,5:0} f={s.FontSize,4:0} '{s.Text.Replace('\n', '|')}' [{s.Name}/{s.StyleLabel}]");
                }
            }
        }

        return 0;
    }
}
