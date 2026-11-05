using System;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// <c>WEBSERVICE</c> and <c>FILTERXML</c>. Neither reaches the network or anything else by itself: the answers come from the resolver the host
/// supplies (<see cref="ICalcResolver.ResolveWebService"/>, <see cref="ICalcResolver.ResolveFilterXml"/>), which denies by default.
/// </summary>
internal static class WebFunctions
{
    // Microsoft's WEBSERVICE documentation: a URL over 2048 characters, or a response over 32767, gives #VALUE!.
    private const int MaxUrl = 2048;
    private const int MaxResponse = 32767;

    private static readonly ArgKind[] V = [ArgKind.Value];

    public static void Register(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("WEBSERVICE", 1, 1, V, WebService));
        r.Add(FunctionDefinition.Create("FILTERXML", 2, 2, [ArgKind.Value, ArgKind.Value], FilterXml));
    }

    private static bool TryText(in CalcValue v, out string text, out CalcValue error)
    {
        error = default;
        switch (v.Kind)
        {
            case ValueKind.Text:
                text = v.Text;
                return true;
            case ValueKind.Number:
                text = Fx.Inv(v.Number);
                return true;
            case ValueKind.Boolean:
                text = v.Boolean ? "TRUE" : "FALSE";
                return true;
            default:
                text = string.Empty;
                error = v.IsError ? v : Fx.Err(CalcError.Value);
                return false;
        }
    }

    private static CalcValue WebService(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!TryText(a[0], out string url, out CalcValue e))
        {
            return e;
        }

        // Only http and https are served; ftp:// and file:// give #VALUE! (Microsoft's WEBSERVICE documentation).
        if (url.Length == 0 || url.Length > MaxUrl
            || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return Fx.Err(CalcError.Value);
        }

        CalcValue answer = ev.Resolver.ResolveWebService(url);
        return answer.Kind == ValueKind.Text && answer.Text.Length > MaxResponse ? Fx.Err(CalcError.Value) : answer;
    }

    private static CalcValue FilterXml(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!TryText(a[0], out string xml, out CalcValue e) || !TryText(a[1], out string xpath, out e))
        {
            return e;
        }

        return xml.Length == 0 || xpath.Length == 0 ? Fx.Err(CalcError.Value) : ev.Resolver.ResolveFilterXml(xml, xpath);
    }
}
