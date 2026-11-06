using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using static RecursiveAI.OneDocument.Ppt.Binary.PptXml;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>What kind of page a shape tree is written for.</summary>
internal enum PageKind
{
    Slide,
    Master,
    Layout,
    NotesPage,
    NotesMaster,
    Handout,
}

/// <summary>Writes the shapes of one drawing as PresentationML <c>p:sp</c>, <c>p:pic</c>, <c>p:cxnSp</c>, <c>p:grpSp</c> and table graphic frames.</summary>
internal sealed partial class PptShapeWriter
{
    private readonly PptConversion _conv;
    private readonly PageOut _page;
    private readonly StringBuilder _sb;
    private readonly PageKind _kind;
    private readonly ArtProperties _defaults;
    private readonly IReadOnlyList<PptTextBody> _listTexts;
    private readonly ArtReadContext _read;
    private readonly bool _skipText;
    private int _ordinal;

    public PptShapeWriter(PptConversion conv, PageOut page, StringBuilder sb, PageKind kind, IReadOnlyList<PptTextBody> listTexts, bool placeholdersOnly = false)
    {
        _conv = conv;
        _page = page;
        _sb = sb;
        _kind = kind;
        _listTexts = listTexts;
        _defaults = conv.P.DrawingGroup?.DefaultProperties ?? ArtProperties.Empty;
        _read = conv.P.File.ReadContext;
        _skipText = placeholdersOnly;
    }

    /// <summary>Placeholder facts read from a shape's client data.</summary>
    internal readonly record struct Placeholder(int Id, int Size, uint Position);

    public static Placeholder? ReadPlaceholder(ArtShape shape)
    {
        ArtRecord? atom = shape.ClientData?.Find(PptRecordType.PlaceholderAtom);
        if (atom is null || atom.Payload.Length < 8)
        {
            return null;
        }

        uint position = (uint)atom.Int32At(0);
        ReadOnlySpan<byte> p = atom.Payload.Span;
        // [MS-PPT] §2.7.8: position 0xFFFFFFFF marks "not a placeholder".
        return position == 0xFFFFFFFF ? null : new Placeholder(p[4], p[5], position);
    }

    /// <summary>
    /// Writes the date, footer and slide number placeholders that the page's header and footer settings turn on. PowerPoint 97-2003 keeps no
    /// such shapes on a slide: the master's placeholders are shown with the text of the <c>HeadersFooters</c> record, so the converter makes the
    /// slide's own placeholders (positions come from the master through the layout). Types the drawing already has are left alone.
    /// </summary>
    public void WriteHeaderFooterPlaceholders(PptPage page, IReadOnlyList<ArtShape> existing)
    {
        ArtRecord? hf = _conv.HeadersFooters(page);
        ArtRecord? atom = hf?.Find(PptRecordType.HeadersFootersAtom);
        if (hf is null || atom is null || atom.Payload.Length < 4)
        {
            return;
        }

        // [MS-PPT] §2.4.15.2 HeadersFootersAtom: formatId (2), then flags: 0 date, 1 today's date, 2 user date, 3 slide number, 4 header, 5 footer.
        int formatId = atom.UInt16At(0);
        int flags = atom.UInt16At(2);
        var have = new HashSet<int>();
        foreach (ArtShape s in existing)
        {
            if (ReadPlaceholder(s) is { } ph)
            {
                have.Add(ph.Id);
            }
        }

        string? footer = _conv.HeaderFooterString(page, 2);
        string? userDate = _conv.HeaderFooterString(page, 0);
        if ((flags & 1) != 0 && !have.Contains(7))
        {
            string body = (flags & 4) != 0 && userDate is not null
                ? "<a:r><a:rPr lang=\"en-US\"/><a:t>" + Escape(userDate) + "</a:t></a:r>"
                : "<a:fld id=\"{" + FieldId(_page.NextField()) + "}\" type=\"datetime" + Math.Clamp(formatId + 1, 1, 13) + "\"><a:rPr lang=\"en-US\"/><a:t>‹date›</a:t></a:fld>";
            WriteHeaderFooter("Date Placeholder", "dt", 10, body);
        }

        if ((flags & 0x20) != 0 && !have.Contains(9) && footer is not null)
        {
            WriteHeaderFooter("Footer Placeholder", "ftr", 11, "<a:r><a:rPr lang=\"en-US\"/><a:t>" + Escape(footer) + "</a:t></a:r>");
        }

        if ((flags & 8) != 0 && !have.Contains(8))
        {
            WriteHeaderFooter("Slide Number Placeholder", "sldNum", 12, "<a:fld id=\"{" + FieldId(_page.NextField()) + "}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld>");
        }
    }

