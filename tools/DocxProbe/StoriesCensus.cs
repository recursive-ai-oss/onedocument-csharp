using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tools.DocxProbe;

internal static partial class Program
{
    /// <summary>
    /// <c>DocxProbe stories-census FILE...</c>: per file, the text boxes by kind with their paragraph counts, linked chains and boxes
    /// that have a copy in the other <c>mc:AlternateContent</c> branch; the numbered paragraphs per story kind; the core, extended and
    /// custom properties found. One JSON line each; typed failures are reported by code, anything else is an unexpected exception.
    /// Exit 1 when a file raised an exception that is not an <see cref="OfficeException"/>.
    /// </summary>
    private static int StoriesCensus(string[] files)
    {
        int unexpected = 0;
        foreach (string file in files)
        {
            var defects = new DefectCollection();
            var line = new Dictionary<string, object?> { ["file"] = Path.GetFileName(file) };
            try
            {
                var context = new OpenContext(defects: defects);
                using OpcPackage package = OpcPackage.Open(file, context);
                using Document doc = Document.FromPackage(package, context);
                IReadOnlyList<TextBox> boxes = doc.TextBoxes();
                line["boxes"] = boxes.Count;
                line["drawingBoxes"] = boxes.Count(b => b.Kind == TextBoxKind.DrawingMl);
                line["vmlBoxes"] = boxes.Count(b => b.Kind == TextBoxKind.Vml);
                line["withAlternate"] = boxes.Count(b => b.Alternate.IsValid);
                line["boxParagraphs"] = boxes.Sum(b => b.Paragraphs().Count());
                line["anchored"] = boxes.Count(b => b.Anchor is not null);
                line["chains"] = boxes.Count(b => b.Chain.Count > 1);
                line["nestedBoxes"] = boxes.Sum(b => b.TextBoxes().Count);
                var byHost = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (TextBox b in boxes)
                {
                    byHost[b.HostStory.ToString()] = byHost.GetValueOrDefault(b.HostStory.ToString()) + 1;
                }

                line["boxesByHost"] = byHost;
                var labels = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (StoryLabels s in doc.Numbering.LabelStories())
                {
                    labels[s.Kind.ToString()] = labels.GetValueOrDefault(s.Kind.ToString()) + s.Labels.Count;
                }

                line["labelsByStory"] = labels;
                CoreInfo(doc, line);
            }
            catch (OfficeException ex)
            {
                line["error"] = ex.Defect.Code;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                line["unexpected"] = ex.GetType().Name + ": " + ex.Message;
                unexpected++;
            }

            line["defects"] = defects.Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(line));
        }

        return unexpected == 0 ? 0 : 1;
    }

    private static void CoreInfo(Document doc, Dictionary<string, object?> line)
    {
        RecursiveAI.OneDocument.Properties.CoreProperties c = doc.Properties.Core;
        int core = new object?[] { c.Title, c.Subject, c.Creator, c.Keywords, c.Description, c.LastModifiedBy, c.Revision, c.Category, c.ContentStatus, c.Language, c.Identifier, c.Version, c.Created, c.Modified, c.LastPrinted }.Count(v => v is not null);
        line["coreProperties"] = core;
        line["appProperties"] = new object?[] { doc.Properties.Extended.Application, doc.Properties.Extended.Company, doc.Properties.Extended.Pages, doc.Properties.Extended.Words }.Count(v => v is not null);
        IReadOnlyList<RecursiveAI.OneDocument.Properties.CustomProperty> custom = doc.Properties.Custom.List();
        line["customProperties"] = custom.Count;
        line["customTypes"] = custom.GroupBy(p => p.Type.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        line["createdParsed"] = c.Created is not null;
    }
}
