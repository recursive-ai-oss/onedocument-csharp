using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>What a removal took out of the file.</summary>
public enum VbaRemovedKind
{
    /// <summary>The project itself: <c>vbaProject.bin</c>, the <c>Macros</c> or <c>_VBA_PROJECT_CUR</c> storage, or the PowerPoint project storage.</summary>
    Project = 0,

    /// <summary>A signature part or stream that belonged to the project.</summary>
    Signature = 1,

    /// <summary>The form-data part that pairs a Word document with its project (<c>vbaData.xml</c>).</summary>
    FormData = 2,

    /// <summary>A record or flag that pointed at the project (the Excel project record, the PowerPoint macro flag).</summary>
    Marker = 3,

    /// <summary>A relationship that pointed at a removed part.</summary>
    Relationship = 4,

    /// <summary>The main part's content type, changed from the macro-enabled one to the macro-free one.</summary>
    ContentType = 5,
}

/// <summary>One thing a removal took out or changed.</summary>
/// <param name="Kind">What it was.</param>
/// <param name="Location">Where it was: a part name, a storage or stream path, or a record offset.</param>
/// <param name="Bytes">The size taken out, or 0 when not applicable.</param>
/// <param name="Description">A short statement of what was done.</param>
public sealed record VbaRemovedItem(VbaRemovedKind Kind, string Location, long Bytes, string Description);

/// <summary>Something the removal could not do or chose to leave, with a stable code (<c>vba.*</c>) and where it is.</summary>
/// <param name="Code">The code, listed in the defect catalogue.</param>
/// <param name="Message">What was left and why.</param>
/// <param name="Location">Where it is.</param>
public sealed record VbaRemovalNote(string Code, string Message, DefectLocation Location);

/// <summary>
/// What <see cref="VbaRemoval"/> did: every item taken out, the format change, and what it left or could not do. A removal never
/// reports success as a bare boolean.
/// </summary>
public sealed class VbaRemovalReport
{
    private readonly List<VbaRemovedItem> _removed = [];
    private readonly List<VbaRemovalNote> _notes = [];

    internal VbaRemovalReport(OfficeFormat sourceFormat)
    {
        SourceFormat = sourceFormat;
        OutputFormat = sourceFormat;
    }

    /// <summary>Gets the format of the input.</summary>
    public OfficeFormat SourceFormat { get; }

    /// <summary>Gets the format of the output: the macro-free counterpart for macro-enabled OOXML (<c>DOCM</c> becomes <c>DOCX</c>), the same format otherwise.</summary>
    public OfficeFormat OutputFormat { get; internal set; }

    /// <summary>Gets a value indicating whether the input held a VBA project. When false the output is a copy.</summary>
    public bool HadProject { get; internal set; }

    /// <summary>Gets the number of modules the removed project held (0 when it could not be read, or there was none).</summary>
    public int ModulesRemoved { get; internal set; }

    /// <summary>Gets the signature kinds that were removed with the project (a signature belongs to the project and cannot outlive it).</summary>
    public VbaSignatureKinds SignaturesRemoved { get; internal set; }

    /// <summary>Gets everything taken out or changed, in the order it was done.</summary>
    public IReadOnlyList<VbaRemovedItem> Removed => _removed;

    /// <summary>Gets what was left in place or could not be done.</summary>
    public IReadOnlyList<VbaRemovalNote> NotDone => _notes;

    /// <summary>Gets the total size in bytes of the project and signature data taken out.</summary>
    public long BytesRemoved
    {
        get
        {
            long n = 0;
            foreach (VbaRemovedItem i in _removed)
            {
                n += i.Bytes;
            }

            return n;
        }
    }

    internal void Add(VbaRemovedKind kind, string location, long bytes, string description) => _removed.Add(new VbaRemovedItem(kind, location, bytes, description));

    internal void Note(string code, string message, DefectLocation location) => _notes.Add(new VbaRemovalNote(code, message, location));
}
