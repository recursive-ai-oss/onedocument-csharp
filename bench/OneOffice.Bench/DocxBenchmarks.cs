using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using RecursiveAI.OneOffice.Bench.Inputs;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>Shared setup: loads one generated input, creating it on first use.</summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public abstract class DocxBenchmarkBase
{
    /// <summary>Which generated input to run on (small, medium, large, pages1000).</summary>
    [Params("small", "medium", "large", "pages1000")]
    public string Input { get; set; } = "small";

    /// <summary>The input's bytes.</summary>
    protected byte[] Bytes { get; private set; } = [];

    /// <summary>Loads the input; the directory comes from <c>ONEOFFICE_BENCH_INPUTS</c> (default: next to the build output).</summary>
    [GlobalSetup]
    public void LoadInput()
    {
        string dir = Environment.GetEnvironmentVariable("ONEOFFICE_BENCH_INPUTS")
            ?? Path.Combine(AppContext.BaseDirectory, "inputs");
        Bytes = File.ReadAllBytes(DocxGenerator.Ensure(dir, [Input])[Input]);
    }
}

/// <summary>Open a package and save it, untouched.</summary>
public class DocxLoadSave : DocxBenchmarkBase
{
    /// <summary>The floor: every entry read and rewritten through the BCL zip classes.</summary>
    [Benchmark(Baseline = true)]
    public long RawZip() => Rows.RawZipLoadSave(Bytes);

    /// <summary>The SDK: open and clone.</summary>
    [Benchmark]
    public long OpenXml() => Rows.OpenXmlLoadSave(Bytes);

    /// <summary>The XWPF model: load and write.</summary>
    [Benchmark]
    public long Npoi() => Rows.NpoiLoadSave(Bytes);
}

/// <summary>Change one paragraph's text and save.</summary>
public class DocxEdit : DocxBenchmarkBase
{
    /// <summary>The floor: rewrite one part, copy the rest.</summary>
    [Benchmark(Baseline = true)]
    public long RawZip() => Rows.RawZipEdit(Bytes);

    /// <summary>The SDK.</summary>
    [Benchmark]
    public long OpenXml() => Rows.OpenXmlEdit(Bytes);

    /// <summary>The XWPF model.</summary>
    [Benchmark]
    public long Npoi() => Rows.NpoiEdit(Bytes);
}

/// <summary>Open a document and read one small part.</summary>
public class DocxLazyOpen : DocxBenchmarkBase
{
    /// <summary>The floor: open the zip, read one entry.</summary>
    [Benchmark(Baseline = true)]
    public long RawZip() => Rows.RawZipLazyOpen(Bytes);

    /// <summary>The SDK.</summary>
    [Benchmark]
    public long OpenXml() => Rows.OpenXmlLazyOpen(Bytes);

    /// <summary>The XWPF model, which loads everything.</summary>
    [Benchmark]
    public long Npoi() => Rows.NpoiLazyOpen(Bytes);
}

/// <summary>Pull the text out of the body.</summary>
public class DocxExtract : DocxBenchmarkBase
{
    /// <summary>The SDK through its DOM.</summary>
    [Benchmark(Baseline = true)]
    public long OpenXmlDom() => Rows.OpenXmlExtractDom(Bytes);

    /// <summary>The SDK through its forward reader.</summary>
    [Benchmark]
    public long OpenXmlReader() => Rows.OpenXmlExtractReader(Bytes);

    /// <summary>The XWPF extractor.</summary>
    [Benchmark]
    public long Npoi() => Rows.NpoiExtract(Bytes);

    /// <summary>OneOffice's streaming extractor.</summary>
    [Benchmark]
    public long OneOffice() => Rows.OneOfficeExtract(Bytes);
}
