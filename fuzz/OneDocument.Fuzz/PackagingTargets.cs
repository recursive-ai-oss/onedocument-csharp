using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>The OPC package reader and writer: zip, content types, relationships, Flat OPC, and the rewrite of whatever opened.</summary>
internal static class OpcTarget
{
    // Small limits keep each execution fast; a hostile input is still exercised against the same code paths.
    private static readonly OfficeLimits s_limits = new() { MaxPartCount = 2000, MaxTotalUncompressedSize = 8L << 20, MaxPartSize = 1 << 20 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        try
        {
            using OpcPackage pkg = OpcPackage.Open(new MemoryStream(bytes), new OpenContext(s_limits));
            var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            bool complete = true;
            foreach (IPackagePart part in pkg.Parts)
            {
                _ = part.ContentType;
                _ = part.Length;
                try
                {
                    contents[part.Name] = part.GetBytes().ToArray();
                    using Stream s = part.OpenRead();
                    s.ReadByte();
                }
                catch (OfficeException)
                {
                    complete = false;
                }
            }

            foreach (Relationship r in pkg.GetRelationships())
            {
                OpcPackage.TryResolveTarget(null, r, out _);
            }

            foreach (IPackagePart part in pkg.Parts)
            {
                foreach (Relationship r in pkg.GetRelationships(part.Name))
                {
                    OpcPackage.TryResolveTarget(part.Name, r, out _);
                }
            }

            if (!complete)
            {
                return;
            }

            // What was read must come back from a rewrite: the same parts with the same bytes and content types.
            using var output = new MemoryStream();
            pkg.WriteTo(output);
            using OpcPackage back = OpcPackage.Open(new MemoryStream(output.ToArray()), new OpenContext(s_limits));
            if (back.Parts.Count != pkg.Parts.Count)
            {
                throw new InvalidOperationException($"rewrite changed the part count from {pkg.Parts.Count} to {back.Parts.Count}");
            }

            foreach (IPackagePart part in back.Parts)
            {
                if (!contents.TryGetValue(part.Name, out byte[]? want))
                {
                    throw new InvalidOperationException("rewrite invented the part " + part.Name);
                }

                // A part that came from Flat OPC loses its XML declaration inside the wrapper, so only zip input is compared byte for byte.
                if (!pkg.IsFlatOpc && !part.GetBytes().Span.SequenceEqual(want))
                {
                    throw new InvalidOperationException("rewrite changed the bytes of " + part.Name);
                }
            }

            using var flat = new MemoryStream();
            pkg.WriteFlatOpcTo(flat);
            using OpcPackage fromFlat = OpcPackage.Open(new MemoryStream(flat.ToArray()), new OpenContext(s_limits));
            if (fromFlat.Parts.Count != pkg.Parts.Count)
            {
                throw new InvalidOperationException("flat form changed the part count");
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
        const string Doc = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        string types = "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/>" +
            $"<Override PartName=\"/word/document.xml\" ContentType=\"{Doc}\"/></Types>";
        string rels = "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";
        string docRels = "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"a\" Type=\"t\" Target=\"media/image1.png\"/><Relationship Id=\"b\" Type=\"t\" Target=\"http://example.com/\" TargetMode=\"External\"/></Relationships>";
        byte[] zip = Zip(CompressionLevel.Optimal,
            ("[Content_Types].xml", types), ("_rels/.rels", rels), ("word/document.xml", "<w:document xmlns:w=\"x\"><w:body/></w:document>"),
            ("word/_rels/document.xml.rels", docRels), ("word/media/image1.png", "\u0089PNG....data"), ("word/media/caf%C3%A9.png", "x"));
        File.WriteAllBytes(Path.Combine(directory, "docx"), zip);
        File.WriteAllBytes(Path.Combine(directory, "docx-stored"), Zip(CompressionLevel.NoCompression, ("[Content_Types].xml", types), ("_rels/.rels", rels), ("word/document.xml", "<a/>")));
        File.WriteAllBytes(Path.Combine(directory, "truncated"), zip[..(zip.Length * 2 / 3)]);
        File.WriteAllText(Path.Combine(directory, "flat"), "<?xml version=\"1.0\" standalone=\"yes\"?><pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\"><pkg:part pkg:name=\"/_rels/.rels\" pkg:contentType=\"application/vnd.openxmlformats-package.relationships+xml\"><pkg:xmlData>" + rels.Replace("<?xml version=\"1.0\"?>", string.Empty, StringComparison.Ordinal) + $"</pkg:xmlData></pkg:part><pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"{Doc}\"><pkg:xmlData><w:document xmlns:w=\"x\"><w:body/></w:document></pkg:xmlData></pkg:part><pkg:part pkg:name=\"/word/media/a.png\" pkg:contentType=\"image/png\"><pkg:binaryData>iVBORw0KGgo=</pkg:binaryData></pkg:part></pkg:package>");
        return 0;
    }

    internal static byte[] Zip(CompressionLevel level, params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream s = zip.CreateEntry(name, level).Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return ms.ToArray();
    }
}

/// <summary>The compound-file package adapter: directory, streams, recovery, and the rewrite of whatever opened.</summary>
internal static class CfbTarget
{
    private static readonly OfficeLimits s_limits = new() { MaxPartCount = 2000, MaxTotalUncompressedSize = 8L << 20, MaxPartSize = 1 << 20 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        try
        {
            using CfbPackage pkg = CfbPackage.Open(new MemoryStream(bytes), new OpenContext(s_limits));
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            bool complete = true;
            foreach (IPackagePart part in pkg.Parts)
            {
                try
                {
                    contents[part.Name] = part.GetBytes().ToArray();
                    using Stream s = part.OpenRead();
                    s.ReadByte();
                }
                catch (OfficeException)
                {
                    complete = false;
                }
            }

            if (!complete)
            {
                return;
            }

            // Rewrite after a change, so the writer path (not just the original-bytes copy) runs; the rest must be unchanged.
            if (pkg.Parts.Count > 0)
            {
                pkg.Parts[0].SetBytes(new byte[] { 1, 2, 3 });
                contents[pkg.Parts[0].Name] = [1, 2, 3];
            }

            using var output = new MemoryStream();
            pkg.WriteTo(output);
            using CfbPackage back = CfbPackage.Open(new MemoryStream(output.ToArray()), new OpenContext(s_limits));
            if (back.Parts.Count != pkg.Parts.Count)
            {
                throw new InvalidOperationException($"rewrite changed the stream count from {pkg.Parts.Count} to {back.Parts.Count}");
            }

            foreach (IPackagePart part in back.Parts)
            {
                if (!contents.TryGetValue(part.Name, out byte[]? want) || !part.GetBytes().Span.SequenceEqual(want))
                {
                    throw new InvalidOperationException("rewrite changed the stream " + part.Name);
                }
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
        File.WriteAllBytes(Path.Combine(directory, "doc"), Build(3));
        File.WriteAllBytes(Path.Combine(directory, "doc4"), Build(4));
        byte[] big = Build(3);
        File.WriteAllBytes(Path.Combine(directory, "truncated"), big[..(big.Length / 2)]);
        return 0;
    }

    private static byte[] Build(int major)
    {
        using var ms = new MemoryStream();
        using (var w = new CompoundFileWriter(ms, new CompoundFileWriterOptions { MajorVersion = major }, leaveOpen: true))
        {
            void File(string name, int length)
            {
                byte[] data = new byte[length];
                new Random(length).NextBytes(data);
                w.WriteEntry(new ArchiveEntry { Name = name, Kind = ArchiveEntryKind.File, Size = length }, data);
            }

            File("WordDocument", 9000);
            File("1Table", 200);
            File("%0005SummaryInformation", 80);
            w.WriteEntry(new ArchiveEntry { Name = "Storage", Kind = ArchiveEntryKind.Directory });
            File("Storage/Inner", 4500);
            w.Finish();
        }

        return ms.ToArray();
    }
}
