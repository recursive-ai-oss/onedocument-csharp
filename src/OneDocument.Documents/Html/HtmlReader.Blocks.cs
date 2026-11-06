using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using OneHtmlNode = RecursiveAI.OneHtml.Node;

namespace RecursiveAI.OneDocument.Documents.Html;

internal sealed partial class HtmlReader
{
    // ---- blocks ----------------------------------------------------------------------------------------------------------------

    private static int Margin(ComputedStyle s, string prop, double em)
    {
        string v = Prop(s, prop);
        return v == "auto" ? 0 : Math.Max(0, HtmlCss.Twips(v, em) ?? 0);
    }

    private void BlockElement(OneHtmlNode e, ComputedStyle s, Format f, string display)
    {
        _ct.ThrowIfCancellationRequested();
        FlushPara();
        string name = e.LocalName;
        double em = HtmlCss.FontPoints(s);
        bool listy = name is "ul" or "ol" or "li" or "menu" or "dir";
        int mt = Margin(s, "margin-top", em);
        int mb = Margin(s, "margin-bottom", em);
        int dl = listy ? 0 : Margin(s, "margin-left", em) + Margin(s, "padding-left", em);
        int dr = listy ? 0 : Margin(s, "margin-right", em) + Margin(s, "padding-right", em);
        bool rtl = s.IsRightToLeft;
        if (rtl)
        {
            // In a right-to-left block the start side is the right one.
            (dl, dr) = (dr, dl);
        }

        _gap = Math.Max(_gap, mt);
        int savedL = _indentLeft;
        int savedR = _indentRight;
        _indentLeft += dl;
        _indentRight += dr;
        OneHtmlNode savedSource = _source;
        _source = e;
        HList? savedList = _liList;
        bool savedUsed = _liUsed;
        HList? itemList = null;
        if (name == "li")
        {
            itemList = _lists.Count > 0 ? _lists.Peek().List : NewList(false, "bullet", 1, 0, "disc");
            _liList = itemList;
            _liUsed = false;
        }

        if (WantsBreakBefore(s))
        {
            _pageBreakNext = true;
        }

        PlaceAnchor(e);
        (bool u, bool st) = Decoration(s);
        Format f2 = f with { Underline = f.Underline || u, Strike = f.Strike || st, Lang = NormalLang(e, f.Lang) };
        Walk(e, f2);
        FlushPara();
        if (itemList is not null && !_liUsed)
        {
            // An empty item still shows its marker.
            EnsurePara();
            FlushPara();
        }

        _liList = savedList;
        _liUsed = savedUsed;
        _source = savedSource;
        _indentLeft = savedL;
        _indentRight = savedR;
        _gap = Math.Max(_gap, mb);
        if (WantsBreakAfter(s))
        {
            _pageBreakNext = true;
        }
    }

    private void Rule(OneHtmlNode e, ComputedStyle s)
    {
        FlushPara();
        double em = HtmlCss.FontPoints(s);
        _gap = Math.Max(_gap, Margin(s, "margin-top", em));
        var p = new HPara
        {
            Rule = true,
            Props = new HParaProps { Before = _gap, IndentLeft = _indentLeft, IndentRight = _indentRight, BorderBottom = new HBorder("single", 6, "808080") },
        };
        foreach (string b in _pendingBookmarks)
        {
            p.Inlines.Add(new HBookmark(b));
        }

        _pendingBookmarks.Clear();
        _gap = Margin(s, "margin-bottom", em);
        _target.Add(p);
    }

    // ---- text ------------------------------------------------------------------------------------------------------------------

    private static bool HasRtlChars(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF')
            {
                return true;
            }
        }

