using OneFont;
using OneRender.Fonts;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>
/// The standard <see cref="IFontProvider"/>: a set of font families the caller supplies (files, streams, directories, fonts embedded in a
/// document) plus the open default faces, with the <see cref="FontSubstitutionTable"/> applied for families that are missing.
/// </summary>
/// <remarks>
/// Lookup order for a request: the requested family in the collection (supplied fonts and defaults alike), then the table's candidates in
/// order, then a generic face chosen by the family class, PANOSE data and the family name. Script fallbacks come from faces added with
/// <see cref="AddFallback(FontFace)"/> and from the optional OneRender font packages when <see cref="AddOptionalPackages"/> was called.
/// A collection is safe for concurrent lookups once filled; adding fonts is not safe beside lookups.
/// </remarks>
public sealed class FontLibrary : IFontProvider
{
    private sealed class Entry(string family, int weight, bool italic, Func<FontFace> load)
    {
        private FontFace? _face;

        public string Family { get; } = family;

        public int Weight { get; } = weight;

        public bool Italic { get; } = italic;

        public FontFace Face => _face ??= load();
    }

    private readonly Dictionary<string, List<Entry>> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _fallbacks = [];
    private readonly object _gate = new();

    /// <summary>Creates an empty collection with the default substitution table.</summary>
    public FontLibrary()
    {
        Substitutions = FontSubstitutionTable.CreateDefault();
    }

    /// <summary>Gets the substitution table; add entries to teach the collection about other families.</summary>
    public FontSubstitutionTable Substitutions { get; }

    /// <summary>Gets the families the collection holds, in no particular order.</summary>
    public IEnumerable<string> Families => _families.Values.Select(static l => l[0].Family);

    /// <summary>
    /// Creates a collection with the default open faces (Liberation Sans, Serif and Mono, Carlito, Caladea) and the symbols fallback.
    /// </summary>
    /// <returns>A new collection.</returns>
    public static FontLibrary CreateDefault()
    {
        var c = new FontLibrary();
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            c.AddEntry(new Entry(info.Family, info.Weight, info.Slant == FontSlant.Italic, () => FontFace.Load(info.ReadAllBytes(), info.FaceIndex)));
        }

        foreach (FontFaceInfo info in DefaultFonts.FallbackFaces)
        {
            c._fallbacks.Add(new Entry(info.Family, info.Weight, info.Slant == FontSlant.Italic, () => FontFace.Load(info.ReadAllBytes(), info.FaceIndex)));
        }

        // The math font: Cambria Math has no open twin, and Noto Sans Math carries a MATH table, which equation layout needs.
        // It is small (about 650 kB) and loads on first use.
        foreach (FontFaceInfo info in OneRender.Fonts.Noto.NotoMathFonts.Package.Faces)
        {
            var entry = new Entry(info.Family, info.Weight, info.Slant == FontSlant.Italic, () => FontFace.Load(info.ReadAllBytes(), info.FaceIndex));
            c.AddEntry(entry);
            c._fallbacks.Add(entry);
        }

