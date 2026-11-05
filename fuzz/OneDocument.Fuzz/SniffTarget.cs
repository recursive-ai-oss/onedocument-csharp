using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>Format sniffing, with the container readers it reaches (zip directory, compound-file directory).</summary>
internal static class SniffTarget
{
    // Small limits keep each execution fast; a hostile input is still exercised against the same code paths.
    private static readonly OfficeLimits s_limits = new() { MaxPartCount = 1000, MaxTotalUncompressedSize = 8L << 20, MaxPartSize = 1 << 20 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        try
        {
            OfficeFormat fromMemory = FormatSniffer.Detect(bytes, s_limits);

            // The same bytes through a stream-backed source must give the same answer.
            using IByteSource source = ByteSource.FromStream(new MemoryStream(bytes), leaveOpen: false);
            OfficeFormat fromStream = FormatSniffer.Detect(source, s_limits);
            if (fromMemory != fromStream)
            {
                throw new InvalidOperationException($"memory says {fromMemory}, stream says {fromStream}");
            }
        }
        catch (OfficeException)
        {
            // A typed rejection is the contract.
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "docx"), Zip(("[Content_Types].xml", "<Types xmlns=\"x\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"), ("_rels/.rels", "<Relationships xmlns=\"x\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"), ("word/document.xml", "<x/>")));
        File.WriteAllBytes(Path.Combine(directory, "odt"), Zip(("mimetype", "application/vnd.oasis.opendocument.text"), ("content.xml", "<x/>")));
        File.WriteAllText(Path.Combine(directory, "rtf"), "{\\rtf1\\ansi hello}");
        File.WriteAllText(Path.Combine(directory, "csv"), "a,b,c\n1,2,3\n");
        File.WriteAllText(Path.Combine(directory, "flatopc"), "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\"><pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"><pkg:xmlData><x/></pkg:xmlData></pkg:part></pkg:package>");
        File.WriteAllText(Path.Combine(directory, "html"), "<!DOCTYPE html><html><body>x</body></html>");
        return 0;
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return ms.ToArray();
    }
}
