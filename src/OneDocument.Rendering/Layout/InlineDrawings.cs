using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

// What painting a document's drawings needs from the document: the theme, the colour map and the bytes of embedded pictures.
internal sealed class DrawingSources
{
    private readonly Dictionary<string, byte[]?> _bytes = new(StringComparer.Ordinal);
    private readonly Document _doc;
    private readonly IReadOnlyDictionary<string, Relationship> _relationships;

    private DrawingSources(Document doc, IReadOnlyDictionary<string, Relationship> relationships, DrawingTheme theme, ColorMap map)
    {
        _doc = doc;
        _relationships = relationships;
        Theme = theme;
        Map = map;
        Scope = doc.MainPartName;
    }

    public DrawingTheme Theme { get; }

    public ColorMap Map { get; }

    public string Scope { get; }

    // The layout the drawings belong to; shape text is laid out with it.
    public LayoutContext? Layout { get; set; }

    public static DrawingSources Create(Document doc, IReadOnlyDictionary<string, Relationship> relationships)
    {
        DrawingTheme theme = DrawingTheme.Default;
        (MarkupDocument? themeDoc, _) = Xml.LoadRelated(doc, "theme");
        if (themeDoc is not null)
        {
            theme = DrawingTheme.Read(themeDoc.Root);
        }

        ColorMap map = ColorMap.Default;
        (MarkupDocument? settings, _) = Xml.LoadRelated(doc, "settings");
        if (settings is not null)
        {
            MarkupNode mapping = DrawingTheme.Child(settings.Root, "clrSchemeMapping");
            if (!mapping.IsNull)
            {
                map = ColorMap.Read(mapping);
            }
        }

        return new DrawingSources(doc, relationships, theme, map);
    }

    // The chart a relationship id of the main part names, or null when it is missing or is not a chart.
    public RecursiveAI.OneDocument.Charts.ChartPart? Chart(string relationshipId) =>
        RecursiveAI.OneDocument.Charts.ChartHost.Load(_doc.Package, _doc.MainPartName, relationshipId);

    // The SmartArt painter for frames in the main part.
    public Func<RecursiveAI.OneDocument.Rendering.SmartArt.DiagramPaintRequest, bool> DiagramPainter(RecursiveAI.OneDocument.Rendering.SmartArt.DiagramOptions? options) =>
        RecursiveAI.OneDocument.Rendering.SmartArt.DiagramHost.CreatePainter(_doc.Package, _doc.MainPartName, options);

    // The bytes of the part a relationship id of the main part names; external (linked) targets are never opened.
    public byte[]? ImageBytes(string relationshipId)
    {
        if (_bytes.TryGetValue(relationshipId, out byte[]? cached))
        {
            return cached;
        }

        byte[]? bytes = null;
        if (_relationships.TryGetValue(relationshipId, out Relationship? rel) && OpcPackage.TryResolveTarget(_doc.MainPartName, rel, out PartUri target) && _doc.Package.FindPart(target.Value) is { } part)
        {
            bytes = part.GetBytes().ToArray();
        }

        _bytes[relationshipId] = bytes;
        return bytes;
    }
}

// A drawing in a paragraph: the w:drawing element, what it says about itself, and where its pictures come from.
internal sealed class InlineDrawing(MarkupNode node, DrawingInfo info, DrawingSources sources)
{
    public MarkupNode Node { get; } = node;

    public DrawingInfo Info { get; } = info;

    public DrawingSources Sources { get; } = sources;

    // Where in the paragraph text the anchor sits (an anchored drawing takes no character).
    public int TextOffset { get; set; }

    // Set once layout has placed the drawing on a page; the painter then draws it from the page's floats.
    public bool Placed { get; set; }
}
