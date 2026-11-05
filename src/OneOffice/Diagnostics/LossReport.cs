namespace RecursiveAI.OneOffice.Diagnostics;

/// <summary>
/// Receives what a conversion could not carry over. Every conversion and every format that is narrower than the model it is written from
/// (CSV from a workbook, Markdown from a document, a legacy format from the hub) reports through this one interface.
/// </summary>
public interface ILossSink
{
    /// <summary>Reports a feature that was dropped or flattened.</summary>
    /// <param name="code">A stable lower-case code, <c>loss.&lt;feature&gt;</c>, for example <c>loss.formulas</c>. Codes are listed in <c>docs/contracts.md</c> and never reused.</param>
    /// <param name="feature">The feature in words a person can read: <c>formulas</c>, <c>cell styles</c>.</param>
    /// <param name="count">How many items were affected (cells, sheets, paragraphs); 1 when the feature is a single property.</param>
    /// <param name="message">What happened to them, in English.</param>
    void Report(string code, string feature, long count, string message);
}

/// <summary>One kind of loss: what was dropped, how many items, and what became of them.</summary>
/// <param name="Code">The stable <c>loss.&lt;feature&gt;</c> code.</param>
/// <param name="Feature">The feature, in words.</param>
/// <param name="Count">The number of items affected.</param>
/// <param name="Message">What happened to them.</param>
public readonly record struct LossItem(string Code, string Feature, long Count, string Message);

/// <summary>
/// A list of everything a conversion dropped, one <see cref="LossItem"/> per code. Reports for the same code add their counts together, so
/// a million dropped cell styles are one line. An empty report means nothing was lost. Not safe for concurrent writers.
/// </summary>
public sealed class LossReport : ILossSink
{
    private readonly List<LossItem> _items = [];

    /// <summary>Gets the losses in the order each code was first reported.</summary>
    public IReadOnlyList<LossItem> Items => _items;

    /// <summary>Gets a value indicating whether nothing was lost.</summary>
    public bool IsLossless => _items.Count == 0;

    /// <summary>Reports a loss. A report for a code already present adds to its count and keeps the first message.</summary>
    /// <param name="code">The <c>loss.&lt;feature&gt;</c> code.</param>
    /// <param name="feature">The feature, in words.</param>
    /// <param name="count">The number of items affected; values below 1 are treated as 1.</param>
    /// <param name="message">What happened to them.</param>
    /// <exception cref="ArgumentException">The code or the message is null or empty.</exception>
    public void Report(string code, string feature, long count, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentNullException.ThrowIfNull(feature);
        if (count < 1)
        {
            count = 1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Code == code)
            {
                LossItem old = _items[i];
                _items[i] = old with { Count = old.Count + count };
                return;
            }
        }

        _items.Add(new LossItem(code, feature, count, message));
    }

    /// <summary>Gets the count reported for a code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The count, or 0 when the code was not reported.</returns>
    public long CountOf(string code)
    {
        foreach (LossItem item in _items)
        {
            if (item.Code == code)
            {
                return item.Count;
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (_items.Count == 0)
        {
            return "no loss";
        }

        var sb = new System.Text.StringBuilder();
        foreach (LossItem i in _items)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            sb.Append(i.Feature).Append(" (").Append(i.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(')');
        }

        return sb.ToString();
    }
}
