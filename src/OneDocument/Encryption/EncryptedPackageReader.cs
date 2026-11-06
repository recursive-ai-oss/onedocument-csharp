using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Encryption;

/// <summary>
/// Opens an encrypted OOXML package through the format registry: the compound file is decrypted with the password of the open context,
/// and the package inside is sniffed and opened as whatever it is (a document, workbook or presentation). The result has no file
/// path, so <c>Save()</c> is not available and <c>SaveAs</c> writes it unencrypted.
/// </summary>
internal static class EncryptedPackageReader
{
    internal static void Register(FormatRegistry registry) =>
        registry.RegisterReader((source, _, context, _, token) => Open(registry, source, context, token), OfficeFormat.EncryptedPackage);

    private static OfficeFile Open(FormatRegistry registry, IByteSource source, OpenContext context, CancellationToken cancellationToken)
    {
        IByteSource? inner = null;
        try
        {
            byte[] plain = PackageEncryption.DecryptCompoundFile(source, context);
            cancellationToken.ThrowIfCancellationRequested();
            inner = ByteSource.FromMemory(plain);
            OfficeFormat format = FormatSniffer.Detect(inner, context.Limits);
            if (format is OfficeFormat.Unknown or OfficeFormat.EncryptedPackage)
            {
                throw new OfficeFormatException(DefectKind.Encryption, "encryption.inner-format", "The decrypted package is not a format OneDocument knows.", DefectLocation.InRecord("/EncryptedPackage", 8));
            }

            OfficeFile file = registry.GetReader(format)(inner, format, context, null, cancellationToken);
            inner = null;
            source.Dispose();
            return file;
        }
        catch
        {
            inner?.Dispose();
            source.Dispose();
            throw;
        }
    }
}
