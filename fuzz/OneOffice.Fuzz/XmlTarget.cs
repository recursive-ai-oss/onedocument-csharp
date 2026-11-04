using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The XML tokenizer and the markup tree. Every input must either be refused with the tokenizer's typed error (or the library's
/// typed exception) or be accepted, and when accepted:
/// the raw tokens tile the input; reading the input in blocks gives the same tokens; the tree writes the input back byte for byte;
/// the streaming reader sees the same tokens; the tree and the tokenizer agree on what is well-formed; and an edit followed by a
/// re-parse keeps the document well-formed.
/// </summary>
internal static class XmlTarget
{
    /// <summary>The tokenizer alone: tiling, and block-wise reading against whole-buffer reading.</summary>
    public static void RunTokenizer(ReadOnlySpan<byte> data)
    {
        byte[] input = data.ToArray();
        (List<string>? whole, XmlErrorCode? wholeError) = Tokenize(input);

        // Block-wise reading must agree with whole-buffer reading, at a split point chosen from the input itself.
        int split = input.Length == 0 ? 1 : Math.Max(1, (input[0] ^ input[^1]) % (input.Length + 1));
        (List<string>? chunked, XmlErrorCode? chunkedError) = Tokenize(input, split);
        if (wholeError != chunkedError || (whole is not null && !whole.SequenceEqual(chunked!)))
        {
            throw new InvalidOperationException("chunked reading disagrees with whole-buffer reading");
        }
    }

    /// <summary>The tree and the streaming reader against the tokenizer, and an edit followed by a re-parse.</summary>
    public static void RunTree(ReadOnlySpan<byte> data)
    {
        byte[] input = data.ToArray();
        MarkupDocument? doc = TreeRoundTrip(input);
        if (!XmlEncoding.TryDetect(input, out XmlEncodingKind kind, out _) || kind != XmlEncodingKind.Utf8)
        {
            // The tree transcodes UTF-16; the tokenizer refuses it. Only the tree's typed behaviour is checked.
            return;
        }

        (List<string>? whole, XmlErrorCode? wholeError) = Tokenize(input);
        if ((doc is null) != (wholeError is not null))
        {
            throw new InvalidOperationException($"tree and tokenizer disagree: tree {(doc is null ? "refused" : "accepted")}, tokenizer {wholeError?.ToString() ?? "accepted"}");
        }

        if (doc is null)
        {
            return;
        }

        int readerTokens = 0;
        using (var reader = new MarkupReader(new MemoryStream(input), bufferSize: 4096))
        {
            while (reader.Read())
            {
                readerTokens++;
            }
        }

        if (readerTokens != whole!.Count)
        {
            throw new InvalidOperationException("the streaming reader saw a different number of tokens");
        }

        // An edit changes the root only and the result is still well-formed XML.
        MarkupNode root = doc.Root;
        root.SetAttribute("xmlns:fuzzprobe", "urn:fuzz");
        byte[] edited = doc.ToArray();
        if (!MarkupDocument.TryParse(edited, out MarkupDocument? again) || again is null)
        {
            throw new InvalidOperationException("the edited document does not parse");
        }

        if (again.Root.GetAttribute("xmlns:fuzzprobe") != "urn:fuzz")
        {
            throw new InvalidOperationException("the edit was lost");
        }
    }

    private static MarkupDocument? TreeRoundTrip(byte[] input)
    {
        try
        {
            MarkupDocument doc = MarkupDocument.Parse(input);
            if (!doc.ToArray().AsSpan().SequenceEqual(input))
            {
                throw new InvalidOperationException("tree round trip changed the bytes");
            }

            _ = doc.Root.InnerText;
            return doc;
        }
        catch (RecursiveAI.OneOffice.OfficeException)
        {
            return null;
        }
    }

    private static (List<string>? Tokens, XmlErrorCode? Error) Tokenize(byte[] data, int block = 0)
    {
        var tokens = new List<string>();
        try
        {
            if (block == 0)
            {
                var tk = new XmlTokenizer(data);
                int next = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
                while (tk.Read())
                {
                    if (tk.Info.Start != next)
                    {
                        throw new InvalidOperationException("tokens do not tile the input");
                    }

                    next = tk.Info.End;
                    tokens.Add(Describe(tk));
                }

                if (next != data.Length)
                {
                    throw new InvalidOperationException("tokens stop short of the end of the input");
                }
            }
            else
            {
                var state = new XmlTokenizerState();
                int head = 0;
                int end = 0;
                while (true)
                {
                    end = (int)Math.Min(data.Length, (long)end + block);
                    var tk = new XmlTokenizer(data.AsSpan(head, end - head), end == data.Length, state);
                    while (tk.Read())
                    {
                        tokens.Add(Describe(tk));
                    }

                    head += tk.BytesConsumed;
                    if (tk.EndOfDocument)
                    {
                        break;
                    }
                }
            }

            return (tokens, null);
        }
        catch (XmlSyntaxException ex)
        {
            return (null, ex.Code);
        }
    }

    private static string Describe(XmlTokenizer tk) =>
        tk.Kind + "|" + Convert.ToHexString(tk.Raw) + "|" + Convert.ToHexString(tk.NamespaceUri);

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] seeds =
        [
            "\uFEFF<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:document xmlns:w=\"urn:w\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"w14\" xmlns:w14=\"urn:w14\">\r\n  <w:body><w:p w14:paraId='1'><w:r><w:t xml:space=\"preserve\">a &amp; b</w:t></w:r><w:r/></w:p>\r\n<mc:AlternateContent><mc:Choice Requires=\"w14\"><w:p/></mc:Choice><mc:Fallback><w:p/></mc:Fallback></mc:AlternateContent><!-- c --><?pi d?><![CDATA[x]]></w:body>\r\n</w:document>\r\n",
            "<a/>",
            "<a xmlns='urn:d' xmlns:p='urn:p'><p:b p:c=\"1\" d = '2' >t</p:b><e/></a>",
            "<root><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\"><f>SUM(A1:A2)</f><v>3</v></c></root>",
            "<!-- only a comment -->",
            "<!DOCTYPE a [<!ENTITY e \"x\">]><a>&e;</a>",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "xml" + i), Encoding.UTF8.GetBytes(seeds[i]));
        }

        File.WriteAllBytes(Path.Combine(directory, "utf16"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<a b=\"1\">\u00e9</a>")]);
        return 0;
    }
}
