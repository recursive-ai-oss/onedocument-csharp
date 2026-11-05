using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

internal sealed partial class RenderSession
{
    // SmartArt: the slide keeps a drawing part the application saved with the diagram (the "cached drawing"); it holds ordinary shapes in
    // the diagram's own space. The data part names it in an extension (dsp:dataModelExt relId) by a relationship id of the slide.
    // Regenerating a diagram from its layout definition is not done here.
    private bool PaintDiagram(PartContext pc, MarkupNode frame, MarkupNode data, DisplayList list, double x, double y, double w, double h)
    {
        string uri = data.GetAttribute("uri") ?? string.Empty;
        if (uri.EndsWith("/ole", StringComparison.Ordinal))
        {
            return PaintObjectPreview(pc, data, list, x, y, w, h);
        }

        if (!uri.EndsWith("/diagram", StringComparison.Ordinal))
        {
            return false;
        }

        MarkupNode relIds = DrawingTheme.Child(data, "relIds");
        string? dataRel = relIds.IsNull ? null : RelationshipAttribute(relIds, "dm");
        string? drawingRel = null;
        if (dataRel is not null && LoadRelated(pc.Part, dataRel) is { } dataDoc)
        {
            drawingRel = FindDataModelExtension(dataDoc.Root, 0);
        }

        if (drawingRel is not null && LoadRelated(pc.Part, drawingRel) is { } drawingDoc)
        {
            MarkupNode tree = DrawingTheme.Child(drawingDoc.Root, "spTree");
            if (!tree.IsNull)
            {
                DrawingPainter.DrawShapes(list, NextId, tree, x / K, y / K, pc.Drawing);
                AddNote("slide.smartart-cached", "SmartArt is drawn from the drawing the application saved with it; it is not regenerated from its layout definition.");
                return true;
            }
        }

        AddNote("slide.smartart-not-painted", "A SmartArt diagram with no saved drawing is not painted; regenerating it from its layout definition is not supported here.");
        return true;
    }

    // An embedded object is shown by the picture saved with it (p:oleObj holds a p:pic fallback).
    private bool PaintObjectPreview(PartContext pc, MarkupNode data, DisplayList list, double x, double y, double w, double h)
    {
        MarkupNode pic = FindDescendant(data, "pic", 0);
        if (pic.IsNull)
        {
            AddNote("slide.object-not-painted", "An embedded object with no saved preview picture is not painted.");
            return true;
        }

        bool tagged = pc.Drawing.Tagged;
        pc.Drawing.Tagged = false;
        DrawingPainter.Draw(list, NextId, pic, new DrawingBox(x / K, y / K, w / K, h / K), pc.Drawing);
        pc.Drawing.Tagged = tagged;
        return true;
    }

    private static MarkupNode FindDescendant(MarkupNode n, string name, int depth)
    {
        if (depth > 6)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == name)
            {
                return c;
            }

            MarkupNode found = FindDescendant(c, name, depth + 1);
            if (!found.IsNull)
            {
                return found;
            }
        }

        return default;
    }

    private static string? FindDataModelExtension(MarkupNode n, int depth)
    {
        if (depth > 6)
        {
            return null;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "dataModelExt")
            {
                return c.GetAttribute("relId");
            }

            if (FindDataModelExtension(c, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? RelationshipAttribute(MarkupNode e, string local)
    {
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local)))
            {
                return a.Value;
            }
        }

        return null;
    }

    private readonly List<MarkupDocument> _related = [];

    private MarkupDocument? LoadRelated(PresentationPart part, string relationshipId)
    {
        foreach (Relationship r in part.Relationships)
        {
            if (r.Id == relationshipId && r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(part.PartName, r, out PartUri target)
                && part.Owner.Package.FindPart(target.Value) is { } p
                && MarkupDocument.TryParse(p.GetBytes(), out MarkupDocument? doc, part.Owner.Context, target.Value) && doc is not null)
            {
                _related.Add(doc);
                return doc;
            }
        }

        return null;
    }
}
