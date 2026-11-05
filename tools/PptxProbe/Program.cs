using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Tools.PptxProbe;

/// <summary>
/// Opens every presentation under a directory with <c>Presentation.Open</c>, walks the whole hub, saves it untouched and compares
/// every part byte for byte with the input, then applies one edit per kind (paragraph text, table cell, duplicate, add, move, delete)
/// and lists the parts each edit changed. Writes <c>sweep-N.jsonl</c> (one line per file) and the saved files.
/// Usage: PptxProbe sweep &lt;inputDir&gt; &lt;outDir&gt; [--shard i/n] [--limit N]. Exit 0: the findings are in the lines.
/// </summary>
internal static class Program
{
    private static readonly string[] s_presentationParts = ["[Content_Types].xml", "ppt/presentation.xml", "ppt/_rels/presentation.xml.rels"];

    private static int Main(string[] args)
    {
        if (args.Length < 3 || args[0] != "sweep")
        {
            Console.Error.WriteLine("usage: PptxProbe sweep <inputDir> <outDir> [--shard i/n] [--limit N]");
            return 2;
        }

        string input = args[1];
        string output = args[2];
        int shard = 0, shards = 1, limit = int.MaxValue;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--shard" && i + 1 < args.Length)
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
            else if (args[i] == "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i]);
            }
        }

        Directory.CreateDirectory(Path.Combine(output, "untouched"));
        Directory.CreateDirectory(Path.Combine(output, "edited"));
        string[] all = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".potx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ppsx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        using var log = new StreamWriter(Path.Combine(output, "sweep-" + shard + ".jsonl"));
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long parts = 0, identical = 0;
        for (int index = 0; index < all.Length; index++)
        {
            if (index % shards != shard || counts.Values.Sum() >= limit)
            {
                continue;
            }

            string file = all[index];
            string id = index.ToString("D5");
            var line = new Dictionary<string, object?> { ["kind"] = "file", ["id"] = id, ["file"] = Path.GetRelativePath(input, file) };
            var sw = Stopwatch.StartNew();
            try
            {
                Run(file, id, output, line);
                line["status"] = line.TryGetValue("untouchedDiff", out object? d) && d is int diff && diff > 0 ? "untouched-diff" : "ok";
                parts += (int)line["untouchedParts"]!;
                identical += (int)line["untouchedParts"]! - (int)line["untouchedDiff"]!;
            }
            catch (OfficeException ex)
            {
                line["status"] = "typed-error";
                line["errorClass"] = ex.GetType().Name;
                line["errorCode"] = ex.Defect.Code;
                line["location"] = ex.Location.ToString();
            }
            catch (IOException ex)
            {
                line["status"] = "typed-error";
                line["errorClass"] = ex.GetType().Name;
                line["errorCode"] = "io";
            }
            catch (Exception ex)
            {
                // An exception that is not an OfficeException leaving the public API is a bug in the library.
                line["status"] = "UNTYPED-EXCEPTION";
                line["error"] = ex.GetType().Name + ": " + (ex.Message.Length > 200 ? ex.Message[..200] : ex.Message);
            }

            line["ms"] = sw.ElapsedMilliseconds;
            string status = (string)line["status"]!;
            counts[status] = counts.GetValueOrDefault(status) + 1;
            log.WriteLine(JsonSerializer.Serialize(line));
            log.Flush();
        }

