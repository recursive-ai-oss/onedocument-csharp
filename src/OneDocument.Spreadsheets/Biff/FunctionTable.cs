using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// The function numbers of BIFF8 formulas ([MS-XLS] §2.5.198.17 Ftab). The table is the one the binary workbook codec uses
/// (<see cref="XlsbFunctionTable"/>; the two specifications list the same numbers); BIFF8 stops at 379, the later numbers are
/// the newer functions that BIFF8 files store as <c>_xlfn.</c> names.
/// </summary>
internal static class FunctionTable
{
    // [MS-XLS] §2.5.198.17: the last function number a BIFF8 file may use.
    private const int Limit = 380;

    /// <summary>The function name for a function number, or null when the number is not assigned.</summary>
    public static string? Name(int tab) => tab is >= 0 and < Limit && tab != 255 && XlsbFunctionTable.TryGet(tab, out string name, out _, out _) ? name : null;

    /// <summary>The argument count of a function that is always written with <c>PtgFunc</c>, or -1 when the count varies.</summary>
    public static int FixedArgs(int tab) => tab is >= 0 and < Limit && XlsbFunctionTable.TryGet(tab, out _, out int min, out int max) && min == max ? min : -1;

    /// <summary>The function number for a name, or -1.</summary>
    public static int IndexOf(string name) => XlsbFunctionTable.TryGetId(name, out int id) && id < Limit ? id : -1;
}
