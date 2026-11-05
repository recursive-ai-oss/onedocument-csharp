using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

// What a header or footer paragraph needs to know about the page it is printed on.
internal sealed class PageFieldState
{
    public long Printed; // the number the page prints
    public string? NumberFormat; // w:pgNumType w:fmt of the page's section
    public int NumPages; // total pages from the previous pass; 0 on the first pass
    public int SectionPages; // pages of the page's section from the previous pass
    public int SectionNumber; // 1-based
    public int Physical; // 1-based page index in the document
    public PageTotals? Totals; // the previous pass's counts
}

// The hook the paragraph formatter calls before it reads a paragraph's runs: fields whose value layout knows replace the text of their
// result runs (see Fields/FieldRunReplacer.cs and docs/layout-fields.md).
internal static class PageFieldScanner
{
    public static void Prepare(LayoutContext ctx, Paragraph p) => ctx.RunText = FieldRunReplacer.Prepare(ctx, p);
}
