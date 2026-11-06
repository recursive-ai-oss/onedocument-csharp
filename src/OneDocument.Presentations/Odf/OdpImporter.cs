using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Converts an OpenDocument presentation (ODP, OTP, FODP) into an in-memory PPTX package that the presentation hub wraps like any other.
// This file holds the driver, the masters and layouts, the slides and notes and the package assembly; shapes, text and tables are in the
// other parts. Everything is written as PresentationML text, the way the PowerPoint 97-2003 converter does.
internal sealed partial class OdpImporter
{
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string PNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string PmlType = "application/vnd.openxmlformats-officedocument.presentationml.";
    private const string AnimNs = "urn:oasis:names:tc:opendocument:xmlns:animation:1.0";
    private const string SmilNs = "urn:oasis:names:tc:opendocument:xmlns:smil-compatible:1.0";
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string Decl = "xmlns:a=\"" + ANs + "\" xmlns:r=\"" + RNs + "\" xmlns:p=\"" + PNs + "\"";

    // Frames the text of a slide may hold: an OpenDocument page draws at most this deep through groups.
    private const int MaxGroupDepth = 24;

    private readonly OdfPackage _pkg;
    private readonly OpenContext _ctx;
    private readonly ILossSink _loss;
    private readonly CancellationToken _ct;
    private readonly OdfStyleSet _styles;
    private readonly bool _template;
    private readonly Dictionary<string, string> _fonts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _footerDecls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dateDecls = new(StringComparer.Ordinal);
    private readonly List<(string Name, string ContentType, byte[] Data)> _media = [];
    private readonly Dictionary<string, string> _mediaByHref = new(StringComparer.Ordinal);
    private OpcPackage _out = null!;
    private int _depth;

    public OdpImporter(OdfPackage package, OpenContext context, ILossSink loss, bool template, CancellationToken cancellationToken)
    {
        _pkg = package;
        _ctx = context;
        _loss = loss;
        _template = template;
        _ct = cancellationToken;
        _styles = package.Styles;
    }

    // One part being written: its XML text, relationships and the next shape id.
    private sealed class Part
    {
        private int _nextId = 2;

