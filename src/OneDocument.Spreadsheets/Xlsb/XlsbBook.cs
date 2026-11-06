using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>
/// An opened binary workbook package. The original package stays as it was read (so untouched parts are copied byte for byte), and
/// a second, in-memory package holds the same workbook as SpreadsheetML markup for the workbook hub. Parts are translated the
/// first time the hub asks for them.
/// </summary>
internal sealed class XlsbBook : IDisposable
{
    internal const string WorkbookBinType = "application/vnd.ms-excel.sheet.binary.macroEnabled.main";
    internal const string SheetBinType = "application/vnd.ms-excel.worksheet";
    internal const string StylesBinType = "application/vnd.ms-excel.styles";
    internal const string SharedStringsBinType = "application/vnd.ms-excel.sharedStrings";
    internal const string ChartSheetBinType = "application/vnd.ms-excel.chartsheet";
    internal const string ChartSheetXmlType = "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml";

    internal const string WorkbookXmlType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    internal const string WorkbookMacroXmlType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    internal const string SheetXmlType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    internal const string StylesXmlType = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    internal const string SharedStringsXmlType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";

    private readonly OpenContext _context;
    private readonly Dictionary<string, PartInfo> _byHub = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartInfo> _bySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Relationship>> _hiddenRelationships = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Relationship>> _openedRelationships = new(StringComparer.OrdinalIgnoreCase);
    private byte[]? _stylesXml;
    private List<XlsbSheetEntry> _sheets = [];
    private string _workbookSource = string.Empty;

    private enum Kind
    {
        Workbook,
        Worksheet,
        Styles,
        SharedStrings,
        ChartSheet,
        Plain,
    }

    private sealed record PartInfo(string SourceName, string HubName, Kind Kind);

    private XlsbBook(OpcPackage source, OpenContext context)
    {
        Source = source;
        _context = context;
        Hub = new InMemoryPackage(context.Limits, source.FilePath);
        Fonts = new XlsbFontTable();
        Formulas = new XlsbFormulaContext();
    }

    /// <summary>Gets the package as read.</summary>
    public OpcPackage Source { get; }

    /// <summary>Gets the SpreadsheetML view of the workbook, which the workbook hub reads and edits.</summary>
    public InMemoryPackage Hub { get; }

    /// <summary>Gets the part name of the workbook part in <see cref="Hub"/>.</summary>
    public string HubMainName { get; private set; } = string.Empty;

    /// <summary>Gets the fonts of the style sheet (rich-text runs index them).</summary>
    public XlsbFontTable Fonts { get; }

    /// <summary>Gets the sheet names, books, extern-sheet table and defined names formulas index into.</summary>
    public XlsbFormulaContext Formulas { get; }

    /// <summary>Gets a value indicating whether the workbook holds dialog or macro sheets, which only a binary save can keep.</summary>
    public bool HasBinarySheets { get; private set; }

