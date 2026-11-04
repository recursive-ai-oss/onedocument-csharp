using System;

namespace RecursiveAI.OneOffice.Tools.ViewGenerator;

/// <summary>
/// Will read the pinned OOXML schemas and write the typed-view source that is committed under src/OneOffice.Schema/Generated.
/// The output is a pure function of the pinned schemas, so running it twice changes nothing. Not implemented yet.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("ViewGenerator: not implemented yet.");
        return 2;
    }
}