        public Part(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public StringBuilder Xml { get; } = new();

        public List<Relationship> Rels { get; } = [];

        public int NextId() => _nextId++;

        public string AddRel(string type, string target, bool external = false)
        {
            foreach (Relationship r in Rels)
            {
                if (r.Type == type && r.Target == target)
                {
                    return r.Id;
                }
            }

            string id = "rId" + (Rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            Rels.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            return id;
        }
    }

    private sealed class MasterInfo
    {
        public string Name = "";
        public string Display = "";
        public MarkupNode Node;
        public int Number;
        public long Width, Height;
        public Part Part = null!;
        public readonly Dictionary<string, LayoutInfo> Layouts = new(StringComparer.Ordinal);
        public long Id;
    }

    private sealed class LayoutInfo
    {
        public int Number;
        public string Name = "";
        public string Type = "cust";
        public MarkupNode PageLayout;
        public MasterInfo Master = null!;
        public long Id;
    }

    private sealed class PageInfo
    {
        public MarkupNode Node;
        public MasterInfo Master = null!;
        public LayoutInfo Layout = null!;
        public int Number;
        public bool Hidden;
    }

    private static string? A(MarkupNode n, string ns, string local) => n.IsNull ? null : n.GetAttribute(ns, local);

    private static bool Is(MarkupNode n, string ns, string local) => n.Kind == MarkupNodeKind.Element && n.IsElement(ns, local);

    private static MarkupNode Child(MarkupNode n, string ns, string local) => OdfMarkup.Child(n, ns, local);

    private void Defect(DefectKind kind, string code, DefectSeverity severity, string message, MarkupNode at, string part = "content.xml") =>
        _ctx.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InPart(part, Math.Max(0, at.IsNull ? 0 : at.SourceStart))));

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    private void CheckDepth(MarkupNode at)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, _depth, DefectLocation.InPart("content.xml", Math.Max(0, at.SourceStart)));
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(Prolog + xml);

    // ---- driver ---------------------------------------------------------------------------------------------------------------

    public OpcPackage Convert()
    {
        _out = OpcPackage.Create(_ctx.Limits);
        try
        {
            MarkupNode content = _pkg.ContentRoot;
            if (content.IsNull)
            {
                throw new OfficeFormatException(DefectKind.Structure, "odf.no-content", "The OpenDocument file has no content.xml.", DefectLocation.InPart("content.xml", 0));
            }

            MarkupNode body = Child(Child(content, OdfNamespaces.Office, "body"), OdfNamespaces.Office, "presentation");
            if (body.IsNull)
            {
                string actual = OdfMediaTypes.FromBody(_pkg.Body);
                throw new OfficeFormatException(DefectKind.Structure, "odf.not-presentation", actual.Length > 0
                    ? "The OpenDocument file holds " + actual + ", not a presentation."
                    : "The OpenDocument file has no office:presentation body.", DefectLocation.InPart("content.xml", Math.Max(0, content.SourceStart)));
            }

            ReadFonts(content);
            ReadFonts(_pkg.StylesRoot);
            ReadDeclarations(body);
            Build(body);
            return _out;
        }
        catch
        {
            _out.Dispose();
            throw;
        }
    }

    private void ReadFonts(MarkupNode root)
    {
        MarkupNode decls = Child(root, OdfNamespaces.Office, "font-face-decls");
        if (decls.IsNull)
        {
            return;
        }

        foreach (MarkupNode f in decls.ChildElements)
        {
            if (Is(f, OdfNamespaces.Style, "font-face") && A(f, OdfNamespaces.Style, "name") is { } name
                && (A(f, OdfNamespaces.Svg, "font-family") ?? A(f, OdfNamespaces.Fo, "font-family")) is { } family)
            {
                _fonts.TryAdd(name, CleanFamily(family));
            }
        }
    }

    // "'Times New Roman', serif" is a CSS list; DrawingML names one typeface.
    private static string CleanFamily(string family)
    {
        int comma = family.IndexOf(',', StringComparison.Ordinal);
        string first = (comma >= 0 ? family[..comma] : family).Trim().Trim('\'', '"');
        return first;
    }

    // presentation:header-decl, footer-decl and date-time-decl give the text the footer, header and date frames of a page show.
    private void ReadDeclarations(MarkupNode body)
    {
        foreach (MarkupNode c in body.ChildElements)
        {
            if (Is(c, OdfNamespaces.Presentation, "footer-decl") && A(c, OdfNamespaces.Presentation, "name") is { } fn)
            {
                _footerDecls.TryAdd(fn, c.InnerText);
            }
            else if (Is(c, OdfNamespaces.Presentation, "date-time-decl") && A(c, OdfNamespaces.Presentation, "name") is { } dn)
            {
                _dateDecls.TryAdd(dn, c.InnerText);
            }
        }
    }

    private void Build(MarkupNode body)
    {
        var masters = new List<MasterInfo>();
        MarkupNode masterStyles = Child(_pkg.StylesRoot, OdfNamespaces.Office, "master-styles");
        if (!masterStyles.IsNull)
        {
            foreach (MarkupNode m in masterStyles.ChildElements)
            {
                if (Is(m, OdfNamespaces.Style, "master-page") && A(m, OdfNamespaces.Style, "name") is { } name)
                {
                    OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), _ctx.Limits.MaxPartCount, masters.Count + 1, DefectLocation.InPart("styles.xml", Math.Max(0, m.SourceStart)));
                    var info = new MasterInfo { Name = name, Display = A(m, OdfNamespaces.Style, "display-name") ?? OdpUnits.DecodeStyleName(name), Node = m, Number = masters.Count + 1 };
                    SizeOf(info);
                    masters.Add(info);
                }
            }
        }

        if (masters.Count == 0)
        {
            // A presentation without master pages still needs one master in PresentationML.
            Defect(DefectKind.Structure, "odp.no-master-page", DefectSeverity.Repaired, "The presentation has no master page; a plain one was added.", body, "styles.xml");
            masters.Add(new MasterInfo { Name = "", Display = "Default", Number = 1, Width = 9144000, Height = 5143500 });
        }

        var pages = new List<PageInfo>();
        foreach (MarkupNode page in body.ChildElements)
        {
            if (!Is(page, OdfNamespaces.Draw, "page"))
            {
                continue;
            }

            _ct.ThrowIfCancellationRequested();
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), _ctx.Limits.MaxPartCount, pages.Count + 1, DefectLocation.InPart("content.xml", Math.Max(0, page.SourceStart)));
            string? masterName = A(page, OdfNamespaces.Draw, "master-page-name");
            MasterInfo master = masters.FirstOrDefault(m => m.Name == masterName) ?? masters[0];
            if (masterName is not null && master.Name != masterName)
            {
                Defect(DefectKind.Reference, "odp.missing-master-page", DefectSeverity.Repaired, "A slide names the master page '" + masterName + "', which the file does not define; the first master page was used.", page);
            }

            string? layoutName = A(page, OdfNamespaces.Presentation, "presentation-page-layout-name");
            string key = layoutName ?? "";
            if (!master.Layouts.TryGetValue(key, out LayoutInfo? layout))
            {
                MarkupNode pl = layoutName is null ? default : FindPresentationPageLayout(layoutName);
                layout = new LayoutInfo { Master = master, PageLayout = pl };
                layout.Type = LayoutTypeOf(pl);
                layout.Number = master.Layouts.Count + 1;
                layout.Name = layout.Type switch
                {
                    "title" => "Title Slide",
                    "obj" => "Title and Content",
                    "titleOnly" => "Title Only",
                    "blank" => "Blank",
                    _ => "Layout " + OdpUnits.I(layout.Number),
                };
                if (master.Layouts.Count > 0 || masters.Count > 1)
                {
                    layout.Name = master.Display + " - " + layout.Name;
                }

                master.Layouts[key] = layout;
            }

            bool hidden = false;
            if (A(page, OdfNamespaces.Draw, "style-name") is { } dp && _styles.Find("drawing-page", dp) is { } dps)
            {
                hidden = _styles.Resolve(dps, "drawing-page-properties", OdfNamespaces.Presentation, "visibility") == "hidden";
            }

            pages.Add(new PageInfo { Node = page, Master = master, Layout = layout, Number = pages.Count + 1, Hidden = hidden });
        }

        foreach (MasterInfo m in masters)
        {
            if (m.Layouts.Count == 0)
            {
                m.Layouts[""] = new LayoutInfo { Master = m, Number = 1, Name = "Blank", Type = "blank" };
            }
        }

        if (masters.Select(m => (m.Width, m.Height)).Distinct().Count() > 1)
        {
            Lost("loss.odp-page-sizes", "master pages with different page sizes", "PresentationML has one slide size; the size of the first master page is used for the whole presentation.");
        }

        long width = pages.Count > 0 ? pages[0].Master.Width : masters[0].Width;
        long height = pages.Count > 0 ? pages[0].Master.Height : masters[0].Height;

        long nextId = 2147483648;
        foreach (MasterInfo m in masters)
        {
            m.Id = nextId++;
            foreach (LayoutInfo l in m.Layouts.Values.OrderBy(l => l.Number))
            {
                l.Id = nextId++;
            }
        }

        var presRels = new Part("/ppt/presentation.xml");
        var masterRelIds = new List<(MasterInfo, string)>();
        int themeCount = 0;
        foreach (MasterInfo m in masters)
        {
            _ct.ThrowIfCancellationRequested();
            int theme = ++themeCount;
            WriteMaster(m, theme);
            masterRelIds.Add((m, presRels.AddRel(RelBase + "slideMaster", "slideMasters/slideMaster" + OdpUnits.I(m.Number) + ".xml")));
        }

        bool anyNotes = pages.Any(p => !Child(p.Node, OdfNamespaces.Presentation, "notes").IsNull);
        string? notesMasterRel = null;
        if (anyNotes)
        {
            int theme = ++themeCount;
            WriteNotesMaster(theme);
            notesMasterRel = presRels.AddRel(RelBase + "notesMaster", "notesMasters/notesMaster1.xml");
        }

        var slideIds = new List<(int Id, string Rel)>();
        foreach (PageInfo page in pages)
        {
            _ct.ThrowIfCancellationRequested();
            WriteSlide(page, width, height, anyNotes);
            slideIds.Add((255 + page.Number, presRels.AddRel(RelBase + "slide", "slides/slide" + OdpUnits.I(page.Number) + ".xml")));
        }

        CountAnimations(pages);

        var sb = new StringBuilder("<p:presentation " + Decl + " saveSubsetFonts=\"1\"><p:sldMasterIdLst>");
        foreach ((MasterInfo m, string rel) in masterRelIds)
        {
            sb.Append("<p:sldMasterId id=\"").Append(m.Id).Append("\" r:id=\"").Append(rel).Append("\"/>");
        }

        sb.Append("</p:sldMasterIdLst>");
        if (notesMasterRel is not null)
        {
            sb.Append("<p:notesMasterIdLst><p:notesMasterId r:id=\"").Append(notesMasterRel).Append("\"/></p:notesMasterIdLst>");
        }

        if (slideIds.Count > 0)
        {
            sb.Append("<p:sldIdLst>");
            foreach ((int id, string rel) in slideIds)
            {
                sb.Append("<p:sldId id=\"").Append(id).Append("\" r:id=\"").Append(rel).Append("\"/>");
            }

            sb.Append("</p:sldIdLst>");
        }

        sb.Append("<p:sldSz cx=\"").Append(width).Append("\" cy=\"").Append(height).Append("\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>");
        sb.Append("<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">");
        sb.Append("<a:defRPr sz=\"1800\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:defaultTextStyle></p:presentation>");
        _out.AddPart("/ppt/presentation.xml", PmlType + (_template ? "template.main+xml" : "presentation.main+xml"), Bytes(sb.ToString()));
        var rootRels = new List<Relationship> { new("rId1", RelBase + "officeDocument", "ppt/presentation.xml") };
        AddDocumentProperties(rootRels);
        _out.SetRelationships(null, rootRels);
        _out.SetRelationships("/ppt/presentation.xml", presRels.Rels);
        foreach ((string name, string contentType, byte[] data) in _media)
        {
            _out.AddPart(name, contentType, data);
        }
    }

    private void SizeOf(MasterInfo m)
    {
        // OpenDocument 1.3 Part 1 §20.340 (fo:page-width) in the page layout the master page names: the slide size.
        if (A(m.Node, OdfNamespaces.Style, "page-layout-name") is { } pl && _styles.Find("page-layout", pl) is { } layout)
        {
            MarkupNode props = layout.Properties("page-layout-properties");
            m.Width = Math.Clamp(OdpUnits.Emu(A(props, OdfNamespaces.Fo, "page-width"), 0), 0, 51206400);
            m.Height = Math.Clamp(OdpUnits.Emu(A(props, OdfNamespaces.Fo, "page-height"), 0), 0, 51206400);
        }

        if (m.Width <= 0 || m.Height <= 0)
        {
            // 28 x 15.75 cm is the 16:9 page the application defaults to.
            m.Width = 10080000;
            m.Height = 5670000;
        }
    }

    // presentation:presentation-page-layout is a style:presentation-page-layout in the styles; the style set does not index it, so it is found here.
    private MarkupNode FindPresentationPageLayout(string name)
    {
        foreach (MarkupNode root in new[] { _pkg.StylesRoot, _pkg.ContentRoot })
        {
            foreach (string container in new[] { "styles", "automatic-styles" })
            {
                MarkupNode c = Child(root, OdfNamespaces.Office, container);
                if (c.IsNull)
                {
                    continue;
                }

                foreach (MarkupNode e in c.ChildElements)
                {
                    if (Is(e, OdfNamespaces.Style, "presentation-page-layout") && A(e, OdfNamespaces.Style, "name") == name)
                    {
                        return e;
                    }
                }
            }
        }

        return default;
    }

    // The kind of layout from the objects the page layout lists: a title and a subtitle make a title slide, a title and outline text a
    // content slide, a title alone a title-only slide, nothing a blank one.
    private static string LayoutTypeOf(MarkupNode pl)
    {
        if (pl.IsNull)
        {
            return "cust";
        }

        bool title = false, subtitle = false, outline = false, other = false;
        foreach (MarkupNode ph in pl.ChildElements)
        {
            if (!Is(ph, OdfNamespaces.Presentation, "placeholder"))
            {
                continue;
            }

            switch (A(ph, OdfNamespaces.Presentation, "object"))
            {
                case "title":
                    title = true;
                    break;
                case "subtitle":
                    subtitle = true;
                    break;
                case "outline" or "text":
                    outline = true;
                    break;
                default:
                    other = true;
                    break;
            }
        }

        if (title && subtitle && !outline)
        {
            return "title";
        }

        if (title && outline && !other)
        {
            return "obj";
        }

        if (title && !outline && !subtitle && !other)
        {
            return "titleOnly";
        }

        if (!title && !outline && !subtitle && !other)
        {
            return "blank";
        }

        return "cust";
    }

    // ---- masters and layouts --------------------------------------------------------------------------------------------------

    private const string GroupHead =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private const string ClrMap = "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>";

    private void WriteTheme(int number, string name)
    {
        string xml = NewPresentation.ThemeXmlText.Replace("name=\"Office Theme\"", "name=\"" + OdpUnits.Esc(name) + "\"", StringComparison.Ordinal);
        _out.AddPart("/ppt/theme/theme" + OdpUnits.I(number) + ".xml", "application/vnd.openxmlformats-officedocument.theme+xml", Bytes(xml));
    }

    private void WriteMaster(MasterInfo m, int theme)
    {
        string partName = "/ppt/slideMasters/slideMaster" + OdpUnits.I(m.Number) + ".xml";
        var part = new Part(partName);
        m.Part = part;
        WriteTheme(theme, m.Display);
        var sb = part.Xml;
        sb.Append("<p:sldMaster ").Append(Decl).Append("><p:cSld>");
        if (!m.Node.IsNull)
        {
            sb.Append(BackgroundOf(A(m.Node, OdfNamespaces.Draw, "style-name"), part));
        }

        sb.Append("<p:spTree>").Append(GroupHead);
        var scope = new Scope(part, m, SceneKind.Master, default);
        if (!m.Node.IsNull)
        {
            WriteShapes(scope, m.Node, sb, null);
        }

        sb.Append("</p:spTree></p:cSld>").Append(ClrMap).Append("<p:sldLayoutIdLst>");
        var layouts = m.Layouts.Values.OrderBy(l => l.Number).ToList();
        var relIds = new List<string>();
        foreach (LayoutInfo l in layouts)
        {
            relIds.Add(part.AddRel(RelBase + "slideLayout", "../slideLayouts/slideLayout" + LayoutFileNumber(l) + ".xml"));
        }

        for (int i = 0; i < layouts.Count; i++)
        {
            sb.Append("<p:sldLayoutId id=\"").Append(layouts[i].Id).Append("\" r:id=\"").Append(relIds[i]).Append("\"/>");
        }

        sb.Append("</p:sldLayoutIdLst>");
        string themeRel = part.AddRel(RelBase + "theme", "../theme/theme" + OdpUnits.I(theme) + ".xml");
        _ = themeRel;
        WriteMasterTextStyles(m, sb);
        sb.Append("</p:sldMaster>");
        _out.AddPart(partName, PmlType + "slideMaster+xml", Bytes(sb.ToString()));
        _out.SetRelationships(partName, part.Rels);
        foreach (LayoutInfo l in layouts)
        {
            WriteLayout(m, l);
        }
    }

        // Layout part numbers run over all masters; each layout gets a stable number from its master number and its own.
    private static string LayoutFileNumber(LayoutInfo l) => OdpUnits.I(l.Master.Number * 1000 + l.Number);

    private void WriteLayout(MasterInfo m, LayoutInfo l)
    {
        string partName = "/ppt/slideLayouts/slideLayout" + LayoutFileNumber(l) + ".xml";
        var part = new Part(partName);
        var sb = part.Xml;
        sb.Append("<p:sldLayout ").Append(Decl).Append(" type=\"").Append(l.Type).Append("\" preserve=\"1\"><p:cSld name=\"").Append(OdpUnits.Esc(l.Name)).Append("\"><p:spTree>").Append(GroupHead);
        int bodyIdx = 0;
        if (!l.PageLayout.IsNull)
        {
            foreach (MarkupNode ph in l.PageLayout.ChildElements)
            {
                if (!Is(ph, OdfNamespaces.Presentation, "placeholder"))
                {
                    continue;
                }

                string? obj = A(ph, OdfNamespaces.Presentation, "object");
                string? phXml = obj switch
                {
                    "title" => "type=\"title\"",
                    "subtitle" => "type=\"subTitle\" idx=\"1\"",
                    "outline" or "text" => "type=\"body\" idx=\"" + OdpUnits.I(++bodyIdx) + "\"",
                    _ => null,
                };
                if (phXml is null)
                {
                    continue;
                }

                long x = OdpUnits.Emu(A(ph, OdfNamespaces.Svg, "x")), y = OdpUnits.Emu(A(ph, OdfNamespaces.Svg, "y"));
                long w = Math.Max(0, OdpUnits.Emu(A(ph, OdfNamespaces.Svg, "width"))), h = Math.Max(0, OdpUnits.Emu(A(ph, OdfNamespaces.Svg, "height")));
                int id = part.NextId();
                sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(obj switch { "title" => "Title", "subtitle" => "Subtitle", _ => "Content" }).Append(' ').Append(id)
                    .Append("\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph ").Append(phXml).Append("/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"").Append(x).Append("\" y=\"").Append(y)
                    .Append("\"/><a:ext cx=\"").Append(w).Append("\" cy=\"").Append(h).Append("\"/></a:xfrm></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>");
            }
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        part.AddRel(RelBase + "slideMaster", "../slideMasters/slideMaster" + OdpUnits.I(m.Number) + ".xml");
        _out.AddPart(partName, PmlType + "slideLayout+xml", Bytes(sb.ToString()));
        _out.SetRelationships(partName, part.Rels);
    }

    // The title style and the five body levels of a master come from the presentation styles named <master>-title and <master>-outline1…5.
    private void WriteMasterTextStyles(MasterInfo m, StringBuilder sb)
    {
        sb.Append("<p:txStyles><p:titleStyle>");
        OdfStyle? title = _styles.Find("presentation", m.Name + "-title");
        AppendStyleLevel(sb, 1, title, null, titleLevel: true);
        sb.Append("</p:titleStyle><p:bodyStyle>");
        for (int level = 1; level <= 5; level++)
        {
            OdfStyle? outline = _styles.Find("presentation", m.Name + "-outline" + OdpUnits.I(level));
            AppendStyleLevel(sb, level, outline, outline, titleLevel: false);
        }

        sb.Append("</p:bodyStyle><p:otherStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\"><a:defRPr sz=\"1800\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:otherStyle></p:txStyles>");
    }

    private void AppendStyleLevel(StringBuilder sb, int level, OdfStyle? style, OdfStyle? listHolder, bool titleLevel)
    {
        string tag = "a:lvl" + OdpUnits.I(level) + "pPr";
        var cascade = new StyleCascade(_styles, style);
        sb.Append('<').Append(tag).Append(" marL=\"").Append(OdpUnits.I(titleLevel ? 0 : 228600 + (level - 1) * 457200)).Append("\" indent=\"").Append(titleLevel ? "0" : "-228600").Append("\" algn=\"")
            .Append(titleLevel ? "ctr" : "l").Append("\" defTabSz=\"914400\">");
        if (!titleLevel)
        {
            AppendBullet(sb, ListStyleOf(listHolder), level - 1);
        }
        else
        {
            sb.Append("<a:buNone/>");
        }

        sb.Append("<a:defRPr");
        double? size = cascade.TextPoints("font-size");
        sb.Append(" sz=\"").Append(OdpUnits.I(size is { } s && s > 0 && s < 4000 ? (long)Math.Round(s * 100) : titleLevel ? 4400 : Math.Max(1000, 2800 - (level - 1) * 400))).Append("\" kern=\"1200\">");
        sb.Append("<a:solidFill>");
        string? color = OdpUnits.Color(cascade.Text(OdfNamespaces.Fo, "color"));
        sb.Append(color is null ? "<a:schemeClr val=\"tx1\"/>" : "<a:srgbClr val=\"" + color + "\"/>");
        sb.Append("</a:solidFill><a:latin typeface=\"").Append(titleLevel ? "+mj-lt" : "+mn-lt").Append("\"/><a:ea typeface=\"").Append(titleLevel ? "+mj-ea" : "+mn-ea").Append("\"/><a:cs typeface=\"")
            .Append(titleLevel ? "+mj-cs" : "+mn-cs").Append("\"/></a:defRPr></").Append(tag).Append('>');
    }

    private string BackgroundOf(string? drawingPageStyle, Part part)
    {
        if (drawingPageStyle is null || _styles.Find("drawing-page", drawingPageStyle) is not { } style)
        {
            return "";
        }

        string? fill = _styles.Resolve(style, "drawing-page-properties", OdfNamespaces.Draw, "fill");
        if (fill is null or "none")
        {
            return "";
        }

        string xml = FillXml(fill, key => _styles.Resolve(style, "drawing-page-properties", OdfNamespaces.Draw, key), key => _styles.Resolve(style, "drawing-page-properties", OdfNamespaces.Fo, key), part, style.Element);
        return xml.Length == 0 ? "" : "<p:bg><p:bgPr>" + xml + "<a:effectLst/></p:bgPr></p:bg>";
    }

    // ---- notes ----------------------------------------------------------------------------------------------------------------

    private void WriteNotesMaster(int theme)
    {
        WriteTheme(theme, "Notes");
        var part = new Part("/ppt/notesMasters/notesMaster1.xml");
        part.Xml.Append("<p:notesMaster ").Append(Decl).Append("><p:cSld><p:spTree>").Append(GroupHead)
            .Append("<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>")
            .Append("<p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:sp>")
            .Append("<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>")
            .Append("<p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>")
            .Append("<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Click to edit Master text styles</a:t></a:r></a:p></p:txBody></p:sp>")
            .Append("</p:spTree></p:cSld>").Append(ClrMap)
            .Append("<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\"><a:defRPr sz=\"1200\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:notesStyle></p:notesMaster>");
        part.AddRel(RelBase + "theme", "../theme/theme" + OdpUnits.I(theme) + ".xml");
        _out.AddPart(part.Name, PmlType + "notesMaster+xml", Bytes(part.Xml.ToString()));
        _out.SetRelationships(part.Name, part.Rels);
    }

    private void WriteNotes(PageInfo page, MarkupNode notes, string slidePartName)
    {
        string partName = "/ppt/notesSlides/notesSlide" + OdpUnits.I(page.Number) + ".xml";
        var part = new Part(partName);
        var scope = new Scope(part, page.Master, SceneKind.Notes, page.Node);
        var sb = part.Xml;
        sb.Append("<p:notes ").Append(Decl).Append("><p:cSld><p:spTree>").Append(GroupHead);
        sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>");
        part.NextId();
        bool wroteBody = false;
        foreach (MarkupNode frame in notes.ChildElements)
        {
            if (Is(frame, OdfNamespaces.Draw, "frame") && A(frame, OdfNamespaces.Presentation, "class") == "notes")
            {
                int id = part.NextId();
                sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"Notes Placeholder ").Append(id).Append("\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/>");
                MarkupNode box = Child(frame, OdfNamespaces.Draw, "text-box");
                var style = new ShapeStyle(_styles, A(frame, OdfNamespaces.Presentation, "style-name"), A(frame, OdfNamespaces.Draw, "style-name"), A(frame, OdfNamespaces.Draw, "text-style-name"));
                WriteTextBody(scope, sb, box, style, "p:txBody", placeholder: true);
                sb.Append("</p:sp>");
                wroteBody = true;
                break;
            }
        }

        if (!wroteBody)
        {
            int id = part.NextId();
            sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"Notes Placeholder ").Append(id).Append("\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>");
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>");
        part.AddRel(RelBase + "notesMaster", "../notesMasters/notesMaster1.xml");
        part.AddRel(RelBase + "slide", "../slides/slide" + OdpUnits.I(page.Number) + ".xml");
        _ = slidePartName;
        _out.AddPart(partName, PmlType + "notesSlide+xml", Bytes(sb.ToString()));
        _out.SetRelationships(partName, part.Rels);
    }

    // ---- slides ---------------------------------------------------------------------------------------------------------------

    private void WriteSlide(PageInfo page, long width, long height, bool anyNotes)
    {
        _ = width;
        _ = height;
        string partName = "/ppt/slides/slide" + OdpUnits.I(page.Number) + ".xml";
        var part = new Part(partName);
        var scope = new Scope(part, page.Master, SceneKind.Slide, page.Node);
        var sb = part.Xml;
        string name = A(page.Node, OdfNamespaces.Draw, "name") ?? "";
        sb.Append("<p:sld ").Append(Decl).Append(page.Hidden ? " show=\"0\"" : "").Append("><p:cSld");
        if (name.Length > 0)
        {
            sb.Append(" name=\"").Append(OdpUnits.Esc(name)).Append('"');
        }

        sb.Append('>').Append(BackgroundOf(A(page.Node, OdfNamespaces.Draw, "style-name"), part)).Append("<p:spTree>").Append(GroupHead);
        WriteShapes(scope, page.Node, sb, null);
        WriteDisplayedMasterFrames(scope, page, sb);
        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>");
        sb.Append("</p:sld>");
        part.AddRel(RelBase + "slideLayout", "../slideLayouts/slideLayout" + LayoutFileNumber(page.Layout) + ".xml");
        MarkupNode notes = Child(page.Node, OdfNamespaces.Presentation, "notes");
        if (anyNotes && !notes.IsNull)
        {
            WriteNotes(page, notes, partName);
            part.AddRel(RelBase + "notesSlide", "../notesSlides/notesSlide" + OdpUnits.I(page.Number) + ".xml");
        }

        _out.AddPart(partName, PmlType + "slide+xml", Bytes(sb.ToString()));
        _out.SetRelationships(partName, part.Rels);
    }

    // OpenDocument shows the footer, date and page-number frames of the master page on a slide when the slide's page style asks for them
    // (presentation:display-footer and friends); PresentationML shows them only as placeholders on the slide itself, so they are copied.
    private void WriteDisplayedMasterFrames(Scope scope, PageInfo page, StringBuilder sb)
    {
        if (page.Master.Node.IsNull)
        {
            return;
        }

        OdfStyle? dp = A(page.Node, OdfNamespaces.Draw, "style-name") is { } n ? _styles.Find("drawing-page", n) : null;
        bool Shown(string attribute) => dp is not null && _styles.Resolve(dp, "drawing-page-properties", OdfNamespaces.Presentation, attribute) == "true";
        foreach (MarkupNode frame in page.Master.Node.ChildElements)
        {
            if (!Is(frame, OdfNamespaces.Draw, "frame"))
            {
                continue;
            }

            string? cls = A(frame, OdfNamespaces.Presentation, "class");
            bool show = cls switch
            {
                "footer" => Shown("display-footer"),
                "date-time" => Shown("display-date-time"),
                "page-number" => Shown("display-page-number"),
                "header" => Shown("display-header"),
                _ => false,
            };
            if (show)
            {
                // The slide's own use-footer-name and use-date-time-name pick the text of the declaration.
                var slideScope = scope with { Page = page.Node, FromMaster = true };
                WriteFrame(slideScope, frame, sb, null);
            }
        }
    }

    private void CountAnimations(List<PageInfo> pages)
    {
        int animated = 0, transitions = 0;
        foreach (PageInfo p in pages)
        {
            foreach (MarkupNode c in p.Node.ChildElements)
            {
                if (c.NamespaceUri == AnimNs)
                {
                    animated++;
                    break;
                }
            }

            if (A(p.Node, OdfNamespaces.Draw, "style-name") is { } dp && _styles.Find("drawing-page", dp) is { } style)
            {
                MarkupNode props = style.Properties("drawing-page-properties");
                bool smil = A(props, SmilNs, "type") is not null;
                bool legacy = A(props, OdfNamespaces.Presentation, "transition-style") is { } ts && ts != "none";
                if (!props.IsNull && (smil || legacy))
                {
                    transitions++;
                }
            }
        }

        if (animated > 0)
        {
            Lost("loss.odp-animations", "animations", "Slide animations are not converted; they stay in the OpenDocument file and are written back only when the presentation is saved unchanged in its own format.", animated);
        }

        if (transitions > 0)
        {
            Lost("loss.odp-transitions", "slide transitions", "Slide transitions are not converted; they stay in the OpenDocument file and are written back only when the presentation is saved unchanged in its own format.", transitions);
        }
    }

    // ---- document properties --------------------------------------------------------------------------------------------------

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d))
        {
            return null;
        }

        return d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private void AddDocumentProperties(List<Relationship> rootRels)
    {
        OdfMetadata meta = _pkg.Metadata;
        var core = new StringBuilder();
        void Add(string element, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                core.Append('<').Append(element).Append('>').Append(OdpUnits.Esc(value)).Append("</").Append(element).Append('>');
            }
        }

        void AddDate(string element, string? raw)
        {
            string? d = NormalizeDate(raw);
            if (raw is not null && d is null)
            {
                _ctx.Defects.Report(new Defect(DefectKind.Value, "odf.bad-date", DefectSeverity.Dropped, "The date '" + raw + "' in the document properties is not a date.", DefectLocation.InPart("meta.xml", 0)));
            }

            if (d is not null)
            {
                core.Append('<').Append(element).Append(" xsi:type=\"dcterms:W3CDTF\">").Append(d).Append("</").Append(element).Append('>');
            }
        }

        Add("dc:title", meta.Title);
        Add("dc:subject", meta.Subject);
        Add("dc:creator", meta.Creator);
        Add("cp:keywords", string.Join(", ", meta.Keywords));
        Add("dc:description", meta.Description);
        Add("cp:lastModifiedBy", meta.LastModifiedBy);
        AddDate("dcterms:created", meta.Created);
        AddDate("dcterms:modified", meta.Modified);
        Add("dc:language", meta.Language);
        if (core.Length == 0)
        {
            return;
        }

        string xml = "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" + core + "</cp:coreProperties>";
        _out.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Bytes(xml));
        rootRels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
    }

    // ---- pictures -------------------------------------------------------------------------------------------------------------

    // Registers a picture's bytes as a media part and returns its part name; null when the picture is linked, missing or not an image.
    private string? RegisterImage(MarkupNode img, Part? owner = null)
    {
        _ = owner;
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
            MarkupNode bin = Child(img, OdfNamespaces.Office, "binary-data");
            if (!bin.IsNull)
            {
                var clean = new StringBuilder();
                foreach (char c in bin.InnerText)
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

        string? ext = SniffImage(data);
        if (ext is null)
        {
            Lost("loss.odp-picture-format", "pictures in a format PresentationML cannot hold", "Pictures that are not PNG, JPEG, GIF, BMP, TIFF, EMF or WMF (StarView metafiles, for one) were dropped.");
            return null;
        }

        string contentType = ext switch
        {
            "png" => "image/png",
            "jpg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tif" => "image/tiff",
            "wmf" => "image/x-wmf",
            "emf" => "image/x-emf",
            _ => "application/octet-stream",
        };
        string part = "/ppt/media/image" + OdpUnits.I(_media.Count + 1) + "." + ext;
        _media.Add((part, contentType, data));
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

        return null;
    }
}
