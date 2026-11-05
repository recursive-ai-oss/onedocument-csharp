using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Equations;

/// <summary>
/// Reads Office Math markup (OMML, ECMA-376 Part 1 §22.1) into a <see cref="MathNode"/> tree. The tree is a view for layout and text
/// linearisation; reading never changes the markup, and what the model does not know is skipped with a <see cref="MathNote"/>.
/// </summary>
/// <remarks>
/// Nesting deeper than <see cref="MaxDepth"/> and trees with more than <see cref="MaxNodes"/> nodes are cut off with a note rather than
/// failing, so a hostile document can neither exhaust the stack nor the memory. Transitional and Strict namespaces are both read.
/// </remarks>
public sealed class OmmlReader
{
    /// <summary>The deepest nesting of math objects read; deeper content is skipped with a <c>math.depth</c> note.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most nodes read for one equation group; the rest is skipped with a <c>math.size</c> note.</summary>
    public const int MaxNodes = 200_000;

    private readonly List<MathNote> _notes = [];
    private int _nodes;
    private bool _depthNoted;
    private bool _sizeNoted;

    private OmmlReader()
    {
    }

    /// <summary>Gets what the reader skipped or limited.</summary>
    public IReadOnlyList<MathNote> Notes => _notes;

    /// <summary>Reads an <c>m:oMath</c> or <c>m:oMathPara</c> element from the markup tree.</summary>
    /// <param name="element">The element.</param>
    /// <param name="notes">Returns the notes; may be empty.</param>
    /// <returns>The tree, or null when the element is neither <c>m:oMath</c> nor <c>m:oMathPara</c>.</returns>
    public static MathNode? Read(MarkupNode element, out IReadOnlyList<MathNote> notes)
    {
        var reader = new OmmlReader();
        notes = reader._notes;
        if (element.IsNull || element.Kind != MarkupNodeKind.Element || Ns.Classify(element.NamespaceUriBytes) != NsKind.Math)
        {
            return null;
        }

        Elem? root = reader.Capture(element, 0);
        return root is null ? null : reader.Build(root);
    }

    /// <summary>Reads the document-wide settings from a <c>m:mathPr</c> element.</summary>
    /// <param name="mathPr">The element; a null node gives <see cref="MathSettings.Default"/>.</param>
    /// <returns>The settings.</returns>
    public static MathSettings ReadSettings(MarkupNode mathPr)
    {
        if (mathPr.IsNull)
        {
            return MathSettings.Default;
        }

        var reader = new OmmlReader();
        Elem? root = reader.Capture(mathPr, 0);
        return root is null ? MathSettings.Default : ReadSettings(root);
    }

    // ---- Capturing: markup nodes or stream tokens become a small element tree ----

    internal sealed class Elem(string name, bool isMath)
    {
        public readonly string Name = name;
        public readonly bool IsMath = isMath;
        public readonly List<(string Name, string Value)> Attributes = [];
        public readonly List<Elem> Children = [];
        public System.Text.StringBuilder? Text;
        public int Offset = -1;

        public string? Attr(string local)
        {
            foreach ((string n, string v) in Attributes)
            {
                if (n == local)
                {
                    return v;
                }
            }

            return null;
        }

        public Elem? Child(string local, bool math = true)
        {
            foreach (Elem c in Children)
            {
                if (c.Name == local && c.IsMath == math)
                {
                    return c;
                }
            }

            return null;
        }
    }

    private Elem? Capture(MarkupNode node, int depth)
    {
        if (++_nodes > MaxNodes)
        {
            NoteOnce(ref _sizeNoted, "math.size", "The equation has more nodes than the reader allows; the rest was skipped.");
            return null;
        }

        var e = new Elem(node.LocalNameString, Ns.Classify(node.NamespaceUriBytes) == NsKind.Math) { Offset = node.SourceStart };
        foreach (MarkupAttributeRef a in node.Attributes)
        {
            if (!a.IsNamespaceDeclaration)
            {
                e.Attributes.Add((System.Text.Encoding.UTF8.GetString(a.LocalName), a.Value));
            }
        }

        for (MarkupNode c = node.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element)
            {
                if (depth >= MaxDepth)
                {
                    NoteOnce(ref _depthNoted, "math.depth", "The equation is nested deeper than the reader allows; the inner part was skipped.");
                    continue;
                }

                if (Capture(c, depth + 1) is { } child)
                {
                    e.Children.Add(child);
                }
            }
            else if (c.Kind is MarkupNodeKind.Text or MarkupNodeKind.Whitespace or MarkupNodeKind.CData)
            {
                (e.Text ??= new System.Text.StringBuilder()).Append(c.InnerText);
            }
        }

