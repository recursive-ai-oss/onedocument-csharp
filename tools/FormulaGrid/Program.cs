using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Tools.FormulaGrid;

/// <summary>
/// Checks the formula layer against desktop Excel. <c>FormulaGrid gen DIR</c> writes <c>*.fgrid</c> files (the test formulas, generated from
/// value lists and syntax lists below); <c>office-oracle.sh run --formulas DIR</c> has Excel enter each one and write <c>*.fgrid.tsv</c>;
/// <c>FormulaGrid compare DIR [ANSWERS]</c> evaluates and prints the same formulas with OneOffice and lists every disagreement.
/// Operator grid (operators x value kinds), seed-function edge cases, array and lambda shapes, A1 &lt;-&gt; R1C1 and display text.
/// </summary>
internal static class Program
{
    private const char Sep = '\u001f';
    private const int Block = 60;

    private sealed record Test(string Id, string How, string Text);

    private static readonly string[] Values =
    [
        "0", "1", "-1", "2.5", "\"3\"", "\"a\"", "\"\"", "TRUE", "FALSE", "#N/A", "#DIV/0!", "A1", "\"1/2/2020\"", "\"50%\"", "\"1,000\"", "\"12:30\"",
        "\" 7 \"", "\"1e3\"", "\"$5\"", "1E+20", "0.1", "\"B\"",
    ];

    private static readonly string[] Operators = ["+", "-", "*", "/", "^", "&", "=", "<>", "<", "<=", ">", ">="];

