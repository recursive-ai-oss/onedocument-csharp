using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>
/// A spreadsheet workbook: the semantic hub over a SpreadsheetML package (ECMA-376 Part 1, CT_Workbook). It lists the sheets with their
/// names, states and order, and loads each worksheet's cells only when it is touched. A workbook opened and saved without edits
/// writes every part byte-identical; an edit changes only the parts it touches.
/// </summary>
/// <remarks>
/// <para>Opening reads the content types, the package relationships, the workbook part and its relationships. A worksheet is read when
/// <see cref="Worksheet.Store"/> (or a cell) is first used, and the shared string table when a string is read or added.
/// Opening never recalculates; cached values stay as stored.</para>
/// <para>A workbook is not safe for concurrent mutation. Once the sheets in use are loaded, read-only access from several threads is safe.</para>
/// </remarks>
public sealed partial class Workbook : OfficeFile
{
    private const string MainXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MainXlsm = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string MainXltx = "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml";
    private const string MainXltm = "application/vnd.ms-excel.template.macroEnabled.main+xml";
    private const string MainXlam = "application/vnd.ms-excel.addin.macroEnabled.main+xml";
    private const string MainXlsb = "application/vnd.ms-excel.sheet.binary.macroEnabled.main+xml";
    private const string WorksheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string SharedStringsContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string RelBaseStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships/";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelNsStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string MainNsStrict = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    private readonly bool _ownsPackage;
    private readonly List<Worksheet> _sheets = [];
    private readonly SheetCollection _collection;
    private MarkupDocument _tree = null!;
    private MarkupNode _sheetsNode;
    private SharedStringTable? _sst;
    private string? _sstPartName;
    private bool _structureChanged;

    private Workbook(IOpcPackage package, OfficeFormat format, OpenContext context, string mainPartName, bool ownsPackage)
        : base(format, context, (package as OpcPackage)?.FilePath)
    {
        Package = package;
        MainPartName = mainPartName;
        _ownsPackage = ownsPackage;
        _collection = new SheetCollection(this, _sheets);
    }

    /// <inheritdoc />
    public override OfficeKind Kind => OfficeKind.Workbook;

    /// <summary>Gets the package the workbook sits on.</summary>
    public IOpcPackage Package { get; }

    /// <summary>Gets the part name of the workbook part, normally <c>/xl/workbook.xml</c>.</summary>
    public string MainPartName { get; }

    /// <summary>Gets the sheets in workbook order.</summary>
    public SheetCollection Sheets => _collection;

    /// <summary>Gets the markup tree of the workbook part: the hop from the hub to the markup layer.</summary>
    public MarkupDocument Markup => _tree;

    /// <summary>Gets a value indicating whether the workbook uses the Strict namespaces.</summary>
    public bool IsStrict => _tree.Root.NamespaceUri == MainNsStrict;

    internal OpenContext OpenContext => Context;

    internal Worksheet? FirstSheet => _sheets.Count > 0 ? _sheets[0] : null;

