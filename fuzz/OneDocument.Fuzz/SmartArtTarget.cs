using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.SmartArt;
using RecursiveAI.OneDocument.Rendering.Tests;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// SmartArt over arbitrary bytes. After one option byte (bits 0-1 pick the frame size) the input is split at zero bytes into the data
/// model, layout definition, colour transform, quick style and stored drawing of a diagram in a slide. The layout engine and the painter
/// must never throw, whatever the definitions say, and every shape must have finite coordinates.
/// </summary>
internal static class SmartArtTarget
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32 };
    private static readonly (double W, double H)[] s_sizes = [(432, 288), (40, 25), (900, 60), (6, 6)];
    private static FontLibrary? s_fonts;

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        (double w, double h) = s_sizes[data[0] & 3];
        var pieces = new List<byte[]>();
        byte[] all = data[1..].ToArray();
        int start = 0;
        for (int i = 0; i < all.Length && pieces.Count < 4; i++)
        {
            if (all[i] == 0)
            {
                pieces.Add(all[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(all[start..]);
        while (pieces.Count < 5)
        {
            pieces.Add([]);
        }

        var p = new InMemoryPackage(s_limits);
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "ppt/presentation.xml")]);
        p.AddPart("/ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", Encoding.UTF8.GetBytes("<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>"));
        p.AddPart("/ppt/slides/slide1.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml", Encoding.UTF8.GetBytes("<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>"));
        p.AddPart("/ppt/diagrams/data1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml", pieces[0]);
        p.AddPart("/ppt/diagrams/layout1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml", pieces[1]);
        p.AddPart("/ppt/diagrams/colors1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml", pieces[2]);
        p.AddPart("/ppt/diagrams/quickStyle1.xml", "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml", pieces[3]);
        p.AddPart("/ppt/diagrams/drawing1.xml", "application/vnd.ms-office.drawingml.diagramDrawing+xml", pieces[4]);
        p.SetRelationships("/ppt/slides/slide1.xml",
        [
            new Relationship("rIdD1", Rel + "diagramData", "../diagrams/data1.xml"),
            new Relationship("rIdL1", Rel + "diagramLayout", "../diagrams/layout1.xml"),
            new Relationship("rIdC1", Rel + "diagramColors", "../diagrams/colors1.xml"),
            new Relationship("rIdQ1", Rel + "diagramQuickStyle", "../diagrams/quickStyle1.xml"),
            new Relationship("rIdX1", "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", "../diagrams/drawing1.xml"),
        ]);

        s_fonts ??= FontLibrary.CreateDefault();
        var refs = new DiagramReferences("rIdD1", "rIdL1", "rIdQ1", "rIdC1");
        DiagramLayoutResult? made = DiagramHost.Generate(p, "/ppt/slides/slide1.xml", refs, w, h, fonts: s_fonts);
        if (made is not null)
        {
            foreach (DiagramShape s in made.Shapes)
            {
                if (!double.IsFinite(s.X) || !double.IsFinite(s.Y) || !double.IsFinite(s.Width) || !double.IsFinite(s.Height) || !double.IsFinite(s.FontSize))
                {
                    throw new InvalidOperationException("non-finite diagram shape");
                }
            }
        }

        _ = DiagramHost.ReadStored(p, "/ppt/slides/slide1.xml", refs);
        foreach (DiagramRegeneration mode in new[] { DiagramRegeneration.WhenMissingOrStale, DiagramRegeneration.Always })
        {
            var list = new DisplayList();
            uint n = 0;
            var ctx = new DrawingContext { Fonts = s_fonts };
            _ = DiagramHost.Paint(p, "/ppt/slides/slide1.xml", new DiagramPaintRequest(refs, list, () => ItemId.Of(++n), ctx, 0, 0, w * 4 / 3, h * 4 / 3, null, false), new DiagramOptions { Regeneration = mode });
            list.Validate();
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        int n = 0;
        foreach ((string name, string family, SmartArtSamples.Node[] roots, bool _) in SmartArtSamples.Cases())
        {
            byte[] sep = [0];
            using var ms = new MemoryStream();
            ms.WriteByte(0);
            foreach (string part in new[] { SmartArtSamples.DataModel(roots), SmartArtSamples.LayoutFor(family), SmartArtSamples.Colors(), SmartArtSamples.QuickStyle() })
            {
                ms.Write(Encoding.UTF8.GetBytes(part));
                ms.Write(sep);
            }

            ms.Write(Encoding.UTF8.GetBytes(SmartArtSamples.CacheFor(family, roots, 432, 288)));
            File.WriteAllBytes(Path.Combine(directory, "seed-" + name), ms.ToArray());
            n++;
        }

        return n > 0 ? 0 : 1;
    }
}
