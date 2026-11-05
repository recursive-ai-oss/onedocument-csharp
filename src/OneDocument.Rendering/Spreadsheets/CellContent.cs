using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>What a cell shows: its runs of text and the facts alignment and overflow need.</summary>
internal sealed class CellContent
{
    public List<(string Text, TextStyle Style)> Spans { get; } = [];
    public bool IsNumber;          // numbers and dates: right aligned in General, never overflow
    public bool IsCentered;        // booleans and errors centre in General
    public bool IsEmptyText => Spans.Count == 0 || Spans.All(s => s.Text.Length == 0);
    public string PlainText => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>Builds the displayed text of a cell from its value, number format and rich-text runs.</summary>
internal static class CellContents
{
    public static CellContent? Build(RenderContext ctx, Worksheet ws, SheetPrintSettings settings, CellValue value, StyleInfo style, double availableWidth, uint? colorOverride)
    {
        TextStyle baseStyle = style.Text;
        var content = new CellContent();
        switch (value.Kind)
        {
            case CellValueKind.Empty:
                return null;
            case CellValueKind.Text:
                {
                    string text = ws.GetText(value);
                    if (text.Length == 0)
                    {
                        return null;
                    }

                    if (ws.TryGetSharedStringIndex(value, out int index) && ws.Workbook.SharedStrings.IsRich(index) && RichRuns(ctx, ws.Workbook.SharedStrings.GetItemXml(index), baseStyle) is { Count: > 0 } runs)
                    {
                        foreach ((string t, TextStyle s) in runs)
                        {
                            content.Spans.Add((t, colorOverride is { } c ? s with { Rgb = c } : s));
                        }

                        return content;
                    }

                    if (!style.IsGeneral && !style.Format.Code.Equals("@", StringComparison.Ordinal))
                    {
                        text = style.Format.FormatText(text);
                    }

                    content.Spans.Add((text, colorOverride is { } c2 ? baseStyle with { Rgb = c2 } : baseStyle));
                    return content;
                }

            case CellValueKind.Boolean:
                content.IsCentered = true;
                content.Spans.Add((value.Number != 0 ? "TRUE" : "FALSE", colorOverride is { } c3 ? baseStyle with { Rgb = c3 } : baseStyle));
                return content;
            case CellValueKind.Error:
                {
                    content.IsCentered = true;
                    string shown = settings.Errors switch
                    {
                        SheetErrorPrinting.Blank => string.Empty,
                        SheetErrorPrinting.Dash => "--",
                        SheetErrorPrinting.NotAvailable => "#N/A",
                        _ => ws.GetText(value),
                    };
                    if (shown.Length == 0)
                    {
                        return null;
                    }

                    content.Spans.Add((shown, baseStyle));
                    return content;
                }

            default:
                return Number(ctx, ws, value.Number, style, baseStyle, availableWidth, colorOverride);
        }
    }

    private static CellContent Number(RenderContext ctx, Worksheet ws, double number, StyleInfo style, TextStyle baseStyle, double available, uint? colorOverride)
    {
        var content = new CellContent { IsNumber = true };
        NumberFormat nf = style.Format;
        DateSystem system = ws.Workbook.DateSystem;
        Span<char> buffer = ctx.Scratch;
        uint rgb = baseStyle.Rgb;
        string shown;
        FormatResult result;
        if (style.IsGeneral)
        {
            // General fits the number to the column: fewer digits, then scientific, as the width shrinks.
            shown = string.Empty;
            result = default;
            bool fitted = false;
            for (int gw = 11; gw >= 1; gw--)
            {
                result = nf.TryFormat(number, buffer, new FormatOptions(system, GeneralWidth: gw));
                if (result.Status == FormatStatus.InvalidNumber)
                {
                    shown = "#NUM!";
                    fitted = true;
                    break;
                }

                if (result.Status != FormatStatus.Ok)
                {
                    continue;
                }

                shown = new string(buffer[..result.Length]);
                if (gw == 1 || ctx.Text.Measure(shown, baseStyle) <= available + 1e-6)
                {
                    fitted = true;
                    break;
                }
            }

            if (!fitted || ctx.Text.Measure(shown, baseStyle) > available + 1e-6)
            {
                shown = Hashes(ctx, baseStyle, available);
            }
        }
        else
        {
            result = nf.TryFormat(number, buffer, new FormatOptions(system));
            if (result.Status == FormatStatus.InvalidNumber)
            {
                shown = "#NUM!";
            }
            else if (result.Status != FormatStatus.Ok)
            {
                shown = Hashes(ctx, baseStyle, available);
                result = default;
            }
            else
            {
                shown = new string(buffer[..result.Length]);
                if (result.FillPosition >= 0 && result.FillPosition <= shown.Length)
                {
                    // ECMA-376 Part 1 §18.8.31: "*c" repeats c to fill the cell's width.
                    string before = shown[..result.FillPosition];
                    string after = shown[result.FillPosition..];
                    double used = ctx.Text.Measure(before + after, baseStyle);
                    double one = ctx.Text.Measure(result.FillCharacter.ToString(), baseStyle);
                    int n = one > 0 ? (int)Math.Floor((available - used) / one) : 0;
                    shown = before + new string(result.FillCharacter, Math.Max(0, n)) + after;
                }

                if (ctx.Text.Measure(shown, baseStyle) > available + 1e-6)
                {
                    shown = Hashes(ctx, baseStyle, available);
                    result = default;
                }
            }
        }

        if (result.Color is { } nc)
        {
            rgb = nc.PaletteIndex > 0
                ? (uint)ws.Workbook.Styles.ResolveColor(StyleColor.FromIndexed(nc.PaletteIndex + 7)) & 0xFFFFFFu
                : (uint)nc.Rgb & 0xFFFFFFu;
        }

        if (colorOverride is { } co)
        {
            rgb = co;
        }

        content.Spans.Add((shown, baseStyle with { Rgb = rgb }));
        return content;
    }