        return e;
    }

    /// <summary>
    /// Collects the tokens of one equation from a forward reader positioned on the start of an <c>m:oMath</c> or <c>m:oMathPara</c>
    /// element, and returns its tree. The reader is left on the matching end tag.
    /// </summary>
    /// <param name="reader">The reader, on the start element.</param>
    /// <returns>The tree, or null for an empty element.</returns>
    internal static MathNode? ReadStream(MarkupReader reader)
    {
        var self = new OmmlReader();
        var stack = new Stack<Elem>();
        Elem? root = null;
        int depth = 0;
        int skipped = 0;
        do
        {
            switch (reader.Kind)
            {
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EmptyElement:
                    if (skipped > 0 || depth >= MaxDepth || ++self._nodes > MaxNodes)
                    {
                        if (reader.Kind == XmlTokenKind.StartElement)
                        {
                            skipped++;
                        }

                        break;
                    }

                    var e = new Elem(System.Text.Encoding.UTF8.GetString(reader.LocalName), Ns.Classify(reader.NamespaceUri) == NsKind.Math);
                    foreach (XmlAttributeToken a in reader.Attributes)
                    {
                        if (!a.IsNamespaceDeclaration)
                        {
                            e.Attributes.Add((System.Text.Encoding.UTF8.GetString(a.LocalName), XmlText.GetString(a.RawValue, isAttributeValue: true)));
                        }
                    }

                    if (stack.Count > 0)
                    {
                        stack.Peek().Children.Add(e);
                    }
                    else
                    {
                        root = e;
                    }

                    if (reader.Kind == XmlTokenKind.StartElement)
                    {
                        stack.Push(e);
                        depth++;
                    }

                    break;
                case XmlTokenKind.EndElement:
                    if (skipped > 0)
                    {
                        skipped--;
                    }
                    else if (stack.Count > 0)
                    {
                        stack.Pop();
                        depth--;
                    }

                    break;
                case XmlTokenKind.Text:
                case XmlTokenKind.Whitespace:
                case XmlTokenKind.CData:
                    if (skipped == 0 && stack.Count > 0 && reader.GetText() is { } t)
                    {
                        (stack.Peek().Text ??= new System.Text.StringBuilder()).Append(t);
                    }

                    break;
            }

            if (stack.Count == 0 && skipped == 0 && root is not null)
            {
                break;
            }
        }
        while (reader.Read());

        return root is null ? null : self.Build(root);
    }

    private void NoteOnce(ref bool flag, string code, string message)
    {
        if (!flag)
        {
            flag = true;
            _notes.Add(new MathNote(code, message));
        }
    }

    // ---- Building: ECMA-376 Part 1 §22.1.2 ----

    private MathNode? Build(Elem root)
    {
        if (!root.IsMath)
        {
            return null;
        }

        if (root.Name == "oMathPara")
        {
            var para = new MathNode(MathKind.OMathPara) { SourceOffset = root.Offset };
            if (root.Child("oMathParaPr") is { } pr && pr.Child("jc") is { } jc)
            {
                para.Properties = new MathProperties { Justification = Jc(jc.Attr("val")) };
            }

            foreach (Elem c in root.Children)
            {
                if (c.IsMath && c.Name == "oMath")
                {
                    para.Add(BuildOMath(c));
                }
            }

            return para;
        }

        return root.Name == "oMath" ? BuildOMath(root) : null;
    }

    private MathNode BuildOMath(Elem e)
    {
        var node = new MathNode(MathKind.OMath) { SourceOffset = e.Offset };
        AddContent(node, e);
        return node;
    }

    // The children of an argument or of an oMath: runs and objects. Insertions are read through; deletions are not part of the text.
    private void AddContent(MathNode parent, Elem e)
    {
        foreach (Elem c in e.Children)
        {
            if (!c.IsMath)
            {
                if (c.Name == "ins" || c.Name == "moveTo")
                {
                    AddContent(parent, c);
                }
                else if (c.Name == "r")
                {
                    // A Word run among math runs: keep its text as normal text.
                    string text = TextOf(c, "t");
                    if (text.Length > 0)
                    {
                        parent.Add(new MathNode(MathKind.Run) { Text = text, Run = new MathRunProperties { NormalText = true }, SourceOffset = c.Offset });
                    }
                }
                else if (c.Name is not ("del" or "moveFrom" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "rPr" or "pPr" or "commentRangeStart" or "commentRangeEnd" or "permStart" or "permEnd"))
                {
                    Skipped(c.Name);
                }

                continue;
            }

            MathNode? n = BuildObject(c);
            if (n is not null)
            {
                parent.Add(n);
            }
        }
    }

    private void Skipped(string name) =>
        _notes.Add(new MathNote("math.unknown-element", $"The element \"{name}\" inside an equation is not part of the math model and was skipped."));

    private static string TextOf(Elem e, string childName)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Elem c in e.Children)
        {
            if (c.Name == childName && c.Text is { } t)
            {
                sb.Append(t);
            }
        }

        return sb.ToString();
    }

    private MathNode? BuildObject(Elem e)
    {
        MathNode n;
        switch (e.Name)
        {
            case "r":
                return BuildRun(e);
            case "acc":
                n = NewObject(MathKind.Accent, e, "accPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "bar":
                n = NewObject(MathKind.Bar, e, "barPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "box":
                n = NewObject(MathKind.Box, e, "boxPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "borderBox":
                n = NewObject(MathKind.BorderBox, e, "borderBoxPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "d":
                n = NewObject(MathKind.Delimiter, e, "dPr");
                foreach (Elem c in e.Children)
                {
                    if (c.IsMath && c.Name == "e")
                    {
                        n.Add(Argument(c, MathRole.Element));
                    }
                }

                return n;
            case "eqArr":
                n = NewObject(MathKind.EquationArray, e, "eqArrPr");
                foreach (Elem c in e.Children)
                {
                    if (c.IsMath && c.Name == "e")
                    {
                        n.Add(Argument(c, MathRole.Element));
                    }
                }

                return n;
            case "f":
                n = NewObject(MathKind.Fraction, e, "fPr");
                Arg(n, e, "num", MathRole.Numerator);
                Arg(n, e, "den", MathRole.Denominator);
                return n;
            case "func":
                n = NewObject(MathKind.Function, e, "funcPr");
                Arg(n, e, "fName", MathRole.FunctionName);
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "groupChr":
                n = NewObject(MathKind.GroupCharacter, e, "groupChrPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "limLow":
                n = NewObject(MathKind.LowerLimit, e, "limLowPr");
                Arg(n, e, "e", MathRole.Base);
                Arg(n, e, "lim", MathRole.Limit);
                return n;
            case "limUpp":
                n = NewObject(MathKind.UpperLimit, e, "limUppPr");
                Arg(n, e, "e", MathRole.Base);
                Arg(n, e, "lim", MathRole.Limit);
                return n;
            case "m":
                n = NewObject(MathKind.Matrix, e, "mPr");
                foreach (Elem row in e.Children)
                {
                    if (row.IsMath && row.Name == "mr")
                    {
                        var r = new MathNode(MathKind.MatrixRow) { SourceOffset = row.Offset };
                        foreach (Elem cell in row.Children)
                        {
                            if (cell.IsMath && cell.Name == "e")
                            {
                                r.Add(Argument(cell, MathRole.Element));
                            }
                        }

                        n.Add(r);
                    }
                }

                return n;
            case "nary":
                n = NewObject(MathKind.NAry, e, "naryPr");
                Arg(n, e, "sub", MathRole.Subscript);
                Arg(n, e, "sup", MathRole.Superscript);
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "phant":
                n = NewObject(MathKind.Phantom, e, "phantPr");
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "rad":
                n = NewObject(MathKind.Radical, e, "radPr");
                Arg(n, e, "deg", MathRole.Degree);
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "sPre":
                n = NewObject(MathKind.PreScript, e, "sPrePr");
                Arg(n, e, "sub", MathRole.Subscript);
                Arg(n, e, "sup", MathRole.Superscript);
                Arg(n, e, "e", MathRole.Base);
                return n;
            case "sSub":
                n = NewObject(MathKind.SubScript, e, "sSubPr");
                Arg(n, e, "e", MathRole.Base);
                Arg(n, e, "sub", MathRole.Subscript);
                return n;
            case "sSup":
                n = NewObject(MathKind.SuperScript, e, "sSupPr");
                Arg(n, e, "e", MathRole.Base);
                Arg(n, e, "sup", MathRole.Superscript);
                return n;
            case "sSubSup":
                n = NewObject(MathKind.SubSuperScript, e, "sSubSupPr");
                Arg(n, e, "e", MathRole.Base);
                Arg(n, e, "sub", MathRole.Subscript);
                Arg(n, e, "sup", MathRole.Superscript);
                return n;
            case "oMath":
                return BuildOMath(e);
            default:
                Skipped(e.Name);
                return null;
        }
    }

    private static MathNode NewObject(MathKind kind, Elem e, string propertiesName)
    {
        var n = new MathNode(kind) { SourceOffset = e.Offset };
        if (e.Child(propertiesName) is { } pr)
        {
            n.Properties = ReadProperties(pr);
        }

        return n;
    }

    private void Arg(MathNode parent, Elem e, string name, MathRole role)
    {
        if (e.Child(name) is { } c)
        {
            parent.Add(Argument(c, role));
        }
    }

    private MathNode Argument(Elem e, MathRole role)
    {
        var a = new MathNode(MathKind.Argument, role) { SourceOffset = e.Offset };
        AddContent(a, e);
        return a;
    }

    private static MathNode BuildRun(Elem e)
    {
        var run = new MathRunProperties();
        if (e.Child("rPr") is { } rp)
        {
            foreach (Elem p in rp.Children)
            {
                if (!p.IsMath)
                {
                    continue;
                }

                switch (p.Name)
                {
                    case "scr":
                        run.Script = p.Attr("val") switch
                        {
                            "script" => MathScript.Script,
                            "fraktur" => MathScript.Fraktur,
                            "double-struck" => MathScript.DoubleStruck,
                            "sans-serif" => MathScript.SansSerif,
                            "monospace" => MathScript.Monospace,
                            _ => MathScript.Roman,
                        };
                        break;
                    case "sty":
                        run.Style = p.Attr("val") switch
                        {
                            "p" => MathStyle.Plain,
                            "b" => MathStyle.Bold,
                            "i" => MathStyle.Italic,
                            "bi" => MathStyle.BoldItalic,
                            _ => MathStyle.Default,
                        };
                        break;
                    case "lit":
                        run.Literal = OnOff(p, true);
                        break;
                    case "nor":
                        run.NormalText = OnOff(p, true);
                        break;
                    case "aln":
                        run.AlignmentPoint = OnOff(p, true);
                        break;
                    case "brk":
                        run.Break = true;
                        run.BreakAlignAt = int.TryParse(p.Attr("alnAt"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int at) ? at : 0;
                        break;
                }
            }
        }

        if (e.Child("rPr", math: false) is { } wp)
        {
            ReadCharacterFormat(wp, run);
        }

        return new MathNode(MathKind.Run) { Text = TextOf(e, "t"), Run = run, SourceOffset = e.Offset };
    }

    private static void ReadCharacterFormat(Elem wp, MathRunProperties run)
    {
        foreach (Elem p in wp.Children)
        {
            if (p.IsMath)
            {
                continue;
            }

            switch (p.Name)
            {
                case "sz":
                    if (double.TryParse(p.Attr("val"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hp) && hp > 0 && hp < 4000)
                    {
                        run.Size = hp / 2.0;
                    }

                    break;
                case "color":
                    if (p.Attr("val") is { Length: 6 } c && uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
                    {
                        run.Color = rgb;
                    }

                    break;
                case "rFonts":
                    run.FontFamily = p.Attr("ascii") ?? p.Attr("hAnsi");
                    break;
                case "b":
                    run.Bold = OnOff(p, true);
                    break;
                case "i":
                    run.Italic = OnOff(p, true);
                    break;
            }
        }
    }

    // ECMA-376 Part 1 §22.1.2.? (CT_OnOff): an element without a value is on.
    private static bool OnOff(Elem e, bool whenAbsent)
    {
        string? v = e.Attr("val");
        return v is null ? whenAbsent : v is "1" or "true" or "on";
    }

    private static MathJustification Jc(string? v) => v switch
    {
        "left" => MathJustification.Left,
        "right" => MathJustification.Right,
        "center" => MathJustification.Center,
        _ => MathJustification.CenterGroup,
    };

    private static MathVerticalAlignment? Vert(string? v) => v switch
    {
        "top" => MathVerticalAlignment.Top,
        "bot" => MathVerticalAlignment.Bottom,
        "bottom" => MathVerticalAlignment.Bottom,
        "baseline" => MathVerticalAlignment.Baseline,
        "center" => MathVerticalAlignment.Center,
        _ => null,
    };

    private static MathSpacingRule Rule(string? v) => v switch
    {
        "1" => MathSpacingRule.OneAndHalf,
        "2" => MathSpacingRule.DoubleLine,
        "3" => MathSpacingRule.Exactly,
        "4" => MathSpacingRule.Multiple,
        _ => MathSpacingRule.SingleLine,
    };

    private static int Int(string? v) => int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : 0;

    private static MathProperties ReadProperties(Elem pr)
    {
        var p = new MathProperties();
        List<MatrixColumnSpec>? columns = null;
        foreach (Elem c in pr.Children)
        {
            if (!c.IsMath)
            {
                continue;
            }

            string? v = c.Attr("val");
            switch (c.Name)
            {
                case "chr":
                    p.Character = v ?? string.Empty;
                    break;
                case "begChr":
                    p.OpenCharacter = v ?? string.Empty;
                    break;
                case "endChr":
                    p.CloseCharacter = v ?? string.Empty;
                    break;
                case "sepChr":
                    p.SeparatorCharacter = v ?? string.Empty;
                    break;
                case "grow":
                    p.Grow = OnOff(c, true);
                    break;
                case "type":
                    p.FractionStyle = v switch
                    {
                        "skw" => FractionStyle.Skewed,
                        "lin" => FractionStyle.Linear,
                        "noBar" => FractionStyle.NoBar,
                        _ => FractionStyle.Bar,
                    };
                    break;
                case "limLoc":
                    p.LimitLocation = v switch
                    {
                        "subSup" => LimitLocation.SubSup,
                        "undOvr" => LimitLocation.UnderOver,
                        _ => LimitLocation.Default,
                    };
                    break;
                case "subHide":
                    p.HideSubscript = OnOff(c, true);
                    break;
                case "supHide":
                    p.HideSuperscript = OnOff(c, true);
                    break;
                case "degHide":
                    p.HideDegree = OnOff(c, true);
                    break;
                case "pos":
                    p.Placement = v == "top" ? MathPlacement.Top : MathPlacement.Bottom;
                    break;
                case "vertJc":
                    p.VerticalAlignment = Vert(v);
                    break;
                case "baseJc":
                    p.BaseJustification = Vert(v);
                    break;
                case "rSpRule":
                    p.RowSpacingRule = Rule(v);
                    break;
                case "rSp":
                    p.RowSpacing = Int(v);
                    break;
                case "cGpRule":
                    p.ColumnGapRule = Rule(v);
                    break;
                case "cGp":
                    p.ColumnGap = Int(v);
                    break;
                case "cSp":
                    p.ColumnSpacing = Int(v);
                    break;
                case "mcs":
                    columns = [];
                    foreach (Elem mc in c.Children)
                    {
                        if (mc.IsMath && mc.Name == "mc" && mc.Child("mcPr") is { } mcPr)
                        {
                            int count = mcPr.Child("count") is { } cnt ? Math.Clamp(Int(cnt.Attr("val")), 1, 1000) : 1;
                            MathJustification j = mcPr.Child("mcJc") is { } mj ? Jc(mj.Attr("val")) : MathJustification.Center;
                            columns.Add(new MatrixColumnSpec(count, j));
                        }
                    }

                    break;
                case "show":
                    p.Show = OnOff(c, true);
                    break;
                case "zeroWid":
                    p.ZeroWidth = OnOff(c, true);
                    break;
                case "zeroAsc":
                    p.ZeroAscent = OnOff(c, true);
                    break;
                case "zeroDesc":
                    p.ZeroDescent = OnOff(c, true);
                    break;
                case "hideTop":
                    p.HideTop = OnOff(c, true);
                    break;
                case "hideBot":
                    p.HideBottom = OnOff(c, true);
                    break;
                case "hideLeft":
                    p.HideLeft = OnOff(c, true);
                    break;
                case "hideRight":
                    p.HideRight = OnOff(c, true);
                    break;
                case "strikeH":
                    p.StrikeHorizontal = OnOff(c, true);
                    break;
                case "strikeV":
                    p.StrikeVertical = OnOff(c, true);
                    break;
                case "strikeBLTR":
                    p.StrikeBottomLeftToTopRight = OnOff(c, true);
                    break;
                case "strikeTLBR":
                    p.StrikeTopLeftToBottomRight = OnOff(c, true);
                    break;
                case "diff":
                    p.Differential = OnOff(c, true);
                    break;
                case "opEmu":
                    p.OperatorEmulation = OnOff(c, true);
                    break;
                case "noBreak":
                    p.NoBreak = OnOff(c, true);
                    break;
                case "ctrlPr":
                    if (c.Child("rPr", math: false) is { } wp)
                    {
                        var tmp = new MathRunProperties();
                        ReadCharacterFormat(wp, tmp);
                        p.ControlSize = tmp.Size;
                    }

                    break;
            }
        }

        if (columns is not null)
        {
            p.Columns = columns;
        }

        return p;
    }

    private static MathSettings ReadSettings(Elem root)
    {
        string font = "Cambria Math";
        BinaryBreak brk = BinaryBreak.Before;
        BinarySubtractBreak sub = BinarySubtractBreak.Minus;
        bool small = false;
        bool dispDef = true;
        double l = 0;
        double r = 0;
        MathJustification jc = MathJustification.CenterGroup;
        double wrapIndent = 72;
        bool wrapRight = false;
        LimitLocation intLim = LimitLocation.SubSup;
        LimitLocation naryLim = LimitLocation.UnderOver;
        foreach (Elem c in root.Children)
        {
            if (!c.IsMath)
            {
                continue;
            }

            string? v = c.Attr("val");
            switch (c.Name)
            {
                case "mathFont":
                    if (!string.IsNullOrEmpty(v))
                    {
                        font = v;
                    }

                    break;
                case "brkBin":
                    brk = v switch { "after" => BinaryBreak.After, "repeat" => BinaryBreak.Repeat, _ => BinaryBreak.Before };
                    break;
                case "brkBinSub":
                    sub = v switch { "+-" => BinarySubtractBreak.PlusMinus, "-+" => BinarySubtractBreak.MinusPlus, _ => BinarySubtractBreak.Minus };
                    break;
                case "smallFrac":
                    small = OnOff(c, true);
                    break;
                case "dispDef":
                    dispDef = OnOff(c, true);
                    break;
                case "lMargin":
                    l = Int(v) / 20.0;
                    break;
                case "rMargin":
                    r = Int(v) / 20.0;
                    break;
                case "defJc":
                    jc = Jc(v);
                    break;
                case "wrapIndent":
                    wrapIndent = Int(v) / 20.0;
                    break;
                case "wrapRight":
                    wrapRight = OnOff(c, true);
                    break;
                case "intLim":
                    intLim = v == "undOvr" ? LimitLocation.UnderOver : LimitLocation.SubSup;
                    break;
                case "naryLim":
                    naryLim = v == "subSup" ? LimitLocation.SubSup : LimitLocation.UnderOver;
                    break;
            }
        }

        return new MathSettings
        {
            MathFont = font,
            BreakBinary = brk,
            BreakSubtract = sub,
            SmallFractions = small,
            DisplayDefault = dispDef,
            LeftMargin = l,
            RightMargin = r,
            DefaultJustification = jc,
            WrapIndent = wrapIndent,
            WrapRight = wrapRight,
            IntegralLimits = intLim,
            NaryLimits = naryLim,
        };
    }
}