    private static string FieldId(int n) => "00000000-0000-4000-8000-" + n.ToString("X12", CultureInfo.InvariantCulture);

    private void WriteHeaderFooter(string name, string type, int idx, string paragraphContent)
    {
        int id = _page.NextShapeId++;
        _sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\" name=\"").Append(name).Append("\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"")
            .Append(type).Append("\" sz=\"quarter\" idx=\"").Append(idx.ToString(CultureInfo.InvariantCulture)).Append("\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p>")
            .Append(paragraphContent).Append("</a:p></p:txBody></p:sp>");
    }

    /// <summary>The fill of a page's background shape as DrawingML, or empty when the shape sets none.</summary>
    public string BackgroundFill(ArtShape background) => FillXml(background);

    public void WriteShapes(IReadOnlyList<ArtShape> shapes)
    {
        foreach (ArtShape shape in shapes)
        {
            WriteShape(shape);
        }
    }

    // ---- property access: the shape's own table first, then the drawing group's defaults ----

    private int Int(ArtShape s, int id, int fallback) =>
        s.Properties.Has(id) ? s.Properties.GetInt(id, fallback) : _defaults.GetInt(id, fallback);

    private bool Has(ArtShape s, int id) => s.Properties.Has(id) || _defaults.Has(id);

    // A boolean property set tells whether a flag is set only when its "use" bit is set; otherwise the question goes to the next table.
    private static bool? FlagOf(ArtProperties p, int set, int flag)
    {
        bool whenFalse = p.GetFlag(set, flag, false);
        bool whenTrue = p.GetFlag(set, flag, true);
        return whenFalse == whenTrue ? whenFalse : null;
    }

    private bool? Flag(ArtShape s, int set, int flag) => FlagOf(s.Properties, set, flag) ?? FlagOf(_defaults, set, flag);

    private ArtColor? Color(ArtShape s, int id) => s.Properties.GetColor(id) ?? _defaults.GetColor(id);

    // ---- geometry ----

