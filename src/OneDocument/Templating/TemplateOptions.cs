using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Templating;

/// <summary>The characters that open and close tags. The default is <c>{{ }}</c>, <c>{% %}</c> and <c>{# #}</c>.</summary>
/// <param name="ExpressionOpen">Opens an expression.</param>
/// <param name="ExpressionClose">Closes an expression.</param>
/// <param name="BlockOpen">Opens a block tag.</param>
/// <param name="BlockClose">Closes a block tag.</param>
/// <param name="CommentOpen">Opens a comment.</param>
/// <param name="CommentClose">Closes a comment.</param>
public sealed record TemplateDelimiters(
    string ExpressionOpen = "{{",
    string ExpressionClose = "}}",
    string BlockOpen = "{%",
    string BlockClose = "%}",
    string CommentOpen = "{#",
    string CommentClose = "#}")
{
    /// <summary>The default delimiters.</summary>
    public static TemplateDelimiters Default { get; } = new();

    /// <summary>Checks that every delimiter is non-empty and that the three openers differ, none being a prefix of another.</summary>
    /// <exception cref="ArgumentException">The delimiters cannot be told apart.</exception>
    public void Validate()
    {
        string[] open = [ExpressionOpen, BlockOpen, CommentOpen];
        string[] close = [ExpressionClose, BlockClose, CommentClose];
        foreach (string s in open)
        {
            if (string.IsNullOrEmpty(s))
            {
                throw new ArgumentException("A delimiter is empty.", nameof(ExpressionOpen));
            }
        }

        foreach (string s in close)
        {
            if (string.IsNullOrEmpty(s))
            {
                throw new ArgumentException("A delimiter is empty.", nameof(ExpressionClose));
            }
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (i != j && open[i].StartsWith(open[j], StringComparison.Ordinal))
                {
                    throw new ArgumentException("An opening delimiter is the start of another.", nameof(ExpressionOpen));
                }
            }
        }
    }
}

/// <summary>How a template is read and rendered. Instances are immutable.</summary>
public sealed class TemplateOptions
{
    /// <summary>The tag delimiters.</summary>
    public TemplateDelimiters Delimiters { get; init; } = TemplateDelimiters.Default;

    /// <summary>
    /// When true, data a template names but the data does not have, and an expression that evaluates to an error, throw <see cref="TemplateException"/>
    /// at the first one. When false (the default) a missing value renders empty and is reported as the warning <c>template.missing-data</c>.
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>Limits for formula length and depth, block nesting and include recursion. Null means <see cref="OfficeLimits.Default"/>.</summary>
    public OfficeLimits? Limits { get; init; }

    /// <summary>Where warnings and errors go as they are found. Null collects them only in the result.</summary>
    public IDefectSink? Defects { get; init; }

    /// <summary>The functions expressions may call; null means <see cref="FunctionRegistry.CreateDefault"/>. Registered C# functions (<see cref="FunctionRegistry.AddCustom"/>) are the only way to extend the engine.</summary>
    public FunctionRegistry? Functions { get; init; }

    /// <summary>The clock <c>TODAY</c> and <c>NOW</c> read. Null means those functions return the default clock's time, which makes output depend on it; pass a fixed clock for reproducible output.</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>The seed of <c>RAND</c> and friends, so output is reproducible. Default 0.</summary>
    public ulong RandomSeed { get; init; }

    /// <summary>The binder's scope callback, or null.</summary>
    public ITemplateScopeResolver? Scopes { get; init; }

    /// <summary>Where <c>{% include %}</c> finds templates. Null denies every include (<c>template.include-denied</c>).</summary>
    public ITemplateIncludes? Includes { get; init; }

    /// <summary>The most loop iterations in one render, all loops together. Default 1,000,000.</summary>
    public long MaxIterations { get; init; } = 1_000_000;

    /// <summary>The most characters one render may produce (text pieces, summed). Default 64 million.</summary>
    public long MaxOutputCharacters { get; init; } = 64L << 20;

