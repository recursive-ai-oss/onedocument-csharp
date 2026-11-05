using RecursiveAI.OneDocument.Equations;
using static RecursiveAI.OneDocument.Rendering.Tests.MathCases;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// The linear (UnicodeMath-style) form of every object: the alternative text of a rendered equation and what text extraction returns.
public sealed class MathLinearTests
{
    private static string L(string inner) => MathLinearizer.Linearize(MathLayoutTests.Parse(inner));

    [Fact]
    public void LinearFormsFollowUnicodeMath()
    {
        Assert.Equal("(a+b)/c", L(Frac(R("a+b"), R("c"))));
        Assert.Equal("a/b", L(Frac(R("a"), R("b"))));
        Assert.Equal("x^2", L(Sup(R("x"), R("2"))));
        Assert.Equal("x_i", L(Sub(R("x"), R("i"))));
        Assert.Equal("x_k^n", L("<m:sSubSup><m:e>" + R("x") + "</m:e><m:sub>" + R("k") + "</m:sub><m:sup>" + R("n") + "</m:sup></m:sSubSup>"));
        Assert.Equal("\u2211_(i=1)^n\u2592i", L(Nary("\u2211", R("i=1"), R("n"), R("i"))));
        Assert.Equal("\u221A(x+1)", L(Rad(R("x+1"))));
        Assert.Equal("\u221Bx", L(Rad(R("x"), R("3"))));
        Assert.Equal("\u221A(5&x)", L(Rad(R("x"), R("5"))));
        Assert.Equal("(a+b)", L(Delim("(", ")", R("a+b"))));
        Assert.Equal("[a\u2223b]", L("<m:d><m:dPr><m:begChr m:val=\"[\"/><m:sepChr m:val=\"\u2223\"/><m:endChr m:val=\"]\"/></m:dPr>" + E(R("a")) + E(R("b")) + "</m:d>"));
        Assert.Equal("\u25A0(a&b@c&d)", L(Matrix([[R("a"), R("b")], [R("c"), R("d")]])));
        Assert.Equal("\u2588(x&=1@y&=2)", L("<m:eqArr>" + E(R("x") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=1</m:t></m:r>") + E(R("y") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=2</m:t></m:r>") + "</m:eqArr>"));
    }

    [Fact]
    public void EveryObjectLinearisesToNonEmptyText()
    {
        string x = R("x");
        string[] cases =
        [
            "<m:acc><m:e>" + x + "</m:e></m:acc>",
            "<m:bar><m:e>" + x + "</m:e></m:bar>",
            "<m:box><m:e>" + x + "</m:e></m:box>",
            "<m:borderBox><m:e>" + x + "</m:e></m:borderBox>",
            "<m:func><m:fName>" + R("sin") + "</m:fName><m:e>" + x + "</m:e></m:func>",
            "<m:groupChr><m:e>" + x + "</m:e></m:groupChr>",
            "<m:limLow><m:e>" + x + "</m:e><m:lim>" + R("y") + "</m:lim></m:limLow>",
            "<m:limUpp><m:e>" + x + "</m:e><m:lim>" + R("y") + "</m:lim></m:limUpp>",
            "<m:phant><m:e>" + x + "</m:e></m:phant>",
            "<m:sPre><m:sub>" + R("a") + "</m:sub><m:sup>" + R("b") + "</m:sup><m:e>" + x + "</m:e></m:sPre>",
        ];
        foreach (string c in cases)
        {
            Assert.False(string.IsNullOrEmpty(L(c)), c);
        }
    }
}
