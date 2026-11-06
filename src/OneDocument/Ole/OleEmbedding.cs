using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Ole;

/// <summary>
/// The names and fragments the converters of the legacy formats share when they write an embedded OLE object into an Office Open XML
/// package ([MS-OLEDS] storage → <c>oleObject</c> part, with the picture shape that shows it).
/// </summary>
public static class OleEmbedding
{
    /// <summary>The content type of an embedded-object part (<c>word/embeddings/oleObject1.bin</c>): a compound file holding the object's storage.</summary>
    public const string ObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";

    /// <summary>The relationship type from a part to an embedded-object part, or to the file a linked object points at.</summary>
    public const string ObjectRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";

    /// <summary>
    /// The VML picture-frame shape type (<c>_x0000_t75</c>) that the shapes of embedded objects refer to, as ECMA-376 Part 1 §17.3.3.19 (the
    /// <c>object</c> element) shows it. It uses the <c>v:</c> and <c>o:</c> prefixes, which the part declares.
    /// </summary>
    public const string PictureShapeType =
        "<v:shapetype id=\"_x0000_t75\" coordsize=\"21600,21600\" o:spt=\"75\" o:preferrelative=\"t\" path=\"m@4@5l@4@11@9@11@9@5xe\" filled=\"f\" stroked=\"f\">"
        + "<v:stroke joinstyle=\"miter\"/><v:formulas><v:f eqn=\"if lineDrawn pixelLineWidth 0\"/><v:f eqn=\"sum @0 1 0\"/><v:f eqn=\"sum 0 0 @1\"/><v:f eqn=\"prod @2 1 2\"/>"
        + "<v:f eqn=\"prod @3 21600 pixelWidth\"/><v:f eqn=\"prod @3 21600 pixelHeight\"/><v:f eqn=\"sum @0 0 1\"/><v:f eqn=\"prod @6 1 2\"/><v:f eqn=\"prod @7 21600 pixelWidth\"/>"
        + "<v:f eqn=\"sum @8 21600 0\"/><v:f eqn=\"prod @7 21600 pixelHeight\"/><v:f eqn=\"sum @10 21600 0\"/></v:formulas>"
        + "<v:path o:extrusionok=\"f\" gradientshapeok=\"t\" o:connecttype=\"rect\"/><o:lock v:ext=\"edit\" aspectratio=\"t\"/></v:shapetype>";

    /// <summary>Makes the file URI that a linked object's external relationship carries from a path in the form the legacy file stores (<c>C:\dir\file.xls</c>).</summary>
    /// <param name="path">The path or URL as stored. A path with a scheme is returned as it is.</param>
    /// <returns>A <c>file:///</c> URI, with characters outside the unreserved set percent-encoded as UTF-8.</returns>
    public static string FileUri(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains("://", StringComparison.Ordinal))
        {
            return path;
        }

        string p = path.Replace('\\', '/');
        var sb = new StringBuilder("file:///");
        foreach (char c in p)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '/' or ':' or '.' or '-' or '_' or '~')
            {
                sb.Append(c);
            }
            else
            {
                foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
        }

        return sb.ToString();
    }
}
