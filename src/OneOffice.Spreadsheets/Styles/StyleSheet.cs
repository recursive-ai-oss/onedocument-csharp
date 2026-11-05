using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>One element of a table style (CT_TableStyleElement, §18.8.40).</summary>
/// <param name="Type">The element type, such as <c>wholeTable</c>, <c>headerRow</c> or <c>firstRowStripe</c>.</param>
/// <param name="DxfId">The index into the differential formats.</param>
/// <param name="Size">The stripe size.</param>
public readonly record struct TableStyleElement(string Type, int DxfId, int Size);

/// <summary>A custom table or pivot style (CT_TableStyle, §18.8.39).</summary>
/// <param name="Name">The style's name.</param>
/// <param name="AppliesToPivots">The style can be used for pivot tables.</param>
/// <param name="AppliesToTables">The style can be used for tables.</param>
/// <param name="Elements">The formatted parts of the style.</param>
public sealed record TableStyleDefinition(string Name, bool AppliesToPivots, bool AppliesToTables, IReadOnlyList<TableStyleElement> Elements);

/// <summary>
/// The workbook's style sheet (the <c>styles</c> part, ECMA-376 Part 1 §18.8): number formats, fonts, fills, borders, cell formats, named
/// cell styles, differential formats and table styles. It is a view over the part's markup tree; what the model does not read (extension
/// lists, unknown attributes and elements) stays where it is when the part is saved. A cell holds an index into the cell formats
/// (<see cref="Cell.StyleIndex"/>); <see cref="Resolve"/> turns it into the effective font, fill, border, alignment and number format.
/// </summary>
/// <remarks>
/// <para>Resolution: a cell format's own ids are used whatever its <c>applyX</c> flags say (observed in Excel 16.0.20527); an alignment or protection child the cell format lacks comes from its named cell style.</para>
/// <para>Not safe for concurrent mutation; reading is safe from several threads once nothing is being added.</para>
/// </remarks>
public sealed class StyleSheet
{
    private static readonly string[] SheetOrder = ["numFmts", "fonts", "fills", "borders", "cellStyleXfs", "cellXfs", "cellStyles", "dxfs", "tableStyles", "colors", "extLst"];

    private readonly Workbook _workbook;
    private readonly MarkupDocument _doc;
    private readonly string _pc;
    private readonly Dictionary<int, string> _numFmts = [];
    private readonly Dictionary<string, int> _numFmtIds = new(StringComparer.Ordinal);
    private readonly List<MarkupNode> _fonts = [];
    private readonly List<MarkupNode> _fills = [];
    private readonly List<MarkupNode> _borders = [];
    private readonly List<MarkupNode> _styleXfs = [];
    private readonly List<MarkupNode> _xfs = [];
    private readonly List<MarkupNode> _dxfs = [];
    private readonly List<NamedCellStyle> _cellStyles = [];
    private readonly Dictionary<int, string> _styleNameByXf = [];
    private readonly ConcurrentDictionary<string, NumberFormat> _formatCache = new(StringComparer.Ordinal);
    private ResolvedCellStyle?[] _resolved = [];
    private NumberFormat?[] _formatByXf = [];
    private int[]? _palette;
    private ThemeColors? _theme;
    private Dictionary<string, int>? _fontKeys, _fillKeys, _borderKeys, _xfKeys;

    internal StyleSheet(Workbook workbook, MarkupDocument doc, string? partName)
    {
        _workbook = workbook;
        _doc = doc;
        PartName = partName;
        MarkupNode root = doc.Root;
        _pc = Xl.Prefix(root);
        Load(workbook.OpenContext, partName ?? "/xl/styles.xml");
    }

    /// <summary>Gets the part name, or null for a style sheet that has not been written yet.</summary>
    public string? PartName { get; internal set; }

    /// <summary>Gets the markup tree of the part, for the typed views and any element the model does not read.</summary>
    public MarkupDocument Markup => _doc;

    /// <summary>Gets a value indicating whether the style sheet has to be written at the next save.</summary>
    public bool IsModified => _doc.IsModified;

    /// <summary>Gets the number of fonts.</summary>
    public int FontCount => _fonts.Count;

