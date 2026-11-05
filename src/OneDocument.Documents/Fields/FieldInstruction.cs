using System.Collections.ObjectModel;
using System.Text;

namespace RecursiveAI.OneDocument.Documents.Fields;

/// <summary>A switch of a field instruction: <c>\h</c>, <c>\o "1-3"</c>, <c>\* MERGEFORMAT</c>.</summary>
/// <param name="Name">The switch name without the backslash, as written (<c>h</c>, <c>o</c>, <c>*</c>).</param>
/// <param name="Value">The argument that follows the switch, without quotes; null when it has none.</param>
public readonly record struct FieldSwitch(string Name, string? Value);

/// <summary>
/// A field instruction split into its field type, positional arguments and switches. The text is the one the document holds
/// (the concatenated <c>w:instrText</c> or the <c>w:instr</c> attribute); parsing never fails and never changes it.
/// </summary>
/// <remarks>
/// <para>ECMA-376 Part 1 §17.16: the first word is the field type, case-insensitive; arguments are words or quoted strings,
/// a backslash followed by a character starts a switch, and inside quotes a backslash escapes the next character. Which switches
/// take an argument depends on the field type (§17.16): the argument of a switch is the next word that is not itself a switch,
/// except for the flag switches listed per type in this parser, which take none. A word that follows a flag stays positional.</para>
/// <para>A formula field (<c>= SUM(ABOVE)</c>) has the type <c>=</c> and the expression as its one argument.</para>
/// </remarks>
public sealed class FieldInstruction
{
    private static readonly Dictionary<string, string> FlagSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        // Switches that never take an argument, by field type (ECMA-376 Part 1 §17.16).
        ["HYPERLINK"] = "mn",
        ["REF"] = "fhnprtw",
        ["NOTEREF"] = "fhp",
        ["PAGEREF"] = "hp",
        ["TOC"] = "huwxz",
        ["TOA"] = "ehp",
        ["MERGEFIELD"] = "mv",
        ["SEQ"] = "chn",
        ["XE"] = "bir",
        ["INDEX"] = "hrz",
        ["DATE"] = "hls",
        ["TIME"] = "hls",
        ["PRINTDATE"] = "hls",
        ["SAVEDATE"] = "hls",
        ["CREATEDATE"] = "hls",
        ["EDITTIME"] = "h",
        ["STYLEREF"] = "lnprtw",
        ["INCLUDETEXT"] = "",
        ["INCLUDEPICTURE"] = "d",
        ["SYMBOL"] = "au",
        ["AUTOTEXT"] = "",
        ["PAGE"] = "",
        ["NUMPAGES"] = "",
    };

    private readonly List<string> _arguments = [];
    private readonly List<FieldSwitch> _switches = [];

    private FieldInstruction(string text)
    {
        Text = text;
        Arguments = new ReadOnlyCollection<string>(_arguments);
        Switches = new ReadOnlyCollection<FieldSwitch>(_switches);
        Type = string.Empty;
    }

    /// <summary>Gets the instruction text as given.</summary>
    public string Text { get; }

    /// <summary>Gets the field type in upper case (<c>TOC</c>, <c>PAGEREF</c>, <c>=</c>); empty for an empty instruction.</summary>
    public string Type { get; private set; }

    /// <summary>Gets the positional arguments in order (a bookmark name, a file name, a formula), quotes removed.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the switches in order of appearance.</summary>
    public IReadOnlyList<FieldSwitch> Switches { get; }

    /// <summary>Gets a value indicating whether every quoted string was closed. False means the last argument runs to the end of the text.</summary>
    public bool IsWellFormed { get; private set; } = true;

    /// <summary>Gets the first argument, or null.</summary>
    public string? FirstArgument => _arguments.Count > 0 ? _arguments[0] : null;

    /// <summary>Finds a switch by name, case-insensitively.</summary>
    /// <param name="name">The switch name without the backslash.</param>
    /// <returns>The first switch with that name, or null.</returns>
    public FieldSwitch? Switch(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (FieldSwitch s in _switches)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Tells whether the instruction has a switch with the given name.</summary>
    /// <param name="name">The switch name without the backslash.</param>
    /// <returns>True when present.</returns>
    public bool HasSwitch(string name) => Switch(name) is not null;

    /// <summary>Parses an instruction. Never throws for any input text.</summary>
    /// <param name="text">The instruction text.</param>
    /// <returns>The parsed instruction.</returns>
    public static FieldInstruction Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new FieldInstruction(text);
        List<(string Value, bool Quoted, bool IsSwitch)> tokens = result.Tokenize(text);
        int i = 0;
        if (tokens.Count > 0 && !tokens[0].IsSwitch)
        {
            string first = tokens[0].Value;
            i = 1;
            if (!tokens[0].Quoted && first.Length > 1 && first[0] == '=')
            {
                // "=SUM(A1)" with no space: the formula is the argument.
                result.Type = "=";
                result._arguments.Add(first[1..]);
            }
            else
            {
                result.Type = first.ToUpperInvariant();
            }
        }

        string flags = FlagSwitches.TryGetValue(result.Type, out string? f) ? f : string.Empty;
        for (; i < tokens.Count; i++)
        {
            (string value, bool quoted, bool isSwitch) = tokens[i];
            if (!isSwitch)
            {
                result._arguments.Add(value);
                continue;
            }

            // The token still carries the backslash; the name is its first character, the rest an attached argument.
            string name = value.Length > 1 ? value.Substring(1, 1) : string.Empty;
            string? attached = value.Length > 2 ? value[2..] : null;
            if (name.Length == 0)
            {
                result._arguments.Add(value);
                continue;
            }

            string? argument = attached;
            bool isFlag = flags.Contains(name, StringComparison.OrdinalIgnoreCase);
            if (argument is null && !isFlag && i + 1 < tokens.Count && !tokens[i + 1].IsSwitch)
            {
                argument = tokens[i + 1].Value;
                i++;
            }

            result._switches.Add(new FieldSwitch(name, argument));
            _ = quoted;
        }

        return result;
    }

    private List<(string Value, bool Quoted, bool IsSwitch)> Tokenize(string text)
    {
        var tokens = new List<(string, bool, bool)>();
        int i = 0;
        var sb = new StringBuilder();
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            sb.Clear();
            if (text[i] == '"' || text[i] == '\u201C' || text[i] == '\u201D')
            {
                // Quoted string; Word's autocorrect may have turned the quotes curly.
                i++;
                bool closed = false;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        sb.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    i++;
                    if (c == '"' || c == '\u201D' || c == '\u201C')
                    {
                        closed = true;
                        break;
                    }

                    sb.Append(c);
                }

                IsWellFormed &= closed;
                tokens.Add((sb.ToString(), true, false));
                continue;
            }

            bool isSwitch = text[i] == '\\' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]);
            if (isSwitch)
            {
                // "\x" then, glued on, an attached argument such as \*MERGEFORMAT or \l"bookmark".
                sb.Append('\\').Append(text[i + 1]);
                i += 2;
                if (i < text.Length && text[i] == '"')
                {
                    i++;
                    var arg = new StringBuilder();
                    bool closed = false;
                    while (i < text.Length)
                    {
                        char c = text[i++];
                        if (c == '"')
                        {
                            closed = true;
                            break;
                        }

                        arg.Append(c);
                    }

                    IsWellFormed &= closed;
                    sb.Append(arg);
                    tokens.Add((sb.ToString(), false, true));
                    continue;
                }
            }

            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                sb.Append(text[i]);
                i++;
            }

            tokens.Add((sb.ToString(), false, isSwitch));
        }

        return tokens;
    }
}
