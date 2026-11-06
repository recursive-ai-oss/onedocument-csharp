using System;
using System.IO;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rendering.Metafiles;
using RecursiveAI.OneDocument.Rendering.Tests;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The code that turns a metafile's command stream into display-list items (the interpreter itself is OneImage's). The input is an EMF or WMF file;
/// it is sized, painted into a display list with the raster fallback allowed, tagged and untagged, and the list must validate. Damage is reported
/// as defects through the context, so nothing but a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class MetafileTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        _ = MetafilePainter.IsMetafile(bytes);
        _ = MetafilePainter.TryGetNaturalSize(bytes, out _, out _);
        for (int pass = 0; pass < 2; pass++)
        {
            var list = new DisplayList();
            var images = new PictureImages();
            uint owner = 0;
            var ctx = new MetafilePaintContext(list, images, () => ItemId.Of(++owner))
            {
                Report = d => { if (Environment.GetEnvironmentVariable("ONEDOCUMENT_FUZZ_TRACE") is not null) { Console.Error.WriteLine(d); } },
                Tagged = pass == 1,
                AltText = "picture",
                AllowRasterFallback = pass == 0,
                MaxRasterSide = 512,
            };
            try
            {
                _ = MetafilePainter.TryPaint(bytes, ctx, new RectF(0, 0, 200, 150), pass == 1 ? new PictureCrop(0.1, 0.1, 0.1, 0.1) : default);
                list.Validate();
            }
            catch (OfficeException)
            {
            }
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "sample.emf"), DrawingSamples.Emf());
        string repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
        string testdata = Path.Combine(repo, "testdata", "metafile");
        if (Directory.Exists(testdata))
        {
            foreach (string f in Directory.GetFiles(testdata, "*.*", SearchOption.AllDirectories))
            {
                if ((f.EndsWith(".emf", StringComparison.Ordinal) || f.EndsWith(".wmf", StringComparison.Ordinal)) && new FileInfo(f).Length < 20_000)
                {
                    File.Copy(f, Path.Combine(dir, Path.GetRelativePath(testdata, f).Replace('/', '_').Replace('\\', '_')), true);
                }
            }
        }

        return 0;
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
