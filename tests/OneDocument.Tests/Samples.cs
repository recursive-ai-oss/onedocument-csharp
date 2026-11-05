using System.IO.Compression;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>Tiny inputs generated in memory for the sniffer, the source and the registry tests.</summary>
internal static class Samples
{
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using Stream s = e.Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return ms.ToArray();
    }

    public static byte[] Cfb(params string[] streamNames)
    {
        using var ms = new MemoryStream();
        using (var writer = new CompoundFileWriter(ms, leaveOpen: true))
        {
            foreach (string name in streamNames)
            {
                byte[] data = [1, 2, 3, 4];
                writer.WriteEntry(new ArchiveEntry { Name = name, Kind = ArchiveEntryKind.File, Size = data.Length }, data);
            }

            writer.Finish();
        }

        return ms.ToArray();
    }

    public static string RootRels(string target) => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"" + target + "\"/></Relationships>";

    public static byte[] Opc(string mainPart, string mainContentType, string? rels = null) =>
        Zip(
            ("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                $"<Override PartName=\"/{mainPart}\" ContentType=\"{mainContentType}\"/></Types>"),
            ("_rels/.rels", rels ?? RootRels(mainPart)),
            (mainPart, "<x/>"));

    public static byte[] Odf(string mimetype) => Zip(("mimetype", mimetype), ("content.xml", "<x/>"));

    public static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);
}
