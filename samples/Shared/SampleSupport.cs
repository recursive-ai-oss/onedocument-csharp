using System.Text;

namespace RecursiveAI.OneDocument.Samples;

/// <summary>What every sample shares: a work folder and a pass/fail exit code. Not part of the task each sample shows.</summary>
internal static class SampleSupport
{
    /// <summary>The folder the sample writes to: the first argument, or a fresh folder under the temp directory.</summary>
    public static string WorkDirectory(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "onedocument-sample-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Checks a written PDF starts with the PDF signature and has some content; throws otherwise.</summary>
    public static void RequirePdf(string path)
    {
        byte[] head = new byte[5];
        using FileStream stream = File.OpenRead(path);
        if (stream.Length < 500 || stream.Read(head) != 5 || Encoding.ASCII.GetString(head) != "%PDF-")
        {
            throw new InvalidOperationException(path + " is not a PDF.");
        }

        Console.WriteLine(Path.GetFileName(path) + ": " + stream.Length + " bytes");
    }
}
