using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Turns the failures malformed record data can cause in the binary translators into one typed exception.</summary>
internal static class XlsbGuard
{
    /// <summary>Returns true for the exceptions a short or inconsistent record produces; type errors in our own code are not among them for long, and cancellation and out-of-memory never are.</summary>
    public static bool IsMalformed(Exception e) =>
        e is FormatException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or OverflowException
            or InvalidCastException or NullReferenceException or KeyNotFoundException or ArithmeticException
            or InsufficientExecutionStackException or System.Text.DecoderFallbackException && e is not OperationCanceledException;

    /// <summary>Wraps the failure as a record defect located in the part.</summary>
    public static OfficeFormatException Typed(Exception e, string partName) =>
        new(DefectKind.Record, "xlsb.part-unreadable", "The part could not be read: " + e.Message, DefectLocation.InPart(partName), e);
}
