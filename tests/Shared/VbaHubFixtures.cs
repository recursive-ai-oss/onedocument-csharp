using System.IO.Compression;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>The macro-enabled files of testdata/vba, and a signed variant made by adding a signature part to one of them.</summary>
internal static class VbaHubFixtures
{
    public static string Path(string name, [CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "..", "testdata", "vba", name));

    public static Dictionary<string, byte[]> Entries(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var ms = new MemoryStream();
            using Stream s = e.Open();
            s.CopyTo(ms);
            return ms.ToArray();
        });
    }

    /// <summary>Copies a fixture and adds a legacy signature part related to its project part.</summary>
    public static string Signed(string name, string directory, string projectPart)
    {
        string target = System.IO.Path.Combine(directory, "signed-" + name);
        using (OpcPackage package = OpcPackage.Open(Path(name)))
        {
            string dir = projectPart[..projectPart.LastIndexOf('/')];
            package.AddPart(dir + "/vbaProjectSignature.bin", "application/vnd.ms-office.vbaProjectSignature", new byte[] { 0x30, 0x03, 1, 2, 3 });
            package.SetRelationships(projectPart, [new Relationship("rIdSig1", "http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature", "vbaProjectSignature.bin")]);
            package.SaveAs(target);
        }

        return target;
    }
}
