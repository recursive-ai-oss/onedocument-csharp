using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// Which function names a file stores with a prefix. Functions added after the first edition of the standard are written <c>_xlfn.NAME</c>;
/// the two that Excel treats as worksheet-only (<c>FILTER</c>, <c>SORT</c>) are written <c>_xlfn._xlws.NAME</c>. The set is the future-function
/// list of the Excel extensions to the file format (the <c>future-function-list</c> and <c>worksheet-only-function-list</c> rules of its Formulas
/// grammar), corrected by what Excel 16.0 build 20527 actually stored when each name was entered and the file saved: names the grammar lists
/// that Excel stores bare (<c>NETWORKDAYS.INTL</c>, <c>WORKDAY.INTL</c>, <c>ISO.CEILING</c>, <c>ECMA.CEILING</c>) are left out, and names missing
/// from the grammar that Excel prefixes (<c>IFS</c>, <c>CONCAT</c>, <c>TEXTJOIN</c>, <c>SWITCH</c>, <c>XLOOKUP</c>, <c>XMATCH</c>, ...) are in.
/// </summary>
public static class FutureFunctions
{
    // Confirmed: Excel stored these with the prefix.
    private static readonly string[] Confirmed =
        [
            "ACOT", "ACOTH", "AGGREGATE", "ANCHORARRAY", "ARABIC", "ARRAYTOTEXT", "BASE", "BETA.DIST", "BETA.INV", "BINOM.DIST", "BINOM.DIST.RANGE",
            "BINOM.INV", "BITAND", "BITLSHIFT", "BITOR", "BITRSHIFT", "BITXOR", "BYCOL", "BYROW", "CEILING.MATH", "CEILING.PRECISE", "CHISQ.DIST",
            "CHISQ.DIST.RT", "CHISQ.INV", "CHISQ.INV.RT", "CHISQ.TEST", "CHOOSECOLS", "CHOOSEROWS", "COMBINA", "CONCAT", "CONFIDENCE.NORM",
            "CONFIDENCE.T", "COT", "COTH", "COVARIANCE.P", "COVARIANCE.S", "CSC", "CSCH", "DAYS", "DECIMAL", "DETECTLANGUAGE", "DROP", "ENCODEURL",
            "ERF.PRECISE", "ERFC.PRECISE", "EXPAND", "EXPON.DIST", "F.DIST", "F.DIST.RT", "F.INV", "F.INV.RT", "F.TEST", "FIELDVALUE", "FILTERXML",
            "FLOOR.MATH", "FLOOR.PRECISE", "FORECAST.ETS", "FORECAST.ETS.CONFINT", "FORECAST.ETS.SEASONALITY", "FORECAST.ETS.STAT",
            "FORECAST.LINEAR", "FORMULATEXT", "GAMMA", "GAMMA.DIST", "GAMMA.INV", "GAMMALN.PRECISE", "GAUSS", "GROUPBY", "HSTACK", "HYPGEOM.DIST",
            "IFNA", "IFS", "IMAGE", "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMSEC", "IMSECH", "IMSINH", "IMTAN", "ISFORMULA", "ISOMITTED",
            "ISOWEEKNUM", "LAMBDA", "LET", "LOGNORM.DIST", "LOGNORM.INV", "MAKEARRAY", "MAP", "MAXIFS", "MINIFS", "MODE.MULT", "MODE.SNGL", "MUNIT",
            "NEGBINOM.DIST", "NORM.DIST", "NORM.INV", "NORM.S.DIST", "NORM.S.INV", "NUMBERVALUE", "PDURATION", "PERCENTILE.EXC", "PERCENTILE.INC",
            "PERCENTOF", "PERCENTRANK.EXC", "PERCENTRANK.INC", "PERMUTATIONA", "PHI", "PIVOTBY", "POISSON.DIST", "QUARTILE.EXC", "QUARTILE.INC",
            "RANDARRAY", "RANK.AVG", "RANK.EQ", "REDUCE", "REGEXEXTRACT", "REGEXREPLACE", "REGEXTEST", "RRI", "SCAN", "SEC", "SECH", "SEQUENCE",
            "SHEET", "SHEETS", "SINGLE", "SKEW.P", "SORTBY", "STDEV.P", "STDEV.S", "STOCKHISTORY", "SWITCH", "T.DIST", "T.DIST.2T", "T.DIST.RT",
            "T.INV", "T.INV.2T", "T.TEST", "TAKE", "TEXTAFTER", "TEXTBEFORE", "TEXTJOIN", "TEXTSPLIT", "TOCOL", "TOROW", "TRANSLATE", "TRIMRANGE",
            "UNICHAR", "UNICODE", "UNIQUE", "VALUETOTEXT", "VAR.P", "VAR.S", "VSTACK", "WEBSERVICE", "WEIBULL.DIST", "WRAPCOLS", "WRAPROWS",
            "XLOOKUP", "XMATCH", "XOR", "Z.TEST",
        ];

    // Listed by the file-format grammar; this Excel build does not know them (service-bound or newer), so the prefix is taken from the grammar.
    private static readonly string[] GrammarOnly =
        [
            "COPILOT", "LONGTEXT", "PQSOURCE", "PYTHON_STR", "PYTHON_TYPE", "PYTHON_TYPENAME", "QUERYSTRING",
        ];

    private static readonly FrozenSet<string> Xlfn = FrozenSet.ToFrozenSet([.. Confirmed, .. GrammarOnly], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> XlfnWs = FrozenSet.ToFrozenSet(["FILTER", "SORT"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The prefix a file stores in front of a function name.</summary>
    /// <param name="name">The function name without prefix.</param>
    /// <returns><c>_xlfn._xlws.</c>, <c>_xlfn.</c>, or the empty string for a function that has none.</returns>
    public static string PrefixFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return XlfnWs.Contains(name) ? "_xlfn._xlws." : Xlfn.Contains(name) ? "_xlfn." : string.Empty;
    }
}