    // A number that does not fit shows a row of # that fills the cell.
    private static string Hashes(RenderContext ctx, TextStyle style, double available)
    {
        double one = ctx.Text.Measure("#", style);
        int n = one > 0 ? (int)Math.Floor(available / one) : 1;
        return new string('#', Math.Max(1, n));
    }

    // ECMA-376 Part 1 §18.4: an si holds either one t or runs r, each with optional rPr (character properties) and a t. Phonetic runs (rPh) are not shown.
    private static List<(string, TextStyle)> RichRuns(RenderContext ctx, ReadOnlyMemory<byte> xml, TextStyle baseStyle)
    {
        var list = new List<(string, TextStyle)>();
        if (xml.IsEmpty || !MarkupDocument.TryParse(xml, out MarkupDocument? doc, null, null) || doc is null)
        {
            return list;
        }

        foreach (MarkupNode r in doc.Root.ChildElements)
        {
            string local = r.LocalNameString;
            if (local == "t")
            {
                list.Add((Decode(r.InnerText), baseStyle));
            }
            else if (local == "r")
            {
                TextStyle style = baseStyle;
                string text = string.Empty;
                foreach (MarkupNode c in r.ChildElements)
                {
                    if (c.LocalNameString == "t")
                    {
                        text = Decode(c.InnerText);
                    }
                    else if (c.LocalNameString == "rPr")
                    {
                        style = ApplyRunProperties(ctx, c, baseStyle);
                    }
                }

                if (text.Length > 0)
                {
                    list.Add((text, style));
                }
            }
        }

        return list;
    }

    private static TextStyle ApplyRunProperties(RenderContext ctx, MarkupNode rPr, TextStyle s)
    {
        foreach (MarkupNode p in rPr.ChildElements)
        {
            string val = p.GetAttribute("val") ?? string.Empty;
            switch (p.LocalNameString)
            {
                case "rFont": s = s with { Family = val }; break;
                case "sz": s = s with { Size = X.Double(val, s.Size) }; break;
                case "b": s = s with { Bold = val is not ("0" or "false") }; break;
                case "i": s = s with { Italic = val is not ("0" or "false") }; break;
                case "strike": s = s with { Strike = val is not ("0" or "false") }; break;
                case "u": s = s with { Underline = val switch { "none" => FontUnderline.None, "double" => FontUnderline.Doubled, _ => FontUnderline.Straight } }; break;
                case "vertAlign": s = s with { Vertical = val switch { "superscript" => FontVerticalAlignment.Superscript, "subscript" => FontVerticalAlignment.Subscript, _ => FontVerticalAlignment.Baseline } }; break;
                case "color": s = s with { Rgb = ctx.Styles.Rgb(ReadColor(p), 0x000000u) }; break;
            }
        }

        return s;
    }

    internal static StyleColor ReadColor(MarkupNode e)
    {
        double tint = X.Double(e.GetAttribute("tint"), 0);
        if (e.GetAttribute("rgb") is { } rgb)
        {
            string h = rgb.Length == 6 ? "FF" + rgb : rgb;
            if (h.Length == 8 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            {
                return new StyleColor(ColorKind.Rgb, v, tint);
            }
        }

        if (e.GetAttribute("theme") is { } th && int.TryParse(th, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ti))
        {
            return new StyleColor(ColorKind.Theme, ti, tint);
        }

        if (e.GetAttribute("indexed") is { } ix && int.TryParse(ix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ii))
        {
            return new StyleColor(ColorKind.Indexed, ii, tint);
        }

        return default;
    }

    // ECMA-376 Part 1 §22.4.2.4 (ST_Xstring): _xHHHH_ stands for the UTF-16 unit HHHH.
    internal static string Decode(string s)
    {
        if (!s.Contains("_x", StringComparison.Ordinal))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_' && i + 6 < s.Length + 0 && s[i + 1] == 'x' && s[i + 6] == '_' &&
                int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }
}
