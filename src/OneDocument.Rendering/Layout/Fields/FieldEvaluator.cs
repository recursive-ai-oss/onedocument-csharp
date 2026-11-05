using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Properties;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Layout;

// The value of each field type layout knows. A null result means "keep the result stored in the file". Which fields are computed by
// default and which only when the caller asks for every field to be updated is stated in docs/layout-fields.md; the split follows what
// depends on the page (always computed) and what Word recomputes only when it updates fields before printing.
internal static class FieldEvaluator
{
    internal const string SourceMissing = "Error! Reference source not found.";
    private const string NoStyleText = "Error! No text of specified style in document.";

    public static string? Evaluate(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        switch (f.Type)
        {
            case "PAGE":
                return Page(site, f);
            case "NUMPAGES":
                env.UsedFacts = true;
                return NumberText(f, Math.Max(1, (env.Facts?.Totals ?? site.Page?.Totals)?.NumPages ?? 1), null);
            case "SECTIONPAGES":
                return SectionPages(site, f);
            case "SECTION":
                return NumberText(f, site.Page?.SectionNumber ?? env.Model.SectionOf(site.ParagraphIndex) + 1, null);
            case "PAGEREF":
                return PageRef(site, f);
            case "NOTEREF":
                return NoteRef(site, f);
            case "REF":
                return Ref(site, f);
            case "STYLEREF":
                return StyleRef(site, f);
            case "SEQ":
                return site.Info is { SeqValid: true } seq ? SeqText(seq, f) : null;
            case "ODXEPAGES":
                return IndexPages.Evaluate(site, f);
        }

        if (!env.Update)
        {
            return null;
        }

        switch (f.Type)
        {
            case "DATE":
            case "TIME":
                return Now(site, f);
            case "CREATEDATE":
                return StoredDate(site, f, site.Env.Doc.Properties.Core.Created, "M/d/yyyy h:mm AM/PM", missing: null);
            case "SAVEDATE":
                return StoredDate(site, f, site.Env.Doc.Properties.Core.Modified, "M/d/yyyy h:mm AM/PM", missing: null);
            case "PRINTDATE":
                return StoredDate(site, f, site.Env.Doc.Properties.Core.LastPrinted, "M/d/yyyy h:mm AM/PM", missing: "0/0/0000 0:00:00 AM");
            case "AUTHOR":
                return Property(f, env.Doc.Properties.Core.Creator);
            case "TITLE":
                return Property(f, env.Doc.Properties.Core.Title);
            case "SUBJECT":
                return Property(f, env.Doc.Properties.Core.Subject);
            case "KEYWORDS":
                return Property(f, env.Doc.Properties.Core.Keywords);
            case "COMMENTS":
                return Property(f, env.Doc.Properties.Core.Description);
            case "LASTSAVEDBY":
                return Property(f, env.Doc.Properties.Core.LastModifiedBy);
            case "REVNUM":
                return Property(f, env.Doc.Properties.Core.Revision);
            case "FILENAME":
                return FileName(env, f);
            case "NUMWORDS":
                return NumberText(f, env.Doc.Properties.Extended.Words ?? CountWords(env.Model), null);
            case "NUMCHARS":
                return NumberText(f, env.Doc.Properties.Extended.Characters ?? CountCharacters(env.Model), null);
            case "DOCPROPERTY":
                return DocProperty(site, f);
            case "IF":
                return If(f);
            case "=":
                return FormulaField.Evaluate(site, f);
            case "SYMBOL":
                return Symbol(f);
            case "QUOTE":
                return Finish(string.Concat(f.Arguments), f);
            default:
                return null;
        }
    }

    // --- page numbers ------------------------------------------------------------------------------------------------------------

