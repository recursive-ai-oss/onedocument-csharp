using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.Comments;
using RecursiveAI.OneOffice.Documents.Fields;
using RecursiveAI.OneOffice.Documents.Notes;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.DocxProbe;

internal static partial class Program
{
    /// <summary>
    /// <c>DocxProbe census FILE...</c> counts fields (by type, kind, depth), notes, note references and comments per file and prints
    /// one JSON line each, with the defects the readers reported. Exit 1 when a file could not be read.
    /// </summary>
    private static int Census(string[] files)
    {
        int failures = 0;
        foreach (string file in files)
        {
            var defects = new DefectCollection();
            var sw = Stopwatch.StartNew();
            var line = new Dictionary<string, object?> { ["file"] = Path.GetFileName(file) };
            try
            {
                using InMemoryPackage package = Read(file);
                using Document doc = Document.FromPackage(package, new OpenContext(defects: defects));
                IReadOnlyList<Field> fields = doc.Fields();
                var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (Field f in fields)
                {
                    string t = f.Type.Length == 0 ? "(none)" : f.Type;
                    types[t] = types.GetValueOrDefault(t) + 1;
                    _ = f.ResultText;
                }

                int otherFields = 0;
                foreach (Note n in doc.Footnotes.Concat(doc.Endnotes))
                {
                    otherFields += doc.Fields(n).Count;
                }

                foreach (Comment c in doc.Comments)
                {
                    otherFields += doc.Fields(c).Count;
                }

                int anchored = doc.Comments.Count(c => c.Anchor.Reference is not null);
                line["fields"] = fields.Count;
                line["simple"] = fields.Count(f => f.Kind == FieldKind.Simple);
                line["complex"] = fields.Count(f => f.Kind == FieldKind.Complex);
                line["incomplete"] = fields.Count(f => !f.IsComplete);
                line["maxDepth"] = fields.Count == 0 ? 0 : fields.Max(f => f.Depth) + 1;
                line["types"] = types;
                line["fieldsInNotesAndComments"] = otherFields;
                line["footnotes"] = doc.Footnotes.Normal().Count;
                line["endnotes"] = doc.Endnotes.Normal().Count;
                line["footnoteRefs"] = doc.NoteReferences(NoteKind.Footnote).Count;
                line["endnoteRefs"] = doc.NoteReferences(NoteKind.Endnote).Count;
                line["comments"] = doc.Comments.Count;
                line["replies"] = doc.Comments.Count(c => c.IsReply);
                line["anchored"] = anchored;
            }
            catch (OfficeException ex)
            {
                failures++;
                line["error"] = ex.Defect.Code;
            }
            catch (InvalidDataException)
            {
                // The probe's own zip reader (the BCL, test tooling only) refused the file, for example a truncated download.
                failures++;
                line["error"] = "zip.unreadable";
            }

            line["defects"] = defects.GroupBy(d => d.Code).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
            line["ms"] = sw.ElapsedMilliseconds;
            Console.WriteLine(JsonSerializer.Serialize(line));
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>DocxProbe annotate IN OUT</c> adds a comment with a reply and a footnote to the first paragraph that has a run, and
    /// rewrites the first field's cached result, then writes a new zip. Used to check that the edits are schema-valid and open elsewhere.
    /// </summary>
    private static int Annotate(string input, string output) => Rewrite(input, output, doc =>
    {
        // A run with text in a paragraph that has no field: a comment or note reference inside a field's code is not meaningful.
        Run? run = doc.Body.Paragraphs()
            .Where(p => !p.Markup.Node.OuterXml.Contains("fldChar", StringComparison.Ordinal) && !p.Markup.Node.OuterXml.Contains("fldSimple", StringComparison.Ordinal))
            .SelectMany(p => p.Runs()).FirstOrDefault(r => r.Text.Length > 0);
        var info = new Dictionary<string, object?>();
        if (run is not null)
        {
            Comment c = doc.Comments.Add(run, run, "Probe", "A comment.\nSecond paragraph.", "P", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
            doc.Comments.Reply(c, "Probe", "A reply.", "P", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));
            doc.Footnotes.Add("A footnote.").InsertReference(run);
            info["annotated"] = true;
        }

        Field? field = doc.Fields().FirstOrDefault(f => f.IsComplete);
        if (field is not null)
        {
            field.SetResult(field.ResultText.Length > 0 ? field.ResultText : "1");
            info["fieldType"] = field.Type;
        }

        return info;
    });
}
