using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>State shared by every page while a PowerPoint file is converted to PresentationML: the package being built, pictures already written and the loss report.</summary>
internal sealed class PptConversion
{
    private readonly Dictionary<int, string?> _images = [];
    private int _imageCount;

    public PptConversion(PptPresentation presentation, OpcPackage package, ILossSink loss)
    {
        P = presentation;
        Package = package;
        Loss = new LocatedLoss(loss, presentation.File.Context.Defects);
    }

    public PptPresentation P { get; }

    public OpcPackage Package { get; }

    /// <summary>The loss sink; every item is also reported as a defect at the record offset in <see cref="LocatedLoss.Offset"/>.</summary>
    public LocatedLoss Loss { get; }

    /// <summary>Slide identifier to part file name (<c>slide3.xml</c>), filled before any page is written so hyperlinks between slides can be resolved.</summary>
    public Dictionary<uint, string> SlideParts { get; } = [];

    /// <summary>Shapes with animation info seen on the slide being written; reported with the slide.</summary>
    public int AnimatedShapes { get; set; }

    public string? SlidePartOf(uint slideId) => SlideParts.GetValueOrDefault(slideId);

    /// <summary>
    /// The text a header or footer metacharacter stands for ([MS-PPT] §2.4.15): the page's own <c>HeadersFooters</c> container first, then the
    /// document's default. Null when neither has a string for it.
    /// </summary>
    public string? HeaderFooterText(PptPage? page, PptMetaCharKind kind) => HeaderFooterString(page, kind == PptMetaCharKind.Header ? 1 : 2);

    /// <summary>The slide or notes <c>HeadersFooters</c> container in force for a page: its own, else the document's default (instance 3 for slides, 4 for notes pages).</summary>
    public ArtRecord? HeadersFooters(PptPage? page)
    {
        int docInstance = page is PptNotes ? 4 : 3;
        ArtRecord? own = page?.Container.Find(PptRecordType.HeadersFooters);
        if (own is not null)
        {
            return own;
        }

        ArtRecord? doc = P.File.DocumentContainer;
        return doc?.FindAll(PptRecordType.HeadersFooters).FirstOrDefault(c => c.Header.Instance == docInstance) ?? doc?.Find(PptRecordType.HeadersFooters);
    }

    /// <summary>A string of the header and footer record: instance 0 is the user date, 1 the header, 2 the footer.</summary>
    public string? HeaderFooterString(PptPage? page, int instance)
    {
        foreach (ArtRecord? container in new[] { HeadersFooters(page) })
        {
            ArtRecord? text = container?.FindAll(PptRecordType.CString).FirstOrDefault(c => c.Header.Instance == instance);
            if (text is not null)
            {
                return Utf16(text.Payload.Span);
            }
        }

        return null;
    }

    private static string Utf16(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
        }

        int end = Array.IndexOf(chars, '\0');
        return new string(chars, 0, end < 0 ? chars.Length : end);
    }

    public string Font(int index) => index >= 0 && index < P.Fonts.Count && P.Fonts[index].Length > 0 ? P.Fonts[index] : "Arial";

    /// <summary>Writes the picture of a one-based BLIP index to the package once and returns its part name, or null when the picture cannot be read.</summary>
    public string? ImagePart(int blipIndex)
    {
        if (_images.TryGetValue(blipIndex, out string? known))
        {
            return known;
        }

        ArtImage? image = P.GetImage(blipIndex);
        string? name = null;
        if (image is not null)
        {
            if (image.Format == ArtImageFormat.Pict)
            {
                Loss.Report("ppt.picture-pict", "picture", 1, "A Macintosh PICT picture is carried as is; PresentationML has no PICT picture type, so Office will not show it.");
            }

            _imageCount++;
            name = "/ppt/media/image" + _imageCount.ToString(CultureInfo.InvariantCulture) + "." + image.Extension;
            Package.AddPart(name, image.MediaType, image.Data);
        }

        _images[blipIndex] = name;
        return name;
    }
}

/// <summary>Per-page state: relationships being collected, shape identifiers, the colour scheme in force.</summary>
internal sealed class PageOut
{
    private readonly List<Relationship> _rels = [];
    private readonly Dictionary<string, string> _relIndex = [];
    private int _fieldCounter;

    public PageOut(PptConversion conv, PptColorScheme scheme, PptColorScheme masterScheme, string sourcePartName, PptPage? source = null)
    {
        Source = source;
        Conv = conv;
        Scheme = scheme;
        MasterScheme = masterScheme;
        PartName = sourcePartName;
    }

    public PptConversion Conv { get; }

    public PptColorScheme Scheme { get; }

    public PptColorScheme MasterScheme { get; }

    public string PartName { get; }

    /// <summary>The slide or notes page being written, whose own header and footer strings take precedence, or null.</summary>
    public PptPage? Source { get; }

    public int NextShapeId { get; set; } = 2;

    public IReadOnlyList<Relationship> Rels => _rels;

    public int NextField() => ++_fieldCounter;

