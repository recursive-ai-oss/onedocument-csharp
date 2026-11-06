using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Packaging;
using static RecursiveAI.OneDocument.Ppt.Binary.PptXml;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>Turns PowerPoint text bodies into DrawingML paragraphs and runs.</summary>
internal static class PptTextWriter
{
    /// <summary>The paragraph properties for a PowerPoint paragraph format: only the fields the format sets, so everything else inherits as it did in PowerPoint.</summary>
    public static string ParagraphProperties(PptParagraphFormat pf, int level, PageOut page, PptRuler? ruler, string tag = "a:pPr", string extra = "")
    {
        var attrs = new StringBuilder();
        if (level > 0)
        {
            attrs.Append(" lvl=\"").Append(level).Append('"');
        }

        int? left = pf.Has(PptParagraphMasks.LeftMargin) ? pf.LeftMargin : ruler?.LeftMargin[Math.Min(level, 4)];
        int? indent = pf.Has(PptParagraphMasks.Indent) ? pf.Indent : ruler?.Indent[Math.Min(level, 4)];
        if (left is { } l)
        {
            attrs.Append(" marL=\"").Append(Num(Emu(Math.Max(0, l)))).Append('"');
        }

        if (indent is { } ind)
        {
            // PowerPoint stores the indent as the position of the first line; DrawingML stores it relative to the left margin.
            // [MS-PPT] §2.9.20 (leftMargin, indent) and ECMA-376 Part 1 §21.1.2.2.7 (indent). PowerPoint's own conversion keeps the pair as stored when both exist.
            long relative = left is { } lm ? ind - lm : ind;
            attrs.Append(" indent=\"").Append(Num(Emu(relative))).Append('"');
        }

        if (pf.Has(PptParagraphMasks.Align))
        {
            string? algn = pf.Alignment switch
            {
                0 => "l",
                1 => "ctr",
                2 => "r",
                3 => "just",
                4 => "dist",
                5 => "thaiDist",
                6 => "justLow",
                _ => null,
            };
            if (algn is not null)
            {
                attrs.Append(" algn=\"").Append(algn).Append('"');
            }
        }

        if (pf.Has(PptParagraphMasks.DefaultTabSize))
        {
            attrs.Append(" defTabSz=\"").Append(Num(Emu(Math.Max(0, pf.DefaultTabSize)))).Append('"');
        }

        if (pf.Has(PptParagraphMasks.TextDirection))
        {
            attrs.Append(" rtl=\"").Append(pf.TextDirection == 1 ? '1' : '0').Append('"');
        }

        var body = new StringBuilder();
        if (pf.Has(PptParagraphMasks.LineSpacing))
        {
            body.Append("<a:lnSpc>").Append(Spacing(pf.LineSpacing)).Append("</a:lnSpc>");
        }

        if (pf.Has(PptParagraphMasks.SpaceBefore))
        {
            body.Append("<a:spcBef>").Append(Spacing(pf.SpaceBefore)).Append("</a:spcBef>");
        }

        if (pf.Has(PptParagraphMasks.SpaceAfter))
        {
            body.Append("<a:spcAft>").Append(Spacing(pf.SpaceAfter)).Append("</a:spcAft>");
        }

        AppendBullet(body, pf, page);
        IReadOnlyList<(int Position, int Type)> tabs = pf.Has(PptParagraphMasks.TabStops) ? pf.TabStops : ruler?.TabStops ?? [];
        if (tabs.Count > 0)
        {
            body.Append("<a:tabLst>");
            // ECMA-376 Part 1 §21.1.2.2.13: a tab list holds at most 32 tabs.
            foreach ((int position, int type) in tabs.Take(32))
            {
                string alg = type switch
                {
                    1 => "ctr",
                    2 => "r",
                    3 => "dec",
                    _ => "l",
                };
                body.Append("<a:tab pos=\"").Append(Num(Emu(Math.Max(0, position)))).Append("\" algn=\"").Append(alg).Append("\"/>");
            }

            body.Append("</a:tabLst>");
        }

        body.Append(extra);
        if (attrs.Length == 0 && body.Length == 0)
        {
            return "";
        }

        return body.Length == 0 ? "<" + tag + attrs + "/>" : "<" + tag + attrs + ">" + body + "</" + tag + ">";
    }

