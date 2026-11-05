using System.Text;

namespace RecursiveAI.OneDocument.Equations;

/// <summary>
/// Writes an equation tree as UnicodeMath-style linear text, the plain-text form Word's linear format uses: <c>(a+b)/c</c>,
/// <c>x^2</c>, <c>∑_(i=1)^n▒i</c>, <c>√(n&amp;a)</c>, <c>■(a&amp;b@c&amp;d)</c>. The text is used as the alternative text of a laid-out
/// equation and as the text extraction returns for math.
/// </summary>
/// <remarks>
/// The conventions are those of the Unicode Technical Note "UnicodeMath, a nearly plain-text encoding of mathematics": a simple operand
/// is a run of letters and digits and needs no parentheses; anything else is parenthesised; <c>/</c> is a stacked fraction, U+2044 a
/// skewed one, U+2215 a linear one and U+00A6 a stack without a bar; <c>_</c> and <c>^</c> introduce scripts; U+2592 separates an n-ary
/// operator from its operand; <c>@</c> ends a row and <c>&amp;</c> separates cells. The invisible function-apply character is left out so the
/// text reads and searches like plain text: a function name is followed directly by its argument.
/// </remarks>
public static class MathLinearizer
{
    /// <summary>Gets the linear text of a tree.</summary>
    /// <param name="node">An equation, a display group or any object of one. Null gives an empty string.</param>
    /// <returns>The text. Display groups put each equation on its own line (separated by <c>\n</c>).</returns>
    public static string Linearize(MathNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        Write(sb, node, 0);
        return sb.ToString();
    }

    private static string Text(MathNode? n, int depth)
    {
        if (n is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        Write(sb, n, depth);
        return sb.ToString();
    }

    private static void WriteChildren(StringBuilder sb, MathNode n, int depth)
    {
        foreach (MathNode c in n.Children)
        {
            Write(sb, c, depth);
        }
    }

    private static void Write(StringBuilder sb, MathNode n, int depth)
    {
        if (depth > OmmlReader.MaxDepth + 8)
        {
            return;
        }

        depth++;
        MathProperties p = n.Properties;
        switch (n.Kind)
        {
            case MathKind.OMathPara:
                for (int i = 0; i < n.Children.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }

                    Write(sb, n.Children[i], depth);
                }

                break;
            case MathKind.OMath:
            case MathKind.Argument:
                WriteChildren(sb, n, depth);
                break;
            case MathKind.Run:
                if (n.Run is { AlignmentPoint: true })
                {
                    sb.Append('&');
                }

                sb.Append(n.Text);
                break;
            case MathKind.Accent:
                {
                    string b = Text(n.Argument(MathRole.Base), depth);
                    string chr = p.Character ?? "\u0302";
                    sb.Append(IsSimple(b) && b.Length == 1 ? b : Wrap(b)).Append(chr);
                    break;
                }

            case MathKind.Bar:
                sb.Append(p.Placement == MathPlacement.Top ? "\u00AF" : "\u2581").Append(Operand(Text(n.Argument(MathRole.Base), depth)));
                break;
            case MathKind.Box:
                sb.Append('\u25A1').Append(Group(Text(n.Argument(MathRole.Base), depth)));
                break;
            case MathKind.BorderBox:
                sb.Append('\u25AD').Append(Group(Text(n.Argument(MathRole.Base), depth)));
                break;
            case MathKind.Phantom:
                sb.Append('\u27E1').Append(Group(Text(n.Argument(MathRole.Base), depth)));
                break;
            case MathKind.Delimiter:
                {
                    string open = p.OpenCharacter ?? "(";
                    string close = p.CloseCharacter ?? ")";
                    string sep = p.SeparatorCharacter ?? "|";
                    sb.Append(open);
                    for (int i = 0; i < n.Children.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(sep.Length > 0 ? sep : string.Empty);
                        }

                        Write(sb, n.Children[i], depth);
                    }

                    sb.Append(close);
                    break;
                }

            case MathKind.EquationArray:
                sb.Append("\u2588(");
                for (int i = 0; i < n.Children.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('@');
                    }

                    Write(sb, n.Children[i], depth);
                }

                sb.Append(')');
                break;
            case MathKind.Matrix:
                sb.Append("\u25A0(");
                for (int r = 0; r < n.Children.Count; r++)
                {
                    if (r > 0)
                    {
                        sb.Append('@');
                    }

                    MathNode row = n.Children[r];
                    for (int c = 0; c < row.Children.Count; c++)
                    {
                        if (c > 0)
                        {
                            sb.Append('&');
                        }

                        Write(sb, row.Children[c], depth);
                    }
                }

                sb.Append(')');
                break;
            case MathKind.Fraction:
                {
                    string num = Operand(Text(n.Argument(MathRole.Numerator), depth));
                    string den = Operand(Text(n.Argument(MathRole.Denominator), depth));
                    string op = p.FractionStyle switch
                    {
                        FractionStyle.Skewed => "\u2044",
                        FractionStyle.Linear => "\u2215",
                        FractionStyle.NoBar => "\u00A6",
                        _ => "/",
                    };
                    sb.Append(num).Append(op).Append(den);
                    break;
                }

