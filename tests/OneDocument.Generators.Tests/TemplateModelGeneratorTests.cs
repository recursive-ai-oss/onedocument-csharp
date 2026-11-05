using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RecursiveAI.OneDocument.Generators.Tests;

public class TemplateModelGeneratorTests
{
    private const string Header = "using System; using System.Collections.Generic; using RecursiveAI.OneDocument.Templating;\n";

    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];

    private static (string[] GeneratorIds, string[] CompileErrors, string[] Sources) Run(string code)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Header + code, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TemplateModelGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        return (
            [.. generatorDiagnostics.Select(d => d.Id)],
            [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage())],
            [.. driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString())]);
    }

    [Fact]
    public void Every_shape_of_type_generates_code_that_compiles()
    {
        (string[] ids, string[] errors, string[] sources) = Run("""
            namespace Shop {
              [TemplateModel] public partial class A { public string Name { get; set; } = ""; public int @class { get; set; } public int this[int i] => i; public static int S => 1; public string W { set { } } }
              [TemplateModel] public partial struct B { public int X { get; set; } }
              [TemplateModel] public partial record C(string P, int Q);
              [TemplateModel] public partial record struct D(string P);
              public partial class Outer { [TemplateModel] public partial class Inner : Base { public int Own { get; set; } } }
              public class Base { public int Inherited { get; set; } }
            }
            """);
        Assert.Empty(ids);
        Assert.Empty(errors);
        Assert.Equal(5, sources.Length);
        Assert.Contains(sources, s => s.Contains("\"Inherited\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, s => s.Contains("\"S\"", StringComparison.Ordinal) || s.Contains("\"W\"", StringComparison.Ordinal) || s.Contains("\"Item\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_model_that_is_not_partial_or_is_generic_is_an_error()
    {
        Assert.Equal(["OOTPL001"], Run("[TemplateModel] public class A { public int X { get; set; } }").GeneratorIds);
        Assert.Equal(["OOTPL001"], Run("public class O { [TemplateModel] public partial class A { } }").GeneratorIds);
        Assert.Equal(["OOTPL002"], Run("[TemplateModel] public partial class G<T> { public T X { get; set; } = default!; }").GeneratorIds);
    }
}
