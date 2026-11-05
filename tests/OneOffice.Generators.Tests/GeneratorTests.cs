using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RecursiveAI.OneOffice.Generators.Tests;

public class GeneratorTests
{
    private const string Header = "using System; using System.Collections.Generic; using RecursiveAI.OneOffice.Spreadsheets.Binding;\n";

    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences()
    {
        string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return [.. tpa.Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    }

    private static (ImmutableArray<Diagnostic> Generator, ImmutableArray<Diagnostic> Compile, string[] Sources) Run(string code)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + code, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PocoBindingGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string[] sources = [.. result.GeneratedTrees.Select(t => t.GetText().ToString())];
        return (generatorDiagnostics, output.GetDiagnostics(), sources);
    }

    private static string[] Ids(ImmutableArray<Diagnostic> d) => [.. d.Select(x => x.Id).Where(i => i.StartsWith("OOBIND", StringComparison.Ordinal))];

    private static void NoErrors(ImmutableArray<Diagnostic> compile) =>
        Assert.Empty(compile.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage() + " @" + d.Location.GetLineSpan().StartLinePosition));

    [Fact]
    public void GeneratedCodeCompilesForEveryShapeOfType()
    {
        (ImmutableArray<Diagnostic> gen, ImmutableArray<Diagnostic> compile, string[] sources) = Run(@"
namespace N
{
    public enum E : byte { A = 1, B = 2, Alias = 2 }
    public enum U : ulong { Big = 18446744073709551615 }

    [SpreadsheetRow] public partial class C { public int I { get; set; } public E? Mode { get; set; } public U Huge; public string? S; public string T { get; init; } = """"; public decimal? D; public Guid G; public DateTimeOffset O; }
    [SpreadsheetRow(Sheet = ""A \""quoted\"" sheet"", Strict = true)] public partial record R(int X, string Y);
    [SpreadsheetRow] public partial record struct RS(double A, DateOnly B);
    [SpreadsheetRow] public readonly partial struct RO { public int V { get; init; } }
    public partial class Outer { [SpreadsheetRow] public partial struct Inner { public TimeOnly T { get; set; } } }
}
[SpreadsheetRow] public partial class Global { public bool B { get; set; } public float F { get; set; } public sbyte Sb { get; set; } public ushort Us { get; set; } }
");
        Assert.Empty(Ids(gen));
        NoErrors(compile);
        Assert.Equal(6, sources.Length);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        const string Code = "[SpreadsheetRow] public partial class C { public int I { get; set; } public string? S { get; set; } }";
        Assert.Equal(Run(Code).Sources, Run(Code).Sources);
    }

    [Fact]
    public void UnsupportedMemberIsAWarningAndLeftOut()
    {
        (ImmutableArray<Diagnostic> gen, ImmutableArray<Diagnostic> compile, string[] sources) = Run(
            "[SpreadsheetRow] public partial class C { public int I { get; set; } public List<int> L { get; set; } = new(); [SpreadsheetColumn(Ignore = true)] public object? O { get; set; } }");
        Diagnostic d = Assert.Single(gen);
        Assert.Equal("OOBIND001", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("'L'", d.GetMessage());
        NoErrors(compile);
        Assert.DoesNotContain("\"L\"", Assert.Single(sources));
    }

    [Fact]
    public void ComputedGetOnlyPropertiesAreQuietlySkipped()
    {
        (ImmutableArray<Diagnostic> gen, _, string[] sources) = Run(
            "[SpreadsheetRow] public partial class C { public int I { get; set; } public List<int> Sizes => new(); public int Twice => I * 2; }");
        Assert.Empty(gen);
        Assert.DoesNotContain("Twice", Assert.Single(sources));
    }

    [Fact]
    public void NonPartialTypeIsAnError()
    {
        (ImmutableArray<Diagnostic> gen, _, string[] sources) = Run("[SpreadsheetRow] public class C { public int I { get; set; } }");
        Assert.Equal(["OOBIND002"], Ids(gen));
        Assert.Equal(DiagnosticSeverity.Error, gen[0].Severity);
        Assert.Empty(sources);
        (gen, _, sources) = Run("public class Outer { [SpreadsheetRow] public partial class C { public int I { get; set; } } }");
        Assert.Equal(["OOBIND002"], Ids(gen));
    }

    [Fact]
    public void ConstructorParameterWithoutAMemberIsAnError()
    {
        (ImmutableArray<Diagnostic> gen, _, string[] sources) = Run("[SpreadsheetRow] public partial class C { public C(int missing) { } public int I { get; set; } }");
        Assert.Equal(["OOBIND003"], Ids(gen));
        Assert.Empty(sources);
    }

    [Fact]
    public void DuplicateColumnNamesWarn()
    {
        (ImmutableArray<Diagnostic> gen, ImmutableArray<Diagnostic> compile, _) = Run(
            "[SpreadsheetRow] public partial class C { public int Name { get; set; } [SpreadsheetColumn(\"name\")] public int Other { get; set; } }");
        Assert.Equal(["OOBIND004"], Ids(gen));
        NoErrors(compile);
    }

    [Fact]
    public void GenericTypesAreAnError()
    {
        (ImmutableArray<Diagnostic> gen, _, string[] sources) = Run("[SpreadsheetRow] public partial class C<T> { public int I { get; set; } }");
        Assert.Equal(["OOBIND005"], Ids(gen));
        Assert.Empty(sources);
    }

    [Fact]
    public void IndexOutsideTheSheetWarnsAndIsIgnored()
    {
        (ImmutableArray<Diagnostic> gen, _, string[] sources) = Run("[SpreadsheetRow] public partial class C { [SpreadsheetColumn(Index = 20000)] public int I { get; set; } }");
        Assert.Equal(["OOBIND006"], Ids(gen));
        Assert.Contains(", 0, null, true, true)", Assert.Single(sources));
    }

    [Fact]
    public void TypeWithNoColumnsWarns()
    {
        (ImmutableArray<Diagnostic> gen, ImmutableArray<Diagnostic> compile, _) = Run("[SpreadsheetRow] public partial class C { }");
        Assert.Equal(["OOBIND007"], Ids(gen));
        NoErrors(compile);
    }

    [Fact]
    public void OrderPlacesMarkedColumnsFirst()
    {
        (_, _, string[] sources) = Run(
            "[SpreadsheetRow] public partial class C { public int A { get; set; } [SpreadsheetColumn(Order = 2)] public int B { get; set; } [SpreadsheetColumn(Order = 1)] public int C2 { get; set; } }");
        string s = Assert.Single(sources);
        Assert.True(s.IndexOf("\"C2\"", StringComparison.Ordinal) < s.IndexOf("\"B\"", StringComparison.Ordinal));
        Assert.True(s.IndexOf("\"B\"", StringComparison.Ordinal) < s.IndexOf("\"A\"", StringComparison.Ordinal));
    }
}
