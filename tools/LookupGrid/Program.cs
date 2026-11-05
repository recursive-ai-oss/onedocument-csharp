using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Tools.LookupGrid;

/// <summary>
/// Formula grid for the lookup, reference, dynamic-array and LAMBDA-helper functions, in the format of <c>office-oracle.sh run --formulas</c>.
/// <c>LookupGrid gen DIR</c> writes <c>look*.fgrid</c> (inputs) and an empty <c>look*.fgrid.tsv</c> beside each (the slot for Excel's answers).
/// <c>LookupGrid compare DIR</c> evaluates every test with OneOffice and compares with the answers that are filled in (empty slots are skipped).
/// <c>LookupGrid selfcheck DIR</c> writes OneOffice's own answers into a temporary copy and compares them, to prove the file format round-trips.
/// Each file begins with three data tests; later tests refer to the cells those spill into (B1:D3, B61:E64, B121:B123).
/// </summary>
internal static class Program
{
    private const char Sep = '\u001f';
    private const int Block = 60;

    private sealed record Test(string Id, string How, string Text);

    private static readonly string[] Data =
    [
        "={1,2,3;4,5,6;7,8,9}",
        "={\"apple\",10,TRUE,1;\"banana\",20,FALSE,2;\"cherry\",30,TRUE,3;\"date\",40,FALSE,4}",
        "={30;20;10}",
    ];

    private static readonly string[] Needles = ["0", "1", "2", "2.5", "3", "4", "\"b\"", "\"B\"", "\"a*\"", "\"?e*\"", "\"~*\"", "\"1\"", "TRUE", "FALSE", "#N/A", "\"\"", "E100", "\"zz\""];

    private static readonly string[] Vectors =
    [
        "{1,2,3,4}", "{1;2;3;4}", "{4,3,2,1}", "{1,1,2,2,3}", "{3,1,2}", "{\"a\",\"b\",\"c\"}", "{\"c\",\"B\",\"a\"}", "{\"apple\",\"banana\",\"*\"}",
        "{1,\"a\",TRUE}", "{\"a\",1,FALSE,2}", "{1,#N/A,3}", "{2,\"\",1}", "{TRUE,FALSE}", "{FALSE,TRUE}", "{5}", "{\"1\",2,3}", "B121:B123", "B1:D1", "B1:B3", "B61:B64",
    ];

    private static IEnumerable<string> Formulas()
    {
        // MATCH, XMATCH over vectors x needles x modes.
        foreach (string v in Vectors)
        {
            foreach (string n in Needles)
            {
                foreach (string t in new[] { "", ",0", ",1", ",-1" })
                {
                    yield return $"MATCH({n},{v}{t})";
                }

                foreach (string m in new[] { "", ",0", ",-1", ",1", ",2", ",0,-1", ",-1,-1", ",1,-1", ",0,2", ",-1,2", ",1,2", ",0,-2", ",-1,-2", ",1,-2", ",5", ",0,0" })
                {
                    yield return $"XMATCH({n},{v}{m})";
                }
            }
        }

        // Random unsorted data: how binary search behaves when the data is not in order.
        var rnd = new Random(20261105);
        string[] pool = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "\"a\"", "\"b\"", "\"c\"", "TRUE", "FALSE", "#N/A", "\"\""];
        for (int i = 0; i < 150; i++)
        {
            int len = rnd.Next(2, 9);
            string arr = "{" + string.Join(",", Enumerable.Range(0, len).Select(_ => pool[rnd.Next(i % 3 == 0 ? pool.Length : 9)])) + "}";
            string needle = pool[rnd.Next(pool.Length - 1)];
            yield return $"MATCH({needle},{arr},1)";
            yield return $"MATCH({needle},{arr},-1)";
            yield return $"XMATCH({needle},{arr},-1,2)";
            yield return $"XMATCH({needle},{arr},1,-2)";
            yield return $"XMATCH({needle},{arr},0,2)";
        }

        // XLOOKUP
        foreach (string f in new[]
        {
            "XLOOKUP(2,{1,2,3},{\"a\",\"b\",\"c\"})", "XLOOKUP(9,{1,2,3},{\"a\",\"b\",\"c\"})", "XLOOKUP(9,{1,2,3},{\"a\",\"b\",\"c\"},\"none\")", "XLOOKUP(9,{1,2,3},{\"a\",\"b\",\"c\"},)",
            "XLOOKUP(2.5,{1,2,3},{10,20,30},,-1)", "XLOOKUP(2.5,{1,2,3},{10,20,30},,1)", "XLOOKUP(\"B*\",{\"a\",\"b\"},{10,20},,2)", "XLOOKUP(2,{1,2,3},{10,20})",
            "XLOOKUP(2,{1,2,3},{10,20,30;40,50,60})", "XLOOKUP(2,{1;2;3},{10,20;30,40;50,60})", "XLOOKUP(2,{1,2,3},{10,20,30;40,50,60;70,80,90})", "XLOOKUP({1,3},{1,2,3},{10,20,30})",
            "XLOOKUP({1;3},{1,2,3},{10,20,30})", "XLOOKUP({1,3},{1;2;3},{10,20;30,40;50,60})", "XLOOKUP(2,B1:D1,B2:D2)", "XLOOKUP(2,B1:B3,B2:D2)", "XLOOKUP(\"cherry\",B61:B64,C61:E64)",
            "XLOOKUP(\"CHERRY\",B61:B64,C61:C64)", "XLOOKUP(\"cherry\",B61:B64,C61:C64):C64", "ROWS(XLOOKUP(\"cherry\",B61:B64,C61:C64):C64)", "XLOOKUP(2,{1,2,2},{10,20,30},,0,-1)", "XLOOKUP(2,{1,2,2},{10,20,30},,0,1)",
            "XLOOKUP(2,{1,2,2,3},{10,20,30,40},,0,2)", "XLOOKUP(2,{3,2,2,1},{10,20,30,40},,0,-2)", "XLOOKUP(\"\",{\"\",\"a\"},{1,2})", "XLOOKUP(#N/A,{#N/A,1},{1,2})", "XLOOKUP(1,{1,2},{#N/A,2})",
            "XLOOKUP(1,{1},{5})", "XLOOKUP(1,5,{5})", "XLOOKUP(1,{1,2;3,4},{1,2})", "XLOOKUP(E100,B1:D3,B1:D3)",
        })
        {
            yield return f;
        }

