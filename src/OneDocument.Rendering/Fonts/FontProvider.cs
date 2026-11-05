using OneFont;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>How closely a resolved font stands in for the one a document asked for.</summary>
public enum FontMatchKind
{
    /// <summary>The requested family itself was found (installed, supplied by the caller, or embedded in the document).</summary>
    Exact = 0,

    /// <summary>A different family with the same glyph advances (Calibri to Carlito, Arial to Liberation Sans). Line breaks are expected to agree.</summary>
    MetricCompatible = 1,

    /// <summary>The closest available family by design (Aptos to Carlito, Segoe UI to Liberation Sans). Widths differ, so line breaks may differ.</summary>
    Approximate = 2,

    /// <summary>Nothing similar by name; the family class, PANOSE or charset hints chose a default sans, serif or monospaced face.</summary>
    Generic = 3,
}

/// <summary>
/// What a document asks of a font: a family name as written in the document (after theme resolution) with the style and hints
/// the font table gives for it.
/// </summary>
/// <param name="Family">The family name, for example <c>Calibri</c>. Compared without regard to case.</param>
/// <param name="Bold">True for bold weight.</param>
/// <param name="Italic">True for italic or oblique style.</param>
/// <param name="FamilyClass">The <c>w:family</c> value of the document's font table entry (<c>roman</c>, <c>swiss</c>, <c>modern</c>, <c>script</c>, <c>decorative</c>, <c>auto</c>), or null.</param>
/// <param name="Panose">The <c>w:panose1</c> value (ten hex pairs), or null.</param>
/// <param name="Charset">The <c>w:charset</c> value (two hex digits as a number), or null.</param>
public readonly record struct FontRequest(string Family, bool Bold = false, bool Italic = false, string? FamilyClass = null, string? Panose = null, int? Charset = null);

/// <summary>A font a provider chose, with how it relates to the request.</summary>
/// <param name="Face">The font face to shape and paint with.</param>
/// <param name="RequestedFamily">The family that was asked for.</param>
/// <param name="ActualFamily">The family name of <paramref name="Face"/>.</param>
/// <param name="Kind">How closely the face stands in for the request.</param>
public sealed record FontMatch(FontFace Face, string RequestedFamily, string ActualFamily, FontMatchKind Kind)
{
    /// <summary>Gets whether the face is the requested family.</summary>
    public bool IsExact => Kind == FontMatchKind.Exact;
}

/// <summary>A report that the layout used a different font from the one the document names. Substitutions are never silent.</summary>
/// <param name="Requested">The family the document asked for.</param>
/// <param name="Actual">The family used.</param>
/// <param name="Kind">How close the stand-in is.</param>
public sealed record FontSubstitutionNote(string Requested, string Actual, FontMatchKind Kind)
{
    /// <summary>Gets a one-line description.</summary>
    public string Message => Kind switch
    {
        FontMatchKind.MetricCompatible => $"Font \"{Requested}\" is not available; \"{Actual}\" has the same glyph widths and was used instead.",
        FontMatchKind.Approximate => $"Font \"{Requested}\" is not available; \"{Actual}\" is only an approximate match, so line breaks may differ from the original application.",
        FontMatchKind.Generic => $"Font \"{Requested}\" is not available and has no known stand-in; the generic face \"{Actual}\" was used.",
        _ => $"Font \"{Requested}\" was used.",
    };
}

/// <summary>
/// Supplies fonts to the layout. The standard implementation is <see cref="FontLibrary"/>; implement this to bring fonts from
/// another place. Calls come from one layout at a time and need not be thread-safe, but a provider shared by several layouts must be.
/// </summary>
public interface IFontProvider
{
    /// <summary>Chooses a face for a request.</summary>
    /// <param name="request">The family, style and hints.</param>
    /// <returns>The match, or null when the provider has nothing at all (the layout then reports a defect and uses its own default).</returns>
    FontMatch? Resolve(in FontRequest request);

    /// <summary>
    /// Finds a face that has a glyph for a character the chosen face lacks (a script fallback).
    /// </summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <param name="bold">Whether bold is wanted.</param>
    /// <param name="italic">Whether italic is wanted.</param>
    /// <returns>A face with the glyph, or null when none is known.</returns>
    FontFace? FindFallback(int codePoint, bool bold, bool italic);
}
