using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Documents.Rtf;

// A border as RTF describes it (RTF 1.9.1 "Paragraph Borders", "Table Definitions"): a style word, a width in twips, a colour index and a space.
internal sealed class Brdr
{
    public string? Style; // the brdr* style word without the prefix: s, th, db, dot, dash ... ; null = not set
    public int Width = 10;
    public int Color;
    public int Space;

    public bool IsSet => Style is not null;

    public Brdr Clone() => (Brdr)MemberwiseClone();

    // ECMA-376 Part 1 §17.18.2 (ST_Border) values for RTF's border styles.
    public string? Val => Style switch
    {
        null => null,
        "s" or "hair" or "sh" => "single",
        "th" => "thick",
        "db" => "double",
        "dot" => "dotted",
        "dash" or "dashsm" => "dashed",
        "dashd" => "dotDash",
        "dashdd" => "dotDotDash",
        "triple" => "triple",
        "wavy" => "wave",
        "wavydb" => "doubleWave",
        "inset" => "inset",
        "outset" => "outset",
        "engrave" => "engrave",
        "emboss" => "emboss",
        "none" => "nil",
        "thtnmg" => "thinThickMediumGap",
        "thtnlg" => "thinThickLargeGap",
        "thtnsg" => "thinThickSmallGap",
        "tnthmg" => "thickThinMediumGap",
        "tnthlg" => "thickThinLargeGap",
        "tnthsg" => "thickThinSmallGap",
        "tnthtnmg" => "thinThickThinMediumGap",
        "tnthtnlg" => "thinThickThinLargeGap",
        "tnthtnsg" => "thinThickThinSmallGap",
        _ => "single",
    };
}

internal struct TabStop
{
    public int Pos;
    public string Align; // left, center, right, decimal, bar
    public string? Leader;
}

// Paragraph formatting in effect (RTF 1.9.1 "Paragraph Formatting Properties"). Null/-1 means "not set", so a style or the defaults show through.
internal sealed class ParaFmt
{
    public string? Jc;
    public int? Li, Ri, Fi, Sb, Sa, Sl;
    public int SlMult;
    public bool KeepLines, KeepNext, PageBreakBefore, NoWidow, ContextSpacing, Rtl;
    public bool? WidowCtl;
    public bool SbAuto, SaAuto;
    public int Style = -1;
    public int OutlineLevel = -1;
    public int Ls = -1, Ilvl;
    public bool InTable;
    public int Itap;
    public List<TabStop> Tabs = [];
    public Brdr Top = new(), Left = new(), Bottom = new(), Right = new(), Bar = new(), Between = new();
    public int ShadePct = -1, ShadeFill = -1, ShadeFore = -1;
    public bool HasFrame;

    public ParaFmt Clone()
    {
        var c = (ParaFmt)MemberwiseClone();
        c.Tabs = [.. Tabs];
        c.Top = Top.Clone();
        c.Left = Left.Clone();
        c.Bottom = Bottom.Clone();
        c.Right = Right.Clone();
        c.Bar = Bar.Clone();
        c.Between = Between.Clone();
        return c;
    }
}

// The state RTF scopes by group (RTF 1.9.1 "Document Syntax": a group saves and restores the formatting state): character, paragraph and
// the \uc skip count.
internal sealed class St
{
    public int? B, I, Strike, Dstrike, Caps, SmallCaps, Hidden, Outline, Shadow, Emboss, Imprint;
    public int? Script; // 1 subscript, 2 superscript, 0 none
    public string? Ul;
    public int? Fs, Font, Cf, Highlight, Expand, ScaleX, Position, Lang, LangFe, CharStyle;
    public int? Kern, AF, AFs, AB, AI;
    public int? ShadeFill, ShadeFore;
    public bool Rtl;
    public int Uc = 1;
    public int Skip;
    public int Ins = -1, InsDttm, Del = -1, DelDttm;
    public bool InsOn, DelOn;
    public ParaFmt P = new();
    public bool SymbolFont;

    public St Clone()
    {
        var c = (St)MemberwiseClone();
        c.P = P.Clone();
        c.Skip = 0;
        return c;
    }

    // RTF 1.9.1 \plain: resets the character properties (not the paragraph's) to the defaults.
    public void Plain()
    {
        B = I = Strike = Dstrike = Caps = SmallCaps = Hidden = Outline = Shadow = Emboss = Imprint = Script = null;
        Ul = null;
        Fs = Cf = Highlight = Expand = ScaleX = Position = Lang = LangFe = CharStyle = Kern = ShadeFill = ShadeFore = null;
        Font = null;
        Rtl = false;
        InsOn = DelOn = false;
        SymbolFont = false;
    }
}

internal sealed class FontEntry
{
    public int Index;
    public string Name = string.Empty;
    public string? AltName;
    public string Family = "auto"; // roman swiss modern script decor
    public int Charset;
    public int CodePage; // 0: the document's
    public int Pitch;
}

internal sealed class CellDef
{
    public int RightX;
    public int VMerge; // 0 none, 1 first (\clvmgf), 2 continue (\clvmrg)
    public int HMerge; // 0 none, 1 first (\clmgf), 2 continue (\clmrg)
    public string? VAlign;
    public int ShadeFill = -1, ShadePct = -1, ShadeFore = -1;
    public Brdr Top = new(), Left = new(), Bottom = new(), Right = new();
    public bool NoWrap;
    public int PadL = -1, PadR = -1, PadT = -1, PadB = -1;
    public string? Direction;
}

