using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc;

/// <summary>
/// Last-resort translation for the Word binary readers. The readers check every length and offset themselves and report what they
/// repair; this turns an arithmetic or indexing failure from a structure they did not foresee into the typed exception the contract
/// promises, instead of letting it escape.
/// </summary>
internal static class DocGuard
{
    /// <summary>Runs <paramref name="action"/> and maps unexpected failures on malformed input to <see cref="OfficeFormatException"/> (<c>doc.malformed</c>).</summary>
    internal static T Run<T>(Func<T> action, string stream = "/WordDocument")
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidCastException or OverflowException
            or InvalidOperationException or FormatException or NullReferenceException or KeyNotFoundException or ArithmeticException or OutOfMemoryException)
        {
            throw new OfficeFormatException(DefectKind.Record, "doc.malformed", "The Word binary file is damaged in a way the reader could not repair: " + ex.GetType().Name + ".", DefectLocation.InRecord(stream), ex);
        }
    }

    /// <summary>Runs an action that returns nothing; see <see cref="Run{T}(Func{T}, string)"/>.</summary>
    internal static void Run(Action action, string stream = "/WordDocument") =>
        Run<object?>(() =>
        {
            action();
            return null;
        }, stream);
}