    // [MS-PPT] §2.2.20 (ParaSpacing): 0 to 13200 is a percentage of the line height, a negative value is master units (1/8 point).
    private static string Spacing(int value) =>
        value >= 0 ? "<a:spcPct val=\"" + Num(value * 1000L) + "\"/>" : "<a:spcPts val=\"" + Num(-value * 100L / 8) + "\"/>";

    private static void AppendBullet(StringBuilder body, PptParagraphFormat pf, PageOut page)
    {
        // DrawingML order: buClr, buSzPct/buSzPts, buFont, then the bullet itself.
        bool? has = pf.HasBulletFlag;
        if (pf.Has(PptParagraphMasks.BulletColor) && (pf.BulletFlags & 4) != 0 && !pf.BulletColor.IsUndefined)
        {
            body.Append("<a:buClr>").Append(page.Color(pf.BulletColor)).Append("</a:buClr>");
        }

        if (pf.Has(PptParagraphMasks.BulletSize) && (pf.BulletFlags & 8) != 0)
        {
            int size = pf.BulletSize;
            if (size is >= 25 and <= 400)
            {
                body.Append("<a:buSzPct val=\"").Append(Num(size * 1000L)).Append("\"/>");
            }
            else if (size < 0)
            {
                body.Append("<a:buSzPts val=\"").Append(Num(-size * 100L)).Append("\"/>");
            }
        }

        if (pf.Has(PptParagraphMasks.BulletFont) && (pf.BulletFlags & 2) != 0)
        {
            body.Append("<a:buFont typeface=\"").Append(Escape(page.Conv.Font(pf.BulletFontRef))).Append("\"/>");
        }

        if (has == false)
        {
            body.Append("<a:buNone/>");
        }
        else if (has == true || pf.Has(PptParagraphMasks.BulletChar))
        {
            if (pf.Has(PptParagraphMasks.BulletChar) && pf.BulletChar != 0)
            {
                body.Append("<a:buChar char=\"").Append(Escape(pf.BulletChar.ToString())).Append("\"/>");
            }
            else if (has == true)
            {
                body.Append("<a:buChar char=\"&#8226;\"/>");
            }
        }
    }

    /// <summary>Run properties for a character format. <paramref name="language"/> is a BCP 47 tag or empty. <paramref name="link"/> is a relationship id for a hyperlink, or empty.</summary>
    public static string RunProperties(PptCharFormat? cf, string language, PageOut page, string tag, string? link = null, string linkAction = "")
    {
        var attrs = new StringBuilder();
        if (language.Length > 0)
        {
            attrs.Append(" lang=\"").Append(language).Append('"');
        }

        var children = new StringBuilder();
        if (cf is not null)
        {
            if (cf.Has(17) && cf.FontSize is >= 1 and <= 4000)
            {
                attrs.Append(" sz=\"").Append(cf.FontSize * 100).Append('"');
            }

            if (cf.Bold is { } b)
            {
                attrs.Append(" b=\"").Append(b ? '1' : '0').Append('"');
            }

            if (cf.Italic is { } i)
            {
                attrs.Append(" i=\"").Append(i ? '1' : '0').Append('"');
            }

            if (cf.Underline is { } u)
            {
                attrs.Append(" u=\"").Append(u ? "sng" : "none").Append('"');
            }

            if (cf.Has(19) && cf.Position != 0)
            {
                attrs.Append(" baseline=\"").Append(cf.Position * 1000).Append('"');
            }

            if (cf.Has(18) && !cf.Color.IsUndefined)
            {
                children.Append("<a:solidFill>").Append(page.Color(cf.Color)).Append("</a:solidFill>");
            }

            if (cf.Shadow == true)
            {
                children.Append("<a:effectLst><a:outerShdw blurRad=\"38100\" dist=\"38100\" dir=\"2700000\" algn=\"tl\"><a:srgbClr val=\"000000\"><a:alpha val=\"43137\"/></a:srgbClr></a:outerShdw></a:effectLst>");
            }

            // DrawingML order: latin, ea, cs, sym. The ANSI typeface stands in for the Latin one only when the format names none.
            if (cf.Has(16))
            {
                children.Append("<a:latin typeface=\"").Append(Escape(page.Conv.Font(cf.FontRef))).Append("\"/>");
            }
            else if (cf.Has(22))
            {
                children.Append("<a:latin typeface=\"").Append(Escape(page.Conv.Font(cf.AnsiFontRef))).Append("\"/>");
            }

            if (cf.Has(21))
            {
                children.Append("<a:ea typeface=\"").Append(Escape(page.Conv.Font(cf.OldEaFontRef))).Append("\"/>");
            }

            if (cf.Has(23))
            {
                children.Append("<a:sym typeface=\"").Append(Escape(page.Conv.Font(cf.SymbolFontRef))).Append("\"/>");
            }
        }

        if (link is not null)
        {
            children.Append("<a:hlinkClick r:id=\"").Append(link).Append('"').Append(linkAction).Append("/>");
        }

        if (attrs.Length == 0 && children.Length == 0)
        {
            return "";
        }

        return children.Length == 0 ? "<" + tag + attrs + "/>" : "<" + tag + attrs + ">" + children + "</" + tag + ">";
    }

