using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;

namespace RecursiveAI.OneDocument.Tests.Calc;

public class OpenFormulaTests
{
    private static string DataDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../../testdata/ods"));

    private static string Parse(string openFormula)
    {
        Assert.True(OpenFormulaParser.TryParse(openFormula, out FormulaNode? node, out FormulaSyntaxError error), openFormula + ": " + error);
        return FormulaPrinter.Print(node!, new FormulaPrintOptions { Form = FormulaForm.Display, Syntax = FormulaSyntax.A1 });
    }

    [Theory]
    [InlineData("of:=1+2*3", "1+2*3")]
    [InlineData("of:=[.A1]+[$Sheet2.$B$3]", "A1+Sheet2!$B$3")]
    [InlineData("of:=SUM([$'My Sheet'.A1:.B2];[.C3])", "SUM('My Sheet'!A1:B2,C3)")]
    [InlineData("of:=SUM([.A:.A])", "SUM(A:A)")]
    [InlineData("of:=SUM([.$2:.$4])", "SUM($2:$4)")]
    [InlineData("of:=SUM([$Sheet1.A1:$Sheet3.A1])", "SUM(Sheet1:Sheet3!A1)")]
    [InlineData("of:=SUM([$Sheet1.A1:Sheet3.B2])", "SUM(Sheet1:Sheet3!A1:B2)")]
    [InlineData("of:=SUM({1;2|3;4})", "SUM({1,2;3,4})")]
    [InlineData("of:=SUM([.A1:.B2]![.B1:.C3])", "SUM(A1:B2 B1:C3)")]
    [InlineData("of:=SUM(([.A1]~[.B2]))", "SUM((A1,B2))")]
    [InlineData("of:=IF([.A1];;2)", "IF(A1,,2)")]
    [InlineData("of:=TRUE()", "TRUE")]
    [InlineData("of:=COM.MICROSOFT.XLOOKUP(1;[.A1:.A3];[.B1:.B3])", "XLOOKUP(1,A1:A3,B1:B3)")]
    [InlineData("of:=LEGACY.CHIDIST(1;2)", "CHIDIST(1,2)")]
    [InlineData("of:=FDIST(1;2;3;1)", "F.DIST(1,2,3,1)")]
    [InlineData("of:=[.A1]%", "A1%")]
    [InlineData("of:=#N/A", "#N/A")]
    [InlineData("of:=1E+030", "1E+30")]
    public void Reads_open_formula(string source, string expected) => Assert.Equal(expected, Parse(source));

    [Theory]
    [InlineData("of:=[")]
    [InlineData("of:=SUM(")]
    [InlineData("of:=1+")]
    [InlineData("of:=[.A1")]
    [InlineData("of:=\"abc")]
    [InlineData("of:={1;2|3}")]
    [InlineData("of:=[.ZZZZ1]")]
    [InlineData("of:=['file:///x.ods'#$Sheet1.A1]")]
    [InlineData("of:=#BOGUS!")]
    public void Bad_text_is_a_located_error_not_an_exception(string source)
    {
        Assert.False(OpenFormulaParser.TryParse(source, out FormulaNode? node, out FormulaSyntaxError error));
        Assert.Null(node);
        Assert.StartsWith("openformula.", error.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void External_references_are_named_as_such()
    {
        Assert.False(OpenFormulaParser.TryParse("of:=['file:///x.ods'#$Sheet1.A1]", out _, out FormulaSyntaxError error));
        Assert.Equal("openformula.external-reference", error.Code);
    }

    [Fact]
    public void Depth_is_limited()
    {
        string deep = new string('(', 500) + "1" + new string(')', 500);
        Assert.False(OpenFormulaParser.TryParse("of:=" + deep, out _, out FormulaSyntaxError error));
        Assert.Equal("openformula.too-deep", error.Code);
    }

    [Fact]
    public void Printing_reports_what_open_formula_cannot_say()
    {
        var problems = new List<OpenFormulaProblem>();
        FormulaNode node = FormulaParser.Parse("LAMBDA(x,x)(1)+A1#+Table1[Col]+BAHTTEXT(1)");
        string text = OpenFormulaPrinter.Print(node, problems);
        Assert.Contains(problems, p => p.Code == "openformula.invoke");
        Assert.Contains(problems, p => p.Code == "openformula.spill");
        Assert.Contains(problems, p => p.Code == "openformula.structured-reference");
        Assert.Contains(problems, p => p is { Code: "openformula.function", Detail: "BAHTTEXT", Portable: true });
        Assert.DoesNotContain("Table1", text, StringComparison.Ordinal);
    }

    // The expressions LibreOffice 26.8 wrote for a sheet of Excel formulas (testdata/ods/openformula-expressions.tsv): reading its text gives the same tree as the Excel text,
    // and our printer's text reads back to that tree too.
    [Fact]
    public void Matches_what_the_reference_reader_wrote_for_each_expression()
    {
        // Rows whose Excel text is not a plain formula in the input file or that the reference reader spells in its own way.
        string[] skip = ["=TRUE()", "=[1]Sheet1!A1", "=Table1[[#This Row],[Col]]", "=SUM(Table1[Col])", "=@A1:A3", "=A1#", "=LET(x,1,x+1)", "=LAMBDA(x,x)(1)", "=XLOOKUP(1,A1:A3,B1:B3)", "=#REF!", "=A1:B2:C3"];
        int checkedRows = 0;
        foreach (string line in File.ReadAllLines(Path.Combine(DataDir(), "openformula-expressions.tsv")))
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 2 || skip.Contains(parts[0], StringComparer.Ordinal) || !parts[1].StartsWith("of:=", StringComparison.Ordinal))
            {
                continue;
            }

            string excel = FormulaPrinter.Print(FormulaParser.Parse(parts[0][1..]), new FormulaPrintOptions { Form = FormulaForm.Display });
            Assert.Equal(excel, Parse(parts[1]));

            string ours = OpenFormulaPrinter.Print(FormulaParser.Parse(parts[0][1..]));
            Assert.Equal(excel, Parse("of:=" + ours));
            checkedRows++;
        }

        Assert.True(checkedRows >= 55, "rows checked: " + checkedRows);
    }

    // Every function the calc engine has, as LibreOffice 26.8 writes it (testdata/ods/openformula-functions.tsv).
    [Fact]
    public void Function_names_match_the_reference_reader()
    {
        int mapped = 0;
        foreach (string line in File.ReadAllLines(Path.Combine(DataDir(), "openformula-functions.tsv")))
        {
            string[] parts = line.Split('\t');
            string calc = parts[0];
            string written = parts[1]["of:=".Length..parts[1].IndexOf('(', StringComparison.Ordinal)];
            string ours = OpenFormulaFunctions.ToFileName(calc, out bool portable);
            if (portable)
            {
                Assert.Equal(written, ours);
                Assert.Equal(calc, OpenFormulaFunctions.ToCalcName(written));
                mapped++;
            }
            else
            {
                // The reference reader does not know these and writes the name in lower case (with _xlfn. for the newer ones).
                Assert.Equal(calc, written.Replace("_xlfn.", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
            }
        }

        Assert.True(mapped > 450, "functions mapped: " + mapped);
    }
}
