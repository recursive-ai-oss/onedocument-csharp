using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The financial, engineering and database function families. The input chooses one function and its arguments (numbers, text from a pool of
/// plausible operands, ranges over a small database, booleans, omitted arguments); the call is evaluated with small limits. Only a typed
/// <see cref="OfficeException"/> may escape; a number result must be finite and never a reference, and a second run gives the same answer.
/// </summary>
internal static class FinFuncsTarget
{
    private const string Names =
        "FV PV PMT NPER RATE IPMT PPMT ISPMT CUMIPMT CUMPRINC EFFECT NOMINAL RRI PDURATION FVSCHEDULE NPV XNPV IRR XIRR MIRR DOLLARDE DOLLARFR " +
        "SLN SYD DB DDB VDB AMORLINC AMORDEGRC COUPDAYBS COUPDAYS COUPDAYSNC COUPNCD COUPNUM COUPPCD PRICE YIELD DURATION MDURATION ACCRINT ACCRINTM " +
        "DISC INTRATE RECEIVED PRICEDISC YIELDDISC PRICEMAT YIELDMAT TBILLPRICE TBILLYIELD TBILLEQ ODDFPRICE ODDFYIELD ODDLPRICE ODDLYIELD " +
        "BIN2DEC BIN2OCT BIN2HEX OCT2DEC OCT2BIN OCT2HEX DEC2BIN DEC2OCT DEC2HEX HEX2BIN HEX2DEC HEX2OCT BITAND BITOR BITXOR BITLSHIFT BITRSHIFT " +
        "DELTA GESTEP ERF ERF.PRECISE ERFC ERFC.PRECISE BESSELI BESSELJ BESSELK BESSELY CONVERT COMPLEX IMREAL IMAGINARY IMABS IMARGUMENT IMCONJUGATE " +
        "IMSIN IMCOS IMTAN IMCOT IMSEC IMCSC IMSINH IMCOSH IMSECH IMCSCH IMEXP IMLN IMLOG10 IMLOG2 IMSQRT IMPOWER IMDIV IMSUB IMSUM IMPRODUCT " +
        "DSUM DAVERAGE DCOUNT DCOUNTA DGET DMAX DMIN DPRODUCT DSTDEV DSTDEVP DVAR DVARP";

    private static readonly string[] Functions = Names.Split(' ');

    private static readonly string[] Texts =
    [
        "1010", "FF", "7777777777", "3+4i", "-j", "5", "kg", "lbm", "m", "ft", "C", "F", "K", "km", "Tree", "Height", "Yield", "=Apple", ">10", "A", "i", "1e-5+2j",
    ];

    private static readonly string[] Ranges = ["A1:E5", "A1:A5", "B1:B5", "C1:C2", "A1:B2", "A1:E1", "D1:E3"];

    private sealed class Grid : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column)
        {
            if (row == 1)
            {
                return CalcValue.FromText(column switch { 1 => "Tree", 2 => "Height", 3 => "Age", 4 => "Yield", _ => "Profit" });
            }

            int h = unchecked((row * 31) + (column * 17));
            return (h & 7) switch
            {
                0 => CalcValue.Blank,
                1 => CalcValue.FromText(column == 1 ? "Apple" : "t" + (h & 15).ToString(CultureInfo.InvariantCulture)),
                2 => CalcValue.FromBoolean((h & 8) != 0),
                3 => CalcValue.FromError(CalcError.NA),
                _ => CalcValue.FromNumber((h % 100) - 20),
            };
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        string name = Functions[data[0] % Functions.Length];
        int count = data[1] % 9;
        var sb = new StringBuilder(name).Append('(');
        int p = 2;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            byte sel = p < data.Length ? data[p++] : (byte)0;
            switch (sel % 6)
            {
                case 0:
                    {
                        double d = 0;
                        if (p + 8 <= data.Length)
                        {
                            d = BitConverter.ToDouble(data.Slice(p, 8));
                            p += 8;
                        }

                        if (!double.IsFinite(d))
                        {
                            d = 1;
                        }

                        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    }

                case 1:
                    sb.Append((p < data.Length ? (sbyte)data[p++] : 0).ToString(CultureInfo.InvariantCulture));
                    break;
                case 2:
                    sb.Append('"').Append(Texts[(p < data.Length ? data[p++] : 0) % Texts.Length]).Append('"');
                    break;
                case 3:
                    sb.Append(Ranges[(p < data.Length ? data[p++] : 0) % Ranges.Length]);
                    break;
                case 4:
                    sb.Append((sel & 8) != 0 ? "TRUE" : "FALSE");
                    break;
                default:
                    break; // omitted
            }
        }

        sb.Append(')');
        string formula = sb.ToString();
        if (!FormulaParser.TryParse(formula, out FormulaNode? n, out _, new FormulaParserOptions { FileForm = false }) || n is null)
        {
            return;
        }

        try
        {
            var limits = new OfficeLimits { MaxRecursionDepth = 32, MaxAllocationBytes = 8 << 20 };
            var ev = new Evaluator(new Grid(), limits: limits, options: new EvaluatorOptions { MaxSteps = 200_000 });
            CalcValue v = ev.Evaluate(n, new CalcCell("Sheet1", 20, 20));
            CalcValue again = ev.Evaluate(n, new CalcCell("Sheet1", 20, 20));
            if (v.Kind == ValueKind.Reference)
            {
                throw new InvalidOperationException("Evaluate must not return a reference: " + formula);
            }

            if (v.Kind == ValueKind.Number && !double.IsFinite(v.Number))
            {
                throw new InvalidOperationException("a number result must be finite: " + formula);
            }

            if (v.IsScalar && !v.Equals(again))
            {
                throw new InvalidOperationException("evaluation is not deterministic: " + formula);
            }
        }
        catch (OfficeException)
        {
            // A typed limit is the right outcome for a call that asks for too much.
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string[] calls = ["PMT(0.05,10,1000)", "PRICE(40000,41000,0.05,0.06,100,2,0)", "DEC2BIN(9,4)", "BESSELJ(2,1)", "CONVERT(1,\"km\",\"m\")", "IMSUM(\"3+4i\",\"1-i\")", "DSUM(A1:E5,\"Yield\",A1:B2)", "XIRR(A1:A5,B1:B5)"];
        int i = 0;
        foreach (string call in calls)
        {
            string fn = call[..call.IndexOf('(', StringComparison.Ordinal)];
            int index = Array.IndexOf(Functions, fn);
            var b = new List<byte> { (byte)Math.Max(index, 0), 3 };
            b.AddRange([0, .. BitConverter.GetBytes(0.05), 1, 10, 1, 100]);
            File.WriteAllBytes(Path.Combine(dir, $"f-{i++}"), b.ToArray());
        }

        return 0;
    }
}
