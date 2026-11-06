// Extract the text of a Word 97-2003 file: one line.
using RecursiveAI.OneDocument;

// The task.
string text = Office.ExtractText(Path.Combine(AppContext.BaseDirectory, "basic.doc"));

if (string.IsNullOrWhiteSpace(text))
{
    throw new InvalidOperationException("No text came out of the .doc file.");
}

Console.WriteLine(text.Length + " characters; first line: " + text.Split('\n')[0]);
