using System;
using System.Collections.Generic;
using System.Linq;

namespace RecursiveAI.OneDocument.Templating;

/// <summary>
/// A piece of template text a binder hands to the engine: one run of a Word paragraph, one text run of a slide, one cell string. Tags may be split
/// across consecutive segments of one unit (Word does this whenever a tag was typed in more than one edit); the engine reads the segments of a unit
/// as one text and maps what it produces back to the segment each piece came from.
/// </summary>
/// <param name="Text">The segment's text. May be empty.</param>
public readonly record struct TemplateSegment(string Text);

/// <summary>
/// A structural unit the binder can repeat, drop or keep whole: a paragraph, a table-cell paragraph, a text line, a slide shape. Units are the
/// granularity of repetition: a loop whose tags sit in different units repeats the units between them. A tag never spans two units.
/// </summary>
public sealed class TemplateUnit
{
    /// <summary>Creates a unit.</summary>
    /// <param name="segments">The text segments in order.</param>
    /// <param name="location">Where the unit is for error messages: a part URI plus whatever the binder can add (<c>/word/document.xml</c>), or null.</param>
    public TemplateUnit(IReadOnlyList<TemplateSegment> segments, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments;
        Location = location;
    }

    /// <summary>Creates a unit of one segment.</summary>
    /// <param name="text">The text.</param>
    /// <param name="location">The location, or null.</param>
    public TemplateUnit(string text, string? location = null)
        : this([new TemplateSegment(text)], location)
    {
    }

    /// <summary>The segments, in order.</summary>
    public IReadOnlyList<TemplateSegment> Segments { get; }

    /// <summary>The binder-supplied location used in defects (an <c>InPart</c> location with the offset inside the unit's text), or null for a bare text offset.</summary>
    public string? Location { get; }

    /// <summary>The segments' text joined.</summary>
    public string Text
    {
        get
        {
            if (Segments.Count == 1)
            {
                return Segments[0].Text ?? string.Empty;
            }

            return string.Concat(Segments.Select(static s => s.Text ?? string.Empty));
        }
    }
}

/// <summary>What a binder gives the engine: units in document order.</summary>
public sealed class TemplateSource
{
    /// <summary>Creates a source.</summary>
    /// <param name="units">The units in document order.</param>
    public TemplateSource(IReadOnlyList<TemplateUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        Units = units;
    }

    /// <summary>The units in document order.</summary>
    public IReadOnlyList<TemplateUnit> Units { get; }

    /// <summary>Splits text into one unit per line (<c>\n</c> or <c>\r\n</c>); the plain-text binder.</summary>
    /// <param name="text">The text.</param>
    /// <param name="location">The location used in defects, or null.</param>
    /// <returns>The source. Text without a line break is one unit.</returns>
    public static TemplateSource FromText(string text, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var units = new List<TemplateUnit>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
                units.Add(new TemplateUnit(text[start..end], location));
                start = i + 1;
            }
        }

        return new TemplateSource(units);
    }
}

/// <summary>An inclusive range of unit indexes.</summary>
/// <param name="First">First unit.</param>
/// <param name="Last">Last unit.</param>
public readonly record struct TemplateUnitRange(int First, int Last);

/// <summary>What a binder is told about a block when it decides which units that block repeats or keeps.</summary>
/// <param name="Tag">The tag name: <c>for</c> or <c>if</c>.</param>
/// <param name="Scope">The scope the author wrote (<c>scope=table-row</c>), or null when the template did not say.</param>
/// <param name="OpenUnit">The unit holding the opening tag.</param>
/// <param name="CloseUnit">The unit holding the closing tag.</param>
/// <param name="Expression">The text of the loop source or condition.</param>
public readonly record struct TemplateBlockInfo(string Tag, string? Scope, int OpenUnit, int CloseUnit, string Expression);

/// <summary>
/// The scope callback of the binder contract. Without it a block repeats (or keeps) the text between its tags: inside one unit that is inline text,
/// across units it is the units strictly between the unit of the opening tag and the unit of the closing tag. A binder that knows the document's
/// structure (a table row around the tags, a slide, a worksheet row) widens the block.
/// </summary>
public interface ITemplateScopeResolver
{
    /// <summary>Gives the units a block covers.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The units to repeat or keep as a whole, tags included (the tags themselves print nothing), or null to keep the default meaning. The range must contain both tag units.</returns>
    TemplateUnitRange? Resolve(in TemplateBlockInfo block);
}

/// <summary>Finds the text of an <c>{% include %}</c>. The engine never reads files or the network itself; the default denies every include.</summary>
public interface ITemplateIncludes
{
    /// <summary>Gives an included template.</summary>
    /// <param name="name">The name written in the tag.</param>
    /// <returns>The template, or null when there is none (reported as <c>template.include-not-found</c>).</returns>
    TemplateSource? Find(string name);
}
