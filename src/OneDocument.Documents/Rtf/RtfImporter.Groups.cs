using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfImporter
{
    private readonly List<(string Name, string Xml, RelSet Rels, bool Footer)> _headerParts = [];

    // Destinations whose content this reader does not use. They are kept as bytes when they sit at the top of the file so that an
    // RTF document that is read and written again keeps them.
    private static readonly HashSet<string> KeptHeaderDestinations = new(StringComparer.Ordinal)
    {
        "generator", "xmlnstbl", "themedata", "colorschememapping", "datastore", "latentstyles", "mmathPr", "userprops", "defchp", "defpap",
        "rsidtbl", "wgrffmtfilter", "pgptbl", "docvar", "fchars", "lchars", "nofcharsws", "filetbl", "rsid", "template", "formfield",
        "protusertbl", "xform", "private", "background", "cxs", "nextfile", "mmath", "pgdsctbl", "listpicture",
    };

    private void ProcessGroup(RtfNode g, St st, RtfNode parent)
    {
        _ct.ThrowIfCancellationRequested();
        string? dest = g.Destination;
        if (_curStyle is not null || _collect is not null)
        {
            return;
        }

        switch (dest)
        {
            case "fonttbl": ReadFonts(g); return;
            case "colortbl": ReadColors(g); return;
            case "stylesheet": ReadStyleSheet(g); return;
            case "listtable": ReadListTable(g); return;
            case "listoverridetable": ReadOverrides(g); return;
            case "revtbl":
                {
                    foreach (string a in PlainText(g, st).Split(';'))
                    {
                        if (a.Length > 0)
                        {
                            _authors.Add(a);
                        }
                    }

                    return;
                }

            case "info": ReadInfo(g, st); return;
            case "header" or "headerr" or "headerl" or "headerf" or "footer" or "footerr" or "footerl" or "footerf":
                ReadHeaderFooter(g, st, dest);
                return;
            case "footnote": ReadFootnote(g, st); return;
            case "field": ReadField(g, st); return;
            case "pict": ReadPicture(g, st); return;
            case "shppict": WalkClone(g, st); return;
            case "nonshppict":
                if (!HasChild(parent, "shppict"))
                {
                    WalkClone(g, st);
                }

                return;
            case "bkmkstart": ReadBookmarkStart(g, st); return;
            case "bkmkend": ReadBookmarkEnd(g, st); return;
            case "annotation": ReadAnnotation(g, st); return;
            case "atnid": _atnInitials = PlainText(g, st); return;
            case "atnauthor": _atnAuthor = PlainText(g, st); return;
            case "atrfstart": ReadRangeStart(g, st); return;
            case "atrfend": ReadRangeEnd(g, st); return;
            case "atndate" or "atnparent" or "atntime" or "atnref" or "atnicn":
                return;
            case "object": ReadObject(g, st); return;
            case "shp": ReadShape(g, st); return;
            case "do":
                Loss.Report("loss.rtf-drawing-object", "legacy drawing objects", 1, "Word 97 drawing objects (\\do) are not carried into the document.");
                return;
            case "pn" or "pnseclvl" or "pntxta" or "pntxtb" or "listtext" or "nonesttables" or "shprslt" or "objdata" or "objclass"
                or "objname" or "fldinst" or "fldtype" or "blipuid" or "picprop" or "falt" or "panose" or "fname" or "ftnsep" or "ftnsepc"
                or "aftnsep" or "aftnsepc" or "ftncn" or "aftncn" or "sp" or "bkmkcolf" or "xmlattrname" or "xmlattrvalue" or "xmlopen"
                or "xmlclose" or "keycode" or "pgdsc" or "cpicts":
                return;
            case "pntext":
                WalkClone(g, st);
                return;
            case "nesttableprops":
                _nest++;
                try
                {
                    St c = st.Clone();
                    Walk(g, c, 0);
                }
                finally
                {
                    _nest--;
                }

                return;
            case "upr":
                {
                    // {\upr{ANSI text}{\*\ud{Unicode text}}}: the \ud copy is the one that keeps every character.
                    foreach (RtfNode c in g.Children)
                    {
                        if (c.Kind == RtfNodeKind.Group && c.Destination == "ud")
                        {
                            WalkClone(c, st);
                            return;
                        }
                    }

                    return;
                }

            case "ud":
                WalkClone(g, st);
                return;
            case "rtf" when ReferenceEquals(g, _tree.Root):
                return;
        }

        if (dest is not null && g.IsStarred)
        {
            UnknownDestination(g, dest, parent);
            return;
        }

        if (dest is not null && KeptHeaderDestinations.Contains(dest))
        {
            UnknownDestination(g, dest, parent);
            return;
        }

        if (g.IsStarred)
        {
            UnknownDestination(g, "*", parent);
            return;
        }

        // An ordinary group: formatting and text in its own scope.
        WalkClone(g, st);
    }

    private void UnknownDestination(RtfNode g, string dest, RtfNode parent)
    {
        if (ReferenceEquals(parent, _tree.Root) && _atRootLevel && !_bodyStarted)
        {
            // Kept as bytes so that an RTF -> RTF round trip of an edited document still has them.
            _preserved.Add(_tree.SourceOf(g).ToArray());
        }

        if (dest is not ("generator" or "rsidtbl" or "xmlnstbl" or "wgrffmtfilter" or "datastore" or "themedata" or "colorschememapping" or "latentstyles"
            or "defchp" or "defpap" or "userprops" or "fchars" or "lchars" or "nofcharsws"))
        {
            Loss.Report("loss.rtf-unknown-destination", "unknown destinations", 1, "Extension destinations (\\*) this reader does not know are skipped; at the top of the file they are kept for writing back.");
        }
    }

    private bool _bodyStarted;

    private void WalkClone(RtfNode g, St st)
    {
        St c = st.Clone();
        Walk(g, c, 0);
    }

    private static bool HasChild(RtfNode g, string dest)
    {
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group && c.Destination == dest)
            {
                return true;
            }
        }

        return false;
    }

    // ---- tables of definitions ----------------------------------------------------------------------------------------------------

    private void ReadFonts(RtfNode g)
    {
        FontEntry? cur = null;
        var name = new StringBuilder();
        var bytes = new List<byte>();
        ReadFontItems(g, ref cur, name, bytes);
    }

    private void ReadFontItems(RtfNode container, ref FontEntry? cur, StringBuilder name, List<byte> bytes)
    {
        foreach (RtfNode n in container.Children)
        {
            switch (n.Kind)
            {
                case RtfNodeKind.ControlWord:
                    switch (n.Name)
                    {
                        case "f":
                            cur = new FontEntry { Index = n.Parameter };
                            _fonts[n.Parameter] = cur;
                            name.Clear();
                            bytes.Clear();
                            break;
                        case "froman": if (cur is not null) { cur.Family = "roman"; } break;
                        case "fswiss": if (cur is not null) { cur.Family = "swiss"; } break;
                        case "fmodern": if (cur is not null) { cur.Family = "modern"; } break;
                        case "fscript": if (cur is not null) { cur.Family = "script"; } break;
                        case "fdecor": if (cur is not null) { cur.Family = "decorative"; } break;
                        case "ftech" or "fnil": if (cur is not null) { cur.Family = "auto"; } break;
                        case "fbidi": if (cur is not null) { cur.Family = "auto"; } break;
                        case "fcharset":
                            if (cur is not null)
                            {
                                cur.Charset = n.Parameter;
                                cur.CodePage = n.Parameter == 0 ? 0 : RtfCodePages.FromCharset(n.Parameter);
                            }

                            break;
                        case "cpg":
                            if (cur is not null && n.Parameter > 0)
                            {
                                cur.CodePage = n.Parameter;
                            }

                            break;
                        case "fprq":
                            if (cur is not null) { cur.Pitch = n.Parameter; }
                            break;
                    }

                    break;
                case RtfNodeKind.Hex:
                    bytes.Add(n.Hex);
                    break;
                case RtfNodeKind.Text:
                    {
                        ReadOnlySpan<byte> data = n.Data.Span;
                        foreach (byte b in data)
                        {
                            if (b == (byte)';')
                            {
                                FinishFontName(cur, name, bytes);
                            }
                            else
                            {
                                bytes.Add(b);
                            }
                        }

                        break;
                    }

                case RtfNodeKind.Group:
                    {
                        string? d = n.Destination;
                        if (d == "falt")
                        {
                            if (cur is not null)
                            {
                                cur.AltName = PlainText(n, new St()).TrimEnd(';');
                            }
                        }
                        else if (d is "panose" or "fname" or "fontemb" or "fontfile" or "ftnil")
                        {
                            // not used
                        }
                        else
                        {
                            ReadFontItems(n, ref cur, name, bytes);
                        }

                        break;
                    }
            }
        }
    }

    private void FinishFontName(FontEntry? cur, StringBuilder name, List<byte> bytes)
    {
        if (cur is not null && bytes.Count > 0)
        {
            int cp = cur.CodePage > 0 ? cur.CodePage : _ansiCp;
            cur.Name = Latin([.. bytes], cp).Trim();
        }

        bytes.Clear();
        name.Clear();
    }

    private void ReadColors(RtfNode g)
    {
        int r = -1, gr = -1, b = -1;
        foreach (RtfNode n in g.Children)
        {
            if (n.Kind == RtfNodeKind.ControlWord)
            {
                switch (n.Name)
                {
                    case "red": r = n.Parameter & 255; break;
                    case "green": gr = n.Parameter & 255; break;
                    case "blue": b = n.Parameter & 255; break;
                }
            }
            else if (n.Kind == RtfNodeKind.Text)
            {
                foreach (byte c in n.Data.Span)
                {
                    if (c == (byte)';')
                    {
                        bool any = r >= 0 || gr >= 0 || b >= 0;
                        _colors.Add(any ? (Math.Max(r, 0) << 16) | (Math.Max(gr, 0) << 8) | Math.Max(b, 0) : -1);
                        r = gr = b = -1;
                    }
                }
            }
        }
    }

    private string? ColorHex(int? index)
    {
        if (index is not int i || i < 0 || i >= _colors.Count || _colors[i] < 0)
        {
            return null;
        }

        return X.Hex6(_colors[i]);
    }

    private void ReadInfo(RtfNode g, St st)
    {
        foreach (RtfNode n in g.Children)
        {
            if (n.Kind != RtfNodeKind.Group || n.Destination is not { } d)
            {
                continue;
            }

            switch (d)
            {
                case "creatim" or "revtim" or "printim" or "buptim":
                    {
                        int yr = 0, mo = 0, dy = 0, hr = 0, mi = 0, sec = 0;
                        foreach (RtfNode w in n.Children)
                        {
                            if (w.Kind != RtfNodeKind.ControlWord)
                            {
                                continue;
                            }

                            switch (w.Name)
                            {
                                case "yr": yr = w.Parameter; break;
                                case "mo": mo = w.Parameter; break;
                                case "dy": dy = w.Parameter; break;
                                case "hr": hr = w.Parameter; break;
                                case "min": mi = w.Parameter; break;
                                case "sec": sec = w.Parameter; break;
                            }
                        }

                        if (yr is >= 1601 and <= 9999 && mo is >= 1 and <= 12 && dy is >= 1 and <= 31 && hr is >= 0 and <= 23 && mi is >= 0 and <= 59)
                        {
                            _info[d] = string.Create(CultureInfo.InvariantCulture, $"{yr:D4}-{mo:D2}-{dy:D2}T{hr:D2}:{mi:D2}:{Math.Clamp(sec, 0, 59):D2}Z");
                        }

                        break;
                    }

                case "title" or "subject" or "author" or "manager" or "company" or "operator" or "category" or "keywords" or "doccomm" or "hlinkbase":
                    _info[d] = PlainText(n, st).Trim();
                    break;
            }
        }
    }

    // ---- styles --------------------------------------------------------------------------------------------------------------------

    private void ReadStyleSheet(RtfNode g)
    {
        foreach (RtfNode item in g.Children)
        {
            if (item.Kind != RtfNodeKind.Group)
            {
                continue;
            }

            var def = new StyleDef();
            _curStyle = def;
            _styleName = new StringBuilder();
            try
            {
                Walk(item, def.State, 0);
            }
            finally
            {
                _curStyle = null;
            }

            string nm = _styleName.ToString();
            int semi = nm.IndexOf(';', StringComparison.Ordinal);
            def.Name = (semi >= 0 ? nm[..semi] : nm).Trim();
            _styleName = null;
            if (def.Name.Length == 0)
            {
                continue;
            }

            string key = StyleKey(def.Type, def.Number);
            def.Id = MakeStyleId(def.Name, def.Number);
            _styles[key] = def;
        }
    }

    private static string StyleKey(string type, int number) => type[0] + X.N(number);

    private readonly HashSet<string> _styleIds = new(StringComparer.OrdinalIgnoreCase);

    private string MakeStyleId(string name, int number)
    {
        var sb = new StringBuilder();
        bool upper = true;
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
        }

        string id = sb.Length == 0 ? "Style" + X.N(number) : sb.ToString();
        if (id.Length > 60)
        {
            id = id[..60];
        }

        string unique = id;
        for (int i = 2; !_styleIds.Add(unique); i++)
        {
            unique = id + X.N(i);
        }

        return unique;
    }

    // Words that belong to a style definition and not to text (RTF 1.9.1 "Style Sheet").
    private bool StyleWord(RtfNode n)
    {
        StyleDef d = _curStyle!;
        int v = n.Parameter;
        switch (n.Name)
        {
            case "s": d.Type = "paragraph"; d.Number = v; return true;
            case "cs": d.Type = "character"; d.Number = v; return true;
            case "ts": d.Type = "table"; d.Number = v; return true;
            case "ds": d.Type = "section"; d.Number = v; return true;
            case "sbasedon": d.BasedOn = v; return true;
            case "snext": d.Next = v; return true;
            case "slink": d.Link = v; return true;
            case "additive": d.Additive = true; return true;
            case "sqformat": d.QFormat = true; return true;
            case "shidden" or "ssemihidden": d.Hidden = true; return true;
            case "sunhideused": d.Unhide = true; return true;
            case "spriority": d.Priority = v; return true;
            case "styrsid" or "slocked" or "sautoupd" or "scompose" or "sreply" or "sprmo" or "keycode" or "sbasedon0":
                return true;
            default: return false;
        }
    }

    // ---- lists (RTF 1.9.1 "List Table") ---------------------------------------------------------------------------------------------

    private void ReadListTable(RtfNode g)
    {
        foreach (RtfNode n in g.Children)
        {
            if (n.Kind != RtfNodeKind.Group || n.Destination != "list")
            {
                continue;
            }

            var list = new ListDef();
            foreach (RtfNode c in n.Children)
            {
                if (c.Kind == RtfNodeKind.ControlWord)
                {
                    switch (c.Name)
                    {
                        case "listid": list.Id = c.Parameter; break;
                        case "listtemplateid": list.TemplateId = c.Parameter; break;
                        case "listsimple": list.Simple = c.Parameter != 0; break;
                    }
                }
                else if (c.Kind == RtfNodeKind.Group)
                {
                    if (c.Destination == "listlevel")
                    {
                        list.Levels.Add(ReadListLevel(c));
                    }
                    else if (c.Destination == "listname")
                    {
                        list.Name = PlainText(c, new St()).TrimEnd(';');
                    }
                }
            }

            _lists.Add(list);
        }
    }

    private static ListLevelDef ReadListLevel(RtfNode g)
    {
        var lvl = new ListLevelDef();
        int? font = null;
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.ControlWord)
            {
                int v = c.Parameter;
                switch (c.Name)
                {
                    case "levelnfc" or "levelnfcn": lvl.Nfc = v; break;
                    case "leveljc" or "leveljcn": lvl.Jc = v; break;
                    case "levelfollow": lvl.Follow = v; break;
                    case "levelstartat": lvl.Start = v; break;
                    case "levelnorestart": lvl.NoRestart = v; break;
                    case "levellegal": lvl.LegalNumbering = v; break;
                    case "fi": lvl.Fi = v; break;
                    case "li" or "lin": lvl.Li = v; break;
                    case "jclisttab" or "tx": lvl.Tab = v; break;
                    case "f": font = v; lvl.FontIndex = v; break;
                    case "b": lvl.B = Flag(c); break;
                    case "i": lvl.I = Flag(c); break;
                    case "fs": lvl.Fs = v; break;
                    case "cf": lvl.Cf = v; break;
                    case "ul": lvl.Ul = "single"; break;
                }
            }
            else if (c.Kind == RtfNodeKind.Group && c.Destination == "leveltext")
            {
                lvl.TextItems = ReadLevelText(c);
            }
        }

        _ = font;
        return lvl;
    }

    // The items of \leveltext: the length byte, then characters (bytes, or Unicode characters flagged with 0x10000).
    private static List<int> ReadLevelText(RtfNode g)
    {
        var items = new List<int>();
        int skip = 0;
        int uc = 1;
        foreach (RtfNode c in g.Children)
        {
            switch (c.Kind)
            {
                case RtfNodeKind.ControlWord:
                    if (c.Name == "u")
                    {
                        items.Add(0x10000 | (ushort)(short)c.Parameter);
                        skip = uc;
                    }
                    else if (c.Name == "uc")
                    {
                        uc = c.Parameter;
                    }

                    break;
                case RtfNodeKind.Hex:
                    if (skip > 0)
                    {
                        skip--;
                    }
                    else
                    {
                        items.Add(c.Hex);
                    }

                    break;
                case RtfNodeKind.Text:
                    foreach (byte b in c.Data.Span)
                    {
                        if (skip > 0)
                        {
                            skip--;
                        }
                        else if (b != (byte)';')
                        {
                            items.Add(b);
                        }
                    }

                    break;
            }
        }

        return items;
    }

    private void ReadOverrides(RtfNode g)
    {
        foreach (RtfNode n in g.Children)
        {
            if (n.Kind != RtfNodeKind.Group || n.Destination != "listoverride")
            {
                continue;
            }

            var o = new ListOverrideDef();
            int level = 0;
            foreach (RtfNode c in n.Children)
            {
                if (c.Kind == RtfNodeKind.ControlWord)
                {
                    if (c.Name == "listid")
                    {
                        o.ListId = c.Parameter;
                    }
                    else if (c.Name == "ls")
                    {
                        o.Ls = c.Parameter;
                    }
                }
                else if (c.Kind == RtfNodeKind.Group && c.Destination == "lfolevel")
                {
                    foreach (RtfNode w in c.Children)
                    {
                        if (w.Kind == RtfNodeKind.ControlWord && w.Name == "levelstartat")
                        {
                            o.StartOverrides[level] = w.Parameter;
                        }
                        else if (w.Kind == RtfNodeKind.Group && w.Destination == "listoverridelevel")
                        {
                            foreach (RtfNode x in w.Children)
                            {
                                if (x.Kind == RtfNodeKind.ControlWord && x.Name == "levelstartat")
                                {
                                    o.StartOverrides[level] = x.Parameter;
                                }
                            }
                        }
                    }

                    level++;
                }
            }

            _overrides.Add(o);
        }
    }
}
