using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The template parser and renderer. The first byte picks strict mode and the delimiters; the rest is the template text, one unit per line and
/// one segment per 0x01 byte (so tags are split across segments). A template either parses, or every fatal defect is a located <c>template.</c>
/// or <c>formula.</c> error inside the unit's text; a template that parses renders against fixed data with small limits, deterministically,
/// and only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class TemplateTarget
{
    private static readonly ITemplateData Data = TemplateData.From(new Dictionary<string, object?>
    {
        ["name"] = "Ada",
        ["n"] = 3,
        ["items"] = new List<object?>
        {
            new Dictionary<string, object?> { ["price"] = 1.5, ["qty"] = 2, ["tags"] = new List<object?> { "a", "b" } },
            new Dictionary<string, object?> { ["price"] = 10, ["qty"] = 1, ["tags"] = new List<object?>() },
        },
    });

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        byte mode = data[0];
        string text = Encoding.UTF8.GetString(data[1..]);
        var units = new List<TemplateUnit>();
        foreach (string line in text.Split('\n'))
        {
            var segments = new List<TemplateSegment>();
            foreach (string s in line.Split('\u0001'))
            {
                segments.Add(new TemplateSegment(s));
            }

            units.Add(new TemplateUnit(segments));
        }

        var options = new TemplateOptions
        {
            Strict = (mode & 1) != 0,
            Delimiters = (mode & 2) == 0 ? TemplateDelimiters.Default : new TemplateDelimiters("<<", ">>", "<%", "%>", "<#", "#>"),
            MaxIterations = 2000,
            MaxOutputCharacters = 100_000,
            Scopes = (mode & 4) == 0 ? null : new WholeUnits(),
        };
        var source = new TemplateSource(units);
        ParsedTemplate parsed;
        try
        {
            parsed = TemplateEngine.Parse(source, options);
        }
        catch (OfficeException)
        {
            return;
        }

        foreach (Defect d in parsed.Defects)
        {
            if (!d.Code.StartsWith("template.", StringComparison.Ordinal) && !d.Code.StartsWith("formula.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("unexpected defect code " + d);
            }

            if (d.Location.Kind != DefectLocationKind.Text || d.Location.Offset < 0 || d.Location.Offset > text.Length + units.Count)
            {
                throw new InvalidOperationException("defect is not located inside the text: " + d);
            }
        }

        if (!parsed.IsValid)
        {
            return;
        }

        string first;
        try
        {
            first = parsed.Render(Data).ToText();
        }
        catch (OfficeException)
        {
            return;
        }

        if (first != parsed.Render(Data).ToText())
        {
            throw new InvalidOperationException("a second render differs");
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] seeds =
        [
            "Hi {{ name }}!",
            "{% for i in items %}\n- {{ i.price * i.qty }}\n{% endfor %}\ntotal {{ SUM(items.price * items.qty) }}",
            "{% if n > 2 %}big{% elif n %}mid{% else %}small{% endif %}",
            "a{{ na\u0001me }}b{% for x in it\u0001ems %}{{ loop.index }}{% endfor %}",
            "{% raw %}{{ x }}{% endraw %}{# c #}{{- name -}} {% image name width=3 %}",
            "{% for i in items scope=table-row %}{% if i.qty > 1 %}{{ i.tags }}{% else %}-{% endif %}{% endfor %}",
            "{{ \u201Cq\u201D & \"}}\" }}{{ IFERROR(nothing, 1) }}{% include 'x' %}",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "seed" + i), [(byte)(i % 8), .. Encoding.UTF8.GetBytes(seeds[i])]);
        }

        return 0;
    }

    private sealed class WholeUnits : ITemplateScopeResolver
    {
        public TemplateUnitRange? Resolve(in TemplateBlockInfo block) => new(block.OpenUnit, block.CloseUnit);
    }
}
