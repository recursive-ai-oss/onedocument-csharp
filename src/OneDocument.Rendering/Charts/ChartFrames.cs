using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>A graphic frame in a slide, drawing or document part that shows a chart.</summary>
/// <param name="RelationshipId">The <c>r:id</c> of the chart relationship in the frame's part.</param>
/// <param name="WidthEmu">Frame width in EMU, or null when the frame does not state one (spreadsheet anchors state cells, not sizes).</param>
/// <param name="HeightEmu">Frame height in EMU, or null.</param>
/// <param name="Name">The frame's name (<c>cNvPr/@name</c> or <c>docPr/@name</c>), or null.</param>
/// <param name="Description">The frame's alternative text (<c>descr</c>), or null.</param>
/// <param name="IsExtended">True for a chartex reference (<c>cx:chart</c>).</param>
public sealed record ChartFrame(string RelationshipId, long? WidthEmu, long? HeightEmu, string? Name, string? Description, bool IsExtended);

/// <summary>Finds the chart references in a part: PresentationML <c>p:graphicFrame</c>, SpreadsheetML drawing <c>xdr:graphicFrame</c> and WordprocessingML <c>wp:inline</c>/<c>wp:anchor</c>.</summary>
public static class ChartFrames
{
    /// <summary>Lists the chart frames of a part in document order. Chart references inside <c>mc:Fallback</c> are skipped so a chartex frame is not counted twice.</summary>
    /// <param name="part">The slide, drawing or document part.</param>
    /// <returns>The frames.</returns>
    public static IReadOnlyList<ChartFrame> Find(MarkupDocument part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var list = new List<ChartFrame>();
        Walk(part.Root, list, 0);
        return list;
    }

    private static void Walk(MarkupNode n, List<ChartFrame> list, int depth)
    {
        if (n.IsNull || depth > 64 || list.Count > 10_000)
        {
            return;
        }

        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            string name = c.LocalNameString;
            if (name == "Fallback")
            {
                continue;
            }

            if (name is "graphicFrame" or "inline" or "anchor" && FindChartRef(c) is { } r)
            {
                list.Add(Frame(c, r.Id, r.Extended));
                continue;
            }

            Walk(c, list, depth + 1);
        }
    }

    private static (string Id, bool Extended)? FindChartRef(MarkupNode frame)
    {
        for (MarkupNode c = frame.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (c.LocalNameString == "chart" && IdOf(c) is { } id)
            {
                return (id, c.NamespaceUri.Contains("chartex", StringComparison.Ordinal));
            }

            if (c.LocalNameString != "Fallback" && FindChartRef(c) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    private static string? IdOf(MarkupNode chart)
    {
        foreach (MarkupAttributeRef a in chart.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("id"u8))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static ChartFrame Frame(MarkupNode frame, string id, bool extended)
    {
        long? w = null;
        long? h = null;
        string? name = null;
        string? descr = null;
        Scan(frame, ref w, ref h, ref name, ref descr, 0);
        return new ChartFrame(id, w, h, name, descr, extended);
    }

    private static void Scan(MarkupNode n, ref long? w, ref long? h, ref string? name, ref string? descr, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "cNvPr":
                case "docPr":
                    name ??= c.GetAttribute("name");
                    descr ??= c.GetAttribute("descr");
                    break;
                case "extent":
                    w ??= ChartReader.Lng(c.GetAttribute("cx"));
                    h ??= ChartReader.Lng(c.GetAttribute("cy"));
                    break;
                case "ext" when c.GetAttribute("cx") is not null:
                    w ??= ChartReader.Lng(c.GetAttribute("cx"));
                    h ??= ChartReader.Lng(c.GetAttribute("cy"));
                    break;
                case "graphic":
                case "graphicData":
                    return;
            }

            if (c.LocalNameString is "nvGraphicFramePr" or "xfrm" or "cNvGraphicFramePr")
            {
                Scan(c, ref w, ref h, ref name, ref descr, depth + 1);
            }
        }
    }
}
