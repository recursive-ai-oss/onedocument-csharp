using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Cli;

/// <summary>Small text helpers shared by the commands.</summary>
internal static class CliText
{
    /// <summary>
    /// Returns <c> at &lt;location&gt;</c> for a defect line, or nothing when the defect has no location, so a line never reads
    /// <c>error x at : message</c>.
    /// </summary>
    /// <param name="location">The defect location.</param>
    /// <returns>The text to put between the code and the colon.</returns>
    public static string At(DefectLocation location)
    {
        string text = location.ToString();
        return text.Length == 0 ? string.Empty : " at " + text;
    }
}
