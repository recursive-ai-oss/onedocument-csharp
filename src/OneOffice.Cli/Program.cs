using System;
using System.IO;
using RecursiveAI.OneOffice;

namespace RecursiveAI.OneOffice.Cli;

/// <summary>The <c>oneoffice</c> tool. The commands arrive with the features they front; for now it can name a file's format.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "detect")
        {
            try
            {
                Console.WriteLine(Office.Detect(args[1]));
                return 0;
            }
            catch (Exception ex) when (ex is OfficeException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Console.Error.WriteLine("usage: oneoffice detect <file>");
        return 2;
    }
}