    private static ArtRect? Bounds(ArtShape s)
    {
        if (s.ChildAnchor is { } child)
        {
            return child;
        }

        ArtRecord? a = s.ClientAnchor;
        if (a is null)
        {
            return null;
        }

        // [MS-PPT] §2.7.2: an 8-byte anchor is a SmallRectStruct and a 16-byte one a RectStruct; both are top, left, right, bottom.
        if (a.Payload.Length >= 16)
        {
            return new ArtRect(a.Int32At(4), a.Int32At(0), a.Int32At(8), a.Int32At(12));
        }

        if (a.Payload.Length >= 8)
        {
            ReadOnlySpan<byte> p = a.Payload.Span;
            return new ArtRect(
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(p[2..]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(p),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(p[4..]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(p[6..]));
        }

        return null;
    }

    private string Xfrm(ArtShape s, ArtRect? bounds, bool group, string tag = "a:xfrm")
    {
        if (bounds is not { } b)
        {
            return "";
        }

        var attrs = new StringBuilder();
        // [MS-ODRAW] §2.3.2.1: rotation is a 16.16 fixed-point angle in degrees, clockwise.
        long rot = (long)Int(s, ArtPropertyId.Rotation, 0) * 60000 / 65536;
        rot = ((rot % 21600000) + 21600000) % 21600000;
        if (rot != 0)
        {
            attrs.Append(" rot=\"").Append(Num(rot)).Append('"');
        }

        if ((s.Flags & ArtShapeAttributes.FlipH) != 0)
        {
            attrs.Append(" flipH=\"1\"");
        }

        if ((s.Flags & ArtShapeAttributes.FlipV) != 0)
        {
            attrs.Append(" flipV=\"1\"");
        }

        long x = Emu(Math.Min(b.Left, b.Right));
        long y = Emu(Math.Min(b.Top, b.Bottom));
        long cx = Emu(Math.Abs(b.Width));
        long cy = Emu(Math.Abs(b.Height));
        string xml = "<" + tag + attrs + "><a:off x=\"" + Num(x) + "\" y=\"" + Num(y) + "\"/><a:ext cx=\"" + Num(cx) + "\" cy=\"" + Num(cy) + "\"/>";
        if (group)
        {
            ArtRect space = s.GroupSpace is { Width: not 0, Height: not 0 } gs ? gs : b;
            xml += "<a:chOff x=\"" + Num(Emu(space.Left)) + "\" y=\"" + Num(Emu(space.Top)) + "\"/><a:chExt cx=\"" + Num(Emu(Math.Abs(space.Width))) + "\" cy=\"" + Num(Emu(Math.Abs(space.Height))) + "\"/>";
        }

        return xml + "</" + tag + ">";
    }

    // ---- one shape ----

    public void WriteShape(ArtShape s)
    {
        if ((s.Flags & ArtShapeAttributes.Deleted) != 0)
        {
            return;
        }

        _conv.Loss.Offset = s.Record.Offset;
        if (s.ClientData?.Find(PptRecordType.AnimationInfo) is not null)
        {
            _conv.AnimatedShapes++;
        }

        if (s.IsGroup)
        {
            if (s.Properties.GetInt(ArtPropertyId.TableProperties, 0) != 0 && s.Children.Count > 0)
            {
                WriteTable(s);
            }
            else
            {
                WriteGroup(s);
            }

            return;
        }

        Placeholder? ph = ReadPlaceholder(s);
        bool picture = s.ShapeType == 75 && s.Properties.GetInt(ArtPropertyId.Pib, 0) > 0;
        if (picture)
        {
            if (!TryWriteOle(s, ph))
            {
                WritePicture(s, ph);
            }

            return;
        }

        if (_kind == PageKind.Layout && ph is null)
        {
            return;
        }

        bool connector = PptShapeTypes.IsConnector(s.ShapeType) || (s.Flags & ArtShapeAttributes.Connector) != 0;
        if (s.ShapeType == 75 && ph is null)
        {
            _conv.Loss.Report("ppt.shape-approximated", "shape type", 1, "A picture frame without a picture is written as a rectangle.");
        }

        WriteSp(s, ph, connector);
    }

    private string Id(ArtShape s, out string name)
    {
        string? given = s.Properties.GetString(ArtPropertyId.Name);
        int id = _page.NextShapeId++;
        name = string.IsNullOrEmpty(given) ? "Shape " + id.ToString(CultureInfo.InvariantCulture) : given;
        string descr = s.Properties.GetString(ArtPropertyId.Description) is { Length: > 0 } d ? " descr=\"" + Escape(d) + "\"" : "";
        return "<p:cNvPr id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" name=\"" + Escape(name) + "\"" + descr;
    }

    private string ShapeHyperlink(ArtShape s)
    {
        if (s.ClientData is null)
        {
            return "";
        }

        foreach (ArtRecord info in s.ClientData.FindAll(PptRecordType.InteractiveInfo))
        {
            if (PptActions.TryHyperlink(info, _page, out string rel, out string action))
            {
                return "<a:hlinkClick r:id=\"" + rel + "\"" + action + "/>";
            }
        }

        return "";
    }

    private void WriteGroup(ArtShape g)
    {
        ArtRect? bounds = Bounds(g) ?? g.GroupSpace;
        string head = Id(g, out _);
        _sb.Append("<p:grpSp><p:nvGrpSpPr>").Append(head).Append("/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr>");
        _sb.Append(Xfrm(g, bounds, group: true));
        _sb.Append("</p:grpSpPr>");
        foreach (ArtShape child in g.Children)
        {
            WriteShape(child);
        }

        _sb.Append("</p:grpSp>");
    }

    private void WritePicture(ArtShape s, Placeholder? ph)
    {
        int index = s.Properties.GetInt(ArtPropertyId.Pib, 0);
        string rel = _page.AddImage(index);
        if (rel.Length == 0)
        {
            _conv.Loss.Report("ppt.picture-missing", "picture", 1, "A picture shape refers to a picture that is not in the file; the shape is written without an image.");
            WriteSp(s, ph, false);
            return;
        }

        ArtRect? bounds = Bounds(s);
        string head = Id(s, out _);
        _sb.Append("<p:pic><p:nvPicPr>").Append(head).Append('>').Append(ShapeHyperlink(s)).Append("</p:cNvPr><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr>").Append(ph is { } picPh ? PlaceholderXml(picPh) : "").Append("</p:nvPr></p:nvPicPr>");
        _sb.Append("<p:blipFill><a:blip r:embed=\"").Append(rel).Append("\"/>");
        // [MS-ODRAW] §2.3.23: crops are 16.16 fractions of the picture's size; DrawingML srcRect is in 1/1000 percent.
        int top = Crop(Int(s, ArtPropertyId.CropFromTop, 0));
        int bottom = Crop(Int(s, ArtPropertyId.CropFromBottom, 0));
        int left = Crop(Int(s, ArtPropertyId.CropFromLeft, 0));
        int right = Crop(Int(s, ArtPropertyId.CropFromRight, 0));
        if ((top | bottom | left | right) != 0)
        {
            _sb.Append("<a:srcRect");
            AppendCrop("l", left);
            AppendCrop("t", top);
            AppendCrop("r", right);
            AppendCrop("b", bottom);
            _sb.Append("/>");
        }

        _sb.Append("<a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>");
        _sb.Append(Xfrm(s, bounds, group: false)).Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>").Append(LineXml(s)).Append("</p:spPr></p:pic>");
    }

    private void AppendCrop(string side, int value)
    {
        if (value != 0)
        {
            _sb.Append(' ').Append(side).Append("=\"").Append(Num(value)).Append('"');
        }
    }

    private static int Crop(int fixedPoint) => (int)Math.Clamp((long)fixedPoint * 100000 / 65536, -100000, 100000);

    private void WriteSp(ArtShape s, Placeholder? ph, bool connector)
    {
        ArtRect? bounds = Bounds(s);
        string head = Id(s, out _);
        bool textBox = s.ShapeType == 202 && ph is null;
        string phXml = ph is { } p ? PlaceholderXml(p) : "";
        string tag = connector ? "p:cxnSp" : "p:sp";
        string nv = connector ? "p:nvCxnSpPr" : "p:nvSpPr";
        string cNv = connector ? "<p:cNvCxnSpPr/>" : textBox ? "<p:cNvSpPr txBox=\"1\"/>" : ph is not null ? "<p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr>" : "<p:cNvSpPr/>";
        _sb.Append('<').Append(tag).Append("><").Append(nv).Append('>').Append(head);
        string link = ShapeHyperlink(s);
        _sb.Append(link.Length == 0 ? "/>" : ">" + link + "</p:cNvPr>").Append(cNv).Append("<p:nvPr>").Append(phXml).Append("</p:nvPr></").Append(nv).Append("><p:spPr>");
        if (bounds is not null)
        {
            _sb.Append(Xfrm(s, bounds, group: false));
        }

        if (ph is null || bounds is not null)
        {
            _sb.Append(GeometryXml(s, connector));
        }

        if (WordArtText(s) is { Length: > 0 } && !_skipText)
        {
            _sb.Append("<a:noFill/><a:ln><a:noFill/></a:ln>");
        }
        else if (!_skipText || ph is null)
        {
            _sb.Append(FillXml(s));
            _sb.Append(LineXml(s));
            _sb.Append(ShadowXml(s));
        }

        _sb.Append("</p:spPr>");
        if (!connector)
        {
            WriteText(s, ph);
        }

        _sb.Append("</").Append(tag).Append('>');
    }

    private string PlaceholderXml(Placeholder ph)
    {
        string? type = PptShapeTypes.PlaceholderType(ph.Id);
        if (type is null)
        {
            _conv.Loss.Report("ppt.placeholder-unknown", "placeholder", 1, "A placeholder of an unknown type is written as a plain shape.");
            return "";
        }

        if (_kind == PageKind.Master)
        {
            // A slide master holds only title, body, date, footer and slide number placeholders; PowerPoint refuses the file when a
            // master carries a subtitle, a centred title or an object placeholder (observed with a converted title master). The layouts keep
            // the specific types and inherit from these.
            type = type switch
            {
                "title" or "ctrTitle" => "title",
                "dt" or "ftr" or "sldNum" => type,
                _ => "body",
            };
        }

        bool titleLike = type is "title" or "ctrTitle";
        string attrs = type == "obj" ? "" : " type=\"" + type + "\"";
        string idx = "";
        if (!titleLike)
        {
            int number = type switch
            {
                "dt" => 10,
                "ftr" => 11,
                "sldNum" => 12,
                "hdr" => 13,
                "sldImg" => 2,
                _ => ++_ordinal,
            };
            idx = " idx=\"" + number.ToString(CultureInfo.InvariantCulture) + "\"";
        }

        string size = ph.Size == 1 ? " sz=\"half\"" : ph.Size == 2 ? " sz=\"quarter\"" : "";
        return "<p:ph" + attrs + size + idx + "/>";
    }

    // ---- geometry, fill, line, shadow ----

    private string GeometryXml(ArtShape s, bool connector)
    {
        string? preset = PptShapeTypes.Preset(s.ShapeType);
        if (s.ShapeType == 0 || (preset is null && s.Properties.Has(ArtPropertyId.Vertices)))
        {
            string? custom = PptGeometry.Custom(s, _conv.Loss);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (preset is null)
        {
            if (s.ShapeType != 0)
            {
                _conv.Loss.Report("ppt.shape-approximated", "shape type", 1, "A shape type with no DrawingML preset is written as a rectangle (type " + s.ShapeType.ToString(CultureInfo.InvariantCulture) + ").");
            }

            preset = connector ? "line" : "rect";
        }

        return "<a:prstGeom prst=\"" + preset + "\">" + Adjusts(s, preset) + "</a:prstGeom>";
    }

    private string Adjusts(ArtShape s, string preset)
    {
        if (!s.Properties.Has(ArtPropertyId.Adjust1))
        {
            return "<a:avLst/>";
        }

        if (preset == "roundRect")
        {
            // The rounded rectangle's corner radius is adjust1 out of 21600 of the shorter side in OfficeArt; DrawingML counts 100000.
            long adj = Math.Clamp((long)s.Properties.GetInt(ArtPropertyId.Adjust1, 3600) * 100000 / 21600, 0, 50000);
            return "<a:avLst><a:gd name=\"adj\" fmla=\"val " + Num(adj) + "\"/></a:avLst>";
        }

        _conv.Loss.Report("ppt.shape-adjust-dropped", "shape adjustments", 1, "Adjustment handles of a preset shape are not converted; the shape uses its default proportions.");
        return "<a:avLst/>";
    }

    private string FillXml(ArtShape s)
    {
        bool? filled = Flag(s, ArtPropertyId.FillBooleans, 0x10);
        if (filled == false)
        {
            return "<a:noFill/>";
        }

        if (filled is null && !Has(s, ArtPropertyId.FillColor) && !Has(s, ArtPropertyId.FillType))
        {
            return "";
        }

        int type = Int(s, ArtPropertyId.FillType, 0);
        long opacity = Int(s, ArtPropertyId.FillOpacity, 0x10000);
        ArtColor fore = Color(s, ArtPropertyId.FillColor) ?? new ArtColor(0x00FFFFFF);
        ArtColor back = Color(s, ArtPropertyId.FillBackColor) ?? new ArtColor(0x00FFFFFF);
        switch (type)
        {
            case 3:
            case 2:
            {
                int blip = Int(s, ArtPropertyId.FillBlip, 0);
                string rel = blip > 0 ? _page.AddImage(blip) : "";
                if (rel.Length > 0)
                {
                    string mode = type == 2 ? "<a:tile tx=\"0\" ty=\"0\" sx=\"100000\" sy=\"100000\" flip=\"none\" algn=\"tl\"/>" : "<a:stretch><a:fillRect/></a:stretch>";
                    return "<a:blipFill dpi=\"0\" rotWithShape=\"1\"><a:blip r:embed=\"" + rel + "\"/><a:srcRect/>" + mode + "</a:blipFill>";
                }

                break;
            }

            case 4:
            case 5:
            case 6:
                return GradientXml(s, type, fore, back, opacity);
            case 1:
                _conv.Loss.Report("ppt.fill-approximated", "fill", 1, "A pattern fill is written as a solid fill in its foreground colour.");
                break;
            case 7:
                return "<a:grpFill/>";
        }

        return "<a:solidFill>" + _page.ArtColorXml(fore, opacity) + "</a:solidFill>";
    }

    private string GradientXml(ArtShape s, int type, ArtColor fore, ArtColor back, long opacity)
    {
        var stops = new List<(long Pos, ArtColor Color)>();
        if (s.Properties.TryGet(ArtPropertyId.FillShadeColors, out ArtProperty shade) && shade.IsComplex)
        {
            foreach (ReadOnlyMemory<byte> element in s.Properties.GetArray(ArtPropertyId.FillShadeColors, out int width))
            {
                if (width >= 8)
                {
                    uint color = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(element.Span);
                    uint position = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(element.Span[4..]);
                    stops.Add((Math.Clamp((long)position * 100000 / 65536, 0, 100000), new ArtColor(color)));
                }
            }
        }

        if (stops.Count == 0)
        {
            stops.Add((0, fore));
            stops.Add((100000, back));
        }

        var sb = new StringBuilder("<a:gradFill rotWithShape=\"1\"><a:gsLst>");
        foreach ((long pos, ArtColor color) in stops)
        {
            sb.Append("<a:gs pos=\"").Append(Num(pos)).Append("\">").Append(_page.ArtColorXml(color, opacity)).Append("</a:gs>");
        }

        sb.Append("</a:gsLst>");
        if (type == 4)
        {
            // [MS-ODRAW] §2.3.7.14: fillAngle is counter-clockwise with zero pointing up; DrawingML measures clockwise from the x axis.
            // Settled by the converted-file comparison cases rather than by the text alone.
            long angle = (long)Int(s, ArtPropertyId.FillAngle, 0) * 60000 / 65536;
            long ooxml = (((270L * 60000) - angle) % 21600000 + 21600000) % 21600000;
            sb.Append("<a:lin ang=\"").Append(Num(ooxml)).Append("\" scaled=\"0\"/>");
        }
        else
        {
            sb.Append("<a:path path=\"").Append(type == 5 ? "rect" : "shape").Append("\"><a:fillToRect l=\"50000\" t=\"50000\" r=\"50000\" b=\"50000\"/></a:path>");
        }

        return sb.Append("</a:gradFill>").ToString();
    }

    private string LineXml(ArtShape s)
    {
        bool? line = Flag(s, ArtPropertyId.LineBooleans, 0x8);
        if (line == false)
        {
            return "<a:ln><a:noFill/></a:ln>";
        }

        if (line is null && !Has(s, ArtPropertyId.LineColor) && !Has(s, ArtPropertyId.LineWidth))
        {
            return "";
        }

        long width = Int(s, ArtPropertyId.LineWidth, 9525);
        ArtColor color = Color(s, ArtPropertyId.LineColor) ?? new ArtColor(0);
        long opacity = Int(s, ArtPropertyId.LineOpacity, 0x10000);
        var attrs = new StringBuilder(" w=\"").Append(Num(Math.Max(0, width))).Append('"');
        attrs.Append(Int(s, ArtPropertyId.LineEndCapStyle, 2) switch
        {
            0 => " cap=\"rnd\"",
            1 => " cap=\"sq\"",
            _ => " cap=\"flat\"",
        });
        string? cmpd = Int(s, ArtPropertyId.LineStyle, 0) switch
        {
            1 => "dbl",
            2 => "thickThin",
            3 => "thinThick",
            4 => "tri",
            _ => null,
        };
        if (cmpd is not null)
        {
            attrs.Append(" cmpd=\"").Append(cmpd).Append('"');
        }

        var sb = new StringBuilder("<a:ln").Append(attrs).Append('>');
        sb.Append("<a:solidFill>").Append(_page.ArtColorXml(color, opacity)).Append("</a:solidFill>");
        string? dash = Int(s, ArtPropertyId.LineDashing, 0) switch
        {
            1 => "sysDash",
            2 => "sysDot",
            3 => "sysDashDot",
            4 => "sysDashDotDot",
            5 => "dot",
            6 => "dash",
            7 => "lgDash",
            8 => "dashDot",
            9 => "lgDashDot",
            10 => "lgDashDotDot",
            _ => null,
        };
        if (dash is not null)
        {
            sb.Append("<a:prstDash val=\"").Append(dash).Append("\"/>");
        }

        sb.Append(Int(s, ArtPropertyId.LineJoinStyle, 2) switch
        {
            0 => "<a:bevel/>",
            1 => "<a:miter lim=\"800000\"/>",
            _ => "<a:round/>",
        });
        AppendArrow(sb, "a:headEnd", Int(s, ArtPropertyId.LineStartArrowhead, 0), Int(s, ArtPropertyId.LineStartArrowWidth, 1), Int(s, ArtPropertyId.LineStartArrowLength, 1));
        AppendArrow(sb, "a:tailEnd", Int(s, ArtPropertyId.LineEndArrowhead, 0), Int(s, ArtPropertyId.LineEndArrowWidth, 1), Int(s, ArtPropertyId.LineEndArrowLength, 1));
        return sb.Append("</a:ln>").ToString();
    }

    private static void AppendArrow(StringBuilder sb, string tag, int head, int width, int length)
    {
        string? type = head switch
        {
            1 => "triangle",
            2 => "stealth",
            3 => "diamond",
            4 => "oval",
            5 => "arrow",
            _ => null,
        };
        if (type is null)
        {
            return;
        }

        static string Size(int v) => v switch
        {
            0 => "sm",
            2 => "lg",
            _ => "med",
        };
        sb.Append('<').Append(tag).Append(" type=\"").Append(type).Append("\" w=\"").Append(Size(width)).Append("\" len=\"").Append(Size(length)).Append("\"/>");
    }

    private string ShadowXml(ArtShape s)
    {
        if (Flag(s, ArtPropertyId.ShadowBooleans, 0x2) != true)
        {
            return "";
        }

        long dx = Int(s, ArtPropertyId.ShadowOffsetX, 25400);
        long dy = Int(s, ArtPropertyId.ShadowOffsetY, 25400);
        double dist = Math.Sqrt((double)(dx * dx) + (dy * dy));
        long dir = ((long)Math.Round(Math.Atan2(dy, dx) * 180 / Math.PI * 60000) % 21600000 + 21600000) % 21600000;
        ArtColor color = Color(s, ArtPropertyId.ShadowColor) ?? new ArtColor(0x00808080);
        long opacity = Int(s, ArtPropertyId.ShadowOpacity, 0x10000);
        return "<a:effectLst><a:outerShdw dist=\"" + Num((long)dist) + "\" dir=\"" + Num(dir) + "\" algn=\"tl\" rotWithShape=\"0\">" + _page.ArtColorXml(color, opacity) + "</a:outerShdw></a:effectLst>";
    }

    // ---- text ----

    private PptTextBody? TextOf(ArtShape s)
    {
        ArtRecord? box = s.ClientTextbox;
        if (box is null || box.Payload.Length == 0)
        {
            return null;
        }

        IReadOnlyList<ArtRecord> records = box.Children.Count > 0
            ? box.Children
            : ArtRecords.Parse(box.Payload, box.Offset + ArtRecordHeader.Size, _read);
        IReadOnlyList<PptTextBody> bodies = PptText.ReadBodies(records, _read);
        if (bodies.Count == 0)
        {
            return null;
        }

        PptTextBody body = bodies[0];
        if (body.OutlineReference >= 0)
        {
            if (body.OutlineReference < _listTexts.Count)
            {
                PptTextBody listed = _listTexts[body.OutlineReference].Clone();
                if (body.Ruler is not null)
                {
                    listed.Ruler = body.Ruler;
                }

                return listed;
            }

            _read.Repair("ppt.outline-reference", "A text shape refers to slide list text " + body.OutlineReference + " which does not exist.", body.Offset);
            return null;
        }

        return body;
    }

    // [MS-ODRAW] §2.3.22: WordArt shapes (types 136 to 201 and the text-on-path types) carry their text in the geometry text property, not in a text box.
    private static string? WordArtText(ArtShape s) =>
        s.ShapeType is (>= 24 and <= 31) or (>= 136 and <= 201) ? s.Properties.GetString(0x00C0) : null;

    private void WriteText(ArtShape s, Placeholder? ph)
    {
        if (WordArtText(s) is { Length: > 0 } art && !_skipText)
        {
            _conv.Loss.Report("ppt.wordart-flattened", "WordArt", 1, "WordArt text is written as plain text in the shape; its text effects and path are not converted.");
            ArtColor? color = Color(s, ArtPropertyId.FillColor);
            string fill = color is { } c ? "<a:solidFill>" + _page.ArtColorXml(c) + "</a:solidFill>" : "";
            _sb.Append("<p:txBody><a:bodyPr wrap=\"none\" anchor=\"ctr\" rtlCol=\"0\"/><a:lstStyle/>");
            foreach (string line in art.Replace("\r\n", "\r", StringComparison.Ordinal).Split('\r', '\n'))
            {
                _sb.Append("<a:p><a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"en-US\">").Append(fill).Append("</a:rPr><a:t>").Append(Escape(line)).Append("</a:t></a:r></a:p>");
            }

            _sb.Append("</p:txBody>");
            return;
        }

        PptTextBody? body = _skipText && ph is not null ? null : TextOf(s);
        if (body is null && ph is null && !HasTextProperties(s))
        {
            return;
        }

        _sb.Append("<p:txBody>").Append(BodyProperties(s)).Append("<a:lstStyle/>");
        if (body is null || _skipText)
        {
            _sb.Append("<a:p><a:endParaRPr lang=\"en-US\"/></a:p>");
        }
        else
        {
            PptTextWriter.WriteParagraphs(_sb, body, _page);
        }

        _sb.Append("</p:txBody>");
    }

    private static bool HasTextProperties(ArtShape s) => s.ShapeType is 202 or 1 or 2 or 3 && s.ClientTextbox is not null;

    private string BodyProperties(ArtShape s)
    {
        var a = new StringBuilder();
        // [MS-ODRAW] §2.3.21: insets in EMU; defaults 91440 / 45720 are DrawingML's defaults too, so only explicit values are written.
        AppendInset(a, s, ArtPropertyId.TextLeft, "lIns");
        AppendInset(a, s, ArtPropertyId.TextTop, "tIns");
        AppendInset(a, s, ArtPropertyId.TextRight, "rIns");
        AppendInset(a, s, ArtPropertyId.TextBottom, "bIns");
        if (Has(s, ArtPropertyId.WrapText))
        {
            a.Append(Int(s, ArtPropertyId.WrapText, 0) == 2 ? " wrap=\"none\"" : " wrap=\"square\"");
        }

        if (Has(s, ArtPropertyId.AnchorText))
        {
            int anchor = Int(s, ArtPropertyId.AnchorText, 0);
            a.Append(anchor switch
            {
                1 or 4 => " anchor=\"ctr\"",
                2 or 5 => " anchor=\"b\"",
                _ => " anchor=\"t\"",
            });
            if (anchor >= 3 && anchor <= 5)
            {
                a.Append(" anchorCtr=\"1\"");
            }
        }

        int flow = Int(s, ArtPropertyId.TextFlow, 0);
        string? vert = flow switch
        {
            1 => "vert",
            2 => "vert270",
            3 => "wordArtVert",
            4 => "eaVert",
            _ => null,
        };
        if (vert is not null)
        {
            a.Append(" vert=\"").Append(vert).Append('"');
        }

        a.Append(" rtlCol=\"0\"");
        bool? fit = Flag(s, ArtPropertyId.TextBooleans, 0x2);
        return fit == true ? "<a:bodyPr" + a + "><a:spAutoFit/></a:bodyPr>" : "<a:bodyPr" + a + "/>";
    }

    private static void AppendInset(StringBuilder a, ArtShape s, int id, string name)
    {
        if (s.Properties.Has(id))
        {
            a.Append(' ').Append(name).Append("=\"").Append(Num(Math.Max(0, s.Properties.GetInt(id, 0)))).Append('"');
        }
    }

    // ---- tables ----

    private void WriteTable(ArtShape g)
    {
        // PowerPoint 2003 stores a table as a group: the cells are rectangles in row-major order, the borders lines. The row count is the
        // length of tableRowProperties; the column count follows from the cells.
        IReadOnlyList<ReadOnlyMemory<byte>> rowHeights = g.Properties.GetArray(ArtPropertyId.TableRowProperties, out _);
        List<ArtShape> cells = g.Children.Where(c => !c.IsGroup && !PptShapeTypes.IsConnector(c.ShapeType) && c.ShapeType != 20).ToList();
        int rows = rowHeights.Count;
        if (rows == 0 || cells.Count == 0 || cells.Count % rows != 0)
        {
            _conv.Loss.Report("ppt.table-as-group", "table", 1, "A table group whose cells do not form a regular grid is written as a group of shapes.");
            WriteGroup(g);
            return;
        }

        int cols = cells.Count / rows;
        ArtRect? bounds = Bounds(g);
        if (bounds is null)
        {
            WriteGroup(g);
            return;
        }

        string head = Id(g, out _);
        _sb.Append("<p:graphicFrame><p:nvGraphicFramePr>").Append(head).Append("/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
        ArtRect b = bounds.Value;
        _sb.Append("<p:xfrm><a:off x=\"").Append(Num(Emu(b.Left))).Append("\" y=\"").Append(Num(Emu(b.Top))).Append("\"/><a:ext cx=\"").Append(Num(Emu(b.Width))).Append("\" cy=\"").Append(Num(Emu(b.Height))).Append("\"/></p:xfrm>");
        _sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr/><a:tblGrid>");
        for (int c = 0; c < cols; c++)
        {
            ArtRect? cb = Bounds(cells[c]);
            _sb.Append("<a:gridCol w=\"").Append(Num(Emu(cb?.Width ?? 0))).Append("\"/>");
        }

        _sb.Append("</a:tblGrid>");
        for (int r = 0; r < rows; r++)
        {
            ArtRect? rb = Bounds(cells[r * cols]);
            _sb.Append("<a:tr h=\"").Append(Num(Emu(rb?.Height ?? 0))).Append("\">");
            for (int c = 0; c < cols; c++)
            {
                ArtShape cell = cells[(r * cols) + c];
                PptTextBody? body = TextOf(cell);
                _sb.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>");
                if (body is null)
                {
                    _sb.Append("<a:p><a:endParaRPr lang=\"en-US\"/></a:p>");
                }
                else
                {
                    PptTextWriter.WriteParagraphs(_sb, body, _page);
                }

                _sb.Append("</a:txBody><a:tcPr>");
                string line = LineXml(cell);
                if (line.Length > 0)
                {
                    foreach (string side in new[] { "lnL", "lnR", "lnT", "lnB" })
                    {
                        _sb.Append(line.Replace("<a:ln", "<a:" + side, StringComparison.Ordinal).Replace("</a:ln>", "</a:" + side + ">", StringComparison.Ordinal));
                    }
                }

                _sb.Append(FillXml(cell)).Append("</a:tcPr></a:tc>");
            }

            _sb.Append("</a:tr>");
        }

        _sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        _conv.Loss.Report("ppt.table-borders-approximated", "table borders", 1, "The border lines of a table drawn as separate shapes are written as the borders of each cell's own outline.");
    }
}
