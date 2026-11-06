using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Drawings: a draw:frame holding an image becomes a DrawingML picture (inline or anchored), one holding a text box becomes a VML text box
// with converted content, one holding an embedded object keeps the object's preview picture. Other shapes are reported as losses.
internal sealed partial class OdtImporter
{
    private int _shapeCount;

    private void EmitFrame(Inl st, MarkupNode frame)
    {
        Story s = st.S;
        MarkupNode image = OdfMarkup.Child(frame, OdfNamespaces.Draw, "image");
        MarkupNode textBox = OdfMarkup.Child(frame, OdfNamespaces.Draw, "text-box");
        bool hasObject = !OdfMarkup.Child(frame, OdfNamespaces.Draw, "object").IsNull || !OdfMarkup.Child(frame, OdfNamespaces.Draw, "object-ole").IsNull;
        if (hasObject)
        {
            Lost("loss.embedded-objects", "embedded objects", "Embedded objects (formulas, charts, OLE) were reduced to their preview picture, or dropped when they have none.");
        }

        if (!image.IsNull)
        {
            EmitPicture(st, frame, image);
        }
        else if (!textBox.IsNull)
        {
            EmitTextBox(st, frame, textBox);
        }
        else if (!hasObject)
        {
            Lost("loss.shapes", "drawing shapes", "A frame with content other than a picture or a text box was dropped.");
        }
        else
        {
            EmitFormulaText(st, frame);
        }

        _ = s;
    }

    // An embedded formula is MathML in a sub-document of the package. The hub has no path from MathML to its equations, so the symbols' text stands in
    // for it, and the formula is reported as lost.
    private void EmitFormulaText(Inl st, MarkupNode frame)
    {
        MarkupNode obj = OdfMarkup.Child(frame, OdfNamespaces.Draw, "object");
        string? href = obj.IsNull ? null : A(obj, OdfNamespaces.XLink, "href");
        if (href is null || _pkg.IsFlat)
        {
            return;
        }

        string entry = (href.StartsWith("./", StringComparison.Ordinal) ? href[2..] : href).TrimEnd('/') + "/content.xml";
        if (!_pkg.TryReadEntry(entry, out ReadOnlyMemory<byte> bytes) || !MarkupDocument.TryParse(bytes, out MarkupDocument? doc, _ctx, entry) || doc is null)
        {
            return;
        }

        MarkupNode root = doc.Root;
        if (root.IsNull || root.NamespaceUri != OdfNamespaces.MathMl)
        {
            return;
        }

        Lost("loss.formulas", "formulas", "Embedded formulas (MathML) are written as the plain text of their symbols; the equation is not carried.");
        var sb = new StringBuilder();
        CollectMathText(root, sb, 0);
        if (sb.Length > 0)
        {
            Flush(st);
            st.LastSpace = false;
            st.AddText(sb.ToString());
            st.LastSpace = false;
        }
    }

