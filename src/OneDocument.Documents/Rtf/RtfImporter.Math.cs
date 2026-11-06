using System.Text;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

// RTF 1.9.1 "Mathematics": a math zone is {\mmath {\*\moMath ...}} (inline) or {\mmath {\*\moMathPara {\*\moMath ...}+}} (display). The groups
// mirror Office Math Markup Language element for element: {\mf ...} is m:f, {\mnum ...} is m:num, a run is {\mr text}, and a property
// such as {\mchr x} is <m:chr m:val="x"/>. Reading it is therefore a renaming, not an interpretation.
internal sealed partial class RtfImporter
{
    private const string MNs = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    // Groups that hold math (arguments and runs); every other group of one text value is a property.
    private static readonly HashSet<string> MathContainers = new(StringComparer.Ordinal)
    {
        "mnum", "mden", "me", "mdeg", "mlim", "mfName", "msub", "msup", "mr", "mmr",
    };

    private void ReadMath(RtfNode g, St st)
    {
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind != RtfNodeKind.Group)
            {
                continue;
            }

            switch (c.Destination)
            {
                case "moMathPara":
                    {
                        var sb = new StringBuilder("<m:oMathPara>");
                        foreach (RtfNode e in c.Children)
                        {
                            if (e.Kind == RtfNodeKind.Group && e.Destination == "moMath")
                            {
                                sb.Append(MathZone(e, st));
                            }
                            else if (e.Kind == RtfNodeKind.Group && e.Destination == "moMathParaPr")
                            {
                                sb.Append("<m:oMathParaPr>").Append(MathChildren(e, st)).Append("</m:oMathParaPr>");
                            }
                        }

                        AppendInline(sb.Append("</m:oMathPara>").ToString());
                        break;
                    }

                case "moMath":
                    AppendInline(MathZone(c, st));
                    break;
            }
        }
    }

    private string MathZone(RtfNode g, St st) => "<m:oMath>" + MathChildren(g, st) + "</m:oMath>";

    // The content of a math group: groups become elements, loose text becomes a run, and control words with a value become properties.
    private string MathChildren(RtfNode g, St st)
    {
        var sb = new StringBuilder();
        var text = new StringBuilder();
        var bytes = new List<byte>();
        St c = st.Clone();
        c.Skip = 0;
        int skip = 0;

        void FlushText()
        {
            if (bytes.Count > 0)
            {
                text.Append(Latin([.. bytes], CodePageOf(c)));
                bytes.Clear();
            }

            if (text.Length > 0)
            {
                sb.Append("<m:r><m:t xml:space=\"preserve\">").Append(X.Esc(text.ToString())).Append("</m:t></m:r>");
                text.Clear();
            }
        }

        foreach (RtfNode n in g.Children)
        {
            switch (n.Kind)
            {
                case RtfNodeKind.Group:
                    FlushText();
                    sb.Append(MathGroup(n, c));
                    break;
                case RtfNodeKind.ControlWord:
                    if (n.Name == "u")
                    {
                        text.Append((char)(ushort)(short)n.Parameter);
                        skip = c.Uc;
                    }
                    else if (n.Name == "uc")
                    {
                        c.Uc = Math.Max(0, n.Parameter);
                    }
                    else if (n.Name == "f")
                    {
                        c.Font = n.Parameter;
                    }
                    else if (n.Name.Length > 1 && n.Name[0] == 'm' && n.HasParameter)
                    {
                        FlushText();
                        sb.Append("<m:").Append(MathName(n.Name)).Append(" m:val=\"").Append(X.N(n.Parameter)).Append("\"/>");
                    }

                    break;
                case RtfNodeKind.Hex:
                    if (skip > 0)
                    {
                        skip--;
                    }
                    else
                    {
                        bytes.Add(n.Hex);
                    }

                    break;
                case RtfNodeKind.Text:
                    foreach (byte b in n.Data.Span)
                    {
                        if (skip > 0)
                        {
                            skip--;
                        }
                        else
                        {
                            bytes.Add(b);
                        }
                    }

                    break;
            }
        }

        FlushText();
        return sb.ToString();
    }

    private string MathGroup(RtfNode g, St st)
    {
        string? dest = g.Destination;
        if (dest is null || dest.Length < 2 || dest[0] != 'm')
        {
            return string.Empty;
        }

        string name = MathName(dest);
        if (dest == "mctrlPr")
        {
            return "<m:ctrlPr/>";
        }

        if (dest == "mr")
        {
            // {\mr\mscr0\msty2 a}: the words with a number are run properties, the rest is the text.
            var props = new StringBuilder();
            var text = new StringBuilder();
            var bytes = new List<byte>();
            int skip = 0;
            int uc = st.Uc;
            foreach (RtfNode n in g.Children)
            {
                switch (n.Kind)
                {
                    case RtfNodeKind.ControlWord when n.Name == "u":
                        text.Append((char)(ushort)(short)n.Parameter);
                        skip = uc;
                        break;
                    case RtfNodeKind.ControlWord when n.Name == "uc":
                        uc = Math.Max(0, n.Parameter);
                        break;
                    case RtfNodeKind.ControlWord when n.Name.Length > 1 && n.Name[0] == 'm' && n.HasParameter:
                        props.Append("<m:").Append(MathName(n.Name)).Append(" m:val=\"").Append(X.N(n.Parameter)).Append("\"/>");
                        break;
                    case RtfNodeKind.Hex when skip > 0:
                        skip--;
                        break;
                    case RtfNodeKind.Hex:
                        bytes.Add(n.Hex);
                        break;
                    case RtfNodeKind.Text:
                        foreach (byte b in n.Data.Span)
                        {
                            if (skip > 0)
                            {
                                skip--;
                            }
                            else
                            {
                                bytes.Add(b);
                            }
                        }

                        break;
                }
            }

            if (bytes.Count > 0)
            {
                text.Append(Latin([.. bytes], CodePageOf(st)));
            }

            return "<m:r>" + (props.Length > 0 ? "<m:rPr>" + props + "</m:rPr>" : string.Empty) +
                (text.Length > 0 ? "<m:t xml:space=\"preserve\">" + X.Esc(text.ToString()) + "</m:t>" : string.Empty) + "</m:r>";
        }

        bool hasGroups = false;
        foreach (RtfNode n in g.Children)
        {
            if (n.Kind == RtfNodeKind.Group)
            {
                hasGroups = true;
                break;
            }
        }

        if (!hasGroups && !MathContainers.Contains(dest))
        {
            // A property in the group form: {\mtype skw} is <m:type m:val="skw"/>.
            string value = PlainText(g, st).Trim();
            return "<m:" + name + (value.Length > 0 ? " m:val=\"" + X.Esc(value) + "\"" : string.Empty) + "/>";
        }

        return "<m:" + name + ">" + MathChildren(g, st) + "</m:" + name + ">";
    }

    // \mXyz is m:xyz with the case of the OMML element name: the first letter after the m is lower case except where the element name
    // itself starts with an upper-case pair (sSup, fName ...), which RTF writes as given.
    private static string MathName(string word) => word switch
    {
        "mlimlow" => "limLow",
        "mlimupp" => "limUpp",
        _ => char.ToLowerInvariant(word[1]) + word[2..],
    };
}
