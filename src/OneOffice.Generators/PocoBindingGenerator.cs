using Microsoft.CodeAnalysis;

namespace RecursiveAI.OneOffice.Generators;

/// <summary>
/// Generates, at compile time, the code that reads and writes plain classes as spreadsheet rows and as template data, so that
/// binding never uses reflection (which NativeAOT and trimming cannot keep). It is a skeleton: the spreadsheet and template work
/// that needs it fills in <see cref="Initialize"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PocoBindingGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Nothing to generate yet.
    }
}
