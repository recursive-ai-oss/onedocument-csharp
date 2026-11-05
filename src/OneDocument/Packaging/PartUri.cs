namespace RecursiveAI.OneDocument.Packaging;

/// <summary>
/// The name of a part in an Open Packaging Conventions package: an absolute path such as <c>/word/document.xml</c>. A valid name
/// starts with <c>/</c>, has no empty, <c>.</c> or <c>..</c> segment, does not end with <c>/</c>, and has no backslash or control
/// character. Part names are compared ignoring case, as the packaging conventions require, so <c>/Word/Document.xml</c> and
/// <c>/word/document.xml</c> are one part; the spelling given is kept for writing.
/// </summary>
public readonly struct PartUri : IEquatable<PartUri>
{
    private readonly string? _value;

    /// <summary>Creates a part name.</summary>
    /// <param name="value">The name.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid part name.</exception>
    public PartUri(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsValid(value))
        {
            throw new ArgumentException($"'{value}' is not a valid part name.", nameof(value));
        }

        _value = value;
    }

    /// <summary>Gets the name as spelled; the empty string for a default value.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the directory part of the name with a trailing slash (<c>/word/</c> for <c>/word/document.xml</c>; <c>/</c> at the root).</summary>
    public string Directory
    {
        get
        {
            string v = Value;
            int i = v.LastIndexOf('/');
            return i < 0 ? "/" : v[..(i + 1)];
        }
    }

    /// <summary>Gets the extension with its dot, or the empty string.</summary>
    public string Extension => Path.GetExtension(Value);

    /// <summary>Checks whether a string is a valid part name.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>True if valid.</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value[^1] == '/')
        {
            return false;
        }

        int segmentStart = 1;
        for (int i = 1; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                char c = value[i];
                if (c == '\\' || char.IsControl(c))
                {
                    return false;
                }

                if (c != '/')
                {
                    continue;
                }
            }

            ReadOnlySpan<char> segment = value.AsSpan(segmentStart, i - segmentStart);
            if (segment.IsEmpty || segment.SequenceEqual(".") || segment.SequenceEqual(".."))
            {
                return false;
            }

            segmentStart = i + 1;
        }

        return true;
    }

    /// <summary>Tries to create a part name.</summary>
    /// <param name="value">The name.</param>
    /// <param name="uri">The part name, when valid.</param>
    /// <returns>True if <paramref name="value"/> is a valid part name.</returns>
    public static bool TryCreate(string? value, out PartUri uri)
    {
        if (IsValid(value))
        {
            uri = new PartUri(value!);
            return true;
        }

        uri = default;
        return false;
    }

    /// <summary>
    /// Resolves a relationship target against the part that holds the relationship, as a relative reference: <c>../media/a.png</c>
    /// from <c>/word/document.xml</c> is <c>/media/a.png</c>, and a target starting with <c>/</c> is already absolute. The target is
    /// taken as written, without percent-decoding or fragment handling; the packaging reader does those.
    /// </summary>
    /// <param name="source">The part that owns the relationship, or null for a package-level relationship (the base is then <c>/</c>).</param>
    /// <param name="target">The relationship's <c>Target</c>.</param>
    /// <param name="resolved">The part name, when the target stays inside the package and is valid.</param>
    /// <returns>True when resolved; false for a target that climbs above the root or is not a valid name.</returns>
    public static bool TryResolve(PartUri? source, string target, out PartUri resolved)
    {
        resolved = default;
        if (string.IsNullOrEmpty(target) || target[^1] == '/')
        {
            return false;
        }

        string combined = target[0] == '/' ? target : (source?.Directory ?? "/") + target;
        var stack = new List<string>();
        foreach (string segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    return false;
                }

                stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(segment);
            }
        }

        return TryCreate("/" + string.Join('/', stack), out resolved);
    }

    /// <inheritdoc />
    public bool Equals(PartUri other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PartUri other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two names, ignoring case.</summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns>True if they name the same part.</returns>
    public static bool operator ==(PartUri left, PartUri right) => left.Equals(right);

    /// <summary>Compares two names, ignoring case.</summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns>True if they name different parts.</returns>
    public static bool operator !=(PartUri left, PartUri right) => !left.Equals(right);
}
