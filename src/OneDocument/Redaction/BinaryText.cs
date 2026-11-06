using System.Linq;
using System.Text;

namespace RecursiveAI.OneDocument.Redaction;

// Reading and overwriting text inside parts that are not XML (embedded objects, macro projects, picture metadata). A match is overwritten in
// place with 'X' so the part keeps its length and structure; nothing is parsed.
internal static class BinaryText
{
    private const int MinimumRun = 4;

    public static bool IsZip(ReadOnlySpan<byte> b) => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;

        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".tif", ".tiff", ".bmp", ".emf", ".wmf", ".webp", ".jfif", ".pct", ".pict"];

    public static bool IsImageName(string name)
    {
        foreach (string ext in ImageExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Pictures hold compressed data that looks like text by chance, so a pattern only counts there in long runs and when it matches at least this much.
    public const int ImageMinimumRun = 8;

    public const int ImageMinimumMatch = 6;

    public readonly record struct Run(int ByteStart, int Stride, string Text);

    // Printable ASCII runs of at least four characters, as one byte per character and as UTF-16LE (both alignments).
    public static IEnumerable<Run> Runs(byte[] data, int minimumRun = MinimumRun)
    {
        foreach (Run r in RunsOf(data, 1, 0, minimumRun))
        {
            yield return r;
        }

        for (int alignment = 0; alignment < 2; alignment++)
        {
            foreach (Run r in RunsOf(data, 2, alignment, minimumRun))
            {
                yield return r;
            }
        }
    }

    private static bool Printable(byte b) => b is >= 0x20 and <= 0x7E or 9 or 10 or 13;

    private static IEnumerable<Run> RunsOf(byte[] data, int stride, int alignment, int minimumRun)
    {
        var sb = new StringBuilder();
        int start = -1;
        for (int i = alignment; i + stride <= data.Length; i += stride)
        {
            bool ok = Printable(data[i]) && (stride == 1 || data[i + 1] == 0);
            if (ok)
            {
                if (start < 0)
                {
                    start = i;
                }

                sb.Append((char)data[i]);
                continue;
            }

            if (sb.Length >= minimumRun)
            {
                yield return new Run(start, stride, sb.ToString());
            }

            sb.Clear();
            start = -1;
        }

        if (sb.Length >= minimumRun)
        {
            yield return new Run(start, stride, sb.ToString());
        }
    }

    public static IEnumerable<(byte[] Needle, int Stride)> Needles(string literal)
    {
        yield return (Encoding.UTF8.GetBytes(literal), 1);
        yield return (Encoding.Unicode.GetBytes(literal), 2);
        bool latin1 = true;
        foreach (char c in literal)
        {
            latin1 &= c < 256;
        }

        if (latin1)
        {
            yield return (Encoding.Latin1.GetBytes(literal), 1);
        }
    }

    public static IEnumerable<int> Find(byte[] data, byte[] needle)
    {
        if (needle.Length == 0)
        {
            yield break;
        }

        int from = 0;
        while (from <= data.Length - needle.Length)
        {
            int at = data.AsSpan(from).IndexOf(needle);
            if (at < 0)
            {
                yield break;
            }

            yield return from + at;
            from += at + 1;
        }
    }

    // Overwrites matches in place; returns how many.
    public static int Redact(byte[] data, RedactionContext ctx, bool literalOnly, string location)
    {
        int count = 0;
        foreach (RedactionMatch m in ctx.Plan.Matches)
        {
            if (m.IsRegex)
            {
                continue;
            }

            foreach ((byte[] needle, int stride) in Needles(m.PatternText))
            {
                foreach (int at in Find(data, needle).ToArray())
                {
                    Overwrite(data, at, needle.Length, stride);
                    count++;
                }
            }
        }

        var spans = new List<(int Start, int Length)>();
        foreach (Run run in Runs(data, literalOnly ? ImageMinimumRun : MinimumRun).ToArray())
        {
            if (!ctx.FindSpans(run.Text, location, spans))
            {
                continue;
            }

            foreach ((int s, int l) in spans.ToArray())
            {
                if (literalOnly && l < ImageMinimumMatch && !IsLiteralHit(ctx, run.Text.Substring(s, l)))
                {
                    continue;
                }

                for (int k = 0; k < l; k++)
                {
                    data[run.ByteStart + ((s + k) * run.Stride)] = (byte)'X';
                }

                count++;
            }
        }

        return count;
    }

    private static bool IsLiteralHit(RedactionContext ctx, string text)
    {
        foreach (RedactionMatch m in ctx.Plan.Matches)
        {
            if (!m.IsRegex && string.Equals(m.PatternText, text, m.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Overwrite(byte[] data, int at, int length, int stride)
    {
        if (stride == 2)
        {
            for (int k = 0; k + 1 < length; k += 2)
            {
                data[at + k] = (byte)'X';
                data[at + k + 1] = 0;
            }

            return;
        }

        data.AsSpan(at, length).Fill((byte)'X');
    }
}
