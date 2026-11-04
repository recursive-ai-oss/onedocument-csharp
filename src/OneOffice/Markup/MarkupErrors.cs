using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Markup;

/// <summary>Maps between the tokenizer's self-contained types and the library's shared limits and exceptions.</summary>
internal static class MarkupErrors
{
    internal static XmlLimits ToXmlLimits(OfficeLimits limits) => new()
    {
        MaxDepth = limits.MaxXmlDepth,
        MaxAttributeCount = limits.MaxXmlAttributeCount,
        MaxNameLength = limits.MaxXmlNameLength,
    };

    /// <summary>Turns a tokenizer error into the exception the public API raises. <paramref name="offsetMap"/> converts an offset in the UTF-8 working buffer to one in the original bytes.</summary>
    internal static OfficeException Translate(XmlSyntaxException ex, string? partUri, Func<long, long>? offsetMap = null)
    {
        long offset = ex.Offset >= 0 && offsetMap is not null ? offsetMap(ex.Offset) : ex.Offset;
        DefectLocation where = partUri is null ? DefectLocation.AtText(offset) : DefectLocation.InPart(partUri, offset);
        if (ex.LimitName is string name)
        {
            string property = name switch
            {
                nameof(XmlLimits.MaxDepth) => nameof(OfficeLimits.MaxXmlDepth),
                nameof(XmlLimits.MaxAttributeCount) => nameof(OfficeLimits.MaxXmlAttributeCount),
                _ => nameof(OfficeLimits.MaxXmlNameLength),
            };
            return new OfficeLimitException(property, ex.Limit, ex.Actual, where, ex);
        }

        return new OfficeFormatException(DefectKind.Markup, ex.CodeName, ex.Message, where, ex);
    }

    /// <summary>Counts how far an offset into UTF-8 text reaches into the same text as UTF-16 bytes, for error locations in UTF-16 parts.</summary>
    internal static long Utf16Offset(ReadOnlySpan<byte> utf8, long offset, int bomLength)
    {
        int n = (int)Math.Clamp(offset, 0, utf8.Length);
        return bomLength + ((long)Encoding.UTF8.GetCharCount(utf8[..n]) * 2);
    }
}