    /// <summary>Gets the number of fills.</summary>
    public int FillCount => _fills.Count;

    /// <summary>Gets the number of borders.</summary>
    public int BorderCount => _borders.Count;

    /// <summary>Gets the number of cell formats (the valid values of <see cref="Cell.StyleIndex"/>).</summary>
    public int CellFormatCount => _xfs.Count;

    /// <summary>Gets the number of differential formats.</summary>
    public int DifferentialFormatCount => _dxfs.Count;

    /// <summary>Gets the named cell styles, in the order the part lists them.</summary>
    public IReadOnlyList<NamedCellStyle> CellStyles => _cellStyles;

    /// <summary>Gets the custom number formats as a map of id to code (ids 164 and up, plus any built-in id the file redefines).</summary>
    public IReadOnlyDictionary<int, string> NumberFormats => _numFmts;

    /// <summary>Gets the table style the part names as default for new tables, or null.</summary>
    public string? DefaultTableStyle => Xl.Child(_doc.Root, "tableStyles") is { IsNull: false } t ? t.GetAttribute("defaultTableStyle") : null;

    /// <summary>Gets the custom table and pivot styles defined in the part.</summary>
    public IReadOnlyList<TableStyleDefinition> TableStyles
    {
        get
        {
            var list = new List<TableStyleDefinition>();
            MarkupNode ts = Xl.Child(_doc.Root, "tableStyles");
            if (ts.IsNull)
            {
                return list;
            }

            foreach (MarkupNode s in ts.ChildElements)
            {
                if (s.LocalNameString != "tableStyle")
                {
                    continue;
                }

                var elements = new List<TableStyleElement>();
                foreach (MarkupNode e in s.ChildElements)
                {
                    if (e.LocalNameString == "tableStyleElement")
                    {
                        elements.Add(new TableStyleElement(e.GetAttribute("type") ?? string.Empty, Xl.Int(e.GetAttribute("dxfId"), -1), Xl.Int(e.GetAttribute("size"), 1)));
                    }
                }

                list.Add(new TableStyleDefinition(s.GetAttribute("name") ?? string.Empty, s.GetAttribute("pivot") != "0", s.GetAttribute("table") != "0", elements));
            }

            return list;
        }
    }

    private void Load(OpenContext context, string partName)
    {
        MarkupNode root = _doc.Root;
        if (root.IsNull || !root.LocalName.SequenceEqual("styleSheet"u8))
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsx.styles-wrong-root", "The styles part does not start with a styleSheet element.", DefectLocation.InPart(partName, root.IsNull ? -1 : root.SourceStart));
        }

        foreach (MarkupNode c in root.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "numFmts":
                    foreach (MarkupNode n in c.ChildElements)
                    {
                        if (n.LocalNameString == "numFmt" && int.TryParse(n.GetAttribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && n.GetAttribute("formatCode") is { } code)
                        {
                            _numFmts[id] = code;
                            _numFmtIds.TryAdd(code, id);
                        }
                        else
                        {
                            context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.numfmt-invalid", DefectSeverity.Dropped, "A numFmt has no valid id or format code; it was ignored.", DefectLocation.InPart(partName, n.SourceStart)));
                        }
                    }

                    break;
                case "fonts": Collect(c, "font", _fonts); break;
                case "fills": Collect(c, "fill", _fills); break;
                case "borders": Collect(c, "border", _borders); break;
                case "cellStyleXfs": Collect(c, "xf", _styleXfs); break;
                case "cellXfs": Collect(c, "xf", _xfs); break;
                case "dxfs": Collect(c, "dxf", _dxfs); break;
                case "cellStyles":
                    foreach (MarkupNode n in c.ChildElements)
                    {
                        if (n.LocalNameString == "cellStyle")
                        {
                            var s = new NamedCellStyle(n.GetAttribute("name") ?? string.Empty, Xl.Int(n.GetAttribute("xfId")), Xl.Int(n.GetAttribute("builtinId"), -1), Xl.FlagAttr(n, "hidden"));
                            _cellStyles.Add(s);
                            _styleNameByXf.TryAdd(s.FormatId, s.Name);
                        }
                    }

                    break;
                case "colors":
                    MarkupNode ic = Xl.Child(c, "indexedColors");
                    if (!ic.IsNull)
                    {
                        var pal = new List<int>();
                        foreach (MarkupNode rc in ic.ChildElements)
                        {
                            pal.Add(StyleColor.Read(rc).Value);
                        }

                        _palette = [.. pal];
                    }

                    break;
            }
        }