    // ECMA-376 Part 1 §17.16.5.44 (PAGE): the number of the page the field is on, in the section's page-number format unless \* says otherwise.
    private static string? Page(FieldSite site, FieldInstruction f)
    {
        if (site.Page is { } state)
        {
            return NumberText(f, state.Printed, state.NumberFormat);
        }

        FieldEnv env = site.Env;
        env.UsedFacts = true;
        if (env.Facts is not { } facts || site.ParagraphIndex < 0)
        {
            return null;
        }

        int page = facts.PageOf(env.Model, site.ParagraphIndex, site.Info?.Offset ?? 0);
        return page < 0 ? null : NumberText(f, facts.Pages[page].Printed, facts.Pages[page].Format);
    }

    private static string? SectionPages(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        env.UsedFacts = true;
        PageTotals? totals = env.Facts?.Totals ?? site.Page?.Totals;
        int section = site.Page is { } p ? p.SectionNumber - 1 : env.Model.SectionOf(site.ParagraphIndex);
        int count = totals is not null && section >= 0 && section < totals.SectionPages.Length ? totals.SectionPages[section] : 1;
        return NumberText(f, Math.Max(1, count), null);
    }

    // ECMA-376 Part 1 §17.16.5.45 (PAGEREF): the page number of a bookmark, in the format of the page it is on; \p gives "above" or "below".
    private static string? PageRef(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        env.UsedFacts = true;
        if (f.FirstArgument is not { } name)
        {
            return null;
        }

        BookmarkInfo? bm = env.Model.Bookmark(name);
        if (bm is null)
        {
            return Missing(site, name);
        }

        FieldFacts? known = env.Facts;
        int page = known is null ? -1 : known.PageOf(env.Model, bm.StartParagraph, bm.StartOffset);
        if (f.HasSwitch("p"))
        {
            // The field's position relative to the bookmark: "above" or "below" on the same page, "on page N" on another.
            int own = known is null || site.ParagraphIndex < 0 ? -1 : known.PageOf(env.Model, site.ParagraphIndex, site.Info?.Offset ?? 0);
            if (known is not null && page >= 0 && own >= 0 && page != own)
            {
                return "on page " + NumberText(f, known.Pages[page].Printed, known.Pages[page].Format);
            }

            return Finish(Relative(site, bm) ?? string.Empty, f);
        }

        if (known is null)
        {
            return null;
        }

        return page < 0 ? null : NumberText(f, known.Pages[page].Printed, known.Pages[page].Format);
    }

    // ECMA-376 Part 1 §17.16.5.40 (NOTEREF): the mark of the note whose reference sits in the bookmark.
    private static string? NoteRef(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        if (f.FirstArgument is not { } name)
        {
            return null;
        }

        BookmarkInfo? bm = env.Model.Bookmark(name);
        if (bm is null)
        {
            return Missing(site, name);
        }

        if (f.HasSwitch("p"))
        {
            return Finish(Relative(site, bm) ?? string.Empty, f);
        }

        if (bm.NoteKind is not { } kind)
        {
            return env.Update ? SourceMissing : null;
        }

        if (env.Model.NotesRestartEachPage(kind))
        {
            site.Ctx.NoteOnce("layout.noteref-page-restart", "layout.noteref-page-restart", "Notes restart on every page; NOTEREF numbers count through the document.", site.Paragraph);
        }

        return env.Model.TryNoteNumber(kind, bm.NoteId, out string text) ? Finish(text, f) : null;
    }

    // ECMA-376 Part 1 §17.16.5.51 (REF): the text of a bookmark, or with \n, \r and \w its paragraph number; \p gives "above" or "below".
    private static string? Ref(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        if (f.FirstArgument is not { } name)
        {
            return null;
        }

        BookmarkInfo? bm = env.Model.Bookmark(name);
        if (bm is null)
        {
            return Missing(site, name);
        }

        bool numbered = f.HasSwitch("n") || f.HasSwitch("r") || f.HasSwitch("w");
        if (f.HasSwitch("p") && !numbered)
        {
            return Finish(Relative(site, bm) ?? string.Empty, f);
        }

        if (numbered)
        {
            string? number = ParagraphNumber(env, bm.StartParagraph, f.HasSwitch("w") ? 2 : f.HasSwitch("r") ? 1 : 0, site.ParagraphIndex);
            string text = number is null ? "0" : Finish(number, f);
            // With \p the position follows the number (ECMA-376 Part 1 §17.16.5.51).
            return f.HasSwitch("p") && Relative(site, bm) is { } position ? text + " " + position : text;
        }

        return Finish(bm.Text.ToString().TrimEnd('\n'), f);
    }

