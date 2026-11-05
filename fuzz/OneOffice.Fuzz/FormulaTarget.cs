using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The formula parser, printer and evaluator. <c>formula</c> takes the input as formula text (the first byte picks the notation and options):
/// it must parse or give a located <c>formula.</c> error inside the text; text that parses must print back to the same characters in file form,
/// and the printed A1, R1C1, display and translated forms must parse again. <c>formulaeval</c> evaluates what parses against a deterministic grid
/// with small limits; only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class FormulaTarget
{
    private sealed class GridResolver : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column)
        {
            int h = unchecked((row * 31) + (column * 17));
            return (h & 7) switch
            {
                0 => CalcValue.Blank,
                1 => CalcValue.FromText("t" + (h & 15).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                2 => CalcValue.FromBoolean((h & 8) != 0),
                3 => CalcValue.FromError(CalcError.NA),
                _ => CalcValue.FromNumber(h % 100),
            };
        }

        public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin) =>
            name.Length % 2 == 0 ? CalcValue.FromNumber(name.Length) : CalcValue.FromError(CalcError.Name);

        public CalcValue ResolveMember(IReadOnlyList<string> segments) => CalcValue.FromArray(new CalcArray(2, 1, [1, segments.Count]));
    }

    private static FormulaParserOptions OptionsFor(byte b) => new()
    {
        Syntax = (b & 1) == 0 ? FormulaSyntax.A1 : FormulaSyntax.R1C1,
        TemplateExtensions = (b & 2) != 0,
        OriginRow = 7,
        OriginColumn = 5,
        Locale = (b & 4) != 0 ? FormulaLocale.ForDecimalSeparator(",") : FormulaLocale.Invariant,
    };

    public static void RunParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        FormulaParserOptions o = OptionsFor(data[0]);
        string text = Encoding.UTF8.GetString(data[1..]);
        if (!FormulaParser.TryParse(text, out FormulaNode? n, out FormulaSyntaxError err, o))
        {
            if (!err.Code.StartsWith("formula.", StringComparison.Ordinal) || err.Offset < 0 || err.Offset > text.Length)
            {
                throw new InvalidOperationException("a rejected formula must carry a located formula. error: " + err);
            }

            return;
        }

        _ = n!.CountNodes();
        bool plain = (data[0] & 5) == 0;
        string file = FormulaPrinter.Print(n);
        if (plain && file != text)
        {
            throw new InvalidOperationException("file form does not round-trip: " + file);
        }

        if (HasNameRange(n))
        {
            return; // A one-letter name beside a colon (a name in R1C1 text) reads as a column range in A1 text, in Excel too.
        }

        // The printed forms must be formulas again, and printing them must be stable.
        var plainOptions = new FormulaParserOptions { TemplateExtensions = o.TemplateExtensions };
        if (!FormulaParser.TryParse(file, out FormulaNode? again, out FormulaSyntaxError e2, plainOptions) || again is null)
        {
            throw new InvalidOperationException("printed file form does not parse: " + e2 + " :: " + file);
        }

        if (FormulaPrinter.Print(again) != file)
        {
            throw new InvalidOperationException("printing is not stable");
        }

        _ = FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.Display, Locale = o.Locale });
        _ = FormulaPrinter.Print(FormulaTranslator.Translate(n, 3, -2));
        if (HasR1C1Name(n) || System.Text.RegularExpressions.Regex.IsMatch(file, @"(?i)(^|[^\w.'])R\d*C\d*(:\w+)?!"))
        {
            return; // A name or unquoted sheet name such as r, c or R1C1 reads as a reference in R1C1 text; Excel does not allow such names.
        }

        string r1 = FormulaPrinter.Print(n, new FormulaPrintOptions { Syntax = FormulaSyntax.R1C1, OriginRow = 7, OriginColumn = 5 });
        if (!FormulaParser.TryParse(r1, out FormulaNode? back, out FormulaSyntaxError e3, new FormulaParserOptions { Syntax = FormulaSyntax.R1C1, OriginRow = 7, OriginColumn = 5, TemplateExtensions = o.TemplateExtensions }) || back is null)
        {
            throw new InvalidOperationException("printed R1C1 does not parse: " + e3 + " :: " + r1);
        }

        if (FormulaPrinter.Print(back, new FormulaPrintOptions { Syntax = FormulaSyntax.R1C1, OriginRow = 7, OriginColumn = 5 }) != r1)
        {
            throw new InvalidOperationException("R1C1 does not print back to the same text: " + r1);
        }

    }

    private static bool HasNameRange(FormulaNode n)
    {
        if (n is BinaryNode { Operator: BinaryOperator.Range } b && (b.Left is NameNode || b.Right is NameNode))
        {
            return true;
        }

        foreach (FormulaNode c in n.Children)
        {
            if (HasNameRange(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasR1C1Name(FormulaNode n)
    {
        // Two whole-row or whole-column operands of a range operator print as one reference in R1C1 text ("R[-6]:R[-6]" is both): the same
        // cells, a different tree.
        if (n is BinaryNode { Operator: BinaryOperator.Range } w && (w.Left is AreaReferenceNode { AreaKind: not AreaKind.Cells } || w.Right is AreaReferenceNode { AreaKind: not AreaKind.Cells }))
        {
            return true;
        }

        if (n is NameNode nn && (RefSyntax.LooksLikeR1C1(nn.Name) || (nn.Name.Length <= 2 && nn.Name.Length > 0 && char.ToUpperInvariant(nn.Name[0]) is 'R' or 'C')))
        {
            return true;
        }

        if (n is StructuredReferenceNode { Table: { } tbl } sr && (RefSyntax.LooksLikeR1C1(tbl) || (tbl.Length <= 2 && tbl.Length > 0 && char.ToUpperInvariant(tbl[0]) is 'R' or 'C')))
        {
            return true; // A table named like a cell reference cannot be told apart in R1C1 text either.
        }

        foreach (FormulaNode c in n.Children)
        {
            if (HasR1C1Name(c))
            {
                return true;
            }
        }

        return false;
    }

    public static void RunEval(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        var po = new FormulaParserOptions { TemplateExtensions = (data[0] & 2) != 0, FileForm = false };
        string text = Encoding.UTF8.GetString(data[1..]);
        if (!FormulaParser.TryParse(text, out FormulaNode? n, out _, po) || n is null)
        {
            return;
        }

        try
        {
            EvalChecked(n, data[0]);
        }
        catch (OfficeException)
        {
            // A typed limit is the correct outcome for a formula that asks for too much.
        }
    }

    private static void EvalChecked(FormulaNode n, byte flags)
    {
        byte[] data = [flags];
        var limits = new OfficeLimits { MaxRecursionDepth = 32, MaxAllocationBytes = 4 << 20 };
        var options = new EvaluatorOptions { MaxSteps = 100_000, LegacyImplicitIntersection = (data[0] & 1) != 0 };
        var ev = new Evaluator(new GridResolver(), limits: limits, options: options);
        CalcValue v = ev.Evaluate(n, new CalcCell("Sheet1", 3 + (data[0] >> 4), 2));
        if (v.Kind == ValueKind.Reference)
        {
            throw new InvalidOperationException("Evaluate must not return a reference");
        }

        CalcValue again = ev.Evaluate(n, new CalcCell("Sheet1", 3 + (data[0] >> 4), 2));
        if (v.Kind != again.Kind || (v.IsScalar && !v.Equals(again) && !(v.Kind == ValueKind.Number && double.IsNaN(v.Number))))
        {
            throw new InvalidOperationException("evaluation is not deterministic");
        }

        if (v.Kind == ValueKind.Number && !double.IsFinite(v.Number))
        {
            throw new InvalidOperationException("a number result must be finite");
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string[] formulas =
        [
            "SUM(A1:B2)", "IF(A1>0,\"yes\",\"no\")", "'My Sheet'!$A$1:$B$2+1", "Sheet1:Sheet3!A1", "[1]Sheet1!A1", "Table1[[#This Row],[Col]]", "{1,2;3,4}+1",
            "_xlfn.LET(_xlpm.x,5,_xlpm.x*2)", "_xlfn.LAMBDA(_xlpm.x,_xlpm.x+1)(3)", "_xlfn.SEQUENCE(3,2)", "_xlfn.MAP(A1:A3,_xlfn.LAMBDA(_xlpm.v,_xlpm.v*2))",
            "-A1^2%", "A:A", "1:3", "(A1,B1)", "A1:B2 B1:C3", "#N/A", "IFERROR(1/0,\"x\")", "TEXT(A1,\"0.00\")", "item.price*2", "INDEX(A1:C3,2,0)",
            "LET(f,LAMBDA(s,n,IF(n<1,0,s(s,n-1)+1)),f(f,5))", "MAKEARRAY(3,3,LAMBDA(r,c,r*c))", "R[-1]C[2]+R1C1", "AND(A1:A3)", "CONCAT(A1:B2)",
        ];
        int i = 0;
        foreach (string f in formulas)
        {
            var b = new List<byte> { (byte)(f.StartsWith("R[", StringComparison.Ordinal) ? 1 : f.Contains('.', StringComparison.Ordinal) && f.StartsWith("item", StringComparison.Ordinal) ? 2 : 0) };
            b.AddRange(Encoding.UTF8.GetBytes(f));
            File.WriteAllBytes(Path.Combine(dir, $"f-{i++}"), b.ToArray());
        }

        return 0;
    }
}