    private static void CollectMathText(MarkupNode n, StringBuilder sb, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.NamespaceUri == OdfNamespaces.MathMl && c.LocalNameString is "mi" or "mn" or "mo" or "mtext")
            {
                sb.Append(c.InnerText);
            }
            else if (c.LocalNameString != "annotation" && c.LocalNameString != "annotation-xml")
            {
                CollectMathText(c, sb, depth + 1);
            }
        }
    }

    private OdfStyle? GraphicStyle(MarkupNode frame) =>
        A(frame, OdfNamespaces.Draw, "style-name") is { } n ? _styles.Find("graphic", n) : null;

    private static string? GraphicProp(OdtImporter self, OdfStyle? style, string ns, string local)
    {
        if (style is null)
        {
            return null;
        }

        foreach (OdfStyle st in self._styles.Chain(style))
        {
            string? v = OdfMarkup.Attribute(st.Properties("graphic-properties"), ns, local);
            if (v is not null)
            {
                return v;
            }
        }

        return null;
    }

    private (long Cx, long Cy) FrameSize(MarkupNode frame)
    {
        long cx = 72 * 12700L, cy = 72 * 12700L;
        string? w = A(frame, OdfNamespaces.Svg, "width");
        string? h = A(frame, OdfNamespaces.Svg, "height");
        if (w is not null && w.EndsWith('%') && double.TryParse(w.AsSpan(0, w.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
        {
            cx = (long)(PageContentWidth(_currentMaster) * 635L * pct / 100);
        }
        else if (OdfUnits.TryEmu(w, out long ew) && ew > 0)
        {
            cx = ew;
        }

        if (OdfUnits.TryEmu(h, out long eh) && eh > 0)
        {
            cy = eh;
        }
        else if (OdfUnits.TryEmu(A(frame, OdfNamespaces.Style, "rel-height"), out long _))
        {
            cy = cx;
        }

        return (Math.Min(cx, 400L * 12700 * 100), Math.Min(cy, 400L * 12700 * 100));
    }

    private string? RegisterImage(MarkupNode img)
    {
        string? href = A(img, OdfNamespaces.XLink, "href");
        byte[]? data = null;
        if (!string.IsNullOrEmpty(href))
        {
            if (_mediaByHref.TryGetValue(href, out string? known))
            {
                return known;
            }

            if (href.Contains(':', StringComparison.Ordinal) || href.StartsWith("//", StringComparison.Ordinal))
            {
                Lost("loss.linked-images", "linked pictures", "Pictures linked from another location are not fetched and were dropped.");
                return null;
            }

            string entry = href.StartsWith("./", StringComparison.Ordinal) ? href[2..] : href;
            if (!_pkg.IsFlat && _pkg.TryReadEntry(entry, out ReadOnlyMemory<byte> bytes))
            {
                data = bytes.ToArray();
            }
            else
            {
                Defect(DefectKind.Reference, "odf.missing-picture", DefectSeverity.Dropped, "A picture refers to '" + href + "', which the package does not hold.", img);
                Lost("loss.pictures", "pictures", "Pictures whose data is not in the file were dropped.");
                return null;
            }
        }
        else
        {
            MarkupNode bin = OdfMarkup.Child(img, OdfNamespaces.Office, "binary-data");
            if (!bin.IsNull)
            {
                string text = bin.InnerText;
                var clean = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    if (!char.IsWhiteSpace(c))
                    {
                        clean.Append(c);
                    }
                }

                try
                {
                    data = System.Convert.FromBase64String(clean.ToString());
                }
                catch (FormatException)
                {
                    Defect(DefectKind.Encoding, "odf.bad-base64", DefectSeverity.Dropped, "Embedded picture data is not valid base64.", bin);
                    Lost("loss.pictures", "pictures", "Pictures whose data could not be decoded were dropped.");
                    return null;
                }
            }
        }

        if (data is null || data.Length == 0)
        {
            return null;
        }

        string ext = SniffImage(data) ?? "bin";
        string contentType = ext switch
        {
            "png" => "image/png",
            "jpg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tif" => "image/tiff",
            "wmf" => "image/x-wmf",
            "emf" => "image/x-emf",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
        _mediaCount++;
        string part = "/word/media/image" + OdfUnits.I(_mediaCount) + "." + ext;
        _mediaFiles.Add((part, contentType, data));
        if (href is not null)
        {
            _mediaByHref[href] = part;
        }

        return part;
    }

    private static string? SniffImage(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G')
        {
            return "png";
        }

        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8)
        {
            return "jpg";
        }

        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F')
        {
            return "gif";
        }

        if (d.Length >= 2 && d[0] == 'B' && d[1] == 'M')
        {
            return "bmp";
        }

        if (d.Length >= 4 && ((d[0] == 'I' && d[1] == 'I' && d[2] == 42) || (d[0] == 'M' && d[1] == 'M' && d[3] == 42)))
        {
            return "tif";
        }

        if (d.Length >= 44 && d[0] == 1 && d[1] == 0 && d[2] == 0 && d[3] == 0 && d[40] == ' ' && d[41] == 'E' && d[42] == 'M' && d[43] == 'F')
        {
            return "emf";
        }

        if (d.Length >= 4 && d[0] == 0xD7 && d[1] == 0xCD && d[2] == 0xC6 && d[3] == 0x9A)
        {
            return "wmf";
        }

        if (d.Length >= 5)
        {
            string head = Encoding.ASCII.GetString(d, 0, Math.Min(256, d.Length)).TrimStart();
            if (head.StartsWith("<svg", StringComparison.Ordinal) || head.StartsWith("<?xml", StringComparison.Ordinal) && head.Contains("<svg", StringComparison.Ordinal))
            {
                return "svg";
            }
        }

        return null;
    }

    // A drawing shape other than a frame (rectangle, custom shape, group...). Its geometry is not carried; the text it holds becomes a text box and
    // the frames inside a group are written as they are.
    private void EmitShape(Inl st, MarkupNode shape)
    {
        string local = shape.LocalNameString;
        if (local == "g" || local == "a")
        {
            bool any = false;
            foreach (MarkupNode c in shape.ChildElements)
            {
                if (c.NamespaceUri == OdfNamespaces.Draw)
                {
                    Flush(st);
                    if (c.LocalNameString == "frame")
                    {
                        EmitFrame(st, c);
                    }
                    else
                    {
                        EmitShape(st, c);
                    }

                    any = true;
                }
            }

            if (!any)
            {
                Lost("loss.shapes", "drawing shapes", "Drawing shapes (lines, rectangles, custom shapes, groups) were dropped; the text inside them is kept in text boxes.");
            }

            return;
        }

        Lost("loss.shapes", "drawing shapes", "Drawing shapes (lines, rectangles, custom shapes, groups) were dropped; the text inside them is kept in text boxes.");
        bool hasText = false;
        foreach (MarkupNode c in shape.ChildElements)
        {
            if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString is "p" or "h" or "list")
            {
                hasText = true;
                break;
            }
        }

        if (hasText)
        {
            Flush(st);
            EmitTextBox(st, shape, shape);
        }
    }

    private void EmitPicture(Inl st, MarkupNode frame, MarkupNode image)
    {
        string? part = RegisterImage(image);
        if (part is null)
        {
            return;
        }

        Story s = st.S;
        (long cx, long cy) = FrameSize(frame);
        string rid = s.AddRel(RelBase + "image", part[6..]);
        int id = _drawingId++;
        string name = A(frame, OdfNamespaces.Draw, "name") is { Length: > 0 } nm ? nm : "Picture " + OdfUnits.I(id);
        string? title = ChildText(frame, "title");
        string? descr = ChildText(frame, "desc");
        OdfStyle? gs = GraphicStyle(frame);
        string anchorType = A(frame, OdfNamespaces.Text, "anchor-type") ?? "paragraph";
        if (GraphicProp(this, gs, OdfNamespaces.Fo, "clip") is { } clip && HasCrop(clip))
        {
            Lost("loss.picture-cropping", "picture cropping", "Cropping of a picture was dropped.");
        }

        var sb = new StringBuilder("<w:r><w:drawing>");
        string docPr = "<wp:docPr id=\"" + OdfUnits.I(id) + "\" name=\"" + OdfUnits.Esc(name) + "\"" + (descr is null ? string.Empty : " descr=\"" + OdfUnits.Esc(descr) + "\"") + (title is null ? string.Empty : " title=\"" + OdfUnits.Esc(title) + "\"") + "/>";
        string graphic = "<a:graphic><a:graphicData uri=\"" + PicNs + "\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"" + OdfUnits.I(id) + "\" name=\"" + OdfUnits.Esc(name) + "\"/><pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + OdfUnits.I(cx) + "\" cy=\"" + OdfUnits.I(cy) + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>";
        string extent = "<wp:extent cx=\"" + OdfUnits.I(cx) + "\" cy=\"" + OdfUnits.I(cy) + "\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>";
        if (anchorType == "as-char")
        {
            sb.Append("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">").Append(extent).Append(docPr).Append("<wp:cNvGraphicFramePr/>").Append(graphic).Append("</wp:inline>");
        }
        else
        {
            sb.Append(AnchorOpen(frame, gs, anchorType)).Append(extent).Append(WrapXml(gs)).Append(docPr).Append("<wp:cNvGraphicFramePr/>").Append(graphic).Append("</wp:anchor>");
        }

        sb.Append("</w:drawing></w:r>");
        s.Raw(sb.ToString());
    }

    // fo:clip is rect(top, right, bottom, left) in any length unit; "rect(0in, 0in, 0in, 0in)" crops nothing.
    private static bool HasCrop(string clip)
    {
        foreach (string token in clip.Split(ClipSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (OdfUnits.TryPoints(token, out double pt) && Math.Abs(pt) > 0.0001)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] ClipSeparators = ['(', ')', ',', ' '];

    private static string? ChildText(MarkupNode frame, string svgLocal)
    {
        MarkupNode c = OdfMarkup.Child(frame, OdfNamespaces.Svg, svgLocal);
        return c.IsNull || c.InnerText.Length == 0 ? null : c.InnerText;
    }

    private string AnchorOpen(MarkupNode frame, OdfStyle? gs, string anchorType)
    {
        string? hRel = GraphicProp(this, gs, OdfNamespaces.Style, "horizontal-rel");
        string? vRel = GraphicProp(this, gs, OdfNamespaces.Style, "vertical-rel");
        string? hPos = GraphicProp(this, gs, OdfNamespaces.Style, "horizontal-pos");
        string? vPos = GraphicProp(this, gs, OdfNamespaces.Style, "vertical-pos");
        string hFrom = hRel switch
        {
            "page" or "page-start-margin" or "page-end-margin" => "page",
            "page-content" => "margin",
            "char" => "character",
            _ => anchorType == "page" ? "page" : "column",
        };
        string vFrom = vRel switch
        {
            "page" => "page",
            "page-content" => "margin",
            "char" or "baseline" or "line" or "text" => "line",
            _ => anchorType == "page" ? "page" : "paragraph",
        };
        string h = hPos switch
        {
            "center" => "<wp:align>center</wp:align>",
            "right" => "<wp:align>right</wp:align>",
            "left" when A(frame, OdfNamespaces.Svg, "x") is null => "<wp:align>left</wp:align>",
            _ => "<wp:posOffset>" + OdfUnits.I(OdfUnits.TryEmu(A(frame, OdfNamespaces.Svg, "x"), out long x) ? x : 0) + "</wp:posOffset>",
        };
        string v = vPos switch
        {
            "middle" => "<wp:align>center</wp:align>",
            "bottom" => "<wp:align>bottom</wp:align>",
            "top" when A(frame, OdfNamespaces.Svg, "y") is null => "<wp:align>top</wp:align>",
            _ => "<wp:posOffset>" + OdfUnits.I(OdfUnits.TryEmu(A(frame, OdfNamespaces.Svg, "y"), out long y) ? y : 0) + "</wp:posOffset>",
        };
        bool behind = GraphicProp(this, gs, OdfNamespaces.Style, "run-through") == "background";
        return "<wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"" + OdfUnits.I(251658240 + _drawingId) + "\" behindDoc=\"" + (behind ? "1" : "0")
            + "\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\"><wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"" + hFrom + "\">" + h + "</wp:positionH><wp:positionV relativeFrom=\"" + vFrom + "\">" + v + "</wp:positionV>";
    }

    private string WrapXml(OdfStyle? gs) => GraphicProp(this, gs, OdfNamespaces.Style, "wrap") switch
    {
        "none" => "<wp:wrapTopAndBottom/>",
        "left" => "<wp:wrapSquare wrapText=\"left\"/>",
        "right" => "<wp:wrapSquare wrapText=\"right\"/>",
        "run-through" => "<wp:wrapNone/>",
        "biggest" => "<wp:wrapSquare wrapText=\"largest\"/>",
        _ => "<wp:wrapSquare wrapText=\"bothSides\"/>",
    };

    private void EmitTextBox(Inl st, MarkupNode frame, MarkupNode textBox)
    {
        Story s = st.S;
        _shapeCount++;
        (long cx, long cy) = FrameSize(frame);
        OdfStyle? gs = GraphicStyle(frame);
        string anchorType = A(frame, OdfNamespaces.Text, "anchor-type") ?? "paragraph";
        double wPt = cx / 12700.0, hPt = cy / 12700.0;
        if (OdfUnits.TryPoints(A(frame, OdfNamespaces.Svg, "height"), out _) == false && OdfUnits.TryPoints(A(textBox, OdfNamespaces.Fo, "min-height"), out double mh))
        {
            hPt = mh;
        }

        string style;
        if (anchorType == "as-char")
        {
            style = "position:relative;width:" + OdfUnits.Pt(wPt) + ";height:" + OdfUnits.Pt(hPt);
        }
        else
        {
            OdfUnits.TryPoints(A(frame, OdfNamespaces.Svg, "x"), out double x);
            OdfUnits.TryPoints(A(frame, OdfNamespaces.Svg, "y"), out double y);
            string hRel = GraphicProp(this, gs, OdfNamespaces.Style, "horizontal-rel") is "page" or "page-start-margin" ? "page" : GraphicProp(this, gs, OdfNamespaces.Style, "horizontal-rel") == "page-content" ? "margin" : "text";
            string vRel = GraphicProp(this, gs, OdfNamespaces.Style, "vertical-rel") is "page" ? "page" : GraphicProp(this, gs, OdfNamespaces.Style, "vertical-rel") == "page-content" ? "margin" : "text";
            style = "position:absolute;margin-left:" + OdfUnits.Pt(x) + ";margin-top:" + OdfUnits.Pt(y) + ";width:" + OdfUnits.Pt(wPt) + ";height:" + OdfUnits.Pt(hPt)
                + ";mso-position-horizontal-relative:" + hRel + ";mso-position-vertical-relative:" + vRel;
        }

        string fill = Hex(GraphicProp(this, gs, OdfNamespaces.Fo, "background-color"));
        string stroke = GraphicProp(this, gs, OdfNamespaces.Fo, "border") is { } b && b != "none" ? b : null!;
        string strokeColor = string.Empty;
        if (stroke is not null)
        {
            foreach (string part in stroke.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Hex(part).Length > 0)
                {
                    strokeColor = Hex(part);
                }
            }
        }

        s.Raw("<w:r><w:pict><v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\"><v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>"
            + "<v:shape id=\"TextBox" + OdfUnits.I(_shapeCount) + "\" type=\"#_x0000_t202\" style=\"" + style + "\""
            + (fill.Length > 0 ? " fillcolor=\"#" + fill + "\"" : " filled=\"f\"")
            + (strokeColor.Length > 0 ? " strokecolor=\"#" + strokeColor + "\"" : " stroked=\"f\"")
            + "><v:textbox><w:txbxContent>");
        bool saved = s.EndsWithParagraph;
        s.EndsWithParagraph = false;
        foreach (MarkupNode c in textBox.ChildElements)
        {
            // A shape's own children include its geometry; only the text and tables are content.
            if (c.NamespaceUri == OdfNamespaces.Text || c.NamespaceUri == OdfNamespaces.Table)
            {
                EmitBlock(s, c, null, null);
            }
        }

        if (!s.EndsWithParagraph)
        {
            s.Raw("<w:p/>");
        }

        s.EndsWithParagraph = saved;
        s.Raw("</w:txbxContent></v:textbox>");
        if (anchorType != "as-char")
        {
            s.Raw("<w10:wrap type=\"" + (GraphicProp(this, gs, OdfNamespaces.Style, "wrap") is "none" ? "topAndBottom" : "square") + "\"/>");
        }

        s.Raw("</v:shape></w:pict></w:r>");
    }
}