    private static string? Missing(FieldSite site, string name)
    {
        site.Ctx.NoteOnce("layout.field-source-missing:" + name, "layout.field-source-missing", $"A field refers to the bookmark \"{name}\", which the document does not have.", site.Paragraph);
        return site.Env.Update ? SourceMissing : null;
    }

    // "above" or "below": whether the bookmark comes before or after the field in the document.
    private static string? Relative(FieldSite site, BookmarkInfo bm)
    {
        if (site.Info is not { } info || info.ParagraphIndex < 0)
        {
            return null;
        }

        bool before = bm.StartParagraph < info.ParagraphIndex || (bm.StartParagraph == info.ParagraphIndex && bm.StartOffset <= info.Offset);
        return before ? "above" : "below";
    }

    // ECMA-376 Part 1 §17.16.5.51 (REF \n, \r, \w): the number of the paragraph without its trailing period. \r adds the levels the field's own
    // paragraph does not share, \w every level above it; the levels join as they are written ("1." "a." "i." read "1.a.i").
    internal static string? ParagraphNumber(FieldEnv env, int paragraph, int context, int fromParagraph)
    {
        if (paragraph < 0 || paragraph >= env.Model.Paragraphs.Count)
        {
            return null;
        }

        List<string>? chain = NumberChain(env, paragraph);
        if (chain is null)
        {
            return null;
        }

        if (context == 0)
        {
            return Strip(chain[^1]);
        }

        if (context == 1 && fromParagraph >= 0 && NumberChain(env, fromParagraph) is { } own)
        {
            int shared = 0;
            while (shared < chain.Count - 1 && shared < own.Count && chain[shared] == own[shared])
            {
                shared++;
            }

            return Strip(string.Concat(chain.Skip(shared)));
        }

        return Strip(string.Concat(chain));
    }

    // The labels of the paragraph's list levels from the outermost to its own. A level whose text already repeats the higher levels (1.2.3)
    // stands alone.
    private static List<string>? NumberChain(FieldEnv env, int paragraph)
    {
        Paragraph p = env.Model.Paragraphs[paragraph];
        int at = env.LabelIndex(p);
        if (at < 0)
        {
            return null;
        }

        ListLabel label = env.Labels[at];
        var chain = new List<string> { label.Text };
        if (label.Definition.Text.Contains("%1", StringComparison.Ordinal) && label.Level > 0)
        {
            return chain;
        }

        int level = label.Level;
        for (int i = at - 1; i >= 0 && level > 0; i--)
        {
            ListLabel l = env.Labels[i];
            if (l.NumberingId == label.NumberingId && l.Level < level)
            {
                chain.Insert(0, l.Text);
                level = l.Level;
            }
        }

        return chain;
    }

    private static string Strip(string label) => label.TrimEnd('.');

