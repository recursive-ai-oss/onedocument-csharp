using System.Collections.Frozen;

namespace RecursiveAI.OneDocument.Calc.OpenFormula;

/// <summary>
/// The names OpenFormula files use for the calc engine's functions. Most names are the same; the functions that came to Excel after 2007 carry
/// the <c>COM.MICROSOFT.</c> prefix, the pre-2010 statistical functions that were replaced keep a <c>LEGACY.</c> prefix, and a handful are spelt differently.
/// </summary>
/// <remarks>
/// OpenDocument 1.3 Part 4 §6 lists the standard functions and allows implementation-defined ones under a reversed-domain prefix. The prefix
/// table here was settled against the files LibreOffice 26.8 writes for every function the calc engine has (the same names are read back).
/// </remarks>
public static class OpenFormulaFunctions
{
    private static readonly string[] Prefixes = ["COM.MICROSOFT.", "ORG.OPENOFFICE.", "ORG.LIBREOFFICE.", "LEGACY."];

    // Functions a file spells with the COM.MICROSOFT. prefix.
    private static readonly FrozenSet<string> ComMicrosoft = FrozenSet.ToFrozenSet(
        [
            "AGGREGATE", "BETA.DIST", "BETA.INV", "BINOM.DIST", "BINOM.INV", "CEILING", "CEILING.MATH", "CEILING.PRECISE", "CHISQ.DIST", "CHISQ.DIST.RT",
            "CHISQ.INV", "CHISQ.INV.RT", "CHISQ.TEST", "CHOOSECOLS", "CHOOSEROWS", "CONCAT", "CONFIDENCE.NORM", "CONFIDENCE.T", "COVARIANCE.P",
            "COVARIANCE.S", "DROP", "ERF.PRECISE", "ERFC.PRECISE", "EXPAND", "EXPON.DIST", "F.DIST.RT", "F.INV.RT", "F.TEST", "FILTER", "FILTERXML",
            "FLOOR", "FLOOR.MATH", "FLOOR.PRECISE", "FORECAST.ETS", "FORECAST.ETS.CONFINT", "FORECAST.ETS.SEASONALITY", "FORECAST.ETS.STAT",
            "FORECAST.LINEAR", "GAMMA.DIST", "GAMMA.INV", "GAMMALN.PRECISE", "HSTACK", "HYPGEOM.DIST", "IFS", "ISO.CEILING", "LET", "LOGNORM.DIST",
            "LOGNORM.INV", "MAXIFS", "MINIFS", "MODE.MULT", "MODE.SNGL", "NEGBINOM.DIST", "NETWORKDAYS.INTL", "NORM.DIST", "NORM.INV", "NORM.S.DIST",
            "NORM.S.INV", "PERCENTILE.EXC", "PERCENTILE.INC", "PERCENTRANK.EXC", "PERCENTRANK.INC", "POISSON.DIST", "QUARTILE.EXC", "QUARTILE.INC",
            "RANDARRAY", "RANK.AVG", "RANK.EQ", "SEQUENCE", "SORT", "SORTBY", "STDEV.P", "STDEV.S", "SWITCH", "T.DIST", "T.DIST.2T", "T.DIST.RT",
            "T.INV", "T.INV.2T", "T.TEST", "TAKE", "TEXTAFTER", "TEXTBEFORE", "TEXTJOIN", "TEXTSPLIT", "TOCOL", "TOROW", "UNIQUE", "VAR.P", "VAR.S",
            "VSTACK", "WEBSERVICE", "WEIBULL.DIST", "WORKDAY.INTL", "WRAPCOLS", "WRAPROWS", "XLOOKUP", "XMATCH", "Z.TEST",
        ],
        StringComparer.Ordinal);

    // Excel's pre-2010 functions that have a newer replacement: the old one is the LEGACY. name, and the plain name belongs to LibreOffice's own.
    private static readonly FrozenSet<string> Legacy = FrozenSet.ToFrozenSet(
        ["CHIDIST", "CHIINV", "CHITEST", "FDIST", "FINV", "NORMSDIST", "NORMSINV", "TDIST"], StringComparer.Ordinal);

    // Excel name -> OpenFormula name where the two are simply spelt differently.
    private static readonly FrozenDictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["F.DIST"] = "FDIST",
        ["F.INV"] = "FINV",
        ["SKEW.P"] = "SKEWP",
        ["FORMULATEXT"] = "FORMULA",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> RenamedBack = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FDIST"] = "F.DIST",
        ["FINV"] = "F.INV",
        ["SKEWP"] = "SKEW.P",
        ["FORMULA"] = "FORMULATEXT",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Functions the calc engine has and LibreOffice (the reference reader) does not know: they are written, but flagged as not portable.
    private static readonly FrozenSet<string> NotInOpenFormula = FrozenSet.ToFrozenSet(
        [
            "ANCHORARRAY", "ARRAYTOTEXT", "BAHTTEXT", "BINOM.DIST.RANGE", "BYCOL", "BYROW", "DBCS", "DETECTLANGUAGE", "GROUPBY", "IMCOSH", "IMCOT",
            "IMCSC", "IMCSCH", "IMSEC", "IMSECH", "IMSINH", "IMTAN", "ISOMITTED", "LAMBDA", "MAKEARRAY", "MAP", "PERCENTOF", "PHONETIC", "PIVOTBY",
            "REDUCE", "REGEXEXTRACT", "REGEXREPLACE", "REGEXTEST", "SCAN", "SINGLE", "TRANSLATE", "TRIMRANGE", "VALUETOTEXT",
        ],
        StringComparer.Ordinal);

    /// <summary>Maps a function name as an OpenFormula file spells it to the calc engine's name.</summary>
    /// <param name="fileName">The name as written, with any <c>COM.MICROSOFT.</c>, <c>ORG.OPENOFFICE.</c>, <c>ORG.LIBREOFFICE.</c> or <c>LEGACY.</c> prefix, in any case.</param>
    /// <returns>The upper-case name without prefix; <c>FDIST</c>, <c>FINV</c>, <c>SKEWP</c> and <c>FORMULA</c> read as <c>F.DIST</c>, <c>F.INV</c>, <c>SKEW.P</c> and <c>FORMULATEXT</c> unless <c>LEGACY.</c> marks the old Excel function.</returns>
    public static string ToCalcName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string name = fileName.ToUpperInvariant();
        bool legacy = false;
        foreach (string p in Prefixes)
        {
            if (name.StartsWith(p, StringComparison.Ordinal))
            {
                legacy = p == "LEGACY.";
                name = name[p.Length..];
                break;
            }
        }

        if (!legacy && RenamedBack.TryGetValue(name, out string? back))
        {
            return back;
        }

        return name;
    }

    /// <summary>Maps a calc-engine function name to the name an OpenFormula file uses.</summary>
    /// <param name="calcName">The function name without <c>_xlfn.</c> prefixes, in any case.</param>
    /// <param name="portable">False when LibreOffice, the reference reader, does not know the function, so another reader shows <c>#NAME?</c> after recalculating.</param>
    /// <returns>The name to write, in upper case.</returns>
    public static string ToFileName(string calcName, out bool portable)
    {
        ArgumentNullException.ThrowIfNull(calcName);
        string name = calcName.ToUpperInvariant();
        portable = !NotInOpenFormula.Contains(name);
        if (Renamed.TryGetValue(name, out string? renamed))
        {
            return renamed;
        }

        if (Legacy.Contains(name))
        {
            return "LEGACY." + name;
        }

        return ComMicrosoft.Contains(name) ? "COM.MICROSOFT." + name : name;
    }
}
