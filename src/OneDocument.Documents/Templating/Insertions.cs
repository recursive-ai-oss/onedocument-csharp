using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>Turns the non-text pieces of a rendered paragraph (pictures, links, HTML, Markdown) into WordprocessingML.</summary>
internal static class Insertions
{
    private const long EmuPerPixel = 9525;
    private const long MaxDefaultWidth = 5_486_400; // 6 inches

    private static readonly string Wml = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // ---- hyperlinks ------------------------------------------------------------------------------------------------------------

    /// <summary>A hyperlink holding one run that takes the formatting of the run where the tag began.</summary>
    public static string Hyperlink(RenderContext ctx, string prefix, string runStart, string rPr, string url, string text)
    {
        string p = prefix.Length == 0 ? string.Empty : prefix + ":";
        string target;
        if (url.StartsWith('#'))
        {
            target = " " + p + "anchor=\"" + XmlSnip.Escape(url[1..]) + "\"";
        }
        else
        {
            string id = ctx.AddRelationship(ctx.CurrentPart, ctx.RelationshipType("hyperlink"), url, RelationshipTargetMode.External);
            target = " xmlns:r=\"" + (ctx.Strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "\" r:id=\"" + id + "\"";
        }

        EnsureHyperlinkStyle(ctx);
        string props = WithRunStyle(rPr, p, "Hyperlink");
        return "<" + p + "hyperlink" + target + " " + p + "history=\"1\">" + runStart + props + XmlSnip.RunContent(prefix, text) + "</" + p + "r></" + p + "hyperlink>";
    }

    // ECMA-376 Part 1 §17.3.2.28 (w:rPr): w:rStyle is the first property.
    internal static string WithRunStyle(string rPr, string p, string styleId)
    {
        string style = "<" + p + "rStyle " + p + "val=\"" + styleId + "\"/>";
        if (rPr.Length == 0)
        {
            return "<" + p + "rPr>" + style + "</" + p + "rPr>";
        }

        if (rPr.Contains(p + "rStyle", StringComparison.Ordinal))
        {
            return rPr;
        }

        if (rPr.EndsWith("/>", StringComparison.Ordinal))
        {
            return "<" + p + "rPr>" + style + "</" + p + "rPr>";
        }

        int gt = rPr.IndexOf('>', StringComparison.Ordinal);
        return rPr.Insert(gt + 1, style);
    }

    internal static void EnsureHyperlinkStyle(RenderContext ctx)
    {
        Styles.StyleSheet sheet = ctx.Document.Styles;
        if (sheet.Find("Hyperlink") is not null)
        {
            return;
        }

        Styles.StyleDefinition style = sheet.AddStyle("Hyperlink", "Hyperlink", Styles.StyleType.Character, "DefaultParagraphFont");
        style.Markup.Node.AppendXml(Encoding.UTF8.GetBytes(
            "<w:uiPriority xmlns:w=\"" + ctx.Ns + "\" w:val=\"99\"/>"));
        style.Markup.Node.AppendXml(Encoding.UTF8.GetBytes(
            "<w:rPr xmlns:w=\"" + ctx.Ns + "\"><w:color w:val=\"0563C1\" w:themeColor=\"hyperlink\"/><w:u w:val=\"single\"/></w:rPr>"));
    }

    // ---- pictures --------------------------------------------------------------------------------------------------------------

    /// <summary>An inline picture in a run that takes the run properties of the run where the tag began.</summary>
    public static string Image(RenderContext ctx, ParagraphModel paragraph, ParagraphItem? segment, TemplateInsertion insertion)
    {
        string where = ctx.CurrentPart;
        if (insertion.Value.Kind == TemplateDataKind.Null)
        {
            return string.Empty;
        }

        if (!TryGetBytes(insertion.Value.Raw, ctx.Options.Limits ?? OfficeLimits.Default, out byte[]? bytes) || !ImageInfo.TryRead(bytes, out ImageInfo info))
        {
            ctx.Warnings.Add(new Defect(DefectKind.Value, "template.image-unsupported", DefectSeverity.Dropped,
                "The value of the image tag is not a PNG, JPEG, GIF or BMP picture (as bytes, a stream or base64 text); nothing was inserted.", DefectLocation.InPart(where)));
            return string.Empty;
        }

        insertion.Options.TryGetValue("width", out string? w);
        insertion.Options.TryGetValue("height", out string? h);
        insertion.Options.TryGetValue("fit", out string? fit);
        insertion.Options.TryGetValue("alt", out string? alt);
        (long cx, long cy) = Size(info, ParseLength(w), ParseLength(h), fit ?? "contain");

        string rid = ctx.AddImage(bytes, info.Extension, info.ContentType);
        string id = ctx.Ids.NewDrawingId();
        string p = paragraph.Prefix.Length == 0 ? string.Empty : paragraph.Prefix + ":";
        string drawing = Drawing(ctx, p, rid, id, "Picture " + id, XmlSnip.Escape(alt ?? string.Empty), cx, cy);
        string runStart = segment?.RunStart is { Length: > 0 } s ? s : "<" + p + "r>";
        return runStart + (segment?.RunProperties ?? string.Empty) + drawing + "</" + p + "r>";
    }

    /// <summary>The <c>w:drawing</c> of an inline picture (ECMA-376 Part 1 §20.4.2.8 wp:inline, §21.2.2.150 pic:pic).</summary>
    internal static string Drawing(RenderContext ctx, string p, string relationshipId, string docPrId, string name, string alt, long cx, long cy) =>
        Drawing(ctx.Strict, p, relationshipId, docPrId, name, alt, cx, cy);

    /// <summary>The same drawing for a caller that has no render context; <paramref name="strict"/> selects the Strict namespaces.</summary>
    internal static string Drawing(bool strict, string p, string relationshipId, string docPrId, string name, string alt, long cx, long cy)
    {
        string wp = strict ? "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing" : "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        string a = strict ? "http://purl.oclc.org/ooxml/drawingml/main" : "http://schemas.openxmlformats.org/drawingml/2006/main";
        string pic = strict ? "http://purl.oclc.org/ooxml/drawingml/picture" : "http://schemas.openxmlformats.org/drawingml/2006/picture";
        string r = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string cxs = cx.ToString(CultureInfo.InvariantCulture);
        string cys = cy.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append("drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" xmlns:wp=\"").Append(wp)
            .Append("\" xmlns:a=\"").Append(a).Append("\" xmlns:pic=\"").Append(pic).Append("\" xmlns:r=\"").Append(r).Append("\">")
            .Append("<wp:extent cx=\"").Append(cxs).Append("\" cy=\"").Append(cys).Append("\"/>")
            .Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>")
            .Append("<wp:docPr id=\"").Append(docPrId).Append("\" name=\"").Append(XmlSnip.Escape(name)).Append("\" descr=\"").Append(alt).Append("\"/>")
            .Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>")
            .Append("<a:graphic><a:graphicData uri=\"").Append(pic).Append("\"><pic:pic>")
            .Append("<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"").Append(XmlSnip.Escape(name)).Append("\"/><pic:cNvPicPr/></pic:nvPicPr>")
            .Append("<pic:blipFill><a:blip r:embed=\"").Append(relationshipId).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
            .Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cxs).Append("\" cy=\"").Append(cys).Append("\"/></a:xfrm>")
            .Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></")
            .Append(p).Append("drawing>");
        _ = Wml;
        return sb.ToString();
    }

