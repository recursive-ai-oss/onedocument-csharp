using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>The eight colours of a PowerPoint colour scheme ([MS-PPT] §2.5.14), as RGB bytes.</summary>
/// <param name="Colors">Background, text and lines, shadows, title text, fills, accent, accent and hyperlink, accent and followed hyperlink.</param>
public sealed record PptColorScheme(IReadOnlyList<(byte Red, byte Green, byte Blue)> Colors)
{
    /// <summary>The default scheme PowerPoint 97 gave new presentations (white background, black text).</summary>
    public static PptColorScheme Default { get; } = new(
    [
        (0xFF, 0xFF, 0xFF), (0x00, 0x00, 0x00), (0x80, 0x80, 0x80), (0x00, 0x00, 0x00),
        (0x00, 0x99, 0x99), (0x99, 0x33, 0x33), (0x33, 0x33, 0x99), (0x99, 0x66, 0x00),
    ]);

    /// <summary>True when both schemes list the same eight colours.</summary>
    /// <param name="other">The scheme to compare with.</param>
    public bool SameAs(PptColorScheme? other)
    {
        if (other is null || other.Colors.Count != Colors.Count)
        {
            return false;
        }

        for (int i = 0; i < Colors.Count; i++)
        {
            if (Colors[i] != other.Colors[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a <c>ColorSchemeAtom</c>: eight four-byte entries (RGB and a padding byte).</summary>
    /// <param name="record">The atom.</param>
    /// <returns>The scheme, or null when the atom holds fewer than eight colours.</returns>
    public static PptColorScheme? Read(ArtRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ReadOnlySpan<byte> data = record.Payload.Span;
        if (data.Length < 32)
        {
            return null;
        }

        var colors = new (byte, byte, byte)[8];
        for (int i = 0; i < 8; i++)
        {
            colors[i] = (data[i * 4], data[(i * 4) + 1], data[(i * 4) + 2]);
        }

        return new PptColorScheme(colors);
    }
}

/// <summary>A page of the presentation: a slide, a master, a notes page or the handout master. Holds its container and a lazily read drawing.</summary>
public class PptPage
{
    private ArtDrawing? _drawing;
    private bool _drawingRead;

    internal PptPage(PptPresentation owner, ArtRecord container, uint persistId)
    {
        Owner = owner;
        Container = container;
        PersistId = persistId;
    }

    /// <summary>The presentation the page belongs to.</summary>
    public PptPresentation Owner { get; }

    /// <summary>The slide, master, notes or handout container.</summary>
    public ArtRecord Container { get; }

    /// <summary>The persist identifier the page was found under.</summary>
    public uint PersistId { get; }

    /// <summary>The colour scheme of the page, or null when it has none (it follows its master).</summary>
    public PptColorScheme? ColorScheme { get; internal set; }

    /// <summary>The page's own drawing (<c>PPDrawing</c>), or null when the page has none or it is unreadable.</summary>
    public ArtDrawing? Drawing
    {
        get
        {
            if (!_drawingRead)
            {
                _drawingRead = true;
                ArtRecord? holder = Container.Find(PptRecordType.Drawing);
                ArtRecord? dg = holder?.Find(ArtRecordType.DgContainer);
                if (dg is not null)
                {
                    _drawing = ArtDrawing.Parse(dg, Owner.File.ReadContext);
                }
            }

            return _drawing;
        }
    }
}

/// <summary>A main master or a title master.</summary>
public sealed class PptMaster : PptPage
{
    internal PptMaster(PptPresentation owner, ArtRecord container, uint persistId, uint masterId, bool isTitleMaster)
        : base(owner, container, persistId)
    {
        MasterId = masterId;
        IsTitleMaster = isTitleMaster;
    }

    /// <summary>The master identifier slides refer to (<c>slideAtom.masterIdRef</c>).</summary>
    public uint MasterId { get; }

    /// <summary>True for a title master (a slide container in the master list), false for a main master.</summary>
    public bool IsTitleMaster { get; }

    /// <summary>For a title master, the main master it builds on (its <c>masterIdRef</c>); otherwise 0.</summary>
    public uint MainMasterId { get; internal set; }

    /// <summary>The text styles of the master by text type (<c>TextMasterStyleAtom</c> instances).</summary>
    public IReadOnlyDictionary<int, PptMasterStyle> TextStyles { get; internal set; } = new Dictionary<int, PptMasterStyle>();

    /// <summary>The <c>SlideAtom</c> flags of a title master (bit 0: follows the main master's objects).</summary>
    public int Flags { get; internal set; }
}

/// <summary>A presentation slide.</summary>
public sealed class PptSlide : PptPage
{
    internal PptSlide(PptPresentation owner, ArtRecord container, uint persistId, uint slideId)
        : base(owner, container, persistId)
    {
        SlideId = slideId;
    }

    /// <summary>The slide identifier (<c>SlidePersistAtom.slideId</c>).</summary>
    public uint SlideId { get; }

    /// <summary>The layout hint (<c>SlideLayoutType</c>): 0 title slide, 1 title and body, 7 title only, 16 blank…</summary>
    public int Layout { get; internal set; }

    /// <summary>The placeholder types of the layout (<c>PlaceholderEnum</c> values; 0 is none), up to eight.</summary>
    public IReadOnlyList<int> LayoutPlaceholders { get; internal set; } = [];

    /// <summary>The master (main or title) the slide uses.</summary>
    public uint MasterIdRef { get; internal set; }

    /// <summary>The notes page's identifier (0 when the slide has none).</summary>
    public uint NotesIdRef { get; internal set; }

    /// <summary>The <c>SlideFlags</c>: bit 0 shows the master's objects, bit 1 uses the master's colour scheme, bit 2 uses the master's background.</summary>
    public int Flags { get; internal set; }

    /// <summary>The text kept in the document's slide list for this slide (the placeholder texts of older files), in order.</summary>
    public IReadOnlyList<PptTextBody> ListTexts { get; internal set; } = [];

    /// <summary>The slide's name (<c>SlideNameAtom</c>), or empty.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>The transition record (<c>SlideShowSlideInfoAtom</c>), or null.</summary>
    public ArtRecord? Transition { get; internal set; }
}

/// <summary>A notes page, or the notes master.</summary>
public sealed class PptNotes : PptPage
{
    internal PptNotes(PptPresentation owner, ArtRecord container, uint persistId, uint notesId)
        : base(owner, container, persistId)
    {
        NotesId = notesId;
    }

    /// <summary>The notes identifier (<c>SlidePersistAtom.slideId</c> of the notes list); 0 for the notes master.</summary>
    public uint NotesId { get; }

    /// <summary>The identifier of the slide the notes belong to (<c>notesAtom.slideIdRef</c>); 0 for the notes master.</summary>
    public uint SlideIdRef { get; internal set; }

    /// <summary>The text kept in the document's notes list, in order.</summary>
    public IReadOnlyList<PptTextBody> ListTexts { get; internal set; } = [];
}

/// <summary>
/// The structure of a PowerPoint binary file above the records: size, fonts, masters, slides, notes, hyperlinks and the drawing group.
/// </summary>
public sealed class PptPresentation
{
    private PptPresentation(PptFile file)
    {
        File = file;
    }

    /// <summary>The record-level file.</summary>
    public PptFile File { get; }

    /// <summary>The slide width in master units (1/576 inch).</summary>
    public int SlideWidth { get; private set; } = 5760;

    /// <summary>The slide height in master units.</summary>
    public int SlideHeight { get; private set; } = 4320;

    /// <summary>The notes page width in master units.</summary>
    public int NotesWidth { get; private set; } = 4320;

    /// <summary>The notes page height in master units.</summary>
    public int NotesHeight { get; private set; } = 5760;

    /// <summary>The number of the first slide.</summary>
    public int FirstSlideNumber { get; private set; } = 1;

    /// <summary>The <c>SlideSizeEnum</c> value.</summary>
    public int SlideSizeType { get; private set; }

    /// <summary>The typeface names, indexed by the font references of text formatting.</summary>
    public IReadOnlyList<string> Fonts { get; private set; } = [];

    /// <summary>The document-level default text styles by text type (from the environment).</summary>
    public IReadOnlyDictionary<int, PptMasterStyle> DefaultTextStyles { get; private set; } = new Dictionary<int, PptMasterStyle>();

    /// <summary>The main masters and title masters, in the file's order.</summary>
    public IReadOnlyList<PptMaster> Masters { get; private set; } = [];

    /// <summary>The slides in presentation order.</summary>
    public IReadOnlyList<PptSlide> Slides { get; private set; } = [];

    /// <summary>The notes pages.</summary>
    public IReadOnlyList<PptNotes> NotesPages { get; private set; } = [];

    /// <summary>The notes master, or null.</summary>
    public PptNotes? NotesMaster { get; private set; }

    /// <summary>The handout master, or null.</summary>
    public PptPage? HandoutMaster { get; private set; }

    /// <summary>The drawing group (shared pictures and defaults), or null when the file has none.</summary>
    public ArtDrawingGroup? DrawingGroup { get; private set; }

    /// <summary>The hyperlinks of the document by <c>ExHyperlinkId</c>: target address and friendly name.</summary>
    public IReadOnlyDictionary<uint, (string Target, string Name)> Hyperlinks { get; private set; } = new Dictionary<uint, (string, string)>();

    /// <summary>True when the file carries a VBA project (never run).</summary>
    public bool HasVbaProject { get; private set; }

    /// <summary>The external-object list container (OLE objects, media), kept as a record, or null.</summary>
    public ArtRecord? ExternalObjects { get; private set; }

    /// <summary>The embedded pictures of the drawing group, decoded on demand: the image of a one-based BLIP index, or null.</summary>
    /// <param name="index">The one-based index of the BLIP store entry.</param>
    public ArtImage? GetImage(int index)
    {
        ArtBlipStore? store = DrawingGroup?.Blips;
        return store?.GetImage(index, File.PicturesStream, new ArtReadContext(File.Context.Limits, File.Context.Defects, "Pictures"));
    }

    /// <summary>Reads the document structure of an opened file.</summary>
    /// <param name="file">The record-level file.</param>
    /// <returns>The presentation.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static PptPresentation Read(PptFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var p = new PptPresentation(file);
        p.ReadDocument();
        return p;
    }

    private void ReadDocument()
    {
        ArtRecord doc = File.DocumentContainer!;
        ArtReadContext read = File.ReadContext;
        ArtRecord? atom = doc.Find(PptRecordType.DocumentAtom);
        uint notesMasterRef = 0;
        uint handoutRef = 0;
        if (atom is not null)
        {
            // [MS-PPT] §2.4.2: slideSize and notesSize are PointStructs in master units.
            SlideWidth = ClampSize(atom.Int32At(0), SlideWidth);
            SlideHeight = ClampSize(atom.Int32At(4), SlideHeight);
            NotesWidth = ClampSize(atom.Int32At(8), NotesWidth);
            NotesHeight = ClampSize(atom.Int32At(12), NotesHeight);
            notesMasterRef = (uint)atom.Int32At(24);
            handoutRef = (uint)atom.Int32At(28);
            FirstSlideNumber = Math.Max(0, atom.UInt16At(32, 1));
            SlideSizeType = atom.UInt16At(34);
        }
        else
        {
            read.Repair("ppt.document-atom-missing", "The document container has no DocumentAtom; the default slide size is used.", doc.Offset);
        }

        ArtRecord? env = doc.Find(PptRecordType.Environment);
        if (env is not null)
        {
            ReadEnvironment(env);
        }

        ArtRecord? dgg = doc.Find(PptRecordType.DrawingGroup)?.Find(ArtRecordType.DggContainer);
        if (dgg is not null)
        {
            DrawingGroup = ArtDrawingGroup.Parse(dgg, read);
        }

        HasVbaProject = File.HasVbaProject || doc.Find(PptRecordType.VbaInfo) is not null;
        ExternalObjects = doc.Find(PptRecordType.ExternalObjectList);
        if (ExternalObjects is not null)
        {
            ReadHyperlinks(ExternalObjects);
        }

        var masters = new List<PptMaster>();
        var slides = new List<PptSlide>();
        var notes = new List<PptNotes>();
        foreach (ArtRecord list in doc.FindAll(PptRecordType.SlideListWithText))
        {
            switch (list.Header.Instance)
            {
                case 1:
                    ReadMasters(list, masters);
                    break;
                case 0:
                    ReadSlides(list, slides);
                    break;
                case 2:
                    ReadNotes(list, notes);
                    break;
            }
        }

        Masters = masters;
        Slides = slides;
        NotesPages = notes;
        if (notesMasterRef != 0 && File.GetPersistObject(notesMasterRef) is { } nm && nm.Type == PptRecordType.Notes)
        {
            NotesMaster = new PptNotes(this, nm, notesMasterRef, 0) { ColorScheme = SchemeOf(nm) };
        }

        if (handoutRef != 0 && File.GetPersistObject(handoutRef) is { } hm && hm.Type == PptRecordType.Handout)
        {
            HandoutMaster = new PptPage(this, hm, handoutRef) { ColorScheme = SchemeOf(hm) };
        }
    }

    private static int ClampSize(int value, int fallback) => value is >= 0x240 and <= 0x7E00 ? value : fallback;

    private static PptColorScheme? SchemeOf(ArtRecord container)
    {
        ArtRecord? best = null;
        foreach (ArtRecord r in container.FindAll(PptRecordType.ColorSchemeAtom))
        {
            // [MS-PPT] §2.5.14: the slide's own scheme is the atom with instance 1.
            if (r.Header.Instance == 1)
            {
                best = r;
                break;
            }

            best ??= r;
        }

        return best is null ? null : PptColorScheme.Read(best);
    }

    private void ReadEnvironment(ArtRecord env)
    {
        ArtReadContext read = File.ReadContext;
        var fonts = new List<string>();
        if (env.Find(PptRecordType.FontCollection) is { } collection)
        {
            foreach (ArtRecord entry in collection.FindAll(PptRecordType.FontEntityAtom))
            {
                // [MS-PPT] §2.9.10: lfFaceName is 32 UTF-16 characters, NUL terminated.
                ReadOnlySpan<byte> face = entry.Payload.Span[..Math.Min(64, entry.Payload.Length)];
                var chars = new char[face.Length / 2];
                for (int i = 0; i < chars.Length; i++)
                {
                    chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(face[(i * 2)..]);
                }

                int end = Array.IndexOf(chars, '\0');
                fonts.Add(new string(chars, 0, end < 0 ? chars.Length : end));
            }
        }

        Fonts = fonts;
        var styles = new Dictionary<int, PptMasterStyle>();
        foreach (ArtRecord r in env.FindAll(PptRecordType.TextMasterStyleAtom))
        {
            styles[r.Header.Instance] = PptText.ReadMasterStyle(r, read);
        }

        DefaultTextStyles = styles;
    }

    // [MS-PPT] §2.10: an ExHyperlink container holds ExHyperlinkAtom (the id) and CStrings: instance 0 the friendly name, instance 1 the target.
    private void ReadHyperlinks(ArtRecord list)
    {
        var links = new Dictionary<uint, (string, string)>();
        foreach (ArtRecord link in list.FindAll(PptRecordType.ExternalHyperlink))
        {
            ArtRecord? atom = link.Find(PptRecordType.ExternalHyperlinkAtom);
            if (atom is null)
            {
                continue;
            }

            string name = "";
            string target = "";
            foreach (ArtRecord s in link.FindAll(PptRecordType.CString))
            {
                string text = Utf16(s.Payload.Span);
                if (s.Header.Instance == 0)
                {
                    name = text;
                }
                else if (s.Header.Instance == 1)
                {
                    target = text;
                }
            }

            links[(uint)atom.Int32At(0)] = (target, name);
        }

        Hyperlinks = links;
    }

    private static string Utf16(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
        }

        int end = Array.IndexOf(chars, '\0');
        return new string(chars, 0, end < 0 ? chars.Length : end);
    }

    // The slide list is a flat run: a SlidePersistAtom, then the text records of that slide, then the next SlidePersistAtom.
    private static IEnumerable<(ArtRecord Atom, List<ArtRecord> Records)> Entries(ArtRecord list)
    {
        ArtRecord? atom = null;
        List<ArtRecord> records = [];
        foreach (ArtRecord child in list.Children)
        {
            if (child.Type == PptRecordType.SlidePersistAtom)
            {
                if (atom is not null)
                {
                    yield return (atom, records);
                }

                atom = child;
                records = [];
            }
            else if (atom is not null)
            {
                records.Add(child);
            }
        }

        if (atom is not null)
        {
            yield return (atom, records);
        }
    }

    private void ReadMasters(ArtRecord list, List<PptMaster> masters)
    {
        foreach ((ArtRecord atom, _) in Entries(list))
        {
            uint persist = (uint)atom.Int32At(0);
            uint masterId = (uint)atom.Int32At(12);
            ArtRecord? container = File.GetPersistObject(persist);
            if (container is null || container.Type is not (PptRecordType.MainMaster or PptRecordType.Slide))
            {
                File.ReadContext.Drop("ppt.master-missing", "A master list entry does not lead to a master or title master container.", atom.Offset);
                continue;
            }

            bool title = container.Type == PptRecordType.Slide;
            var master = new PptMaster(this, container, persist, masterId, title) { ColorScheme = SchemeOf(container) };
            ArtRecord? slideAtom = container.Find(PptRecordType.SlideAtom);
            if (slideAtom is not null)
            {
                master.MainMasterId = title ? (uint)slideAtom.Int32At(12) : 0;
                master.Flags = slideAtom.UInt16At(20);
            }

            var styles = new Dictionary<int, PptMasterStyle>();
            foreach (ArtRecord r in container.FindAll(PptRecordType.TextMasterStyleAtom))
            {
                styles[r.Header.Instance] = PptText.ReadMasterStyle(r, File.ReadContext);
            }

            master.TextStyles = styles;
            masters.Add(master);
        }
    }

    private void ReadSlides(ArtRecord list, List<PptSlide> slides)
    {
        foreach ((ArtRecord atom, List<ArtRecord> records) in Entries(list))
        {
            uint persist = (uint)atom.Int32At(0);
            uint slideId = (uint)atom.Int32At(12);
            ArtRecord? container = File.GetPersistObject(persist);
            if (container is null || container.Type != PptRecordType.Slide)
            {
                File.ReadContext.Drop("ppt.slide-missing", "A slide list entry does not lead to a slide container; the slide is dropped.", atom.Offset);
                continue;
            }

            var slide = new PptSlide(this, container, persist, slideId) { ColorScheme = SchemeOf(container) };
            ArtRecord? slideAtom = container.Find(PptRecordType.SlideAtom);
            if (slideAtom is not null)
            {
                slide.Layout = slideAtom.Int32At(0);
                var types = new List<int>(8);
                for (int i = 0; i < 8 && 4 + i < slideAtom.Payload.Length; i++)
                {
                    types.Add(slideAtom.Payload.Span[4 + i]);
                }

                slide.LayoutPlaceholders = types;
                slide.MasterIdRef = (uint)slideAtom.Int32At(12);
                slide.NotesIdRef = (uint)slideAtom.Int32At(16);
                slide.Flags = slideAtom.UInt16At(20);
            }

            slide.ListTexts = PptText.ReadBodies(records, File.ReadContext);
            slide.Transition = container.Find(PptRecordType.SlideShowSlideInfoAtom);
            slides.Add(slide);
        }
    }

    private void ReadNotes(ArtRecord list, List<PptNotes> notes)
    {
        foreach ((ArtRecord atom, List<ArtRecord> records) in Entries(list))
        {
            uint persist = (uint)atom.Int32At(0);
            uint notesId = (uint)atom.Int32At(12);
            ArtRecord? container = File.GetPersistObject(persist);
            if (container is null || container.Type != PptRecordType.Notes)
            {
                File.ReadContext.Drop("ppt.notes-missing", "A notes list entry does not lead to a notes container; the notes are dropped.", atom.Offset);
                continue;
            }

            var page = new PptNotes(this, container, persist, notesId) { ColorScheme = SchemeOf(container) };
            ArtRecord? notesAtom = container.Find(PptRecordType.NotesAtom);
            if (notesAtom is not null)
            {
                page.SlideIdRef = (uint)notesAtom.Int32At(0);
            }

            page.ListTexts = PptText.ReadBodies(records, File.ReadContext);
            notes.Add(page);
        }
    }
}