    // ECMA-376 Part 1 §17.16.5.59 (STYLEREF): the nearest text of a paragraph or character style. In a header or footer: the first (\l: last)
    // on the page, else the nearest before the page, else the nearest after it. In the body: the nearest before the field, else after it
    // (\l: the nearest after, else before).
    private static string? StyleRef(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        if (f.FirstArgument is not { } styleName)
        {
            return null;
        }

        HashSet<string> ids = env.StyleIdsFor(styleName);
        FieldModel model = env.Model;
        bool last = f.HasSwitch("l");
        bool characterStyle = ids.Any(id => env.Doc.Styles.Find(id)?.Type == RecursiveAI.OneDocument.Documents.Styles.StyleType.Character);
        string? Candidate(int idx)
        {
            if (idx < 0 || idx >= model.Paragraphs.Count)
            {
                return null;
            }

            return characterStyle ? CharacterStyleText(model.Paragraphs[idx], ids, last) : StyleMatches(model, ids, idx) ? model.Paragraphs[idx].Text.Replace('\n', ' ').Trim() : null;
        }

        int found = -1;
        string? text = null;
        bool Take(int idx)
        {
            if (Candidate(idx) is { } t)
            {
                found = idx;
                text = t;
                return true;
            }

            return false;
        }

        if (site.Page is { } state)
        {
            env.UsedFacts = true;
            if (env.Facts is not { } facts)
            {
                return null;
            }

            int page = Math.Clamp(state.Physical - 1, 0, facts.ByPage.Length - 1);
            List<int> here = facts.ByPage[page];
            for (int k = 0; k < here.Count && found < 0; k++)
            {
                Take(last ? here[here.Count - 1 - k] : here[k]);
            }

            // \l searches from the bottom of the page to the beginning of the document, then to the end; the default from the top of the page
            // back to the beginning, then from the bottom to the end.
            for (int pg = page - 1; pg >= 0 && found < 0; pg--)
            {
                List<int> list = facts.ByPage[pg];
                for (int k = list.Count - 1; k >= 0 && found < 0; k--)
                {
                    Take(list[k]);
                }
            }

            for (int pg = page + 1; pg < facts.ByPage.Length && found < 0; pg++)
            {
                foreach (int idx in facts.ByPage[pg])
                {
                    if (Take(idx))
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            int from = site.ParagraphIndex;
            if (from < 0)
            {
                return null;
            }

            if (last)
            {
                for (int i = from + 1; i < model.Paragraphs.Count && found < 0; i++)
                {
                    Take(i);
                }

                for (int i = from; i >= 0 && found < 0; i--)
                {
                    Take(i);
                }
            }
            else
            {
                for (int i = from; i >= 0 && found < 0; i--)
                {
                    Take(i);
                }

                for (int i = from + 1; i < model.Paragraphs.Count && found < 0; i++)
                {
                    Take(i);
                }
            }
        }

        if (found < 0)
        {
            return NoStyleText;
        }

        if (f.HasSwitch("n") || f.HasSwitch("r") || f.HasSwitch("w"))
        {
            string? number = ParagraphNumber(env, found, f.HasSwitch("w") ? 2 : f.HasSwitch("r") ? 1 : 0, site.ParagraphIndex);
            return number is null ? "0" : Finish(number, f);
        }

        if (f.HasSwitch("p") && site.Info is not null)
        {
            return found < site.ParagraphIndex ? "above" : "below";
        }

        return Finish(text ?? string.Empty, f);
    }

    // The first (or last) stretch of runs a paragraph formats with a character style.
    private static string? CharacterStyleText(Paragraph p, HashSet<string> ids, bool last)
    {
        string? result = null;
        var current = new System.Text.StringBuilder();
        foreach (Run r in p.Runs())
        {
            if (r.StyleId is { } id && ids.Contains(id))
            {
                current.Append(r.Text);
                continue;
            }

            if (current.Length > 0)
            {
                result = current.ToString().Trim();
                if (!last)
                {
                    return result;
                }

                current.Clear();
            }
        }

        return current.Length > 0 ? current.ToString().Trim() : result;
    }

    private static bool StyleMatches(FieldModel model, HashSet<string> ids, int index) =>
        index >= 0 && index < model.Paragraphs.Count && model.Paragraphs[index].ResolvedProperties.StyleId is { } id && ids.Contains(id);

    // ECMA-376 Part 1 §17.16.5.56 (SEQ).
    internal static string SeqText(FieldInfo info, FieldInstruction f) => info.SeqHidden ? string.Empty : NumberText(f, info.SeqValue, null);

    internal static string SeqText(FieldInfo info) => SeqText(info, info.Parsed);

    // --- dates, properties, document facts -----------------------------------------------------------------------------------------

    private static string? Now(FieldSite site, FieldInstruction f)
    {
        TimeProvider? clock = site.Env.Options.Clock;
        if (clock is null)
        {
            return null; // layout never reads the system clock by itself
        }

        DateTime now = clock.GetLocalNow().DateTime;
        string defaultPicture = f.Type == "DATE" ? "M/d/yyyy" : "h:mm AM/PM";
        return Finish(WordPicture.FormatDate(now, f.Switch("@")?.Value ?? defaultPicture), f);
    }

    private static string? StoredDate(FieldSite site, FieldInstruction f, DateTimeOffset? value, string defaultPicture, string? missing)
    {
        if (value is null)
        {
            return missing;
        }

        TimeProvider? clock = site.Env.Options.Clock;
        DateTime local = clock is null ? value.Value.UtcDateTime : TimeZoneInfo.ConvertTime(value.Value, clock.LocalTimeZone).DateTime;
        return Finish(WordPicture.FormatDate(local, f.Switch("@")?.Value ?? defaultPicture), f);
    }

    private static string? Property(FieldInstruction f, string? value) => Finish(f.FirstArgument ?? value ?? string.Empty, f);

    private static string? FileName(FieldEnv env, FieldInstruction f)
    {
        string? path = env.Doc.FilePath;
        if (path is null)
        {
            return null;
        }

        return Finish(f.HasSwitch("p") ? path : Path.GetFileName(path), f);
    }

    private static long CountWords(FieldModel model)
    {
        long n = 0;
        foreach (Paragraph p in model.Paragraphs)
        {
            bool inWord = false;
            foreach (char c in p.Text)
            {
                bool space = char.IsWhiteSpace(c);
                if (!space && !inWord)
                {
                    n++;
                }

                inWord = !space;
            }
        }

        return n;
    }

    private static long CountCharacters(FieldModel model)
    {
        long n = 0;
        foreach (Paragraph p in model.Paragraphs)
        {
            foreach (char c in p.Text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    n++;
                }
            }
        }

        return n;
    }

    // ECMA-376 Part 1 §17.16.5.14 (DOCPROPERTY): a built-in property by its display name, else a custom property.
    private static string? DocProperty(FieldSite site, FieldInstruction f)
    {
        if (f.FirstArgument is not { } name)
        {
            return null;
        }

        FieldEnv env = site.Env;
        CoreProperties core = env.Doc.Properties.Core;
        ExtendedProperties ext = env.Doc.Properties.Extended;
        string? text = name.ToLowerInvariant() switch
        {
            "title" => core.Title,
            "subject" => core.Subject,
            "author" => core.Creator,
            "keywords" => core.Keywords,
            "comments" => core.Description,
            "last author" or "lastauthor" => core.LastModifiedBy,
            "revision number" => core.Revision,
            "category" => core.Category,
            "content status" => core.ContentStatus,
            "company" => ext.Company,
            "manager" => ext.Manager,
            "template" => ext.Template,
            "pages" => (env.Facts?.Totals.NumPages ?? ext.Pages)?.ToString(CultureInfo.InvariantCulture),
            "words" => (ext.Words ?? CountWords(env.Model)).ToString(CultureInfo.InvariantCulture),
            "characters" => (ext.Characters ?? CountCharacters(env.Model)).ToString(CultureInfo.InvariantCulture),
            "creation date" => StoredDate(site, f, core.Created, "M/d/yyyy h:mm AM/PM", null),
            "last save time" => StoredDate(site, f, core.Modified, "M/d/yyyy h:mm AM/PM", null),
            "last print date" => StoredDate(site, f, core.LastPrinted, "M/d/yyyy h:mm AM/PM", "0/0/0000 0:00:00 AM"),
            _ => null,
        };
        if (text is null && env.Doc.Properties.Custom.Find(name) is { } custom)
        {
            text = custom.Text;
        }

        return text is null ? "Error! Unknown document property name." : Finish(text, f);
    }

    // ECMA-376 Part 1 §17.16.5.26 (IF): compare two expressions, show the first text when the comparison holds, else the second.
    private static string? If(FieldInstruction f)
    {
        IReadOnlyList<string> a = f.Arguments;
        if (a.Count < 4 || !(a[1] is "=" or "<>" or "<" or ">" or "<=" or ">="))
        {
            return null;
        }

        bool holds = MergePass.Compare(a[0], a[1], a[2]);
        return Finish(holds ? a[3] : (a.Count > 4 ? a[4] : string.Empty), f);
    }

    // ECMA-376 Part 1 §17.16.5.61 (SYMBOL): the character with the given code, decimal or 0x hex; \u says it is a Unicode value.
    private static string? Symbol(FieldInstruction f)
    {
        if (f.FirstArgument is not { } code)
        {
            return null;
        }

        bool ok = code.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(code.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)
            : int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        if (!ok || v is < 0 or > 0x10FFFF || v is >= 0xD800 and <= 0xDFFF)
        {
            return null;
        }

        string font = f.Switch("f")?.Value ?? string.Empty;
        bool symbolFont = !f.HasSwitch("u") && v <= 0xFF && (font.Equals("Symbol", StringComparison.OrdinalIgnoreCase) || font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase));
        return symbolFont ? SymbolMap.Map(font, 0xF000 + v) : char.ConvertFromUtf32(v);
    }