        // LOOKUP, VLOOKUP, HLOOKUP
        foreach (string n in new[] { "0", "1", "2.5", "4", "9", "\"b\"", "\"cherry\"", "\"c*\"", "\"zzz\"", "TRUE", "E100" })
        {
            foreach (string t in new[] { "B1:D3", "B61:E64", "{1,2,3;\"a\",\"b\",\"c\"}", "{1,2,3,4;5,6,7,8}" })
            {
                foreach (string tail in new[] { "1", "2", "3", "0", "5" })
                {
                    yield return $"VLOOKUP({n},{t},{tail})";
                    yield return $"VLOOKUP({n},{t},{tail},FALSE)";
                    yield return $"VLOOKUP({n},{t},{tail},TRUE)";
                    yield return $"HLOOKUP({n},{t},{tail})";
                    yield return $"HLOOKUP({n},{t},{tail},FALSE)";
                }

                yield return $"VLOOKUP({n},{t},2,)";
                yield return $"LOOKUP({n},{t})";
            }

            yield return $"LOOKUP({n},{{1,2,3}},{{\"a\",\"b\",\"c\"}})";
            yield return $"LOOKUP({n},{{1;2;3}},{{\"a\",\"b\"}})";
            yield return $"LOOKUP({n},{{\"a\",\"b\",\"c\"}},{{1;2;3}})";
        }

        // INDEX extensions, CHOOSE family
        foreach (string f in new[]
        {
            "INDEX((B1:D3,B61:E64),1,1,2)", "INDEX((B1:D3,B61:E64),1,1,3)", "INDEX((B1:D3,B61:E64),1,1,0)", "INDEX((B1:D3,B61:E64),4,1,2)", "INDEX((B1:D3,B61:E64),1,1)", "SUM(INDEX(B1:D3,0,2))", "SUM(INDEX(B1:D3,2,0))",
            "INDEX(B1:D3,2,2,1)", "INDEX(B1:D3,2,2,2)", "INDEX(B1:D3,5)", "INDEX(B1:B3,2)", "INDEX(B1:D1,2)", "INDEX(B1:D3,,2)", "INDEX(B1:D3,2,)", "INDEX(B1:D3,0)", "INDEX(B1:D3,4)",
            "CHOOSE(2,B1,C1,D1)", "SUM(CHOOSE(1,B1:B3,C1:C3))", "CHOOSE({1,2},\"a\",\"b\")", "SUM(CHOOSE(2,B1:B3,C1:C3):D3)",
            "CHOOSECOLS({1,2,3;4,5,6},1,3)", "CHOOSECOLS({1,2,3;4,5,6},-1)", "CHOOSECOLS({1,2,3;4,5,6},4)", "CHOOSECOLS({1,2,3;4,5,6},0)", "CHOOSECOLS({1,2,3;4,5,6},{1,2})", "CHOOSECOLS({1,2,3;4,5,6},1.9)", "CHOOSECOLS({1,2,3;4,5,6},\"2\")", "CHOOSECOLS({1,2,3;4,5,6},\"x\")", "CHOOSECOLS(B1:D3,2)",
            "CHOOSEROWS({1,2;3,4;5,6},{1,3})", "CHOOSEROWS({1,2;3,4;5,6},-1,1)", "CHOOSEROWS({1,2;3,4;5,6},4)", "CHOOSEROWS(5,1)", "CHOOSEROWS({1,2},#N/A)",
        })
        {
            yield return f;
        }

