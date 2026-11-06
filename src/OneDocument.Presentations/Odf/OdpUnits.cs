using System.Globalization;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Lengths, colours and text escapes shared by the OpenDocument presentation importer and exporter.
internal static class OdpUnits
{
    public static bool TryPoints(string? value, out double points) => RecursiveAI.OneDocument.Odf.OdfLengths.TryPoints(value, out points);

    public static bool TryEmu(string? value, out long emu)
    {
        emu = 0;
        if (!TryPoints(value, out double pt))
        {
            return false;
        }

        emu = (long)Math.Clamp(Math.Round(pt * 12700), -(1L << 40), 1L << 40);
        return true;
    }

    public static long Emu(string? value, long fallback = 0) => TryEmu(value, out long e) ? e : fallback;

    public static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Esc(string? s) => PptXml.Escape(s);

    // Lengths written from EMU: centimetres with up to four decimals, which is finer than an EMU needs to survive a round trip.
    public static string Cm(long emu) => RecursiveAI.OneDocument.Odf.OdfLengths.Cm(emu / 12700.0);

    public static string Pt(double points) => RecursiveAI.OneDocument.Odf.OdfLengths.Pt(points);

    // #rrggbb to the six upper-case hex digits DrawingML wants; null for transparent or anything that is not a colour.
    public static string? Color(string? value)
    {
        if (value is { Length: 7 } && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            return value[1..].ToUpperInvariant();
        }

        return null;
    }

    // "50%" to 0.5; null when the value is not a percentage.
    public static double? Percent(string? value)
    {
        if (value is { Length: > 1 } && value[^1] == '%' && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
        {
            return v / 100;
        }

        return null;
    }

    public static string DecodeStyleName(string name) => RecursiveAI.OneDocument.Odf.OdfLengths.DecodeStyleName(name);
}
