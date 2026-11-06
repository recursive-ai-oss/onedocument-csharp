using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Rtf;

/// <summary>Maps the RTF layer's <see cref="RtfDefect"/> values onto the shared <see cref="Defect"/> vocabulary (<c>rtf.*</c> codes).</summary>
public static class RtfDefects
{
    /// <summary>
    /// The code of a kind: <c>rtf.</c> and the kind's name in kebab case (<c>rtf.unclosed-groups</c>). The codes are listed in <c>docs/defects.md</c>.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The stable code.</returns>
    public static string CodeOf(RtfDefectKind kind)
    {
        string name = kind.ToString();
        var sb = new StringBuilder("rtf.", 32);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Converts one RTF defect to the shared type.</summary>
    /// <param name="defect">The RTF defect.</param>
    /// <param name="stream">The name to put in the location: a file path, a part name or <c>rtf</c>.</param>
    /// <returns>A <see cref="Defect"/> of kind <see cref="DefectKind.Record"/> (<see cref="DefectKind.Limit"/> for limits) located at the byte offset.</returns>
    public static Defect ToDefect(RtfDefect defect, string stream = "rtf")
    {
        ArgumentNullException.ThrowIfNull(stream);
        bool limit = defect.Kind is RtfDefectKind.NestingLimitExceeded or RtfDefectKind.OutputLimitExceeded or RtfDefectKind.ExpansionRatioExceeded
            or RtfDefectKind.PictureLimitExceeded or RtfDefectKind.ContentLimitExceeded;
        DefectSeverity severity = defect.Kind switch
        {
            RtfDefectKind.NotRtf => DefectSeverity.Fatal,
            RtfDefectKind.NestedTableFlattened or RtfDefectKind.InvalidPictureData or RtfDefectKind.PictureLimitExceeded
                or RtfDefectKind.ContentLimitExceeded or RtfDefectKind.NestingLimitExceeded or RtfDefectKind.OutputLimitExceeded
                or RtfDefectKind.ExpansionRatioExceeded or RtfDefectKind.UndefinedReference => DefectSeverity.Dropped,
            _ => DefectSeverity.Repaired,
        };
        return new Defect(limit ? DefectKind.Limit : DefectKind.Record, CodeOf(defect.Kind), severity, defect.Message, DefectLocation.InRecord(stream, defect.Offset));
    }

    /// <summary>Reports every defect in a list to a sink.</summary>
    /// <param name="defects">The RTF defects.</param>
    /// <param name="sink">Where they go.</param>
    /// <param name="stream">The name for the location.</param>
    public static void ReportTo(IEnumerable<RtfDefect> defects, IDefectSink sink, string stream = "rtf")
    {
        ArgumentNullException.ThrowIfNull(defects);
        ArgumentNullException.ThrowIfNull(sink);
        foreach (RtfDefect d in defects)
        {
            sink.Report(ToDefect(d, stream));
        }
    }
}
