using RecursiveAI.OneArchive;
using RecursiveAI.OneCompress;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Packaging;

/// <summary>
/// Maps the container library's exceptions onto OneOffice's typed hierarchy, so that nothing from the layer below leaks out of a public
/// API. Every place that calls into zip or compound-file code wraps the call and translates what it catches here.
/// </summary>
internal static class ArchiveErrors
{
    /// <summary>Translates an archive exception into the matching <see cref="OfficeException"/>.</summary>
    /// <param name="ex">The exception from the container library.</param>
    /// <param name="location">Where in the input the call was working, if known.</param>
    internal static OfficeException Translate(CompressionException ex, DefectLocation location = default)
    {
        switch (ex)
        {
            case CompressionLimitException limit:
                return new OfficeLimitException(MapLimitName(limit.LimitName), limit.Limit, limit.Requested, location, ex);
            case ArchivePasswordException password:
                return new OfficeEncryptedException(
                    password.Problem == ArchivePasswordProblem.Required ? EncryptionProblem.PasswordRequired : EncryptionProblem.PasswordIncorrect,
                    location,
                    ex);
            case ArchiveUnsupportedException unsupported:
                return new OfficeUnsupportedException(unsupported.Feature, location, ex);
            case CompressionFormatException format:
                return new OfficeFormatException(
                    DefectKind.Packaging,
                    "packaging.damaged",
                    format.Message,
                    location.Kind == DefectLocationKind.None ? DefectLocation.InPart(string.Empty, format.Offset) : location,
                    ex);
            default:
                // Safety refusals (dangerous paths, overlapping entries) and anything else the archive layer refuses.
                return new OfficeFormatException(DefectKind.Packaging, "packaging.refused", ex.Message, location, ex);
        }
    }

    // The archive limit names differ from ours; the ones that have an equivalent are renamed so callers see OfficeLimits names.
    private static string MapLimitName(string archiveName) => archiveName switch
    {
        nameof(ArchiveLimits.MaxTotalSize) => nameof(OfficeLimits.MaxTotalUncompressedSize),
        nameof(ArchiveLimits.MaxEntryCount) => nameof(OfficeLimits.MaxPartCount),
        nameof(ArchiveLimits.MaxExpansionRatio) => nameof(OfficeLimits.MaxExpansionRatio),
        _ => archiveName,
    };
}