        // Reference functions (the test sits in column B of its block: row 1 + 60 * index)
        foreach (string f in new[]
        {
            "ROW()", "COLUMN()", "ROW(B3)", "ROW(B3:B5)", "ROW(B3:D5)", "COLUMN(C1:E1)", "COLUMN(C1:E3)", "ROW({1,2})", "ROW(5)", "ROW(\"a\")", "ROW(#N/A)", "ROW((B3:B4,D7))", "COLUMN((B3:B4,D7))", "ROW(1:3)", "COLUMN(A:C)",
            "ROWS((B1:B3,D1:D5))", "COLUMNS((B1:B3,D1:E5))", "ROWS(B1:D3)", "ROWS({1,2;3,4})", "ROWS(5)", "ROWS(\"a\")", "COLUMNS(B:D)",
            "AREAS(B1:D3)", "AREAS((B1:D3,B61:E64))", "AREAS(5)", "AREAS({1,2})", "AREAS(#N/A)", "AREAS(B1)",
            "ADDRESS(2,3)", "ADDRESS(2,3,1)", "ADDRESS(2,3,2)", "ADDRESS(2,3,3)", "ADDRESS(2,3,4)", "ADDRESS(2,3,5)", "ADDRESS(2,3,0)", "ADDRESS(2,3,1,FALSE)", "ADDRESS(2,3,2,FALSE)", "ADDRESS(2,3,3,FALSE)", "ADDRESS(2,3,4,FALSE)", "ADDRESS(2,3,1,0)", "ADDRESS(2,3,,FALSE)",
            "ADDRESS(2,3,1,TRUE,\"Sheet2\")", "ADDRESS(2,3,1,TRUE,\"My Sheet\")", "ADDRESS(2,3,1,TRUE,\"it's\")", "ADDRESS(2,3,1,FALSE,\"S\")", "ADDRESS(2,3,1,TRUE,\"\")", "ADDRESS(2,3,1,TRUE,\"A1\")", "ADDRESS(2,3,1,TRUE,\"2020\")", "ADDRESS(2,3,1,TRUE,\"R1C1\")", "ADDRESS(2,3,1,TRUE,5)",
            "ADDRESS(1048576,16384)", "ADDRESS(1048577,1)", "ADDRESS(1,16385)", "ADDRESS(0,1)", "ADDRESS(1,0)", "ADDRESS(-1,1)", "ADDRESS(1.9,2.9)", "ADDRESS(\"2\",\"3\")", "ADDRESS(\"x\",1)", "ADDRESS({1,2},3)", "ADDRESS(1,{1,2,3})", "ADDRESS(#N/A,1)", "ADDRESS(1,27)", "ADDRESS(1,703)",
            "OFFSET(B1,1,1)", "OFFSET(B1,1,1,2,2)", "SUM(OFFSET(B1,0,0,3,3))", "OFFSET(B1,-1,0)", "OFFSET(B1,0,-1)", "OFFSET(B1,0,0,0,1)", "OFFSET(B1,0,0,1,0)", "SUM(OFFSET(B3,0,0,-2,1))", "SUM(OFFSET(D1,0,0,1,-3))", "SUM(OFFSET(B1:B2,1,0))", "SUM(OFFSET(B1:B2,1,0,1,1))",
            "OFFSET(5,1,1)", "OFFSET({1,2},0,0)", "OFFSET((B1,B2),0,0)", "OFFSET(B1,1.9,1.9)", "OFFSET(B1,\"1\",0)", "OFFSET(B1,\"x\",0)", "OFFSET(B1,0,0,,)", "OFFSET(B1,,)", "OFFSET(B1,1048576,0)", "OFFSET(B1,0,16384)", "OFFSET(B1,{1,2},0)", "OFFSET(#REF!,1,1)",
            "INDIRECT(\"B2\")", "SUM(INDIRECT(\"B1:D3\"))", "INDIRECT(\"R2C2\",FALSE)", "INDIRECT(\"R[1]C[1]\",FALSE)", "INDIRECT(\"R2C2\")", "INDIRECT(\"B2\",FALSE)", "INDIRECT(\"Sheet1!B2\")", "INDIRECT(\"'Sheet1'!B2\")", "INDIRECT(\"NoSuchSheet!B2\")", "INDIRECT(\"\")", "INDIRECT(\" B2\")", "INDIRECT(\"B2 \")",
            "INDIRECT(\"$B$2\")", "INDIRECT(\"b2\")", "INDIRECT(\"B2:B3:B4\")", "INDIRECT(\"B:B\")", "ROWS(INDIRECT(\"2:4\"))", "INDIRECT(\"5\")", "INDIRECT(\"B2+1\")", "INDIRECT(\"=B2\")", "INDIRECT(\"nosuchname\")", "INDIRECT(\"(B2)\")", "INDIRECT(\"B1,B2\")", "INDIRECT(\"B1 B1:B2\")",
            "INDIRECT(B2)", "INDIRECT(#N/A)", "INDIRECT({\"B1\",\"B2\"})", "INDIRECT(\"B2\",\"x\")", "INDIRECT(\"B2\",)", "INDIRECT(\"[1]Sheet1!B2\")", "INDIRECT(\"XFD1048576\")", "INDIRECT(\"XFE1\")", "INDIRECT(\"R1C1:R2C2\",FALSE)", "INDIRECT(\"RC\",FALSE)", "INDIRECT(\"R[-1]C\",FALSE)",
            "TRANSPOSE({1,2,3})", "TRANSPOSE({1,2;3,4;5,6})", "TRANSPOSE(7)", "TRANSPOSE(B1:D3)", "TRANSPOSE(E100:E101)", "TRANSPOSE(#N/A)", "TRANSPOSE({1,#N/A})", "TRANSPOSE(\"a\")",
            "HYPERLINK(\"http://x\",\"click\")", "HYPERLINK(\"http://x\")", "HYPERLINK(\"http://x\",5)", "HYPERLINK(\"http://x\",)", "HYPERLINK(5)", "HYPERLINK(E100)", "HYPERLINK(\"x\",E100)", "HYPERLINK(#N/A,\"a\")", "HYPERLINK({\"a\",\"b\"})", "HYPERLINK(\"x\",TRUE)",
            "GETPIVOTDATA(\"Sales\",B1)", "GETPIVOTDATA(\"Sales\",B1,\"Region\",\"East\")", "GETPIVOTDATA(\"Sales\")",
            "TRIMRANGE(B1:E8)", "ROWS(TRIMRANGE(B1:E8))", "COLUMNS(TRIMRANGE(B1:E8))", "ROWS(TRIMRANGE(B1:E8,1))", "ROWS(TRIMRANGE(B1:E8,2))", "ROWS(TRIMRANGE(B1:E8,0,0))", "TRIMRANGE(B1:E8,4)", "TRIMRANGE({1,2;3,4})", "TRIMRANGE(5)", "TRIMRANGE(F1:G2)", "SUM(TRIMRANGE(B1:E8))",
            "SUM(B1:.E8)", "SUM(B1.:E8)", "SUM(B1:E8.)", "SUM(.B1:E8)", "SUM(B1:.E8,0)",
        })
        {
            yield return f;
        }

