using System;
using System.IO;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Packaging;

// Usage: SaveChild <path> <megabytes>
// Builds a package of incompressible parts and saves it over <path>. It prints "saving" just before the save starts, so a caller
// can kill the process while the new file is being written.
string path = args[0];
int megabytes = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
var limits = new OfficeLimits { MaxPartSize = 512L << 20, MaxTotalUncompressedSize = 4L << 30 };
using OpcPackage package = OpcPackage.Create(limits);
package.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")]);
var random = new Random(11);
for (int i = 0; i < megabytes / 8; i++)
{
    byte[] data = new byte[8 << 20];
    random.NextBytes(data);
    package.AddPart("/word/part" + i + ".bin", "application/octet-stream", data);
}

package.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body/></w:document>"u8.ToArray());
Console.Out.WriteLine("saving");
Console.Out.Flush();
package.SaveAs(path);
Console.Out.WriteLine("saved");
