using System.Globalization;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>The obfuscation [MS-OVBA] §2.4.3 puts over the <c>CMG</c>, <c>DPB</c> and <c>GC</c> values. It is a self-describing XOR chain, not a secret: it is undone only to read the flags, never to learn a password.</summary>
internal static class VbaDataEncryption
{
    /// <summary>Undoes the encoding of a hexadecimal value ([MS-OVBA] §2.4.3.3).</summary>
    /// <param name="hex">The value as written in the <c>PROJECT</c> stream.</param>
    /// <returns>The data bytes, or null when the value is not valid hexadecimal, is cut short, or has a version other than 2.</returns>
    internal static byte[]? Decode(string hex)
    {
        if (hex.Length % 2 != 0 || hex.Length < 12)
        {
            return null;
        }

        byte[] b = new byte[hex.Length / 2];
        for (int i = 0; i < b.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out b[i]))
            {
                return null;
            }
        }

        byte seed = b[0];
        byte versionEnc = b[1];
        byte projKeyEnc = b[2];
        if ((seed ^ versionEnc) != 2)
        {
            return null;
        }

        byte unencrypted1 = (byte)(seed ^ projKeyEnc);
        byte encrypted1 = projKeyEnc;
        byte encrypted2 = versionEnc;
        int pos = 3;
        int ignored = (seed & 6) / 2;

        byte Step(byte enc)
        {
            byte plain = (byte)(enc ^ (byte)(encrypted2 + unencrypted1));
            encrypted2 = encrypted1;
            encrypted1 = enc;
            unencrypted1 = plain;
            return plain;
        }

        if (pos + ignored + 4 > b.Length)
        {
            return null;
        }

        for (int i = 0; i < ignored; i++)
        {
            Step(b[pos++]);
        }

        uint length = 0;
        for (int i = 0; i < 4; i++)
        {
            length |= (uint)Step(b[pos++]) << (8 * i);
        }

        if (length > b.Length - pos)
        {
            return null;
        }

        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Step(b[pos++]);
        }

        return data;
    }

    /// <summary>Reads the three protection values into a report.</summary>
    internal static VbaProtection Interpret(string? cmg, string? dpb, string? gc, out bool unreadable)
    {
        unreadable = false;
        bool user = false, host = false, editor = false, password = false, visible = true;
        if (cmg is not null)
        {
            byte[]? d = Decode(cmg);
            if (d is null || d.Length < 4)
            {
                unreadable = true;
            }
            else
            {
                // [MS-OVBA] §2.3.1.15: bit 0 fUserProtected, bit 1 fHostProtected, bit 2 fVBEProtected.
                uint flags = BitConverter.ToUInt32(d, 0);
                user = (flags & 1) != 0;
                host = (flags & 2) != 0;
                editor = (flags & 4) != 0;
            }
        }

        if (dpb is not null)
        {
            byte[]? d = Decode(dpb);
            if (d is null || d.Length == 0)
            {
                unreadable = true;
            }
            else
            {
                // [MS-OVBA] §2.3.1.16: a project without a password has the single byte 0x00.
                password = !(d.Length == 1 && d[0] == 0);
            }
        }

        if (gc is not null)
        {
            byte[]? d = Decode(gc);
            if (d is null || d.Length < 1)
            {
                unreadable = true;
            }
            else
            {
                // [MS-OVBA] §2.3.1.17: 0x00 is not visible, 0xFF visible.
                visible = d[0] != 0;
            }
        }

        return new VbaProtection(cmg, dpb, gc, user, host, editor, password, visible, unreadable);
    }
}

/// <summary>Finds the streams that hold VBA signatures. They are located, never read or verified.</summary>
internal static class VbaSignatures
{
    // The signature streams carry the control character U+0005 in front of their names.
    private const string Prefix = "\u0005DigitalSignature";

    internal static VbaSignatureKinds Detect(CfbPackage cfb)
    {
        VbaSignatureKinds kinds = VbaSignatureKinds.None;
        foreach (CfbStreamPart p in cfb.Streams)
        {
            kinds |= OfStreamName(p.Name);
        }

        return kinds;
    }

    internal static VbaSignatureKinds OfStreamName(string path)
    {
        int slash = path.LastIndexOf('/');
        string leaf = slash < 0 ? path : path[(slash + 1)..];
        if (!leaf.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return VbaSignatureKinds.None;
        }

        string suffix = leaf[Prefix.Length..];
        if (suffix.Equals("Ex", StringComparison.OrdinalIgnoreCase))
        {
            return VbaSignatureKinds.Agile;
        }

        return suffix.Equals("Ext", StringComparison.OrdinalIgnoreCase) ? VbaSignatureKinds.V3 : suffix.Length == 0 ? VbaSignatureKinds.Legacy : VbaSignatureKinds.None;
    }

    // OOXML keeps each signature in a part of its own (vbaProjectSignature.bin and its Agile and V3 siblings).
    internal static VbaSignatureKinds OfPart(string partName, string contentType)
    {
        string leaf = partName[(partName.LastIndexOf('/') + 1)..];
        if (contentType.StartsWith("application/vnd.ms-office.vbaProjectSignature", StringComparison.OrdinalIgnoreCase)
            || leaf.StartsWith("vbaProjectSignature", StringComparison.OrdinalIgnoreCase))
        {
            string c = (contentType.Length > 0 ? contentType : leaf);
            if (c.Contains("V3", StringComparison.OrdinalIgnoreCase))
            {
                return VbaSignatureKinds.V3;
            }

            return c.Contains("Agile", StringComparison.OrdinalIgnoreCase) ? VbaSignatureKinds.Agile : VbaSignatureKinds.Legacy;
        }

        return VbaSignatureKinds.None;
    }
}
