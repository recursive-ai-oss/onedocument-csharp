using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Packaging;
using static RecursiveAI.OneDocument.Ppt.Binary.PptXml;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>
/// Converts a PowerPoint 97-2003 file into a PresentationML package. The result is an ordinary in-memory package that the presentation hub
/// opens as typed views; what has no equivalent goes to the loss report and, with its record offset, to the defects.
/// </summary>
public static partial class PptConverter
{
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";
    private const string PmlType = "application/vnd.openxmlformats-officedocument.presentationml.";
    private const string GroupHead = "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    /// <summary>One layout to write: the master it belongs to and the page it is modelled on.</summary>
    private sealed class LayoutOut
    {
        public int Number { get; init; }

        public required string Name { get; init; }

        public required string Type { get; init; }

        public PptSlide? Prototype { get; init; }

        public PptMaster? TitleMaster { get; init; }

        public bool ShowMasterShapes { get; init; } = true;
    }

    private sealed class MasterOut
    {
        public int Number { get; init; }

        public PptMaster? Source { get; init; }

        public Dictionary<string, LayoutOut> Layouts { get; } = [];
    }

    /// <summary>
    /// Builds the PresentationML package for an opened PowerPoint file.
    /// </summary>
    /// <param name="file">The record-level file.</param>
    /// <param name="loss">Receives everything that did not map; may be shared with other conversions.</param>
    /// <param name="cancellationToken">Cancels the conversion between pages.</param>
    /// <returns>The package, which the caller owns.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OpcPackage Convert(PptFile file, ILossSink loss, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(loss);
        PptPresentation presentation = PptPresentation.Read(file);
        OpcPackage package = OpcPackage.Create(file.Context.Limits);
        try
        {
            Build(presentation, package, loss, cancellationToken);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private static void Build(PptPresentation p, OpcPackage package, ILossSink lossSink, CancellationToken ct)
    {
        var conv = new PptConversion(p, package, lossSink);
        List<PptMaster> mains = p.Masters.Where(m => !m.IsTitleMaster).ToList();
        var masters = new List<MasterOut>();
        if (mains.Count == 0)
        {
            masters.Add(new MasterOut { Number = 1, Source = null });
            if (p.Slides.Count > 0)
            {
                p.File.ReadContext.Repair("ppt.master-missing", "The file has no main master; a plain master is written.", p.File.DocumentContainer?.Offset ?? 0);
            }
        }
        else
        {
            for (int i = 0; i < mains.Count; i++)
            {
                masters.Add(new MasterOut { Number = i + 1, Source = mains[i] });
            }
        }

        MasterOut MasterFor(uint masterIdRef, out PptMaster? title)
        {
            title = p.Masters.FirstOrDefault(m => m.MasterId == masterIdRef && m.IsTitleMaster);
            uint mainId = title is not null ? title.MainMasterId : masterIdRef;
            MasterOut? found = masters.FirstOrDefault(m => m.Source is not null && m.Source.MasterId == mainId);
            if (found is null && title is not null)
            {
                // A title master that names no main master belongs to the main master that precedes it in the list.
                int at = p.Masters.ToList().IndexOf(title);
                PptMaster? before = p.Masters.Take(at).LastOrDefault(m => !m.IsTitleMaster);
                found = masters.FirstOrDefault(m => m.Source == before);
            }

            return found ?? masters[0];
        }

        // Comment authors are numbered over the whole presentation before any slide is written.
        var commentsBySlide = new Dictionary<PptSlide, IReadOnlyList<PptComment>>();
        var authors = new List<(string Name, string Initials, int LastIndex)>();
        foreach (PptSlide slide in p.Slides)
        {
            IReadOnlyList<PptComment> comments = PptDeep.Comments(slide.Container, p.File.ReadContext);
            if (comments.Count == 0)
            {
                continue;
            }

            commentsBySlide[slide] = comments;
            foreach (PptComment c in comments)
            {
                int at = authors.FindIndex(a => a.Name == c.Author);
                if (at < 0)
                {
                    authors.Add((c.Author, c.Initials, c.Index));
                }
                else
                {
                    authors[at] = authors[at] with { LastIndex = Math.Max(authors[at].LastIndex, c.Index) };
                }
            }
        }

        // Slides get their part numbers first so hyperlinks between slides can name them.
        for (int i = 0; i < p.Slides.Count; i++)
        {
            conv.SlideParts[p.Slides[i].SlideId] = "slide" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml";
        }

        // Layouts: one per distinct slide layout used with each master, modelled on the first slide that uses it.
        int layoutCounter = 0;
        var slideLayout = new Dictionary<PptSlide, (MasterOut Master, LayoutOut Layout)>();
        foreach (PptSlide slide in p.Slides)
        {
            MasterOut m = MasterFor(slide.MasterIdRef, out PptMaster? title);
            string key = title is not null ? "title:" + title.MasterId : "g:" + slide.Layout + ":" + string.Join(',', slide.LayoutPlaceholders);
            if (!m.Layouts.TryGetValue(key, out LayoutOut? layout))
            {
                layout = new LayoutOut
                {
                    Number = ++layoutCounter,
                    Name = title is not null ? "Title Slide" : LayoutName(slide.Layout),
                    Type = title is not null ? "title" : PptShapeTypes.LayoutType(slide.Layout),
                    Prototype = title is null ? slide : null,
                    TitleMaster = title,
                    ShowMasterShapes = title is null || (title.Flags & 1) != 0,
                };
                m.Layouts[key] = layout;
            }

            slideLayout[slide] = (m, layout);
        }

        foreach (MasterOut m in masters)
        {
            if (m.Layouts.Count == 0)
            {
                m.Layouts["blank"] = new LayoutOut { Number = ++layoutCounter, Name = "Blank", Type = "blank" };
            }
        }

        // Identifiers: masters and layouts share one number space from 2147483648; slides use the identifiers the file gave them.
        long nextId = 2147483648;
        var masterIds = new Dictionary<MasterOut, long>();
        var layoutIds = new Dictionary<LayoutOut, long>();
        foreach (MasterOut m in masters)
        {
            masterIds[m] = nextId++;
            foreach (LayoutOut l in m.Layouts.Values.OrderBy(l => l.Number))
            {
                layoutIds[l] = nextId++;
            }
        }

        int themeCount = 0;
        var presRels = new List<Relationship>();
        int relCounter = 0;
        string PresRel(string type, string target)
        {
            string id = "rId" + (++relCounter).ToString(CultureInfo.InvariantCulture);
            presRels.Add(new Relationship(id, type, target));
            return id;
        }

        var masterRelIds = new List<(MasterOut, string)>();
        foreach (MasterOut m in masters)
        {
            ct.ThrowIfCancellationRequested();
            int theme = ++themeCount;
            WriteMaster(conv, m, theme, layoutIds, masterIds[m]);
            masterRelIds.Add((m, PresRel(Rel.SlideMaster, "slideMasters/slideMaster" + m.Number.ToString(CultureInfo.InvariantCulture) + ".xml")));
        }

        string themeRel = PresRel(Rel.Theme, "theme/theme1.xml");

        // Notes master, handout master: written when the file has them or when a notes page needs one.
        bool anyNotes = p.NotesPages.Count > 0;
        string? notesMasterRel = null;
        if (anyNotes || p.NotesMaster is not null)
        {
            int theme = ++themeCount;
            WriteNotesMaster(conv, p.NotesMaster, theme);
            notesMasterRel = PresRel(Rel.NotesMaster, "notesMasters/notesMaster1.xml");
        }

        string? handoutRel = null;
        if (p.HandoutMaster is not null)
        {
            int theme = ++themeCount;
            WriteHandoutMaster(conv, p.HandoutMaster, theme);
            handoutRel = PresRel(Rel.HandoutMaster, "handoutMasters/handoutMaster1.xml");
        }

        var slideIds = new List<(uint Id, string Rel, bool Hidden)>();
        var usedIds = new HashSet<uint>();
        uint fallbackId = 256;
        for (int i = 0; i < p.Slides.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PptSlide slide = p.Slides[i];
            (MasterOut master, LayoutOut layout) = slideLayout[slide];
            WriteSlide(conv, slide, i + 1, master, layout, commentsBySlide.GetValueOrDefault(slide), authors);
            uint id = slide.SlideId;
            if (id < 256 || id > int.MaxValue || !usedIds.Add(id))
            {
                while (!usedIds.Add(fallbackId))
                {
                    fallbackId++;
                }

                id = fallbackId;
            }

            slideIds.Add((id, PresRel(Rel.Slide, "slides/slide" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml"), (slide.Transition is { } t && t.Payload.Length >= 12 && (t.UInt16At(10) & 4) != 0)));
        }

        if (p.HasVbaProject)
        {
            conv.Loss.Offset = p.File.DocumentContainer?.Offset ?? -1;
            conv.Loss.Report("ppt.vba-not-carried", "VBA project", 1, "The file's VBA project is stored in the file's own compressed OLE storage and is not carried into the converted presentation.");
        }

        int ole = p.ExternalObjects is null ? 0 : p.ExternalObjects.Children.Count(c => c.Type is PptRecordType.ExternalOleEmbed or PptRecordType.ExternalOleLink);
        ole -= conv.OleCarried;
        if (ole > 0)
        {
            conv.Loss.Offset = p.ExternalObjects!.Offset;
            conv.Loss.Report("ppt.ole-object-flattened", "OLE objects", ole, "OLE objects whose storage or picture is missing or unreadable are written as their preview pictures; the object data is not carried.");
        }

        if (authors.Count > 0)
        {
            var au = new StringBuilder("<p:cmAuthorLst " + Ns + ">");
            for (int i = 0; i < authors.Count; i++)
            {
                au.Append("<p:cmAuthor id=\"").Append(i).Append("\" name=\"").Append(Escape(authors[i].Name)).Append("\" initials=\"").Append(Escape(authors[i].Initials)).Append("\" lastIdx=\"").Append(authors[i].LastIndex).Append("\" clrIdx=\"").Append(i % 6).Append("\"/>");
            }

            au.Append("</p:cmAuthorLst>");
            package.AddPart("/ppt/commentAuthors.xml", PmlType + "commentAuthors+xml", Bytes(au.ToString()));
            PresRel(Rel.CommentAuthors, "commentAuthors.xml");
        }

        WritePresentation(conv, presRels, masterRelIds, masterIds, notesMasterRel, handoutRel, slideIds, themeRel);
        package.SetRelationships(null, [new Relationship("rId1", Rel.OfficeDocument, "ppt/presentation.xml")]);
        package.SetRelationships("/ppt/presentation.xml", presRels);
        ApplyProperties(p, package, conv);
        _ = themeRel;
    }

    private static string LayoutName(int layout) => layout switch
    {
        0 => "Title Slide",
        1 => "Title and Content",
        7 => "Title Only",
        8 => "Two Content",
        15 => "Content",
        16 => "Blank",
        _ => "Layout " + layout.ToString(CultureInfo.InvariantCulture),
    };

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(Prolog + xml);

    // ---- presentation.xml ----

    private static void WritePresentation(PptConversion conv, List<Relationship> rels, List<(MasterOut Master, string Rel)> masterRels, Dictionary<MasterOut, long> masterIds, string? notesMasterRel, string? handoutRel, List<(uint Id, string Rel, bool Hidden)> slides, string themeRel)
    {
        PptPresentation p = conv.P;
        var sb = new StringBuilder("<p:presentation " + Ns + " saveSubsetFonts=\"1\"");
        if (p.FirstSlideNumber != 1)
        {
            sb.Append(" firstSlideNum=\"").Append(p.FirstSlideNumber).Append('"');
        }

        sb.Append("><p:sldMasterIdLst>");
        foreach ((MasterOut m, string rel) in masterRels)
        {
            sb.Append("<p:sldMasterId id=\"").Append(masterIds[m]).Append("\" r:id=\"").Append(rel).Append("\"/>");
        }

        sb.Append("</p:sldMasterIdLst>");
        if (notesMasterRel is not null)
        {
            sb.Append("<p:notesMasterIdLst><p:notesMasterId r:id=\"").Append(notesMasterRel).Append("\"/></p:notesMasterIdLst>");
        }

        if (handoutRel is not null)
        {
            sb.Append("<p:handoutMasterIdLst><p:handoutMasterId r:id=\"").Append(handoutRel).Append("\"/></p:handoutMasterIdLst>");
        }

        if (slides.Count > 0)
        {
            sb.Append("<p:sldIdLst>");
            foreach ((uint id, string rel, _) in slides)
            {
                sb.Append("<p:sldId id=\"").Append(id).Append("\" r:id=\"").Append(rel).Append("\"/>");
            }

            sb.Append("</p:sldIdLst>");
        }

        sb.Append("<p:sldSz cx=\"").Append(Num(Emu(p.SlideWidth))).Append("\" cy=\"").Append(Num(Emu(p.SlideHeight))).Append("\"/>");
        sb.Append("<p:notesSz cx=\"").Append(Num(Emu(p.NotesWidth))).Append("\" cy=\"").Append(Num(Emu(p.NotesHeight))).Append("\"/>");
        var page = new PageOut(conv, PptColorScheme.Default, PptColorScheme.Default, "/ppt/presentation.xml");
        sb.Append("<p:defaultTextStyle>");
        p.DefaultTextStyles.TryGetValue(4, out PptMasterStyle? other);
        AppendLevels(sb, other, null, page, "a:lvl", "a:defPPr");
        sb.Append("</p:defaultTextStyle></p:presentation>");
        conv.Package.AddPart("/ppt/presentation.xml", PmlType + "presentation.main+xml", Bytes(sb.ToString()));
        _ = themeRel;
    }

    // Levels 1 to 5 of a text style as <a:lvlNpPr> elements; levels the style lacks fall back to the second style.
    private static void AppendLevels(StringBuilder sb, PptMasterStyle? style, PptMasterStyle? fallback, PageOut page, string tagPrefix, string? defaultElement = null)
    {
        _ = defaultElement;
        bool any = false;
        for (int level = 0; level < 5; level++)
        {
            (PptParagraphFormat Paragraph, PptCharFormat Character)? entry =
                style is not null && level < style.Levels.Count ? style.Levels[level]
                : fallback is not null && level < fallback.Levels.Count ? fallback.Levels[level]
                : null;
            if (entry is null)
            {
                continue;
            }

            any = true;
            string defRPr = PptTextWriter.RunProperties(entry.Value.Character, "", page, "a:defRPr");
            string tag = tagPrefix + (level + 1).ToString(CultureInfo.InvariantCulture) + "pPr";
            string pPr = PptTextWriter.ParagraphProperties(entry.Value.Paragraph, 0, page, null, tag, defRPr);
            sb.Append(pPr.Length > 0 ? pPr : "<" + tag + ">" + defRPr + "</" + tag + ">");
        }

        if (!any)
        {
            sb.Append("<a:lvl1pPr><a:defRPr sz=\"1800\"/></a:lvl1pPr>");
        }
    }

    // ---- theme ----

    private static string ThemeXml(PptConversion conv, PptColorScheme scheme, PptMaster? master)
    {
        (byte r, byte g, byte b) C(int i) => scheme.Colors[i];
        string Clr(int i) => "<a:srgbClr val=\"" + Hex(C(i).r, C(i).g, C(i).b) + "\"/>";
        string major = FontOf(conv, master, 0);
        string minor = FontOf(conv, master, 1);
        const string Solid = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
        string fill3 = Solid + Solid + Solid;
        string line3 = "<a:ln w=\"6350\">" + Solid + "</a:ln><a:ln w=\"12700\">" + Solid + "</a:ln><a:ln w=\"19050\">" + Solid + "</a:ln>";
        const string Effect = "<a:effectStyle><a:effectLst/></a:effectStyle>";
        return "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\"><a:themeElements><a:clrScheme name=\"Imported\">" +
            "<a:dk1>" + Clr(1) + "</a:dk1><a:lt1>" + Clr(0) + "</a:lt1><a:dk2>" + Clr(2) + "</a:dk2><a:lt2>" + Clr(3) + "</a:lt2>" +
            "<a:accent1>" + Clr(4) + "</a:accent1><a:accent2>" + Clr(5) + "</a:accent2><a:accent3>" + Clr(6) + "</a:accent3><a:accent4>" + Clr(7) + "</a:accent4>" +
            "<a:accent5>" + Clr(4) + "</a:accent5><a:accent6>" + Clr(5) + "</a:accent6><a:hlink>" + Clr(6) + "</a:hlink><a:folHlink>" + Clr(7) + "</a:folHlink></a:clrScheme>" +
            "<a:fontScheme name=\"Imported\"><a:majorFont><a:latin typeface=\"" + Escape(major) + "\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"" + Escape(minor) + "\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
            "<a:fmtScheme name=\"Imported\"><a:fillStyleLst>" + fill3 + "</a:fillStyleLst><a:lnStyleLst>" + line3 + "</a:lnStyleLst><a:effectStyleLst>" + Effect + Effect + Effect +
            "</a:effectStyleLst><a:bgFillStyleLst>" + fill3 + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    }

    private static string FontOf(PptConversion conv, PptMaster? master, int textType)
    {
        PptMasterStyle? style = null;
        master?.TextStyles.TryGetValue(textType, out style);
        if (style is null)
        {
            conv.P.DefaultTextStyles.TryGetValue(textType, out style);
        }

        if (style is { Levels.Count: > 0 } && style.Levels[0].Character.Has(16))
        {
            return conv.Font(style.Levels[0].Character.FontRef);
        }

        return "Arial";
    }

    private static string WriteTheme(PptConversion conv, int number, PptColorScheme scheme, PptMaster? master)
    {
        string name = "/ppt/theme/theme" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
        conv.Package.AddPart(name, "application/vnd.openxmlformats-officedocument.theme+xml", Bytes(ThemeXml(conv, scheme, master)));
        return "../theme/theme" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
    }

    private const string ClrMap = "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>";

    // ---- masters and layouts ----

    private static void WriteMaster(PptConversion conv, MasterOut m, int themeNumber, Dictionary<LayoutOut, long> layoutIds, long masterId)
    {
        _ = masterId;
        PptMaster? src = m.Source;
        PptColorScheme scheme = src?.ColorScheme ?? PptColorScheme.Default;
        string themeTarget = WriteTheme(conv, themeNumber, scheme, src);
        string partName = "/ppt/slideMasters/slideMaster" + m.Number.ToString(CultureInfo.InvariantCulture) + ".xml";
        var page = new PageOut(conv, scheme, scheme, partName);
        var sb = new StringBuilder("<p:sldMaster " + Ns + "><p:cSld>");
        conv.Loss.Offset = src?.Container.Offset ?? -1;
        ArtDrawing? drawing = src?.Drawing;
        sb.Append(BackgroundXml(conv, page, drawing, "<p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef>"));
        sb.Append("<p:spTree>").Append(GroupHead);
        if (drawing is not null)
        {
            new PptShapeWriter(conv, page, sb, PageKind.Master, []).WriteShapes(drawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld>").Append(ClrMap).Append("<p:sldLayoutIdLst>");
        var rels = new List<Relationship>();
        foreach (LayoutOut l in m.Layouts.Values.OrderBy(l => l.Number))
        {
            string rid = "rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            rels.Add(new Relationship(rid, Rel.SlideLayout, "../slideLayouts/slideLayout" + l.Number.ToString(CultureInfo.InvariantCulture) + ".xml"));
            sb.Append("<p:sldLayoutId id=\"").Append(layoutIds[l]).Append("\" r:id=\"").Append(rid).Append("\"/>");
        }

        sb.Append("</p:sldLayoutIdLst><p:txStyles>");
        PptMasterStyle? title = StyleOf(conv, src, 0);
        PptMasterStyle? body = StyleOf(conv, src, 1);
        PptMasterStyle? other = StyleOf(conv, src, 4);
        sb.Append("<p:titleStyle>");
        AppendLevels(sb, title, null, page, "a:lvl");
        sb.Append("</p:titleStyle><p:bodyStyle>");
        AppendLevels(sb, body, null, page, "a:lvl");
        sb.Append("</p:bodyStyle><p:otherStyle>");
        AppendLevels(sb, other, null, page, "a:lvl");
        sb.Append("</p:otherStyle></p:txStyles></p:sldMaster>");
        rels.Add(new Relationship("rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture), Rel.Theme, themeTarget));
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "slideMaster+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
        foreach (LayoutOut l in m.Layouts.Values.OrderBy(l => l.Number))
        {
            WriteLayout(conv, m, l, scheme);
        }
    }

    // The shapes wrote their relationship ids as rIdP1, rIdP2…; once the part's fixed relationships are known they are numbered after them.
    private static void FixRelationshipIds(StringBuilder sb, int offset)
    {
        string text = System.Text.RegularExpressions.Regex.Replace(
            sb.ToString(),
            "\"rIdP([0-9]+)\"",
            match => "\"rId" + (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + offset).ToString(CultureInfo.InvariantCulture) + "\"");
        sb.Clear().Append(text);
    }

    private static PptMasterStyle? StyleOf(PptConversion conv, PptMaster? master, int type)
    {
        if (master is not null && master.TextStyles.TryGetValue(type, out PptMasterStyle? s))
        {
            return s;
        }

        return conv.P.DefaultTextStyles.GetValueOrDefault(type);
    }

    private static string BackgroundXml(PptConversion conv, PageOut page, ArtDrawing? drawing, string fallback)
    {
        if (drawing?.Background is { } bg)
        {
            string fill = new PptShapeWriter(conv, page, new StringBuilder(), PageKind.Master, []).BackgroundFill(bg);
            if (fill.Length > 0)
            {
                return "<p:bg><p:bgPr>" + fill + "<a:effectLst/></p:bgPr></p:bg>";
            }
        }

        return fallback.Length == 0 ? "" : "<p:bg>" + fallback + "</p:bg>";
    }

    private static void WriteLayout(PptConversion conv, MasterOut m, LayoutOut l, PptColorScheme scheme)
    {
        string partName = "/ppt/slideLayouts/slideLayout" + l.Number.ToString(CultureInfo.InvariantCulture) + ".xml";
        var page = new PageOut(conv, scheme, scheme, partName);
        var sb = new StringBuilder("<p:sldLayout " + Ns + " type=\"" + l.Type + "\" preserve=\"1\"" + (l.ShowMasterShapes ? "" : " showMasterSp=\"0\"") + "><p:cSld name=\"" + Escape(l.Name) + "\"><p:spTree>" + GroupHead);
        if (l.TitleMaster?.Drawing is { } titleDrawing)
        {
            conv.Loss.Offset = l.TitleMaster.Container.Offset;
            new PptShapeWriter(conv, page, sb, PageKind.Master, []).WriteShapes(titleDrawing.Shapes);
        }
        else if (l.Prototype?.Drawing is { } protoDrawing)
        {
            conv.Loss.Offset = l.Prototype.Container.Offset;
            new PptShapeWriter(conv, page, sb, PageKind.Layout, l.Prototype.ListTexts, placeholdersOnly: true).WriteShapes(protoDrawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        var rels = new List<Relationship> { new("rId1", Rel.SlideMaster, "../slideMasters/slideMaster" + m.Number.ToString(CultureInfo.InvariantCulture) + ".xml") };
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "slideLayout+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
    }

    // Adds the page's relationships after the fixed ones and renumbers the ids the shapes wrote.
    private static void AppendPageRels(StringBuilder sb, PageOut page, List<Relationship> rels)
    {
        int offset = rels.Count;
        FixRelationshipIds(sb, offset);
        foreach (Relationship r in page.Rels)
        {
            int n = int.Parse(r.Id[4..], CultureInfo.InvariantCulture) + offset;
            rels.Add(r with { Id = "rId" + n.ToString(CultureInfo.InvariantCulture) });
        }
    }

    // ---- slides ----

    private static void WriteSlide(PptConversion conv, PptSlide slide, int number, MasterOut master, LayoutOut layout, IReadOnlyList<PptComment>? comments, List<(string Name, string Initials, int LastIndex)> authors)
    {
        string partName = "/ppt/slides/slide" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
        PptColorScheme masterScheme = master.Source?.ColorScheme ?? PptColorScheme.Default;
        // SlideFlags bit 1 (fMasterScheme): the slide uses the master's colours.
        PptColorScheme scheme = (slide.Flags & 2) != 0 || slide.ColorScheme is null ? masterScheme : slide.ColorScheme;
        var page = new PageOut(conv, scheme, masterScheme, partName, slide);
        conv.Loss.Offset = slide.Container.Offset;
        conv.AnimatedShapes = 0;
        var sb = new StringBuilder("<p:sld " + Ns);
        if ((slide.Flags & 1) == 0)
        {
            sb.Append(" showMasterSp=\"0\"");
        }

        if (slide.Transition is { } t && t.Payload.Length >= 12 && (t.UInt16At(10) & 4) != 0)
        {
            sb.Append(" show=\"0\"");
        }

        sb.Append("><p:cSld");
        if (slide.Name.Length > 0)
        {
            sb.Append(" name=\"").Append(Escape(slide.Name)).Append('"');
        }

        sb.Append('>');
        ArtDrawing? drawing = slide.Drawing;
        if ((slide.Flags & 4) == 0)
        {
            sb.Append(BackgroundXml(conv, page, drawing, ""));
        }

        sb.Append("<p:spTree>").Append(GroupHead);
        if (drawing is not null)
        {
            var shapes = new PptShapeWriter(conv, page, sb, PageKind.Slide, slide.ListTexts);
            shapes.WriteShapes(drawing.Shapes);
            shapes.WriteHeaderFooterPlaceholders(slide, drawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>");
        sb.Append(TransitionXml(conv, slide));
        sb.Append("</p:sld>");
        var rels = new List<Relationship> { new("rId1", Rel.SlideLayout, "../slideLayouts/slideLayout" + layout.Number.ToString(CultureInfo.InvariantCulture) + ".xml") };
        PptNotes? notes = conv.P.NotesPages.FirstOrDefault(n => n.SlideIdRef == slide.SlideId && n.SlideIdRef != 0)
            ?? (slide.NotesIdRef != 0 ? conv.P.NotesPages.FirstOrDefault(n => n.NotesId == slide.NotesIdRef) : null);
        if (notes is not null)
        {
            rels.Add(new Relationship("rId2", Rel.NotesSlide, "../notesSlides/notesSlide" + number.ToString(CultureInfo.InvariantCulture) + ".xml"));
            WriteNotesSlide(conv, notes, number, slide);
        }

        if (comments is { Count: > 0 })
        {
            string commentPart = "/ppt/comments/comment" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
            var cm = new StringBuilder("<p:cmLst " + Ns + ">");
            foreach (PptComment c in comments)
            {
                int authorId = authors.FindIndex(a => a.Name == c.Author);
                cm.Append("<p:cm authorId=\"").Append(authorId).Append("\" dt=\"").Append(c.Time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("\" idx=\"").Append(Math.Max(1, c.Index)).Append("\"><p:pos x=\"").Append(c.X).Append("\" y=\"").Append(c.Y).Append("\"/><p:text>").Append(Escape(c.Text)).Append("</p:text></p:cm>");
            }

            cm.Append("</p:cmLst>");
            conv.Package.AddPart(commentPart, PmlType + "comments+xml", Bytes(cm.ToString()));
            rels.Add(new Relationship("rId" + (rels.Count + 1).ToString(CultureInfo.InvariantCulture), Rel.Comments, "../comments/comment" + number.ToString(CultureInfo.InvariantCulture) + ".xml"));
        }

        int animated = PptDeep.FindAll(slide.Container, 0xF144, conv.P.File.ReadContext).Count;
        if (animated > 0 || conv.AnimatedShapes > 0)
        {
            conv.Loss.Report("ppt.animations-dropped", "animations", Math.Max(1, animated + conv.AnimatedShapes), "Animation effects are not converted; the shapes appear without them.");
        }

        conv.AnimatedShapes = 0;
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "slide+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
    }

    private static string TransitionXml(PptConversion conv, PptSlide slide)
    {
        if (slide.Transition is not { } t || t.Payload.Length < 16)
        {
            return "";
        }

        int direction = t.Payload.Span[8];
        int effect = t.Payload.Span[9];
        int flags = t.UInt16At(10);
        int speed = t.Payload.Span[12];
        string spd = speed switch
        {
            0 => "slow",
            2 => "fast",
            _ => "med",
        };
        string? inner = effect switch
        {
            0 => direction == 1 ? "<p:cut thruBlk=\"1\"/>" : "<p:cut/>",
            1 => "<p:random/>",
            2 => "<p:blinds dir=\"" + (direction == 1 ? "horz" : "vert") + "\"/>",
            3 => "<p:checker dir=\"" + (direction == 1 ? "vert" : "horz") + "\"/>",
            4 => "<p:cover dir=\"" + Dir8(direction) + "\"/>",
            5 => "<p:dissolve/>",
            6 => "<p:fade/>",
            7 => "<p:pull dir=\"" + Dir8(direction) + "\"/>",
            8 => "<p:randomBar dir=\"" + (direction == 1 ? "vert" : "horz") + "\"/>",
            9 => "<p:strips dir=\"" + Dir8(direction) + "\"/>",
            10 => "<p:wipe dir=\"" + Dir4(direction) + "\"/>",
            11 => "<p:zoom dir=\"" + (direction == 1 ? "in" : "out") + "\"/>",
            13 => "<p:split orient=\"" + (direction < 2 ? "horz" : "vert") + "\" dir=\"" + (direction % 2 == 0 ? "out" : "in") + "\"/>",
            17 => "<p:diamond/>",
            18 => "<p:plus/>",
            19 => "<p:wedge/>",
            20 => "<p:push dir=\"" + Dir4(direction) + "\"/>",
            21 => "<p:comb dir=\"" + (direction == 1 ? "vert" : "horz") + "\"/>",
            22 => "<p:newsflash/>",
            23 => "<p:fade/>",
            26 => "<p:wheel spokes=\"" + Math.Clamp((int)direction, 1, 8) + "\"/>",
            27 => "<p:circle/>",
            _ => null,
        };
        if (inner is null)
        {
            if (effect != 255)
            {
                conv.Loss.Offset = t.Offset;
                conv.Loss.Report("ppt.transition-dropped", "slide transition", 1, "A slide transition of type " + effect.ToString(CultureInfo.InvariantCulture) + " has no PresentationML equivalent.");
            }

            inner = "";
        }

        var attrs = new StringBuilder(" spd=\"" + spd + "\"");
        if ((flags & 1) == 0)
        {
            attrs.Append(" advClick=\"0\"");
        }

        if ((flags & 0x400) != 0)
        {
            attrs.Append(" advTm=\"").Append(Math.Max(0, t.Int32At(0))).Append('"');
        }

        return "<p:transition" + attrs + (inner.Length == 0 ? "/>" : ">" + inner + "</p:transition>");
    }

    private static string Dir4(int d) => d switch
    {
        0 => "l",
        1 => "u",
        2 => "r",
        _ => "d",
    };

    private static string Dir8(int d) => d switch
    {
        0 => "l",
        1 => "u",
        2 => "r",
        3 => "d",
        4 => "lu",
        5 => "ru",
        6 => "ld",
        _ => "rd",
    };

    // ---- notes ----

    private static void WriteNotesSlide(PptConversion conv, PptNotes notes, int number, PptSlide slide)
    {
        string partName = "/ppt/notesSlides/notesSlide" + number.ToString(CultureInfo.InvariantCulture) + ".xml";
        PptColorScheme masterScheme = conv.P.NotesMaster?.ColorScheme ?? PptColorScheme.Default;
        PptColorScheme scheme = notes.ColorScheme ?? masterScheme;
        var page = new PageOut(conv, scheme, masterScheme, partName, notes);
        conv.Loss.Offset = notes.Container.Offset;
        var sb = new StringBuilder("<p:notes " + Ns + "><p:cSld><p:spTree>" + GroupHead);
        if (notes.Drawing is { } drawing)
        {
            new PptShapeWriter(conv, page, sb, PageKind.NotesPage, notes.ListTexts).WriteShapes(drawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>");
        var rels = new List<Relationship>
        {
            new("rId1", Rel.NotesMaster, "../notesMasters/notesMaster1.xml"),
            new("rId2", Rel.Slide, "../slides/slide" + number.ToString(CultureInfo.InvariantCulture) + ".xml"),
        };
        _ = slide;
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "notesSlide+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
    }

    private static void WriteNotesMaster(PptConversion conv, PptNotes? source, int themeNumber)
    {
        PptColorScheme scheme = source?.ColorScheme ?? PptColorScheme.Default;
        string themeTarget = WriteTheme(conv, themeNumber, scheme, null);
        const string partName = "/ppt/notesMasters/notesMaster1.xml";
        var page = new PageOut(conv, scheme, scheme, partName);
        conv.Loss.Offset = source?.Container.Offset ?? -1;
        var sb = new StringBuilder("<p:notesMaster " + Ns + "><p:cSld>");
        sb.Append(BackgroundXml(conv, page, source?.Drawing, "<p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef>"));
        sb.Append("<p:spTree>").Append(GroupHead);
        if (source?.Drawing is { } drawing)
        {
            new PptShapeWriter(conv, page, sb, PageKind.NotesMaster, []).WriteShapes(drawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld>").Append(ClrMap);
        PptMasterStyle? notesStyle = conv.P.DefaultTextStyles.GetValueOrDefault(2);
        sb.Append("<p:notesStyle>");
        AppendLevels(sb, notesStyle, null, page, "a:lvl");
        sb.Append("</p:notesStyle></p:notesMaster>");
        var rels = new List<Relationship> { new("rId1", Rel.Theme, themeTarget) };
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "notesMaster+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
    }

    private static void WriteHandoutMaster(PptConversion conv, PptPage source, int themeNumber)
    {
        PptColorScheme scheme = source.ColorScheme ?? PptColorScheme.Default;
        string themeTarget = WriteTheme(conv, themeNumber, scheme, null);
        const string partName = "/ppt/handoutMasters/handoutMaster1.xml";
        var page = new PageOut(conv, scheme, scheme, partName);
        conv.Loss.Offset = source.Container.Offset;
        var sb = new StringBuilder("<p:handoutMaster " + Ns + "><p:cSld>");
        sb.Append(BackgroundXml(conv, page, source.Drawing, "<p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef>"));
        sb.Append("<p:spTree>").Append(GroupHead);
        if (source.Drawing is { } drawing)
        {
            new PptShapeWriter(conv, page, sb, PageKind.Handout, []).WriteShapes(drawing.Shapes);
        }

        sb.Append("</p:spTree></p:cSld>").Append(ClrMap).Append("</p:handoutMaster>");
        var rels = new List<Relationship> { new("rId1", Rel.Theme, themeTarget) };
        AppendPageRels(sb, page, rels);
        conv.Package.AddPart(partName, PmlType + "handoutMaster+xml", Bytes(sb.ToString()));
        conv.Package.SetRelationships(partName, rels);
    }
}