    private static readonly string[] Functions =
    [
        "SUM()", "SUM(1,2,3)", "SUM(\"3\",TRUE)", "SUM(\"x\")", "SUM(A1,1)", "SUM({1,\"a\",TRUE})", "SUM(#N/A,1)", "SUM({1,#N/A})", "SUM(1,2,,3)", "SUM(A1:A3,2)",
        "IF(TRUE,1,2)", "IF(FALSE,1)", "IF(TRUE,,2)", "IF(FALSE,1,)", "IF(\"TRUE\",1,2)", "IF(\"x\",1,2)", "IF(0,1,2)", "IF(2,1,2)", "IF(#N/A,1,2)", "IF(A1,1,2)",
        "IF({TRUE,FALSE,TRUE},1,2)", "IF({1,0},{10,20},{30,40})", "IF({TRUE;FALSE},{1,2},3)", "IF({TRUE,FALSE},\"a\")", "IF({TRUE,FALSE},,\"b\")",
        "AND(TRUE,TRUE)", "AND(TRUE,FALSE)", "AND(1,2)", "AND(0,#N/A)", "AND(\"x\")", "AND(\"TRUE\")", "AND({TRUE,\"x\"})", "AND(A1)", "AND(A1:A3)", "AND({1,2},{0})", "AND()",
        "OR(FALSE,FALSE)", "OR(FALSE,1)", "OR(0,0)", "OR(1,#N/A)", "OR(\"x\",TRUE)", "OR({FALSE,\"x\"})", "OR(A1:A3)",
        "NOT(TRUE)", "NOT(0)", "NOT(5)", "NOT(\"TRUE\")", "NOT(\"x\")", "NOT(#N/A)", "NOT({TRUE,FALSE})", "NOT(A1)", "NOT(\"\")",
        "CONCAT(\"a\",\"b\")", "CONCAT(1,2,TRUE)", "CONCAT({\"a\",\"b\";\"c\",\"d\"})", "CONCAT(A1,\"x\")", "CONCAT(#N/A)", "CONCAT({1,#N/A})", "CONCAT(0.1+0.2)", "CONCAT(1E+20)", "CONCAT(1E-10)", "CONCAT(123456789012345678)", "CONCAT(0.000001)", "CONCAT(0.0000001)", "CONCAT(1/3)", "CONCAT(-0.5)", "CONCAT(1E+15)", "CONCAT(1E+14)", "CONCAT(123456789.123456789)",
        "LEN(\"abc\")", "LEN(\"\")", "LEN(123)", "LEN(TRUE)", "LEN(A1)", "LEN(#N/A)", "LEN({\"a\",\"bb\",12345})", "LEN(1/3)", "LEN(1E+20)", "LEN(0.1+0.2)",
        "ISERROR(1)", "ISERROR(#N/A)", "ISERROR(1/0)", "ISERROR(\"x\")", "ISERROR({1,#N/A,\"a\"})", "ISERROR(A1)", "ISERROR(#REF!)", "ISERROR(#CALC!)", "ISERROR(#SPILL!)",
        "ROWS(A1:A5)", "ROWS(A1:C2)", "ROWS(5)", "ROWS({1,2;3,4;5,6})", "ROWS(\"a\")", "ROWS(#N/A)", "COLUMNS(A1:C2)", "COLUMNS({1,2,3})", "COLUMNS(7)", "COLUMNS(A:C)", "ROWS(1:3)", "ROWS(A:A)",
        "INDEX({1,2,3;4,5,6},2,3)", "INDEX({1,2,3;4,5,6},0,2)", "INDEX({1,2,3;4,5,6},2,0)", "INDEX({1,2,3;4,5,6},2)", "INDEX({1,2,3},2)", "INDEX({1;2;3},2)", "INDEX({1,2,3},0,2)", "INDEX({1;2;3},2,1)", "INDEX({1,2,3},1,4)", "INDEX({1,2,3},4)", "INDEX({1,2,3;4,5,6},3,1)", "INDEX({1,2,3;4,5,6},-1,1)", "INDEX({1,2,3;4,5,6},0,0)", "INDEX({1,2,3;4,5,6},,2)", "INDEX(A1:C3,2,2)", "INDEX(A1:C3,4,1)", "INDEX(A1:C3,0,0)", "ROWS(INDEX(A1:C3,0,2))", "INDEX(5,1,1)", "INDEX(5,2)", "INDEX({1,2,3},1.9)", "INDEX({1,2,3},\"2\")", "INDEX({1,2,3},\"x\")", "INDEX({1,2;3,4},{1,2},1)",
        "SEQUENCE(3)", "SEQUENCE(2,3)", "SEQUENCE(2,3,10)", "SEQUENCE(2,3,10,5)", "SEQUENCE(3,,5)", "SEQUENCE(0)", "SEQUENCE(-1)", "SEQUENCE(1,0)", "SEQUENCE(2.9)", "SEQUENCE(\"2\")", "SEQUENCE(\"x\")", "SEQUENCE(#N/A)", "SEQUENCE(1,1,-1,-0.5)", "SEQUENCE(1,3,1,0)", "SEQUENCE(TRUE)", "SEQUENCE(2,2,\"1\")", "SEQUENCE({2,3})", "SEQUENCE(A1)",
        "MAKEARRAY(2,3,LAMBDA(r,c,r*10+c))", "MAKEARRAY(0,1,LAMBDA(r,c,1))", "MAKEARRAY(1,1,LAMBDA(r,c,{1,2}))", "MAKEARRAY(2,2,LAMBDA(r,c,IF(r=c,1,0)))", "MAKEARRAY(2,2,LAMBDA(r,c,r/(c-1)))", "MAKEARRAY(1,2,LAMBDA(r,c,\"a\"&c))", "MAKEARRAY(1,1,5)", "MAKEARRAY(2,2,LAMBDA(r,c,SEQUENCE(1)))", "MAKEARRAY(2,2,LAMBDA(r,1))",
        "MAP({1,2,3},LAMBDA(x,x*2))", "MAP({1,2;3,4},LAMBDA(x,x+1))", "MAP({1,2},{10,20},LAMBDA(a,b,a+b))", "MAP({1,2,3},{10,20},LAMBDA(a,b,a+b))", "MAP({1;2},{10,20},LAMBDA(a,b,a+b))", "MAP(5,LAMBDA(x,x*2))", "MAP({1,2},LAMBDA(x,{1,2}))", "MAP({1,2},LAMBDA(x,#N/A))", "MAP({1,\"a\"},LAMBDA(x,x&\"!\"))", "MAP({1,2},5)", "MAP(SEQUENCE(3),LAMBDA(x,x^2))", "MAP({1,2},LAMBDA(x,y,x))", "MAP(A1:A2,LAMBDA(x,x))", "MAP({1,2},LAMBDA(x,IF(x>1,\"big\",\"small\")))", "MAP({1,2,3},LAMBDA(x,SEQUENCE(x)))",
        "TEXT(1234.5,\"#,##0.00\")", "TEXT(0.5,\"0%\")", "TEXT(43831,\"yyyy-mm-dd\")", "TEXT(\"abc\",\"0\")", "TEXT(\"1\",\"0.00\")", "TEXT(TRUE,\"0\")", "TEXT(A1,\"0.0\")", "TEXT(-1,\"0;(0)\")", "TEXT(1,\"\")", "TEXT(1,\"General\")", "TEXT(#N/A,\"0\")", "TEXT({1,2},\"0.0\")", "TEXT(1.5,\"0\")", "TEXT(\"abc\",\"@@\")", "TEXT(0.123,\"0.0%\")", "TEXT(1,\"[\")",
        "IFERROR(1,2)", "IFERROR(1/0,2)", "IFERROR(#N/A,\"x\")", "IFERROR({1,#N/A},0)", "IFERROR({1,#N/A},{5,6})", "IFERROR(A1,5)", "IFNA(#N/A,1)", "IFNA(1/0,1)", "IFNA({1,#N/A},0)", "IFERROR(1/0,)", "IFERROR(,1)",
        "IFS(FALSE,1,TRUE,2)", "IFS(FALSE,1)", "IFS(1,\"a\")", "IFS(\"x\",1)", "IFS(#N/A,1,TRUE,2)", "IFS({TRUE,FALSE},1,TRUE,2)", "IFS(TRUE,1/0)",
        "CHOOSE(2,\"a\",\"b\",\"c\")", "CHOOSE(0,1,2)", "CHOOSE(4,1,2,3)", "CHOOSE(1.9,\"a\",\"b\")", "CHOOSE(\"2\",1,2)", "CHOOSE(\"x\",1,2)", "CHOOSE({1,2},\"a\",\"b\")", "CHOOSE(#N/A,1)", "CHOOSE(TRUE,5,6)",
        "LET(x,5,x*2)", "LET(x,5,y,x+1,x*y)", "LET(x,{1,2,3},SUM(x))", "LET(x,1,x)", "LET(x,A1,x)", "LET(x,1)", "LET(x,1/0,5)", "LET(x,1,x,2,x)", "LET(a,1,b,a+1,c,b+1,c)", "LET(f,LAMBDA(a,a+1),f(1))", "LET(f,LAMBDA(a,a+1),f)", "LET(x,SEQUENCE(3),x*2)", "LET(x,5,LAMBDA(y,x+y)(1))", "LET(sum,3,sum+1)",
        "LAMBDA(x,x+1)(3)", "LAMBDA(x,y,x*y)(3,4)", "LAMBDA(x,x)(1,2)", "LAMBDA(x,x)()", "LAMBDA(x,x+1)({1,2,3})", "LAMBDA(x,x)", "LAMBDA(5)()", "LAMBDA(x,LAMBDA(y,x+y))(1)(2)",
        "LET(f,LAMBDA(n,IF(n<=1,1,n*f(n-1))),f(5))", "LET(f,LAMBDA(self,n,IF(n<=1,1,n*self(self,n-1))),f(f,5))",
        "A1", "A1:B2", "A1+1", "A1&\"x\"", "A1=0", "A1:A3+1", "A1:A3=0", "SUM(A1:A3+1)", "-A1:A2", "{1,2;3,4}", "{1,2;3,4}+10", "{1,2;3,4}*{1;2}", "{1,2;3,4}*{1,2}", "{1,2,3}+{1;2}", "{1,2}+{1,2,3}", "{1,2;3,4}+{1,2;3,4;5,6}", "{1,2}^2", "{\"a\",\"b\"}&1", "-{1,2}", "{1,2}%", "{1,2}={1,3}", "{TRUE,FALSE}+1", "{1,#N/A}+1", "{1,\"a\"}+1", "1+{1,2}", "{}",
        "(1.1-1-0.1)", "1.1-1-0.1", "1-0.9-0.1", "0.1+0.2-0.3", "(0.1+0.2-0.3)", "(0.1+0.2)-0.3", "=0.1+0.2=0.3", "1.15-1.1", "1.15-1.1-0.05", "100.1-100-0.1", "1E-15+1-1", "1+1E-15-1", "(1+1E-15)-1", "1E16+1-1E16", "3*1.1-3.3", "10.2-10-0.2", "-(0.1+0.2-0.3)", "SUM(1.1,-1,-0.1)", "0.1*3-0.3", "(0.1*3)=0.3", "0.1*3=0.30000000000000004",
        "(0.1+0.2)=0.3", "1.0000000000000002=1", "1.000000000000001=1", "1.00000000000001=1", "123456789012345=123456789012346", "1234567890123456=1234567890123457", "0.1+0.7=0.8", "4.35*100=435", "1E-300=2E-300", "-0=0", "1/3*3=1", "2^0.5*2^0.5=2", "1.1+2.2=3.3",
        "10%", "-5%", "5%%", "-2^2", "2^-1", "2^3^2", "-2^-2", "+\"5\"", "+\"a\"", "-\"a\"", "-\"5\"", "--\"5\"", "-TRUE", "-A1", "-\"\"", "+TRUE", "+A1", "+#N/A", "-#N/A", "#N/A%", "\"a\"%", "\"5\"%", "TRUE%", "A1%", "1E308*10", "-1E308*10", "1E-320/1E10", "2^1024", "2^-1080", "(-8)^(1/3)", "(-8)^3", "0^0", "0^1", "0^-1", "(-0)^-1", "1^1E300", "(-1)^1E300", "(-1)^0.5", "4^0.5", "9^-0.5", "10^308", "10^309", "0/0", "1/0", "-1/0", "0/1", "\"1\"/\"2\"",
        "1&2", "1.0&\"\"", "TRUE&FALSE", "A1&A1", "A1&1", "#N/A&1", "1&#N/A", "#N/A&#REF!", "\"a\"&\"b\"", "0.1+0.2&\"\"", "1/3&\"\"", "1E+20&\"\"", "1E-5&\"\"", "1E-10&\"\"", "100000000000000&\"\"", "1000000000000000&\"\"", "0.000001&\"\"", "0.0000001&\"\"", "0.00000001&\"\"", "0.000000001&\"\"", "-1.5&\"\"", "1234567.891&\"\"", "12345678901234567890&\"\"",
        "\"a\"<\"b\"", "\"a\"<\"B\"", "\"B\"<\"a\"", "\"a\"=\"A\"", "\"a\"<\"A\"", "\"a\">\"A\"", "\"Z\"<\"a\"", "\"_\"<\"a\"", "\"_\"<\"A\"", "\"-\"<\"a\"", "\"a-b\"<\"ab\"", "\"ab\"<\"a b\"", "\"a b\"<\"ab\"", "\"10\"<\"9\"", "\"é\"<\"f\"", "\"é\"=\"e\"", "\"ä\"<\"b\"", "\"ß\"=\"ss\"", "\"\"<\"a\"", "\"a\"<TRUE", "1<\"a\"", "TRUE>100", "FALSE<0", "FALSE=0", "TRUE=1", "\"\"=0", "A1=\"\"", "A1=0", "A1=FALSE", "A1<1", "A1>-1", "A1<\"a\"", "A1>TRUE", "#N/A=#N/A", "#N/A=1", "1<>#DIV/0!", "\"3\"=3", "\"3\"+0=3",
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA", "#SPILL!", "#CALC!", "#BLOCKED!", "#CONNECT!", "#BUSY!", "#FIELD!", "#UNKNOWN!",
        "NOSUCHFUNCTION(1)", "UnknownName", "UnknownName+1", "SUM(UnknownName)", "IFERROR(UnknownName,3)", "ISERROR(UnknownName)",
        "@A1:A3", "@{1,2,3}", "@5", "@SEQUENCE(3)", "@A1",
    ];