    /// <summary>The deepest nesting of blocks and includes. Default 64 (also bounded by <see cref="OfficeLimits.MaxRecursionDepth"/>).</summary>
    public int MaxBlockDepth { get; init; } = 64;

    /// <summary>The most rows read from a data reader or enumerable when data is adapted. Default 1,000,000.</summary>
    public int MaxRows { get; init; } = 1_000_000;
}

/// <summary>A template that cannot be rendered (parse errors), or strict-mode data that is missing. The defect holds the code and location.</summary>
public sealed class TemplateException : OfficeFormatException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="defect">The defect.</param>
    public TemplateException(Defect defect)
        : base(defect)
    {
    }
}

/// <summary>What a piece of output is.</summary>
public enum TemplatePieceKind
{
    /// <summary>Text: either a stretch of the template that is kept, or the printed value of a tag.</summary>
    Text = 0,

    /// <summary>An <c>{% image %}</c> tag; the binder inserts the picture.</summary>
    Image = 1,

    /// <summary>An <c>{% html %}</c> tag; the binder converts and inserts the markup.</summary>
    Html = 2,

    /// <summary>A <c>{% markdown %}</c> tag; the binder converts and inserts the markdown.</summary>
    Markdown = 3,
}

/// <summary>The evaluated arguments of an <c>image</c>, <c>html</c> or <c>markdown</c> tag, for the binder to render.</summary>
/// <param name="Value">The value the tag's expression produced, as data (a scalar holding text or <c>byte[]</c> in <see cref="ITemplateData.Raw"/>).</param>
/// <param name="Options">The <c>name=value</c> options written after the expression (names lower-cased).</param>
public sealed record TemplateInsertion(ITemplateData Value, IReadOnlyDictionary<string, string> Options);

/// <summary>One stretch of output inside a rendered unit.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Segment">The index, in the source unit, of the segment whose formatting the piece takes: the segment the text came from, or where the tag began.</param>
/// <param name="Text">The text; empty for a tag that printed nothing and for non-text pieces.</param>
/// <param name="Insertion">The arguments of an image, HTML or markdown tag; null for text.</param>
/// <param name="Typed">For the printed value of a <c>{{ }}</c> tag: the value as a number, boolean, error or text rather than its printed form, so a binder whose unit is a single tag (a spreadsheet cell) can keep the type. Null for kept template text, other tags, and values that are collections or records.</param>
public sealed record TemplatePiece(TemplatePieceKind Kind, int Segment, string Text, TemplateInsertion? Insertion = null, CalcValue? Typed = null);

/// <summary>One copy of a source unit in the output, with what it now says.</summary>
/// <param name="SourceUnit">The index of the unit in the <see cref="TemplateSource"/> it is a copy of.</param>
/// <param name="Pieces">The pieces in order. A binder rebuilds the unit with one run per piece, each taking the formatting of the segment it names.</param>
public sealed record TemplateRenderedUnit(int SourceUnit, IReadOnlyList<TemplatePiece> Pieces)
{
    /// <summary>The text pieces joined (non-text pieces contribute nothing).</summary>
    public string Text
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (TemplatePiece p in Pieces)
            {
                sb.Append(p.Text);
            }

            return sb.ToString();
        }
    }
}

/// <summary>The result of rendering a template.</summary>
/// <param name="Units">The output units in order. A source unit may appear many times (a loop body) or not at all (a false branch, a tag-only unit).</param>
/// <param name="Defects">The warnings found while rendering (<c>template.missing-data</c> and the like); errors throw instead.</param>
public sealed record TemplateResult(IReadOnlyList<TemplateRenderedUnit> Units, IReadOnlyList<Defect> Defects)
{
    /// <summary>The output as plain text, one line per unit, lines joined with <c>\n</c>.</summary>
    public string ToText() => string.Join('\n', Units.Select(static u => u.Text));
}
