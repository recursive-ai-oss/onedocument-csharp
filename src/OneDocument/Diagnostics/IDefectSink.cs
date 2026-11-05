namespace RecursiveAI.OneDocument.Diagnostics;

/// <summary>
/// Receives the defects a reader finds. Readers call <see cref="Report"/> from whatever thread they run on (calc and layout
/// parallelise), so an implementation must be safe to call concurrently. It must not throw.
/// </summary>
public interface IDefectSink
{
    /// <summary>Records a defect.</summary>
    /// <param name="defect">The defect.</param>
    void Report(Defect defect);
}

/// <summary>A sink that discards everything, for callers that only want the document.</summary>
public sealed class DiscardDefectSink : IDefectSink
{
    /// <summary>Gets the shared instance.</summary>
    public static DiscardDefectSink Instance { get; } = new();

    private DiscardDefectSink()
    {
    }

    /// <inheritdoc />
    public void Report(Defect defect)
    {
    }
}
