using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents with equations: every Office Math object inline and as display math, wrapped display math and justification.
// They join the layout case set (LayoutCases.All), so the tests render them and the differential tool sends them to Word.
internal static class MathCases
{
    internal const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    internal static string R(string text, string? rPr = null) =>
        "<m:r>" + (rPr is null ? string.Empty : "<m:rPr>" + rPr + "</m:rPr>") + "<m:t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(text) + "</m:t></m:r>";

    internal static string E(string inner) => "<m:e>" + inner + "</m:e>";

    internal static string Frac(string num, string den, string? type = null) =>
        "<m:f>" + (type is null ? string.Empty : "<m:fPr><m:type m:val=\"" + type + "\"/></m:fPr>") + "<m:num>" + num + "</m:num><m:den>" + den + "</m:den></m:f>";

    internal static string Sup(string b, string s) => "<m:sSup><m:e>" + b + "</m:e><m:sup>" + s + "</m:sup></m:sSup>";

    internal static string Sub(string b, string s) => "<m:sSub><m:e>" + b + "</m:e><m:sub>" + s + "</m:sub></m:sSub>";

    internal static string Rad(string e, string? deg = null) =>
        "<m:rad>" + (deg is null ? "<m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/>" : "<m:deg>" + deg + "</m:deg>") + "<m:e>" + e + "</m:e></m:rad>";

    internal static string Nary(string chr, string sub, string sup, string e, string? limLoc = null) =>
        "<m:nary><m:naryPr><m:chr m:val=\"" + chr + "\"/>" + (limLoc is null ? string.Empty : "<m:limLoc m:val=\"" + limLoc + "\"/>") + "</m:naryPr><m:sub>" + sub + "</m:sub><m:sup>" + sup + "</m:sup><m:e>" + e + "</m:e></m:nary>";

    internal static string Delim(string open, string close, params string[] items) =>
        "<m:d><m:dPr><m:begChr m:val=\"" + open + "\"/><m:endChr m:val=\"" + close + "\"/></m:dPr>" + string.Concat(items.Select(E)) + "</m:d>";

    internal static string Matrix(string[][] rows) =>
        "<m:m>" + string.Concat(rows.Select(r => "<m:mr>" + string.Concat(r.Select(E)) + "</m:mr>")) + "</m:m>";

    // A paragraph of text around an inline equation.
    internal static string Inline(string before, string math, string after, string? pPr = null) =>
        "<w:p>" + (pPr is null ? string.Empty : "<w:pPr>" + pPr + "</w:pPr>") + "<w:r><w:t xml:space=\"preserve\">" + before + "</w:t></w:r><m:oMath xmlns:m=\"" + M + "\">" + math + "</m:oMath><w:r><w:t xml:space=\"preserve\">" + after + "</w:t></w:r></w:p>";

    // A paragraph that is a display equation.
    internal static string Display(string math, string? jc = null, string? pPr = null) =>
        "<w:p>" + (pPr is null ? string.Empty : "<w:pPr>" + pPr + "</w:pPr>") + "<m:oMathPara xmlns:m=\"" + M + "\">" + (jc is null ? string.Empty : "<m:oMathParaPr><m:jc m:val=\"" + jc + "\"/></m:oMathParaPr>") + "<m:oMath>" + math + "</m:oMath></m:oMathPara></w:p>";

    private static string Head(string text) => TestDocx.P(text, "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/>", "<w:b/>");

    private static string MathPr(string inner) => "<w:settings xmlns:w=\"" + TestDocx.W + "\"><m:mathPr xmlns:m=\"" + M + "\">" + inner + "</m:mathPr></w:settings>";

    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        void Add(string name, string body, string? settings = null) => list.Add((name, TestDocx.Bytes(body, settings: settings)));

        string x = R("x");
        string y = R("y");

