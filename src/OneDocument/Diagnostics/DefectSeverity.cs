namespace RecursiveAI.OneDocument.Diagnostics;

/// <summary>What the reader did about a <see cref="Defect"/>.</summary>
public enum DefectSeverity
{
    /// <summary>The reader repaired the input and kept all the content, as Office would have.</summary>
    Repaired = 0,

    /// <summary>The reader could not keep some content and dropped it; the rest is intact.</summary>
    Dropped = 1,

    /// <summary>The input cannot be read. The reader reports the defect and raises an <see cref="OfficeException"/> that carries it.</summary>
    Fatal = 2,
}
