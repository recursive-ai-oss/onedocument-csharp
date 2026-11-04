using BenchmarkDotNet.Running;

namespace RecursiveAI.OneOffice.Bench;

internal static class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
