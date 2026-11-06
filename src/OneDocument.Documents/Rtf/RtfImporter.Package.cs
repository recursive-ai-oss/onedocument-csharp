using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfImporter
{
    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string RootNs =
        " xmlns:w=\"" + WNs + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"" +
        " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"" +
        " xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
    private const string CtPrefix = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    /// <summary>The document parts as a package, ready for <see cref="Document.FromPackage"/>.</summary>
    internal OpcPackage BuildPackage()
    {
        OpcPackage package = OpcPackage.Create(_ctx.Limits);
        try
        {
            string main = "/word/document.xml";
            package.SetRelationships(null,
            [
                new Relationship("rId1", RelNs + "officeDocument", "word/document.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
                new Relationship("rId3", RelNs + "extended-properties", "docProps/app.xml"),
            ]);

            // The fixed relationships come first so that their ids do not depend on how many headers or pictures a file has.
            var rels = new RelSet();
            rels.Add(RelNs + "styles", "styles.xml");
            if (_overrides.Count > 0)
            {
                rels.Add(RelNs + "numbering", "numbering.xml");
            }

            rels.Add(RelNs + "fontTable", "fontTable.xml");
            rels.Add(RelNs + "settings", "settings.xml");
            if (_footnotes.Count > 0)
            {
                rels.Add(RelNs + "footnotes", "footnotes.xml");
            }

            if (_endnotes.Count > 0)
            {
                rels.Add(RelNs + "endnotes", "endnotes.xml");
            }

            if (_comments.Count > 0)
            {
                rels.Add(RelNs + "comments", "comments.xml");
            }

            foreach ((_, string type, string target, bool external) in _bodyRels.Items)
            {
                if (!rels.Items.Exists(i => i.Type == type && i.Target == target && i.External == external))
                {
                    // Header/footer and picture relationships were numbered from rId1 while the body was read; renumber them after the fixed ones.
                    rels.Items.Add(("rId" + X.N(rels.Items.Count + 1), type, target, external));
                }
            }

            // Map the body's old ids to the new ones, in every place they were written.
            string bodyXml = _bodyXml;
            string finalSect = _finalSect;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < _bodyRels.Items.Count; i++)
            {
                (string oid, string t, string tg, bool ex) = _bodyRels.Items[i];
                string nid = rels.Items.Find(r => r.Type == t && r.Target == tg && r.External == ex).Id;
                map[oid] = nid;
            }

            bodyXml = RemapIds(bodyXml, map);
            finalSect = RemapIds(finalSect, map);

            package.AddPart(main, CtPrefix + "document.main+xml",
                Encoding.UTF8.GetBytes(Decl + "<w:document" + RootNs + "><w:body>" + bodyXml + finalSect + "</w:body></w:document>"));

            package.AddPart("/word/styles.xml", CtPrefix + "styles+xml", Encoding.UTF8.GetBytes(Decl + StylesXml()));
            if (_overrides.Count > 0)
            {
                package.AddPart("/word/numbering.xml", CtPrefix + "numbering+xml", Encoding.UTF8.GetBytes(Decl + NumberingXml()));
            }

            package.AddPart("/word/fontTable.xml", CtPrefix + "fontTable+xml", Encoding.UTF8.GetBytes(Decl + FontTableXml()));
            package.AddPart("/word/settings.xml", CtPrefix + "settings+xml", Encoding.UTF8.GetBytes(Decl + SettingsXml()));
            if (_footnotes.Count > 0)
            {
                package.AddPart("/word/footnotes.xml", CtPrefix + "footnotes+xml", Encoding.UTF8.GetBytes(Decl + NotesXml("footnotes", "footnote", _footnotes)));
                SetPartRels(package, "/word/footnotes.xml", _fnRels);
            }

            if (_endnotes.Count > 0)
            {
                package.AddPart("/word/endnotes.xml", CtPrefix + "endnotes+xml", Encoding.UTF8.GetBytes(Decl + NotesXml("endnotes", "endnote", _endnotes)));
                SetPartRels(package, "/word/endnotes.xml", _enRels);
            }

            if (_comments.Count > 0)
            {
                var cx = new StringBuilder(Decl).Append("<w:comments").Append(RootNs).Append('>');
                foreach ((int _, string xml) in _comments)
                {
                    cx.Append(xml);
                }

                package.AddPart("/word/comments.xml", CtPrefix + "comments+xml", Encoding.UTF8.GetBytes(cx.Append("</w:comments>").ToString()));
                SetPartRels(package, "/word/comments.xml", _cmRels);
            }

            foreach ((string name, string xml, RelSet hr, bool footer) in _headerParts)
            {
                string tag = footer ? "w:ftr" : "w:hdr";
                package.AddPart(name, CtPrefix + (footer ? "footer+xml" : "header+xml"), Encoding.UTF8.GetBytes(Decl + "<" + tag + RootNs + ">" + xml + "</" + tag + ">"));
                SetPartRels(package, name, hr);
            }

            foreach (Media m in _media)
            {
                package.AddPart(m.Name, m.ContentType, m.Data);
            }

            SetPartRels(package, main, rels);
            WriteDocProps(package);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private static string RemapIds(string xml, Dictionary<string, string> map)
    {
        if (map.Count == 0 || xml.Length == 0)
        {
            return xml;
        }

        // Only attributes of the form r:id="rIdN" / r:embed="rIdN" carry a relationship id.
        var sb = new StringBuilder(xml.Length);
        int i = 0;
        while (i < xml.Length)
        {
            int at = xml.IndexOf("=\"rId", i, StringComparison.Ordinal);
            if (at < 0)
            {
                sb.Append(xml, i, xml.Length - i);
                break;
            }

            int end = xml.IndexOf('"', at + 2);
            if (end < 0)
            {
                sb.Append(xml, i, xml.Length - i);
                break;
            }

            string id = xml.Substring(at + 2, end - at - 2);
            sb.Append(xml, i, at + 2 - i).Append(map.TryGetValue(id, out string? n) ? n : id);
            i = end;
        }

        return sb.ToString();
    }

    private static void SetPartRels(OpcPackage package, string part, RelSet rels)
    {
        if (rels.Items.Count == 0)
        {
            return;
        }

        var list = new List<Relationship>();
        foreach ((string id, string type, string target, bool external) in rels.Items)
        {
            list.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
        }

        package.SetRelationships(part, list);
    }

    private static string NotesXml(string root, string item, List<(int Id, string Xml)> notes)
    {
        var sb = new StringBuilder("<w:").Append(root).Append(RootNs).Append('>');
        string sep = "<w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p>";
        string cont = "<w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p>";
        sb.Append("<w:").Append(item).Append(" w:type=\"separator\" w:id=\"-1\">").Append(sep).Append("</w:").Append(item).Append('>');
        sb.Append("<w:").Append(item).Append(" w:type=\"continuationSeparator\" w:id=\"0\">").Append(cont).Append("</w:").Append(item).Append('>');
        foreach ((int _, string xml) in notes)
        {
            sb.Append(xml);
        }

        return sb.Append("</w:").Append(root).Append('>').ToString();
    }

    private string SettingsXml()
    {
        var sb = new StringBuilder("<w:settings").Append(RootNs).Append('>');
        if (_docSec.MirrorMargins)
        {
            sb.Append("<w:mirrorMargins/>");
        }

        if (_trackRevisions)
        {
            sb.Append("<w:trackRevisions/>");
        }

        sb.Append("<w:defaultTabStop w:val=\"").Append(X.N(_defTab)).Append("\"/>");
        if (_facingPages)
        {
            sb.Append("<w:evenAndOddHeaders/>");
        }

        if (_footnotes.Count > 0)
        {
            sb.Append("<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>");
        }

        if (_endnotes.Count > 0)
        {
            sb.Append("<w:endnotePr><w:endnote w:id=\"-1\"/><w:endnote w:id=\"0\"/></w:endnotePr>");
        }

        return sb.Append("</w:settings>").ToString();
    }

    private string FontTableXml()
    {
        var sb = new StringBuilder("<w:fonts").Append(RootNs).Append('>');
        foreach (FontEntry f in _fonts.Values.OrderBy(f => f.Index))
        {
            if (f.Name.Length == 0)
            {
                continue;
            }

            sb.Append("<w:font w:name=\"").Append(X.Esc(f.Name)).Append("\">");
            if (f.AltName is { Length: > 0 } alt)
            {
                sb.Append("<w:altName w:val=\"").Append(X.Esc(alt)).Append("\"/>");
            }

            sb.Append("<w:charset w:val=\"").Append((f.Charset & 255).ToString("X2", CultureInfo.InvariantCulture)).Append("\"/>");
            sb.Append("<w:family w:val=\"").Append(f.Family).Append("\"/>");
            sb.Append("<w:pitch w:val=\"").Append(f.Pitch == 1 ? "fixed" : f.Pitch == 2 ? "variable" : "default").Append("\"/></w:font>");
        }

        return sb.Append("</w:fonts>").ToString();
    }

    private string StylesXml()
    {
        var sb = new StringBuilder("<w:styles").Append(RootNs).Append('>');
        sb.Append("<w:docDefaults><w:rPrDefault><w:rPr>");
        if (_fonts.TryGetValue(_defFont, out FontEntry? df) && df.Name.Length > 0)
        {
            string n = X.Esc(df.Name);
            sb.Append("<w:rFonts w:ascii=\"").Append(n).Append("\" w:hAnsi=\"").Append(n).Append("\" w:eastAsia=\"").Append(n).Append("\" w:cs=\"").Append(n).Append("\"/>");
        }

        // RTF 1.9.1 \fs: the default size is 24 half-points (12 pt).
        sb.Append("<w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/><w:lang w:val=\"").Append(LangTag(_defLang)).Append("\"/></w:rPr></w:rPrDefault>");
        sb.Append("<w:pPrDefault><w:pPr><w:widowControl").Append(_widowCtl ? "/>" : " w:val=\"0\"/>").Append("</w:pPr></w:pPrDefault></w:docDefaults>");
        bool hasNormal = false;
        foreach (StyleDef d in _styles.Values.OrderBy(s => s.Type).ThenBy(s => s.Number))
        {
            if (d.Type == "section")
            {
                continue;
            }

            bool isDefault = d.Type == "paragraph" && d.Number == 0 || d.Type == "character" && d.Name.Equals("Default Paragraph Font", StringComparison.OrdinalIgnoreCase)
                || d.Type == "table" && (d.Name.Equals("Normal Table", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("Table Normal", StringComparison.OrdinalIgnoreCase));
            hasNormal |= d.Type == "paragraph" && d.Number == 0;
            sb.Append("<w:style w:type=\"").Append(d.Type).Append('"').Append(isDefault ? " w:default=\"1\"" : string.Empty).Append(" w:styleId=\"").Append(X.Esc(d.Id)).Append("\">");
            sb.Append("<w:name w:val=\"").Append(X.Esc(d.Name)).Append("\"/>");
            if (d.BasedOn >= 0 && !(d.Type == "paragraph" && d.Number == 0) && _styles.TryGetValue(StyleKey(d.Type, d.BasedOn), out StyleDef? bd) && bd != d)
            {
                sb.Append("<w:basedOn w:val=\"").Append(X.Esc(bd.Id)).Append("\"/>");
            }

            if (d.Next >= 0 && d.Type == "paragraph" && _styles.TryGetValue(StyleKey("paragraph", d.Next), out StyleDef? nd))
            {
                sb.Append("<w:next w:val=\"").Append(X.Esc(nd.Id)).Append("\"/>");
            }

            if (d.Priority >= 0)
            {
                sb.Append("<w:uiPriority w:val=\"").Append(X.N(d.Priority)).Append("\"/>");
            }

            if (d.Hidden)
            {
                sb.Append("<w:semiHidden/>");
            }

            if (d.QFormat)
            {
                sb.Append("<w:qFormat/>");
            }

            if (d.Type != "character")
            {
                string pp = ParaProps(d.State, d.State.P, forStyle: true);
                if (pp.Length > 0)
                {
                    sb.Append("<w:pPr>").Append(pp).Append("</w:pPr>");
                }
            }

            var rp = new StringBuilder();
            AppendRunProps(rp, d.State, includeStyle: false);
            if (rp.Length > 0)
            {
                sb.Append("<w:rPr>").Append(rp).Append("</w:rPr>");
            }

            sb.Append("</w:style>");
        }

        if (!hasNormal)
        {
            sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        }

        return sb.Append("</w:styles>").ToString();
    }

    // ECMA-376 Part 1 §17.9 (numbering): one abstractNum per RTF list, one num per list override.
    private string NumberingXml()
    {
        var sb = new StringBuilder("<w:numbering").Append(RootNs).Append('>');
        for (int i = 0; i < _lists.Count; i++)
        {
            ListDef l = _lists[i];
            sb.Append("<w:abstractNum w:abstractNumId=\"").Append(X.N(i)).Append("\"><w:nsid w:val=\"").Append(((uint)l.Id).ToString("X8", CultureInfo.InvariantCulture)).Append("\"/>");
            sb.Append("<w:multiLevelType w:val=\"").Append(l.Simple ? "singleLevel" : "multilevel").Append("\"/>");
            if (l.Name.Length > 0)
            {
                sb.Append("<w:name w:val=\"").Append(X.Esc(l.Name)).Append("\"/>");
            }

            for (int k = 0; k < l.Levels.Count && k < 9; k++)
            {
                LevelXml(sb, l.Levels[k], k);
            }

            sb.Append("</w:abstractNum>");
        }

        foreach (ListOverrideDef o in _overrides)
        {
            int idx = _lists.FindIndex(l => l.Id == o.ListId);
            if (idx < 0)
            {
                continue;
            }

            sb.Append("<w:num w:numId=\"").Append(X.N(o.Ls)).Append("\"><w:abstractNumId w:val=\"").Append(X.N(idx)).Append("\"/>");
            foreach (KeyValuePair<int, int> so in o.StartOverrides.OrderBy(p => p.Key))
            {
                if (so.Key < 9)
                {
                    sb.Append("<w:lvlOverride w:ilvl=\"").Append(X.N(so.Key)).Append("\"><w:startOverride w:val=\"").Append(X.N(so.Value)).Append("\"/></w:lvlOverride>");
                }
            }

            sb.Append("</w:num>");
        }

        return sb.Append("</w:numbering>").ToString();
    }

    private void LevelXml(StringBuilder sb, ListLevelDef l, int ilvl)
    {
        FontEntry? font = l.FontIndex is int fi && _fonts.TryGetValue(fi, out FontEntry? fe) ? fe : null;
        bool symbol = font is { Charset: 2 };
        string fmt = NumFmt(l.Nfc);
        sb.Append("<w:lvl w:ilvl=\"").Append(X.N(ilvl)).Append("\"><w:start w:val=\"").Append(X.N(l.Start)).Append("\"/><w:numFmt w:val=\"").Append(fmt).Append("\"/>");
        if (l.NoRestart == 1)
        {
            sb.Append("<w:lvlRestart w:val=\"0\"/>");
        }

        if (l.LegalNumbering == 1)
        {
            sb.Append("<w:isLgl/>");
        }

        sb.Append("<w:suff w:val=\"").Append(l.Follow switch { 1 => "space", 2 => "nothing", _ => "tab" }).Append("\"/>");
        var text = new StringBuilder();
        List<int> items = l.TextItems;
        int count = items.Count > 0 ? Math.Min(items.Count - 1, Math.Max(0, items[0])) : 0;
        if (items.Count > 1 && items[0] <= 0)
        {
            count = items.Count - 1;
        }

        for (int i = 1; i <= count && i < items.Count; i++)
        {
            int it = items[i];
            if ((it & 0x10000) != 0)
            {
                text.Append((char)(it & 0xFFFF));
            }
            else if (it < 9)
            {
                text.Append('%').Append(it + 1);
            }
            else if (symbol && it >= 0x20)
            {
                text.Append((char)(0xF000 + it));
            }
            else
            {
                text.Append(Latin([(byte)it], font is { CodePage: > 0 } ? font.CodePage : _ansiCp));
            }
        }

        sb.Append("<w:lvlText w:val=\"").Append(X.Esc(text.ToString())).Append("\"/>");
        sb.Append("<w:lvlJc w:val=\"").Append(l.Jc switch { 1 => "center", 2 => "right", _ => "left" }).Append("\"/>");
        if (l.Li is not null || l.Fi is not null || l.Tab is not null)
        {
            sb.Append("<w:pPr>");
            if (l.Tab is int tab)
            {
                sb.Append("<w:tabs><w:tab w:val=\"num\" w:pos=\"").Append(X.N(tab)).Append("\"/></w:tabs>");
            }

            sb.Append("<w:ind");
            if (l.Li is int li)
            {
                sb.Append(" w:left=\"").Append(X.N(li)).Append('"');
            }

            if (l.Fi is int f && f != 0)
            {
                sb.Append(f > 0 ? " w:firstLine=\"" + X.N(f) + "\"" : " w:hanging=\"" + X.N(-f) + "\"");
            }

            sb.Append("/></w:pPr>");
        }

        var rp = new StringBuilder();
        if (font is { Name.Length: > 0 })
        {
            string n = X.Esc(font.Name);
            rp.Append("<w:rFonts w:ascii=\"").Append(n).Append("\" w:hAnsi=\"").Append(n).Append("\" w:hint=\"default\"/>");
        }

        Toggle(rp, "b", l.B);
        Toggle(rp, "i", l.I);
        if (l.Cf is int cf)
        {
            rp.Append("<w:color w:val=\"").Append(ColorHex(cf) ?? "auto").Append("\"/>");
        }

        if (l.Fs is int fs && fs > 0)
        {
            rp.Append("<w:sz w:val=\"").Append(X.N(fs)).Append("\"/>");
        }

        if (l.Ul is not null)
        {
            rp.Append("<w:u w:val=\"").Append(l.Ul).Append("\"/>");
        }

        if (rp.Length > 0)
        {
            sb.Append("<w:rPr>").Append(rp).Append("</w:rPr>");
        }

        sb.Append("</w:lvl>");
    }

    // RTF 1.9.1 \levelnfc -> ST_NumberFormat (ECMA-376 Part 1 §17.18.59).
    private static string NumFmt(int nfc) => nfc switch
    {
        0 => "decimal", 1 => "upperRoman", 2 => "lowerRoman", 3 => "upperLetter", 4 => "lowerLetter", 5 => "ordinal", 6 => "cardinalText",
        7 => "ordinalText", 10 => "ideographDigital", 11 => "japaneseCounting", 12 => "aiueo", 13 => "iroha", 14 => "decimalFullWidth",
        15 => "decimalHalfWidth", 18 => "decimalEnclosedCircle", 22 => "decimalZero", 23 => "bullet", 24 => "ganada", 25 => "chosung",
        37 => "chineseCounting", 38 => "chineseLegalSimplified", 39 => "chineseCountingThousand", 40 => "koreanDigital", 41 => "koreanCounting",
        44 => "hebrew1", 45 => "arabicAlpha", 46 => "hebrew2", 47 => "arabicAbjad", 52 => "thaiLetters", 53 => "thaiNumbers", 54 => "thaiCounting",
        56 => "numberInDash", 57 => "russianLower", 58 => "russianUpper", 255 => "none",
        _ => "decimal",
    };

    private void WriteDocProps(OpcPackage package)
    {
        var core = new StringBuilder(Decl).Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
        void Elem(string tag, string key)
        {
            if (_info.TryGetValue(key, out string? v) && v.Length > 0)
            {
                core.Append('<').Append(tag).Append('>').Append(X.Esc(v)).Append("</").Append(tag).Append('>');
            }
        }

        void Date(string tag, string key)
        {
            if (_info.TryGetValue(key, out string? v))
            {
                core.Append('<').Append(tag).Append(" xsi:type=\"dcterms:W3CDTF\">").Append(v).Append("</").Append(tag).Append('>');
            }
        }

        Elem("cp:category", "category");
        Date("dcterms:created", "creatim");
        Elem("dc:creator", "author");
        Elem("dc:description", "doccomm");
        Elem("cp:keywords", "keywords");
        Elem("cp:lastModifiedBy", "operator");
        Date("dcterms:modified", "revtim");
        Elem("dc:subject", "subject");
        Elem("dc:title", "title");
        package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(core.Append("</cp:coreProperties>").ToString()));

        var app = new StringBuilder(Decl).Append("<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">");
        if (_info.TryGetValue("company", out string? co) && co.Length > 0)
        {
            app.Append("<Company>").Append(X.Esc(co)).Append("</Company>");
        }

        if (_info.TryGetValue("manager", out string? mg) && mg.Length > 0)
        {
            app.Append("<Manager>").Append(X.Esc(mg)).Append("</Manager>");
        }

        package.AddPart("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", Encoding.UTF8.GetBytes(app.Append("</Properties>").ToString()));
    }

    /// <summary>The raw bytes of header-level destinations kept for writing back.</summary>
    internal IReadOnlyList<byte[]> Preserved => _preserved;
}