        // Dynamic arrays
        string m1 = "{1,\"a\";2,\"b\";3,\"c\"}";
        foreach (string f in new[]
        {
            $"FILTER({m1},{{TRUE;FALSE;TRUE}})", $"FILTER({m1},{{TRUE,FALSE}})", $"FILTER({m1},{{1;0;2}})", $"FILTER({m1},{{FALSE;FALSE;FALSE}})", $"FILTER({m1},{{FALSE;FALSE;FALSE}},\"none\")", $"FILTER({m1},{{FALSE;FALSE;FALSE}},)", $"FILTER({m1},{{FALSE;FALSE;FALSE}},{{1,2}})",
            $"FILTER({m1},{{TRUE;FALSE}})", $"FILTER({m1},{{\"a\";TRUE;TRUE}})", $"FILTER({m1},{{TRUE;#N/A;TRUE}})", $"FILTER({m1},{{TRUE;\"TRUE\";TRUE}})", $"FILTER({m1},{{TRUE;0.5;TRUE}})", $"FILTER({m1},E100:E102)", $"FILTER(B61:E64,D61:D64)", $"FILTER(B61:E64,(B61:B64>\"b\")*(C61:C64<40))",
            "FILTER({1,2,3},{TRUE,FALSE,TRUE})", "FILTER({1,2,3},{TRUE;FALSE;TRUE})", "FILTER(5,TRUE)", "FILTER({1,2;3,4},{TRUE,FALSE})", "FILTER({1,2;3,4},{TRUE;FALSE})", "FILTER(E100:E102,{TRUE;TRUE;TRUE})",
            "SORT({3;1;2})", "SORT({3;1;2},1,-1)", "SORT({3,1,2},1,1,TRUE)", "SORT({3,1,2})", "SORT({3,1,2},,,TRUE)", "SORT({2,\"b\";1,\"a\";2,\"a\"},{1,2},{1,1})", "SORT({2,\"b\";1,\"a\";2,\"a\"},{1,2},{-1,1})", "SORT({2,\"b\";1,\"a\";2,\"a\"},{2,1})",
            "SORT({\"b\";TRUE;3;\"a\";FALSE;#N/A;\"B\";-1;\"\"})", "SORT({\"b\";TRUE;3;\"a\";FALSE;#N/A;\"B\";-1;\"\"},1,-1)", "SORT({3;E100;1;E101;2})", "SORT(E100:E103)", "SORT({\"b\";\"a\";\"B\";\"A\";\"b-c\";\"bc\";\"é\";\"e\";\"f\"})", "SORT({#DIV/0!;#N/A;#VALUE!;#REF!;#NUM!;#NAME?;#NULL!})",
            "SORT({1;2},3)", "SORT({1;2},0)", "SORT({1;2},1.5)", "SORT({1;2},1,0)", "SORT({1;2},1,2)", "SORT({1;2},1,\"x\")", "SORT({1;2},\"1\")", "SORT(5)", "SORT({1;2},1,1,\"x\")", "SORT({1;2},1,1,1)", "SORT(B61:E64,3,-1)", "SORT(B61:E64,{3,2},{-1,1})", "SORT(#N/A)",
            "SORTBY({\"a\";\"b\";\"c\"},{3;1;2})", "SORTBY({\"a\";\"b\";\"c\"},{3;1;2},-1)", "SORTBY({\"a\";\"b\";\"c\"},{1;1;0},1,{3;2;1},1)", "SORTBY({\"a\";\"b\";\"c\"},{3;1})", "SORTBY({\"a\",\"b\",\"c\"},{3,1,2})", "SORTBY({\"a\",\"b\",\"c\"},{3;1;2})", "SORTBY({\"a\";\"b\"},{1;2},0)", "SORTBY({\"a\";\"b\"},{1;2},{1,-1})", "SORTBY({\"a\";\"b\"},{1;2},1,{3;4})", "SORTBY(B61:E64,E61:E64,-1)", "SORTBY({\"a\";\"b\"},5)",
            "UNIQUE({1;2;1;3;2})", "UNIQUE({1;2;1;3;2},FALSE,TRUE)", "UNIQUE({\"a\";\"A\";\"b\"})", "UNIQUE({1,2;1,2;3,4})", "UNIQUE({1,1,2;3,3,4},TRUE)", "UNIQUE({1;1},FALSE,TRUE)", "UNIQUE({1;\"1\";TRUE;\"TRUE\"})", "UNIQUE({1;1.0000000000000002})", "UNIQUE({\"\";\"\"})", "UNIQUE(E100:E103)", "UNIQUE({0;E100})", "UNIQUE({#N/A;#N/A})", "UNIQUE(5)", "UNIQUE({1;2},,)", "UNIQUE({1;2},\"x\")", "UNIQUE({\"b\";\"a\";\"b\";\"c\"},,TRUE)",
            "TAKE({1,2,3;4,5,6;7,8,9},2)", "TAKE({1,2,3;4,5,6;7,8,9},-1)", "TAKE({1,2,3;4,5,6;7,8,9},,2)", "TAKE({1,2,3;4,5,6;7,8,9},-2,-2)", "TAKE({1,2;3,4},10)", "TAKE({1,2;3,4},0)", "TAKE({1,2;3,4},1,0)", "TAKE({1,2;3,4},,)", "TAKE({1,2;3,4})", "TAKE({1,2;3,4},1.9)", "TAKE({1,2;3,4},\"1\")", "TAKE({1,2;3,4},\"x\")", "TAKE(5,1)", "TAKE(B1:D3,2,2)", "TAKE({1,2;3,4},{1,2})",
            "DROP({1,2,3;4,5,6;7,8,9},1)", "DROP({1,2,3;4,5,6;7,8,9},-1,1)", "DROP({1,2;3,4},5)", "DROP({1,2;3,4},0)", "DROP({1,2;3,4},,1)", "DROP({1,2;3,4},0,0)", "DROP({1,2;3,4},2)", "DROP({1,2;3,4},1,2)", "DROP({1,2;3,4})",
            "EXPAND({1,2;3,4},3,3)", "EXPAND({1,2;3,4},3,,0)", "EXPAND({1,2;3,4},,3)", "EXPAND({1,2;3,4},1,2)", "EXPAND({1,2;3,4},2,2)", "EXPAND({1,2;3,4},3,3,\"x\")", "EXPAND({1,2;3,4},3,3,{1,2})", "EXPAND({1,2;3,4},0,3)", "EXPAND({1,2;3,4},3.9,3)", "EXPAND({1,2;3,4})", "EXPAND(5,2,2)",
            "HSTACK({1;2},{3;4})", "HSTACK({1;2;3},{4,5})", "HSTACK(1,2,3)", "HSTACK(B1:B2,D1:D3)", "HSTACK(#N/A,1)", "HSTACK({1,#N/A},2)", "HSTACK(E100:E101,1)", "HSTACK({1})", "VSTACK({1,2},{3})", "VSTACK({1,2},3,{4,5})", "VSTACK(#N/A,{1})", "VSTACK(\"a\",{1,2})", "VSTACK(E100:F100,{1,2})", "VSTACK(B1:D1,B61:E61)",
            "TOCOL({1,2;3,4})", "TOCOL({1,2;3,4},0,TRUE)", "TOCOL({1,2;3,4},,TRUE)", "TOROW({1,2;3,4})", "TOROW({1,#N/A;3,4},2)", "TOROW({#N/A},2)", "TOROW({1,2},4)", "TOROW({1,2},-1)", "TOROW(B1:E3,1)", "TOROW(E100:F101,1)", "TOROW(E100:F101)", "TOROW(E100:F101,3)", "TOROW({1,\"\"},1)", "TOCOL(5)", "TOCOL({1,2},\"x\")", "TOROW({1,#N/A,E100},3)",
            "WRAPROWS({1,2,3,4,5},2)", "WRAPROWS({1,2,3,4,5},2,0)", "WRAPROWS({1,2,3,4,5},9)", "WRAPROWS({1,2,3,4,5},0)", "WRAPROWS({1,2,3,4,5},-1)", "WRAPROWS({1,2,3,4,5},1.9)", "WRAPROWS({1;2;3},2)", "WRAPROWS({1,2;3,4},2)", "WRAPROWS(5,2)", "WRAPROWS({1,2,3},{1,2})", "WRAPROWS({1,2,3},2,{1,2})", "WRAPROWS({1,2,3},\"x\")",
            "WRAPCOLS({1,2,3,4,5},2)", "WRAPCOLS({1,2,3,4,5},2,0)", "WRAPCOLS({1;2;3},3)", "WRAPCOLS({1,2;3,4},2)", "WRAPCOLS({1,2,3},0)",
            "ROWS(RANDARRAY(3,2))", "COLUMNS(RANDARRAY(3,2))", "RANDARRAY(1,1,5,5,TRUE)", "RANDARRAY(1,1,5,4)", "RANDARRAY(0)", "RANDARRAY(1,0)", "RANDARRAY(1,1,1.5,3,TRUE)", "RANDARRAY(1,1,-3,-3,TRUE)", "RANDARRAY(2.9,1)", "RANDARRAY(\"2\")", "RANDARRAY(\"x\")", "RANDARRAY(,,,,TRUE)", "AND(RANDARRAY(100)>=0,RANDARRAY(100)<1)", "RANDARRAY(1,1,5,5)", "RANDARRAY(1,1,\"a\")", "RANDARRAY(2,2,1,1,TRUE)",
        })
        {
            yield return f;
        }

