using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private sealed class Note
    {
        public int Row;
        public int Col;
        public int ObjectId;
        public string Author = string.Empty;
        public string? Text;
    }

    private sealed class CondFormat
    {
        public string SqRef = string.Empty;
        public int Row;
        public int Col;
        public int Expected;
        public List<string> Rules = [];
    }

    private sealed class SheetFeatureData
    {
        public readonly List<(string Ref, string? Location, string? Target, string? Display)> Links = [];
        public readonly List<Note> Notes = [];
        public readonly Dictionary<int, string> ObjectTexts = [];
        public int LastObjectId = -1;
        public int LastObjectType = -1;
        public readonly StringBuilder Validations = new();
        public int ValidationCount;
        public readonly List<string> CondFormats = [];
        public CondFormat? CurrentCf;
        public int CfPriority = 1;
        public int Window2 = -1;
        public int TopRow;
        public int LeftCol;
        public int ScaleNormal;
        public int Zoom;
        public (int X, int Y, int TopRow, int LeftCol, int Active)? Pane;
        public bool ProtectSheet;
        public bool ProtectObjects;
        public bool ProtectScenarios;
        public int Password;
        public int DefColWidth = -1;
        public int DefRowHeightFlags = -1;
        public int DefRowHeight;
        public readonly double?[] Margins = new double?[4];
        public string? Header;
        public string? Footer;
        public bool HCenter;
        public bool VCenter;
        public bool PrintGrid;
        public bool PrintHeaders;
        public byte[]? Setup;
        public int WsBool = -1;
        public readonly List<int> HBreaks = [];
        public readonly List<int> VBreaks = [];
        public bool AutoFilter;
        public int AutoFilterCriteria;
        public readonly List<byte[]> Drawing = [];
        public long Shapes;
        public long Controls;
    }

    private SheetFeatureData _sheetFeatures = new();

    private void HandleSheetFeature(SheetCtx sc, BiffRecord r, ref int i)
    {
        SheetFeatureData f = _sheetFeatures;
        switch (r.Type)
        {
            case 0x003E:
            case 0x023E:
                f.Window2 = U16(r, 0);
                f.TopRow = U16(r, 2);
                f.LeftCol = U16(r, 4);
                if (r.Length >= 14)
                {
                    f.ScaleNormal = U16(r, 12);
                }

                break;
            case 0x0041:
                f.Pane = (U16(r, 0), U16(r, 2), U16(r, 4), U16(r, 6), U8(r, 8));
                break;
            case 0x00A0:
                if (U16(r, 2) != 0)
                {
                    f.Zoom = U16(r, 0) * 100 / U16(r, 2);
                }

                break;
            case 0x0012:
                f.ProtectSheet = U16(r, 0) != 0;
                break;
            case 0x0063:
                f.ProtectObjects = U16(r, 0) != 0;
                break;
            case 0x00DD:
                f.ProtectScenarios = U16(r, 0) != 0;
                break;
            case 0x0013:
                f.Password = U16(r, 0);
                break;
            case 0x0055:
                f.DefColWidth = U16(r, 0);
                break;
            case 0x0225:
            case 0x0025:
                if (_version >= 3)
                {
                    f.DefRowHeightFlags = U16(r, 0);
                    f.DefRowHeight = U16(r, 2);
                }
                else
                {
                    f.DefRowHeightFlags = 0;
                    f.DefRowHeight = U16(r, 0) & 0x7FFF;
                }

                break;
            case 0x0026:
                f.Margins[0] = F64(r, 0);
                break;
            case 0x0027:
                f.Margins[1] = F64(r, 0);
                break;
            case 0x0028:
                f.Margins[2] = F64(r, 0);
                break;
            case 0x0029:
                f.Margins[3] = F64(r, 0);
                break;
            case 0x0014:
                f.Header = HeaderFooterText(r);
                break;
            case 0x0015:
                f.Footer = HeaderFooterText(r);
                break;
            case 0x0083:
                f.HCenter = U16(r, 0) != 0;
                break;
            case 0x0084:
                f.VCenter = U16(r, 0) != 0;
                break;
            case 0x002B:
                f.PrintGrid = U16(r, 0) != 0;
                break;
            case 0x002A:
                f.PrintHeaders = U16(r, 0) != 0;
                break;
            case 0x00A1:
                f.Setup = _rs.Data(r).ToArray();
                break;
            case 0x0081:
                f.WsBool = U16(r, 0);
                break;
            case 0x001B:
                ReadBreaks(r, f.HBreaks);
                break;
            case 0x001A:
                ReadBreaks(r, f.VBreaks);
                break;
            case 0x009D:
                f.AutoFilter = true;
                break;
            case 0x009E:
                f.AutoFilterCriteria++;
                break;
            case 0x01B8:
                HyperlinkRecord(r);
                break;
            case 0x001C:
                NoteRecord(sc, r, ref i);
                break;
            case 0x005D:
                ObjRecord(r);
                break;
            case 0x01B6:
                TextObject(r, ref i);
                break;
            case 0x01BE:
                DataValidation(r);
                break;
            case 0x01B0:
                CondFmtRecord(r);
                break;
            case 0x01B1:
                CfRecord(r);
                break;
            case 0x0879:
                CondFmt12Record(r);
                break;
            case 0x087A:
                Cf12Record(r);
                break;
            case 0x087B:
                // CFEx extends a plain CF rule with the properties of the 2007 kinds; the rule itself is read, the extension is not.
                _lossCounts["loss.conditional-formats-extended"] = _lossCounts.GetValueOrDefault("loss.conditional-formats-extended") + 1;
                break;
            case 0x00EC:
                {
                    RecordReader rd = RecordReader.Open(_rs, i, withContinues: true, out int last);
                    i = last;
                    f.Drawing.Add(rd.RestJoined());
                    break;
                }

            case 0x00EB:
                break;
            case 0x00B0:
            case 0x00B1:
            case 0x00B2:
            case 0x00B4:
            case 0x00B5:
            case 0x00B6:
            case 0x00C5:
            case 0x00F0:
            case 0x00F1:
            case 0x00F2:
            case 0x00F7:
                _lossCounts["loss.pivot-tables"] = _lossCounts.GetValueOrDefault("loss.pivot-tables") + (r.Type == 0x00B0 ? 1 : 0);
                break;
            case 0x01AE:
            case 0x0023:
            case 0x0223:
            case 0x0017:
            case 0x0018:
            case 0x0218:
                break;
            default:
                // BIFF2-4 sheet substreams carry the workbook records (fonts, formats, XFs) in the same stream.
                if (_version < 5)
                {
                    HandleGlobal(r, ref i);
                }

                break;
        }
    }

    private static void ReadBreaksCore(ReadOnlySpan<byte> d, List<int> into)
    {
        if (d.Length < 2)
        {
            return;
        }

        int n = d[0] | (d[1] << 8);
        for (int k = 0; k < n && 2 + k * 6 + 2 <= d.Length; k++)
        {
            into.Add(d[2 + k * 6] | (d[3 + k * 6] << 8));
        }
    }

    private void ReadBreaks(BiffRecord r, List<int> into) => ReadBreaksCore(_rs.Data(r).Span, into);

    private string HeaderFooterText(BiffRecord r)
    {
        if (r.Length == 0)
        {
            return string.Empty;
        }

        RecordReader rd = Reader(r);
        return _version >= 8 ? rd.UnicodeString(2, true, _enc8) : rd.UnicodeStringShort(_enc8);
    }

    // ---- hyperlinks ----

    private static readonly Guid UrlMoniker = new("79EAC9E0-BAF9-11CE-8C82-00AA004BA90B");
    private static readonly Guid FileMoniker = new("00000303-0000-0000-C000-000000000046");

    // [MS-XLS] §2.4.140 HLink and [MS-OSHARED] §2.3.7 HyperlinkObject: a reference, a class id, a version, flags, then the optional parts in order.
    private void HyperlinkRecord(BiffRecord r)
    {
        RecordReader rd = Reader(r);
        int r1 = rd.U16();
        int r2 = rd.U16();
        int c1 = rd.U16();
        int c2 = rd.U16();
        rd.Skip(16);
        rd.U32();
        uint flags = rd.U32();
        string? display = null;
        string? target = null;
        string? location = null;
        if ((flags & 0x14) == 0x14)
        {
            display = HyperlinkString(rd);
        }

        if ((flags & 0x80) != 0)
        {
            HyperlinkString(rd);
        }

        if ((flags & 1) != 0)
        {
            if ((flags & 0x100) != 0)
            {
                target = HyperlinkString(rd);
            }
            else
            {
                byte[] clsidBytes = rd.Bytes(16);
                var clsid = new Guid(clsidBytes);
                if (clsid == UrlMoniker)
                {
                    int len = rd.I32();
                    byte[] b = rd.Bytes(Math.Max(0, Math.Min(len, rd.Remaining)));
                    string url = Encoding.Unicode.GetString(b);
                    int z = url.IndexOf('\0', StringComparison.Ordinal);
                    target = z >= 0 ? url[..z] : url;
                }
                else if (clsid == FileMoniker)
                {
                    int anti = rd.U16();
                    int ansiLen = rd.I32();
                    byte[] ansi = rd.Bytes(Math.Max(0, Math.Min(ansiLen, rd.Remaining)));
                    rd.Skip(2 + 2 + 20);
                    string path = _enc8.GetString(ansi).TrimEnd('\0');
                    int uniSize = rd.I32();
                    if (uniSize > 0 && uniSize <= rd.Remaining)
                    {
                        int uniBytes = rd.I32();
                        rd.Skip(2);
                        byte[] ub = rd.Bytes(Math.Max(0, Math.Min(uniBytes, rd.Remaining)));
                        path = Encoding.Unicode.GetString(ub).TrimEnd('\0');
                    }

                    target = (string.Concat(Enumerable.Repeat("..\\", anti)) + path).Replace('\\', '/');
                }
                else
                {
                    _lossCounts["loss.hyperlinks"] = _lossCounts.GetValueOrDefault("loss.hyperlinks") + 1;
                    return;
                }
            }
        }

        if ((flags & 8) != 0)
        {
            location = HyperlinkString(rd);
        }

        if (target is null && location is null)
        {
            return;
        }

        _sheetFeatures.Links.Add((RangeText(r1, c1, r2, c2), location, target, display));
    }

    private static string HyperlinkString(RecordReader rd)
    {
        int cch = rd.I32();
        if (cch <= 0 || cch > rd.Remaining / 2)
        {
            return string.Empty;
        }

        string s = rd.Chars(cch, true, Encoding.Latin1);
        return s.TrimEnd('\0');
    }

    // ---- notes ----

    private void NoteRecord(SheetCtx sc, BiffRecord r, ref int i)
    {
        _ = sc;
        SheetFeatureData f = _sheetFeatures;
        if (_version >= 8)
        {
            RecordReader rd = Reader(r);
            var n = new Note { Row = rd.U16(), Col = rd.U16() };
            rd.U16();
            n.ObjectId = rd.U16();
            n.Author = rd.UnicodeString(2, true, _enc8);
            f.Notes.Add(n);
            return;
        }

        // BIFF2-5: the note text itself, in chunks; a chunk with row 0xFFFF continues the previous note.
        int row = U16(r, 0);
        int col = U16(r, 2);
        int cch = U16(r, 4);
        if (row == 0xFFFF && f.Notes.Count > 0)
        {
            string more = _enc8.GetString(_rs.Bytes.Span.Slice(r.DataOffset + 6, Math.Min(cch, Math.Max(0, r.Length - 6))));
            f.Notes[^1].Text += more;
            return;
        }

        string text = _enc8.GetString(_rs.Bytes.Span.Slice(r.DataOffset + 6, Math.Min(cch, Math.Max(0, r.Length - 6))));
        f.Notes.Add(new Note { Row = row, Col = col, Text = text });
    }

    // [MS-XLS] §2.4.181 Obj: a sequence of sub-records; ftCmo (0x15) names the object type and identifier.
    private void ObjRecord(BiffRecord r)
    {
        SheetFeatureData f = _sheetFeatures;
        int p = 0;
        while (p + 4 <= r.Length)
        {
            int ft = U16(r, p);
            int cb = U16(r, p + 2);
            if (ft == 0x15 && cb >= 6)
            {
                f.LastObjectType = U16(r, p + 4);
                f.LastObjectId = U16(r, p + 6);
                // ot: 0x08 picture, 0x19 note, 0x14 drop-down (filter and validation arrows), 0x06 chart; the rest are drawn shapes or controls.
                if (f.LastObjectType is 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x09 or 0x0E or 0x1E or 0x00)
                {
                    f.Shapes++;
                }
                else if (f.LastObjectType is not (0x08 or 0x19 or 0x14 or 0x06))
                {
                    f.Controls++;
                }

                return;
            }

            if (ft == 0)
            {
                break;
            }

            p += 4 + cb;
        }
    }

    // [MS-XLS] §2.4.329 TxO: the text of a text box or note is in the CONTINUE records after it, led by a flag byte.
    private void TextObject(BiffRecord r, ref int i)
    {
        SheetFeatureData f = _sheetFeatures;
        int cchText = U16(r, 10);
        RecordReader rd = RecordReader.Open(_rs, i, withContinues: true, out int last);
        i = last;
        if (cchText == 0 || rd.RemainingInPart == rd.Remaining)
        {
            return;
        }

        rd.Skip(rd.RemainingInPart);
        byte flag = rd.U8();
        string text = rd.Chars(cchText, (flag & 1) != 0, Encoding.Latin1);
        if (f.LastObjectType == 0x19 && f.LastObjectId >= 0)
        {
            f.ObjectTexts[f.LastObjectId] = text;
        }
    }

    // ---- data validation ----

    private void DataValidation(BiffRecord r)
    {
        SheetFeatureData f = _sheetFeatures;
        RecordReader rd = Reader(r);
        uint flags = rd.U32();
        int type = (int)(flags & 0xF);
        int style = (int)((flags >> 4) & 7);
        bool strList = (flags & 0x80) != 0;
        bool allowBlank = (flags & 0x100) != 0;
        bool suppress = (flags & 0x200) != 0;
        bool showInput = (flags & 0x40000) != 0;
        bool showError = (flags & 0x80000) != 0;
        int op = (int)((flags >> 20) & 0xF);
        string promptTitle = rd.UnicodeString(2, true, _enc8);
        string errorTitle = rd.UnicodeString(2, true, _enc8);
        string prompt = rd.UnicodeString(2, true, _enc8);
        string error = rd.UnicodeString(2, true, _enc8);
        int cce1 = rd.U16();
        rd.U16();
        byte[] g1 = rd.Bytes(cce1);
        int cce2 = rd.U16();
        rd.U16();
        byte[] g2 = rd.Bytes(cce2);
        int cref = rd.U16();
        var refs = new List<string>();
        int firstRow = 0;
        int firstCol = 0;
        for (int k = 0; k < cref && rd.Remaining >= 8; k++)
        {
            int r1 = rd.U16();
            int r2 = rd.U16();
            int c1 = rd.U16();
            int c2 = rd.U16();
            if (k == 0)
            {
                firstRow = r1;
                firstCol = c1;
            }

            refs.Add(RangeText(r1, c1, r2, c2));
        }

        if (refs.Count == 0)
        {
            return;
        }

        string[] types = ["none", "whole", "decimal", "list", "date", "time", "textLength", "custom"];
        string[] ops = ["between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];
        string[] styles = ["stop", "warning", "information"];
        if (type >= types.Length)
        {
            return;
        }

        string? formula1 = null;
        string? formula2 = null;
        if (type == 3 && strList && cce1 > 0 && g1[0] == 0x17)
        {
            formula1 = ListLiteral(g1);
        }
        else
        {
            if (cce1 > 0)
            {
                formula1 = DecodeToText(g1, ReadOnlyMemory<byte>.Empty, firstRow, firstCol, r.Offset);
            }

            if (cce2 > 0 && type is not (0 or 3 or 7) && op < 2)
            {
                formula2 = DecodeToText(g2, ReadOnlyMemory<byte>.Empty, firstRow, firstCol, r.Offset);
            }
        }

        if (type != 0 && formula1 is null)
        {
            _lossCounts["loss.data-validation"] = _lossCounts.GetValueOrDefault("loss.data-validation") + 1;
            return;
        }

        var sb = f.Validations;
        sb.Append("<dataValidation");
        if (type != 0)
        {
            sb.Append(" type=\"").Append(types[type]).Append('"');
        }

        if (style != 0 && style < 3)
        {
            sb.Append(" errorStyle=\"").Append(styles[style]).Append('"');
        }

        if (type is not (0 or 3 or 7) && op != 0 && op < 8)
        {
            sb.Append(" operator=\"").Append(ops[op]).Append('"');
        }

        if (allowBlank)
        {
            sb.Append(" allowBlank=\"1\"");
        }

        if (suppress && type == 3)
        {
            sb.Append(" showDropDown=\"1\"");
        }

        if (showInput)
        {
            sb.Append(" showInputMessage=\"1\"");
        }

        if (showError)
        {
            sb.Append(" showErrorMessage=\"1\"");
        }

        if (errorTitle.Length > 0)
        {
            sb.Append(" errorTitle=\"").Append(Esc(errorTitle)).Append('"');
        }

        if (error.Length > 0)
        {
            sb.Append(" error=\"").Append(Esc(error)).Append('"');
        }

        if (promptTitle.Length > 0)
        {
            sb.Append(" promptTitle=\"").Append(Esc(promptTitle)).Append('"');
        }

        if (prompt.Length > 0)
        {
            sb.Append(" prompt=\"").Append(Esc(prompt)).Append('"');
        }

        sb.Append(" sqref=\"").Append(string.Join(' ', refs)).Append("\">");
        if (formula1 is not null)
        {
            sb.Append("<formula1>").Append(Esc(formula1)).Append("</formula1>");
        }

        if (formula2 is not null)
        {
            sb.Append("<formula2>").Append(Esc(formula2)).Append("</formula2>");
        }

        sb.Append("</dataValidation>");
        f.ValidationCount++;
    }

    // A list typed into the dialog is one string constant whose items are separated by NUL characters.
    private static string? ListLiteral(byte[] g)
    {
        int cch = g.Length > 1 ? g[1] : 0;
        bool wide = g.Length > 2 && (g[2] & 1) != 0;
        int bytes = wide ? cch * 2 : cch;
        if (3 + bytes > g.Length)
        {
            return null;
        }

        string s = wide ? Encoding.Unicode.GetString(g, 3, bytes) : Encoding.Latin1.GetString(g, 3, bytes);
        string items = s.Replace('\0', ',');
        return "\"" + items.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    // ---- conditional formatting ----

    // [MS-XLS] §2.4.56 CondFmt: the number of rules, flags, the bounding range, then the ranges; CF records follow.
    private void CondFmtRecord(BiffRecord r)
    {
        FlushCf();
        int cref = U16(r, 12);
        var refs = new List<string>();
        for (int k = 0; k < cref && 14 + k * 8 + 8 <= r.Length; k++)
        {
            int at = 14 + k * 8;
            refs.Add(RangeText(U16(r, at), U16(r, at + 4), U16(r, at + 2), U16(r, at + 6)));
        }

        if (refs.Count == 0)
        {
            return;
        }

        _sheetFeatures.CurrentCf = new CondFormat { SqRef = string.Join(' ', refs), Row = U16(r, 4), Col = U16(r, 8), Expected = U16(r, 0) };
    }

    private void FlushCf()
    {
        SheetFeatureData f = _sheetFeatures;
        if (f.CurrentCf is { } cf && cf.Rules.Count > 0)
        {
            f.CondFormats.Add("<conditionalFormatting sqref=\"" + cf.SqRef + "\">" + string.Concat(cf.Rules) + "</conditionalFormatting>");
        }

        f.CurrentCf = null;
    }

    // [MS-XLS] §2.4.42 CF: type (1 cell value, 2 formula), comparison, two token lengths, a DXFN, then the tokens.
    private void CfRecord(BiffRecord r)
    {
        SheetFeatureData f = _sheetFeatures;
        if (f.CurrentCf is not { } cf)
        {
            return;
        }

        int ct = U8(r, 0);
        int cp = U8(r, 1);
        int cce1 = U16(r, 2);
        int cce2 = U16(r, 4);
        int dxfLen = r.Length - 6 - cce1 - cce2;
        if (dxfLen < 0)
        {
            Defect("biff.cf-length", DefectSeverity.Dropped, "A conditional-format rule is shorter than its parts.", r.Offset);
            return;
        }

        ReadOnlySpan<byte> all = _rs.Data(r).Span;
        string dxf = DxfXml(all.Slice(6, dxfLen));
        _dxfs.Add(dxf);
        int dxfId = _dxfs.Count - 1;
        ReadOnlyMemory<byte> g1 = _rs.Data(r).Slice(6 + dxfLen, cce1);
        ReadOnlyMemory<byte> g2 = _rs.Data(r).Slice(6 + dxfLen + cce1, cce2);
        string? t1 = cce1 > 0 ? DecodeToText(g1, ReadOnlyMemory<byte>.Empty, cf.Row, cf.Col, r.Offset) : null;
        string? t2 = cce2 > 0 ? DecodeToText(g2, ReadOnlyMemory<byte>.Empty, cf.Row, cf.Col, r.Offset) : null;
        if (t1 is null)
        {
            _lossCounts["loss.conditional-formats"] = _lossCounts.GetValueOrDefault("loss.conditional-formats") + 1;
            return;
        }

        string[] ops = ["", "between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];
        var sb = new StringBuilder();
        if (ct == 1 && cp is >= 1 and <= 8)
        {
            sb.Append("<cfRule type=\"cellIs\" dxfId=\"").Append(I(dxfId)).Append("\" priority=\"").Append(I(f.CfPriority++)).Append("\" operator=\"").Append(ops[cp]).Append("\"><formula>")
                .Append(Esc(t1)).Append("</formula>");
            if (t2 is not null && cp <= 2)
            {
                sb.Append("<formula>").Append(Esc(t2)).Append("</formula>");
            }

            sb.Append("</cfRule>");
        }
        else if (ct == 2)
        {
            sb.Append("<cfRule type=\"expression\" dxfId=\"").Append(I(dxfId)).Append("\" priority=\"").Append(I(f.CfPriority++)).Append("\"><formula>").Append(Esc(t1)).Append("</formula></cfRule>");
        }
        else
        {
            _lossCounts["loss.conditional-formats"] = _lossCounts.GetValueOrDefault("loss.conditional-formats") + 1;
            return;
        }

        cf.Rules.Add(sb.ToString());
    }

    // ---- end of sheet ----

    private int _commentPartCounter;

    private void FinishSheet(SheetCtx sc)
    {
        SheetFeatureData f = _sheetFeatures;
        FlushCf();
        ResolveFormulas(sc);
        if (sc.Columns.Count > 0)
        {
            sc.Sheet.SetColumns(sc.Columns.OrderBy(c => c.Min));
        }

        Worksheet ws = sc.Sheet;
        var rels = new List<Relationship>();
        if (ws.PartName is { } part)
        {
            rels.AddRange(_wb.Package.GetRelationships(part));
        }

        string RelId() => "rId" + I(rels.Count + 1);

        // sheetPr
        var pr = new StringBuilder();
        if (f.WsBool >= 0 && ((f.WsBool & 0x40) == 0 || (f.WsBool & 0x80) == 0))
        {
            pr.Append("<outlinePr");
            if ((f.WsBool & 0x40) == 0)
            {
                pr.Append(" summaryBelow=\"0\"");
            }

            if ((f.WsBool & 0x80) == 0)
            {
                pr.Append(" summaryRight=\"0\"");
            }

            pr.Append("/>");
        }

        if (f.WsBool >= 0 && (f.WsBool & 0x100) != 0)
        {
            pr.Append("<pageSetUpPr fitToPage=\"1\"/>");
        }

        if (pr.Length > 0)
        {
            ws.InsertFrameElement("sheetPr", "<sheetPr>" + pr + "</sheetPr>");
        }

        // sheetFormatPr
        if (f.DefRowHeightFlags >= 0 || f.DefColWidth >= 0)
        {
            var sb = new StringBuilder("<sheetFormatPr");
            if (f.DefColWidth >= 0)
            {
                sb.Append(" baseColWidth=\"").Append(I(f.DefColWidth)).Append('"');
            }

            double ht = f.DefRowHeight > 0 ? f.DefRowHeight / 20.0 : 15;
            sb.Append(" defaultRowHeight=\"").Append(ht.ToString("0.##", CultureInfo.InvariantCulture)).Append('"');
            if (f.DefRowHeightFlags >= 0 && (f.DefRowHeightFlags & 1) != 0)
            {
                sb.Append(" customHeight=\"1\"");
            }

            if (f.DefRowHeightFlags >= 0 && (f.DefRowHeightFlags & 2) != 0)
            {
                sb.Append(" zeroHeight=\"1\"");
            }

            sb.Append("/>");
            ws.InsertFrameElement("sheetFormatPr", sb.ToString());
        }

        // sheetViews
        if (f.Window2 >= 0)
        {
            var sb = new StringBuilder("<sheetViews><sheetView");
            if ((f.Window2 & 0x01) != 0)
            {
                sb.Append(" showFormulas=\"1\"");
            }

            if ((f.Window2 & 0x02) == 0)
            {
                sb.Append(" showGridLines=\"0\"");
            }

            if ((f.Window2 & 0x04) == 0)
            {
                sb.Append(" showRowColHeaders=\"0\"");
            }

            if ((f.Window2 & 0x10) == 0)
            {
                sb.Append(" showZeros=\"0\"");
            }

            if ((f.Window2 & 0x40) != 0)
            {
                sb.Append(" rightToLeft=\"1\"");
            }

            if ((f.Window2 & 0x200) != 0 && sc.Index == 0)
            {
                sb.Append(" tabSelected=\"1\"");
            }

            if (f.Zoom > 0 && f.Zoom != 100)
            {
                sb.Append(" zoomScale=\"").Append(I(f.Zoom)).Append('"');
            }

            if (f.TopRow != 0 || f.LeftCol != 0)
            {
                sb.Append(" topLeftCell=\"").Append(A1(f.TopRow, f.LeftCol)).Append('"');
            }

            sb.Append(" workbookViewId=\"0\">");
            if (f.Pane is { } p && (p.X != 0 || p.Y != 0))
            {
                bool frozen = (f.Window2 & 0x08) != 0;
                string active = p.Active switch { 0 => "bottomRight", 1 => "topRight", 2 => "bottomLeft", _ => "topLeft" };
                if (p.X == 0)
                {
                    active = "bottomLeft";
                }
                else if (p.Y == 0)
                {
                    active = "topRight";
                }

                sb.Append("<pane");
                if (p.X != 0)
                {
                    sb.Append(" xSplit=\"").Append(I(p.X)).Append('"');
                }

                if (p.Y != 0)
                {
                    sb.Append(" ySplit=\"").Append(I(p.Y)).Append('"');
                }

                sb.Append(" topLeftCell=\"").Append(A1(p.TopRow, p.LeftCol)).Append("\" activePane=\"").Append(active).Append('"');
                if (frozen)
                {
                    sb.Append(" state=\"frozen\"");
                }

                sb.Append("/>");
            }

            sb.Append("</sheetView></sheetViews>");
            ws.InsertFrameElement("sheetViews", sb.ToString());
        }

        if (f.ProtectSheet || f.ProtectObjects || f.ProtectScenarios)
        {
            var sb = new StringBuilder("<sheetProtection");
            if (f.Password != 0)
            {
                sb.Append(" password=\"").Append(f.Password.ToString("X4", CultureInfo.InvariantCulture)).Append('"');
            }

            if (f.ProtectSheet)
            {
                sb.Append(" sheet=\"1\"");
            }

            if (f.ProtectObjects)
            {
                sb.Append(" objects=\"1\"");
            }

            if (f.ProtectScenarios)
            {
                sb.Append(" scenarios=\"1\"");
            }

            sb.Append("/>");
            ws.InsertFrameElement("sheetProtection", sb.ToString());
        }

        if (f.AutoFilter)
        {
            DefinedNameInfo? fd = _names.FirstOrDefault(n => n.IsBuiltIn && n.BuiltIn == 0x0D && n.SheetScope == sc.Index);
            if (fd?.Formula is { Length: > 0 } ff)
            {
                int bang = ff.LastIndexOf('!');
                ws.InsertFrameElement("autoFilter", "<autoFilter ref=\"" + Esc(ff[(bang + 1)..].Replace("$", string.Empty, StringComparison.Ordinal)) + "\"/>");
            }
        }

        if (f.AutoFilterCriteria > 0)
        {
            _lossCounts["loss.autofilter-criteria"] = _lossCounts.GetValueOrDefault("loss.autofilter-criteria") + 1;
        }

        if (f.ValidationCount > 0)
        {
            ws.InsertFrameElement("dataValidations", "<dataValidations count=\"" + I(f.ValidationCount) + "\">" + f.Validations + "</dataValidations>");
        }

        foreach (string cf in f.CondFormats)
        {
            ws.InsertFrameElement("conditionalFormatting", cf);
        }

        if (f.Links.Count > 0)
        {
            var sb = new StringBuilder("<hyperlinks xmlns:r=\"" + RelNs + "\">");
            foreach ((string reference, string? location, string? target, string? display) in f.Links)
            {
                sb.Append("<hyperlink ref=\"").Append(reference).Append('"');
                if (target is not null)
                {
                    string id = RelId();
                    rels.Add(new Relationship(id, RelBase + "hyperlink", target, RelationshipTargetMode.External));
                    sb.Append(" r:id=\"").Append(id).Append('"');
                }

                if (location is not null)
                {
                    sb.Append(" location=\"").Append(Esc(location)).Append('"');
                }

                if (!string.IsNullOrEmpty(display))
                {
                    sb.Append(" display=\"").Append(Esc(display)).Append('"');
                }

                sb.Append("/>");
            }

            sb.Append("</hyperlinks>");
            ws.InsertFrameElement("hyperlinks", sb.ToString());
        }

        if (f.HCenter || f.VCenter || f.PrintGrid || f.PrintHeaders)
        {
            var sb = new StringBuilder("<printOptions");
            if (f.HCenter)
            {
                sb.Append(" horizontalCentered=\"1\"");
            }

            if (f.VCenter)
            {
                sb.Append(" verticalCentered=\"1\"");
            }

            if (f.PrintHeaders)
            {
                sb.Append(" headings=\"1\"");
            }

            if (f.PrintGrid)
            {
                sb.Append(" gridLines=\"1\"");
            }

            sb.Append("/>");
            ws.InsertFrameElement("printOptions", sb.ToString());
        }

        double hdr = 0.5;
        double ftr = 0.5;
        if (f.Setup is { Length: >= 34 } su)
        {
            hdr = BitConverter.ToDouble(su, 16);
            ftr = BitConverter.ToDouble(su, 24);
        }

        if (f.Margins.Any(m => m.HasValue) || f.Setup is not null)
        {
            string M(double? v, double d) => (v ?? d).ToString("0.######", CultureInfo.InvariantCulture);
            ws.InsertFrameElement("pageMargins", "<pageMargins left=\"" + M(f.Margins[0], 0.75) + "\" right=\"" + M(f.Margins[1], 0.75) + "\" top=\"" + M(f.Margins[2], 1)
                + "\" bottom=\"" + M(f.Margins[3], 1) + "\" header=\"" + M(hdr, 0.5) + "\" footer=\"" + M(ftr, 0.5) + "\"/>");
        }

        if (f.Setup is { Length: >= 12 } s)
        {
            int paper = s[0] | (s[1] << 8);
            int scale = s[2] | (s[3] << 8);
            int start = (short)(s[4] | (s[5] << 8));
            int fitW = s[6] | (s[7] << 8);
            int fitH = s[8] | (s[9] << 8);
            int flags = s[10] | (s[11] << 8);
            bool noPls = (flags & 4) != 0;
            var sb = new StringBuilder("<pageSetup");
            if (!noPls && paper > 0)
            {
                sb.Append(" paperSize=\"").Append(I(paper)).Append('"');
            }

            if (scale is > 0 and not 100)
            {
                sb.Append(" scale=\"").Append(I(scale)).Append('"');
            }

            if ((flags & 0x80) != 0 && start != 1)
            {
                sb.Append(" firstPageNumber=\"").Append(I(start)).Append("\" useFirstPageNumber=\"1\"");
            }

            if (fitW != 1)
            {
                sb.Append(" fitToWidth=\"").Append(I(fitW)).Append('"');
            }

            if (fitH != 1)
            {
                sb.Append(" fitToHeight=\"").Append(I(fitH)).Append('"');
            }

            if ((flags & 1) != 0)
            {
                sb.Append(" pageOrder=\"overThenDown\"");
            }

            if (!noPls && (flags & 0x40) == 0)
            {
                sb.Append(" orientation=\"").Append((flags & 2) != 0 ? "portrait" : "landscape").Append('"');
            }

            if ((flags & 8) != 0)
            {
                sb.Append(" blackAndWhite=\"1\"");
            }

            if ((flags & 0x10) != 0)
            {
                sb.Append(" draft=\"1\"");
            }

            if ((flags & 0x20) != 0)
            {
                sb.Append(" cellComments=\"").Append((flags & 0x200) != 0 ? "atEnd" : "asDisplayed").Append('"');
            }

            sb.Append("/>");
            ws.InsertFrameElement("pageSetup", sb.ToString());
        }

        if (!string.IsNullOrEmpty(f.Header) || !string.IsNullOrEmpty(f.Footer))
        {
            var sb = new StringBuilder("<headerFooter>");
            if (!string.IsNullOrEmpty(f.Header))
            {
                sb.Append("<oddHeader>").Append(Esc(f.Header)).Append("</oddHeader>");
            }

            if (!string.IsNullOrEmpty(f.Footer))
            {
                sb.Append("<oddFooter>").Append(Esc(f.Footer)).Append("</oddFooter>");
            }

            sb.Append("</headerFooter>");
            ws.InsertFrameElement("headerFooter", sb.ToString());
        }

        if (f.HBreaks.Count > 0)
        {
            ws.InsertFrameElement("rowBreaks", "<rowBreaks count=\"" + I(f.HBreaks.Count) + "\" manualBreakCount=\"" + I(f.HBreaks.Count) + "\">"
                + string.Concat(f.HBreaks.Select(b => "<brk id=\"" + I(b) + "\" max=\"16383\" man=\"1\"/>")) + "</rowBreaks>");
        }

        if (f.VBreaks.Count > 0)
        {
            ws.InsertFrameElement("colBreaks", "<colBreaks count=\"" + I(f.VBreaks.Count) + "\" manualBreakCount=\"" + I(f.VBreaks.Count) + "\">"
                + string.Concat(f.VBreaks.Select(b => "<brk id=\"" + I(b) + "\" max=\"1048575\" man=\"1\"/>")) + "</colBreaks>");
        }

        WriteComments(sc, rels, RelId);
        WriteDrawing(sc, rels, RelId);
        if (f.Shapes > 0 && !_drawingConverted)
        {
            _lossCounts["loss.drawing-shapes"] = _lossCounts.GetValueOrDefault("loss.drawing-shapes") + f.Shapes;
        }

        if (f.Controls > 0)
        {
            _lossCounts["loss.form-controls"] = _lossCounts.GetValueOrDefault("loss.form-controls") + f.Controls;
        }

        if (ws.PartName is { } pn && rels.Count > 0)
        {
            _wb.Package.SetRelationships(pn, rels);
        }
    }

    private bool _drawingConverted;

    // ---- comments ----

    private void WriteComments(SheetCtx sc, List<Relationship> rels, Func<string> relId)
    {
        SheetFeatureData f = _sheetFeatures;
        var notes = f.Notes.Where(n => n.Row != 0xFFFF).ToList();
        if (notes.Count == 0)
        {
            return;
        }

        int n = ++_commentPartCounter;
        var authors = new List<string>();
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<comments xmlns=\"" + MainNs + "\"><authors>");
        var comments = new StringBuilder("<commentList>");
        foreach (Note note in notes)
        {
            string text = note.Text ?? (f.ObjectTexts.TryGetValue(note.ObjectId, out string? t) ? t : string.Empty);
            int ai = authors.IndexOf(note.Author);
            if (ai < 0)
            {
                ai = authors.Count;
                authors.Add(note.Author);
            }

            comments.Append("<comment ref=\"").Append(A1(note.Row, note.Col)).Append("\" authorId=\"").Append(I(ai)).Append("\"><text><t xml:space=\"preserve\">").Append(Esc(text))
                .Append("</t></text></comment>");
        }

        foreach (string a in authors)
        {
            xml.Append("<author>").Append(Esc(a)).Append("</author>");
        }

        xml.Append("</authors>").Append(comments).Append("</commentList></comments>");
        string commentsName = "/xl/comments" + I(n) + ".xml";
        _wb.Package.AddPart(commentsName, "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml", Encoding.UTF8.GetBytes(xml.ToString()));
        string cid = relId();
        rels.Add(new Relationship(cid, RelBase + "comments", "../comments" + I(n) + ".xml"));

        // Excel draws a note from its legacy drawing; a plain box per note is enough for it to show them.
        var vml = new StringBuilder();
        vml.Append("<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">")
            .Append("<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"").Append(I(n)).Append("\"/></o:shapelayout>")
            .Append("<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\"><v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>");
        for (int k = 0; k < notes.Count; k++)
        {
            Note note = notes[k];
            vml.Append("<v:shape id=\"_x0000_s").Append(I(1024 * n + 1 + k)).Append("\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:59.25pt;margin-top:1.5pt;width:108pt;height:59.25pt;z-index:")
                .Append(I(k + 1)).Append(";visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\"><v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/>")
                .Append("<v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox><x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/><x:Anchor>")
                .Append(I(note.Col + 1)).Append(", 15, ").Append(I(Math.Max(note.Row - 1, 0))).Append(", 10, ").Append(I(note.Col + 3)).Append(", 15, ").Append(I(note.Row + 3))
                .Append(", 4</x:Anchor><x:AutoFill>False</x:AutoFill><x:Row>").Append(I(note.Row)).Append("</x:Row><x:Column>").Append(I(note.Col)).Append("</x:Column></x:ClientData></v:shape>");
        }

        vml.Append("</xml>");
        string vmlName = "/xl/drawings/vmlDrawing" + I(n) + ".vml";
        _wb.Package.AddPart(vmlName, "application/vnd.openxmlformats-officedocument.vmlDrawing", Encoding.UTF8.GetBytes(vml.ToString()));
        string vid = relId();
        rels.Add(new Relationship(vid, RelBase + "vmlDrawing", "../drawings/vmlDrawing" + I(n) + ".vml"));
        sc.Sheet.InsertFrameElement("legacyDrawing", "<legacyDrawing xmlns:r=\"" + RelNs + "\" r:id=\"" + vid + "\"/>");
    }

    // ---- names ----

    private void ApplyNames()
    {
        foreach (DefinedNameInfo n in _names)
        {
            if (n.Formula is null)
            {
                Lose("loss.defined-names", "defined names", 1, "A defined name whose formula could not be converted is dropped.");
                continue;
            }

            string name = n.IsBuiltIn ? PtgDecoder.BuiltInName(n.BuiltIn) : n.Name;
            if (name.Length == 0 || name.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase) || (n.IsFunction && n.Formula.Length == 0))
            {
                continue;
            }

            if (n.IsMacro || n.IsFunction)
            {
                Lose("loss.macro-names", "macro names", 1, "Macro-sheet names are dropped.");
                continue;
            }

            Worksheet? scope = n.SheetScope >= 0 && n.SheetScope < _wb.Sheets.Count ? _wb.Sheets[n.SheetScope] : null;
            try
            {
                _wb.Names.Add(name, n.Formula.Length == 0 ? "#REF!" : n.Formula, scope, n.Hidden);
            }
            catch (ArgumentException ex)
            {
                _context.Defects.Report(new Defect(DefectKind.Reference, ex.Message.Contains("already exists", StringComparison.Ordinal) ? "biff.name-repeated" : "biff.name-rejected", DefectSeverity.Dropped, "The defined name '" + name + "' is invalid or repeated and is dropped.", DefectLocation.InRecord(_path, 0)));
            }
        }
    }
}