        Add("80-math-fractions-scripts",
            Head("Fractions and scripts") +
            Inline("Inline fraction ", Frac(R("a+b"), R("c")), " in a sentence, and ") +
            Inline("a script ", Sup(x, R("2")) + R("+") + Sub(y, R("i")) + R("+") + "<m:sSubSup><m:e>" + R("z") + "</m:e><m:sub>" + R("k") + "</m:sub><m:sup>" + R("n") + "</m:sup></m:sSubSup>", " here.") +
            Display(Frac(R("1"), R("1+") + Frac(R("1"), R("1+") + Frac(R("1"), R("x"))))) +
            Display(Frac(R("a"), R("b"), "skw") + R("=") + Frac(R("a"), R("b"), "lin") + R("=") + Frac(R("n"), R("k"), "noBar")) +
            Display("<m:sPre><m:sub>" + R("a") + "</m:sub><m:sup>" + R("b") + "</m:sup><m:e>" + R("X") + "</m:e></m:sPre>" + R("+") + Sup(R("e"), Sup(x, R("2")))));

        Add("81-math-radicals-delimiters",
            Head("Radicals and delimiters") +
            Inline("Roots ", Rad(R("x+1")) + R(",") + Rad(R("x"), R("3")), " inline.") +
            Display(R("x=") + Frac(R("-b\u00B1") + Rad(Sup(R("b"), R("2")) + R("-4ac")), R("2a"))) +
            Display(Delim("(", ")", Frac(R("a"), R("b"))) + Delim("[", "]", Frac(R("a"), Frac(R("b"), R("c")))) + Delim("{", "}", R("x"), R("y")) + Delim("\u2308", "\u230B", Frac(R("p"), R("q")))) +
            Display(Delim("(", ")", Matrix([[R("1"), R("0"), R("0")], [R("0"), R("1"), R("0")], [R("0"), R("0"), R("1")]]))));

        Add("82-math-nary-limits",
            Head("Large operators") +
            Inline("Sum ", Nary("\u2211", R("i=1"), R("n"), Sub(R("a"), R("i"))), " inline, ") +
            Display(Nary("\u2211", R("i=1"), R("n"), Sup(R("i"), R("2"))) + R("=") + Frac(R("n(n+1)(2n+1)"), R("6"))) +
            Display(Nary("\u222B", R("0"), R("\u221E"), Sup(R("e"), R("-x^2")) + R("dx")) + R("=") + Frac(Rad(R("\u03C0")), R("2"))) +
            Display(Nary("\u220F", R("k=1"), R("n"), R("k"), "subSup") + R("=") + Nary("\u22C3", R("i"), R("n"), Sub(R("A"), R("i")))) +
            Display("<m:limLow><m:e>" + R("lim", "<m:sty m:val=\"p\"/>") + "</m:e><m:lim>" + R("x\u2192\u221E") + "</m:lim></m:limLow>" + Frac(R("1"), R("x")) + R("=0")));

