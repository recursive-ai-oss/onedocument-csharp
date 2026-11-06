namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>The function numbers of formulas ([MS-XLS] §2.5.198.17 Ftab) and how many arguments a fixed-count call carries.</summary>
internal static partial class FunctionTable
{
    private static Dictionary<string, int>? _byName;

    private static Dictionary<string, int> ByName => _byName ??= BuildIndex();

    /// <summary>The function name for a function number, or null when the number is not assigned.</summary>
    public static string? Name(int tab) => (uint)tab < (uint)Entries.Length ? Entries[tab].Name : null;

    /// <summary>The argument count of a function that is always written with <c>PtgFunc</c>, or -1 when the count varies.</summary>
    public static int FixedArgs(int tab) => (uint)tab < (uint)Entries.Length ? Entries[tab].Fixed : -1;

    /// <summary>The function number for a name, or -1.</summary>
    public static int IndexOf(string name) => ByName.TryGetValue(name, out int i) ? i : -1;

    private static Dictionary<string, int> BuildIndex()
    {
        var d = new Dictionary<string, int>(Entries.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Entries.Length; i++)
        {
            if (Entries[i].Name is { } n && i != 255 && !d.ContainsKey(n))
            {
                d[n] = i;
            }
        }

        return d;
    }
}