        return c;
    }

    /// <summary>
    /// Adds the faces of the optional OneRender font packages that are registered (for example the Noto Arabic, Hebrew and CJK packages
    /// after their <c>Register()</c> call) as families and as script fallbacks. Faces load on first use.
    /// </summary>
    public void AddOptionalPackages()
    {
        foreach (OptionalFontPackage package in OptionalFonts.Packages)
        {
            foreach (FontFaceInfo info in package.Faces)
            {
                var entry = new Entry(info.Family, info.Weight, info.Slant == FontSlant.Italic, () => FontFace.Load(info.ReadAllBytes(), info.FaceIndex));
                AddEntry(entry);
                _fallbacks.Add(entry);
            }
        }
    }

    /// <summary>Adds every face in font data (TrueType, OpenType or a collection).</summary>
    /// <param name="fontData">The font file's bytes.</param>
    /// <returns>The number of faces added; 0 when the data is not a font (nothing is thrown, a damaged font is skipped).</returns>
    public int AddFont(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        int added = 0;
        int count;
        try
        {
            count = FontFace.GetFaceCount(fontData);
        }
        catch (FontFormatException)
        {
            return 0;
        }

        for (int i = 0; i < count; i++)
        {
            try
            {
                FontFace face = FontFace.Load(fontData, i);
                AddEntry(new Entry(face.FamilyName, face.Weight, face.IsItalic, () => face));
                added++;
            }
            catch (FontFormatException)
            {
                // A damaged face is skipped; the others in the file still load.
            }
        }

        return added;
    }

    /// <summary>Adds the faces of a font file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The number of faces added.</returns>
    public int AddFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return AddFont(File.ReadAllBytes(path));
    }

    /// <summary>Adds the faces of every font file (<c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>) in a directory.</summary>
    /// <param name="directory">The directory; a missing one adds nothing.</param>
    /// <param name="recursive">Whether to include subdirectories.</param>
    /// <returns>The number of faces added.</returns>
    public int AddDirectory(string directory, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int added = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        foreach (string file in Directory.EnumerateFiles(directory, "*.*", options))
        {
            string ext = Path.GetExtension(file);
            if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    added += AddFont(File.ReadAllBytes(file));
                }
                catch (IOException)
                {
                    // An unreadable file is skipped.
                }
            }
        }

        return added;
    }

    /// <summary>Adds the fonts installed on the machine from the usual per-platform directories. Not available in a browser sandbox, where it adds nothing.</summary>
    /// <returns>The number of faces added.</returns>
    public int AddSystemFonts()
    {
        int added = 0;
        string[] dirs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/Library/Fonts",
            "/System/Library/Fonts",
        ];
        foreach (string dir in dirs.Where(static d => d.Length > 0).Distinct(StringComparer.Ordinal))
        {
            added += AddDirectory(dir);
        }

        return added;
    }

    /// <summary>Adds a face used only to cover characters other faces lack.</summary>
    /// <param name="face">The face.</param>
    public void AddFallback(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        _fallbacks.Add(new Entry(face.FamilyName, face.Weight, face.IsItalic, () => face));
    }

    /// <inheritdoc />
    public FontMatch? Resolve(in FontRequest request)
    {
        string family = request.Family ?? string.Empty;
        if (Pick(family, request.Bold, request.Italic) is { } exact)
        {
            return new FontMatch(exact.Face, family, exact.Family, FontMatchKind.Exact);
        }

        foreach ((string target, FontMatchKind kind) in Substitutions.Candidates(family))
        {
            if (Pick(target, request.Bold, request.Italic) is { } sub)
            {
                return new FontMatch(sub.Face, family, sub.Family, kind);
            }
        }

        string generic = GenericFamily(request);
        if (Pick(generic, request.Bold, request.Italic) is { } g)
        {
            return new FontMatch(g.Face, family, g.Family, FontMatchKind.Generic);
        }

        // A collection without the default faces: use whatever it has, so the layout can still proceed.
        foreach (List<Entry> list in _families.Values)
        {
            Entry e = Best(list, request.Bold, request.Italic);
            return new FontMatch(e.Face, family, e.Family, FontMatchKind.Generic);
        }

        return null;
    }

    /// <inheritdoc />
    public FontFace? FindFallback(int codePoint, bool bold, bool italic)
    {
        lock (_gate)
        {
            // Prefer the requested weight within each family, in the order the faces were added.
            foreach (Entry e in _fallbacks)
            {
                FontFace f;
                try
                {
                    f = e.Face;
                }
                catch (FontFormatException)
                {
                    continue;
                }

                if (f.HasGlyph(codePoint))
                {
                    Entry chosen = e;
                    foreach (Entry sibling in _fallbacks)
                    {
                        if (sibling.Family == e.Family && Score(sibling, bold, italic) < Score(chosen, bold, italic))
                        {
                            chosen = sibling;
                        }
                    }

                    return chosen.Face;
                }
            }

            return null;
        }
    }

    private void AddEntry(Entry e)
    {
        if (!_families.TryGetValue(e.Family, out List<Entry>? list))
        {
            _families[e.Family] = list = [];
        }

        list.Add(e);
    }

    private Entry? Pick(string family, bool bold, bool italic) =>
        _families.TryGetValue(family, out List<Entry>? list) && list.Count > 0 ? Best(list, bold, italic) : null;

    private static Entry Best(List<Entry> list, bool bold, bool italic)
    {
        Entry best = list[0];
        int bestScore = Score(best, bold, italic);
        for (int i = 1; i < list.Count; i++)
        {
            int s = Score(list[i], bold, italic);
            if (s < bestScore)
            {
                best = list[i];
                bestScore = s;
            }
        }

        return best;
    }

    private static int Score(Entry e, bool bold, bool italic) =>
        Math.Abs(e.Weight - (bold ? 700 : 400)) + (e.Italic == italic ? 0 : 1000);

    // The hints the font table gives, strongest first: w:family, PANOSE, then words in the name.
    private static string GenericFamily(in FontRequest r)
    {
        switch (r.FamilyClass?.ToLowerInvariant())
        {
            case "roman":
                return "Liberation Serif";
            case "swiss":
                return "Liberation Sans";
            case "modern":
                return "Liberation Mono";
        }

        if (r.Panose is { Length: 20 } p && p[..2] == "02")
        {
            // PANOSE family kind 2 (Latin text): the serif-style digit (third pair) and proportion (fourth pair).
            if (p.Substring(6, 2) == "09")
            {
                return "Liberation Mono";
            }

            int serifStyle = Convert.ToInt32(p.Substring(2, 2), 16);
            return serifStyle is >= 2 and <= 10 ? "Liberation Serif" : "Liberation Sans";
        }

        string n = r.Family ?? string.Empty;
        if (n.Contains("mono", StringComparison.OrdinalIgnoreCase) || n.Contains("courier", StringComparison.OrdinalIgnoreCase) || n.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            return "Liberation Mono";
        }

        if (n.Contains("serif", StringComparison.OrdinalIgnoreCase) && !n.Contains("sans", StringComparison.OrdinalIgnoreCase))
        {
            return "Liberation Serif";
        }

        return "Liberation Sans";
    }
}
