using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfExporter
{
    // ---- styles (RTF 1.9.1 "Style Sheet") ------------------------------------------------------------------------------------------

    private void ReadStyles()
    {
        if (_stylesRoot.IsNull)
        {
            return;
        }

        int next = 1;
        foreach (MarkupNode s in _stylesRoot.ChildElements)
        {
            if (!IsW(s, "style"))
            {
                continue;
            }

            string type = A(s, "type") ?? "paragraph";
            string id = A(s, "styleId") ?? string.Empty;
            if (id.Length == 0 || type == "numbering")
            {
                continue;
            }

            MarkupNode nm = Child(s, "name");
            bool isDefault = A(s, "default") is "1" or "true" or "on";
            var rec = new StyleRec
            {
                Node = s,
                Type = type,
                Id = id,
                Name = nm.IsNull ? id : A(nm, "val") ?? id,
                IsDefault = isDefault,
                BasedOn = Child(s, "basedOn") is { IsNull: false } b ? A(b, "val") : null,
                Next = Child(s, "next") is { IsNull: false } nx ? A(nx, "val") : null,
            };
            if (type == "table")
            {
                _loss.Report("loss.rtf-table-style", "table styles", 1, "Table styles are not written; the formatting a table style gives is not in the RTF.");
                continue;
            }

            rec.Number = type == "paragraph" && isDefault ? 0 : next++;
            _styleNumbers[type + ":" + id] = rec.Number;
            _styleList.Add(rec);
        }
    }

    private int StyleNumber(string type, string? id) =>
        id is not null && _styleNumbers.TryGetValue(type + ":" + id, out int n) ? n : -1;

    private void WriteStyleSheet()
    {
        _w.StartGroup();
        _w.Word("stylesheet");
        bool wroteNormal = false;
        foreach (StyleRec s in _styleList)
        {
            _ct.ThrowIfCancellationRequested();
            bool isNormal = s.Type == "paragraph" && s.Number == 0;
            wroteNormal |= isNormal;
            _w.StartGroup();
            if (s.Type == "character")
            {
                _w.Symbol('*');
                _w.Word("cs", s.Number);
                _w.Word("additive");
            }
            else
            {
                _w.Word("s", s.Number);
            }

            int basedOn = StyleNumber(s.Type, s.BasedOn);
            if (basedOn >= 0 && !isNormal)
            {
                _w.Word("sbasedon", basedOn);
            }
            else if (!isNormal && s.Type == "paragraph")
            {
                _w.Word("sbasedon", 222);
            }

            if (s.Type == "paragraph")
            {
                int nx = StyleNumber("paragraph", s.Next);
                _w.Word("snext", nx >= 0 ? nx : s.Number);
            }

            MarkupNode pPr = Child(s.Node, "pPr");
            MarkupNode rPr = Child(s.Node, "rPr");
            if (isNormal || (basedOn < 0 && s.Type == "paragraph"))
            {
                // The document defaults live in the Normal style in RTF, which has no separate defaults.
                _w.Word("f", FontIndex(_defaultFont));
                _w.Word("fs", _defaultSize);
            }

            if (!pPr.IsNull && s.Type == "paragraph")
            {
                ParagraphWords(pPr, inStyle: true);
            }

            if (!rPr.IsNull)
            {
                RunWords(rPr);
            }

            if (Child(s.Node, "qFormat") is { IsNull: false })
            {
                _w.Word("sqformat");
            }

            if (Child(s.Node, "uiPriority") is { IsNull: false } up)
            {
                _w.Word("spriority", Int(A(up, "val")));
            }

            if (Child(s.Node, "semiHidden") is { IsNull: false })
            {
                _w.Word("ssemihidden");
            }

            _w.Text(s.Name);
            _w.Text(";");
            _w.EndGroup();
        }

        if (!wroteNormal)
        {
            _w.StartGroup();
            _w.Word("s", 0);
            _w.Word("snext", 0);
            _w.Word("f", FontIndex(_defaultFont));
            _w.Word("fs", _defaultSize);
            _w.Text("Normal;");
            _w.EndGroup();
        }

        _w.EndGroup();
    }

    // ---- lists (RTF 1.9.1 "List Table") ----------------------------------------------------------------------------------------------

    private sealed class AbstractList
    {
        public int Id;
        public MarkupNode Node;
        public Dictionary<int, MarkupNode> Levels = [];
    }

    private readonly Dictionary<int, AbstractList> _abstractLists = [];
    private readonly List<(int NumId, int AbstractId, MarkupNode Node)> _nums = [];

    private void ReadNumbering()
    {
        foreach (MarkupNode n in _numberingRoot.ChildElements)
        {
            if (IsW(n, "abstractNum"))
            {
                int id = Int(A(n, "abstractNumId"), -1);
                var al = new AbstractList { Id = id, Node = n };
                foreach (MarkupNode l in n.ChildElements)
                {
                    if (IsW(l, "lvl"))
                    {
                        al.Levels[Int(A(l, "ilvl"))] = l;
                    }
                }

                _abstractLists[id] = al;
            }
            else if (IsW(n, "num"))
            {
                MarkupNode a = Child(n, "abstractNumId");
                _nums.Add((Int(A(n, "numId"), -1), a.IsNull ? -1 : Int(A(a, "val"), -1), n));
            }
        }
    }

    private static int ListIdOf(int abstractId) => 1000 + abstractId;

    private void WriteLists()
    {
        if (_abstractLists.Count == 0)
        {
            return;
        }

        _w.StartGroup();
        _w.Word("listtable");
        foreach (AbstractList al in _abstractLists.Values.OrderBy(a => a.Id))
        {
            _w.StartGroup();
            _w.Word("list");
            _w.Word("listtemplateid", ListIdOf(al.Id));
            string? mt = Child(al.Node, "multiLevelType") is { IsNull: false } m ? A(m, "val") : null;
            if (mt == "singleLevel")
            {
                _w.Word("listsimple", 1);
            }

            for (int lvl = 0; lvl < 9; lvl++)
            {
                if (al.Levels.TryGetValue(lvl, out MarkupNode l))
                {
                    WriteLevel(l, lvl);
                }
            }

            _w.Word("listid", ListIdOf(al.Id));
            _w.EndGroup();
        }

        _w.EndGroup();
        _w.StartGroup();
        _w.Word("listoverridetable");
        foreach ((int numId, int abs, MarkupNode node) in _nums)
        {
            if (!_abstractLists.ContainsKey(abs))
            {
                continue;
            }

            _w.StartGroup();
            _w.Word("listoverride");
            _w.Word("listid", ListIdOf(abs));
            int overrides = 0;
            foreach (MarkupNode lo in node.ChildElements)
            {
                if (IsW(lo, "lvlOverride") && !Child(lo, "startOverride").IsNull)
                {
                    overrides++;
                }
            }

            _w.Word("listoverridecount", overrides);
            foreach (MarkupNode lo in node.ChildElements)
            {
                if (IsW(lo, "lvlOverride") && Child(lo, "startOverride") is { IsNull: false } so)
                {
                    _w.StartGroup();
                    _w.Word("lfolevel");
                    _w.Word("listoverridestartat");
                    _w.Word("levelstartat", Int(A(so, "val"), 1));
                    _w.EndGroup();
                }
            }

            _w.Word("ls", numId);
            _w.EndGroup();
        }

        _w.EndGroup();
    }

    private void WriteLevel(MarkupNode l, int ilvl)
    {
        _w.StartGroup();
        _w.Word("listlevel");
        MarkupNode fmtNode = Child(l, "numFmt");
        string fmt = fmtNode.IsNull ? "decimal" : A(fmtNode, "val") ?? "decimal";
        _w.Word("levelnfc", NfcOf(fmt));
        MarkupNode jc = Child(l, "lvlJc");
        string j = jc.IsNull ? "left" : A(jc, "val") ?? "left";
        _w.Word("leveljc", j is "center" ? 1 : j is "right" or "end" ? 2 : 0);
        MarkupNode suff = Child(l, "suff");
        string sf = suff.IsNull ? "tab" : A(suff, "val") ?? "tab";
        _w.Word("levelfollow", sf == "space" ? 1 : sf == "nothing" ? 2 : 0);
        MarkupNode start = Child(l, "start");
        _w.Word("levelstartat", start.IsNull ? 1 : Int(A(start, "val"), 1));
        if (!Child(l, "isLgl").IsNull)
        {
            _w.Word("levellegal", 1);
        }

        MarkupNode rpr = Child(l, "rPr");
        string? fontName = rpr.IsNull ? null : FontNameOf(Child(rpr, "rFonts"));
        bool symbol = fmt == "bullet" && fontName is not null && IsSymbolFont(fontName);
        MarkupNode lt = Child(l, "lvlText");
        string text = lt.IsNull ? string.Empty : A(lt, "val") ?? string.Empty;
        _w.StartGroup();
        _w.Word("leveltext");
        var numbers = new List<int>();
        var items = new List<(bool Placeholder, int Value)>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                items.Add((true, text[i + 1] - '1'));
                numbers.Add(items.Count);
                i++;
            }
            else
            {
                items.Add((false, text[i]));
            }
        }

        _w.Hex((byte)items.Count);
        foreach ((bool placeholder, int value) in items)
        {
            if (placeholder)
            {
                _w.Hex((byte)value);
            }
            else if (symbol && value >= 0xF020 && value <= 0xF0FF)
            {
                _w.Hex((byte)(value & 0xFF));
            }
            else if (value < 0x80)
            {
                if (value is '\\' or '{' or '}')
                {
                    _w.Hex((byte)value);
                }
                else
                {
                    _w.Text(((char)value).ToString());
                }
            }
            else
            {
                _w.Text(((char)value).ToString());
            }
        }

        _w.Text(";");
        _w.EndGroup();
        _w.StartGroup();
        _w.Word("levelnumbers");
        foreach (int n in numbers)
        {
            _w.Hex((byte)n);
        }

        _w.Text(";");
        _w.EndGroup();
        if (fontName is not null)
        {
            _w.Word("f", FontIndex(fontName, symbol));
        }

        if (!rpr.IsNull)
        {
            RunWords(rpr, skipFont: true);
        }

        MarkupNode ppr = Child(l, "pPr");
        MarkupNode ind = ppr.IsNull ? default : Child(ppr, "ind");
        if (!ind.IsNull)
        {
            WriteIndent(ind);
        }

        MarkupNode tabs = ppr.IsNull ? default : Child(ppr, "tabs");
        if (!tabs.IsNull)
        {
            foreach (MarkupNode t in tabs.ChildElements)
            {
                if (IsW(t, "tab") && A(t, "val") == "num")
                {
                    _w.Word("jclisttab");
                    _w.Word("tx", Int(A(t, "pos")));
                    break;
                }
            }
        }

        _w.Word("levelspace", 0);
        _w.Word("levelindent", 0);
        _ = ilvl;
        _w.EndGroup();
    }

    private static bool IsSymbolFont(string name) =>
        name.ToUpperInvariant() is "SYMBOL" or "WINGDINGS" or "WINGDINGS 2" or "WINGDINGS 3" or "WEBDINGS" or "MT EXTRA";

    // RTF 1.9.1 \levelnfc values for ST_NumberFormat (the inverse of what the reader maps).
    private static int NfcOf(string fmt) => fmt switch
    {
        "decimal" => 0, "upperRoman" => 1, "lowerRoman" => 2, "upperLetter" => 3, "lowerLetter" => 4, "ordinal" => 5, "cardinalText" => 6,
        "ordinalText" => 7, "ideographDigital" => 10, "japaneseCounting" => 11, "aiueo" => 12, "iroha" => 13, "decimalFullWidth" => 14,
        "decimalHalfWidth" => 15, "decimalEnclosedCircle" => 18, "decimalZero" => 22, "bullet" => 23, "ganada" => 24, "chosung" => 25,
        "chineseCounting" => 37, "chineseLegalSimplified" => 38, "chineseCountingThousand" => 39, "koreanDigital" => 40, "koreanCounting" => 41,
        "hebrew1" => 44, "arabicAlpha" => 45, "hebrew2" => 46, "arabicAbjad" => 47, "thaiLetters" => 52, "thaiNumbers" => 53, "thaiCounting" => 54,
        "numberInDash" => 56, "russianLower" => 57, "russianUpper" => 58, "none" => 255,
        _ => 0,
    };

    private void WriteIndent(MarkupNode ind)
    {
        string? left = A(ind, "left") ?? A(ind, "start");
        string? right = A(ind, "right") ?? A(ind, "end");
        string? first = A(ind, "firstLine");
        string? hanging = A(ind, "hanging");
        if (left is not null)
        {
            _w.Word("li", Int(left));
        }

        if (right is not null)
        {
            _w.Word("ri", Int(right));
        }

        if (hanging is not null)
        {
            _w.Word("fi", -Int(hanging));
        }
        else if (first is not null)
        {
            _w.Word("fi", Int(first));
        }
    }

    // ---- run properties -> character words -----------------------------------------------------------------------------------------

    private void ToggleWord(string word, MarkupNode e)
    {
        if (On(e))
        {
            _w.Word(word);
        }
        else
        {
            _w.Word(word, 0);
        }
    }

    // ECMA-376 Part 1 §17.3.2 -> RTF 1.9.1 "Character Formatting Properties".
    private void RunWords(MarkupNode rPr, bool skipFont = false)
    {
        foreach (MarkupNode e in rPr.ChildElements)
        {
            if (e.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            switch (e.LocalNameString)
            {
                case "rStyle":
                    {
                        int n = StyleNumber("character", A(e, "val"));
                        if (n >= 0)
                        {
                            _w.Word("cs", n);
                        }

                        break;
                    }

                case "rFonts":
                    if (!skipFont && FontNameOf(e) is { } fn)
                    {
                        _w.Word("f", FontIndex(fn));
                    }

                    break;
                case "b": ToggleWord("b", e); break;
                case "i": ToggleWord("i", e); break;
                case "caps": ToggleWord("caps", e); break;
                case "smallCaps": ToggleWord("scaps", e); break;
                case "strike": ToggleWord("strike", e); break;
                case "dstrike": ToggleWord("striked", e); break;
                case "outline": ToggleWord("outl", e); break;
                case "shadow": ToggleWord("shad", e); break;
                case "emboss": ToggleWord("embo", e); break;
                case "imprint": ToggleWord("impr", e); break;
                case "vanish": ToggleWord("v", e); break;
                case "rtl": _w.Word(On(e) ? "rtlch" : "ltrch"); break;
                case "color":
                    {
                        string? v = A(e, "val");
                        _w.Word("cf", v is null or "auto" ? 0 : ColorIndex(v));
                        break;
                    }

                case "spacing":
                    _w.Word("expndtw", Int(A(e, "val")));
                    break;
                case "w":
                    _w.Word("charscalex", Int(A(e, "val"), 100));
                    break;
                case "kern":
                    _w.Word("kerning", Int(A(e, "val")));
                    break;
                case "position":
                    {
                        int p = Int(A(e, "val"));
                        _w.Word(p >= 0 ? "up" : "dn", Math.Abs(p));
                        break;
                    }

                case "sz":
                    _w.Word("fs", Int(A(e, "val"), _defaultSize));
                    break;
                case "highlight":
                    {
                        string? hex = HighlightHex(A(e, "val"));
                        if (hex is not null)
                        {
                            _w.Word("highlight", ColorIndex(hex));
                        }

                        break;
                    }

                case "u":
                    {
                        string? v = A(e, "val") ?? "single";
                        _w.Word(UnderlineWord(v));
                        break;
                    }

                case "shd":
                    {
                        string? fill = A(e, "fill");
                        if (fill is not null && fill != "auto")
                        {
                            _w.Word("chcbpat", ColorIndex(fill));
                        }

                        break;
                    }

                case "vertAlign":
                    {
                        string? v = A(e, "val");
                        _w.Word(v == "superscript" ? "super" : v == "subscript" ? "sub" : "nosupersub");
                        break;
                    }

                case "lang":
                    {
                        if (A(e, "val") is { } lv)
                        {
                            _w.Word("lang", LcidOf(lv, 1033));
                        }

                        if (A(e, "eastAsia") is { } le)
                        {
                            _w.Word("langfe", LcidOf(le, 1041));
                        }

                        break;
                    }

                case "bCs" or "iCs" or "szCs" or "noProof" or "snapToGrid" or "webHidden" or "specVanish" or "eastAsianLayout" or "cs":
                    break;
                case "effect" or "em" or "fitText" or "bdr":
                    _loss.Report("loss.rtf-run-effect", "text effects", 1, "Animated text effects, emphasis marks, fit-text and character borders are not written.");
                    break;
            }
        }
    }

    private static string UnderlineWord(string v) => v switch
    {
        "none" => "ulnone",
        "double" => "uldb",
        "thick" => "ulth",
        "dotted" => "uld",
        "dash" => "uldash",
        "dotDash" => "uldashd",
        "dotDotDash" => "uldashdd",
        "words" => "ulw",
        "wave" => "ulwave",
        "dottedHeavy" => "ulthd",
        "dashedHeavy" => "ulthdash",
        "dashDotHeavy" => "ulthdashd",
        "dashDotDotHeavy" => "ulthdashdd",
        "wavyHeavy" => "ulhwave",
        "wavyDouble" => "ululdbwave",
        _ => "ul",
    };

    private static string? HighlightHex(string? name) => name switch
    {
        "black" => "000000", "blue" => "0000FF", "cyan" => "00FFFF", "green" => "00FF00", "magenta" => "FF00FF", "red" => "FF0000",
        "yellow" => "FFFF00", "white" => "FFFFFF", "darkBlue" => "000080", "darkCyan" => "008080", "darkGreen" => "008000",
        "darkMagenta" => "800080", "darkRed" => "800000", "darkYellow" => "808000", "darkGray" => "808080", "lightGray" => "C0C0C0",
        _ => null,
    };

    // ---- paragraph properties -> paragraph words -------------------------------------------------------------------------------------

    private void BorderWords(string side, MarkupNode e)
    {
        string val = A(e, "val") ?? "single";
        _w.Word(side);
        _w.Word(val switch
        {
            "nil" or "none" => "brdrnone",
            "double" => "brdrdb",
            "dotted" => "brdrdot",
            "dashed" or "dashSmallGap" => "brdrdash",
            "dotDash" => "brdrdashd",
            "dotDotDash" => "brdrdashdd",
            "thick" => "brdrth",
            "triple" => "brdrtriple",
            "wave" => "brdrwavy",
            "doubleWave" => "brdrwavydb",
            "inset" => "brdrinset",
            "outset" => "brdroutset",
            "engrave" => "brdrengrave",
            "emboss" => "brdremboss",
            "thinThickSmallGap" => "brdrthtnsg",
            "thinThickMediumGap" => "brdrthtnmg",
            "thinThickLargeGap" => "brdrthtnlg",
            "thickThinSmallGap" => "brdrtnthsg",
            "thickThinMediumGap" => "brdrtnthmg",
            "thickThinLargeGap" => "brdrtnthlg",
            _ => "brdrs",
        });
        if (val is not ("nil" or "none"))
        {
            // ST_EighthPointMeasure -> twips: eighths * 20 / 8.
            _w.Word("brdrw", Math.Clamp(Int(A(e, "sz"), 4) * 5 / 2, 1, 75));
            string? color = A(e, "color");
            if (color is not null and not "auto")
            {
                _w.Word("brdrcf", ColorIndex(color));
            }

            int space = Int(A(e, "space"));
            if (space > 0)
            {
                _w.Word("brsp", space * 20);
            }
        }
    }

    private void ParagraphWords(MarkupNode pPr, bool inStyle)
    {
        MarkupNode numPr = default;
        MarkupNode ind = default;
        foreach (MarkupNode e in pPr.ChildElements)
        {
            if (e.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            switch (e.LocalNameString)
            {
                case "keepNext" when On(e): _w.Word("keepn"); break;
                case "keepLines" when On(e): _w.Word("keep"); break;
                case "pageBreakBefore" when On(e): _w.Word("pagebb"); break;
                case "widowControl": _w.Word(On(e) ? "widctlpar" : "nowidctlpar"); break;
                case "numPr": numPr = e; break;
                case "pBdr":
                    foreach (MarkupNode b in e.ChildElements)
                    {
                        switch (b.LocalNameString)
                        {
                            case "top": BorderWords("brdrt", b); break;
                            case "left" or "start": BorderWords("brdrl", b); break;
                            case "bottom": BorderWords("brdrb", b); break;
                            case "right" or "end": BorderWords("brdrr", b); break;
                            case "between": BorderWords("brdrbtw", b); break;
                            case "bar": BorderWords("brdrbar", b); break;
                        }
                    }

                    break;
                case "shd":
                    {
                        string? fill = A(e, "fill");
                        if (fill is not null and not "auto")
                        {
                            _w.Word("cbpat", ColorIndex(fill));
                        }

                        break;
                    }

                case "tabs":
                    foreach (MarkupNode t in e.ChildElements)
                    {
                        if (!IsW(t, "tab"))
                        {
                            continue;
                        }

                        string val = A(t, "val") ?? "left";
                        if (val == "clear")
                        {
                            continue;
                        }

                        switch (A(t, "leader"))
                        {
                            case "dot": _w.Word("tldot"); break;
                            case "hyphen": _w.Word("tlhyph"); break;
                            case "underscore": _w.Word("tlul"); break;
                            case "heavy": _w.Word("tlth"); break;
                            case "middleDot": _w.Word("tlmdot"); break;
                        }

                        if (val == "bar")
                        {
                            _w.Word("tb", Int(A(t, "pos")));
                        }
                        else
                        {
                            if (val is "right" or "end")
                            {
                                _w.Word("tqr");
                            }
                            else if (val == "center")
                            {
                                _w.Word("tqc");
                            }
                            else if (val == "decimal")
                            {
                                _w.Word("tqdec");
                            }

                            _w.Word("tx", Int(A(t, "pos")));
                        }
                    }

                    break;
                case "bidi" when On(e): _w.Word("rtlpar"); break;
                case "spacing":
                    {
                        if (A(e, "before") is { } bef)
                        {
                            _w.Word("sb", Int(bef));
                        }

                        if (A(e, "beforeAutospacing") is "1" or "true")
                        {
                            _w.Word("sbauto", 1);
                        }

                        if (A(e, "after") is { } aft)
                        {
                            _w.Word("sa", Int(aft));
                        }

                        if (A(e, "afterAutospacing") is "1" or "true")
                        {
                            _w.Word("saauto", 1);
                        }

                        if (A(e, "line") is { } line)
                        {
                            int l = Int(line, 240);
                            string rule = A(e, "lineRule") ?? "auto";
                            if (rule == "auto")
                            {
                                _w.Word("sl", l);
                                _w.Word("slmult", 1);
                            }
                            else
                            {
                                _w.Word("sl", rule == "exact" ? -l : l);
                                _w.Word("slmult", 0);
                            }
                        }

                        break;
                    }

                case "ind": ind = e; break;
                case "contextualSpacing" when On(e): _w.Word("contextualspace"); break;
                case "jc":
                    _w.Word((A(e, "val") ?? "left") switch
                    {
                        "center" => "qc",
                        "right" or "end" => "qr",
                        "both" or "mediumKashida" or "highKashida" or "lowKashida" => "qj",
                        "distribute" => "qd",
                        "thaiDistribute" => "qt",
                        _ => "ql",
                    });
                    break;
                case "outlineLvl":
                    _w.Word("outlinelevel", Int(A(e, "val")));
                    break;
                case "framePr":
                    _loss.Report("loss.rtf-frames", "paragraph frames", 1, "Paragraph frames are written as ordinary paragraphs.");
                    break;
            }
        }

        if (!numPr.IsNull && !inStyle)
        {
            MarkupNode il = Child(numPr, "ilvl");
            MarkupNode ni = Child(numPr, "numId");
            int numId = ni.IsNull ? 0 : Int(A(ni, "val"));
            int level = il.IsNull ? 0 : Int(A(il, "val"));
            if (numId > 0 && _nums.Exists(n => n.NumId == numId))
            {
                _w.Word("ls", numId);
                _w.Word("ilvl", level);
                if (ind.IsNull && LevelIndent(numId, level) is MarkupNode lind && !lind.IsNull)
                {
                    WriteIndent(lind);
                }
            }
        }
        else if (!numPr.IsNull && inStyle)
        {
            MarkupNode ni = Child(numPr, "numId");
            int numId = ni.IsNull ? 0 : Int(A(ni, "val"));
            if (numId > 0 && _nums.Exists(n => n.NumId == numId))
            {
                MarkupNode il = Child(numPr, "ilvl");
                _w.Word("ls", numId);
                _w.Word("ilvl", il.IsNull ? 0 : Int(A(il, "val")));
            }
        }

        if (!ind.IsNull)
        {
            WriteIndent(ind);
        }
    }

    // The indent a list level gives its paragraphs: RTF writers repeat it on every list paragraph.
    private MarkupNode LevelIndent(int numId, int level)
    {
        foreach ((int id, int abs, MarkupNode _) in _nums)
        {
            if (id == numId && _abstractLists.TryGetValue(abs, out AbstractList? al) && al.Levels.TryGetValue(level, out MarkupNode l))
            {
                MarkupNode ppr = Child(l, "pPr");
                return ppr.IsNull ? default : Child(ppr, "ind");
            }
        }

        return default;
    }
}