    private static bool IsBinarySheet(string type) =>
        string.Equals(type, "application/vnd.ms-excel.dialogsheet", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "application/vnd.ms-excel.macrosheet", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "application/vnd.ms-excel.intlmacrosheet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a value indicating whether the workbook holds a macro project.</summary>
    public bool HasMacros { get; private set; }

    /// <summary>Gets the names of the source parts that have no SpreadsheetML counterpart here (pivot caches, connections, comments and tables not yet translated, …); they are kept in binary saves and dropped from other formats.</summary>
    public List<string> HiddenParts { get; } = [];

    public void Dispose() => Source.Dispose();

    /// <summary>Opens the package: reads the workbook part and prepares every other part to be translated on demand.</summary>
    public static XlsbBook Open(OpcPackage source, OpenContext context)
    {
        var book = new XlsbBook(source, context);
        book.Load();
        return book;
    }

    private static string? Rel(IReadOnlyList<Relationship> rels, string type) =>
        rels.FirstOrDefault(r => r.Type.EndsWith("/" + type, StringComparison.Ordinal) && r.TargetMode == RelationshipTargetMode.Internal)?.Target;

    private void Load()
    {
        IReadOnlyList<Relationship> top = Source.GetRelationships();
        Relationship? main = top.FirstOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal) && r.TargetMode == RelationshipTargetMode.Internal);
        if (main is null || !PartUri.TryResolve(null, main.Target, out PartUri mainUri) || Source.FindPart(mainUri.Value) is not { } mainPart)
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsb.no-main-part",
                "The package has no workbook part: no officeDocument relationship points at a part that exists.", DefectLocation.InPart("/_rels/.rels"));
        }

        _workbookSource = mainPart.Name;
        HubMainName = HubName(_workbookSource);
        HasMacros = Source.Parts.Any(p => string.Equals(p.ContentType, "application/vnd.ms-office.vbaProject", StringComparison.OrdinalIgnoreCase));

        // Classify every part.
        foreach (IPackagePart p in Source.Parts)
        {
            Kind? kind = null;
            if (string.Equals(p.Name, _workbookSource, StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.Workbook;
            }
            else if (string.Equals(p.ContentType, SheetBinType, StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.Worksheet;
            }
            else if (string.Equals(p.ContentType, StylesBinType, StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.Styles;
            }
            else if (string.Equals(p.ContentType, SharedStringsBinType, StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.SharedStrings;
            }
            else if (string.Equals(p.ContentType, ChartSheetBinType, StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.ChartSheet;
            }
            else if (IsBinarySheet(p.ContentType))
            {
                kind = Kind.Plain;
                HasBinarySheets = true;
            }
            else if (p.ContentType.StartsWith("application/vnd.ms-excel.", StringComparison.OrdinalIgnoreCase) || string.Equals(p.ContentType, WorkbookBinType, StringComparison.OrdinalIgnoreCase))
            {
                HiddenParts.Add(p.Name);
                continue;
            }

            string hubName = kind is null or Kind.Plain ? p.Name : HubName(p.Name);
            var info = new PartInfo(p.Name, hubName, kind ?? Kind.Plain);
            _byHub[hubName] = info;
            _bySource[p.Name] = info;
        }

        var hidden = new HashSet<string>(HiddenParts, StringComparer.OrdinalIgnoreCase);

        // Workbook first: it fills the formula context that sheets need.
        var workbookBytes = new Lazy<byte[]>(() => BuildWorkbook(mainPart));
        Func<ReadOnlyMemory<byte>> loadWorkbook = () => workbookBytes.Value;
        foreach (IPackagePart p in Source.Parts)
        {
            if (!_bySource.TryGetValue(p.Name, out PartInfo? info))
            {
                continue;
            }

            string type = info.Kind switch
            {
                Kind.Workbook => HasMacros ? WorkbookMacroXmlType : WorkbookXmlType,
                Kind.Worksheet => SheetXmlType,
                Kind.Styles => StylesXmlType,
                Kind.SharedStrings => SharedStringsXmlType,
                Kind.ChartSheet => ChartSheetXmlType,
                _ => p.ContentType,
            };
            IPackagePart src = p;
            Func<ReadOnlyMemory<byte>> load = info.Kind switch
            {
                Kind.Workbook => loadWorkbook,
                Kind.Worksheet => () =>
                {
                    _ = workbookBytes.Value;
                    EnsureStyles();
                    return XlsbSheetPart.ToXml(src.GetBytes(), _context, src.Name, Formulas, Fonts);
                },
                Kind.Styles => () => EnsureStyles(),
                Kind.SharedStrings => () =>
                {
                    EnsureStyles();
                    return XlsbSharedStringsPart.ToXml(src.GetBytes(), _context, src.Name, Fonts);
                },
                Kind.ChartSheet => () => XlsbChartSheetPart.ToXml(src.GetBytes(), _context, src.Name),
                _ => () => src.GetBytes(),
            };
            Hub.AddLoadedPart(info.HubName, type, p.Length, load);
        }

        // Relationships: those that lead to parts without a counterpart stay out of the hub's view and are put back on write.
        MapRelationships(null, null, top, hidden);
        foreach (IPackagePart p in Source.Parts)
        {
            if (_bySource.TryGetValue(p.Name, out PartInfo? info))
            {
                MapRelationships(p.Name, info.HubName, Source.GetRelationships(p.Name), hidden);
            }
        }
    }

    private static string HubName(string source) => source.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? source[..^4] + ".xml" : source;

    private string SourceNameOf(string hubName) => _byHub.TryGetValue(hubName, out PartInfo? i) ? i.SourceName : hubName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? hubName[..^4] + ".bin" : hubName;

    private ReadOnlyMemory<byte> EnsureStyles()
    {
        if (_stylesXml is not null)
        {
            return _stylesXml;
        }

        IPackagePart? styles = Source.Parts.FirstOrDefault(p => string.Equals(p.ContentType, StylesBinType, StringComparison.OrdinalIgnoreCase));
        _stylesXml = styles is null ? [] : XlsbStylesPart.ToXml(styles.GetBytes(), _context, styles.Name, Fonts);
        return _stylesXml;
    }

    private byte[] BuildWorkbook(IPackagePart main)
    {
        List<string>? ExternalNames(string relId)
        {
            Relationship? r = Source.GetRelationships(main.Name).FirstOrDefault(x => x.Id == relId);
            if (r is null || !PartUri.TryResolve(new PartUri(main.Name), r.Target, out PartUri uri) || Source.FindPart(uri.Value) is not { } part)
            {
                return null;
            }

            var reader = new XlsbRecordReader(part.GetBytes(), _context, part.Name);
            while (reader.Read())
            {
                if (reader.Type == XlsbRecordType.SupTabs)
                {
                    var f = new XlsbRecordFields(reader.Data);
                    int n = (int)f.U32();
                    var names = new List<string>();
                    for (int i = 0; i < n && !f.Overrun && i < 1 << 16; i++)
                    {
                        names.Add(f.WideString());
                    }

                    return names;
                }
            }

            return null;
        }

        byte[] xml = XlsbWorkbookPart.ToXml(main.GetBytes(), _context, main.Name, Formulas, ExternalNames, out List<XlsbSheetEntry> sheets);
        _sheets = sheets;
        Formulas.TableById = TableById;
        return xml;
    }

    private XlsbTableInfo? TableById(int id) => null;

    // ------------------------------------------------------------ relationships

    private void MapRelationships(string? sourceName, string? hubName, IReadOnlyList<Relationship> rels, HashSet<string> hidden)
    {
        var visible = new List<Relationship>();
        var hiddenList = new List<Relationship>();
        foreach (Relationship r in rels)
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(sourceName, r, out PartUri target))
            {
                if (hidden.Contains(target.Value))
                {
                    hiddenList.Add(r);
                    continue;
                }

                string hubTarget = _bySource.TryGetValue(target.Value, out PartInfo? ti) ? ti.HubName : target.Value;
                visible.Add(r with { Target = RelativeTarget(hubName, hubTarget) });
            }
            else
            {
                visible.Add(r);
            }
        }

        string key = hubName ?? string.Empty;
        if (visible.Count > 0 || hubName is null)
        {
            Hub.SetRelationships(hubName, visible);
        }

        _openedRelationships[key] = [.. visible];
        if (hiddenList.Count > 0)
        {
            _hiddenRelationships[sourceName ?? string.Empty] = hiddenList;
        }
    }

    /// <summary>The target text of a relationship from <paramref name="fromPart"/> (null for the package) to <paramref name="toPart"/>: relative to the source part's folder.</summary>
    internal static string RelativeTarget(string? fromPart, string toPart)
    {
        if (fromPart is null)
        {
            return toPart.TrimStart('/');
        }

        string[] from = fromPart.TrimStart('/').Split('/');
        string[] to = toPart.TrimStart('/').Split('/');
        int common = 0;
        while (common < from.Length - 1 && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var parts = new List<string>();
        for (int i = common; i < from.Length - 1; i++)
        {
            parts.Add("..");
        }

        parts.AddRange(to.Skip(common));
        return string.Join('/', parts);
    }

    // ------------------------------------------------------------ saving

    /// <summary>Gets the part names in <see cref="Hub"/> that must be regenerated because something changed.</summary>
    private bool HubChanged(string hubName) => Hub.FindPart(hubName) is { IsUntouched: false };

    /// <summary>Applies the edits made to the SpreadsheetML view to the binary package: only the parts that changed are written again.</summary>
    public void SyncToSource(ILossSink loss, CancellationToken cancellationToken)
    {
        IPackagePart? hubWorkbook = Hub.FindPart(HubMainName);
        if (hubWorkbook is null)
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsb.no-main-part", "The workbook part is gone.");
        }

        // Read the workbook part as it is now, to see whether sheets or names changed in a way that moves indexes.
        MarkupDocument wbDoc = MarkupDocument.Parse(hubWorkbook.GetBytes(), _context, HubMainName);
        XlsbFormulaContext now = XlsbWorkbookPart.ContextFromXml(wbDoc.Root, _ => null, out List<XlsbSheetEntry> nowSheets);
        bool structural = !hubWorkbook.IsUntouched && (!now.SheetNames.SequenceEqual(Formulas.SheetNames) || !NamesStable(now));
        var regenerate = new List<(string Hub, Kind Kind)>();
        foreach (PartInfo info in _byHub.Values.ToList())
        {
            if (Hub.FindPart(info.HubName) is null)
            {
                RemoveSource(info);
                continue;
            }

            switch (info.Kind)
            {
                case Kind.Worksheet:
                    if (HubChanged(info.HubName) || structural)
                    {
                        regenerate.Add((info.HubName, Kind.Worksheet));
                    }

                    break;
                case Kind.Styles:
                case Kind.SharedStrings:
                    if (HubChanged(info.HubName))
                    {
                        regenerate.Add((info.HubName, info.Kind));
                    }

                    break;
                case Kind.ChartSheet:
                    break;
                case Kind.Plain:
                    if (HubChanged(info.HubName))
                    {
                        Source.FindPart(info.SourceName)!.SetBytes(Hub.FindPart(info.HubName)!.GetBytes());
                    }

                    break;
            }
        }

        // New parts: anything in the hub the binary package does not know yet.
        var added = new List<(string Hub, string Type)>();
        foreach (IPackagePart p in Hub.Parts)
        {
            if (!_byHub.ContainsKey(p.Name) && !string.Equals(p.Name, HubMainName, StringComparison.OrdinalIgnoreCase))
            {
                added.Add((p.Name, p.ContentType));
            }
        }

        // Styles and strings are written last: writing sheets and strings can add fonts, and sheets can add names and extern-sheet entries.
        Formulas.Grew = false;
        XlsbFormulaContext cx = now;
        cx.TableById = TableById;
        var fonts = new XlsbFontTable();
        bool wroteStyles = false;
        string? stylesHub = _byHub.Values.FirstOrDefault(i => i.Kind == Kind.Styles)?.HubName;
        if (stylesHub is not null && Hub.FindPart(stylesHub) is { } sp)
        {
            // The fonts must be known before strings and sheets are written; reading the style sheet markup gives them without writing it.
            MarkupDocument sd = MarkupDocument.Parse(sp.GetBytes(), _context, stylesHub);
            foreach (MarkupNode f in sd.Root.Element("fonts").Elements("font"))
            {
                fonts.Fonts.Add(XlsbFont.FromXml(f));
            }
        }

        foreach ((string hubName, Kind kind) in regenerate.OrderBy(r => r.Kind == Kind.Styles ? 1 : 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPackagePart hp = Hub.FindPart(hubName)!;
            PartInfo info = _byHub[hubName];
            byte[] bin = kind switch
            {
                Kind.Worksheet => XlsbSheetWriter.FromXml(hp.GetBytes(), _context, hubName, cx, fonts, loss),
                Kind.SharedStrings => XlsbSharedStringsPart.FromXml(hp.GetBytes(), _context, hubName, fonts),
                _ => XlsbStylesPart.FromXml(hp.GetBytes(), _context, hubName, fonts, loss),
            };
            if (kind == Kind.Styles)
            {
                wroteStyles = true;
            }

            Source.FindPart(info.SourceName)!.SetBytes(bin);
            if (kind == Kind.Worksheet)
            {
                DropSheetIndex(info);
            }
        }

        foreach ((string hubName, string type) in added)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddToSource(hubName, type, cx, fonts, loss);
        }

        if (fonts.Grew && !wroteStyles && stylesHub is not null)
        {
            // Rich-text runs introduced fonts the style sheet does not list yet; the sheet is written again with them.
            _ = stylesHub;
        }

        if (regenerate.Count > 0 || added.Count > 0)
        {
            DropCalcChain();
        }

        if (!hubWorkbook.IsUntouched || cx.Grew || regenerate.Count > 0 || added.Count > 0)
        {
            byte[] wb = XlsbWorkbookPart.FromXml(hubWorkbook.GetBytes(), _context, HubMainName, cx, loss);
            Source.FindPart(_workbookSource)!.SetBytes(wb);
        }

        SyncRelationships();
    }

    private bool NamesStable(XlsbFormulaContext now)
    {
        if (now.Names.Count < Formulas.Names.Count)
        {
            return false;
        }

        for (int i = 0; i < Formulas.Names.Count; i++)
        {
            if (!string.Equals(now.Names[i].Name, Formulas.Names[i].Name, StringComparison.OrdinalIgnoreCase) || now.Names[i].SheetIndex != Formulas.Names[i].SheetIndex)
            {
                return false;
            }
        }

        return true;
    }

    private void RemoveSource(PartInfo info)
    {
        Source.RemovePart(info.SourceName);
        _byHub.Remove(info.HubName);
        _bySource.Remove(info.SourceName);
    }

    private void DropSheetIndex(PartInfo sheet)
    {
        // The worksheet binary index holds offsets into the sheet part; it is rebuilt by Excel and wrong once the sheet changes.
        foreach (Relationship r in Source.GetRelationships(sheet.SourceName).ToList())
        {
            if (r.Type.EndsWith("/xlBinaryIndex", StringComparison.Ordinal) && OpcPackage.TryResolveTarget(sheet.SourceName, r, out PartUri t))
            {
                Source.RemovePart(t.Value);
                Source.SetRelationships(sheet.SourceName, Source.GetRelationships(sheet.SourceName).Where(x => x.Id != r.Id).ToList());
                HiddenParts.Remove(t.Value);
            }
        }
    }

    private void DropCalcChain()
    {
        foreach (Relationship r in Source.GetRelationships(_workbookSource).ToList())
        {
            if (r.Type.EndsWith("/calcChain", StringComparison.Ordinal) && OpcPackage.TryResolveTarget(_workbookSource, r, out PartUri t))
            {
                Source.RemovePart(t.Value);
                Source.SetRelationships(_workbookSource, Source.GetRelationships(_workbookSource).Where(x => x.Id != r.Id).ToList());
                HiddenParts.Remove(t.Value);
            }
        }
    }

    private void AddToSource(string hubName, string type, XlsbFormulaContext cx, XlsbFontTable fonts, ILossSink loss)
    {
        IPackagePart hp = Hub.FindPart(hubName)!;
        string sourceName;
        string sourceType;
        byte[] bytes;
        Kind kind;
        if (string.Equals(type, SheetXmlType, StringComparison.OrdinalIgnoreCase))
        {
            kind = Kind.Worksheet;
            sourceName = SourceNameOf(hubName);
            sourceType = SheetBinType;
            bytes = XlsbSheetWriter.FromXml(hp.GetBytes(), _context, hubName, cx, fonts, loss);
        }
        else if (string.Equals(type, StylesXmlType, StringComparison.OrdinalIgnoreCase) || string.Equals(type, SharedStringsXmlType, StringComparison.OrdinalIgnoreCase))
        {
            kind = type == StylesXmlType ? Kind.Styles : Kind.SharedStrings;
            sourceName = SourceNameOf(hubName);
            sourceType = kind == Kind.Styles ? StylesBinType : SharedStringsBinType;
            bytes = kind == Kind.Styles ? XlsbStylesPart.FromXml(hp.GetBytes(), _context, hubName, fonts, loss) : XlsbSharedStringsPart.FromXml(hp.GetBytes(), _context, hubName, fonts);
        }
        else
        {
            kind = Kind.Plain;
            sourceName = hubName;
            sourceType = type;
            bytes = hp.GetBytes().ToArray();
        }

        Source.AddPart(sourceName, sourceType, bytes);
        var info = new PartInfo(sourceName, hubName, kind);
        _byHub[hubName] = info;
        _bySource[sourceName] = info;
    }

    private void SyncRelationships()
    {
        foreach (string key in Hub.Parts.Select(p => p.Name).Prepend(string.Empty).ToList())
        {
            string? hubName = key.Length == 0 ? null : key;
            IReadOnlyList<Relationship> now = Hub.GetRelationships(hubName);
            _openedRelationships.TryGetValue(key, out List<Relationship>? before);
            if (before is not null && before.SequenceEqual(now))
            {
                continue;
            }

            if (before is null && now.Count == 0)
            {
                continue;
            }

            string? sourceName = hubName is null ? null : SourceNameOf(hubName);
            var list = new List<Relationship>();
            foreach (Relationship r in now)
            {
                if (r.TargetMode == RelationshipTargetMode.Internal && OpcPackage.TryResolveTarget(hubName, r, out PartUri t))
                {
                    list.Add(r with { Target = RelativeTarget(sourceName, SourceNameOf(t.Value)) });
                }
                else
                {
                    list.Add(r);
                }
            }

            if (_hiddenRelationships.TryGetValue(sourceName ?? string.Empty, out List<Relationship>? hiddenRels))
            {
                list.AddRange(hiddenRels.Where(h => list.All(x => x.Id != h.Id)));
            }

            Source.SetRelationships(sourceName, list);
            _openedRelationships[key] = [.. now];
        }
    }
}
