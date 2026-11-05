namespace RecursiveAI.OneDocument.Layout;

/// <summary>
/// Maps font families that Office documents name to the open families that stand in for them when the original is not installed.
/// A family can have several candidates; the first one the font collection has wins. Every use is reported as a
/// <see cref="FontSubstitutionNote"/>.
/// </summary>
/// <remarks>
/// <see cref="FontMatchKind.MetricCompatible"/> pairs are designed to have identical advance widths (Carlito for Calibri, Caladea for
/// Cambria, Liberation for Arial, Times New Roman and Courier New). Everything else is <see cref="FontMatchKind.Approximate"/>:
/// the newer Office defaults (Aptos and its relatives) have no open metric twin, so the nearest open design is listed and flagged.
/// </remarks>
public sealed class FontSubstitutionTable
{
    private readonly Dictionary<string, List<(string Target, FontMatchKind Kind)>> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a table filled with the standard substitutions.</summary>
    /// <returns>A new table the caller may extend.</returns>
    public static FontSubstitutionTable CreateDefault()
    {
        var t = new FontSubstitutionTable();
        const FontMatchKind M = FontMatchKind.MetricCompatible;
        const FontMatchKind A = FontMatchKind.Approximate;

        // Metric twins: same advance widths by design (the open families were drawn to match).
        t.Add("Calibri", "Carlito", M);
        t.Add("Cambria", "Caladea", M);
        t.Add("Arial", "Liberation Sans", M);
        t.Add("Helvetica", "Liberation Sans", M);
        t.Add("Times New Roman", "Liberation Serif", M);
        t.Add("Times", "Liberation Serif", M);
        t.Add("Courier New", "Liberation Mono", M);
        t.Add("Courier", "Liberation Mono", M);

        // Newer Office defaults and common system fonts with no open twin: nearest design, flagged approximate.
        foreach (string f in new[] { "Aptos", "Aptos Display", "Aptos Narrow", "Calibri Light", "Segoe UI", "Segoe UI Light", "Segoe UI Semibold" })
        {
            t.Add(f, "Carlito", A);
        }

        foreach (string f in new[] { "Aptos Serif", "Constantia", "Georgia", "Palatino Linotype", "Book Antiqua", "Garamond", "Century Schoolbook", "Sitka Text", "Sitka Heading" })
        {
            t.Add(f, "Liberation Serif", A);
        }

        foreach (string f in new[] { "Arial Narrow", "Arial Black", "Tahoma", "Verdana", "Trebuchet MS", "Lucida Sans Unicode", "Century Gothic", "Franklin Gothic Book", "Franklin Gothic Medium", "Corbel", "Candara", "Gill Sans MT", "Microsoft Sans Serif", "Open Sans", "Roboto" })
        {
            t.Add(f, "Liberation Sans", A);
        }

        foreach (string f in new[] { "Consolas", "Lucida Console", "Cascadia Code", "Cascadia Mono", "Courier Prime", "Andale Mono", "Menlo", "Monaco" })
        {
            t.Add(f, "Liberation Mono", A);
        }

        // The math font: Noto Sans Math has the OpenType MATH table equation layout needs (the original has no open twin).
        t.Add("Cambria Math", "Noto Sans Math", A);
        t.Add("STIX Two Math", "Noto Sans Math", A);
        t.Add("Latin Modern Math", "Noto Sans Math", A);

        // Symbol fonts keep their code points in the private-use area, which the open face does not carry: glyphs may be missing.
        t.Add("Symbol", "Noto Sans Symbols 2", A);
        t.Add("Wingdings", "Noto Sans Symbols 2", A);
        t.Add("Wingdings 2", "Noto Sans Symbols 2", A);
        t.Add("Wingdings 3", "Noto Sans Symbols 2", A);
        t.Add("Webdings", "Noto Sans Symbols 2", A);

        // East Asian system fonts: the Noto CJK faces (optional font package) cover them; listed in order of preference.
        foreach (string f in new[] { "SimSun", "NSimSun", "MS Mincho", "MS PMincho", "Yu Mincho", "Batang", "PMingLiU", "MingLiU", "Songti SC" })
        {
            t.Add(f, "Noto Sans CJK SC", A);
        }

        foreach (string f in new[] { "SimHei", "MS Gothic", "MS PGothic", "Yu Gothic", "Meiryo", "Malgun Gothic", "Gulim", "Dotum", "Microsoft YaHei", "Microsoft JhengHei", "DengXian", "PingFang SC", "Hiragino Sans" })
        {
            t.Add(f, "Noto Sans CJK SC", A);
        }

        // Complex-script system fonts.
        foreach (string f in new[] { "Times New Roman Arabic", "Traditional Arabic", "Simplified Arabic", "Arabic Typesetting", "Sakkal Majalla", "Tahoma Arabic" })
        {
            t.Add(f, "Noto Sans Arabic", A);
        }

        foreach (string f in new[] { "David", "Miriam", "Narkisim", "FrankRuehl", "Gisha", "Arial Hebrew" })
        {
            t.Add(f, "Noto Sans Hebrew", A);
        }

        return t;
    }

    /// <summary>Adds a candidate stand-in for a family. Candidates are tried in the order they were added.</summary>
    /// <param name="family">The family a document names.</param>
    /// <param name="target">The family that stands in for it.</param>
    /// <param name="kind">How close the stand-in is; <see cref="FontMatchKind.Exact"/> is not meaningful here and counts as metric-compatible.</param>
    public void Add(string family, string target, FontMatchKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentException.ThrowIfNullOrEmpty(target);
        if (!_map.TryGetValue(family, out List<(string, FontMatchKind)>? list))
        {
            _map[family] = list = [];
        }

        list.Add((target, kind == FontMatchKind.Exact ? FontMatchKind.MetricCompatible : kind));
    }

    /// <summary>Gets the candidates for a family.</summary>
    /// <param name="family">The family a document names.</param>
    /// <returns>The candidates in preference order; empty when the table does not know the family.</returns>
    public IReadOnlyList<(string Target, FontMatchKind Kind)> Candidates(string family) =>
        _map.TryGetValue(family, out List<(string, FontMatchKind)>? list) ? list : [];

    /// <summary>Gets every family the table has an entry for, in no particular order.</summary>
    public IEnumerable<string> Families => _map.Keys;
}