    /// <summary>
    /// Opens a workbook from a file. The format is found from the content, never the extension: XLSX, XLSM, XLTX, XLTM, XLAM and Flat OPC are accepted.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The workbook, which the caller disposes. Only the package structure, the workbook part and its relationships are read.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format (a document, a binary workbook…), named in the exception.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Workbook Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenSource(ByteSource.FromFile(path), context, Path.GetFullPath(path), cancellationToken);
    }

    /// <summary>Opens a workbook from a seekable stream.</summary>
    /// <param name="stream">The stream; parts are read from it lazily, so it must stay readable until the workbook is disposed.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the workbook is disposed.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The workbook, which the caller disposes. It has no <see cref="OfficeFile.FilePath"/>, so use <c>SaveAs</c>.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Workbook Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenSource(ByteSource.FromStream(stream, leaveOpen), context, null, cancellationToken);
    }

    /// <summary>Opens a workbook from a byte source, which the workbook then owns and disposes.</summary>
    /// <param name="source">The bytes; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The workbook, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Workbook Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        OpenSource(source, context, null, cancellationToken);

    internal static bool IsWorkbookFormat(OfficeFormat format) =>
        format is OfficeFormat.Xlsx or OfficeFormat.Xlsm or OfficeFormat.Xltx or OfficeFormat.Xltm or OfficeFormat.Xlam or OfficeFormat.FlatOpcWorkbook;

    internal static Workbook OpenSource(IByteSource source, OpenContext? context, string? filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OfficeFormat sniffed = FormatSniffer.Detect(source, context.Limits);
            if (sniffed == OfficeFormat.Unknown)
            {
                throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
            }

            if (sniffed is OfficeFormat.Csv or OfficeFormat.Tsv)
            {
                return Csv.CsvDocument.FromSource(source, sniffed, context, cancellationToken);
            }

            if (sniffed == OfficeFormat.Excel2003Xml)
            {
                // Converted at once into an in-memory XLSX package, so the file itself is not needed afterwards.
                Workbook converted = Xml2003.SpreadsheetMl2003.Open(source, context, cancellationToken);
                source.Dispose();
                return converted;
            }

            if (sniffed is OfficeFormat.Xls or OfficeFormat.XlsLegacy)
            {
                return Biff.XlsConverter.FromSource(source, context, cancellationToken);
            }

            if (!IsWorkbookFormat(sniffed))
            {
                throw new FormatNotAvailableException(sniffed, "open as a workbook:");
            }

            OpcPackage package = filePath is null ? OpcPackage.Open(source, context) : OpcPackage.Open(source, filePath, context);
            try
            {
                return FromPackage(package, context, ownsPackage: true);
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Opens the hub over a package that holds a workbook.</summary>
    /// <param name="package">An OPC package.</param>
    /// <param name="context">Limits and defect sink; null for the package's own limits.</param>
    /// <param name="ownsPackage">True to dispose the package when the workbook is disposed.</param>
    /// <returns>The workbook. Nothing but the workbook part and its relationships is read yet.</returns>
    /// <exception cref="OfficeFormatException">The package has no workbook part (<c>xlsx.no-main-part</c>), the part is not a workbook (<c>xlsx.wrong-root</c>) or is not well-formed XML.</exception>
    /// <exception cref="FormatNotAvailableException">The main part is a binary workbook or another kind of document.</exception>
    public static Workbook FromPackage(IOpcPackage package, OpenContext? context = null, bool ownsPackage = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        context ??= new OpenContext(package.Limits);
        string? main = FindMainPart(package);
        if (main is null)
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsx.no-main-part",
                "The package has no workbook part: no officeDocument relationship points at a part that exists.", DefectLocation.InPart("/_rels/.rels"));
        }

        string contentType = package.FindPart(main)!.ContentType;
        OfficeFormat format = FormatOfContentType(package, contentType);
        var wb = new Workbook(package, format, context, main, ownsPackage);
        wb.LoadMain();
        return wb;
    }

    private static string? FindMainPart(IOpcPackage package)
    {
        foreach (Relationship r in package.GetRelationships())
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && IsRelationship(r.Type, "officeDocument")
                && PartUri.TryResolve(null, r.Target, out PartUri uri) && package.FindPart(uri.Value) is { } part)
            {
                return part.Name;
            }
        }

        return null;
    }

    internal static bool IsRelationship(string type, string name) =>
        (type.StartsWith(RelBase, StringComparison.Ordinal) && type.AsSpan(RelBase.Length).SequenceEqual(name))
        || (type.StartsWith(RelBaseStrict, StringComparison.Ordinal) && type.AsSpan(RelBaseStrict.Length).SequenceEqual(name));

    private static OfficeFormat FormatOfContentType(IOpcPackage package, string type)
    {
        if (package is OpcPackage { IsFlatOpc: true })
        {
            return OfficeFormat.FlatOpcWorkbook;
        }

        if (string.Equals(type, MainXlsx, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xlsx;
        }

        if (string.Equals(type, MainXlsm, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xlsm;
        }

        if (string.Equals(type, MainXltx, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xltx;
        }

        if (string.Equals(type, MainXltm, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xltm;
        }

        if (string.Equals(type, MainXlam, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Xlam;
        }

        if (string.Equals(type, MainXlsb, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatNotAvailableException(OfficeFormat.Xlsb, "open as a workbook:");
        }

        // A main part with another content type is read as a plain workbook when its root says so (checked in LoadMain).
        return OfficeFormat.Xlsx;
    }

    private void LoadMain()
    {
        IPackagePart part = Package.FindPart(MainPartName)!;
        _tree = MarkupDocument.Parse(part.GetBytes(), Context, MainPartName);
        MarkupNode root = _tree.Root;
        if (root.IsNull || !root.LocalName.SequenceEqual("workbook"u8) || (root.NamespaceUri != MainNs && root.NamespaceUri != MainNsStrict))
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsx.wrong-root", "The workbook part does not start with a workbook element.",
                DefectLocation.InPart(MainPartName, root.IsNull ? -1 : root.SourceStart));
        }

        foreach (MarkupNode child in root.ChildElements)
        {
            if (child.LocalName.SequenceEqual("sheets"u8))
            {
                _sheetsNode = child;
                break;
            }
        }

        if (_sheetsNode.IsNull)
        {
            Context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.no-sheets", DefectSeverity.Repaired,
                "The workbook part has no sheets element; the workbook reads as having no sheets.", DefectLocation.InPart(MainPartName, root.SourceStart)));
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int implicitId = 0;
        foreach (MarkupNode s in _sheetsNode.ChildElements)
        {
            if (!s.LocalName.SequenceEqual("sheet"u8))
            {
                continue;
            }

            string name = s.GetAttribute("name") ?? string.Empty;
            int id = int.TryParse(s.GetAttribute("sheetId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : ++implicitId;
            string rid = s.GetAttribute(RelNs, "id") ?? s.GetAttribute(RelNsStrict, "id") ?? string.Empty;
            SheetState state = s.GetAttribute("state") switch { "hidden" => SheetState.Hidden, "veryHidden" => SheetState.VeryHidden, _ => SheetState.Visible };
            if (!names.Add(name))
            {
                Context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.sheet-name-duplicate", DefectSeverity.Repaired,
                    "Two sheets are named '" + name + "'; lookups by name find the first.", DefectLocation.InPart(MainPartName, s.SourceStart)));
            }

            (string? partName, SheetKind kind) = ResolveSheet(rid, s.SourceStart);
            var sheet = new Worksheet(this, name, id, rid, partName, kind, state, s);
            sheet.AdoptState(state);
            _sheets.Add(sheet);
        }
    }

    private (string? PartName, SheetKind Kind) ResolveSheet(string relationshipId, int offset)
    {
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.Id != relationshipId)
            {
                continue;
            }

            SheetKind kind = IsRelationship(r.Type, "chartsheet") ? SheetKind.Chartsheet
                : IsRelationship(r.Type, "dialogsheet") ? SheetKind.DialogSheet
                : IsRelationship(r.Type, "xlMacrosheet") || IsRelationship(r.Type, "xlIntlMacrosheet") ? SheetKind.MacroSheet
                : SheetKind.Worksheet;
            if (r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                return (part.Name, kind);
            }

            break;
        }

        Context.Defects.Report(new Defect(DefectKind.Packaging, "xlsx.sheet-part-missing", DefectSeverity.Dropped,
            "A sheet's relationship '" + relationshipId + "' does not lead to a part; the sheet reads as empty.", DefectLocation.InPart(MainPartName, offset)));
        return (null, SheetKind.Worksheet);
    }

    internal IPackagePart? FindPartForSheet(string partName) => Package.FindPart(partName);

    internal int VisibleSheetCount(Worksheet except)
    {
        int n = 0;
        foreach (Worksheet s in _sheets)
        {
            if (!ReferenceEquals(s, except) && s.State == SheetState.Visible)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// Gets the shared string table. It is read the first time it is needed; a workbook without one gets an empty table, which
    /// becomes a part when strings are added.
    /// </summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML.</exception>
    public SharedStringTable SharedStrings
    {
        get
        {
            if (_sst is not null)
            {
                return _sst;
            }

            foreach (Relationship r in Package.GetRelationships(MainPartName))
            {
                if (IsRelationship(r.Type, "sharedStrings") && r.TargetMode == RelationshipTargetMode.Internal
                    && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
                {
                    _sstPartName = part.Name;
                    _sst = SharedStringTable.Read(part.GetBytes(), Context, part.Name);
                    return _sst;
                }
            }

            _sst = SharedStringTable.CreateEmpty();
            return _sst;
        }
    }

    internal SharedStringTable? SharedStringsIfLoaded => _sst;

    internal string? SharedStringsPartName => _sstPartName;

    internal CellValue MakeStringValue(string text)
    {
        SharedStringTable sst = SharedStrings;
        int index = sst.Add(text);
        sst.NoteReference(1);
        return new CellValue(CellValueKind.Text, 0, index);
    }

    internal bool OwnsStringHandle(CellValue v) => v.StringRef >= 0 ? v.StringRef < SharedStrings.Count : true;

    /// <summary>Gets a value indicating whether the file asks the application to recalculate everything when it is opened (<c>calcPr fullCalcOnLoad</c>).</summary>
    public bool NeedsRecalculation => CalcPr()?.GetAttribute("fullCalcOnLoad") is "1" or "true";

    /// <summary>Gets a value indicating whether calculation is set to manual (<c>calcPr calcMode</c>).</summary>
    public bool IsManualCalculation => CalcPr()?.GetAttribute("calcMode") == "manual";

    private MarkupNode? CalcPr()
    {
        foreach (MarkupNode c in _tree.Root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("calcPr"u8))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// Asks the application that opens the file next to recalculate it: sets <c>fullCalcOnLoad</c> in the file's <c>calcPr</c>, unless
    /// calculation is manual. Nothing is calculated here.
    /// </summary>
    public void MarkNeedsRecalculation()
    {
        MarkupNode? calc = CalcPr();
        if (calc is { } c)
        {
            if (c.GetAttribute("calcMode") == "manual" || c.GetAttribute("fullCalcOnLoad") is "1" or "true")
            {
                return;
            }

            c.SetAttribute("fullCalcOnLoad", "1");
            return;
        }

        InsertWorkbookChild("calcPr", "<" + Pc() + "calcPr fullCalcOnLoad=\"1\"/>");
    }

    internal string Pc() => _tree.Root.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(_tree.Root.Prefix) + ":";

    // ECMA-376 Part 1 CT_Workbook: the child elements of workbook, in schema order.
    private static readonly string[] WorkbookOrder =
    [
        "fileVersion", "fileSharing", "workbookPr", "workbookProtection", "bookViews", "sheets", "functionGroups", "externalReferences",
        "definedNames", "calcPr", "oleSize", "customWorkbookViews", "pivotCaches", "smartTagPr", "smartTagTypes", "webPublishing",
        "fileRecoveryPr", "webPublishObjects", "extLst",
    ];

    internal void InsertWorkbookChild(string local, string xml)
    {
        int idx = Array.IndexOf(WorkbookOrder, local);
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        foreach (MarkupNode child in _tree.Root.ChildElements)
        {
            int ci = Array.IndexOf(WorkbookOrder, child.LocalNameString);
            if (ci > idx)
            {
                child.InsertXmlBefore(bytes);
                return;
            }
        }

        _tree.Root.AppendXml(bytes);
    }

    /// <summary>Gets a value indicating whether anything was changed since the workbook was opened or last committed.</summary>
    public bool HasUncommittedChanges
    {
        get
        {
            if (_tree.IsModified || (_sst?.IsModified ?? false) || _structureChanged || FeaturePartsModified())
            {
                return true;
            }

            foreach (Worksheet s in _sheets)
            {
                if (s.IsModified)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Writes everything that was edited back into the package: the sheets whose cells changed, the shared strings, the workbook part.
    /// The parts nobody edited are not touched, so the package copies them as stored. A stale calculation chain (one that lists a cell
    /// that no longer holds a formula, or a sheet that is gone) is removed, because Excel reports a damaged file for it.
    /// </summary>
    public void CommitChanges()
    {
        bool staleChain = _structureChanged;
        foreach (Worksheet s in _sheets)
        {
            if (s.IsLoaded && s.Store.FormulaRemoved)
            {
                staleChain = true;
            }
        }

        foreach (Worksheet s in _sheets)
        {
            s.Commit();
        }

        if (_sst is { IsModified: true } sst)
        {
            byte[] bytes = sst.ToBytes(IsStrict ? MainNsStrict : MainNs);
            if (_sstPartName is not null && Package.FindPart(_sstPartName) is { } existing)
            {
                existing.SetBytes(bytes);
            }
            else
            {
                string name = "/xl/sharedStrings.xml";
                int n = 1;
                while (Package.FindPart(name) is not null)
                {
                    name = "/xl/sharedStrings" + (++n).ToString(CultureInfo.InvariantCulture) + ".xml";
                }

                Package.AddPart(name, SharedStringsContentType, bytes);
                _sstPartName = name;
                var rels = new List<Relationship>(Package.GetRelationships(MainPartName))
                {
                    new Relationship(NewRelationshipId(), RelBase + "sharedStrings", RelativeTo(name)),
                };
                Package.SetRelationships(MainPartName, rels);
            }
        }

        CommitFeatureParts();

        if (staleChain)
        {
            DropCalcChain();
        }

        if (_tree.IsModified && Package.FindPart(MainPartName) is { } main)
        {
            main.SetBytes(_tree.ToArray());
        }

        _structureChanged = false;
    }

    private string RelativeTo(string partName) => partName.StartsWith("/xl/", StringComparison.Ordinal) && MainPartName.StartsWith("/xl/", StringComparison.Ordinal) ? partName[4..] : partName;

    internal string NewRelationshipId()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            used.Add(r.Id);
        }

        for (int i = 1; ; i++)
        {
            string id = "rId" + i.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }

    private void DropCalcChain()
    {
        var keep = new List<Relationship>();
        bool dropped = false;
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (IsRelationship(r.Type, "calcChain"))
            {
                dropped = true;
                if (r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri))
                {
                    Package.RemovePart(uri.Value);
                }
            }
            else
            {
                keep.Add(r);
            }
        }

        if (dropped)
        {
            Package.SetRelationships(MainPartName, keep);
        }
    }

    /// <summary>Saves to <see cref="OfficeFile.FilePath"/>, atomically. The edited parts are committed first.</summary>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <exception cref="InvalidOperationException">The workbook was not opened from a path.</exception>
    public override void Save(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (FilePath is null)
        {
            base.Save(cancellationToken);
            return;
        }

        CommitChanges();
        if (Package is OpcPackage { FilePath: not null } opc)
        {
            opc.Save(cancellationToken);
            return;
        }

        base.Save(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks><c>.xlsx</c>, <c>.xlsm</c>, <c>.xltx</c>, <c>.xltm</c> and <c>.xlam</c> name those formats and <c>.xml</c> names Flat OPC.</remarks>
    protected override OfficeFormat ResolveOutputFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase) ? OfficeFormat.FlatOpcWorkbook : base.ResolveOutputFormat(path);
    }

    /// <inheritdoc />
    protected override void WriteCore(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (IsWorkbookFormat(format))
        {
            Write(format, destination, cancellationToken);
        }
        else if (format is OfficeFormat.Csv or OfficeFormat.Tsv)
        {
            Csv.CsvDocument.Write(this, destination, new Csv.CsvWriteOptions { Delimiter = format == OfficeFormat.Tsv ? '\t' : ',' }, null, cancellationToken);
        }
        else
        {
            base.WriteCore(format, destination, cancellationToken);
        }
    }

    internal void Write(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        if (Package is not OpcPackage opc)
        {
            throw new FormatNotAvailableException(format, "write this package as");
        }

        if (format == OfficeFormat.FlatOpcWorkbook)
        {
            opc.WriteFlatOpcTo(destination, cancellationToken);
            return;
        }

        string target = MainContentTypeOf(format);
        string original = opc.FindPart(MainPartName)!.ContentType;
        bool retype = !string.Equals(original, target, StringComparison.OrdinalIgnoreCase);
        if (retype && format is OfficeFormat.Xlsx or OfficeFormat.Xltx)
        {
            foreach (IPackagePart p in opc.Parts)
            {
                if (string.Equals(p.ContentType, VbaProjectContentType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new OfficeUnsupportedException("saving a workbook that holds a macro project as a macro-free format", DefectLocation.InPart(p.Name));
                }
            }
        }

        try
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, target);
            }

            opc.WriteZipTo(destination, cancellationToken);
        }
        finally
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, original);
            }
        }
    }

    private static string MainContentTypeOf(OfficeFormat format) => format switch
    {
        OfficeFormat.Xlsx => MainXlsx,
        OfficeFormat.Xlsm => MainXlsm,
        OfficeFormat.Xltx => MainXltx,
        OfficeFormat.Xltm => MainXltm,
        OfficeFormat.Xlam => MainXlam,
        _ => throw new FormatNotAvailableException(format, "write a workbook as"),
    };

    /// <inheritdoc />
    protected override void ExtractTextCore(TextWriter output, CancellationToken cancellationToken)
    {
        CommitChanges();
        if (FormatRegistry.Default.TryGetTextExtractor(Format, out TextExtractionHandler? extractor))
        {
            using var buffer = new MemoryStream();
            Package.WriteTo(buffer, cancellationToken);
            using IByteSource source = ByteSource.FromMemory(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            extractor(source, Format, Context, output, cancellationToken);
            return;
        }

        foreach (Worksheet sheet in _sheets)
        {
            if (sheet.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (CellRowView row in sheet.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellValue v = row.ValueAt(i);
                    if (v.Kind == CellValueKind.Text)
                    {
                        output.WriteLine(sheet.GetText(v));
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsPackage)
        {
            Package.Dispose();
        }

        base.Dispose(disposing);
    }
}
