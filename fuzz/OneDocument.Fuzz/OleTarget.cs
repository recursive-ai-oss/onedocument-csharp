using System;
using System.IO;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The OLE layers. <c>olepropset</c> feeds bytes to the property set reader, writes what it read and reads that again, and maps the sets
/// into the package properties as both summary streams. <c>oleobject</c> feeds bytes to the embedded-object reader (a compound file taken as
/// one storage): it describes the object and writes it out as a compound file. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class OleTarget
{
    private static readonly OfficeLimits Limits = new() { MaxAllocationBytes = 1 << 24, MaxPartSize = 8 << 20, MaxPartCount = 2000, MaxDefects = 100 };

    public static void RunPropertySet(ReadOnlySpan<byte> data)
    {
        try
        {
            byte[] bytes = data.ToArray();
            if (OlePropertySets.TryRead(bytes, out OlePropertySets? sets, new OpenContext(Limits)))
            {
                byte[] again = sets!.ToArray();
                OlePropertySets back = OlePropertySets.Read(again, new OpenContext(Limits));
                if (back.Sets.Count != sets.Sets.Count)
                {
                    throw new InvalidOperationException("a property set was lost in a round trip");
                }
            }

            using var package = new InMemoryPackage(Limits);
            var props = new DocumentProperties(package, new OpenContext(Limits));
            OleDocumentProperties.Read(bytes, bytes, props, new OpenContext(Limits));
            OleDocumentProperties.Create(props);
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunObject(ReadOnlySpan<byte> data)
    {
        try
        {
            OleStorageImage? image = OleStorageImage.FromCompoundFile(data.ToArray(), "_1", new OpenContext(Limits));
            if (image is not null)
            {
                OleObjectInfo info = image.Describe();
                _ = info.Kind;
                image.ToCompoundFile(Limits);
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        var set = new OlePropertySet(OlePropertySets.UserDefinedPropertiesFormatId) { CodePage = 1252 };
        set.SetNamed("Project", OlePropertyValue.FromString("Atlas"));
        set.SetNamed("Count", OlePropertyValue.FromInt32(42));
        set.SetNamed("Parts", OlePropertyValue.FromStringVector(["a", "bc"]));
        set.SetNamed("When", OlePropertyValue.FromDate(new DateTimeOffset(2021, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        var doc = new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId) { CodePage = 1252 };
        doc.Set(12, OlePropertyValue.FromVariantVector([OlePropertyValue.FromString("Worksheets"), OlePropertyValue.FromInt32(1)]));
        doc.Set(13, OlePropertyValue.FromStringVector(["Sheet1"]));
        var sets = new OlePropertySets();
        sets.Add(doc);
        sets.Add(set);
        File.WriteAllBytes(Path.Combine(dir, "docsummary.bin"), sets.ToArray());

        var summary = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);
        summary.Set(2, OlePropertyValue.FromString("Title"));
        summary.Set(12, OlePropertyValue.FromDate(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        var one = new OlePropertySets();
        one.Add(summary);
        File.WriteAllBytes(Path.Combine(dir, "summary.bin"), one.ToArray());

        string testdata = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ThisFile())))!, "testdata", "ole");
        if (Directory.Exists(testdata))
        {
            foreach (string f in Directory.GetFiles(testdata, "*.xls"))
            {
                using var cfb = CfbPackage.Open(ByteSource.FromMemory(File.ReadAllBytes(f)));
                foreach (string name in new[] { "/\u0005SummaryInformation", "/\u0005DocumentSummaryInformation" })
                {
                    if (cfb.FindPart(name) is { } part)
                    {
                        File.WriteAllBytes(Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + (name.Contains("Document", StringComparison.Ordinal) ? "-docsummary" : "-summary") + ".bin"), part.GetBytes().ToArray());
                    }
                }

                foreach (OleStorageImage s in OleStorageSet.CaptureByPrefix(cfb, "MBD", new OpenContext()))
                {
                    File.WriteAllBytes(Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "-object.bin"), s.ToCompoundFile());
                }
            }
        }

        return 0;
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