    private static bool TryGetBytes(object? raw, OfficeLimits limits, out byte[] bytes)
    {
        bytes = [];
        switch (raw)
        {
            case byte[] b:
                bytes = b;
                return b.Length <= limits.MaxPartSize;
            case ReadOnlyMemory<byte> m:
                bytes = m.ToArray();
                return bytes.Length <= limits.MaxPartSize;
            case Stream s when s.CanRead:
                {
                    using var ms = new MemoryStream();
                    var buffer = new byte[81920];
                    int n;
                    while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, n);
                        if (ms.Length > limits.MaxPartSize)
                        {
                            return false;
                        }
                    }

                    bytes = ms.ToArray();
                    return true;
                }

            case string text:
                {
                    // A data: URI or plain base64; nothing is read from a path or a URL.
                    int comma = text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? text.IndexOf(',', StringComparison.Ordinal) : -1;
                    string payload = comma >= 0 ? text[(comma + 1)..] : text;
                    try
                    {
                        bytes = Convert.FromBase64String(payload.Trim());
                        return bytes.Length <= limits.MaxPartSize;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }

            default:
                return false;
        }
    }

    internal static long? ParseLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string t = text.Trim().ToLowerInvariant();
        (string unit, double factor)[] units = [("emu", 1), ("cm", 360000), ("mm", 36000), ("in", 914400), ("pt", 12700), ("px", EmuPerPixel)];
        double f = EmuPerPixel;
        foreach ((string unit, double factor) in units)
        {
            if (t.EndsWith(unit, StringComparison.Ordinal))
            {
                t = t[..^unit.Length].Trim();
                f = factor;
                break;
            }
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 && v < 1e6 ? (long)Math.Round(v * f) : null;
    }

    private static (long, long) Size(ImageInfo info, long? w, long? h, string fit)
    {
        double nw = Math.Max(1, info.Width) * EmuPerPixel;
        double nh = Math.Max(1, info.Height) * EmuPerPixel;
        double cx;
        double cy;
        if (w is null && h is null)
        {
            cx = nw;
            cy = nh;
            if (cx > MaxDefaultWidth)
            {
                cy = cy * MaxDefaultWidth / cx;
                cx = MaxDefaultWidth;
            }
        }
        else if (w is not null && h is null)
        {
            cx = w.Value;
            cy = nh * cx / nw;
        }
        else if (w is null)
        {
            cy = h!.Value;
            cx = nw * cy / nh;
        }
        else if (string.Equals(fit, "stretch", StringComparison.OrdinalIgnoreCase))
        {
            cx = w.Value;
            cy = h!.Value;
        }
        else
        {
            double scale = Math.Min(w.Value / nw, h!.Value / nh);
            cx = nw * scale;
            cy = nh * scale;
        }

        return (Math.Max(1, (long)Math.Round(cx)), Math.Max(1, (long)Math.Round(cy)));
    }

    // ---- HTML and Markdown -----------------------------------------------------------------------------------------------------

    /// <summary>Converted content as paragraphs, replacing a paragraph that held only the tag.</summary>
    public static string Blocks(RenderContext ctx, ParagraphModel host, TemplatePiece piece)
    {
        IReadOnlyList<RichBlock> blocks = RichContent.Convert(ctx, piece);
        return RichWriter.Paragraphs(ctx, host, blocks);
    }

    /// <summary>Converted content inline: the runs of every block, joined by line breaks.</summary>
    public static string Inline(RenderContext ctx, ParagraphModel host, ParagraphItem? segment, TemplatePiece piece)
    {
        IReadOnlyList<RichBlock> blocks = RichContent.Convert(ctx, piece);
        return RichWriter.Runs(ctx, host, segment, blocks);
    }
}

/// <summary>The pixel size and type of a picture, read from its header.</summary>
internal readonly record struct ImageInfo(int Width, int Height, string Extension, string ContentType)
{
    public static bool TryRead(byte[] b, out ImageInfo info)
    {
        info = default;
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
        {
            info = new ImageInfo((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)), "png", "image/png");
            return info.Width > 0 && info.Height > 0;
        }

        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
        {
            info = new ImageInfo(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6)), BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8)), "gif", "image/gif");
            return info.Width > 0 && info.Height > 0;
        }

        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
        {
            int w = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18));
            int h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22)));
            info = new ImageInfo(w, h, "bmp", "image/bmp");
            return w > 0 && h > 0;
        }

        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            // Walk the marker segments to the first start-of-frame (ISO/IEC 10918-1 Annex B).
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = b[i + 1];
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) || marker == 0xFF)
                {
                    i += marker == 0xFF ? 1 : 2;
                    continue;
                }

                int length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 2));
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    int h = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 5));
                    int w = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 7));
                    info = new ImageInfo(w, h, "jpeg", "image/jpeg");
                    return w > 0 && h > 0;
                }

                i += 2 + Math.Max(2, length);
            }
        }

        return false;
    }
}
