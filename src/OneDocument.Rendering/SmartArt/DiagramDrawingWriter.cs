using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// Writes the shapes the engine produced as a diagram drawing part (dsp:drawing, [MS-ODRAWXML] §2.3.1), the form Office stores as the
// diagram's cache. Painting a regenerated diagram then goes through exactly the code that paints a cached one.
internal static class DiagramDrawingWriter
{
    public static byte[] Write(IReadOnlyList<OutShape> shapes)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<dsp:drawing xmlns:dgm=\"").Append(Dx.Dgm).Append("\" xmlns:dsp=\"").Append(Dx.Dsp).Append("\" xmlns:a=\"").Append(Dx.A).Append("\">");
        sb.Append("<dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr/>");
        foreach (OutShape s in shapes)
        {
            WriteShape(sb, s);
        }

        sb.Append("</dsp:spTree></dsp:drawing>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteShape(StringBuilder sb, OutShape s)
    {
        LayoutInstance n = s.Instance;
        double x = n.X, y = n.Y, w = n.W, h = n.H;
        string? custom = null;
        if (s.IsConnector && n.Route is { } route)
        {
            (x, y, w, h, custom) = RouteGeometry(route);
        }

        sb.Append("<dsp:sp modelId=\"").Append(Dx.Esc(s.ModelId)).Append("\"><dsp:nvSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvSpPr/></dsp:nvSpPr><dsp:spPr>");
        double rot = ((n.Rotation + (n.Shape?.Rotation ?? 0)) % 360 + 360) % 360;
        sb.Append("<a:xfrm");
        if (rot > 1e-6)
        {
            sb.Append(" rot=\"").Append((long)Math.Round(rot * 60000)).Append('"');
        }

        sb.Append("><a:off x=\"").Append(Dx.Emu(x)).Append("\" y=\"").Append(Dx.Emu(y)).Append("\"/><a:ext cx=\"").Append(Dx.Emu(w)).Append("\" cy=\"").Append(Dx.Emu(h)).Append("\"/></a:xfrm>");
        if (custom is not null)
        {
            sb.Append(custom);
        }
        else
        {
            sb.Append("<a:prstGeom prst=\"").Append(Dx.Esc(s.Geometry)).Append("\"><a:avLst>");
            WriteAdjust(sb, s);
            sb.Append("</a:avLst></a:prstGeom>");
        }

        MarkupNode overrides = s.Presentation?.ShapeProperties ?? default;
        WriteFill(sb, s, overrides);
        WriteLine(sb, s, overrides);
        sb.Append("<a:effectLst/></dsp:spPr>");
        WriteStyle(sb, s);
        if (s.Paragraphs.Count > 0)
        {
            WriteText(sb, s);
        }

        sb.Append("</dsp:sp>");
    }

    private static void WriteAdjust(StringBuilder sb, OutShape s)
    {
        LayoutInstance n = s.Instance;
        if (RecursiveAI.OneDocument.Rendering.Drawing.GeometryDefinition.Preset(s.Geometry) is not { } def)
        {
            return;
        }

        string[] names = [.. def.AdjustNames];
        if (n.AdjustOverride is { } ov && names.Length > 0)
        {
            sb.Append("<a:gd name=\"").Append(names[0]).Append("\" fmla=\"val ").Append(Dx.F(ov)).Append("\"/>");
            return;
        }

        if (s.IsConnector && s.Geometry == "rightArrow" && names.Length >= 2)
        {
            // Office's block arrow in diagrams: a thin shaft and a short head.
            sb.Append("<a:gd name=\"").Append(names[0]).Append("\" fmla=\"val 60000\"/><a:gd name=\"").Append(names[1]).Append("\" fmla=\"val 50000\"/>");
            return;
        }

        if (n.Shape is { } sh)
        {
            foreach ((int idx, double val) in sh.Adjust)
            {
                if (idx >= 1 && idx <= names.Length)
                {
                    sb.Append("<a:gd name=\"").Append(names[idx - 1]).Append("\" fmla=\"val ").Append(Dx.F(val)).Append("\"/>");
                }
            }
        }
    }

    private static void WriteFill(StringBuilder sb, OutShape s, MarkupNode overrides)
    {
        if (s.IsConnector && s.Instance.Route is not null)
        {
            sb.Append("<a:noFill/>");
            return;
        }

        foreach (MarkupNode c in overrides.IsNull ? [] : overrides.ChildElements)
        {
            if (c.LocalNameString is "solidFill" or "gradFill" or "pattFill" or "noFill")
            {
                DrawingXml.Write(c, sb, 0);
                return;
            }
        }

        if (s.FillXml is { } fill)
        {
            sb.Append("<a:solidFill>").Append(fill).Append("</a:solidFill>");
        }
        else
        {
            sb.Append("<a:noFill/>");
        }
    }

    private static void WriteLine(StringBuilder sb, OutShape s, MarkupNode overrides)
    {
        foreach (MarkupNode c in overrides.IsNull ? [] : overrides.ChildElements)
        {
            if (c.LocalNameString == "ln")
            {
                DrawingXml.Write(c, sb, 0);
                return;
            }
        }

        bool oneD = s.IsConnector && s.Instance.Route is not null;
        if (s.LineXml is null || (s.IsConnector && !oneD))
        {
            sb.Append("<a:ln><a:noFill/></a:ln>");
            return;
        }

        sb.Append("<a:ln w=\"").Append(oneD ? 19050 : 12700).Append("\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill>").Append(s.LineXml).Append("</a:solidFill><a:prstDash val=\"solid\"/>");
        if (oneD && s.Instance.Route is { } r)
        {
            if (r.BeginArrow)
            {
                sb.Append("<a:headEnd type=\"triangle\"/>");
            }

            if (r.EndArrow)
            {
                sb.Append("<a:tailEnd type=\"triangle\"/>");
            }
        }

        sb.Append("</a:ln>");
    }

    private static void WriteStyle(StringBuilder sb, OutShape s)
    {
        string fill = s.FillXml ?? "<a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/>";
        string line = s.LineXml ?? "<a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/>";
        string text = s.TextXml ?? "<a:schemeClr val=\"lt1\"/>";
        sb.Append("<dsp:style><a:lnRef idx=\"").Append(s.Style.LineIndex).Append("\">").Append(line).Append("</a:lnRef><a:fillRef idx=\"").Append(s.Style.FillIndex).Append("\">").Append(fill)
            .Append("</a:fillRef><a:effectRef idx=\"").Append(s.Style.EffectIndex).Append("\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"").Append(Dx.Esc(s.Style.FontIndex)).Append("\">")
            .Append(text).Append("</a:fontRef></dsp:style>");
    }

    private static void WriteText(StringBuilder sb, OutShape s)
    {
        (double tx, double ty, double tw, double th) = s.TextRectangle;
        (double ml, double mt, double mr, double mb) = s.Margins;
        sb.Append("<dsp:txBody><a:bodyPr spcFirstLastPara=\"0\" vert=\"horz\" wrap=\"square\" lIns=\"").Append(Dx.Emu(ml)).Append("\" tIns=\"").Append(Dx.Emu(mt)).Append("\" rIns=\"").Append(Dx.Emu(mr))
            .Append("\" bIns=\"").Append(Dx.Emu(mb)).Append("\" numCol=\"1\" spcCol=\"1270\" anchor=\"").Append(s.Anchor).Append("\" anchorCtr=\"0\"><a:noAutofit/></a:bodyPr><a:lstStyle/>");
        foreach (ParaSpec p in s.Paragraphs)
        {
            sb.Append("<a:p><a:pPr marL=\"").Append(Dx.Emu(p.MarginLeft)).Append("\" lvl=\"").Append(p.Level).Append("\" indent=\"").Append(Dx.Emu(p.Indent)).Append("\" algn=\"").Append(p.Align)
                .Append("\" defTabSz=\"").Append(Dx.Emu(p.MaxSize * 0.5)).Append("\"><a:lnSpc><a:spcPct val=\"").Append((int)Math.Round(p.LineSpacing * 100000)).Append("\"/></a:lnSpc><a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:spcAft><a:spcPct val=\"35000\"/></a:spcAft>");
            sb.Append(p.Bullet is { Length: > 0 } b ? "<a:buFont typeface=\"Arial\"/><a:buChar char=\"" + Dx.Esc(b) + "\"/>" : "<a:buNone/>");
            sb.Append("</a:pPr>");
            foreach (RunSpec r in p.Runs)
            {
                string attrs = " lang=\"en-US\" sz=\"" + (int)Math.Round(r.Size * 100) + "\"" + (r.Bold ? " b=\"1\"" : string.Empty) + (r.Italic ? " i=\"1\"" : string.Empty) + " kern=\"1200\"";
                string inner;
                inner = r.ColorXml is null ? string.Empty : r.ColorXml.StartsWith("<a:solidFill", StringComparison.Ordinal) ? r.ColorXml : "<a:solidFill>" + r.ColorXml + "</a:solidFill>";
                if (r.Family is { } fam)
                {
                    inner += "<a:latin typeface=\"" + Dx.Esc(fam) + "\"/>";
                }

                if (r.LineBreak)
                {
                    sb.Append("<a:br><a:rPr").Append(attrs).Append("/></a:br>");
                }
                else
                {
                    sb.Append("<a:r><a:rPr").Append(attrs).Append(inner.Length == 0 ? "/>" : ">" + inner + "</a:rPr>").Append("<a:t>").Append(Dx.Esc(r.Text)).Append("</a:t></a:r>");
                }
            }

            sb.Append("</a:p>");
        }

        sb.Append("</dsp:txBody><dsp:txXfrm><a:off x=\"").Append(Dx.Emu(tx)).Append("\" y=\"").Append(Dx.Emu(ty)).Append("\"/><a:ext cx=\"").Append(Dx.Emu(tw)).Append("\" cy=\"").Append(Dx.Emu(th)).Append("\"/></dsp:txXfrm>");
    }

    // A connector's polyline as a custom geometry whose path coordinates are EMU relative to the bounding box.
    private static (double X, double Y, double W, double H, string Xml) RouteGeometry(ConnectorRoute route)
    {
        double minX = route.Points.Min(p => p.X), maxX = route.Points.Max(p => p.X);
        double minY = route.Points.Min(p => p.Y), maxY = route.Points.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        long ew = Math.Max(1, Dx.Emu(w)), eh = Math.Max(1, Dx.Emu(h));
        var sb = new StringBuilder();
        sb.Append("<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"").Append(ew).Append("\" h=\"").Append(eh).Append("\" fill=\"none\">");
        (double X, double Y)[] pts = [.. route.Points];
        string Pt(int i) => "<a:pt x=\"" + Dx.Emu(pts[i].X - minX) + "\" y=\"" + Dx.Emu(pts[i].Y - minY) + "\"/>";
        sb.Append("<a:moveTo>").Append(Pt(0)).Append("</a:moveTo>");
        if (route.Curved && pts.Length == 4)
        {
            // An S curve through the elbow: the control points are the bend's two corners.
            sb.Append("<a:cubicBezTo>").Append(Pt(1)).Append(Pt(2)).Append(Pt(3)).Append("</a:cubicBezTo>");
        }
        else
        {
            for (int i = 1; i < pts.Length; i++)
            {
                sb.Append("<a:lnTo>").Append(Pt(i)).Append("</a:lnTo>");
            }
        }

        sb.Append("</a:path></a:pathLst></a:custGeom>");
        return (minX, minY, w, h, sb.ToString());
    }
}