            case MathKind.Function:
                {
                    string name = Text(n.Argument(MathRole.FunctionName), depth);
                    string arg = Text(n.Argument(MathRole.Base), depth);
                    sb.Append(name);
                    if (name.Length > 0 && arg.Length > 0 && char.IsLetterOrDigit(name[^1]) && char.IsLetterOrDigit(arg[0]))
                    {
                        // Keeps "sin" and "x" apart: the invisible function-apply sign is left out of the text.
                        sb.Append(' ');
                    }

                    sb.Append(arg);
                    break;
                }

            case MathKind.GroupCharacter:
                {
                    string chr = p.Character ?? "\u23DF";
                    sb.Append(chr).Append(Group(Text(n.Argument(MathRole.Base), depth)));
                    break;
                }

            case MathKind.LowerLimit:
                sb.Append(Operand(Text(n.Argument(MathRole.Base), depth))).Append('_').Append(Operand(Text(n.Argument(MathRole.Limit), depth)));
                break;
            case MathKind.UpperLimit:
                sb.Append(Operand(Text(n.Argument(MathRole.Base), depth))).Append('^').Append(Operand(Text(n.Argument(MathRole.Limit), depth)));
                break;
            case MathKind.NAry:
                {
                    sb.Append(p.Character ?? "\u222B");
                    if (!p.HideSubscript && Text(n.Argument(MathRole.Subscript), depth) is { Length: > 0 } sub)
                    {
                        sb.Append('_').Append(Operand(sub));
                    }

                    if (!p.HideSuperscript && Text(n.Argument(MathRole.Superscript), depth) is { Length: > 0 } sup)
                    {
                        sb.Append('^').Append(Operand(sup));
                    }

                    sb.Append('\u2592');
                    string body = Text(n.Argument(MathRole.Base), depth);
                    sb.Append(IsSimple(body) || IsDelimited(body) ? body : Wrap(body));
                    break;
                }

            case MathKind.Radical:
                {
                    string deg = p.HideDegree ? string.Empty : Text(n.Argument(MathRole.Degree), depth);
                    string body = Text(n.Argument(MathRole.Base), depth);
                    if (deg.Length == 0)
                    {
                        sb.Append('\u221A').Append(IsSimple(body) ? body : Wrap(StripDelimiters(body)));
                    }
                    else if (deg == "3")
                    {
                        sb.Append('\u221B').Append(IsSimple(body) ? body : Wrap(StripDelimiters(body)));
                    }
                    else if (deg == "4")
                    {
                        sb.Append('\u221C').Append(IsSimple(body) ? body : Wrap(StripDelimiters(body)));
                    }
                    else
                    {
                        sb.Append("\u221A(").Append(deg).Append('&').Append(body).Append(')');
                    }

                    break;
                }

            case MathKind.PreScript:
                sb.Append("(_").Append(Operand(Text(n.Argument(MathRole.Subscript), depth))).Append('^').Append(Operand(Text(n.Argument(MathRole.Superscript), depth))).Append(')');
                sb.Append(Text(n.Argument(MathRole.Base), depth));
                break;
            case MathKind.SubScript:
                sb.Append(Base(Text(n.Argument(MathRole.Base), depth))).Append('_').Append(Operand(Text(n.Argument(MathRole.Subscript), depth)));
                break;
            case MathKind.SuperScript:
                sb.Append(Base(Text(n.Argument(MathRole.Base), depth))).Append('^').Append(Operand(Text(n.Argument(MathRole.Superscript), depth)));
                break;
            case MathKind.SubSuperScript:
                sb.Append(Base(Text(n.Argument(MathRole.Base), depth))).Append('_').Append(Operand(Text(n.Argument(MathRole.Subscript), depth)))
                    .Append('^').Append(Operand(Text(n.Argument(MathRole.Superscript), depth)));
                break;
        }
    }

    // A simple operand is letters and digits only (UnicodeMath §2.1); also a decimal number.
    private static bool IsSimple(string s)
    {
        if (s.Length == 0)
        {
            return true;
        }

        foreach (char c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '\u2032' || c == '\u221E'))
            {
                return false;
            }
        }

        return true;
    }

    // True when the text is one bracketed group such as (a+b), [x] or {y}.
    private static bool IsDelimited(string s)
    {
        if (s.Length < 2)
        {
            return false;
        }

        char open = s[0];
        char close = open switch { '(' => ')', '[' => ']', '{' => '}', '\u27E8' => '\u27E9', _ => '\0' };
        if (close == '\0' || s[^1] != close)
        {
            return false;
        }

        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == open)
            {
                depth++;
            }
            else if (s[i] == close)
            {
                depth--;
                if (depth == 0 && i < s.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    private static string Wrap(string s) => "(" + s + ")";

    private static string StripDelimiters(string s) => s.Length > 2 && s[0] == '(' && IsDelimited(s) ? s[1..^1] : s;

    // An operand of a fraction or a script: simple text as is, a bracketed group as is, otherwise parenthesised.
    private static string Operand(string s) => s.Length == 0 || IsSimple(s) || IsDelimited(s) ? s : Wrap(s);

    private static string Base(string s) => s.Length == 0 || IsSimple(s) || IsDelimited(s) ? s : Wrap(s);

    private static string Group(string s) => s.Length == 0 ? "()" : IsSimple(s) && s.Length == 1 ? s : IsDelimited(s) ? s : Wrap(s);
}
