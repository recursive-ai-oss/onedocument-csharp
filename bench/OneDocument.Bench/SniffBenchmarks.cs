using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>The cost of deciding what a file is: the one operation every open starts with.</summary>
[MemoryDiagnoser]
public class SniffBenchmarks
{
    private byte[] _docx = [];
    private byte[] _csv = [];

    [GlobalSetup]
    public void Setup()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<Types xmlns=\"x\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<Relationships xmlns=\"x\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml", "<x/>");
        }

        _docx = ms.ToArray();
        _csv = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("1,2,3\n", 1000)));
    }

    [Benchmark]
    public OfficeFormat Docx() => FormatSniffer.Detect(_docx);

    [Benchmark]
    public OfficeFormat Csv() => FormatSniffer.Detect(_csv);

    private static void Add(ZipArchive zip, string name, string content)
    {
        using Stream s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
