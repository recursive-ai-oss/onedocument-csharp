using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private const string StylesContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private StyleSheet? _styles;
    private ThemeColors? _theme;
    private DefinedNameCollection? _names;

    /// <summary>
    /// Gets the style sheet. It is read the first time it is used. A workbook without a styles part gets a default style sheet in memory
    /// that becomes a part only when something is added to it.
    /// </summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or is not a style sheet (<c>xlsx.styles-wrong-root</c>).</exception>
    public StyleSheet Styles
    {
        get
        {
            if (_styles is not null)
            {
                return _styles;
            }

            foreach (Relationship r in Package.GetRelationships(MainPartName))
            {
                if (IsRelationship(r.Type, "styles") && r.TargetMode == RelationshipTargetMode.Internal
                    && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
                {
                    _styles = new StyleSheet(this, MarkupDocument.Parse(part.GetBytes(), Context, part.Name), part.Name);
                    return _styles;
                }
            }

            _styles = new StyleSheet(this, MarkupDocument.Parse(StyleSheet.NewPartBytes(IsStrict ? MainNsStrict : MainNs), Context, "/xl/styles.xml"), null);
            return _styles;
        }
    }

    /// <summary>
    /// Gets the date system the workbook's serial dates use: 1904 when <c>workbookPr</c> has <c>date1904</c>, else 1900 (ECMA-376 Part 1 §18.2.28).
    /// </summary>
    public DateSystem DateSystem
    {
        get
        {
            foreach (MarkupNode c in _tree.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("workbookPr"u8))
                {
                    return c.GetAttribute("date1904") is "1" or "true" ? DateSystem.Date1904 : DateSystem.Date1900;
                }
            }

            return DateSystem.Date1900;
        }
    }

    /// <summary>Gets the defined names of the workbook and its sheets (ECMA-376 Part 1 §18.2.5).</summary>
    public DefinedNameCollection Names => _names ??= new DefinedNameCollection(this);

    internal ThemeColors LoadTheme()
    {
        if (_theme is not null)
        {
            return _theme;
        }

        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (IsRelationship(r.Type, "theme") && r.TargetMode == RelationshipTargetMode.Internal
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                return _theme = ThemeColors.Read(part.GetBytes(), Context, part.Name);
            }
        }

        return _theme = ThemeColors.Default;
    }

    internal bool FeaturePartsModified()
    {
        if (_styles?.IsModified == true)
        {
            return true;
        }

        foreach (Worksheet s in _sheets)
        {
            if (s.TablesModified)
            {
                return true;
            }
        }

        return false;
    }

    internal void CommitFeatureParts()
    {
        foreach (Worksheet s in _sheets)
        {
            s.CommitTables();
        }

        if (_styles is { IsModified: true } st)
        {
            byte[] bytes = st.ToBytes();
            if (st.PartName is not null && Package.FindPart(st.PartName) is { } existing)
            {
                existing.SetBytes(bytes);
            }
            else
            {
                string name = "/xl/styles.xml";
                for (int n = 1; Package.FindPart(name) is not null; n++)
                {
                    name = "/xl/styles" + (n + 1).ToString(CultureInfo.InvariantCulture) + ".xml";
                }

                Package.AddPart(name, StylesContentType, bytes);
                st.PartName = name;
                var rels = new List<Relationship>(Package.GetRelationships(MainPartName)) { new(NewRelationshipId(), RelBase + "styles", RelativeTo(name)) };
                Package.SetRelationships(MainPartName, rels);
            }
        }
    }

    internal MarkupDocument Tree => _tree;

    internal int NextTableId()
    {
        int max = 0;
        foreach (Worksheet s in _sheets)
        {
            if (s.Kind != SheetKind.Worksheet) continue;
            foreach (Table t in s.Tables)
            {
                max = Math.Max(max, t.Id);
            }
        }

        return max + 1;
    }

    internal void CheckNewTableName(string name, Table? self)
    {
        if (!DefinedNameCollection.IsValidName(name))
        {
            throw new ArgumentException("'" + name + "' is not a valid table name.", nameof(name));
        }

        foreach (Worksheet s in _sheets)
        {
            if (s.Kind != SheetKind.Worksheet) continue;
            foreach (Table t in s.Tables)
            {
                if (!ReferenceEquals(t, self) && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("A table named '" + name + "' exists.", nameof(name));
                }
            }
        }

        if (Names.Find(name) is not null)
        {
            throw new ArgumentException("A defined name '" + name + "' exists.", nameof(name));
        }
    }

    internal string MainNamespace => IsStrict ? MainNsStrict : MainNs;

    internal string RelationshipBase => IsStrict ? RelBaseStrict : RelBase;

    internal string RelationshipNamespace => IsStrict ? RelNsStrict : RelNs;

    internal string RelativeToMain(string partName) => RelativeTo(partName);
}