        _resolved = new ResolvedCellStyle?[_xfs.Count];
        _formatByXf = new NumberFormat?[_xfs.Count];
    }

    private static void Collect(MarkupNode parent, string local, List<MarkupNode> into)
    {
        foreach (MarkupNode n in parent.ChildElements)
        {
            if (n.LocalNameString == local)
            {
                into.Add(n);
            }
        }
    }

    /// <summary>Gets a font by index.</summary>
    /// <param name="index">The index into fonts.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public CellFont GetFont(int index) => CellFont.Read(_fonts[index]);

    /// <summary>Gets a fill by index.</summary>
    /// <param name="index">The index into fills.</param>
    /// <returns>The fill.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public CellFill GetFill(int index) => CellFill.Read(_fills[index]);

    /// <summary>Gets a border by index.</summary>
    /// <param name="index">The index into borders.</param>
    /// <returns>The border.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public CellBorder GetBorder(int index) => CellBorder.Read(_borders[index]);

    /// <summary>Gets a cell format as stored.</summary>
    /// <param name="index">The index into cell formats.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public CellFormat GetCellFormat(int index) => ReadXf(_xfs[index]);

    /// <summary>Gets a named cell style's format (an entry of <c>cellStyleXfs</c>) as stored.</summary>
    /// <param name="index">The index into <c>cellStyleXfs</c>.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public CellFormat GetStyleFormat(int index) => ReadXf(_styleXfs[index]);

    /// <summary>Gets a differential format.</summary>
    /// <param name="index">The index into <c>dxfs</c>.</param>
    /// <returns>The format; each part is null when the dxf does not set it. Note that a solid fill in a dxf shows its background colour, not the foreground.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public DifferentialFormat GetDifferentialFormat(int index)
    {
        MarkupNode d = _dxfs[index];
        CellFont? font = null;
        string? code = null;
        CellFill? fill = null;
        CellBorder? border = null;
        CellAlignment? al = null;
        CellProtection? pr = null;
        foreach (MarkupNode c in d.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "font": font = CellFont.Read(c); break;
                case "numFmt": code = c.GetAttribute("formatCode"); break;
                case "fill": fill = CellFill.Read(c); break;
                case "border": border = CellBorder.Read(c); break;
                case "alignment": al = CellAlignment.Read(c); break;
                case "protection": pr = CellProtection.Read(c); break;
            }
        }

        return new DifferentialFormat(font, code, fill, border, al, pr);
    }

    private static CellFormat ReadXf(MarkupNode x)
    {
        CellAlignment? al = null;
        CellProtection? pr = null;
        foreach (MarkupNode c in x.ChildElements)
        {
            if (c.LocalName.SequenceEqual("alignment"u8)) al = CellAlignment.Read(c);
            else if (c.LocalName.SequenceEqual("protection"u8)) pr = CellProtection.Read(c);
        }

        return new CellFormat(Xl.Int(x.GetAttribute("numFmtId")), Xl.Int(x.GetAttribute("fontId")), Xl.Int(x.GetAttribute("fillId")), Xl.Int(x.GetAttribute("borderId")), Xl.Int(x.GetAttribute("xfId")),
            Xl.OptFlag(x, "applyNumberFormat"), Xl.OptFlag(x, "applyFont"), Xl.OptFlag(x, "applyFill"), Xl.OptFlag(x, "applyBorder"), Xl.OptFlag(x, "applyAlignment"), Xl.OptFlag(x, "applyProtection"),
            Xl.FlagAttr(x, "quotePrefix"), al, pr);
    }

    /// <summary>Gets the format code for a number format id: the file's own definition first, then the built-in table (US English).</summary>
    /// <param name="numberFormatId">The id.</param>
    /// <returns>The code; <c>General</c> for an id nothing defines.</returns>
    public string GetNumberFormatCode(int numberFormatId) =>
        _numFmts.TryGetValue(numberFormatId, out string? c) ? c : NumberFormat.GetBuiltInCode(numberFormatId) ?? "General";

    /// <summary>
    /// Resolves a cell format index into the effective style. An index outside the cell formats resolves like format 0, because Excel
    /// treats a missing format as the default.
    /// </summary>
    /// <param name="formatIndex">The index (what <see cref="Cell.StyleIndex"/> holds).</param>
    /// <returns>The resolved style; the same object for the same index.</returns>
    public ResolvedCellStyle Resolve(int formatIndex)
    {
        if (formatIndex < 0 || formatIndex >= _xfs.Count)
        {
            if (_xfs.Count == 0)
            {
                return DefaultResolved;
            }

            formatIndex = 0;
        }

        ResolvedCellStyle? r = _resolved[formatIndex];
        if (r is not null)
        {
            return r;
        }

        MarkupNode x = _xfs[formatIndex];
        int xfId = Xl.Int(x.GetAttribute("xfId"));
        MarkupNode s = xfId >= 0 && xfId < _styleXfs.Count ? _styleXfs[xfId] : default;
        int Pick(string id) => Xl.Int(x.GetAttribute(id));
        int nf = Pick("numFmtId");
        int fontId = Pick("fontId");
        int fillId = Pick("fillId");
        int borderId = Pick("borderId");

        CellAlignment? al = null;
        CellProtection? pr = null;
        foreach (MarkupNode src in new[] { x, s })
        {
            if (src.IsNull)
            {
                continue;
            }

            foreach (MarkupNode c in src.ChildElements)
            {
                if (al is null && c.LocalName.SequenceEqual("alignment"u8)) al = CellAlignment.Read(c);
            }

            if (al is not null)
            {
                break;
            }
        }

        foreach (MarkupNode src in new[] { x, s })
        {
            if (src.IsNull)
            {
                continue;
            }

            foreach (MarkupNode c in src.ChildElements)
            {
                if (pr is null && c.LocalName.SequenceEqual("protection"u8)) pr = CellProtection.Read(c);
            }

            if (pr is not null)
            {
                break;
            }
        }

        r = new ResolvedCellStyle(formatIndex, _styleNameByXf.GetValueOrDefault(xfId), nf, GetNumberFormatCode(nf),
            fontId >= 0 && fontId < _fonts.Count ? GetFont(fontId) : DefaultFont,
            fillId >= 0 && fillId < _fills.Count ? GetFill(fillId) : new CellFill(),
            borderId >= 0 && borderId < _borders.Count ? GetBorder(borderId) : new CellBorder(),
            al ?? new CellAlignment(), pr ?? new CellProtection(), Xl.FlagAttr(x, "quotePrefix"));
        _resolved[formatIndex] = r;
        return r;
    }

    private static CellFont DefaultFont { get; } = new("Calibri", 11, Family: 2, Scheme: "minor", Color: StyleColor.FromTheme(1));

    private static ResolvedCellStyle DefaultResolved { get; } = new(0, "Normal", 0, "General", DefaultFont, new CellFill(), new CellBorder(), new CellAlignment(), new CellProtection(), false);

    /// <summary>Gets the parsed number format of a cell format, cached per code.</summary>
    /// <param name="formatIndex">The cell format index.</param>
    /// <returns>The number format; <c>General</c> when the code does not parse.</returns>
    public NumberFormat GetNumberFormat(int formatIndex)
    {
        if (formatIndex >= 0 && formatIndex < _formatByXf.Length && _formatByXf[formatIndex] is { } cached)
        {
            return cached;
        }

        string code = Resolve(formatIndex).NumberFormatCode;
        NumberFormat f = _formatCache.GetOrAdd(code, static c => NumberFormat.TryParse(c, out NumberFormat? nf, out _) ? nf : NumberFormat.General);
        if (formatIndex >= 0 && formatIndex < _formatByXf.Length)
        {
            _formatByXf[formatIndex] = f;
        }

        return f;
    }

    // ---- colours ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a colour to ARGB: palette colours through the file's <c>indexedColors</c> override or the default palette (ECMA-376 §18.8.27),
    /// theme colours through the workbook's theme part, with the tint applied in HLS space (§18.3.1.15).
    /// </summary>
    /// <param name="color">The colour.</param>
    /// <returns>The ARGB value; opaque black for automatic or absent colours.</returns>
    public int ResolveColor(StyleColor color)
    {
        int argb;
        switch (color.Kind)
        {
            case ColorKind.Rgb:
                argb = color.Value;
                break;
            case ColorKind.Indexed:
                argb = color.Value switch
                {
                    64 => unchecked((int)0xFF000000),
                    65 => unchecked((int)0xFFFFFFFF),
                    >= 0 when _palette is { } p && color.Value < p.Length => p[color.Value],
                    >= 0 and < 64 => unchecked((int)(0xFF000000u | ThemeColors.DefaultPalette[color.Value])),
                    _ => unchecked((int)0xFF000000),
                };
                break;
            case ColorKind.Theme:
                _theme ??= _workbook.LoadTheme();
                argb = _theme.Get(color.Value);
                break;
            default:
                return unchecked((int)0xFF000000);
        }

        return color.Tint == 0 ? argb : ThemeColors.ApplyTint(argb, color.Tint);
    }

    // ---- adding -----------------------------------------------------------------------------------------------------------------

    private int Append(string collection, List<MarkupNode> list, string xml)
    {
        MarkupNode coll = Xl.Child(_doc.Root, collection);
        if (coll.IsNull)
        {
            coll = Xl.InsertInOrder(_doc.Root, SheetOrder, collection, "<" + _pc + collection + " count=\"0\"/>");
        }

        coll.AppendXml(Encoding.UTF8.GetBytes(xml));
        list.Add(Xl.LastElement(coll));
        coll.SetAttribute("count", list.Count.ToString(CultureInfo.InvariantCulture));
        return list.Count - 1;
    }

    private void EnsureDefaults()
    {
        if (_fonts.Count == 0) Append("fonts", _fonts, DefaultFont.ToXml(_pc));
        if (_fills.Count == 0)
        {
            Append("fills", _fills, new CellFill().ToXml(_pc));
            Append("fills", _fills, new CellFill(FillPattern.Gray125).ToXml(_pc));
        }

        if (_borders.Count == 0) Append("borders", _borders, new CellBorder().ToXml(_pc));
        if (_styleXfs.Count == 0)
        {
            Append("cellStyleXfs", _styleXfs, "<" + _pc + "xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>");
        }

        if (_xfs.Count == 0)
        {
            Append("cellXfs", _xfs, "<" + _pc + "xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
            Array.Resize(ref _resolved, _xfs.Count);
            Array.Resize(ref _formatByXf, _xfs.Count);
        }
    }

    private int GetOrAddIn(string collection, List<MarkupNode> list, ref Dictionary<string, int>? keys, string key, Func<MarkupNode, string> keyOf, string xml)
    {
        if (keys is null)
        {
            keys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                keys.TryAdd(keyOf(list[i]), i);
            }
        }

        if (keys.TryGetValue(key, out int found))
        {
            return found;
        }

        int idx = Append(collection, list, xml);
        keys[key] = idx;
        return idx;
    }

    /// <summary>Finds the id of a number format code, adding a <c>numFmt</c> when the code is neither built in nor already defined.</summary>
    /// <param name="code">The format code; null or <c>General</c> gives 0.</param>
    /// <returns>The id.</returns>
    public int GetOrAddNumberFormat(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "General")
        {
            return 0;
        }

        if (_numFmtIds.TryGetValue(code, out int id))
        {
            return id;
        }

        int builtIn = NumberFormat.FindBuiltInId(code);
        if (builtIn >= 0 && !_numFmts.ContainsKey(builtIn))
        {
            return builtIn;
        }

        id = 164;
        foreach (int k in _numFmts.Keys)
        {
            id = Math.Max(id, k + 1);
        }

        MarkupNode coll = Xl.Child(_doc.Root, "numFmts");
        if (coll.IsNull)
        {
            coll = Xl.InsertInOrder(_doc.Root, SheetOrder, "numFmts", "<" + _pc + "numFmts count=\"0\"/>");
        }

        coll.AppendXml(Encoding.UTF8.GetBytes("<" + _pc + "numFmt numFmtId=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" formatCode=\"" + Xl.Esc(code) + "\"/>"));
        _numFmts[id] = code;
        _numFmtIds[code] = id;
        coll.SetAttribute("count", _numFmts.Count.ToString(CultureInfo.InvariantCulture));
        return id;
    }

    /// <summary>
    /// Finds the cell format that matches <paramref name="spec"/>, or adds one. An existing format with the same number format, font, fill,
    /// border, alignment, protection and named style is reused (apply flags are not compared), so equal requests give one index.
    /// Fonts, fills and borders are interned the same way.
    /// </summary>
    /// <param name="spec">The description.</param>
    /// <returns>The index to store in <see cref="Cell.StyleIndex"/>.</returns>
    /// <exception cref="ArgumentException">The spec names a cell style that does not exist.</exception>
    public int GetOrAdd(CellStyleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        EnsureDefaults();
        int xfId = 0;
        if (spec.StyleName is { } sn && sn != "Normal")
        {
            xfId = -1;
            foreach (NamedCellStyle ncs in _cellStyles)
            {
                if (string.Equals(ncs.Name, sn, StringComparison.OrdinalIgnoreCase))
                {
                    xfId = ncs.FormatId;
                    break;
                }
            }

            if (xfId < 0)
            {
                throw new ArgumentException("The workbook has no cell style named '" + sn + "'.", nameof(spec));
            }
        }

        int nf = GetOrAddNumberFormat(spec.NumberFormatCode);
        int font = spec.Font is null ? 0 : GetOrAddIn("fonts", _fonts, ref _fontKeys, spec.Font.ToXml(string.Empty), n => CellFont.Read(n).ToXml(string.Empty), spec.Font.ToXml(_pc));
        int fill = spec.Fill is null ? 0 : GetOrAddIn("fills", _fills, ref _fillKeys, spec.Fill.ToXml(string.Empty), n => CellFill.Read(n).ToXml(string.Empty), spec.Fill.ToXml(_pc));
        int border = spec.Border is null ? 0 : GetOrAddIn("borders", _borders, ref _borderKeys, spec.Border.ToXml(string.Empty), n => CellBorder.Read(n).ToXml(string.Empty), spec.Border.ToXml(_pc));
        CellAlignment? al = spec.Alignment is { } a && a != new CellAlignment() ? a : null;
        CellProtection? pr = spec.Protection is { } p && p != new CellProtection() ? p : null;

        string key = XfKey(nf, font, fill, border, xfId, spec.QuotePrefix, al?.ToXml(string.Empty), pr?.ToXml(string.Empty));
        if (_xfKeys is null)
        {
            _xfKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _xfs.Count; i++)
            {
                _xfKeys.TryAdd(KeyOfXf(_xfs[i], i), i);
            }
        }

        if (_xfKeys.TryGetValue(key, out int found))
        {
            return found;
        }

        MarkupNode style = xfId < _styleXfs.Count ? _styleXfs[xfId] : default;
        var sb = new StringBuilder("<").Append(_pc).Append("xf numFmtId=\"").Append(nf.ToString(CultureInfo.InvariantCulture)).Append("\" fontId=\"").Append(font.ToString(CultureInfo.InvariantCulture))
            .Append("\" fillId=\"").Append(fill.ToString(CultureInfo.InvariantCulture)).Append("\" borderId=\"").Append(border.ToString(CultureInfo.InvariantCulture)).Append("\" xfId=\"").Append(xfId.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (nf != Xl.Int(style.IsNull ? null : style.GetAttribute("numFmtId"))) sb.Append(" applyNumberFormat=\"1\"");
        if (font != Xl.Int(style.IsNull ? null : style.GetAttribute("fontId"))) sb.Append(" applyFont=\"1\"");
        if (fill != Xl.Int(style.IsNull ? null : style.GetAttribute("fillId"))) sb.Append(" applyFill=\"1\"");
        if (border != Xl.Int(style.IsNull ? null : style.GetAttribute("borderId"))) sb.Append(" applyBorder=\"1\"");
        if (al is not null) sb.Append(" applyAlignment=\"1\"");
        if (pr is not null) sb.Append(" applyProtection=\"1\"");
        if (spec.QuotePrefix) sb.Append(" quotePrefix=\"1\"");
        if (al is null && pr is null)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append('>');
            if (al is not null) sb.Append(al.ToXml(_pc));
            if (pr is not null) sb.Append(pr.ToXml(_pc));
            sb.Append("</").Append(_pc).Append("xf>");
        }

        int idx = Append("cellXfs", _xfs, sb.ToString());
        Array.Resize(ref _resolved, _xfs.Count);
        Array.Resize(ref _formatByXf, _xfs.Count);
        _xfKeys[key] = idx;
        return idx;
    }

    private static string XfKey(int nf, int font, int fill, int border, int xfId, bool quote, string? al, string? pr) =>
        string.Join('|', nf, font, fill, border, xfId, quote ? 1 : 0, al, pr);

    private static string KeyOfXf(MarkupNode x, int index)
    {
        // An xf with children or attributes the model does not read never matches a request, so it is never reused with the wrong meaning.
        string? al = null, pr = null;
        foreach (MarkupNode c in x.ChildElements)
        {
            if (c.LocalName.SequenceEqual("alignment"u8)) { CellAlignment a = CellAlignment.Read(c); al = a == new CellAlignment() ? null : a.ToXml(string.Empty); }
            else if (c.LocalName.SequenceEqual("protection"u8)) { CellProtection p = CellProtection.Read(c); pr = p == new CellProtection() ? null : p.ToXml(string.Empty); }
            else return "!" + index.ToString(CultureInfo.InvariantCulture);
        }

        foreach (MarkupAttributeRef at in x.Attributes)
        {
            if (at.IsNamespaceDeclaration || at.Prefix.Length != 0)
            {
                return "!" + index.ToString(CultureInfo.InvariantCulture);
            }

            string n = Encoding.UTF8.GetString(at.RawName);
            if (n is not ("numFmtId" or "fontId" or "fillId" or "borderId" or "xfId" or "quotePrefix" or "applyNumberFormat" or "applyFont" or "applyFill" or "applyBorder" or "applyAlignment" or "applyProtection"))
            {
                return "!" + index.ToString(CultureInfo.InvariantCulture);
            }
        }

        return XfKey(Xl.Int(x.GetAttribute("numFmtId")), Xl.Int(x.GetAttribute("fontId")), Xl.Int(x.GetAttribute("fillId")), Xl.Int(x.GetAttribute("borderId")), Xl.Int(x.GetAttribute("xfId")), Xl.FlagAttr(x, "quotePrefix"), al, pr);
    }

    /// <summary>Adds a differential format (for conditional formatting and table styles).</summary>
    /// <param name="format">The format; only the parts that are not null are written.</param>
    /// <returns>The index to use as <c>dxfId</c>.</returns>
    public int AddDifferentialFormat(DifferentialFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var sb = new StringBuilder("<").Append(_pc).Append("dxf>");
        if (format.Font is { } f) sb.Append(f.ToXml(_pc));
        if (format.NumberFormatCode is { } code) sb.Append('<').Append(_pc).Append("numFmt numFmtId=\"").Append(GetOrAddNumberFormat(code).ToString(CultureInfo.InvariantCulture)).Append("\" formatCode=\"").Append(Xl.Esc(code)).Append("\"/>");
        if (format.Fill is { } fl) sb.Append(fl.ToXml(_pc));
        if (format.Alignment is { } al) sb.Append(al.ToXml(_pc));
        if (format.Border is { } b) sb.Append(b.ToXml(_pc));
        if (format.Protection is { } p) sb.Append(p.ToXml(_pc));
        sb.Append("</").Append(_pc).Append("dxf>");
        return Append("dxfs", _dxfs, sb.ToString());
    }

    /// <summary>Adds a named cell style built from <paramref name="spec"/>.</summary>
    /// <param name="name">The style's name; unique, case-insensitively.</param>
    /// <param name="spec">The formatting the style carries.</param>
    /// <returns>The index of the style's format in <c>cellStyleXfs</c>; pass the name as <see cref="CellStyleSpec.StyleName"/> to build cell formats on it.</returns>
    /// <exception cref="ArgumentException">A style of that name exists, or the name is empty.</exception>
    public int AddNamedStyle(string name, CellStyleSpec spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(spec);
        foreach (NamedCellStyle s in _cellStyles)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The workbook already has a cell style named '" + name + "'.", nameof(name));
            }
        }

        EnsureDefaults();
        int nf = GetOrAddNumberFormat(spec.NumberFormatCode);
        int font = spec.Font is null ? 0 : GetOrAddIn("fonts", _fonts, ref _fontKeys, spec.Font.ToXml(string.Empty), n => CellFont.Read(n).ToXml(string.Empty), spec.Font.ToXml(_pc));
        int fill = spec.Fill is null ? 0 : GetOrAddIn("fills", _fills, ref _fillKeys, spec.Fill.ToXml(string.Empty), n => CellFill.Read(n).ToXml(string.Empty), spec.Fill.ToXml(_pc));
        int border = spec.Border is null ? 0 : GetOrAddIn("borders", _borders, ref _borderKeys, spec.Border.ToXml(string.Empty), n => CellBorder.Read(n).ToXml(string.Empty), spec.Border.ToXml(_pc));
        var sb = new StringBuilder("<").Append(_pc).Append("xf numFmtId=\"").Append(nf.ToString(CultureInfo.InvariantCulture)).Append("\" fontId=\"").Append(font.ToString(CultureInfo.InvariantCulture))
            .Append("\" fillId=\"").Append(fill.ToString(CultureInfo.InvariantCulture)).Append("\" borderId=\"").Append(border.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (nf != 0) sb.Append(" applyNumberFormat=\"1\"");
        if (font != 0) sb.Append(" applyFont=\"1\"");
        if (fill != 0) sb.Append(" applyFill=\"1\"");
        if (border != 0) sb.Append(" applyBorder=\"1\"");
        sb.Append("/>");
        int xfId = Append("cellStyleXfs", _styleXfs, sb.ToString());
        MarkupNode cs = Xl.Child(_doc.Root, "cellStyles");
        if (cs.IsNull)
        {
            cs = Xl.InsertInOrder(_doc.Root, SheetOrder, "cellStyles", "<" + _pc + "cellStyles count=\"0\"/>");
        }

        if (_cellStyles.Count == 0)
        {
            cs.AppendXml(Encoding.UTF8.GetBytes("<" + _pc + "cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>"));
            _cellStyles.Add(new NamedCellStyle("Normal", 0, 0, false));
            _styleNameByXf.TryAdd(0, "Normal");
        }

        cs.AppendXml(Encoding.UTF8.GetBytes("<" + _pc + "cellStyle name=\"" + Xl.Esc(name) + "\" xfId=\"" + xfId.ToString(CultureInfo.InvariantCulture) + "\" customBuiltin=\"1\"/>"));
        _cellStyles.Add(new NamedCellStyle(name, xfId, -1, false));
        _styleNameByXf.TryAdd(xfId, name);
        cs.SetAttribute("count", _cellStyles.Count.ToString(CultureInfo.InvariantCulture));
        return xfId;
    }

    internal static byte[] NewPartBytes(string ns, string defaultTableStyle = "TableStyleMedium2", string defaultPivotStyle = "PivotStyleLight16") =>
        Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"" + ns + "\">"
            + "<fonts count=\"1\">" + DefaultFont.ToXml(string.Empty) + "</fonts>"
            + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
            + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
            + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
            + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
            + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
            + "<dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"" + defaultTableStyle + "\" defaultPivotStyle=\"" + defaultPivotStyle + "\"/></styleSheet>");

    internal byte[] ToBytes() => _doc.ToArray();

}