    // Column B holds the test formulas themselves, so ranges that would cover it move to X:Z.
    private static string OffColumnB(string f) => f.Replace("A1:C3", "X1:Z3", StringComparison.Ordinal).Replace("A1:C2", "X1:Z2", StringComparison.Ordinal)
        .Replace("A1:B2", "X1:Y2", StringComparison.Ordinal).Replace("A1:A3", "X1:X3", StringComparison.Ordinal).Replace("A1:A5", "X1:X5", StringComparison.Ordinal)
        .Replace("A1:A2", "X1:X2", StringComparison.Ordinal).Replace("A:C", "X:Z", StringComparison.Ordinal);

    // Boundaries Excel decides: when a number becomes scientific text, how near zero a sum snaps to zero, which negative powers are real.
    private static readonly string[] Probes = BuildProbes();

    private static string[] BuildProbes()
    {
        var l = new List<string>();
        foreach (string n in new[] { "1E+15", "1E+16", "1E+17", "1E+18", "1E+19", "9.99999999999999E+19", "1.5E+20", "1E+20", "1E+21", "1E+22", "1.5E+25", "1E+100", "1E-4", "1E-5", "1E-6", "1E-9", "1E-10", "1E-11", "1E-12", "1E-15", "1E-20", "1.5E-11", "0.00001234", "1.234E-9", "1.234E-10", "1.234E-11", "1.234E-20", "1E-300", "-1E-11", "123456.789E-15" })
        {
            l.Add(n + "&\"\"");
        }

        for (int k = 1; k <= 12; k++)
        {
            l.Add($"1+{k}E-15-1");
            l.Add($"(1+{k}E-15)-1");
            l.Add($"1-(1-{k}E-15)");
        }

        foreach (string x in new[] { "100+1E-13-100", "100+2E-13-100", "100+5E-13-100", "1000+1E-12-1000", "1000+4E-12-1000", "1E6+1E-9-1E6", "1E6+5E-9-1E6", "1E15+1-1E15", "0.1+0.2-0.3", "0.3-0.2-0.1", "1.1-1-0.1", "1.1-1", "-(1.1-1-0.1)", "(1.1-1-0.1)", "(1.1-1-0.1)+0", "0+(1.1-1-0.1)", "2*(1.1-1-0.1)", "SUM(0.1,0.2,-0.3)", "SUM(1.1,-1,-0.1)", "SUM(0.1,0.2)-0.3", "(SUM(1.1,-1,-0.1))", "SUM(1.1,-1)-0.1", "SUM({0.1,0.2,-0.3})", "SUM(1E-15,1,-1)", "SUM(1,1E-15,-1)", "SUM(1,2E-15,-1)", "SUM(1,1E-14,-1)", "SUM(1E-15)", "SUM(0.5,0.25,-0.75)", "1E-20+1E-20-2E-20", "IF(TRUE,1.1-1-0.1)", "IFERROR(1.1-1-0.1,0)", "ABS(1.1-1-0.1)" })
        {
            l.Add(x);
        }

        foreach (string x in new[] { "(-8)^(1/3)", "(-27)^(1/3)", "(-8)^(2/3)", "(-8)^0.2", "(-32)^(1/5)", "(-32)^0.2", "(-8)^(-1/3)", "(-1)^1E15", "(-1)^1E16", "(-1)^3E14", "(-1)^9007199254740991", "(-2)^1E15", "(-2)^1E3", "(-2)^101", "(-1)^0.5", "(-1)^(1/3)", "(-16)^(1/4)", "(-1)^(2/3)", "(-8)^0.333333333", "(-8)^0.3333333333333333", "(-1)^-3", "(-2)^-2" })
        {
            l.Add(x);
        }

        foreach (string x in new[] { "@{1,2,3}", "@{1,2;3,4}", "@SEQUENCE(3)", "@X1:X3", "@IF(TRUE,{5,6})" })
        {
            l.Add(x);
        }

        // Second round: where small numbers turn scientific as text, the exact snap boundary in several binades, large integer exponents on a negative base.
        foreach (string n in new[] { "1E-16", "1E-17", "1E-18", "1E-19", "1.5E-16", "1.234E-17", "1.234E-18", "1.234E-19", "123456789012345E-30", "1.5E-17" })
        {
            l.Add(n + "&\"\"");
        }

        foreach (string b in new[] { "1", "100", "1000", "0.1", "0.3" })
        {
            double scale = double.Parse(b, CultureInfo.InvariantCulture);
            double ulp = Math.Pow(2, Math.Floor(Math.Log2(scale)) - 52);
            foreach (int k in new[] { 6, 7, 8, 9, 10 })
            {
                l.Add($"{b}+{(k * ulp).ToString("R", CultureInfo.InvariantCulture)}-{b}");
                l.Add($"SUM({b},{(k * ulp).ToString("R", CultureInfo.InvariantCulture)},-{b})");
            }
        }

        foreach (string x in new[] { "1E6", "1E9", "1E12", "1E13", "1E14", "70368744177664", "140737488355328", "281474976710656", "562949953421312", "100000000000001" })
        {
            l.Add($"(-1)^{x}");
            l.Add($"(-3)^{x}");
        }

        return [.. l];
    }

