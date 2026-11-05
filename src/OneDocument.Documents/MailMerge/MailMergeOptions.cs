using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.MailMerge;

/// <summary>How the records of a mail merge become documents.</summary>
public enum MailMergeMode
{
    /// <summary>One document per pass over the main document: a record each, or several when the document uses <c>NEXT</c>.</summary>
    PerRecord = 0,

    /// <summary>One document holding every pass, one after another, each ending in a next-page section break (Word's "merge to new document").</summary>
    SingleDocument = 1,
}

/// <summary>Options of <see cref="Document.MailMerge(object?, MailMergeOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class MailMergeOptions
{
    /// <summary>Gets the shape of the result. The default is <see cref="MailMergeMode.PerRecord"/>.</summary>
    public MailMergeMode Mode { get; init; }

    /// <summary>
    /// Gets the answers to <c>ASK</c> (keyed by bookmark name) and <c>FILLIN</c> (keyed by prompt) fields. A field with no answer
    /// uses its <c>\d</c> default, else an empty string; nothing is ever asked interactively. Keys compare exactly. The default is none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Answers { get; init; }

    /// <summary>Gets the most records one merge reads. The default is 100,000; more throws <see cref="OfficeLimitException"/>.</summary>
    public int MaxRecords { get; init; } = 100_000;
}

/// <summary>The outcome of a mail merge: the documents and what the merge could not do.</summary>
public sealed class MailMergeResult : IDisposable
{
    internal MailMergeResult(IReadOnlyList<Document> documents, IReadOnlyList<Defect> warnings)
    {
        Documents = documents;
        Warnings = warnings;
    }

    /// <summary>
    /// Gets the merged documents: one per pass in <see cref="MailMergeMode.PerRecord"/> mode, at most one in
    /// <see cref="MailMergeMode.SingleDocument"/> mode. Empty when the data has no records (or every record was skipped). The
    /// documents are independent of the template, which is not changed, and belong to the caller.
    /// </summary>
    public IReadOnlyList<Document> Documents { get; }

    /// <summary>
    /// Gets the problems met while merging, each with a <c>mailmerge.</c> code and the part and offset of the field in the
    /// template: <c>mailmerge.field-missing</c> (the record has no such field; it merged as blank), <c>mailmerge.field-malformed</c>,
    /// <c>mailmerge.field-unsupported</c> (the field type is not expanded and stays) and <c>mailmerge.field-unbalanced</c>.
    /// </summary>
    public IReadOnlyList<Defect> Warnings { get; }

    /// <summary>Disposes every document in <see cref="Documents"/>.</summary>
    public void Dispose()
    {
        foreach (Document d in Documents)
        {
            d.Dispose();
        }
    }
}