        Add("83-math-matrix-eqarray",
            Head("Matrices and equation arrays") +
            Display(Matrix([[R("a"), R("bbb")], [R("cc"), R("d")]])) +
            Display("<m:eqArr>" +
                E(R("x") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=1+2</m:t></m:r>") +
                E(R("yyy") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=3</m:t></m:r>") +
                E(R("zz") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=4+5+6</m:t></m:r>") + "</m:eqArr>") +
            Display(Delim("{", string.Empty, "<m:eqArr>" + E(R("x+y=2")) + E(R("x-y=0")) + "</m:eqArr>")));

        Add("84-math-accents-bars-groups",
            Head("Accents, bars, group characters, boxes") +
            Display("<m:acc><m:accPr><m:chr m:val=\"\u0302\"/></m:accPr><m:e>" + x + "</m:e></m:acc>" + "<m:acc><m:accPr><m:chr m:val=\"\u0303\"/></m:accPr><m:e>" + R("abc") + "</m:e></m:acc>" +
                "<m:acc><m:accPr><m:chr m:val=\"\u20D7\"/></m:accPr><m:e>" + R("v") + "</m:e></m:acc>") +
            Display("<m:bar><m:barPr><m:pos m:val=\"top\"/></m:barPr><m:e>" + R("x+y") + "</m:e></m:bar>" + "<m:bar><m:barPr><m:pos m:val=\"bot\"/></m:barPr><m:e>" + R("x+y") + "</m:e></m:bar>") +
            Display("<m:groupChr><m:groupChrPr><m:chr m:val=\"\u23DF\"/><m:pos m:val=\"bot\"/></m:groupChrPr><m:e>" + R("a+b+c+d") + "</m:e></m:groupChr>" +
                "<m:groupChr><m:groupChrPr><m:chr m:val=\"\u23DE\"/><m:pos m:val=\"top\"/></m:groupChrPr><m:e>" + R("e+f+g+h") + "</m:e></m:groupChr>") +
            Display("<m:borderBox><m:e>" + R("E=mc") + Sup(R(string.Empty), R("2")) + "</m:e></m:borderBox>" + "<m:box><m:e>" + R("box") + "</m:e></m:box>" +
                "<m:borderBox><m:borderBoxPr><m:strikeBLTR m:val=\"1\"/></m:borderBoxPr><m:e>" + R("x") + "</m:e></m:borderBox>" +
                "<m:phant><m:phantPr><m:show m:val=\"0\"/></m:phantPr><m:e>" + R("hidden") + "</m:e></m:phant>" + R("end")));

        Add("85-math-functions-text-styles",
            Head("Functions, text and styles") +
            Display("<m:func><m:fName>" + R("sin", "<m:sty m:val=\"p\"/>") + "</m:fName><m:e>" + R("\u03B8") + "</m:e></m:func>" + R("+") +
                "<m:func><m:fName>" + R("cos", "<m:sty m:val=\"p\"/>") + "</m:fName><m:e>" + R("\u03B8") + "</m:e></m:func>" + R("=1")) +
            Display(R("if ", "<m:nor/>") + R("x>0") + R(" then ", "<m:nor/>") + R("f(x)=\u221Ax")) +
            Display(R("\u0391\u0392\u0393 abc ") + R("bold", "<m:sty m:val=\"b\"/>") + R("R", "<m:scr m:val=\"double-struck\"/>") + R("F", "<m:scr m:val=\"script\"/>") + R("g", "<m:scr m:val=\"fraktur\"/>")) +
            Inline("Text ", R("a") + R("+") + R("b") + R("=") + R("c"), " continues after the equation with enough words to wrap onto a second line of the paragraph."));

        string terms = string.Join("+", Enumerable.Range(1, 18).Select(i => "a" + i + "x"));
        Add("86-math-display-wrap",
            Head("Display equation wider than the line") +
            Display(R("f(x)=" + terms)) +
            Display(R("f(x)=" + terms), pPr: "<w:ind w:left=\"720\" w:right=\"720\"/>"),
            null);
        Add("87-math-display-wrap-break-after",
            Head("Break after the operator, indent 36 pt") +
            Display(R("f(x)=" + terms)) +
            "<w:p><m:oMathPara xmlns:m=\"" + M + "\"><m:oMath>" + R("g=a") + "<m:r><m:rPr><m:brk m:alnAt=\"1\"/></m:rPr><m:t>+b+c</m:t></m:r></m:oMath></m:oMathPara></w:p>",
            MathPr("<m:brkBin m:val=\"after\"/><m:wrapIndent m:val=\"720\"/>"));

        Add("88-math-justification",
            Head("Display justification") +
            Display(Frac(R("a"), R("b")), "left") + Display(Frac(R("a"), R("b")), "right") + Display(Frac(R("a"), R("b")), "center") + Display(Frac(R("a"), R("b"))) +
            Display(Frac(R("a"), R("b")), pPr: "<w:ind w:left=\"1440\"/>"),
            MathPr("<m:defJc m:val=\"left\"/>"));

        Add("89-math-inline-line-height",
            Head("Inline equations raise the line") +
            Inline("Tall inline ", Delim("(", ")", Frac(Frac(R("a"), R("b")), Frac(R("c"), R("d")))), " between lines of plain text.") +
            TestDocx.P("A plain line of the same size follows directly.") +
            Inline("Sum ", Nary("\u2211", R("i=1"), R("n"), R("i")), " and integral ") +
            Inline("Integral ", Nary("\u222B", R("0"), R("1"), R("f(x)dx")), " close to the end of the line, so it must move down intact when the width runs out of room."));

        return list;
    }
}