    /// <summary>Writes the paragraphs of a text body (the <c>a:p</c> elements).</summary>
    public static void WriteParagraphs(StringBuilder sb, PptTextBody body, PageOut page, IReadOnlyDictionary<int, string>? links = null)
    {
        string text = body.Text;
        // Hyperlink ranges from the interactive records that follow the text: (start, end, target).
        List<(int Start, int End, string Rel, string Action)> ranges = LinkRanges(body, page);
        int position = 0;
        while (true)
        {
            int end = text.IndexOf('\r', position);
            bool last = end < 0;
            if (last)
            {
                end = text.Length;
            }

            WriteParagraph(sb, body, page, position, end, ranges);
            if (last)
            {
                break;
            }

            position = end + 1;
            if (position >= text.Length)
            {
                // A text that ends with a paragraph break has no further paragraph; the body's own terminating break closes the last one.
                break;
            }
        }
    }

    private static void WriteParagraph(StringBuilder sb, PptTextBody body, PageOut page, int start, int end, List<(int Start, int End, string Rel, string Action)> ranges)
    {
        PptParagraphRun? run = RunAt(body.ParagraphRuns, start, out int _);
        int level = run?.IndentLevel ?? 0;
        if (run is null && body.MasterLevels.Count > 0)
        {
            level = MasterLevelAt(body, start);
        }

        sb.Append("<a:p>");
        if (run is not null)
        {
            sb.Append(ParagraphProperties(run.Format, level, page, body.Ruler));
        }
        else if (level > 0)
        {
            sb.Append("<a:pPr lvl=\"").Append(level).Append("\"/>");
        }

        int i = start;
        string lastRunProps = "";
        while (i < end)
        {
            char c = body.Text[i];
            if (c == '\v')
            {
                sb.Append("<a:br>").Append(RunProperties(CharAt(body, i, out int _), LanguageAt(body, i), page, "a:rPr")).Append("</a:br>");
                i++;
                continue;
            }

            if (body.MetaChars.FirstOrDefault(m => m.Position == i) is { } meta)
            {
                WriteField(sb, body, page, meta, i);
                i++;
                continue;
            }

            // A run ends where the character run, the language, the hyperlink, a line break or a field changes.
            PptCharFormat? cf = CharAt(body, i, out int cfEnd);
            string lang = LanguageAt(body, i);
            (int Start, int End, string Rel, string Action) link = ranges.Find(r => r.Start <= i && i < r.End);
            int runEnd = Math.Min(end, cfEnd);
            for (int k = i + 1; k < runEnd; k++)
            {
                if (body.Text[k] == '\v' || body.MetaChars.Any(m => m.Position == k) || LanguageAt(body, k) != lang)
                {
                    runEnd = k;
                    break;
                }

                (int Start, int End, string Rel, string Action) other = ranges.Find(r => r.Start <= k && k < r.End);
                if (other.Rel != link.Rel)
                {
                    runEnd = k;
                    break;
                }
            }

            string rPr = RunProperties(cf, lang, page, "a:rPr", link.Rel, link.Action ?? "");
            sb.Append("<a:r>").Append(rPr).Append("<a:t>").Append(Escape(body.Text.Substring(i, runEnd - i))).Append("</a:t></a:r>");
            lastRunProps = rPr;
            i = runEnd;
        }

        if (lastRunProps.Length > 0 || end == start)
        {
            PptCharFormat? tail = CharAt(body, Math.Min(end, Math.Max(0, body.Text.Length)), out int _);
            string endProps = RunProperties(tail, "", page, "a:endParaRPr");
            sb.Append(endProps);
        }

        sb.Append("</a:p>");
    }