    public string AddRel(string type, string target, RelationshipTargetMode mode = RelationshipTargetMode.Internal)
    {
        string key = type + "|" + target + "|" + (int)mode;
        if (_relIndex.TryGetValue(key, out string? id))
        {
            return id;
        }

        id = "rIdP" + (_rels.Count + 1).ToString(CultureInfo.InvariantCulture);
        _rels.Add(new Relationship(id, type, target, mode));
        _relIndex[key] = id;
        return id;
    }

    // Relationship targets are written relative to the part: every page part lives one directory below /ppt.
    public string AddImage(int blipIndex)
    {
        string? part = Conv.ImagePart(blipIndex);
        return part is null ? "" : AddRel(Rel.Image, ".." + part["/ppt".Length..]);
    }

    /// <summary>The scheme colour name in the theme this converter writes, for a PowerPoint scheme index 0 to 7.</summary>
    public static string SchemeName(int index) => index switch
    {
        0 => "bg1",
        1 => "tx1",
        2 => "tx2",
        3 => "bg2",
        4 => "accent1",
        5 => "accent2",
        6 => "accent3",
        _ => "accent4",
    };

    /// <summary>
    /// A colour as DrawingML: a theme reference when the page's scheme is the master's (so the colour follows the theme), otherwise the RGB
    /// value the page's own scheme gives it.
    /// </summary>
    public string SchemeColor(int index, string inner = "")
    {
        index = Math.Clamp(index, 0, 7);
        if (Scheme.SameAs(MasterScheme))
        {
            return "<a:schemeClr val=\"" + SchemeName(index) + "\">" + inner + "</a:schemeClr>";
        }

        (byte r, byte g, byte b) = Scheme.Colors[index];
        return "<a:srgbClr val=\"" + PptXml.Hex(r, g, b) + "\">" + inner + "</a:srgbClr>";
    }

    public static string Rgb(byte r, byte g, byte b, string inner = "") => "<a:srgbClr val=\"" + PptXml.Hex(r, g, b) + "\">" + inner + "</a:srgbClr>";

    public string Color(PptColor c)
    {
        if (c.IsScheme)
        {
            return SchemeColor(c.Index);
        }

        return Rgb(c.Red, c.Green, c.Blue);
    }

    /// <summary>An OfficeArt colour ([MS-ODRAW] §2.2.2): RGB, a scheme index or a system colour; <paramref name="opacity"/> is 16.16 (0x10000 opaque).</summary>
    public string ArtColorXml(ArtColor c, long opacity = 0x10000)
    {
        string alpha = opacity is >= 0x10000 or < 0 ? "" : "<a:alpha val=\"" + PptXml.Num(opacity * 100000 / 0x10000) + "\"/>";
        if (c.IsSchemeIndex)
        {
            return SchemeColor(c.Red, alpha);
        }

        if (c.IsSystemIndex)
        {
            // Windows COLOR_* indexes seen in presentations; the rest fall back to black.
            (byte r, byte g, byte b) = c.Red switch
            {
                0x05 => ((byte)0xFF, (byte)0xFF, (byte)0xFF),
                0x0F => ((byte)0xC0, (byte)0xC0, (byte)0xC0),
                0x08 => ((byte)0, (byte)0, (byte)0),
                _ => ((byte)0, (byte)0, (byte)0),
            };
            return Rgb(r, g, b, alpha);
        }

        return Rgb(c.Red, c.Green, c.Blue, alpha);
    }
}

/// <summary>Relationship type URIs of the parts the converter writes ([ECMA-376] Part 1 §15.2).</summary>
internal static class Rel
{
    private const string Base = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public const string OfficeDocument = Base + "officeDocument";
    public const string Slide = Base + "slide";
    public const string SlideMaster = Base + "slideMaster";
    public const string SlideLayout = Base + "slideLayout";
    public const string NotesSlide = Base + "notesSlide";
    public const string NotesMaster = Base + "notesMaster";
    public const string HandoutMaster = Base + "handoutMaster";
    public const string Theme = Base + "theme";
    public const string Image = Base + "image";
    public const string Hyperlink = Base + "hyperlink";
    public const string Comments = Base + "comments";
    public const string CommentAuthors = Base + "commentAuthors";
    public const string VbaProject = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
}

/// <summary>Forwards loss items to the caller's report and records each as a defect located at the record being converted.</summary>
internal sealed class LocatedLoss : ILossSink
{
    private readonly ILossSink _inner;
    private readonly IDefectSink _defects;

    public LocatedLoss(ILossSink inner, IDefectSink defects)
    {
        _inner = inner;
        _defects = defects;
    }

    /// <summary>The stream offset of the record being converted, or -1.</summary>
    public long Offset { get; set; } = -1;

    public void Report(string code, string feature, long count, string message)
    {
        _inner.Report(code, feature, count, message);
        _defects.Report(new Defect(DefectKind.Unsupported, code, DefectSeverity.Dropped, message, DefectLocation.InRecord(PptFile.DocumentStreamName, Offset)));
    }
}