        return false;
    }

    private void AddText(string text, OneHtmlNode parent, Format f)
    {
        if (text.Length == 0)
        {
            return;
        }

        (HRunProps core, string ws, string transform) = TextStyle(parent);
        bool preserve = ws is "pre" or "pre-wrap" or "break-spaces";
        bool keepNewlines = preserve || ws == "pre-line";
        var sb = new StringBuilder();
        bool pendingSpace = false;

        void Flush()
        {
            if (sb.Length == 0)
            {
                return;
            }

            string t = sb.ToString();
            sb.Clear();
            t = transform switch
            {
                "uppercase" => t.ToUpperInvariant(),
                "lowercase" => t.ToLowerInvariant(),
                "capitalize" => Capitalize(t),
                _ => t,
            };
            HRunProps props = core with { Underline = f.Underline, Strike = f.Strike, Shade = f.Shade, Lang = f.Lang, Rtl = HasRtlChars(t) };
            HPara p = EnsurePara();
            if (p.Inlines.Count > 0 && p.Inlines[^1] is HText last && last.Props.Equals(props) && last.Link == f.Link)
            {
                last.Text += t;
            }
            else
            {
                p.Inlines.Add(new HText(t, props, f.Link));
            }
        }

        foreach (char c in text)
        {
            if (c == '\r' || c == '\0')
            {
                continue;
            }

            if (c == '\n' && keepNewlines)
            {
                Flush();
                EnsurePara().Inlines.Add(new HBreak(false));
                _atStart = true;
                pendingSpace = false;
                continue;
            }

            if (c == '\t' && preserve)
            {
                Flush();
                EnsurePara().Inlines.Add(new HTab());
                _atStart = false;
                continue;
            }

            bool isSpace = c is ' ' or '\t' or '\n' or '\f';
            if (isSpace && !preserve)
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                if (!_atStart)
                {
                    sb.Append(' ');
                }

                pendingSpace = false;
            }

            if (c < ' ' && c != '\t')
            {
                continue; // control characters are not valid in the document
            }

            sb.Append(c);
            _atStart = false;
        }

        if (pendingSpace && !_atStart && sb.Length > 0)
        {
            sb.Append(' ');
            _atStart = true;
        }
        else if (pendingSpace && !_atStart && _cur is not null)
        {
            // The space between two inline elements: "a <b>b</b>" ends its first text with it.
            sb.Append(' ');
            _atStart = true;
        }

        Flush();
    }

    private static string Capitalize(string t)
    {
        var sb = new StringBuilder(t.Length);
        bool start = true;
        foreach (char c in t)
        {
            sb.Append(start ? char.ToUpperInvariant(c) : c);
            start = char.IsWhiteSpace(c);
        }

        return sb.ToString();
    }

    // ---- lists -----------------------------------------------------------------------------------------------------------------

    private HList NewList(bool ordered, string format, int start, int depth, string bullet)
    {
        var l = new HList(_nextListId++, ordered, format, start, depth, bullet);
        _out.Lists.Add(l);
        return l;
    }

    private void ListElement(OneHtmlNode e, ComputedStyle s, Format f)
    {
        FlushPara();
        bool ordered = e.LocalName == "ol";
        string? typeAttr = Attr(e, "type");
        string lst = Prop(s, "list-style-type");
        string format = "bullet";
        string bullet = "disc";
        if (ordered)
        {
            format = typeAttr switch
            {
                "a" => "lowerLetter",
                "A" => "upperLetter",
                "i" => "lowerRoman",
                "I" => "upperRoman",
                "1" => "decimal",
                _ => lst switch
                {
                    "lower-alpha" or "lower-latin" => "lowerLetter",
                    "upper-alpha" or "upper-latin" => "upperLetter",
                    "lower-roman" => "lowerRoman",
                    "upper-roman" => "upperRoman",
                    "disc" or "circle" or "square" => "bullet",
                    _ => "decimal",
                },
            };
            if (format == "bullet")
            {
                bullet = lst;
            }
        }
        else
        {
            string pick = typeAttr?.ToLowerInvariant() ?? lst;
            bullet = pick is "circle" or "square" or "none" ? pick : "disc";
        }

        int start = 1;
        if (ordered && Attr(e, "start") is { } st && int.TryParse(st.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sv))
        {
            start = Math.Clamp(sv, 0, 1_000_000);
        }

        int depth = Math.Min(_lists.Count, 8);
        if (_lists.Count >= _maxDepth)
        {
            Lose("loss.html-depth", "deeply nested content", "Lists nested deeper than the limit were left out.", e);
            return;
        }

        var list = NewList(format != "bullet", format, start, depth, bullet);
        double em = HtmlCss.FontPoints(s);
        _gap = Math.Max(_gap, Margin(s, "margin-top", em));
        _lists.Push(new ListScope(list));
        OneHtmlNode savedSource = _source;
        HList? savedList = _liList;
        bool savedUsed = _liUsed;
        _liList = null;
        _source = e;
        PlaceAnchor(e);
        Walk(e, f with { Lang = NormalLang(e, f.Lang) });
        FlushPara();
        _source = savedSource;
        _liList = savedList;
        _liUsed = savedUsed;
        _lists.Pop();
        _gap = Math.Max(_gap, Margin(s, "margin-bottom", em));
    }

    // ---- images ----------------------------------------------------------------------------------------------------------------

    private void Image(OneHtmlNode e, ComputedStyle s, Format f)
    {
        string src = Attr(e, "src")?.Trim() ?? string.Empty;
        string alt = Attr(e, "alt") ?? string.Empty;
        byte[]? data = null;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            data = DecodeDataUri(src);
            if (data is null)
            {
                Lose("loss.html-image-unreadable", "unreadable images", "An image whose data: URI could not be decoded was left out.", e);
            }
        }
        else if (src.Length > 0)
        {
            if (_options.ImageResolver is { } resolver)
            {
                try
                {
                    HtmlResource? r = resolver(src);
                    if (r is { } res && res.Data.Length > 0 && res.Data.Length <= _context.Limits.MaxPartSize)
                    {
                        data = res.Data.ToArray();
                    }
                    else
                    {
                        Lose("loss.html-external-image", "external images", "An image the resolver did not supply was left out; its alternate text stays.", e);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Lose("loss.html-external-image", "external images", "An image the resolver could not supply was left out; its alternate text stays.", e);
                }
            }
            else
            {
                Lose("loss.html-external-image", "external images", "Images with an external source are not fetched (no resolver was given); their alternate text stays.", e);
            }
        }

        HtmlImageInfo? info = data is null ? null : HtmlImageInfo.Detect(data);
        if (data is not null && info is null)
        {
            Lose("loss.html-image-format", "images in other formats", "Only PNG, JPEG, GIF and BMP pictures are carried; others were left out.", e);
        }

        if (info is not { } img)
        {
            if (alt.Length > 0)
            {
                AddText(alt, e, f);
            }

            return;
        }

        double widthPx = img.Width;
        double heightPx = img.Height;
        double? w = SizeAttr(e, "width", s);
        double? h = SizeAttr(e, "height", s);
        if (w is not null && h is not null)
        {
            widthPx = w.Value;
            heightPx = h.Value;
        }
        else if (w is not null)
        {
            heightPx = w.Value * img.Height / Math.Max(1, img.Width);
            widthPx = w.Value;
        }
        else if (h is not null)
        {
            widthPx = h.Value * img.Width / Math.Max(1, img.Height);
            heightPx = h.Value;
        }

        double maxPx = Math.Max(24, (_avail - _indentLeft - _indentRight) / 15.0);
        if (widthPx > maxPx)
        {
            heightPx *= maxPx / widthPx;
            widthPx = maxPx;
        }

        long wEmu = (long)Math.Clamp(Math.Round(widthPx * 9525), 9525, 20_000_000_000);
        long hEmu = (long)Math.Clamp(Math.Round(heightPx * 9525), 9525, 20_000_000_000);
        PlaceAnchor(e);
        EnsurePara().Inlines.Add(new HImage(data!, img.Extension, img.ContentType, wEmu, hEmu, alt, f.Link));
        _atStart = false;
    }

    private static double? SizeAttr(OneHtmlNode e, string name, ComputedStyle s)
    {
        string? a = Attr(e, name)?.Trim();
        if (!string.IsNullOrEmpty(a) && !a.EndsWith('%') && double.TryParse(a.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 && v < 100000)
        {
            return v;
        }

        if (s.IsDeclared(name))
        {
            double? pt = HtmlCss.Points(Prop(s, name), HtmlCss.FontPoints(s));
            if (pt is > 0)
            {
                return pt.Value / 0.75;
            }
        }

        return null;
    }

    private byte[]? DecodeDataUri(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
        {
            return null;
        }

        string header = uri[5..comma];
        string payload = uri[(comma + 1)..];
        bool b64 = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        long cap = Math.Min(_context.Limits.MaxPartSize, int.MaxValue / 2);
        try
        {
            if (b64)
            {
                if (payload.Length / 4 * 3 > cap)
                {
                    return null;
                }

                // Whitespace inside the payload is allowed by RFC 2397 consumers.
                string clean = payload.Contains('%') ? Decode(payload) : payload;
                var sb = new StringBuilder(clean.Length);
                foreach (char c in clean)
                {
                    if (!char.IsWhiteSpace(c))
                    {
                        sb.Append(c);
                    }
                }

                return Convert.FromBase64String(sb.ToString());
            }

            byte[] raw = Encoding.UTF8.GetBytes(Decode(payload));
            return raw.Length <= cap ? raw : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