    private static void WriteField(StringBuilder sb, PptTextBody body, PageOut page, PptMetaChar meta, int position)
    {
        PptCharFormat? cf = CharAt(body, position, out int _);
        string rPr = RunProperties(cf, LanguageAt(body, position), page, "a:rPr");
        (string? type, string shown) = meta.Kind switch
        {
            PptMetaCharKind.SlideNumber => ("slidenum", "‹#›"),
            PptMetaCharKind.DateTime => ("datetime" + Math.Clamp(meta.FormatId + 1, 1, 13), "‹date›"),
            PptMetaCharKind.GenericDate => ("datetime", "‹date›"),
            PptMetaCharKind.RtfDateTime => ("datetime", "‹date›"),
            _ => ((string?)null, ""),
        };
        if (type is null)
        {
            // Header and footer metacharacters stand for the page's header and footer text; DrawingML has no field for them, so the text itself is written.
            string? literal = page.Conv.HeaderFooterText(page.Source, meta.Kind);
            if (literal is null)
            {
                if (page.Source is not null)
                {
                    page.Conv.Loss.Report("ppt.header-footer-field", "header/footer field", 1, "A header or footer metacharacter has no text in the file's header and footer records; nothing is written for it.");
                }

                return;
            }

            sb.Append("<a:r>").Append(rPr).Append("<a:t>").Append(Escape(literal)).Append("</a:t></a:r>");
            return;
        }

        sb.Append("<a:fld id=\"{").Append(FieldGuid(page.NextField())).Append("}\" type=\"").Append(type).Append("\">");
        sb.Append(rPr.Length == 0 ? "<a:rPr lang=\"en-US\"/>" : rPr).Append("<a:t>").Append(shown).Append("</a:t></a:fld>");
    }

    // Deterministic field identifiers: DrawingML wants a GUID, nothing requires it to be random.
    private static string FieldGuid(int n) => "00000000-0000-4000-8000-" + n.ToString("X12", System.Globalization.CultureInfo.InvariantCulture);

    private static PptParagraphRun? RunAt(IReadOnlyList<PptParagraphRun> runs, int position, out int runEnd)
    {
        int at = 0;
        foreach (PptParagraphRun run in runs)
        {
            if (position < at + run.Count)
            {
                runEnd = at + run.Count;
                return run;
            }

            at += run.Count;
        }

        runEnd = int.MaxValue;
        return null;
    }

    private static PptCharFormat? CharAt(PptTextBody body, int position, out int runEnd)
    {
        int at = 0;
        foreach (PptCharRun run in body.CharRuns)
        {
            if (position < at + run.Count)
            {
                runEnd = at + run.Count;
                return run.Format;
            }

            at += run.Count;
        }

        runEnd = int.MaxValue;
        return null;
    }

    private static int MasterLevelAt(PptTextBody body, int position)
    {
        int at = 0;
        foreach ((int count, int level) in body.MasterLevels)
        {
            if (position < at + count)
            {
                return Math.Min(level, 4);
            }

            at += count;
        }

        return 0;
    }

    private static string LanguageAt(PptTextBody body, int position)
    {
        int at = 0;
        foreach (PptLanguageRun run in body.LanguageRuns)
        {
            if (position < at + run.Count)
            {
                return LanguageTag(run.LanguageId);
            }

            at += run.Count;
        }

        return "";
    }

    // Windows LCIDs that presentations commonly carry; anything else is left without a language so the application default applies.
    private static string LanguageTag(int lcid) => lcid switch
    {
        0x0409 => "en-US",
        0x0809 => "en-GB",
        0x0C09 => "en-AU",
        0x1009 => "en-CA",
        0x0407 => "de-DE",
        0x0807 => "de-CH",
        0x0C07 => "de-AT",
        0x040C => "fr-FR",
        0x080C => "fr-BE",
        0x0C0C => "fr-CA",
        0x040A or 0x0C0A => "es-ES",
        0x080A => "es-MX",
        0x0410 => "it-IT",
        0x0816 => "pt-PT",
        0x0416 => "pt-BR",
        0x0413 => "nl-NL",
        0x041D => "sv-SE",
        0x0414 => "nb-NO",
        0x0406 => "da-DK",
        0x040B => "fi-FI",
        0x0415 => "pl-PL",
        0x0419 => "ru-RU",
        0x0405 => "cs-CZ",
        0x040E => "hu-HU",
        0x0408 => "el-GR",
        0x041F => "tr-TR",
        0x0401 => "ar-SA",
        0x040D => "he-IL",
        0x0411 => "ja-JP",
        0x0412 => "ko-KR",
        0x0804 => "zh-CN",
        0x0404 => "zh-TW",
        0x041E => "th-TH",
        0x0439 => "hi-IN",
        _ => "",
    };