    // --- switches ----------------------------------------------------------------------------------------------------------------

    // The text of a whole number with the field's \* format and \# picture (ECMA-376 Part 1 §17.16.4). The format of the page or the
    // counter is the default; \* Roman, roman, ALPHABETIC, alphabetic, Arabic, ArabicDash, CardText, OrdText, Ordinal and Hex replace it.
    internal static string NumberText(FieldInstruction f, long value, string? defaultFormat)
    {
        string? format = defaultFormat;
        bool lower = false;
        string? dash = null;
        foreach (FieldSwitch s in f.Switches)
        {
            if (s.Name != "*" || s.Value is not { } v)
            {
                continue;
            }

            switch (v)
            {
                case "Roman" or "ROMAN": format = "upperRoman"; break;
                case "roman": format = "lowerRoman"; break;
                case "ALPHABETIC" or "Alphabetic": format = "upperLetter"; break;
                case "alphabetic": format = "lowerLetter"; break;
                case "Arabic" or "ARABIC" or "arabic": format = "decimal"; break;
                case "ArabicDash": dash = "- " + value.ToString(CultureInfo.InvariantCulture) + " -"; break;
                case "Ordinal": format = "ordinal"; break;
                case "CardText": format = "cardinalText"; lower = true; break;
                case "OrdText": format = "ordinalText"; lower = true; break;
                case "Hex": format = "hex"; break;
            }
        }

        string text;
        if (f.Switch("#")?.Value is { } picture)
        {
            text = WordPicture.FormatNumber(value, picture);
        }
        else if (dash is not null)
        {
            text = dash;
        }
        else
        {
            text = NumberFormatter.Format(format, value).Text;
            if (lower)
            {
                text = text.ToLowerInvariant();
            }
        }

        return Finish(text, f);
    }

    // The case formats of \* (Upper, Lower, FirstCap, Caps); the number formats were applied before.
    internal static string Finish(string text, FieldInstruction f)
    {
        foreach (FieldSwitch s in f.Switches)
        {
            if (s.Name == "*" && s.Value is { } v && v.ToLowerInvariant() is "upper" or "lower" or "firstcap" or "caps")
            {
                text = WordPicture.ApplyCase(text, v);
            }
        }

        return text;
    }
}
