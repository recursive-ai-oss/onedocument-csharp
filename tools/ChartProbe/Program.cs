using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Charts;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tools.ChartProbe;

/// <summary>
/// Chart tooling.
/// <c>ChartProbe gen DIR</c> writes one workbook per generated chart sample and per axis-scaling case, with <c>scaling-cases.json</c>
/// listing our automatic axis scale next to an empty slot for the answer from the desktop application.
/// <c>ChartProbe sweep INPUT OUTDIR [--render N] [--shard i/n]</c> reads every chart part of every document under INPUT, counts chart types,
/// writes <c>charts-S.jsonl</c> (one line per file) and optionally renders up to N charts as PNG into OUTDIR.
/// </summary>
internal static class Program
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "gen")
        {
            return Gen(args[1]);
        }

        if (args.Length >= 3 && args[0] == "sweep")
        {
            return Sweep(args);
        }

        Console.Error.WriteLine("usage: ChartProbe gen DIR | ChartProbe sweep INPUT OUTDIR [--render N] [--shard i/n]");
        return 2;
    }

    private static int Gen(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (ChartSamples.Sample s in ChartSamples.All())
        {
            File.WriteAllBytes(Path.Combine(dir, "chart-" + s.Name + ".xlsx"), Workbook(s.Xml, s.IsExtended));
        }

        using var json = new MemoryStream();
        using (var w = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartArray();
            foreach ((string name, double[] values) in ChartSamples.ScalingCases())
            {
                File.WriteAllBytes(Path.Combine(dir, "scaling-" + name + ".xlsx"), Workbook(ChartSamples.ScalingChart(values), false, values));
                AxisScale s = AxisScaler.Compute(new ChartAxis(), values.Min(), values.Max());
                w.WriteStartObject();
                w.WriteString("case", name);
                w.WriteString("file", "scaling-" + name + ".xlsx");
                w.WriteStartArray("values");
                foreach (double v in values)
                {
                    w.WriteNumberValue(v);
                }

                w.WriteEndArray();
                w.WriteStartObject("ours");
                w.WriteNumber("min", s.Min);
                w.WriteNumber("max", s.Max);
                w.WriteNumber("major", s.MajorUnit);
                w.WriteEndObject();
                w.WriteNull("excel");
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        File.WriteAllBytes(Path.Combine(dir, "scaling-cases.json"), json.ToArray());
        Console.WriteLine($"wrote {ChartSamples.All().Count} chart workbooks and {ChartSamples.ScalingCases().Count} scaling workbooks to {dir}");
        return 0;
    }

    private static byte[] Workbook(string chartXml, bool extended, double[]? values = null)
    {
        using OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "/officeDocument", "/xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"{Rel}\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", Rel + "/worksheet", "/xl/worksheets/sheet1.xml")]);
        var sheet = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"{Rel}\"><sheetData>");
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"];
        double[] data = values ?? [12, 19, 7, 15, 22, 9];
        sheet.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Category</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Values</t></is></c></row>");
        for (int i = 0; i < data.Length; i++)
        {
            string cat = values is null ? months[i % months.Length] : "C" + (i + 1);
            sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{i + 2}\"><c r=\"A{i + 2}\" t=\"inlineStr\"><is><t>{cat}</t></is></c><c r=\"B{i + 2}\"><v>{data[i].ToString("R", CultureInfo.InvariantCulture)}</v></c></row>");
        }

        sheet.Append("</sheetData><drawing r:id=\"rId1\"/></worksheet>");
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(sheet.ToString()));
        p.SetRelationships("/xl/worksheets/sheet1.xml", [new Relationship("rId1", Rel + "/drawing", "/xl/drawings/drawing1.xml")]);
        string frame = extended
            ? "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice xmlns:cx1=\"http://schemas.microsoft.com/office/drawing/2015/9/8/chartex\" Requires=\"cx1\">" + Anchor("<a:graphicData uri=\"http://schemas.microsoft.com/office/drawing/2014/chartex\"><cx:chart xmlns:cx=\"http://schemas.microsoft.com/office/drawing/2014/chartex\" r:id=\"rId1\"/></a:graphicData>") + "</mc:Choice><mc:Fallback><xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"3\" name=\"Chart\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1\" cy=\"1\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp></mc:Fallback></mc:AlternateContent>"
            : Anchor("<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId1\"/></a:graphicData>");
        p.AddPart("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"{Rel}\">{frame}</xdr:wsDr>"));
        p.SetRelationships("/xl/drawings/drawing1.xml", [new Relationship("rId1", extended ? "http://schemas.microsoft.com/office/2014/relationships/chartEx" : Rel + "/chart", extended ? "/xl/charts/chartEx1.xml" : "/xl/charts/chart1.xml")]);
        p.AddPart(extended ? "/xl/charts/chartEx1.xml" : "/xl/charts/chart1.xml", extended ? "application/vnd.ms-office.chartex+xml" : "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", Encoding.UTF8.GetBytes(chartXml));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();

        static string Anchor(string graphicData) =>
            "<xdr:twoCellAnchor><xdr:from><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>13</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>21</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
            "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic>" + graphicData + "</a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>";
    }

    private static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", < ' ' => " ", _ => c.ToString() });
        }

        return sb.Append('"').ToString();
    }

    private static int Sweep(string[] args)
    {
        string input = args[1];
        string output = args[2];
        int render = 0;
        int shard = 0;
        int shards = 1;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--render" && i + 1 < args.Length)
            {
                render = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--shard" && i + 1 < args.Length)
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0], CultureInfo.InvariantCulture);
                shards = int.Parse(p[1], CultureInfo.InvariantCulture);
            }
        }

        Directory.CreateDirectory(output);
        var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var noteCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int files = 0, withCharts = 0, charts = 0, failures = 0, identical = 0, rendered = 0, renderFailures = 0;
        string[] all = [.. Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".docm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
        using var log = new StreamWriter(Path.Combine(output, $"charts-{shard}.jsonl"));
        for (int fi = shard; fi < all.Length; fi += shards)
        {
            string file = all[fi];
            files++;
            try
            {
                using OpcPackage pkg = OpcPackage.Open(file);
                IReadOnlyList<string> parts = ChartHost.FindChartParts(pkg);
                if (parts.Count == 0)
                {
                    continue;
                }

                withCharts++;
                foreach (string part in parts)
                {
                    charts++;
                    string kind = "unreadable";
                    string error = string.Empty;
                    int notes = 0;
                    bool same = false;
                    try
                    {
                        ReadOnlyMemory<byte> bytes = pkg.FindPart(part)!.GetBytes();
                        ChartSpace c = ChartReader.Read(bytes, part);
                        kind = string.Join("+", c.Groups.Select(g => g.Type.ToString()).Distinct());
                        if (kind.Length == 0)
                        {
                            kind = "empty";
                        }

                        notes = c.Notes.Count;
                        foreach (ChartNote n in c.Notes)
                        {
                            noteCodes[n.Code] = noteCodes.GetValueOrDefault(n.Code) + 1;
                        }

                        same = c.SourceBytes.Span.SequenceEqual(bytes.Span);
                        if (same)
                        {
                            identical++;
                        }

                        if (rendered < render)
                        {
                            try
                            {
                                File.WriteAllBytes(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(file)}-{Path.GetFileName(part)}.png"), ChartHost.RenderPng(c, 480, 288));
                                rendered++;
                            }
                            catch (Exception ex)
                            {
                                renderFailures++;
                                error = "render: " + ex.Message;
                            }
                        }
                    }
                    catch (OfficeException ex)
                    {
                        failures++;
                        error = ex.Defect.Code + ": " + ex.Message;
                    }

                    types[kind] = types.GetValueOrDefault(kind) + 1;
                    log.WriteLine($"{{\"file\":{Q(Path.GetFileName(file))},\"part\":{Q(part)},\"types\":{Q(kind)},\"notes\":{notes},\"identical\":{(same ? "true" : "false")},\"error\":{Q(error)}}}");
                }
            }
            catch (OfficeException ex)
            {
                failures++;
                log.WriteLine($"{{\"file\":{Q(Path.GetFileName(file))},\"error\":{Q(ex.Defect.Code)}}}");
            }
        }

        Console.WriteLine($"files {files}, with charts {withCharts}, chart parts {charts}, read failures {failures}, bytes kept {identical}, rendered {rendered}, render failures {renderFailures}");
        foreach ((string k, int v) in types)
        {
            Console.WriteLine($"  {k}: {v}");
        }

        foreach ((string k, int v) in noteCodes)
        {
            Console.WriteLine($"  note {k}: {v}");
        }

        return 0;
    }
}
