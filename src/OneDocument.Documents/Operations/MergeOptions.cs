namespace RecursiveAI.OneDocument.Documents;

/// <summary>How a style in the source that has the same name as a style in the destination, but a different definition, is settled.</summary>
public enum StyleConflict
{
    /// <summary>Content that uses the style takes the destination's definition (what Word does when text is inserted from a file).</summary>
    KeepDestination = 0,

    /// <summary>The destination's definition is replaced by the source's; existing destination content that uses the style changes its look.</summary>
    UseSource = 1,

    /// <summary>The source's style is added next to the destination's under a new name (the name with <c>(2)</c>, <c>(3)</c> … added) and the copied content uses it.</summary>
    KeepBoth = 2,
}

/// <summary>What happens to lists of the source whose list definition already exists in the destination.</summary>
public enum NumberingContinuation
{
    /// <summary>The copied paragraphs join the destination's list of the same definition and carry on its numbering.</summary>
    Continue = 0,

    /// <summary>The copied paragraphs form a new list that starts again at the first number of each level.</summary>
    Restart = 1,
}

/// <summary>Whose page setup (size, margins, columns, headers and footers) the copied content has.</summary>
public enum SectionHandling
{
    /// <summary>The copied content continues the destination's section and takes its page setup; sections that end inside the copied range keep theirs.</summary>
    UseDestination = 0,

    /// <summary>The destination's section ends before the copied content and the source's last section describes it, headers and footers included.</summary>
    KeepSource = 1,
}

/// <summary>
/// Choices for appending or inserting one document into another (<see cref="Document.Append(Document, MergeOptions?)"/>,
/// <see cref="Document.Insert(int, Document, MergeOptions?)"/>). The defaults are what Word does for Insert → Text from File.
/// </summary>
public sealed class MergeOptions
{
    /// <summary>Gets or sets how style conflicts are settled. Default <see cref="StyleConflict.KeepDestination"/>.</summary>
    public StyleConflict Styles { get; set; } = StyleConflict.KeepDestination;

    /// <summary>Gets or sets what happens to lists whose definition exists in the destination. Default <see cref="NumberingContinuation.Continue"/>.</summary>
    public NumberingContinuation Numbering { get; set; } = NumberingContinuation.Continue;

    /// <summary>Gets or sets whose page setup the copied content has. Default <see cref="SectionHandling.UseDestination"/>.</summary>
    public SectionHandling Sections { get; set; } = SectionHandling.UseDestination;

    /// <summary>Gets or sets the index of the first body block of the source to copy (blocks are the paragraphs, tables and content controls directly in the body). Default 0.</summary>
    public int SourceStart { get; set; }

    /// <summary>Gets or sets how many blocks to copy from <see cref="SourceStart"/>; null (the default) copies to the end.</summary>
    public int? SourceCount { get; set; }
}
