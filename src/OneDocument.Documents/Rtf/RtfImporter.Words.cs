using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfImporter
{
    private string _tabAlign = "left";
    private string? _tabLeader;

    private static int Flag(RtfNode n) => n.HasParameter && n.Parameter == 0 ? 0 : 1;

    private void Word(RtfNode n, St st, RtfNode parent)
    {
        string name = n.Name;
        int v = n.Parameter;
        if (_curStyle is not null && StyleWord(n))
        {
            return;
        }

        if (_collect is not null)
        {
            return;
        }

        if (CharacterWord(name, n, st) || ParagraphWord(name, n, st) || TableWord(name, n, st) || SectionWord(name, n, st))
        {
            return;
        }

        switch (name)
        {
            case "u":
                AddUnicode(st, v);
                st.Skip = st.Uc;
                break;
            case "uc":
                st.Uc = Math.Max(0, v);
                break;
            case "par":
                if (_curStyle is null)
                {
                    EndParagraph(st);
                }

                break;
            case "cell" or "nestcell":
                EndCell(st);
                break;
            case "row" or "nestrow":
                EndRow(st);
                break;
            case "sect":
                EndSection(st);
                break;
            case "tab":
                AddSpecial(st, "<w:tab/>");
                break;
            case "line":
                AddSpecial(st, "<w:br/>");
                break;
            case "lbr":
                AddSpecial(st, v switch { 1 => "<w:br w:clear=\"left\"/>", 2 => "<w:br w:clear=\"right\"/>", 3 => "<w:br w:clear=\"all\"/>", _ => "<w:br/>" });
                break;
            case "page":
                AddSpecial(st, "<w:br w:type=\"page\"/>");
                break;
            case "column":
                AddSpecial(st, "<w:br w:type=\"column\"/>");
                break;
            case "emdash": AddText(st, "\u2014"); break;
            case "endash": AddText(st, "\u2013"); break;
            case "emspace": AddText(st, "\u2003"); break;
            case "enspace": AddText(st, "\u2002"); break;
            case "qmspace": AddText(st, "\u2005"); break;
            case "bullet": AddText(st, "\u2022"); break;
            case "lquote": AddText(st, "\u2018"); break;
            case "rquote": AddText(st, "\u2019"); break;
            case "ldblquote": AddText(st, "\u201C"); break;
            case "rdblquote": AddText(st, "\u201D"); break;
            case "zwj": AddText(st, "\u200D"); break;
            case "zwnj": AddText(st, "\u200C"); break;
            case "ltrmark": AddText(st, "\u200E"); break;
            case "rtlmark": AddText(st, "\u200F"); break;
            case "chpgn":
                AddField(st, " PAGE ", "1");
                break;
            case "chdate":
                AddField(st, " DATE ", string.Empty);
                break;
            case "chtime":
                AddField(st, " TIME ", string.Empty);
                break;
            case "chftn":
                FootnoteMark(st);
                break;
            case "chatn":
                break; // the annotation mark; the comment itself carries the reference
            case "ansi":
                break;
            case "mac":
                _ansiCp = 10000;
                break;
            case "pc":
                _ansiCp = 437;
                break;
            case "pca":
                _ansiCp = 850;
                break;
            case "ansicpg":
                if (v > 0)
                {
                    _ansiCp = v;
                }

                break;
            case "deff":
                _defFont = v;
                break;
            case "deflang":
                _defLang = v;
                break;
            case "deftab":
                if (v > 0)
                {
                    _defTab = v;
                }

                break;
            case "facingp":
                _facingPages = true;
                break;
            case "widowctrl":
                _widowCtl = true;
                break;
            case "revisions":
                _trackRevisions = true;
                break;
            case "fcharset" or "cpg":
                break;
            default:
                CountUnhandled(name);
                break;
        }
    }

    // Words that only describe layout or editing state this reader does not carry. They are counted once per word for the report
    // when they would change how the document looks.
    private readonly HashSet<string> _unhandled = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LayoutWords = new(StringComparer.Ordinal)
    {
        "absw", "absh", "posx", "posy", "phmrg", "pvmrg", "phpg", "pvpg", "dxfrtext", "dfrmtxtx", "dfrmtxty", "wraparound", "nowrap",
        "abslock", "posxc", "posxr", "posxl", "posyt", "posyb", "posyc", "dropcapli", "dropcapt",
    };

    private void CountUnhandled(string name)
    {
        if (LayoutWords.Contains(name) && _unhandled.Add(name))
        {
            Loss.Report("loss.rtf-frames", "paragraph frames and drop caps", 1, "Positioned paragraphs (frames) and drop caps are read as ordinary paragraphs.");
        }
    }

    private void Symbol(RtfNode n, St st)
    {
        if (_curStyle is not null)
        {
            return;
        }

        string sym = n.Name;
        if (_collect is not null)
        {
            return;
        }

        switch (sym)
        {
            case "\\" or "{" or "}":
                if (st.Skip > 0)
                {
                    st.Skip--;
                    break;
                }

                AddText(st, sym);
                break;
            case "~":
                AddText(st, "\u00A0");
                break;
            case "-":
                AddSpecial(st, "<w:softHyphen/>");
                break;
            case "_":
                AddSpecial(st, "<w:noBreakHyphen/>");
                break;
            case "\r" or "\n":
                EndParagraph(st); // a backslash before a line break is \par
                break;
            default:
                break;
        }
    }

    private void AddUnicode(St st, int n)
    {
        char c = (char)(ushort)(short)n;
        if (st.SymbolFont && c < 0x100)
        {
            if (c >= 0x20)
            {
                AddSymbol(st, c);
            }

            return;
        }

        // Private-use code points of a symbol font (U+F020..F0FF) stay as sym elements.
        if (c is >= '\uF020' and <= '\uF0FF' && st.Font is int f && _fonts.TryGetValue(f, out FontEntry? fe) && fe.Charset == 2)
        {
            AddSymbol(st, c - 0xF000);
            return;
        }

        AddText(st, c.ToString());
    }

    // ---- character formatting (RTF 1.9.1 "Character Formatting Properties") -------------------------------------------------------

    private bool CharacterWord(string name, RtfNode n, St st)
    {
        int v = n.Parameter;
        switch (name)
        {
            case "plain": st.Plain(); st.Font = null; return true;
            case "b": st.B = Flag(n); return true;
            case "i": st.I = Flag(n); return true;
            case "ab": st.AB = Flag(n); return true;
            case "ai": st.AI = Flag(n); return true;
            case "strike": st.Strike = Flag(n); return true;
            case "striked": st.Dstrike = Flag(n); return true;
            case "caps": st.Caps = Flag(n); return true;
            case "scaps": st.SmallCaps = Flag(n); return true;
            case "v": st.Hidden = Flag(n); return true;
            case "outl": st.Outline = Flag(n); return true;
            case "shad": st.Shadow = Flag(n); return true;
            case "embo": st.Emboss = Flag(n); return true;
            case "impr": st.Imprint = Flag(n); return true;
            case "sub": st.Script = Flag(n) == 1 ? 1 : 0; return true;
            case "super": st.Script = Flag(n) == 1 ? 2 : 0; return true;
            case "nosupersub": st.Script = 0; return true;
            case "up": st.Position = n.HasParameter ? v : 6; return true;
            case "dn": st.Position = -(n.HasParameter ? v : 6); return true;
            case "ul": st.Ul = n.HasParameter && v == 0 ? "none" : "single"; return true;
            case "ulnone": st.Ul = "none"; return true;
            case "uld": st.Ul = "dotted"; return true;
            case "uldb": st.Ul = "double"; return true;
            case "ulth": st.Ul = "thick"; return true;
            case "ulw": st.Ul = "words"; return true;
            case "ulwave": st.Ul = "wave"; return true;
            case "uldash": st.Ul = "dash"; return true;
            case "uldashd": st.Ul = "dotDash"; return true;
            case "uldashdd": st.Ul = "dotDotDash"; return true;
            case "ulthd": st.Ul = "dottedHeavy"; return true;
            case "ulthdash": st.Ul = "dashedHeavy"; return true;
            case "ulthdashd": st.Ul = "dashDotHeavy"; return true;
            case "ulthdashdd": st.Ul = "dashDotDotHeavy"; return true;
            case "ulhwave": st.Ul = "wavyHeavy"; return true;
            case "ululdbwave": st.Ul = "wavyDouble"; return true;
            case "ulhair": st.Ul = "single"; return true;
            case "fs": st.Fs = v; return true;
            case "afs": st.AFs = v; return true;
            case "f":
                st.Font = v;
                st.SymbolFont = _fonts.TryGetValue(v, out FontEntry? fe) && fe.Charset == 2;
                return true;
            case "af": st.AF = v; return true;
            case "cf": st.Cf = v; return true;
            case "cb" or "chcbpat": st.ShadeFill = v; return true;
            case "chcfpat": st.ShadeFore = v; return true;
            case "highlight": st.Highlight = v; return true;
            case "expnd": st.Expand = v * 5; return true;
            case "expndtw": st.Expand = v; return true;
            case "kerning": st.Kern = v; return true;
            case "charscalex": st.ScaleX = v; return true;
            case "lang": st.Lang = v; return true;
            case "langfe": st.LangFe = v; return true;
            case "cs": st.CharStyle = v; return true;
            case "rtlch": st.Rtl = true; return true;
            case "ltrch": st.Rtl = false; return true;
            case "loch" or "hich" or "dbch" or "fcs" or "langnp" or "langfenp" or "noproof" or "nosectexpand" or "ulc" or "cchs" or "animtext" or "fittext":
                return true;
            case "revised": st.InsOn = Flag(n) == 1; return true;
            case "revauth": st.Ins = v; return true;
            case "revdttm": st.InsDttm = v; return true;
            case "deleted": st.DelOn = Flag(n) == 1; return true;
            case "revauthdel": st.Del = v; return true;
            case "revdttmdel": st.DelDttm = v; return true;
            default: return false;
        }
    }

    // ---- paragraph formatting (RTF 1.9.1 "Paragraph Formatting Properties") -----------------------------------------------------

    private bool ParagraphWord(string name, RtfNode n, St st)
    {
        ParaFmt p = st.P;
        int v = n.Parameter;
        switch (name)
        {
            case "pard":
                st.P = new ParaFmt();
                _brdrTarget = null;
                return true;
            case "ql": p.Jc = "left"; return true;
            case "qr": p.Jc = "right"; return true;
            case "qc": p.Jc = "center"; return true;
            case "qj": p.Jc = "both"; return true;
            case "qd": p.Jc = "distribute"; return true;
            case "qt": p.Jc = "thaiDistribute"; return true;
            case "li" or "lin": p.Li = v; return true;
            case "ri" or "rin": p.Ri = v; return true;
            case "fi": p.Fi = v; return true;
            case "sb": p.Sb = v; return true;
            case "sa": p.Sa = v; return true;
            case "sbauto": p.SbAuto = Flag(n) == 1; return true;
            case "saauto": p.SaAuto = Flag(n) == 1; return true;
            case "sl": p.Sl = v; return true;
            case "slmult": p.SlMult = v; return true;
            case "contextualspace": p.ContextSpacing = Flag(n) == 1; return true;
            case "keep": p.KeepLines = Flag(n) == 1; return true;
            case "keepn": p.KeepNext = Flag(n) == 1; return true;
            case "pagebb": p.PageBreakBefore = Flag(n) == 1; return true;
            case "widctlpar": p.WidowCtl = true; return true;
            case "nowidctlpar": p.WidowCtl = false; return true;
            case "outlinelevel": p.OutlineLevel = v; return true;
            case "rtlpar": p.Rtl = true; return true;
            case "ltrpar": p.Rtl = false; return true;
            case "s": p.Style = v; return true;
            case "ls": p.Ls = v; return true;
            case "ilvl": p.Ilvl = v; return true;
            case "intbl": p.InTable = true; if (p.Itap == 0) { p.Itap = 1; } return true;
            case "itap": p.Itap = v; p.InTable = v > 0; return true;
            case "cbpat": p.ShadeFill = v; return true;
            case "cfpat": p.ShadeFore = v; return true;
            case "shading": p.ShadePct = v; return true;
            case "tqr": _tabAlign = "right"; return true;
            case "tqc": _tabAlign = "center"; return true;
            case "tqdec": _tabAlign = "decimal"; return true;
            case "tldot": _tabLeader = "dot"; return true;
            case "tlmdot": _tabLeader = "middleDot"; return true;
            case "tlhyph": _tabLeader = "hyphen"; return true;
            case "tlul": _tabLeader = "underscore"; return true;
            case "tlth": _tabLeader = "heavy"; return true;
            case "tleq": _tabLeader = "underscore"; return true;
            case "tx":
                p.Tabs.Add(new TabStop { Pos = v, Align = _tabAlign, Leader = _tabLeader });
                _tabAlign = "left";
                _tabLeader = null;
                return true;
            case "tb":
                p.Tabs.Add(new TabStop { Pos = v, Align = "bar", Leader = null });
                _tabAlign = "left";
                _tabLeader = null;
                return true;
            case "box": _brdrTarget = [p.Top, p.Left, p.Bottom, p.Right]; return true;
            case "brdrt": _brdrTarget = [p.Top]; return true;
            case "brdrl": _brdrTarget = [p.Left]; return true;
            case "brdrb": _brdrTarget = [p.Bottom]; return true;
            case "brdrr": _brdrTarget = [p.Right]; return true;
            case "brdrbar": _brdrTarget = [p.Bar]; return true;
            case "brdrbtw": _brdrTarget = [p.Between]; return true;
            case "brdrw" or "brdrcf" or "brsp": return BorderWord(name, v);
            default:
                if (name.StartsWith("brdr", StringComparison.Ordinal))
                {
                    return BorderWord(name, v);
                }

                return false;
        }
    }

    // RTF 1.9.1 "Paragraph Borders": \brdrs, \brdrdb ... name the style of the border chosen by the last \brdrt / \clbrdrt / \trbrdrt ...
    private bool BorderWord(string name, int v)
    {
        if (_brdrTarget is null)
        {
            return true;
        }

        foreach (Brdr b in _brdrTarget)
        {
            switch (name)
            {
                case "brdrw": b.Width = v; break;
                case "brdrcf": b.Color = v; break;
                case "brsp": b.Space = v; break;
                default:
                    b.Style = name[4..];
                    break;
            }
        }

        return true;
    }

    // ---- sections ------------------------------------------------------------------------------------------------------------------

    private bool SectionWord(string name, RtfNode n, St st)
    {
        int v = n.Parameter;
        switch (name)
        {
            case "sectd":
                {
                    List<(string Kind, string Type, string RelId)> refs = _sec.Refs;
                    _sec = _docSec.Clone();
                    _sec.Refs = refs;
                    return true;
                }

            case "paperw": _docSec.PaperW = _sec.PaperW = v; return true;
            case "paperh": _docSec.PaperH = _sec.PaperH = v; return true;
            case "margl": _docSec.MarL = _sec.MarL = v; return true;
            case "margr": _docSec.MarR = _sec.MarR = v; return true;
            case "margt": _docSec.MarT = _sec.MarT = v; return true;
            case "margb": _docSec.MarB = _sec.MarB = v; return true;
            case "gutter": _docSec.Gutter = _sec.Gutter = v; return true;
            case "landscape": _docSec.Landscape = _sec.Landscape = true; return true;
            case "margmirror": _docSec.MirrorMargins = _sec.MirrorMargins = true; return true;
            case "pgwsxn": _sec.PaperW = v; return true;
            case "pghsxn": _sec.PaperH = v; return true;
            case "marglsxn": _sec.MarL = v; return true;
            case "margrsxn": _sec.MarR = v; return true;
            case "margtsxn": _sec.MarT = v; return true;
            case "margbsxn": _sec.MarB = v; return true;
            case "guttersxn": _sec.Gutter = v; return true;
            case "headery": _sec.HeaderY = v; return true;
            case "footery": _sec.FooterY = v; return true;
            case "lndscpsxn": _sec.Landscape = true; return true;
            case "titlepg": _sec.TitlePg = true; return true;
            case "cols": _sec.Cols = Math.Max(1, v); return true;
            case "colsx": _sec.ColSpace = v; return true;
            case "linebetween": _sec.LineBetween = true; return true;
            case "sbknone": _sec.BreakType = "continuous"; return true;
            case "sbkcol": _sec.BreakType = "nextColumn"; return true;
            case "sbkpage": _sec.BreakType = "nextPage"; return true;
            case "sbkeven": _sec.BreakType = "evenPage"; return true;
            case "sbkodd": _sec.BreakType = "oddPage"; return true;
            case "pgndec": _sec.PgnFmt = "decimal"; return true;
            case "pgnucrm": _sec.PgnFmt = "upperRoman"; return true;
            case "pgnlcrm": _sec.PgnFmt = "lowerRoman"; return true;
            case "pgnucltr": _sec.PgnFmt = "upperLetter"; return true;
            case "pgnlcltr": _sec.PgnFmt = "lowerLetter"; return true;
            case "pgnstart": _sec.PgnStart = v; return true;
            case "pgnrestart": _sec.PgnStart = _sec.PgnStart < 0 ? 1 : _sec.PgnStart; return true;
            case "rtlsect": _sec.Rtl = true; return true;
            case "ltrsect": _sec.Rtl = false; return true;
            default: return false;
        }
    }

    // ---- tables (RTF 1.9.1 "Table Definitions") -------------------------------------------------------------------------------------

    private RowDef CurrentRowDef(St st)
    {
        int depth = _nest > 0 ? Math.Max(2, st.P.Itap) : 1;
        if (!_rowDefs.TryGetValue(depth, out RowDef? def))
        {
            _rowDefs[depth] = def = new RowDef();
        }

        return def;
    }

    private bool TableWord(string name, RtfNode n, St st)
    {
        int v = n.Parameter;
        if (!name.StartsWith("tr", StringComparison.Ordinal) && !name.StartsWith("cl", StringComparison.Ordinal) && name != "cellx")
        {
            return false;
        }

        if (name == "trowd")
        {
            int depth = _nest > 0 ? Math.Max(2, st.P.Itap) : 1;
            _rowDefs[depth] = new RowDef();
            _pendingCell = new CellDef();
            _brdrTarget = null;
            return true;
        }

        RowDef row = CurrentRowDef(st);
        CellDef cell = _pendingCell;
        switch (name)
        {
            case "cellx":
                cell.RightX = v;
                row.Cells.Add(cell);
                _pendingCell = new CellDef();
                _brdrTarget = null;
                return true;
            case "trgaph": row.Gaph = v; return true;
            case "trleft": row.Left = v; return true;
            case "trrh": row.Height = v; return true;
            case "trhdr": row.Header = true; return true;
            case "trkeep" or "trcantsplit": row.CantSplit = true; return true;
            case "trautofit": row.AutoFit = Flag(n) == 1; return true;
            case "trql": row.Jc = 0; return true;
            case "trqc": row.Jc = 1; return true;
            case "trqr": row.Jc = 2; return true;
            case "trpaddl": row.PadL = v; return true;
            case "trpaddr": row.PadR = v; return true;
            case "trpaddt": row.PadT = v; return true;
            case "trpaddb": row.PadB = v; return true;
            case "trcbpat": row.ShadeFill = v; return true;
            case "trbrdrt": _brdrTarget = [row.Top]; return true;
            case "trbrdrl": _brdrTarget = [row.Left_]; return true;
            case "trbrdrb": _brdrTarget = [row.Bottom]; return true;
            case "trbrdrr": _brdrTarget = [row.Right]; return true;
            case "trbrdrh": _brdrTarget = [row.Horiz]; return true;
            case "trbrdrv": _brdrTarget = [row.Vert]; return true;
            case "clvmgf": cell.VMerge = 1; return true;
            case "clvmrg": cell.VMerge = 2; return true;
            case "clmgf": cell.HMerge = 1; return true;
            case "clmrg": cell.HMerge = 2; return true;
            case "clvertalt": cell.VAlign = "top"; return true;
            case "clvertalc": cell.VAlign = "center"; return true;
            case "clvertalb": cell.VAlign = "bottom"; return true;
            case "clcbpat": cell.ShadeFill = v; return true;
            case "clcfpat": cell.ShadeFore = v; return true;
            case "clshdng": cell.ShadePct = v; return true;
            case "clNoWrap" or "clnowrap": cell.NoWrap = true; return true;
            case "clbrdrt": _brdrTarget = [cell.Top]; return true;
            case "clbrdrl": _brdrTarget = [cell.Left]; return true;
            case "clbrdrb": _brdrTarget = [cell.Bottom]; return true;
            case "clbrdrr": _brdrTarget = [cell.Right]; return true;
            case "cltxlrtb": cell.Direction = "lrTb"; return true;
            case "cltxtbrl": cell.Direction = "tbRl"; return true;
            case "cltxbtlr": cell.Direction = "btLr"; return true;
            default: return true; // other row and cell words (widths in other units, bidi, ...) are layout hints Word recomputes
        }
    }
}
