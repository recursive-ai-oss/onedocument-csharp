using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.Revisions;

namespace RecursiveAI.OneOffice.Tools.OracleCases;

/// <summary>
/// Redline authoring checked against the desktop application. <c>OracleCases --authoring DIR</c> writes one generated document per
/// authoring operation (each beside its <c>.actions.txt</c> asking for accept-all and reject-all, and an <c>.expected.tsv</c> with what
/// OneOffice's own accepted and rejected views say). <c>OracleCases --check DIR RESULTS</c> compares what the application's accept-all and
/// reject-all produced, in the result lines and in the documents it saved, with those views.
/// </summary>
internal static class AuthoringCases
{
    private static readonly DateTimeOffset s_date = new(2024, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private const string Pilcrow = "\u00b6";

    /// <summary>The application's revision type (WdRevisionType) for the kinds the grid authors; 0 for the rest.</summary>
    private static int WordType(RevisionKind kind) => kind switch
    {
        RevisionKind.Insertion or RevisionKind.ParagraphMarkInserted => 1,
        RevisionKind.Deletion or RevisionKind.ParagraphMarkDeleted => 2,
        RevisionKind.RunPropertiesChange => 3,
        RevisionKind.ParagraphPropertiesChange => 10,
        RevisionKind.MoveFrom or RevisionKind.ParagraphMarkMovedFrom => 14,
        RevisionKind.MoveTo or RevisionKind.ParagraphMarkMovedTo => 15,
        _ => 0,
    };

    private static Paragraph P(Document d, int i) => d.Body.Paragraphs().ElementAt(i);

    private static Document Base(string bodyXml)
    {
        Document doc = Document.Create();
        foreach (var p in doc.Body.Paragraphs().ToList())
        {
            p.Markup.Node.Remove();
        }

        doc.Body.SectionProperties!.Value.Node.InsertXmlBefore(Encoding.UTF8.GetBytes(bodyXml));
        return doc;
    }

    /// <summary>
    /// Gives the document a settings part that asks for the current compatibility mode. Without one the application opens the file in its
    /// oldest mode, and saving it again (which the accept and reject actions do) stops on the Compatibility Checker when the file has a
    /// DrawingML text box.
    /// </summary>
    private static Document WithCurrentCompatibilityMode(Document doc)
    {
        const string Settings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
        if (doc.Package.FindPart("/word/settings.xml") is null)
        {
            doc.Package.AddPart("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
                Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>"));
            var rels = new List<RecursiveAI.OneOffice.Packaging.Relationship>(doc.Package.GetRelationships("/word/document.xml"))
            {
                new("rIdCompat", Settings, "settings.xml"),
            };
            doc.Package.SetRelationships("/word/document.xml", rels);
        }

        return doc;
    }

    private static string Para(string text, string props = "", string rprops = "") =>
        "<w:p>" + (props.Length > 0 ? "<w:pPr>" + props + "</w:pPr>" : "") + "<w:r>" + (rprops.Length > 0 ? "<w:rPr>" + rprops + "</w:rPr>" : "") + "<w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";

    private const string Plain = "<w:p><w:r><w:t xml:space=\"preserve\">alpha bravo charlie </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>delta echo</w:t></w:r></w:p>"
        + "<w:p><w:pPr><w:ind w:left=\"360\"/></w:pPr><w:r><w:t>foxtrot golf hotel</w:t></w:r></w:p><w:p><w:r><w:t>india juliet</w:t></w:r></w:p>";

    private static string Cell(string text) => "<w:tc><w:tcPr><w:tcW w:w=\"2400\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>";

    private const string TableAfter =
        "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>";

    private static string Table() =>
        TableAfter + "<w:tr>" + Cell("a1") + Cell("b1") + "</w:tr><w:tr>" + Cell("a2") + Cell("b2") + "</w:tr><w:tr>" + Cell("a3") + Cell("b3") + "</w:tr></w:tbl>"
        + "<w:p><w:r><w:t>after the table</w:t></w:r></w:p>";

    private static IEnumerable<(string Name, Func<Document> Build)> Cases()
    {
        yield return ("hub-insert-text", () => Edit(Plain, (d, a) => a.InsertText(P(d, 0), 6, "NEW ")));
        yield return ("hub-delete-text", () => Edit(Plain, (d, a) => a.DeleteText(P(d, 0), 6, 14)));
        yield return ("hub-replace-across-runs", () => Edit(Plain, (d, a) => a.ReplaceText(P(d, 0), 14, 12, "ZULU YANKEE")));
        yield return ("hub-insert-paragraph", () => Edit(Plain, (d, a) => a.InsertParagraph(P(d, 1), "inserted paragraph")));
        yield return ("hub-delete-paragraph", () => Edit(Plain, (d, a) => a.DeleteParagraph(P(d, 1))));
        yield return ("hub-move-text-named", () => Edit(Plain, (d, a) => a.MoveText(P(d, 0), 6, 5, P(d, 2), 6, "redline-move-1")));
        yield return ("hub-move-text-same-paragraph", () => Edit(Plain, (d, a) => a.MoveText(P(d, 0), 0, 6, P(d, 0), 20, "redline-move-2")));
        yield return ("hub-move-paragraph-named", () => Edit(Plain, (d, a) => a.MoveParagraph(P(d, 0), P(d, 2), "redline-move-3")));
        yield return ("hub-format-text-typed", () => Edit(Plain, (d, a) => a.FormatText(P(d, 0), 0, 11, new RunPropertiesDelta { Italic = true, Color = "C00000", SizePoints = 14, Font = "Arial", Underline = RunUnderline.SingleLine })));
        yield return ("hub-format-run-typed", () => Edit(Plain, (d, a) => a.ChangeRunProperties(P(d, 0).Runs().Last(), new RunPropertiesDelta { Bold = false, Strike = true })));
        yield return ("hub-paragraph-properties-typed", () => Edit(Plain, (d, a) => a.ChangeParagraphProperties(P(d, 1), new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center, LeftIndent = 720, FirstLineIndent = 240, SpacingBefore = 120, SpacingAfter = 240, LineSpacingMultiple = 1.5, KeepNext = true })));
        yield return ("hub-numbering-change", () => Edit(Plain, (d, a) => a.ChangeParagraphProperties(P(d, 1), new ParagraphPropertiesDelta { NumberingId = 0 })));
        yield return ("hub-table-insert-row", () => Edit(Para("intro") + Table(), (d, a) => a.InsertRow(d.Body.Blocks().OfType<Table>().First(), 3, ["a4", "b4"])));
        yield return ("hub-table-delete-row", () => Edit(Para("intro") + Table(), (d, a) => a.MarkRowDeleted(d.Body.Blocks().OfType<Table>().First().Rows().ElementAt(1))));
        yield return ("hub-table-insert", () => Edit(Para("before the table") + Para("last paragraph"), (d, a) => a.InsertTable(P(d, 0), [["h1", "h2"], ["x1", "x2"], ["y1", "y2"]])));
        yield return ("hub-table-delete", () => Edit(Para("intro") + Table(), (d, a) => a.DeleteTable(d.Body.Blocks().OfType<Table>().First())));
        yield return ("hub-table-properties-typed", () => Edit(Para("intro") + Table(), (d, a) =>
        {
            Table t = d.Body.Blocks().OfType<Table>().First();
            a.ChangeTableProperties(t, new TablePropertiesDelta { WidthTwips = 5000, Alignment = ParagraphAlignment.Center, FixedLayout = true });
            a.ChangeTableRowProperties(t.Rows().First(), new RowPropertiesDelta { HeightTwips = 600, RepeatAsHeader = true, CannotSplit = true });
            a.ChangeTableCellProperties(t.Rows().First().Cells().First(), new CellPropertiesDelta { ShadingFill = "D9D9D9", VerticalAlignment = "center", WidthTwips = 2400 });
        }));
        yield return ("hub-table-properties-table", () => Edit(Para("intro") + Table(), (d, a) =>
            a.ChangeTableProperties(d.Body.Blocks().OfType<Table>().First(), new TablePropertiesDelta { WidthTwips = 5000, Alignment = ParagraphAlignment.Center, FixedLayout = true })));
        yield return ("hub-table-properties-row", () => Edit(Para("intro") + Table(), (d, a) =>
            a.ChangeTableRowProperties(d.Body.Blocks().OfType<Table>().First().Rows().First(), new RowPropertiesDelta { HeightTwips = 600, RepeatAsHeader = true, CannotSplit = true })));
        yield return ("hub-table-properties-cell", () => Edit(Para("intro") + Table(), (d, a) =>
            a.ChangeTableCellProperties(d.Body.Blocks().OfType<Table>().First().Rows().First().Cells().First(), new CellPropertiesDelta { ShadingFill = "D9D9D9", VerticalAlignment = "center", WidthTwips = 2400 })));
        yield return ("hub-callback-ids-two-authors", () =>
        {
            Document doc = Base(Plain);
            int n = 1000;
            RevisionIds ids = RevisionIds.FromCallback(() => n++);
            RevisionAuthor first = doc.Revisions.CreateAuthor("First", s_date, ids);
            RevisionAuthor second = doc.Revisions.CreateAuthor("Second", s_date.AddHours(2), ids);
            first.InsertText(P(doc, 0), 0, "one ");
            second.DeleteText(P(doc, 1), 0, 8);
            first.ReplaceText(P(doc, 2), 0, 5, "INDIA");
            second.FormatText(P(doc, 2), 6, 3, new RunPropertiesDelta { Bold = true });
            return doc;
        });
        yield return ("hub-footnote", () =>
        {
            Document doc = Base(Para("text with a note"));
            doc.Footnotes.Add("Original footnote text.").AppendReference(P(doc, 0));
            RevisionAuthor a = doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(500));
            a.ReplaceText(doc.Footnotes.Normal()[0].Paragraphs().First(), 9, 8, "changed");
            return doc;
        });
        yield return ("hub-header", () =>
        {
            Document doc = Base(Para("body text"));
            HeaderFooter header = doc.Sections[0].AddOwn(HeaderFooterKind.Header, HeaderFooterType.Default);
            header.Paragraphs().First().SetText("running header here");
            RevisionAuthor a = doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(600));
            a.InsertText(header.Paragraphs().First(), 8, "NEW ");
            return doc;
        });
        yield return ("hub-comment", () =>
        {
            Document doc = Base(Para("commented text"));
            Run r = P(doc, 0).Runs().First();
            doc.Comments.Add(r, r, "Ann", "Original comment words", "A", s_date);
            RevisionAuthor a = doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(700));
            a.ReplaceText(doc.Comments[0].Paragraphs().First(), 9, 7, "reply");
            return doc;
        });
        foreach ((string name, Func<Document> build) in GridCases())
        {
            yield return (name, build);
        }

        foreach ((string name, Func<Document> build) in ReportedCases())
        {
            yield return (name, build);
        }
    }

    // Shapes a tracked-changes writer produced: a paragraph mark revision beside a property change (loaded, not authored), prefixes declared
    // on the paragraph that is joined or copied, note ids after separators, and wrappers whose only content is a tracked change.
    private static IEnumerable<(string Name, Func<Document> Build)> ReportedCases()
    {
        string auth = "w:author=\"Ann\" w:date=\"2024-03-01T10:00:00Z\"";
        string markPara(string mark) => "<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr>" + mark + "</w:rPr><w:pPrChange w:id=\"20\" " + auth + "><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr><w:r><w:t>marked</w:t></w:r></w:p>" + Para("next");
        foreach ((string kind, string mark) in new[] { ("ins", "<w:ins w:id=\"10\" " + auth + "/>"), ("del", "<w:del w:id=\"10\" " + auth + "/>") })
        {
            yield return ("loaded-mark-" + kind + "-pprchange", () => Base(markPara(mark)));
            yield return ("loaded-mark-" + kind + "-pprchange-cell", () => Base("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"4800\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4800\" w:type=\"dxa\"/></w:tcPr>" + markPara(mark) + "</w:tc></w:tr></w:tbl><w:p/>"));
        }

        const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string declared = "<w:p xmlns:r=\"" + R + "\"><w:r><w:t>Hello world</w:t></w:r><w:hyperlink w:anchor=\"_Top\"><w:r><w:t>link</w:t></w:r></w:hyperlink></w:p>" + Para("Other");
        yield return ("prefix-insert-paragraph", () => Edit(declared, (d, a) => a.InsertParagraph(P(d, 0), "added")));
        string declaredPlain = "<w:p xmlns:r=\"" + R + "\"><w:r><w:t>Hello world</w:t></w:r></w:p>" + Para("Other");
        yield return ("prefix-move-paragraph", () => Edit(declaredPlain, (d, a) => a.MoveParagraph(P(d, 0), P(d, 1), "prefix-move")));
        yield return ("prefix-join", () => Base("<w:p xmlns:r=\"" + R + "\"><w:pPr><w:rPr><w:ins w:id=\"1\" " + auth + "/></w:rPr></w:pPr><w:ins w:id=\"2\" " + auth + "><w:r><w:t>one</w:t></w:r></w:ins><w:hyperlink w:anchor=\"_Top\"><w:r><w:t xml:space=\"preserve\"> link</w:t></w:r></w:hyperlink></w:p>" + Para("two")));

        yield return ("note-ids", () =>
        {
            Document doc = Base(Para("first") + Para("second"));
            doc.Footnotes.Add("One.").AppendReference(P(doc, 0));
            doc.Footnotes.Add("Two.").AppendReference(P(doc, 1));
            doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(900)).InsertText(doc.Footnotes.Normal()[1].Paragraphs().First(), 3, "new ");
            return doc;
        });

        string[] wrappers =
        [
            "<w:smartTag w:uri=\"urn:test\" w:element=\"thing\">{0}</w:smartTag>",
            "<w:customXml w:uri=\"urn:test\" w:element=\"thing\">{0}</w:customXml>",
            "<w:hyperlink w:anchor=\"_Top\">{0}</w:hyperlink>",
            "<w:sdt><w:sdtPr><w:id w:val=\"101\"/><w:text/></w:sdtPr><w:sdtContent>{0}</w:sdtContent></w:sdt>",
        ];
        string[] names = ["smarttag", "customxml", "hyperlink", "sdt"];
        for (int i = 0; i < wrappers.Length; i++)
        {
            string w = wrappers[i];
            yield return ("wrapper-" + names[i] + "-ins", () => Base("<w:p><w:r><w:t xml:space=\"preserve\">before </w:t></w:r>" + string.Format(System.Globalization.CultureInfo.InvariantCulture, w, "<w:ins w:id=\"1\" " + auth + "><w:r><w:t>new</w:t></w:r></w:ins>") + "<w:r><w:t xml:space=\"preserve\"> after</w:t></w:r></w:p>"));
            yield return ("wrapper-" + names[i] + "-del", () => Base("<w:p><w:r><w:t xml:space=\"preserve\">before </w:t></w:r>" + string.Format(System.Globalization.CultureInfo.InvariantCulture, w, "<w:del w:id=\"1\" " + auth + "><w:r><w:delText>old</w:delText></w:r></w:del>") + "<w:r><w:t xml:space=\"preserve\"> after</w:t></w:r></w:p>"));
        }
    }

    // One tracked paragraph-properties change plus one other edit of the same paragraph, in both orders, in the body, in a table
    // cell and in a text box (a DrawingML box with its VML copy, and a plain VML box). Resolving all of them must leave nothing.
    private static readonly (string Name, Action<RevisionAuthor, Paragraph, Paragraph> Edit)[] GridEdits =
    [
        ("insert-paragraph", (a, p, _) => a.InsertParagraph(p, "added")),
        ("insert-text", (a, p, _) => a.InsertText(p, 5, ",")),
        ("delete-text", (a, p, _) => a.DeleteText(p, 0, 5)),
        ("replace-text", (a, p, _) => a.ReplaceText(p, 0, 5, "HELLO")),
        ("delete-paragraph", (a, p, _) => a.DeleteParagraph(p)),
        ("move-paragraph", (a, p, q) => a.MoveParagraph(p, q, null)),
        ("move-text", (a, p, q) => a.MoveText(p, 0, 5, q, 2, null)),
        ("format-text", (a, p, _) => a.FormatText(p, 0, 5, new RunPropertiesDelta { Bold = true })),
    ];

    private static IEnumerable<(string Name, Func<Document> Build)> GridCases()
    {
        string cell = "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"4800\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4800\" w:type=\"dxa\"/></w:tcPr>" +
            Para("Hello world", "<w:jc w:val=\"right\"/>") + Para("Other") + "</w:tc></w:tr></w:tbl><w:p/>";
        (string Place, string Body, Func<Document, IEnumerable<Paragraph>> Paragraphs)[] places =
        [
            ("body", Para("Hello world", "<w:jc w:val=\"right\"/>") + Para("Other"), d => d.Body.Paragraphs()),
            ("cell", cell, d => d.Body.Blocks().OfType<Table>().First().Rows().First().Cells().First().Paragraphs()),
            ("box-drawing", Program.BoxXml(true), d => d.TextBoxes()[0].Paragraphs()),
            ("box-vml", Program.BoxXml(false), d => d.TextBoxes()[0].Paragraphs()),
        ];
        foreach ((string place, string body, Func<Document, IEnumerable<Paragraph>> paragraphs) in places)
        {
            foreach ((string edit, Action<RevisionAuthor, Paragraph, Paragraph> apply) in GridEdits)
            {
                foreach (bool propertiesFirst in new[] { true, false })
                {
                    yield return ($"grid-{place}-{edit}-{(propertiesFirst ? "props-first" : "props-last")}", () =>
                    {
                        Document doc = WithCurrentCompatibilityMode(Base(body));
                        RevisionAuthor a = doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(100));
                        Paragraph[] ps = [.. paragraphs(doc)];
                        // The box content starts with "Inside the box"; the grid's offsets work on any text of 11+ characters.
                        var center = new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center };
                        if (propertiesFirst)
                        {
                            a.ChangeParagraphProperties(ps[0], center);
                        }

                        apply(a, ps[0], ps[1]);
                        if (!propertiesFirst)
                        {
                            a.ChangeParagraphProperties(ps[0], center);
                        }

                        return doc;
                    });
                }
            }
        }
    }

    private static Document Edit(string body, Action<Document, RevisionAuthor> edit)
    {
        Document doc = Base(body);
        edit(doc, doc.Revisions.CreateAuthor("Redliner", s_date, RevisionIds.StartingAt(100)));
        return doc;
    }

    internal static int Write(string dir)
    {
        Directory.CreateDirectory(dir);
        int failed = 0;
        foreach ((string name, Func<Document> build) in Cases())
        {
            try
            {
                using Document doc = build();
                doc.Revisions.Refresh();
                var tsv = new StringBuilder();
                foreach (IRevisionPart part in doc.Revisions.Parts)
                {
                    foreach (RevisionView view in new[] { RevisionView.Accepted, RevisionView.Rejected })
                    {
                        tsv.Append(view == RevisionView.Accepted ? "accepted" : "rejected").Append('\t').Append(part.Uri).Append('\t')
                           .Append(Flatten(RevisionSet.GetText(part, view))).Append('\n');
                    }
                }

                tsv.Append("count\t-\t").Append(doc.Revisions.Revisions.Count).Append('\n');
                tsv.Append("types\t-\t").Append(string.Join(',', doc.Revisions.Revisions.Select(r => WordType(r.Kind)).Distinct().OrderBy(x => x))).Append('\n');
                doc.CommitChanges();
                doc.SaveAs(Path.Combine(dir, name + ".docx"), OfficeFormat.Docx);
                File.WriteAllText(Path.Combine(dir, name + ".actions.txt"), "accept-all-stories\nreject-all-stories\n");
                Directory.CreateDirectory(Path.Combine(dir, "expected"));
                File.WriteAllText(Path.Combine(dir, "expected", name + ".expected.tsv"), tsv.ToString());
                Console.WriteLine("ok   " + name + " (" + doc.Revisions.Revisions.Count + " revisions)");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + " " + e.Message);
            }
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>Non-empty paragraphs joined by a pilcrow: row and cell end marks and empty paragraphs differ between readers and are not what is compared.</summary>
    private static string Flatten(string text) =>
        string.Join(Pilcrow, text.Replace("\\u00b6", "\n", StringComparison.Ordinal).Replace("\u0002", string.Empty, StringComparison.Ordinal).Split('\n', '\r', '\u00b6', '\a').Where(s => s.Length > 0));

    internal static int Check(string dir, string results)
    {
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string jsonl = Path.Combine(results, "results.jsonl");
        foreach (string line in File.ReadLines(jsonl))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            JsonElement e = JsonDocument.Parse(line).RootElement;
            if (e.TryGetProperty("kind", out JsonElement k) && k.GetString() == "file" && e.TryGetProperty("file", out JsonElement f))
            {
                string full = f.GetString()!;
                entries[Path.GetFileNameWithoutExtension(full[(full.LastIndexOf('\\') + 1)..])] = e;
            }
        }

        int fails = 0;
        int checkedCount = 0;
        foreach (string expectedFile in Directory.GetFiles(Path.Combine(dir, "expected"), "*.expected.tsv").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(expectedFile)[..^".expected.tsv".Length];
            var expected = new Dictionary<(string View, string Uri), string>();
            foreach (string line in File.ReadAllLines(expectedFile))
            {
                string[] f = line.Split('\t');
                if (f.Length == 3)
                {
                    expected[(f[0], f[1])] = f[2];
                }
            }

            if (!entries.TryGetValue(name, out JsonElement entry))
            {
                Console.WriteLine("MISSING " + name);
                fails++;
                continue;
            }

            string outcome = entry.TryGetProperty("outcome", out JsonElement o) ? o.GetString() ?? "" : "";
            var problems = new List<string>();
            if (outcome != "clean")
            {
                problems.Add("outcome " + outcome);
            }

            // The application's own list of the file as authored (every story, text boxes included) against the revisions we list. It
            // joins neighbouring revisions of one kind (an inserted run and the inserted paragraph mark after it are one revision), so
            // its count may be lower than ours (and it reports a paragraph property change as part of a neighbouring revision), but never higher.
            if (expected.TryGetValue(("count", "-"), out string? ours) && entry.TryGetProperty("stories", out JsonElement storyList))
            {
                int wordCount = 0;
                var wordTypes = new SortedSet<int>();
                foreach (JsonElement story in storyList.EnumerateArray())
                {
                    if (story.TryGetProperty("revisions", out JsonElement srevs) && srevs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement rv in srevs.EnumerateArray())
                        {
                            wordCount++;
                            wordTypes.Add(rv.GetProperty("type").GetInt32());
                        }
                    }
                }

                string oursTypes = expected.GetValueOrDefault(("types", "-")) ?? string.Empty;
                string theirs = string.Join(',', wordTypes);
                Console.WriteLine("     " + name + " revisions: word " + wordCount + " (types " + theirs + ") ours " + ours + " (types " + oursTypes + ")");
                if (wordCount > int.Parse(ours, System.Globalization.CultureInfo.InvariantCulture))
                {
                    problems.Add("revision count: word " + wordCount + " ours " + ours);
                }
            }

            if (entry.TryGetProperty("actions", out JsonElement actions))
            {
                foreach (JsonElement act in actions.EnumerateArray())
                {
                    string action = act.GetProperty("action").GetString()!;
                    string view = action == "accept-all-stories" ? "accepted" : "rejected";
                    if (act.TryGetProperty("error", out JsonElement err))
                    {
                        problems.Add(action + " error " + err.GetString());
                        continue;
                    }

                    string word = Flatten(act.GetProperty("text").GetString() ?? string.Empty);
                    string main = expected.FirstOrDefault(kv => kv.Key.View == view && kv.Key.Uri == "/word/document.xml").Value ?? string.Empty;
                    if (word != main)
                    {
                        problems.Add(action + " main text: word '" + word + "' ours '" + main + "'");
                    }

                    if (act.TryGetProperty("revisionsLeft", out JsonElement left) && left.GetInt32() != 0)
                    {
                        problems.Add(action + " left " + left.GetInt32() + " revisions in the main story");
                    }

                    if (act.TryGetProperty("storyRevisionsLeft", out JsonElement storyLeft) && storyLeft.GetInt32() != 0)
                    {
                        problems.Add(action + " left " + storyLeft.GetInt32() + " revisions in the stories");
                    }

                    // The other parts: read the document the application saved after the action.
                    if (act.TryGetProperty("saved", out JsonElement saved) && File.Exists(Path.Combine(results, saved.GetString()!)))
                    {
                        using Document d = Document.Open(Path.Combine(results, saved.GetString()!));
                        var byUri = d.Revisions.Parts.ToDictionary(p => p.Uri, p => Flatten(RevisionSet.GetText(p, RevisionView.Accepted)), StringComparer.OrdinalIgnoreCase);
                        foreach (((string v, string uri), string text) in expected)
                        {
                            if (v != view || uri == "/word/styles.xml" || uri == "/word/numbering.xml")
                            {
                                continue;
                            }

                            if (!byUri.TryGetValue(uri, out string? got))
                            {
                                if (text.Length > 0)
                                {
                                    problems.Add(action + " " + uri + " missing in the saved file");
                                }
                            }
                            else if (got != text)
                            {
                                problems.Add(action + " " + uri + ": word '" + got + "' ours '" + text + "'");
                            }
                        }
                    }
                    else
                    {
                        problems.Add(action + " saved file not found");
                    }
                }
            }
            else
            {
                problems.Add("no action results");
            }

            checkedCount++;
            if (problems.Count == 0)
            {
                Console.WriteLine("ok   " + name);
            }
            else
            {
                fails++;
                Console.WriteLine("DIFF " + name);
                foreach (string p in problems)
                {
                    Console.WriteLine("       " + p);
                }
            }
        }

        Console.WriteLine(checkedCount + " documents checked, " + fails + " with differences");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Writes OneOffice's own accepted and rejected documents beside the authored ones, for the application to read the same way it reads its own results.</summary>
    internal static int WriteResolved(string dir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach ((string name, Func<Document> build) in Cases())
        {
            foreach (bool accept in new[] { true, false })
            {
                using Document doc = build();
                _ = accept ? doc.Revisions.AcceptAll() : doc.Revisions.RejectAll();
                doc.CommitChanges();
                doc.SaveAs(Path.Combine(outDir, name + (accept ? ".accepted" : ".rejected") + ".docx"), OfficeFormat.Docx);
            }
        }

        return 0;
    }

    /// <summary>Compares the application's probe of the documents it resolved itself (<c>NAME.accept-all-stories.docx</c>) with its probe of ours (<c>NAME.accepted.docx</c>): paragraph formatting, list labels and table cells.</summary>
    internal static int CheckProbe(string results)
    {
        var byFile = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(results, "results.jsonl")))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            JsonElement e = JsonDocument.Parse(line).RootElement;
            if (e.TryGetProperty("kind", out JsonElement k) && k.GetString() == "file" && e.TryGetProperty("file", out JsonElement f))
            {
                string full = f.GetString()!;
                byFile[full[(full.LastIndexOf('\\') + 1)..]] = e;
            }
        }

        int fails = 0;
        int pairs = 0;
        foreach (string wordFile in byFile.Keys.Where(n => n.Contains(".accept-all-stories.", StringComparison.Ordinal) || n.Contains(".reject-all-stories.", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal))
        {
            bool accept = wordFile.Contains(".accept-all-stories.", StringComparison.Ordinal);
            string name = wordFile[..wordFile.IndexOf(accept ? ".accept-all-stories." : ".reject-all-stories.", StringComparison.Ordinal)];
            string ours = name + (accept ? ".accepted.docx" : ".rejected.docx");
            if (!byFile.TryGetValue(ours, out JsonElement ourEntry))
            {
                Console.WriteLine("MISSING " + ours);
                fails++;
                continue;
            }

            pairs++;
            var problems = new List<string>();
            JsonElement w = byFile[wordFile];
            foreach (string field in new[] { "paragraphInfo", "tables", "revisions" })
            {
                string a = Canonical(w, field);
                string b = Canonical(ourEntry, field);
                if (a != b)
                {
                    problems.Add(field + ":\n         word " + a + "\n         ours " + b);
                }
            }

            if (problems.Count == 0)
            {
                Console.WriteLine("ok   " + name + (accept ? " accepted" : " rejected"));
            }
            else
            {
                fails++;
                Console.WriteLine("DIFF " + name + (accept ? " accepted" : " rejected"));
                foreach (string p in problems)
                {
                    Console.WriteLine("       " + p);
                }
            }
        }

        Console.WriteLine(pairs + " pairs compared, " + fails + " with differences");
        return fails == 0 ? 0 : 1;
    }

    private static string Canonical(JsonElement entry, string field)
    {
        if (!entry.TryGetProperty(field, out JsonElement v))
        {
            return "(none)";
        }

        return Drop(v);
    }

    // Page numbers depend on the file's own layout pass and are not what is compared.
    private static string Drop(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                return "{" + string.Join(",", e.EnumerateObject().Where(p => p.Name is not ("page" or "physicalPage")).OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "=" + Drop(p.Value))) + "}";
            case JsonValueKind.Array:
                return "[" + string.Join(",", e.EnumerateArray().Select(Drop)) + "]";
            default:
                return e.ToString();
        }
    }

    /// <summary>
    /// Compares table, row and cell properties (not widths, which an autofit table recomputes whenever the application lays it out) of the
    /// documents the application resolved (<c>NAME.accept-all-stories.docx</c> in <paramref name="wordDir"/>) with ours (<c>NAME.accepted.docx</c> in <paramref name="ourDir"/>).
    /// </summary>
    internal static int CheckTableProperties(string wordDir, string ourDir)
    {
        int fails = 0;
        int pairs = 0;
        foreach (string wordFile in Directory.GetFiles(wordDir, "*-stories.docx").OrderBy(x => x, StringComparer.Ordinal))
        {
            string file = Path.GetFileName(wordFile);
            bool accept = file.Contains(".accept-all-stories.", StringComparison.Ordinal);
            string name = file[..file.IndexOf(accept ? ".accept-all-stories." : ".reject-all-stories.", StringComparison.Ordinal)];
            string ours = Path.Combine(ourDir, name + (accept ? ".accepted.docx" : ".rejected.docx"));
            if (!name.Contains("table", StringComparison.Ordinal) || !File.Exists(ours))
            {
                continue;
            }

            pairs++;
            string a = TableSignature(wordFile);
            string b = TableSignature(ours);
            if (a == b)
            {
                Console.WriteLine("ok   " + name + (accept ? " accepted" : " rejected") + "  " + a);
            }
            else
            {
                fails++;
                Console.WriteLine("DIFF " + name + (accept ? " accepted" : " rejected") + "\n       word " + a + "\n       ours " + b);
            }
        }

        Console.WriteLine(pairs + " pairs compared, " + fails + " with differences");
        return fails == 0 ? 0 : 1;
    }

    private static string TableSignature(string path)
    {
        using Document d = Document.Open(path);
        var sb = new StringBuilder();
        foreach (Table t in d.Body.Blocks().OfType<Table>())
        {
            sb.Append("T[").Append(t.Width is { } w ? w.Type + w.Value : "-").Append('/').Append(t.Justification ?? "-").Append('/').Append(t.IsFixedLayout ? "fixed" : "auto").Append("; ");
            foreach (TableRow r in t.Rows())
            {
                sb.Append("R(").Append(r.Height?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-").Append(r.IsHeader ? ",hdr" : "").Append(r.CannotSplit ? ",nosplit" : "").Append(':');
                foreach (TableCell c in r.Cells())
                {
                    string fill = "-";
                    if (c.PropertiesElement is { } pe)
                    {
                        foreach (var ch in pe.Node.ChildElements)
                        {
                            if (ch.LocalNameString == "shd")
                            {
                                fill = ch.GetAttribute("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "fill") ?? "-";
                            }
                        }
                    }

                    sb.Append('{').Append(c.Text).Append('|').Append(c.VerticalAlignment ?? "-").Append('|').Append(fill).Append('}');
                }

                sb.Append(')');
            }

            sb.Append(']');
        }

        return sb.ToString();
    }
}