internal sealed class RowDef
{
    public List<CellDef> Cells = [];
    public int Gaph = 108, Left, Height, Jc;
    public bool Header, CantSplit, AutoFit;
    public Brdr Top = new(), Left_ = new(), Bottom = new(), Right = new(), Horiz = new(), Vert = new();
    public int PadL = -1, PadR = -1, PadT = -1, PadB = -1;
    public int ShadeFill = -1;
}

internal sealed class ListLevelDef
{
    public int Nfc;
    public int Jc;
    public int Follow;
    public int Start = 1;
    public int NoRestart;
    public List<int> TextItems = [];
    public int? FontIndex;
    public int? Fi, Li;
    public int? B, I, Fs, Cf;
    public string? Ul;
    public int? Tab;
    public int LegalNumbering;
}

internal sealed class ListDef
{
    public int Id;
    public int TemplateId;
    public bool Simple;
    public string Name = string.Empty;
    public List<ListLevelDef> Levels = [];
}

internal sealed class ListOverrideDef
{
    public int ListId;
    public int Ls;
    public Dictionary<int, int> StartOverrides = [];
}

internal sealed class StyleDef
{
    public int Number;
    public string Type = "paragraph"; // paragraph, character, table
    public string Name = string.Empty;
    public string Id = string.Empty;
    public int BasedOn = -1;
    public int Next = -1;
    public int Link = -1;
    public bool Additive;
    public bool Hidden;
    public bool QFormat;
    public bool Unhide;
    public int Priority = -1;
    public St State = new();
}

// One finished block of a story: either a paragraph (kept in parts so a section's properties can still be added) or ready XML.
internal sealed class Block
{
    public string? ParaProps; // children of w:pPr, in schema order, without the sectPr
    public string? Content;   // runs and other paragraph content, or the whole element for tables
    public bool IsTable;
    public string? SectPr;

    public string ToXml()
    {
        if (IsTable)
        {
            return Content ?? string.Empty;
        }

        var sb = new StringBuilder("<w:p>");
        if (!string.IsNullOrEmpty(ParaProps) || SectPr is not null)
        {
            sb.Append("<w:pPr>").Append(ParaProps).Append(SectPr).Append("</w:pPr>");
        }

        return sb.Append(Content).Append("</w:p>").ToString();
    }
}

internal sealed class CellFrame
{
    public List<Block> Blocks = [];
}

internal sealed class RowFrame
{
    public RowDef? Def;
    public List<CellFrame> Cells = [];
}

internal sealed class TableFrame
{
    public int Depth;
    public List<RowFrame> Rows = [];
    public RowFrame Row = new();
    public CellFrame Cell = new();
}

// A story under construction: the body, a header, a footnote or a comment.
internal sealed class Story
{
    public List<Block> Blocks = [];
    public List<TableFrame> Tables = [];
    public StringBuilder Runs = new();
    public StringBuilder PendingText = new();
    public StringBuilder PendingXml = new();
    public string? PendingProps;
    public string PendingRev = string.Empty;
    public bool PendingDeleted;
    public ((int Id, int Dttm) Ins, (int Id, int Dttm) Del) PendingRevState = ((-1, 0), (-1, 0));
    public bool HasContent;   // the open paragraph has runs
    public RelSet Rels = new();
    public List<Block> Target => Tables.Count > 0 ? Tables[^1].Cell.Blocks : Blocks;
}

internal sealed class SectionDef
{
    public int PaperW = 12240, PaperH = 15840, MarL = 1800, MarR = 1800, MarT = 1440, MarB = 1440, Gutter, HeaderY = 720, FooterY = 720;
    public bool Landscape, TitlePg, MirrorMargins;
    public int Cols = 1, ColSpace = 720;
    public bool LineBetween;
    public string BreakType = "nextPage";
    public string? PgnFmt;
    public int PgnStart = -1;
    public int VertAlign;
    public List<(string Kind, string Type, string RelId)> Refs = [];
    public bool Rtl;

    public SectionDef Clone()
    {
        var c = (SectionDef)MemberwiseClone();
        c.Refs = [.. Refs];
        return c;
    }
}

internal static class X
{
    public static string Esc(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string? rep = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => null,
            };
            bool bad = c < ' ' && c != '\t' && c != '\n' && c != '\r' || c is '\uFFFE' or '\uFFFF';
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb?.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                bad = true;
            }

            if (rep is null && !bad)
            {
                sb?.Append(c);
                continue;
            }

            sb ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
            if (rep is not null)
            {
                sb.Append(rep);
            }
        }

        return sb?.ToString() ?? text;
    }

    public static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Hex6(int rgb) => (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    // RTF 1.9.1 \revdttm and friends: a packed date, minute 0-5, hour 6-10, day 11-15, month 16-19, year since 1900 20-28, weekday 29-31.
    public static string? Dttm(int dttm)
    {
        if (dttm <= 0)
        {
            return null;
        }

        int min = dttm & 63, hr = (dttm >> 6) & 31, dy = (dttm >> 11) & 31, mo = (dttm >> 16) & 15, yr = ((dttm >> 20) & 511) + 1900;
        if (mo is < 1 or > 12 || dy is < 1 or > 31 || hr > 23 || min > 59)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{yr:D4}-{mo:D2}-{dy:D2}T{hr:D2}:{min:D2}:00Z");
    }
}

// The relationships of one part, in the order they were added (ids rId1, rId2 ...).
internal sealed class RelSet
{
    public List<(string Id, string Type, string Target, bool External)> Items = [];

    public string Add(string type, string target, bool external = false)
    {
        foreach ((string id, string t, string tg, bool ext) in Items)
        {
            if (t == type && tg == target && ext == external)
            {
                return id;
            }
        }

        string newId = "rId" + X.N(Items.Count + 1);
        Items.Add((newId, type, target, external));
        return newId;
    }
}