    private static readonly string[] SyntaxA1 =
    [
        "A1", "$A$1", "A$1", "$A1", "B2+C3", "SUM(A1:B2)", "SUM($A$1:B$2)", "Sheet2!A1", "'My Sheet'!B2:C3", "Sheet2!A:A", "Sheet2!1:1", "A:A", "$A:$C", "1:3", "$1:$3", "A:C", "XFD1048576", "XFD:XFD", "1048576:1048576",
        "SUM(A1:INDEX(B:B,3))", "A1:B2 B1:C3", "SUM((A1:A3,C1:C3))", "SUM(Sheet2:Sheet3!A1)", "SUM(Sheet2:Sheet3!A1:B2)", "IF(A1>0,\"yes\",\"no\")", "{1,2;3,4}", "{\"a\",TRUE;#N/A,-1.5}", "-A1^2", "A1%", "A1&B1",
        "SEQUENCE(3,2)", "LET(x,5,x*2)", "LET(x,A1,y,B1,x+y)", "LAMBDA(x,x+1)(A1)", "MAP(A1:A3,LAMBDA(v,v*2))", "MAKEARRAY(2,2,LAMBDA(r,c,r*c))", "A1#", "@A1:A3", "@A1", "SUM(A1#)", "A1:INDEX(B:B,2)#",
        "IFERROR(A1/B1,0)", "STDEV.S(A1:A3)", "CONCAT(A1:B2)", "TEXT(A1,\"0.00\")", "1E+10+1.5E-3+0.5", "TRUE", "#REF!", "Sheet2!#REF!", "SUM(A1:A3)/COUNT(A1:A3)", "((A1))", "(A1+B1)*C1", "A1<>B1", "A1<=B1", "A1>=B1", "NOT(A1)", "AND(A1,B1)", "INDEX(A1:C3,2,2)",
    ];

