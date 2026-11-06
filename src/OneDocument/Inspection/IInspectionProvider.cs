using System.Linq;
namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// Inspects and sanitises the parts one kind of document owns, over an OOXML package. The kinds' packages register one each
/// (<see cref="Formats.FormatRegistry.RegisterInspectionProvider"/>); the categories every package shares are handled by the core
/// package and need no provider.
/// </summary>
/// <remarks>
/// A provider reports with <see cref="InspectionSession.Add"/> and, when sanitising, removes with the <see cref="SanitizeSession"/> helpers and
/// records every removal with <see cref="SanitizeSession.Removed"/>. A category the caller did not select (<see cref="SanitizeSession.Selected"/>)
/// is left alone. Whatever a provider finds in a selected category and does not remove it must record with
/// <see cref="SanitizeSession.NotRemoved"/>, or the verification that follows sanitising fails. Providers hold no state between calls.
/// </remarks>
public interface IInspectionProvider
{
    /// <summary>Reports what the kind's parts hold.</summary>
    /// <param name="session">The package and the report.</param>
    void Inspect(InspectionSession session);

    /// <summary>Removes what the caller selected from the kind's parts.</summary>
    /// <param name="session">The package and the record of what was done.</param>
    void Sanitize(SanitizeSession session);
}

/// <summary>Which categories sanitising removes.</summary>
public sealed class SanitizeOptions
{
    private readonly HashSet<InspectionCategory> _categories;

    private SanitizeOptions(IEnumerable<InspectionCategory> categories) => _categories = [.. categories];

    /// <summary>Gets the options that remove the categories Office's Document Inspector removes (<see cref="InspectionCategories.Defaults"/>).</summary>
    public static SanitizeOptions Default { get; } = new(InspectionCategories.Defaults);

    /// <summary>Gets the options that remove every category that can be removed (<see cref="InspectionCategories.Everything"/>), including macros, external links, hidden names and hidden slides.</summary>
    public static SanitizeOptions Everything { get; } = new(InspectionCategories.Everything);

    /// <summary>Gets the categories to remove.</summary>
    public IReadOnlyCollection<InspectionCategory> Categories => _categories;

    /// <summary>Creates options that remove only the named categories.</summary>
    /// <param name="categories">The categories.</param>
    /// <returns>The options.</returns>
    public static SanitizeOptions Only(params InspectionCategory[] categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        return new SanitizeOptions(categories);
    }

    /// <summary>Creates options that remove the default categories except the named ones.</summary>
    /// <param name="keep">The categories to keep.</param>
    /// <returns>The options.</returns>
    public static SanitizeOptions Except(params InspectionCategory[] keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return Default.Without(keep);
    }

    /// <summary>Creates options with the same categories except the named ones.</summary>
    /// <param name="categories">The categories to leave out.</param>
    /// <returns>The new options.</returns>
    public SanitizeOptions Without(params InspectionCategory[] categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        return new SanitizeOptions(_categories.Where(c => Array.IndexOf(categories, c) < 0));
    }

    /// <summary>Tells whether a category is selected.</summary>
    /// <param name="category">The category.</param>
    /// <returns>True when sanitising removes it.</returns>
    public bool Includes(InspectionCategory category) => _categories.Contains(category);
}