        log.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["kind"] = "summary", ["statuses"] = counts, ["untouchedParts"] = parts, ["untouchedIdentical"] = identical }));
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["kind"] = "summary", ["shard"] = shard, ["statuses"] = counts, ["untouchedParts"] = parts, ["untouchedIdentical"] = identical }));
        return 0;
    }

    private static void Run(string file, string id, string output, Dictionary<string, object?> line)
    {
        byte[] source = File.ReadAllBytes(file);
        SortedDictionary<string, string> before = Hashes(source);

        // Untouched: read everything the hub offers, then save.
        using (Presentation deck = Presentation.Open(new MemoryStream(source)))
        {
            Stats stats = Walk(deck);
            line["format"] = deck.Format.ToString();
            line["slides"] = stats.Slides;
            line["shapes"] = stats.Shapes;
            line["placeholders"] = stats.Placeholders;
            line["tables"] = stats.Tables;
            line["notes"] = stats.Notes;
            line["comments"] = stats.Comments;
            line["sections"] = deck.Sections.Count;
            line["defects"] = deck.Defects.Count;
            using var saved = new MemoryStream();
            deck.SaveAs(saved, deck.Format);
            File.WriteAllBytes(Path.Combine(output, "untouched", id + ".pptx"), saved.ToArray());
            SortedDictionary<string, string> after = Hashes(saved.ToArray());
            List<string> diff = Diff(before, after);
            line["untouchedParts"] = before.Count;
            line["untouchedDiff"] = diff.Count;
            if (diff.Count > 0)
            {
                line["firstDiff"] = diff[0];
            }

            line["modelText"] = deck.ExtractText().Length;
        }

        var changed = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var unexpected = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        Edit("text", source, before, id, output, changed, unexpected, deck =>
        {
            foreach (Slide slide in deck.Slides)
            {
                foreach (Shape shape in slide.AllShapes())
                {
                    if (shape.TextBody is { Paragraphs.Count: > 0 } body && body.Paragraphs[0].Text.Length > 0)
                    {
                        body.Paragraphs[0].SetText(body.Paragraphs[0].Text + " (edited)");
                        return new[] { slide.PartName.TrimStart('/') };
                    }
                }
            }

            return null;
        });
        Edit("cell", source, before, id, output, changed, unexpected, deck =>
        {
            foreach (Slide slide in deck.Slides)
            {
                foreach (Shape shape in slide.AllShapes())
                {
                    if (shape.Table is { RowCount: > 0 } table && table.Rows[0].Cells.Count > 0 && !table.Rows[0].Cells[0].IsMergedContinuation)
                    {
                        table.Rows[0].Cells[0].SetText(table.Rows[0].Cells[0].Text + " (edited)");
                        return new[] { slide.PartName.TrimStart('/') };
                    }
                }
            }

            return null;
        });
        Edit("dup", source, before, id, output, changed, unexpected, deck =>
        {
            if (deck.Slides.Count == 0) { return null; }
            deck.DuplicateSlide(deck.Slides[deck.Slides.Count / 2]);
            return s_presentationParts;
        }, allowAdded: true);
        Edit("add", source, before, id, output, changed, unexpected, deck =>
        {
            if (deck.Masters.Count == 0 || deck.Masters[0].Layouts.Count == 0) { return null; }
            deck.AddSlide(deck.Masters[0].Layouts[0], deck.Slides.Count > 1 ? 1 : null);
            return s_presentationParts;
        }, allowAdded: true);
        Edit("move", source, before, id, output, changed, unexpected, deck =>
        {
            if (deck.Slides.Count < 2) { return null; }
            deck.MoveSlide(deck.Slides[^1], 0);
            return ["ppt/presentation.xml"];
        });
        Edit("delete", source, before, id, output, changed, unexpected, deck =>
        {
            if (deck.Slides.Count < 2) { return null; }
            deck.DeleteSlide(deck.Slides[deck.Slides.Count / 2]);
            return s_presentationParts;
        }, allowAdded: false, allowRemoved: true);
        line["changed"] = changed;
        if (unexpected.Count > 0)
        {
            line["unexpected"] = unexpected;
        }
    }

    // Runs one edit on a fresh copy, saves it, and lists the parts that changed against the input. 'allowed' names the parts the edit
    // may change; added (and, for delete, removed) parts are accepted only when the edit says so.
    private static void Edit(string op, byte[] source, SortedDictionary<string, string> before, string id, string output,
        SortedDictionary<string, List<string>> changed, SortedDictionary<string, List<string>> unexpected,
        Func<Presentation, string[]?> edit, bool allowAdded = false, bool allowRemoved = false)
    {
        using Presentation deck = Presentation.Open(new MemoryStream(source));
        string[]? allowed = edit(deck);
        if (allowed is null)
        {
            return;
        }

        using var saved = new MemoryStream();
        deck.SaveAs(saved, deck.Format);
        File.WriteAllBytes(Path.Combine(output, "edited", id + "." + op + ".pptx"), saved.ToArray());
        SortedDictionary<string, string> after = Hashes(saved.ToArray());
        var changes = new List<string>();
        var bad = new List<string>();
        foreach (string name in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            bool inBefore = before.TryGetValue(name, out string? h1);
            bool inAfter = after.TryGetValue(name, out string? h2);
            if (inBefore && inAfter && h1 == h2)
            {
                continue;
            }

            string kind = !inBefore ? "+" : !inAfter ? "-" : "~";
            changes.Add(kind + name);
            bool ok = kind switch
            {
                "+" => allowAdded,
                "-" => allowRemoved,
                _ => Array.IndexOf(allowed, name) >= 0,
            };
            if (!ok)
            {
                bad.Add(kind + name);
            }
        }

        changed[op] = changes;
        if (bad.Count > 0)
        {
            unexpected[op] = bad;
        }
    }

    private readonly record struct Stats(int Slides, int Shapes, int Placeholders, int Tables, int Notes, int Comments);

    // Reads everything the hub exposes, so a damaged part or an inconsistent chain shows up as a defect or an exception.
    private static Stats Walk(Presentation deck)
    {
        int slides = 0, shapes = 0, placeholders = 0, tables = 0, notes = 0, comments = 0;
        _ = deck.SlideSize;
        _ = deck.CustomShows;
        _ = deck.CommentAuthors;
        foreach (SlideMaster master in deck.Masters)
        {
            _ = master.Theme;
            foreach (SlideLayout layout in master.Layouts)
            {
                foreach (Shape s in layout.AllShapes())
                {
                    _ = s.InheritanceChain;
                }
            }
        }

        foreach (Slide slide in deck.Slides)
        {
            slides++;
            _ = slide.Title;
            _ = slide.ColorMap;
            _ = slide.HasTransition;
            if (slide.Notes is { } n)
            {
                notes++;
                _ = n.NotesText;
            }

            comments += slide.Comments.Count;
            foreach (Shape shape in slide.AllShapes())
            {
                shapes++;
                _ = shape.References;
                _ = shape.ResolveBounds();
                if (shape.Placeholder is not null)
                {
                    placeholders++;
                    _ = shape.ResolveBodyFormat();
                }

                if (shape.TextBody is { } body)
                {
                    foreach (TextParagraph p in body.Paragraphs)
                    {
                        ParagraphFormat f = p.ResolveFormat();
                        _ = slide.ResolveColor(f.Run.Color);
                        _ = p.Text;
                    }
                }

                if (shape.Table is { } table)
                {
                    tables++;
                    foreach (TableRow row in table.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                        {
                            _ = cell.Origin;
                        }
                    }
                }
            }
        }

        return new Stats(slides, shapes, placeholders, tables, notes, comments);
    }

    private static SortedDictionary<string, string> Hashes(byte[] zip)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in archive.Entries)
        {
            if (e.FullName.EndsWith('/'))
            {
                continue;
            }

            using Stream s = e.Open();
            map[e.FullName] = Convert.ToHexString(SHA256.HashData(s));
        }

        return map;
    }

    private static List<string> Diff(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        var list = new List<string>();
        foreach (string name in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
        {
            if (!a.TryGetValue(name, out string? x) || !b.TryGetValue(name, out string? y) || x != y)
            {
                list.Add(name);
            }
        }

        return list;
    }
}