    private static readonly string[] SyntaxR1C1 =
    [
        "R1C1", "R[1]C[1]", "R[-1]C[-1]", "RC", "RC[2]", "R[3]C", "R5C", "RC7", "R2C3+R[-1]C[1]", "SUM(R[-1]C:R[-1]C[2])", "SUM(R1C1:R5C5)", "R1:R3", "R[1]:R[2]", "C1:C3", "C[-1]:C[1]", "R", "C", "R:R", "C:C",
        "SUM(R[1]C:R[3]C)", "Sheet2!R1C1", "'My Sheet'!R[1]C[1]:R[2]C[2]", "Sheet2!R1:R3", "IF(RC[-1]>0,RC[-1],0)", "R[-2]C[3]*2", "R1C1#", "@R1C1:R3C1", "SUM(R[-1]C[-1]:R[1]C[1])", "R1048576C16384", "R[1048575]C[16383]",
    ];

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: FormulaGrid gen DIR | compare DIR [ANSWERS_DIR]");
            return 2;
        }

        return args[0] switch
        {
            "gen" => Generate(args[1]),
            "compare" => Compare(args[1], args.Length > 2 ? args[2] : args[1]),
            _ => 2,
        };
    }

    private static IEnumerable<Test> AllTests()
    {
        int i = 0;
        foreach (string a in Values)
        {
            foreach (string b in Values)
            {
                foreach (string op in Operators)
                {
                    yield return new Test($"op{i++}", "a1", "=" + a + op + b);
                }
            }
        }

        i = 0;
        foreach (string f in Functions)
        {
            yield return new Test($"fn{i++}", "a1", "=" + OffColumnB(f));
        }

        i = 0;
        foreach (string f in Probes)
        {
            yield return new Test($"pr{i++}", "a1", "=" + f);
        }

        i = 0;
        foreach (string a in Values)
        {
            yield return new Test($"un{i}", "a1", "=-" + a);
            yield return new Test($"pc{i}", "a1", "=" + a + "%");
            yield return new Test($"pl{i}", "a1", "=+" + a);
            i++;
        }

        i = 0;
        foreach (string s in SyntaxA1)
        {
            yield return new Test($"sa{i++}", "a1", "=" + s);
        }

        i = 0;
        foreach (string s in SyntaxR1C1)
        {
            yield return new Test($"sr{i++}", "r1c1", "=" + s);
        }
    }

    private static int Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        var batch = new List<Test>();
        int n = 0;
        void Flush()
        {
            if (batch.Count == 0)
            {
                return;
            }

            string name = Path.Combine(dir, $"grid{n++:000}.fgrid");
            File.WriteAllLines(name, batch.Select(t => t.Id + "\t" + t.How + "\t" + Esc(t.Text)), new UTF8Encoding(false));
            batch.Clear();
        }

        foreach (Test t in AllTests())
        {
            batch.Add(t);
            if (batch.Count == 700)
            {
                Flush();
            }
        }

        Flush();
        Console.WriteLine($"wrote {n} files");
        return 0;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    private static string Unesc(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char c = s[++i];
                sb.Append(c switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => c });
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private sealed class Blank : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.Blank;
    }

    private static int Compare(string testDir, string answerDir)
    {
        var tests = new Dictionary<string, (Test T, int Index)>();
        var order = new List<Test>();
        foreach (string f in Directory.EnumerateFiles(testDir, "*.fgrid").OrderBy(x => x, StringComparer.Ordinal))
        {
            int idx = 0;
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Split('\t');
                var t = new Test(p[0], p[1], Unesc(p[2]));
                tests[t.Id] = (t, idx++);
                order.Add(t);
            }
        }

        // Excel's numeric codes for the error literals, learned from the answers to the literals themselves.
        var errCode = new Dictionary<int, CalcError> { [-2146826238] = CalcError.Calc };
        var answers = new Dictionary<string, string[]>();
        foreach (string f in Directory.EnumerateFiles(answerDir, "*.fgrid.tsv"))
        {
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Split('\t');
                answers[p[0]] = p;
            }
        }

        string[] literals = Functions.Where(x => x.StartsWith('#')).ToArray();
        for (int i = 0; i < Functions.Length; i++)
        {
            if (Functions[i].StartsWith('#') && answers.TryGetValue($"fn{i}", out string[]? a) && a.Length > 7 && a[7].StartsWith("e:", StringComparison.Ordinal) &&
                CalcErrors.TryParse(Functions[i], out CalcError ce))
            {
                errCode[int.Parse(a[7][2..], CultureInfo.InvariantCulture)] = ce;
            }
        }

        _ = literals;
        var counts = new Dictionary<string, (int Total, int Bad)>();
        var bad = new List<string>();
        var ev = new Evaluator(new Blank());
        foreach (Test t in order)
        {
            string cat = new string(t.Id.TakeWhile(char.IsLetter).ToArray());
            counts.TryGetValue(cat, out (int Total, int Bad) c);
            if (!answers.TryGetValue(t.Id, out string[]? ans))
            {
                counts[cat] = (c.Total, c.Bad);
                continue;
            }

            c.Total++;
            string? diff = cat is "sa" or "sr" ? CompareSyntax(t, ans, tests[t.Id].Index) : CompareValue(t, ans, ev, errCode, tests[t.Id].Index);
            if (diff is not null)
            {
                c.Bad++;
                bad.Add($"{t.Id} [{t.How}] {t.Text} :: {diff}");
            }

            counts[cat] = c;
        }

        foreach (KeyValuePair<string, (int Total, int Bad)> kv in counts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{kv.Key}: compared={kv.Value.Total} disagree={kv.Value.Bad}");
        }

        File.WriteAllLines(Path.Combine(answerDir, "mismatches.txt"), bad);
        Console.WriteLine($"errorCodes learned: {string.Join(", ", errCode.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}");
        Console.WriteLine($"total mismatches: {bad.Count} (see mismatches.txt)");
        return 0;
    }

    private static int RowOf(Test t, int index) => 1 + (Block * index);

    private static string? CompareSyntax(Test t, string[] ans, int index)
    {
        if (ans.Length < 5 || ans[1] != "ok")
        {
            return "excel: " + (ans.Length > 1 ? ans[1] : "?");
        }

        string text = t.Text.TrimStart('=');
        int row = RowOf(t, index);
        try
        {
            string expectedA1 = Unesc(ans[2]).TrimStart('=');
            string expectedR1 = Unesc(ans[3]).TrimStart('=');
            var display = new FormulaPrintOptions { Form = FormulaForm.Display };
            if (t.How == "a1")
            {
                FormulaNode n = FormulaParser.Parse(text, new FormulaParserOptions { FileForm = false });
                string ours = FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.Display, Syntax = FormulaSyntax.R1C1, OriginRow = row, OriginColumn = 2 });
                string oursA1 = FormulaPrinter.Print(n, display);
                return ours == expectedR1 && oursA1 == expectedA1 ? null : $"R1C1 ours '{ours}' excel '{expectedR1}'; A1 ours '{oursA1}' excel '{expectedA1}'";
            }
            else
            {
                FormulaNode n = FormulaParser.Parse(text, new FormulaParserOptions { FileForm = false, Syntax = FormulaSyntax.R1C1, OriginRow = row, OriginColumn = 2 });
                string oursA1 = FormulaPrinter.Print(n, display);
                string ours = FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.Display, Syntax = FormulaSyntax.R1C1, OriginRow = row, OriginColumn = 2 });
                return oursA1 == expectedA1 && ours == expectedR1 ? null : $"A1 ours '{oursA1}' excel '{expectedA1}'; R1C1 ours '{ours}' excel '{expectedR1}'";
            }
        }
        catch (OfficeException ex)
        {
            return "ours rejects: " + ex.Message;
        }
    }

    private static string? CompareValue(Test t, string[] ans, Evaluator ev, Dictionary<int, CalcError> errCode, int index)
    {
        string text = t.Text.TrimStart('=');
        bool parsed = FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError err, new FormulaParserOptions { FileForm = false });
        if (ans.Length < 8 || ans[1] != "ok")
        {
            if (!parsed || node is null)
            {
                return null;
            }

            // Excel refuses some formulas at entry that parse fine (wrong argument count, constants that overflow); ours must then evaluate to an error.
            CalcValue rv;
            try
            {
                rv = ev.Evaluate(node, new CalcCell("Sheet1", RowOf(t, index), 2));
            }
            catch (OfficeException)
            {
                return null;
            }

            return rv.IsError || (rv.Kind == ValueKind.Array && rv.Array![0, 0].IsError) ? null : "excel rejects at entry (" + ans[1] + ") but ours evaluates to " + Show(rv);
        }

        if (!parsed || node is null)
        {
            return "ours rejects (" + err + ") but excel accepts";
        }

        int rows = int.Parse(ans[5], CultureInfo.InvariantCulture);
        int cols = int.Parse(ans[6], CultureInfo.InvariantCulture);
        string[] items = ans[7].Split(Sep);
        CalcValue v;
        try
        {
            v = ev.Evaluate(node, new CalcCell("Sheet1", RowOf(t, index), 2));
        }
        catch (OfficeException ex)
        {
            return "ours throws " + ex.Defect.Code;
        }

        CalcArray a = v.Kind == ValueKind.Array ? v.Array! : CalcArray.OfValue(v);
        if (v.Kind == ValueKind.Lambda)
        {
            a = CalcArray.OfValue(CalcValue.FromError(CalcError.Calc));
        }

        if (a.Rows != rows || a.Columns != cols)
        {
            return $"shape ours {a.Rows}x{a.Columns} excel {rows}x{cols}";
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                string it = items[(r * cols) + c];
                string? d = Same(a[r, c], it, errCode);
                if (d is not null)
                {
                    return $"[{r},{c}] ours {Show(a[r, c])} excel {Unesc(it)}: {d}";
                }
            }
        }

        return null;
    }

    private static string Show(CalcValue v) => v.Kind == ValueKind.Number ? v.Number.ToString("R", CultureInfo.InvariantCulture) : v.ToString();

    private static string? Same(CalcValue ours, string item, Dictionary<int, CalcError> errCode)
    {
        char t = item[0];
        string val = Unesc(item[2..]);
        switch (t)
        {
            case 'z':
            case 'n' when val.Length == 0:
                return ours.IsBlank || (ours.Kind == ValueKind.Number && ours.Number == 0) ? null : "kind";
            case 'n':
                {
                    double x = double.Parse(val, CultureInfo.InvariantCulture);
                    if (ours.IsBlank)
                    {
                        return x == 0 ? null : "kind";
                    }

                    if (ours.Kind != ValueKind.Number)
                    {
                        return "kind";
                    }

                    double y = ours.Number;
                    return x == y || Math.Abs(x - y) <= Math.Max(Math.Abs(x), Math.Abs(y)) * 1e-14 ? null : "value";
                }

            case 's':
                return ours.Kind == ValueKind.Text && ours.Text == val ? null : "text";
            case 'b':
                return ours.Kind == ValueKind.Boolean && ours.Boolean == (val == "True") ? null : "boolean";
            default:
                {
                    int code = int.Parse(val, CultureInfo.InvariantCulture);
                    if (ours.Kind != ValueKind.Error)
                    {
                        return "kind (excel error " + (errCode.TryGetValue(code, out CalcError e0) ? e0.ToString() : code.ToString(CultureInfo.InvariantCulture)) + ")";
                    }

                    return errCode.TryGetValue(code, out CalcError e) ? (e == ours.Error ? null : "error " + e) : "unknown code " + code.ToString(CultureInfo.InvariantCulture);
                }
        }
    }
}
