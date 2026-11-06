namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>Joins runs of text nodes and finds the GitHub "extended autolinks" (www., http://, https://, ftp:// and e-mail addresses) in what is left as plain text.</summary>
internal static class MdAutolink
{
    /// <summary>Merges adjacent text nodes under <paramref name="root"/>, without recursion.</summary>
    public static void MergeText(MdNode root)
    {
        var stack = new Stack<MdNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MdNode n = stack.Pop();
            MdNode? c = n.FirstChild;
            while (c is not null)
            {
                if (c.Kind == MdKind.Text && c.Next is { Kind: MdKind.Text } next)
                {
                    var parts = new System.Text.StringBuilder(c.Literal);
                    MdNode? t = next;
                    while (t is { Kind: MdKind.Text })
                    {
                        parts.Append(t.Literal);
                        MdNode? after = t.Next;
                        t.Unlink();
                        t = after;
                    }

                    c.Literal = parts.ToString();
                }

                if (c.FirstChild is not null)
                {
                    stack.Push(c);
                }

                c = c.Next;
            }
        }
    }

    /// <summary>Replaces autolink candidates in text nodes below <paramref name="root"/> with link nodes. Text inside links is left alone.</summary>
    public static void Apply(MdNode root)
    {
        var stack = new Stack<MdNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MdNode n = stack.Pop();
            if (n.Kind is MdKind.Link or MdKind.Image or MdKind.Code)
            {
                continue;
            }

            MdNode? c = n.FirstChild;
            while (c is not null)
            {
                MdNode? next = c.Next;
                if (c.Kind == MdKind.Text)
                {
                    bool lineStart = c.Prev is null || c.Prev.Kind is MdKind.SoftBreak or MdKind.LineBreak;
                    Split(c, lineStart);
                }
                else if (c.FirstChild is not null)
                {
                    stack.Push(c);
                }

                c = next;
            }
        }
    }

    private static bool IsValidDomainChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.';

    private static void Split(MdNode text, bool lineStart)
    {
        string s = text.Literal;
        if (s.Length < 4 || (s.IndexOf('@', StringComparison.Ordinal) < 0 && s.IndexOf("://", StringComparison.Ordinal) < 0 && s.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0))
        {
            return;
        }

        List<(int Start, int End, string Dest)>? found = null;
        int i = 0;
        int segStart = 0;
        while (i < s.Length)
        {
            bool boundary = i == 0 ? lineStart : (char.IsWhiteSpace(s[i - 1]) || s[i - 1] is '*' or '_' or '~' or '(');
            int end = -1;
            string dest = string.Empty;
            if (boundary && (s[i] is 'w' or 'W' or 'h' or 'H' or 'f' or 'F'))
            {
                end = MatchUrl(s, i, out dest);
            }
            else if (boundary && (s[i] is 'm' or 'M' or 'x' or 'X'))
            {
                end = MatchProtocol(s, i, out dest);
            }

            if (end > i)
            {
                (found ??= []).Add((i, end, dest));
                i = end;
                segStart = end;
                continue;
            }

            if (s[i] == '@' && i > segStart)
            {
                int ls = i;
                while (ls > segStart && (char.IsAsciiLetterOrDigit(s[ls - 1]) || s[ls - 1] is '.' or '_' or '+' or '-'))
                {
                    ls--;
                }

                int de = MatchEmailDomain(s, i + 1);
                if (ls < i && de > i + 1)
                {
                    (found ??= []).Add((ls, de, string.Concat("mailto:", s.AsSpan(ls, de - ls))));
                    i = de;
                    segStart = de;
                    continue;
                }
            }

            i++;
        }

        if (found is null)
        {
            return;
        }

        MdNode anchor = text;
        int pos = 0;
        foreach ((int start, int end2, string dest2) in found)
        {
            if (start > pos)
            {
                var t = new MdNode(MdKind.Text) { Literal = s[pos..start] };
                anchor.InsertAfter(t);
                anchor = t;
            }

            var link = new MdNode(MdKind.Link) { Destination = dest2 };
            link.AppendChild(new MdNode(MdKind.Text) { Literal = s[start..end2] });
            anchor.InsertAfter(link);
            anchor = link;
            pos = end2;
        }

        if (pos < s.Length)
        {
            anchor.InsertAfter(new MdNode(MdKind.Text) { Literal = s[pos..] });
        }

        text.Unlink();
    }

    // GFM "extended protocol autolink": mailto: or xmpp: and an e-mail address; for xmpp: an optional /resource without further slashes.
    private static int MatchProtocol(string s, int i, out string dest)
    {
        dest = string.Empty;
        bool xmpp;
        int start;
        if (string.Compare(s, i, "mailto:", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
        {
            xmpp = false;
            start = i + 7;
        }
        else if (string.Compare(s, i, "xmpp:", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
        {
            xmpp = true;
            start = i + 5;
        }
        else
        {
            return -1;
        }

        int at = start;
        while (at < s.Length && (char.IsAsciiLetterOrDigit(s[at]) || s[at] is '.' or '_' or '+' or '-'))
        {
            at++;
        }

        if (at == start || at >= s.Length || s[at] != '@')
        {
            return -1;
        }

        int e = MatchEmailDomain(s, at + 1);
        if (e < 0)
        {
            return -1;
        }

        if (xmpp && e < s.Length && s[e] == '/')
        {
            int r = e + 1;
            while (r < s.Length && (char.IsAsciiLetterOrDigit(s[r]) || s[r] is '@' or '.' or '_' or '-'))
            {
                r++;
            }

            while (r > e + 1 && s[r - 1] == '.')
            {
                r--;
            }

            e = r;
        }

        dest = s[i..e];
        return e;
    }

    // GFM "extended www autolink" and "extended url autolink": a valid domain, then anything up to white space or '<', less trailing punctuation.
    private static int MatchUrl(string s, int i, out string dest)
    {
        dest = string.Empty;
        int domainStart;
        bool www = false;
        if (string.Compare(s, i, "www.", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
        {
            www = true;
            domainStart = i;
        }
        else if (string.Compare(s, i, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
        {
            domainStart = i + 7;
        }
        else if (string.Compare(s, i, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
        {
            domainStart = i + 8;
        }
        else if (string.Compare(s, i, "ftp://", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
        {
            domainStart = i + 6;
        }
        else
        {
            return -1;
        }

        int d = domainStart;
        while (d < s.Length && IsValidDomainChar(s[d]))
        {
            d++;
        }

        if (www && d <= domainStart + 4)
        {
            return -1;
        }

        if (!www && d == domainStart)
        {
            return -1;
        }

        // At least one period; no underscore in the last two segments.
        string domain = s[domainStart..d];
        string[] segments = domain.Split('.');
        if (segments.Length < 2)
        {
            return -1;
        }

        for (int k = Math.Max(0, segments.Length - 2); k < segments.Length; k++)
        {
            if (segments[k].Contains('_', StringComparison.Ordinal))
            {
                return -1;
            }
        }

        int e = d;
        while (e < s.Length && !char.IsWhiteSpace(s[e]) && s[e] != '<')
        {
            e++;
        }

        e = TrimTrailing(s, i, e);
        if (e <= domainStart)
        {
            return -1;
        }

        dest = www ? "http://" + s[i..e] : s[i..e];
        return e;
    }

    // GFM: trailing punctuation is not part of the link; a ')' stays only when balanced; a trailing entity reference like &amp; is cut.
    private static int TrimTrailing(string s, int start, int end)
    {
        int open = 0;
        int close = 0;
        for (int k = start; k < end; k++)
        {
            if (s[k] == '(')
            {
                open++;
            }
            else if (s[k] == ')')
            {
                close++;
            }
        }

        while (end > start)
        {
            char c = s[end - 1];
            if (c is '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~' or '\'' or '"')
            {
                end--;
                continue;
            }

            if (c == ')')
            {
                if (close > open)
                {
                    end--;
                    close--;
                    continue;
                }

                break;
            }

            if (c == ';')
            {
                int k = end - 2;
                while (k > start && char.IsAsciiLetterOrDigit(s[k]))
                {
                    k--;
                }

                if (k > start && s[k] == '&' && k < end - 2)
                {
                    end = k;
                    continue;
                }
            }

            break;
        }

        return end;
    }

    // The domain of an e-mail autolink: labels of letters, digits, '-' and '_' with at least one period; the last character is not '-' or '_'.
    private static int MatchEmailDomain(string s, int start)
    {
        int e = start;
        while (e < s.Length && (char.IsAsciiLetterOrDigit(s[e]) || s[e] is '-' or '_' or '.'))
        {
            e++;
        }

        // A trailing period is punctuation, not part of the address.
        while (e > start && s[e - 1] == '.')
        {
            e--;
        }

        if (e <= start || s[e - 1] is '-' or '_')
        {
            return -1;
        }

        return s.IndexOf('.', start, e - start) >= 0 && s[start] != '.' ? e : -1;
    }
}
