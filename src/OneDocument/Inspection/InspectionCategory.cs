using System.Linq;
namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// A kind of hidden data or personal information that inspection reports and sanitising can remove. The categories follow the groups of
/// Office's Document Inspector as Microsoft describes them in its support article on removing hidden data and personal information; see
/// <c>docs/sanitize.md</c> for how each maps and which ones can only be reported.
/// </summary>
public enum InspectionCategory
{
    /// <summary>Comments and their replies: Word comments, workbook notes and threaded comments, presentation comments (classic and modern), with their ranges and authors.</summary>
    Comments,

    /// <summary>Tracked changes: Word insertions, deletions, moves and formatting changes; the change-history parts of a shared workbook.</summary>
    Revisions,

    /// <summary>Document properties: core, extended (company, manager, template, hyperlink base, titles) and custom properties.</summary>
    Properties,

    /// <summary>Personal information outside the properties: person parts, <c>w:rsid</c> editing-session ids, printer settings, file-sharing user names, stored absolute paths.</summary>
    PersonalInformation,

    /// <summary>Custom XML data parts and content-control data bindings.</summary>
    CustomXml,

    /// <summary>Headers, footers and watermarks (Word); page-setup headers and footers (Excel).</summary>
    HeadersFooters,

    /// <summary>Hidden text in Word: runs with <c>w:vanish</c> or <c>w:specVanish</c>. White-on-white text is reported as a tip, never as a finding.</summary>
    HiddenText,

    /// <summary>Shapes, pictures and drawings marked hidden.</summary>
    InvisibleContent,

    /// <summary>Presentation shapes placed completely outside the slide.</summary>
    OffSlideContent,

    /// <summary>Hidden rows and columns of worksheets.</summary>
    HiddenRowsColumns,

    /// <summary>Hidden and very hidden worksheets.</summary>
    HiddenSheets,

    /// <summary>Defined names marked hidden (other than the names Excel itself keeps hidden).</summary>
    HiddenNames,

    /// <summary>Hidden slides.</summary>
    HiddenSlides,

    /// <summary>Speaker notes.</summary>
    SpeakerNotes,

    /// <summary>Ink annotations.</summary>
    Ink,

    /// <summary>Embedded documents and OLE objects. Reported only.</summary>
    EmbeddedObjects,

    /// <summary>VBA projects, macro sheets and XLM macros.</summary>
    Macros,

    /// <summary>ActiveX controls. Reported only.</summary>
    ActiveX,

    /// <summary>External workbook links, linked OLE objects and linked pictures.</summary>
    ExternalLinks,

    /// <summary>An attached template that is a file or a remote location.</summary>
    RemoteTemplates,

    /// <summary>Fields that pull in other files or run other programs: <c>INCLUDEPICTURE</c>, <c>INCLUDETEXT</c>, <c>LINK</c>, <c>DDE</c>, <c>DDEAUTO</c>. Reported only.</summary>
    LinkedContent,

    /// <summary>Data connections of a workbook (connection parts and query tables).</summary>
    DataConnections,

    /// <summary>Collapsed headings in Word, which hide the paragraphs under them.</summary>
    CollapsedHeadings,

    /// <summary>Office add-in task panes and web extensions.</summary>
    WebExtensions,

    /// <summary>PivotTable caches that hold a copy of source data. Reported only.</summary>
    PivotCaches,

    /// <summary>The workbook's data model (Power Pivot) and queries. Reported only.</summary>
    DataModel,

    /// <summary>The preview picture stored with the package.</summary>
    Thumbnails,
}

/// <summary>Names, defaults and capabilities of the <see cref="InspectionCategory"/> values.</summary>
public static class InspectionCategories
{
    private static readonly InspectionCategory[] s_all = Enum.GetValues<InspectionCategory>();

    private static readonly HashSet<InspectionCategory> s_reportOnly =
    [
        InspectionCategory.EmbeddedObjects,
        InspectionCategory.ActiveX,
        InspectionCategory.LinkedContent,
        InspectionCategory.PivotCaches,
        InspectionCategory.DataModel,
    ];

    /// <summary>Gets every category, in declaration order.</summary>
    public static IReadOnlyList<InspectionCategory> All => s_all;

    // Office's inspector removes (or offers to remove) everything except these; they are extensions, or changes to what the file is, so
    // the caller has to ask for them. Hidden slides and hidden names are not in Office's list at all, external links are found but never
    // removed, and a macro project changes the file format.
    private static readonly HashSet<InspectionCategory> s_askedFor =
    [
        InspectionCategory.ExternalLinks,
        InspectionCategory.HiddenNames,
        InspectionCategory.HiddenSlides,
        InspectionCategory.Macros,
    ];

    /// <summary>
    /// Gets the categories sanitising removes when the caller names none: the ones Office's Document Inspector removes, plus the preview
    /// picture, add-ins, data connections and the attached template, which it removes with the properties or leaves behind. Not in it:
    /// the categories that are only reported, and external links, hidden names, hidden slides and macros (see <see cref="Everything"/>).
    /// </summary>
    public static IReadOnlyList<InspectionCategory> Defaults { get; } = [.. s_all.Where(c => !s_reportOnly.Contains(c) && !s_askedFor.Contains(c))];

    /// <summary>Gets every category that can be removed: <see cref="Defaults"/> and the ones that have to be asked for.</summary>
    public static IReadOnlyList<InspectionCategory> Everything { get; } = [.. s_all.Where(c => !s_reportOnly.Contains(c))];

    /// <summary>Tells whether sanitising never removes a category, so that it can only be inspected.</summary>
    /// <param name="category">The category.</param>
    /// <returns>True when the category is only reported.</returns>
    public static bool IsReportOnly(InspectionCategory category) => s_reportOnly.Contains(category);

    /// <summary>Gets the name used on the command line and in JSON: lower case, words joined by hyphens (<c>hidden-rows-columns</c>).</summary>
    /// <param name="category">The category.</param>
    /// <returns>The name.</returns>
    public static string Name(InspectionCategory category)
    {
        string s = category.ToString();
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsUpper(s[i]) && i > 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(s[i]));
        }

        return sb.ToString();
    }

    /// <summary>Parses a name written by <see cref="Name"/>, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <param name="category">The category.</param>
    /// <returns>True when the name is a category.</returns>
    public static bool TryParse(string? name, out InspectionCategory category)
    {
        foreach (InspectionCategory c in s_all)
        {
            if (string.Equals(Name(c), name, StringComparison.OrdinalIgnoreCase))
            {
                category = c;
                return true;
            }
        }

        category = default;
        return false;
    }
}
