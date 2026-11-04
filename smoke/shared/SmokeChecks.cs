using System.IO.Compression;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>
/// What the NativeAOT and browser-WASM smoke apps run: the shared contracts end to end on tiny generated inputs. Shared as source so
/// both apps check exactly the same things.
/// </summary>
internal static class SmokeChecks
{
    /// <summary>Runs every check and returns the names of the ones that failed.</summary>
    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string name, bool ok)
        {
            if (!ok)
            {
                failures.Add(name);
            }
        }

        const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        Check("sniff docx", FormatSniffer.Detect(Opc("word/document.xml", Wml)) == OfficeFormat.Docx);
        Check("sniff rtf", FormatSniffer.Detect(Encoding.UTF8.GetBytes("{\\rtf1 hi}")) == OfficeFormat.Rtf);
        Check("sniff csv", FormatSniffer.Detect(Encoding.UTF8.GetBytes("a,b\n1,2\n")) == OfficeFormat.Csv);
        Check("sniff odt", FormatSniffer.Detect(Zip(("mimetype", "application/vnd.oasis.opendocument.text"))) == OfficeFormat.Odt);
        Check("sniff junk", FormatSniffer.Detect(new byte[] { 1, 2, 3 }) == OfficeFormat.Unknown);

        try
        {
            FormatSniffer.Detect(Zip(("a", "x"), ("b", "x"), ("c", "x")), new OfficeLimits { MaxPartCount = 1 });
            Check("limit exception", false);
        }
        catch (OfficeLimitException ex)
        {
            Check("limit exception", ex.LimitName == nameof(OfficeLimits.MaxPartCount));
        }

        var registry = new FormatRegistry();
        registry.RegisterReader((_, _, _, _, _) => throw new InvalidOperationException(), OfficeFormat.Docx);
        Check("registry", registry.TryGetReader(OfficeFormat.Docx, out _) && !registry.TryGetWriter(OfficeFormat.Docx, out _));
        Check("descriptor", OfficeFormats.FromExtension("report.xlsx") == OfficeFormat.Xlsx);

        var sink = new DefectCollection();
        sink.Report(new Defect(DefectKind.Structure, "smoke.defect", DefectSeverity.Repaired, "m", DefectLocation.InCell("Sheet1", "B2")));
        Check("defects", sink.Count == 1 && sink[0].Location.ToString() == "Sheet1!B2");

        using var package = new InMemoryPackage();
        IPackagePart part = package.AddLoadedPart("/a.xml", "application/xml", 1, () => new byte[] { 1 });
        Check("untouched", part.IsUntouched && part.GetBytes().Length == 1 && part.IsUntouched);
        using var output = new MemoryStream();
        package.WriteTo(output);
        Check("package write", output.Length > 0);

        using IByteSource source = ByteSource.FromMemory(new byte[] { 1, 2, 3 });
        using Stream stream = ByteSource.AsStream(source);
        Check("source stream", stream.ReadByte() == 1 && stream.Length == 3);
        failures.AddRange(SchemaSmoke.Run());
        failures.AddRange(SheetSmoke.Run());
        return failures;
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

    private static byte[] Opc(string main, string contentType) => Zip(
        ("[Content_Types].xml", "<Types xmlns=\"x\"><Override PartName=\"/" + main + "\" ContentType=\"" + contentType + "\"/></Types>"),
        ("_rels/.rels", "<Relationships xmlns=\"x\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"" + main + "\"/></Relationships>"),
        (main, "<x/>"));
}
