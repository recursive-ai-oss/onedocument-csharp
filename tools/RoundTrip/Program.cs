using System;

namespace RecursiveAI.OneOffice.Tools.RoundTrip;

/// <summary>Will reopen documents and compare every part byte for byte (per-part SHA-256) before and after a save. Not implemented yet.</summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("RoundTrip: not implemented yet.");
        return 2;
    }
}
