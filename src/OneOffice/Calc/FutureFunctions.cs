using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// Which function names a file stores with a prefix. Functions added after Excel 2007 are written <c>_xlfn.NAME</c>; the dynamic-array
/// functions that Excel treats as worksheet-only are written <c>_xlfn._xlws.NAME</c>. The tables come from Microsoft's published list of
/// functions that show an <c>_xlfn.</c> prefix in older versions, plus the names the file format documents for dynamic arrays.
/// </summary>
public static class FutureFunctions
{
    private static readonly FrozenSet<string> Xlfn = FrozenSet.ToFrozenSet(
        [
            "ACOT", "ACOTH", "AGGREGATE", "ARABIC", "ARRAYTOTEXT", "BASE", "BETA.DIST", "BETA.INV", "BINOM.DIST", "BINOM.DIST.RANGE", "BINOM.INV",
            "BITAND", "BITLSHIFT", "BITOR", "BITRSHIFT", "BITXOR", "BYCOL", "BYROW", "CEILING.MATH", "CEILING.PRECISE", "CHISQ.DIST", "CHISQ.DIST.RT",
            "CHISQ.INV", "CHISQ.INV.RT", "CHISQ.TEST", "CHOOSECOLS", "CHOOSEROWS", "COMBINA", "CONCAT", "CONFIDENCE.NORM", "CONFIDENCE.T", "COT", "COTH",
            "COVARIANCE.P", "COVARIANCE.S", "CSC", "CSCH", "DAYS", "DECIMAL", "DROP", "ERF.PRECISE", "ERFC.PRECISE", "EXPAND", "EXPON.DIST", "F.DIST",
            "F.DIST.RT", "F.INV", "F.INV.RT", "F.TEST", "FILTERXML", "FLOOR.MATH", "FLOOR.PRECISE", "FORECAST.ETS", "FORECAST.ETS.CONFINT",
            "FORECAST.ETS.SEASONALITY", "FORECAST.ETS.STAT", "FORECAST.LINEAR", "FORMULATEXT", "GAMMA", "GAMMA.DIST", "GAMMA.INV", "GAMMALN.PRECISE",
            "GAUSS", "GROUPBY", "HSTACK", "HYPGEOM.DIST", "IFNA", "IFS", "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMSEC", "IMSECH", "IMSINH", "IMTAN",
            "ISFORMULA", "ISOMITTED", "ISOWEEKNUM", "LAMBDA", "LET", "LOGNORM.DIST", "LOGNORM.INV", "MAKEARRAY", "MAP", "MAXIFS", "MINIFS", "MODE.MULT",
            "MODE.SNGL", "MUNIT", "NEGBINOM.DIST", "NETWORKDAYS.INTL", "NORM.DIST", "NORM.INV", "NORM.S.DIST", "NORM.S.INV", "NUMBERVALUE", "PDURATION",
            "PERCENTILE.EXC", "PERCENTILE.INC", "PERCENTOF", "PERCENTRANK.EXC", "PERCENTRANK.INC", "PERMUTATIONA", "PHI", "PIVOTBY", "POISSON.DIST",
            "QUARTILE.EXC", "QUARTILE.INC", "QUERYSTRING", "RANDARRAY", "RANK.AVG", "RANK.EQ", "REDUCE", "REGEXEXTRACT", "REGEXREPLACE", "REGEXTEST",
            "RRI", "SCAN", "SEC", "SECH", "SEQUENCE", "SHEET", "SHEETS", "SKEW.P", "SORTBY", "STDEV.P", "STDEV.S", "SWITCH", "T.DIST", "T.DIST.2T",
            "T.DIST.RT", "T.INV", "T.INV.2T", "T.TEST", "TAKE", "TEXTAFTER", "TEXTBEFORE", "TEXTJOIN", "TEXTSPLIT", "TOCOL", "TOROW", "UNICHAR",
            "UNICODE", "UNIQUE", "VAR.P", "VAR.S", "VSTACK", "WEBSERVICE", "WEIBULL.DIST", "WRAPCOLS", "WRAPROWS", "XLOOKUP", "XMATCH", "XOR",
            "Z.TEST", "ANCHORARRAY", "SINGLE", "CONCAT", "ENCODEURL", "DAYS", "IMAGE", "STOCKHISTORY", "VALUETOTEXT", "LAMBDA", "TRIMRANGE",
        ],
        StringComparer.OrdinalIgnoreCase);

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
