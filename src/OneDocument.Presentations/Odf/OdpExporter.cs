using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// The presentation hub -> OpenDocument presentation. Slides, layouts and their shapes are read through the hub and written as content.xml,
// styles.xml and meta.xml of an OdfPackage, which the caller then writes as a zip (ODP, OTP) or as one flat XML file (FODP). Every
// shape is written with its resolved geometry and character formatting, so the file does not depend on the inheritance the hub resolves.
internal sealed partial class OdpExporter
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\"";

    private readonly Presentation _p;
    private readonly LossReport _loss;
    private readonly OpenContext _ctx;
    private readonly CancellationToken _ct;
    private OdfPackage _out = null!;
    private readonly SortedSet<string> _fonts = new(StringComparer.Ordinal);
    private readonly List<(string Name, byte[] Data, string MediaType)> _media = [];
    private readonly Dictionary<string, string> _mediaByPart = new(StringComparer.Ordinal);
    private readonly StringBuilder _resources = new();
    private int _resourceCount;
    private long _width = 10080000, _height = 5670000;
    private int _depth;

    private OdpExporter(Presentation p, LossReport loss, CancellationToken ct)
    {
        _p = p;
        _loss = loss;
        _ctx = p.Context;
        _ct = ct;
    }

    internal static void Write(Presentation p, OfficeFormat format, Stream destination, LossReport loss, CancellationToken cancellationToken)
    {
        var e = new OdpExporter(p, loss, cancellationToken);
        try
        {
            e.Run(format, destination);
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "odf.export-failed", "The presentation could not be written as OpenDocument: " + ex.GetType().Name + ".", DefectLocation.InPart(p.MainPartName), ex);
        }
    }

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    // ---- automatic style sinks ------------------------------------------------------------------------------------------------

    private sealed class Sink
    {
        public readonly StringBuilder Xml = new();
        public readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Counters = new(StringComparer.Ordinal);
        public string Prefix = "";
    }

    private readonly Sink _content = new();
    private readonly Sink _styles = new() { Prefix = "M" };
    private Sink _sink = null!;

    private string AddStyle(string family, string prefix, string attrs, string children)
    {
        string key = family + "|" + attrs + "|" + children;
        if (_sink.Cache.TryGetValue(key, out string? existing))
        {
            return existing;
        }

        string p = _sink.Prefix + prefix;
        _sink.Counters.TryGetValue(p, out int n);
        _sink.Counters[p] = ++n;
        string name = p + OdpUnits.I(n);
        _sink.Cache[key] = name;
        _sink.Xml.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"").Append(family).Append('"').Append(attrs).Append(children.Length == 0 ? "/>" : ">" + children + "</style:style>");
        return name;
    }

    // ---- run ------------------------------------------------------------------------------------------------------------------

    private readonly List<(SlideMaster Master, SlideLayout Layout, string Name)> _pages = [];

    private void Run(OfficeFormat format, Stream destination)
    {
        string mime = format == OfficeFormat.Otp ? OdfMediaTypes.PresentationTemplate : OdfMediaTypes.Presentation;
        _out = OdfPackage.Create(mime, _ctx);
        if (_p.SlideSize is { } size && size.Width > 0 && size.Height > 0)
        {
            _width = size.Width;
            _height = size.Height;
        }

        // Master pages: one for each layout of each master, named after the layout.
        int number = 0;
        foreach (SlideMaster master in _p.Masters)
        {
            foreach (SlideLayout layout in master.Layouts)
            {
                _pages.Add((master, layout, "Layout" + OdpUnits.I(++number)));
            }
        }

        var masterXml = new StringBuilder();
        _sink = _styles;
        foreach ((SlideMaster master, SlideLayout layout, string name) in _pages)
        {
            _ct.ThrowIfCancellationRequested();
            WriteMasterPage(masterXml, master, layout, name);
        }

        if (_pages.Count == 0)
        {
            masterXml.Append("<style:master-page style:name=\"Default\" style:page-layout-name=\"PM1\"/>");
        }

        _sink = _content;
        var body = new StringBuilder();
        int slideNumber = 0;
        foreach (Slide slide in _p.Slides)
        {
            _ct.ThrowIfCancellationRequested();
            WriteSlide(body, slide, ++slideNumber);
        }

        AppendListStyles(_content.Xml);
        string fontDecls = FontDecls();
        string pageLayout = "<style:page-layout style:name=\"PM1\"><style:page-layout-properties fo:margin-top=\"0cm\" fo:margin-bottom=\"0cm\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:page-width=\""
            + OdpUnits.Cm(_width) + "\" fo:page-height=\"" + OdpUnits.Cm(_height) + "\" style:print-orientation=\"" + (_width >= _height ? "landscape" : "portrait") + "\"/></style:page-layout>";
        string stylesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-styles " + Namespaces + " office:version=\"1.3\">" + fontDecls
            + "<office:styles>" + _resources + "</office:styles><office:automatic-styles>" + pageLayout + _styles.Xml + "</office:automatic-styles><office:master-styles>" + masterXml + "</office:master-styles></office:document-styles>";
        string contentXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-content " + Namespaces + " office:version=\"1.3\">" + fontDecls
            + "<office:automatic-styles>" + _content.Xml + "</office:automatic-styles><office:body><office:presentation>" + body + "</office:presentation></office:body></office:document-content>";
        _out.SetEntry(OdfPackage.ContentName, Encoding.UTF8.GetBytes(contentXml), "text/xml");
        _out.SetEntry(OdfPackage.StylesName, Encoding.UTF8.GetBytes(stylesXml), "text/xml");
        _out.SetEntry(OdfPackage.MetaName, Encoding.UTF8.GetBytes(BuildMeta()), "text/xml");
        foreach ((string name, byte[] data, string mediaType) in _media)
        {
            _out.SetEntry(name, data, mediaType);
        }

        if (format == OfficeFormat.Fodp)
        {
            _out.WriteFlatTo(destination, _ct);
        }
        else
        {
            _out.WriteTo(destination, _ct);
        }

        _out.Dispose();
    }

    private string FontDecls()
    {
        if (_fonts.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder("<office:font-face-decls>");
        foreach (string f in _fonts)
        {
            string family = f.Contains(' ', StringComparison.Ordinal) ? "'" + f + "'" : f;
            sb.Append("<style:font-face style:name=\"").Append(OdpUnits.Esc(f)).Append("\" svg:font-family=\"").Append(OdpUnits.Esc(family)).Append("\"/>");
        }

        return sb.Append("</office:font-face-decls>").ToString();
    }

    private string BuildMeta()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-meta " + Namespaces + " office:version=\"1.3\"><office:meta>");
        sb.Append("<meta:generator>OneDocument</meta:generator>");
        var props = new DocumentProperties(_p.Package, _ctx);
        CoreProperties core = props.Core;
        void Text(string element, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                sb.Append('<').Append(element).Append('>').Append(OdpUnits.Esc(value)).Append("</").Append(element).Append('>');
            }
        }

        Text("dc:title", core.Title);
        Text("dc:description", core.Description);
        Text("dc:subject", core.Subject);
        if (!string.IsNullOrEmpty(core.Keywords))
        {
            foreach (string k in core.Keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Text("meta:keyword", k);
            }
        }

        Text("meta:initial-creator", core.Creator);
        if (core.Created is { } created)
        {
            Text("meta:creation-date", created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        Text("dc:creator", core.LastModifiedBy);
        if (core.Modified is { } modified)
        {
            Text("dc:date", modified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        Text("dc:language", core.Language);
        foreach (CustomProperty cp in props.Custom.List())
        {
            sb.Append("<meta:user-defined meta:name=\"").Append(OdpUnits.Esc(cp.Name)).Append("\">").Append(OdpUnits.Esc(cp.Text)).Append("</meta:user-defined>");
        }

        return sb.Append("</office:meta></office:document-meta>").ToString();
    }

    // ---- master pages ---------------------------------------------------------------------------------------------------------

    private void WriteMasterPage(StringBuilder sb, SlideMaster master, SlideLayout layout, string name)
    {
        string dp = BackgroundStyle(layout, master);
        sb.Append("<style:master-page style:name=\"").Append(name).Append("\" style:display-name=\"").Append(OdpUnits.Esc(layout.Name ?? name)).Append("\" style:page-layout-name=\"PM1\"")
            .Append(dp.Length > 0 ? " draw:style-name=\"" + dp + "\"" : "").Append('>');
        var identity = new Transform(0, 0, 1, 1, 0, 0);
        foreach (PresentationPart part in new PresentationPart[] { master, layout })
        {
            foreach (Shape shape in part.Shapes)
            {
                if (shape.Placeholder is { } ph)
                {
                    if (part == master && layout.Shapes.Any(s => s.Placeholder is { } lp && lp.Type == ph.Type && lp.Index == ph.Index))
                    {
                        continue;
                    }

                    WritePlaceholderOnMaster(sb, shape, ph);
                }
                else
                {
                    WriteShape(sb, shape, identity);
                }
            }
        }

        sb.Append("</style:master-page>");
    }

    private string BackgroundStyle(SlideLayout layout, SlideMaster master)
    {
        foreach (PresentationPart part in new PresentationPart[] { layout, master })
        {
            if (Pml.P(part.Markup, "cSld") is { } csld && Pml.P(csld, "bg") is { } bg && Pml.P(bg, "bgPr") is { } bgPr)
            {
                string fill = FillProperties(bgPr, part, out string? _);
                if (fill.Length > 0)
                {
                    return AddStyle("drawing-page", "dp", "", "<style:drawing-page-properties" + fill + "/>");
                }
            }
        }

        return "";
    }

    private void WritePlaceholderOnMaster(StringBuilder sb, Shape shape, PlaceholderInfo ph)
    {
        string? cls = ClassOf(ph);
        ShapeBounds? b = shape.ResolveBounds();
        if (cls is null || b is null)
        {
            return;
        }

        string style = AddStyle("presentation", "pr", "", "<style:graphic-properties draw:stroke=\"none\" draw:fill=\"none\"/>");
        sb.Append("<draw:frame presentation:style-name=\"").Append(style).Append("\" presentation:class=\"").Append(cls).Append("\" draw:layer=\"backgroundobjects\" svg:width=\"").Append(OdpUnits.Cm(b.Value.Width)).Append("\" svg:height=\"").Append(OdpUnits.Cm(b.Value.Height))
            .Append("\" svg:x=\"").Append(OdpUnits.Cm(b.Value.X)).Append("\" svg:y=\"").Append(OdpUnits.Cm(b.Value.Y)).Append("\"><draw:text-box>");
        if (cls == "page-number")
        {
            sb.Append("<text:p><text:page-number>&lt;number&gt;</text:page-number></text:p>");
        }
        else
        {
            sb.Append("<text:p/>");
        }

        sb.Append("</draw:text-box></draw:frame>");
    }

    private static string? ClassOf(PlaceholderInfo ph) => ph.Type switch
    {
        "title" or "ctrTitle" => "title",
        "subTitle" => "subtitle",
        "body" or "obj" => "outline",
        "dt" => "date-time",
        "ftr" => "footer",
        "sldNum" => "page-number",
        _ => null,
    };

    // ---- slides ---------------------------------------------------------------------------------------------------------------

    private void WriteSlide(StringBuilder sb, Slide slide, int number)
    {
        string master = "Default";
        if (slide.Layout is { } layout)
        {
            foreach ((SlideMaster m, SlideLayout l, string pageName) in _pages)
            {
                if (l.PartName == layout.PartName)
                {
                    master = pageName;
                    break;
                }
            }
        }
        else if (_pages.Count > 0)
        {
            master = _pages[0].Name;
        }

        string attrs = (slide.IsHidden ? " presentation:visibility=\"hidden\"" : "") + (SlideBackground(slide) is { Length: > 0 } bg ? bg : "");
        string dp = AddStyle("drawing-page", "dp", "", "<style:drawing-page-properties" + attrs + "/>");
        string name = slide.Name is { Length: > 0 } n ? n : "Slide " + OdpUnits.I(number);
        sb.Append("<draw:page draw:name=\"").Append(OdpUnits.Esc(name)).Append("\" draw:style-name=\"").Append(dp).Append("\" draw:master-page-name=\"").Append(master).Append("\">");
        var identity = new Transform(0, 0, 1, 1, 0, 0);
        int index = 0;
        foreach (Shape shape in slide.Shapes)
        {
            _ct.ThrowIfCancellationRequested();
            WriteShape(sb, shape, identity, slide, ++index);
        }

        if (slide.Notes is { } notes && notes.NotesText.Length > 0)
        {
            WriteNotes(sb, notes);
        }

        if (slide.HasTransition)
        {
            Lost("loss.odp-export-transitions", "slide transitions", "Slide transitions are not written to OpenDocument.");
        }

        if (slide.HasAnimations)
        {
            Lost("loss.odp-export-animations", "animations", "Slide animations are not written to OpenDocument.");
        }

        if (slide.Comments.Count > 0)
        {
            Lost("loss.odp-export-comments", "comments", "Slide comments are not written to OpenDocument.", slide.Comments.Count);
        }

        sb.Append("</draw:page>");
    }

    private string SlideBackground(Slide slide)
    {
        if (Pml.P(slide.Markup, "cSld") is { } csld && Pml.P(csld, "bg") is { } bg && Pml.P(bg, "bgPr") is { } bgPr)
        {
            return FillProperties(bgPr, slide, out string? _);
        }

        return "";
    }

    private void WriteNotes(StringBuilder sb, NotesSlide notes)
    {
        // The notes page is portrait; the slide's picture sits above the text, both 15 cm wide and 3 cm from the left edge.
        long w = 5400000, h = w * _height / Math.Max(1, _width);
        sb.Append("<presentation:notes><draw:page-thumbnail draw:layer=\"layout\" svg:width=\"").Append(OdpUnits.Cm(w)).Append("\" svg:height=\"").Append(OdpUnits.Cm(h))
            .Append("\" svg:x=\"").Append(OdpUnits.Cm(1080000)).Append("\" svg:y=\"").Append(OdpUnits.Cm(720000)).Append("\" presentation:class=\"page\"/>");
        sb.Append("<draw:frame presentation:class=\"notes\" draw:layer=\"layout\" svg:width=\"").Append(OdpUnits.Cm(w)).Append("\" svg:height=\"").Append(OdpUnits.Cm(3600000))
            .Append("\" svg:x=\"").Append(OdpUnits.Cm(1080000)).Append("\" svg:y=\"").Append(OdpUnits.Cm(720000 + h + 360000)).Append("\"><draw:text-box>");
        if (notes.Body is { } body)
        {
            WriteTextParagraphs(sb, body, null);
        }

        sb.Append("</draw:text-box></draw:frame></presentation:notes>");
    }
}
