using System.Reflection;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using NPOI.XWPF.UserModel;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>The versions of everything a result depends on, read from the loaded assemblies so a table can never name a version it did not run.</summary>
internal static class Versions
{
    /// <summary>One line per library: name and informational version, plus the runtime.</summary>
    public static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("runtime\t").Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription).Append('\n');
        sb.Append("OneDocument\t").Append(Of(typeof(Office))).Append('\n');
        sb.Append("DocumentFormat.OpenXml\t").Append(Of(typeof(WordprocessingDocument))).Append('\n');
        sb.Append("NPOI\t").Append(Of(typeof(XWPFDocument))).Append('\n');
        sb.Append("ClosedXML\t").Append(Of(typeof(ClosedXML.Excel.XLWorkbook))).Append('\n');
        sb.Append("MiniExcel\t").Append(Of(typeof(MiniExcelLibs.MiniExcel))).Append('\n');
        sb.Append("Sylvan.Data.Excel\t").Append(Of(typeof(Sylvan.Data.Excel.ExcelDataReader))).Append('\n');
        sb.Append("ExcelDataReader\t").Append(Of(typeof(ExcelDataReader.ExcelReaderFactory))).Append('\n');
        sb.Append("EPPlus\t").Append(Of(typeof(OfficeOpenXml.ExcelPackage))).Append('\n');
        sb.Append("BenchmarkDotNet\t").Append(Of(typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute))).Append('\n');
        return sb.ToString();
    }

    private static string Of(Type t)
    {
        string? v = t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        int plus = v?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus > 0 ? v![..plus] : v ?? t.Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
