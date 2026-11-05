using System;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Random calls of the statistical and date and time functions. The input picks a function and, byte by byte, its arguments (numbers, text, booleans, errors,
/// array constants, ranges of a deterministic grid). Only a typed <see cref="OfficeException"/> may escape; a result is never a reference, a number is
/// finite, and evaluating twice gives the same value.
/// </summary>
internal static class StatFuncsTarget
{
    private static readonly string[] Names = BuildNames();

    private static readonly string[] Atoms =
    [
        "0", "1", "2", "3", "0.5", "-1", "7", "10", "100", "1E+300", "-1E+300", "0.0001", "99999", "2958465", "40000", "\"x\"", "\"2020-02-29\"", "\"1:30 PM\"",
        "\"0000011\"", "\"1111111\"", "\"Y\"", "\">2\"", "TRUE", "FALSE", "#N/A", "#DIV/0!", "{1,2,3,4,5}", "{5;3;1;4;2}", "{1,2;3,4}", "{1,\"a\",TRUE,#N/A}",
        "A1:A8", "B1:C6", "A1:D4", "C1:C9", "A1", "{0.2,0.3,0.1,0.4}", "{10,20,30}", "{41235,41247}",
    ];

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2024, 3, 9, 15, 30, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Grid : ICalcResolver
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
    }

    private static string[] BuildNames()
    {
        var r = FunctionRegistry.CreateDefault();
        return [.. r.Definitions.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal)];
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        string name = Names[data[0] % Names.Length];
        int argc = data[1] % 8;
        var sb = new StringBuilder(name).Append('(');
        for (int i = 0; i < argc; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            byte b = 2 + i < data.Length ? data[2 + i] : (byte)0;
            sb.Append(Atoms[b % Atoms.Length]);
        }

        sb.Append(')');
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_FUZZ_TRACE") is not null)
        {
            Console.Error.WriteLine(sb.ToString());
        }
        if (!FormulaParser.TryParse(sb.ToString(), out FormulaNode? n, out _, new FormulaParserOptions { FileForm = false }) || n is null)
        {
            return;
        }

        var limits = new OfficeLimits { MaxRecursionDepth = 32, MaxAllocationBytes = 4 << 20 };
        var options = new EvaluatorOptions
        {
            MaxSteps = 200_000,
            Clock = new Clock(),
            DateSystem = (data[1] & 0x80) != 0 ? NumberFormats.DateSystem.Date1904 : NumberFormats.DateSystem.Date1900,
        };
        var ev = new Evaluator(new Grid(), limits: limits, options: options);
        try
        {
            CalcValue v = ev.Evaluate(n, new CalcCell("Sheet1", 3, 2));
            if (v.Kind == ValueKind.Reference || (v.Kind == ValueKind.Number && !double.IsFinite(v.Number)))
            {
                throw new InvalidOperationException("bad result from " + sb);
            }

            CalcValue again = ev.Evaluate(n, new CalcCell("Sheet1", 3, 2));
            if (v.Kind != again.Kind || (v.IsScalar && !v.Equals(again)))
            {
                throw new InvalidOperationException("not deterministic: " + sb);
            }
        }
        catch (OfficeException)
        {
            // A typed limit is the correct outcome for a call that asks for too much.
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 24; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, "seed" + i), [(byte)(i * 17), 3, (byte)(26 + i), (byte)(31 + i), (byte)(i * 3), 5]);
        }

        return 0;
    }
}
