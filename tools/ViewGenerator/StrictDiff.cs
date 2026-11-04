using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RecursiveAI.OneOffice.Tools.ViewGenerator;

/// <summary>Compares the names a Strict complex type declares with the Transitional one, for the generation notes.</summary>
internal static class StrictDiff
{
    public static IEnumerable<string> Report(Library lib, Semantics sem)
    {
        var lines = new List<string>();
        foreach (var (q, strict) in lib.StrictComplexTypes)
        {
            if (!lib.ComplexTypes.TryGetValue(q, out var trans)) { lines.Add(q.Local + ": only in Strict"); continue; }
            var tn = Names(trans);
            var sn = Names(strict);
            var onlyT = tn.Except(sn).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var onlyS = sn.Except(tn).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (onlyT.Count == 0 && onlyS.Count == 0) continue;
            lines.Add(q.Local + ": only Transitional [" + string.Join(" ", onlyT) + "] only Strict [" + string.Join(" ", onlyS) + "]");
        }
        foreach (var q in lib.ComplexTypes.Keys.Where(k => lib.StrictToTransitional.ContainsValue(k.Ns) && !lib.StrictComplexTypes.ContainsKey(k)))
            lines.Add(q.Local + ": only in Transitional");
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static HashSet<string> Names(XElement type)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in type.Descendants())
        {
            var ln = e.Name.LocalName;
            if (ln is "element" or "attribute" or "group" or "attributeGroup")
            {
                var n = e.Attribute("name")?.Value ?? e.Attribute("ref")?.Value;
                if (n is not null) set.Add((ln[0] == 'e' ? "e:" : ln[0] == 'a' && ln.Length == 9 ? "@" : "g:") + n[(n.IndexOf(':') + 1)..]);
            }
        }
        return set;
    }
}
