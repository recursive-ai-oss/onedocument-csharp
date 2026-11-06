using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// Style names in references are meant to match style:name exactly, but producers write the two with and without the _HH_ encoding of special characters.
internal static class OdsStyleLookup
{
    public static OdfStyle? Find(OdfStyleSet styles, string family, string? name)
    {
        if (name is null)
        {
            return null;
        }

        return styles.Find(family, name)
            ?? styles.Find(family, OdfLengths.EncodeStyleName(name))
            ?? styles.Find(family, OdfLengths.DecodeStyleName(name));
    }
}