        // LAMBDA helpers
        foreach (string f in new[]
        {
            "MAP({1,2,3},LAMBDA(x,x*2))", "MAP({\"a\",\"bb\"},LEN)", "MAP({1,2,3},SQRT)", "MAP({1,2},{10,20},LAMBDA(a,b,a+b))", "MAP({1,2},LAMBDA(x,{1,2}))", "MAP({1,2},ABS)", "MAP({1,2},SUM)", "MAP({1,2},{3,4},SUM)", "MAP({1,2},NOSUCH)", "MAP(B1:B3,LAMBDA(v,v+1))", "MAP(E100:E101,LAMBDA(v,ISBLANK(v)))",
            "REDUCE(0,{1,2,3},LAMBDA(a,v,a+v))", "REDUCE(,{1,2,3},LAMBDA(a,v,a+v))", "REDUCE(10,{1,2,3},SUM)", "REDUCE(\"\",{\"a\",\"b\"},LAMBDA(a,v,a&v))", "REDUCE(0,{1,2},LAMBDA(a,v,{1,2}))", "REDUCE(0,{1,2},LAMBDA(a,v,1/0))", "REDUCE(0,{1,2},LAMBDA(a,1))", "REDUCE(0,{1,2;3,4},LAMBDA(a,v,a+v))", "REDUCE(0,E100:E101,LAMBDA(a,v,a+1))", "REDUCE({1,2},{1,2},LAMBDA(a,v,a))", "REDUCE(0,5,LAMBDA(a,v,a+v))", "REDUCE(,{5},LAMBDA(a,v,a+v))", "REDUCE(0,{1,2},5)", "REDUCE(0,#N/A,LAMBDA(a,v,a))",
            "SCAN(0,{1,2,3},LAMBDA(a,v,a+v))", "SCAN(,{1,2,3},LAMBDA(a,v,a+v))", "SCAN(0,{1,2;3,4},LAMBDA(a,v,a+v))", "SCAN(0,{1,2},LAMBDA(a,v,{1,2}))", "SCAN(0,{1,2,3},SUM)", "SCAN(\"\",{\"a\",\"b\"},LAMBDA(a,v,a&v))", "SCAN(0,{1,2},LAMBDA(a,v,1/0))", "SCAN(0,{1,2},LAMBDA(a,1))",
            "BYROW({1,2;3,4},LAMBDA(r,SUM(r)))", "BYCOL({1,2;3,4},LAMBDA(c,SUM(c)))", "BYROW({1,2;3,4},SUM)", "BYCOL({1,2;3,4},SUM)", "BYROW({1,2;3,4},LAMBDA(r,r))", "BYROW({1,2},LAMBDA(a,b,1))", "BYROW(5,LAMBDA(r,r))", "BYROW({1,2;3,4},LAMBDA(r,ROWS(r)&\"x\"&COLUMNS(r)))", "BYCOL({1,2;3,4},LAMBDA(c,ROWS(c)&\"x\"&COLUMNS(c)))", "BYROW(B1:D3,LAMBDA(r,MAX(r)))", "BYROW({1,2;3,4},LAMBDA(r,1/0))",
            "MAKEARRAY(2,3,LAMBDA(r,c,r*10+c))", "MAKEARRAY(0,1,LAMBDA(r,c,1))", "MAKEARRAY(1,1,5)", "MAKEARRAY(2,2,LAMBDA(r,c,{1,2}))", "MAKEARRAY(1.9,2.9,LAMBDA(r,c,r+c))", "MAKEARRAY(\"2\",1,LAMBDA(r,c,r))", "MAKEARRAY(2,2,SUM)", "MAKEARRAY(2,2,LAMBDA(r,c,RANDARRAY()))", "MAKEARRAY({1,2},1,LAMBDA(r,c,r))", "MAKEARRAY(#N/A,1,LAMBDA(r,c,r))",
            "LAMBDA(x,ISOMITTED(x))(1)", "LAMBDA(x,ISOMITTED(x))()", "LAMBDA(x,y,ISOMITTED(y))(1,)", "LAMBDA(x,[y],ISOMITTED(y))(1)", "LAMBDA(x,[y],IF(ISOMITTED(y),x,x+y))(1)", "LAMBDA(x,[y],IF(ISOMITTED(y),x,x+y))(1,2)", "ISOMITTED(1)", "ISOMITTED(B1)",
            "LET(f,SUM,f(1,2))", "LET(f,LAMBDA(x,x+1),MAP({1,2},f))", "SUM", "SUM(SUM)", "MAP({1,2},LAMBDA(x,x))(1)",
            "GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM)", "GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,0)", "GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,1)", "GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,-1)", "GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,2)", "GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,-2)", "GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,2)", "GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,0)", "GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,,{TRUE;FALSE;TRUE})",
            "GROUPBY({\"k\";\"a\";\"b\"},{\"v\";2;3},SUM,3,0)", "GROUPBY({\"k\";\"a\";\"b\"},{\"v\";2;3},SUM,1,0)", "GROUPBY({\"a\";\"b\"},{2;3},SUM,2,0)", "GROUPBY({\"k\";\"a\";\"b\"},{\"v\";2;3},SUM)", "GROUPBY({\"a\";\"b\";\"a\"},{1;3;3},PERCENTOF)", "GROUPBY({\"a\";\"b\";\"a\"},{1,10;3,30;3,30},SUM)", "GROUPBY({\"a\",\"x\";\"b\",\"x\";\"a\",\"y\"},{1;2;3},SUM)", "GROUPBY({\"a\",\"x\";\"b\",\"x\";\"a\",\"y\"},{1;2;3},SUM,0,2)", "GROUPBY({\"a\",\"x\";\"b\",\"x\";\"a\",\"y\"},{1;2;3},SUM,0,0,,,1)",
            "GROUPBY({\"a\";\"b\"},{1;2},LAMBDA(v,SUM(v)*2))", "GROUPBY({\"a\";\"b\"},{1;2},5)", "GROUPBY({\"a\";\"b\"},{1;2;3},SUM)", "GROUPBY({\"a\";\"b\"},{1;2},LAMBDA(v,v))", "GROUPBY({\"a\";\"b\"},{1;2},HSTACK(SUM,SUM))", "GROUPBY(B61:B64,D61:D64,SUM)", "GROUPBY(B61:B64,D61:D64,SUM,0,0,-2)", "GROUPBY(C61:C64,D61:D64,SUM,0,0)",
            "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM)", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0)", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0,0)", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0,0,,0)", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0,1,-1,1,-1)", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0,1,,1,,{TRUE;TRUE;FALSE})", "PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},PERCENTOF)", "PIVOTBY({\"a\";\"b\"},{\"x\";\"x\"},{1;2},SUM,3)", "PIVOTBY({\"a\";\"b\"},{\"x\";\"x\";\"x\"},{1;2},SUM)",
            "PERCENTOF({1,2},{1,2,3,4})", "PERCENTOF(1,0)", "PERCENTOF({1,\"a\"},{1,2})", "PERCENTOF(B1:B3,B1:D3)", "PERCENTOF(#N/A,1)", "PERCENTOF(1)",
        })
        {
            yield return f;
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: LookupGrid gen|compare|selfcheck DIR");
            return 2;
        }

        return args[0] switch
        {
            "gen" => Generate(args[1]),
            "compare" => Compare(args[1], args[1]),
            "selfcheck" => SelfCheck(args[1]),
            _ => 2,
        };
    }

    private static int Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        var all = Formulas().Distinct().ToList();
        int per = 600;
        int files = (all.Count + per - 1) / per;
        for (int f = 0; f < files; f++)
        {
            string name = $"look{f:000}";
            var lines = new List<string>();
            for (int d = 0; d < Data.Length; d++)
            {
                lines.Add($"{name}.dat{d}\ta1\t{Esc(Data[d])}");
            }

            for (int i = f * per; i < Math.Min(all.Count, (f + 1) * per); i++)
            {
                lines.Add($"{name}.t{i}\ta1\t{Esc("=" + all[i])}");
            }

            File.WriteAllLines(Path.Combine(dir, name + ".fgrid"), lines, new UTF8Encoding(false));
            string slot = Path.Combine(dir, name + ".fgrid.tsv");
            if (!File.Exists(slot))
            {
                File.WriteAllText(slot, string.Empty);
            }
        }

        Console.WriteLine($"{all.Count} formulas in {files} files");
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
                i++;
                sb.Append(s[i] switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => s[i] });
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private sealed class GridResolver : ICalcResolver
    {
        private readonly Dictionary<(int, int), CalcValue> _cells = [];

        public void Put(int row, int col, CalcValue v) => _cells[(row, col)] = v;

        public CalcValue GetCell(string? sheet, int row, int column) => _cells.TryGetValue((row, column), out CalcValue v) ? v : CalcValue.Blank;
    }

    // Excel's numeric error codes: 2000-based offsets from -2146826288.
    private static readonly Dictionary<CalcError, int> Codes = new()
    {
        [CalcError.Null] = -2146826288, [CalcError.Div0] = -2146826281, [CalcError.Value] = -2146826273, [CalcError.Ref] = -2146826265,
        [CalcError.Name] = -2146826259, [CalcError.Num] = -2146826252, [CalcError.NA] = -2146826246, [CalcError.Calc] = -2146826238,
    };

    private static string Item(CalcValue v) => v.Kind switch
    {
        ValueKind.Number => "n:" + v.Number.ToString("R", CultureInfo.InvariantCulture),
        ValueKind.Text => "s:" + Esc(v.Text),
        ValueKind.Boolean => "b:" + (v.Boolean ? "True" : "False"),
        ValueKind.Error => "e:" + (Codes.TryGetValue(v.Error, out int c) ? c : 0).ToString(CultureInfo.InvariantCulture),
        _ => "z:",
    };

    // Evaluates one file's tests in order (data first); returns id -> (rows, cols, items), or the error text when evaluation throws.
    private static Dictionary<string, (int Rows, int Cols, string[] Items)> Evaluate(string file)
    {
        var res = new Dictionary<string, (int, int, string[])>();
        var grid = new GridResolver();
        var ev = new Evaluator(grid);
        int index = 0;
        foreach (string line in File.ReadAllLines(file))
        {
            string[] p = line.Split('\t');
            int row = 1 + (Block * index++);
            string text = Unesc(p[2]).TrimStart('=');
            CalcValue v;
            try
            {
                FormulaNode node = FormulaParser.Parse(text, new FormulaParserOptions { FileForm = false });
                v = ev.Evaluate(node, new CalcCell("Sheet1", row, 2));
            }
            catch (OfficeException)
            {
                v = CalcValue.FromError(CalcError.Value);
            }

            if (v.Kind == ValueKind.Lambda)
            {
                v = CalcValue.FromError(CalcError.Calc);
            }

            CalcArray a = v.Kind == ValueKind.Array ? v.Array! : CalcArray.OfValue(v);
            var items = new string[a.Rows * a.Columns];
            for (int r = 0; r < a.Rows; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    items[(r * a.Columns) + c] = Item(a[r, c]);
                    if (p[0].Contains(".dat", StringComparison.Ordinal))
                    {
                        grid.Put(row + r, 2 + c, a[r, c]);
                    }
                }
            }

            res[p[0]] = (a.Rows, a.Columns, items);
        }

        return res;
    }

    private static int SelfCheck(string dir)
    {
        string tmp = Path.Combine(dir, "selfcheck.tmp");
        Directory.CreateDirectory(tmp);
        foreach (string f in Directory.EnumerateFiles(dir, "*.fgrid"))
        {
            Dictionary<string, (int Rows, int Cols, string[] Items)> mine = Evaluate(f);
            File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)), true);
            File.WriteAllLines(Path.Combine(tmp, Path.GetFileName(f) + ".tsv"), mine.Select(kv => $"{kv.Key}\tok\t\t\t\t{kv.Value.Rows}\t{kv.Value.Cols}\t{string.Join(Sep, kv.Value.Items)}"));
        }

        int rc = Compare(tmp, tmp);
        Directory.Delete(tmp, true);
        return rc;
    }

    private static int Compare(string testDir, string answerDir)
    {
        int compared = 0;
        int bad = 0;
        var report = new List<string>();
        foreach (string f in Directory.EnumerateFiles(testDir, "*.fgrid").OrderBy(x => x, StringComparer.Ordinal))
        {
            string ansFile = Path.Combine(answerDir, Path.GetFileName(f) + ".tsv");
            if (!File.Exists(ansFile) || new FileInfo(ansFile).Length == 0)
            {
                Console.WriteLine($"{Path.GetFileName(f)}: no answers yet");
                continue;
            }

            Dictionary<string, (int Rows, int Cols, string[] Items)> mine = Evaluate(f);
            var texts = File.ReadAllLines(f).Select(l => l.Split('\t')).ToDictionary(p => p[0], p => Unesc(p[2]));
            foreach (string line in File.ReadAllLines(ansFile))
            {
                string[] a = line.Split('\t');
                if (a.Length < 8 || a[1] != "ok" || !mine.TryGetValue(a[0], out (int Rows, int Cols, string[] Items) m))
                {
                    continue; // Excel refused the text at entry: nothing to compare.
                }

                compared++;
                string[] theirs = a[7].Split(Sep);
                string? diff = null;
                if (m.Rows != int.Parse(a[5], CultureInfo.InvariantCulture) || m.Cols != int.Parse(a[6], CultureInfo.InvariantCulture))
                {
                    diff = $"shape ours {m.Rows}x{m.Cols} excel {a[5]}x{a[6]}";
                }
                else
                {
                    for (int i = 0; i < m.Items.Length && diff is null; i++)
                    {
                        string x = m.Items[i];
                        string y = theirs[i];
                        bool same = x == y || (x.StartsWith('z') && (y.StartsWith('z') || y == "n:0")) || (y.StartsWith('z') && x == "n:0") ||
                            (x.StartsWith("n:", StringComparison.Ordinal) && y.StartsWith("n:", StringComparison.Ordinal) && Close(x[2..], y[2..]));
                        if (!same)
                        {
                            diff = $"[{i}] ours {x} excel {y}";
                        }
                    }
                }

                if (diff is not null)
                {
                    bad++;
                    report.Add($"{a[0]} {texts[a[0]]} :: {diff}");
                }
            }
        }

        File.WriteAllLines(Path.Combine(answerDir, "mismatches.txt"), report);
        Console.WriteLine($"compared={compared} disagree={bad}");
        return 0;
    }

    private static bool Close(string a, string b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
        Math.Abs(x - y) <= Math.Max(Math.Abs(x), Math.Abs(y)) * 1e-14;
}