    // [MS-PPT] §2.9.56 and §2.9.57: an InteractiveInfo container (the action) precedes the TextInteractiveInfoAtom that gives the range.
    private static List<(int Start, int End, string Rel, string Action)> LinkRanges(PptTextBody body, PageOut page)
    {
        var ranges = new List<(int, int, string, string)>();
        ArtRecord? pending = null;
        foreach (ArtRecord r in body.Interactive)
        {
            if (r.Type == PptRecordType.InteractiveInfo)
            {
                pending = r;
            }
            else if (r.Type == PptRecordType.TextInteractiveInfoAtom && pending is not null)
            {
                int start = r.Int32At(0);
                int end = r.Int32At(4);
                if (PptActions.TryHyperlink(pending, page, out string rel, out string action) && end > start)
                {
                    ranges.Add((start, end, rel, action));
                }

                pending = null;
            }
        }

        return ranges;
    }
}

/// <summary>Interactive actions ([MS-PPT] §2.6.10) that become DrawingML hyperlinks.</summary>
internal static class PptActions
{
    /// <summary>Reads the action of an <c>InteractiveInfo</c> container and adds the matching relationship to the page. Returns false when the action is not a hyperlink or jump.</summary>
    public static bool TryHyperlink(ArtRecord interactive, PageOut page, out string relId, out string actionAttrs)
    {
        relId = "";
        actionAttrs = "";
        ArtRecord? atom = interactive.Find(PptRecordType.InteractiveInfoAtom);
        if (atom is null || atom.Payload.Length < 11)
        {
            return false;
        }

        // InteractiveInfoAtom: soundIdRef(4), exHyperlinkIdRef(4), action(1), oleVerb(1), jump(1), flags(1), hyperlinkType(1).
        ReadOnlySpan<byte> p = atom.Payload.Span;
        uint link = BitConverter.ToUInt32(p[4..8]);
        int action = p[8];
        int jump = p[10];
        switch (action)
        {
            case 3:
                string? jumpAction = jump switch
                {
                    1 => "ppaction://hlinkshowjump?jump=nextslide",
                    2 => "ppaction://hlinkshowjump?jump=previousslide",
                    3 => "ppaction://hlinkshowjump?jump=firstslide",
                    4 => "ppaction://hlinkshowjump?jump=lastslide",
                    5 => "ppaction://hlinkshowjump?jump=lastslideviewed",
                    6 => "ppaction://hlinkshowjump?jump=endshow",
                    _ => null,
                };
                if (jumpAction is null)
                {
                    return false;
                }

                actionAttrs = " action=\"" + jumpAction + "\"";
                return true;
            case 4 when page.Conv.P.Hyperlinks.TryGetValue(link, out (string Target, string Name) target):
                return AddTarget(target.Target, page, out relId, out actionAttrs);
            default:
                return false;
        }
    }

    private static bool AddTarget(string target, PageOut page, out string relId, out string actionAttrs)
    {
        relId = "";
        actionAttrs = "";
        if (target.Length == 0)
        {
            return false;
        }

        // [MS-PPT] §2.10.? : a link inside the presentation is "slideId,slideNumber,slideTitle".
        string[] parts = target.Split(',', 3);
        if (parts.Length == 3 && uint.TryParse(parts[0], out uint slideId) && int.TryParse(parts[1], out _))
        {
            string? slidePart = page.Conv.SlidePartOf(slideId);
            if (slidePart is not null)
            {
                relId = page.AddRel(Rel.Slide, "../slides/" + slidePart);
                actionAttrs = " action=\"ppaction://hlinksldjump\"";
                return true;
            }

            return false;
        }

        relId = page.AddRel(Rel.Hyperlink, target, RelationshipTargetMode.External);
        return true;
    }
}
