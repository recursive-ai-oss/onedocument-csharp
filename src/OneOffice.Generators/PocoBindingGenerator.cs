using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RecursiveAI.OneOffice.Generators;

/// <summary>
/// Generates, at compile time, the code that reads and writes plain classes as spreadsheet rows, so that binding never uses
/// reflection (which NativeAOT and trimming cannot keep). It looks for types marked <c>[SpreadsheetRow]</c> and adds, to each, an
/// implementation of <c>ISpreadsheetRow&lt;T&gt;</c>. Problems with a type or member are reported as diagnostics OOBIND001 to OOBIND006.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PocoBindingGenerator : IIncrementalGenerator
{
    private const string Ns = "RecursiveAI.OneOffice.Spreadsheets.Binding";
    private const string RowAttribute = Ns + ".SpreadsheetRowAttribute";
    private const string ColumnAttribute = Ns + ".SpreadsheetColumnAttribute";

    private static readonly DiagnosticDescriptor UnsupportedMember = new(
        "OOBIND001", "Member type cannot be bound", "'{0}' has type '{1}', which a spreadsheet column cannot hold; the member is left out (mark it [SpreadsheetColumn(Ignore = true)] to silence this)",
        "OneOffice.Binding", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor NotPartial = new(
        "OOBIND002", "Row type must be partial", "'{0}' is marked [SpreadsheetRow] but it, or a type that contains it, is not declared partial",
        "OneOffice.Binding", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor NoConstructor = new(
        "OOBIND003", "Constructor parameter has no column", "'{0}' cannot be built: constructor parameter '{1}' matches no property or field",
        "OneOffice.Binding", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor DuplicateName = new(
        "OOBIND004", "Duplicate column name", "'{0}' has two columns named '{1}' (ignoring case); reading uses the first header match for the first member",
        "OneOffice.Binding", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor GenericType = new(
        "OOBIND005", "Generic row types are not supported", "'{0}' is, or is inside, a generic type; bind a closed type instead",
        "OneOffice.Binding", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor BadIndex = new(
        "OOBIND006", "Column index out of range", "'{0}' sets Index = {1}; a sheet has columns 1 to 16384, so the index is ignored",
        "OneOffice.Binding", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor NoColumns = new(
        "OOBIND007", "Row type has no columns", "'{0}' has no member that can be a column",
        "OneOffice.Binding", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor[] All = [UnsupportedMember, NotPartial, NoConstructor, DuplicateName, GenericType, BadIndex, NoColumns];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Result> rows = context.SyntaxProvider.ForAttributeWithMetadataName(
            RowAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Build(ctx, ct));

        context.RegisterSourceOutput(rows, static (spc, result) =>
        {
            foreach (Diag d in result.Diagnostics)
            {
                spc.ReportDiagnostic(d.ToDiagnostic());
            }

            if (result.Source is not null)
            {
                spc.AddSource(result.HintName!, SourceText.From(result.Source, Encoding.UTF8));
            }
        });
    }

    // ------------------------------------------------------------------ results (value-equal so the pipeline can cache)

    private sealed class Diag : IEquatable<Diag>
    {
        public Diag(string id, string[] args, string? path, TextSpan span, LinePositionSpan lines)
        {
            Id = id;
            Args = args;
            Path = path;
            Span = span;
            Lines = lines;
        }

        public string Id { get; }

        public string[] Args { get; }

        public string? Path { get; }

        public TextSpan Span { get; }

        public LinePositionSpan Lines { get; }

        public Diagnostic ToDiagnostic()
        {
            DiagnosticDescriptor descriptor = All.First(d => d.Id == Id);
            Location location = Path is null ? Location.None : Location.Create(Path, Span, Lines);
            return Diagnostic.Create(descriptor, location, Args.Cast<object>().ToArray());
        }

        public bool Equals(Diag? other) =>
            other is not null && Id == other.Id && Path == other.Path && Span == other.Span && Lines == other.Lines && Args.SequenceEqual(other.Args);

        public override bool Equals(object? obj) => Equals(obj as Diag);

        public override int GetHashCode() => Id.GetHashCode() ^ Span.GetHashCode();
    }

    private sealed class Result : IEquatable<Result>
    {
        public Result(string? hintName, string? source, List<Diag> diagnostics)
        {
            HintName = hintName;
            Source = source;
            Diagnostics = diagnostics;
        }

        public string? HintName { get; }

        public string? Source { get; }

        public List<Diag> Diagnostics { get; }

        public bool Equals(Result? other) =>
            other is not null && HintName == other.HintName && Source == other.Source && Diagnostics.SequenceEqual(other.Diagnostics);

        public override bool Equals(object? obj) => Equals(obj as Result);

        public override int GetHashCode() => (HintName ?? string.Empty).GetHashCode();
    }

    // ------------------------------------------------------------------ analysis

    private enum Kind
    {
        Text, Bool, Int, UInt, ULong, Float, Double, Decimal, DateTime, DateOnly, TimeOnly, DateTimeOffset, Guid, Enum,
    }

    private sealed class Member
    {
        public string Name = string.Empty;
        public ISymbol Symbol = null!;
        public ITypeSymbol Type = null!;
        public bool IsField;
        public bool HasGetter;
        public bool HasSetter;
        public bool Nullable;
        public bool Explicit;
        public bool Ignore;
        public Kind Kind;
        public string TypeName = string.Empty;       // the type without a trailing '?'
        public string? ColumnName;
        public int Order;
        public bool HasOrder;
        public int Index;
        public string? Format;
        public bool EnumAsNumber;
        public bool CtorBound;
        public INamedTypeSymbol? EnumType;
        public int EnumHelper = -1;
    }

    private static Result Build(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var diags = new List<Diag>();
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return new Result(null, null, diags);
        }

        string display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        Location typeLocation = type.Locations.FirstOrDefault() ?? Location.None;

        // The containing chain, outermost first.
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            chain.Insert(0, t);
        }

        if (chain.Any(t => t.IsGenericType))
        {
            diags.Add(MakeDiag(GenericType, typeLocation, display));
            return new Result(null, null, diags);
        }

        foreach (INamedTypeSymbol t in chain)
        {
            foreach (SyntaxReference reference in t.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(ct) is TypeDeclarationSyntax decl && !decl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    diags.Add(MakeDiag(NotPartial, typeLocation, display));
                    return new Result(null, null, diags);
                }
            }
        }

        // Attribute arguments of the type.
        string? sheet = null;
        bool strict = false;
        bool hasHeader = true;
        foreach (KeyValuePair<string, TypedConstant> arg in ctx.Attributes[0].NamedArguments)
        {
            switch (arg.Key)
            {
                case "Sheet":
                    sheet = arg.Value.Value as string;
                    break;
                case "Strict":
                    strict = arg.Value.Value is true;
                    break;
                case "HasHeader":
                    hasHeader = arg.Value.Value is not false;
                    break;
            }
        }

        // Members.
        var members = new List<Member>();
        foreach (ISymbol symbol in type.GetMembers())
        {
            if (symbol.IsStatic || symbol.IsImplicitlyDeclared)
            {
                continue;
            }

            Member? m = null;
            if (symbol is IPropertySymbol { IsIndexer: false } p && p.ExplicitInterfaceImplementations.Length == 0)
            {
                m = new Member { Symbol = p, Type = p.Type, HasGetter = p.GetMethod is not null, HasSetter = p.SetMethod is not null };
            }
            else if (symbol is IFieldSymbol { IsConst: false } f)
            {
                m = new Member { Symbol = f, Type = f.Type, IsField = true, HasGetter = true, HasSetter = !f.IsReadOnly };
            }

            if (m is null)
            {
                continue;
            }

            m.Name = symbol.Name;
            AttributeData? column = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ColumnAttribute);
            m.Explicit = column is not null;
            if (column is not null)
            {
                if (column.ConstructorArguments.Length == 1 && column.ConstructorArguments[0].Value is string n)
                {
                    m.ColumnName = n;
                }

                foreach (KeyValuePair<string, TypedConstant> arg in column.NamedArguments)
                {
                    switch (arg.Key)
                    {
                        case "Order":
                            m.Order = arg.Value.Value is int o ? o : 0;
                            m.HasOrder = true;
                            break;
                        case "Index":
                            m.Index = arg.Value.Value is int i ? i : 0;
                            break;
                        case "Ignore":
                            m.Ignore = arg.Value.Value is true;
                            break;
                        case "Format":
                            m.Format = arg.Value.Value as string;
                            break;
                        case "EnumAsNumber":
                            m.EnumAsNumber = arg.Value.Value is true;
                            break;
                    }
                }
            }

            if (!m.Explicit && symbol.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            members.Add(m);
        }

        // The constructor.
        IMethodSymbol? ctor = ChooseConstructor(type);
        var ctorArgs = new List<(string Param, Member? Bound)>();
        if (ctor is not null)
        {
            foreach (IParameterSymbol parameter in ctor.Parameters)
            {
                Member? bound = members.FirstOrDefault(x => string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (bound is null)
                {
                    diags.Add(MakeDiag(NoConstructor, typeLocation, display, parameter.Name));
                    return new Result(null, null, diags);
                }

                bound.CtorBound = true;
                ctorArgs.Add((parameter.Name, bound));
            }
        }

        // Which members are columns.
        var columns = new List<Member>();
        foreach (Member m in members)
        {
            if (m.Ignore)
            {
                continue;
            }

            bool canRead = m.HasSetter || m.CtorBound;
            if (!m.Explicit && !canRead)
            {
                continue; // a computed, get-only property
            }

            if (!Classify(m))
            {
                diags.Add(MakeDiag(UnsupportedMember, m.Symbol.Locations.FirstOrDefault() ?? Location.None, m.Name, m.Type.ToDisplayString()));
                continue;
            }

            if (m.Index is < 0 or > 16384)
            {
                diags.Add(MakeDiag(BadIndex, m.Symbol.Locations.FirstOrDefault() ?? Location.None, m.Name, m.Index.ToString(CultureInfo.InvariantCulture)));
                m.Index = 0;
            }

            columns.Add(m);
        }

        // A constructor parameter whose member is ignored or unsupported gets the default.
        List<Member> ordered = columns.Where(c => c.HasOrder).OrderBy(c => c.Order).Concat(columns.Where(c => !c.HasOrder)).ToList();
        if (ordered.Count == 0)
        {
            diags.Add(MakeDiag(NoColumns, typeLocation, display));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Member c in ordered)
        {
            if (c.Index == 0 && !seen.Add((c.ColumnName ?? c.Name).Trim()))
            {
                diags.Add(MakeDiag(DuplicateName, c.Symbol.Locations.FirstOrDefault() ?? Location.None, display, c.ColumnName ?? c.Name));
            }
        }

        string source = Emit(type, chain, ordered, ctor, ctorArgs, sheet, strict, hasHeader);
        string hint = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_') + ".SpreadsheetRow.g.cs";
        return new Result(hint, source, diags);
    }

    private static Diag MakeDiag(DiagnosticDescriptor descriptor, Location location, params string[] args)
    {
        if (location.IsInSource)
        {
            return new Diag(descriptor.Id, args, location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span);
        }

        return new Diag(descriptor.Id, args, null, default, default);
    }

    private static IMethodSymbol? ChooseConstructor(INamedTypeSymbol type)
    {
        List<IMethodSymbol> ctors = type.InstanceConstructors
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type)))
            .ToList();

        // A parameterless constructor the author wrote wins; then the one with the most parameters (a record's primary constructor);
        // otherwise the implicit default.
        if (ctors.Any(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared))
        {
            return null;
        }

        IMethodSymbol? widest = ctors.Where(c => c.Parameters.Length > 0).OrderByDescending(c => c.Parameters.Length).FirstOrDefault();
        return widest;
    }

    private static bool Classify(Member m)
    {
        ITypeSymbol t = m.Type;
        if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            m.Nullable = true;
            t = nullable.TypeArguments[0];
        }
        else if (!t.IsValueType)
        {
            m.Nullable = t.NullableAnnotation != NullableAnnotation.NotAnnotated;
        }

        m.TypeName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (t.SpecialType)
        {
            case SpecialType.System_String: m.Kind = Kind.Text; return true;
            case SpecialType.System_Boolean: m.Kind = Kind.Bool; return true;
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Byte:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
                m.Kind = Kind.Int;
                return true;
            case SpecialType.System_UInt64: m.Kind = Kind.ULong; return true;
            case SpecialType.System_Single: m.Kind = Kind.Float; return true;
            case SpecialType.System_Double: m.Kind = Kind.Double; return true;
            case SpecialType.System_Decimal: m.Kind = Kind.Decimal; return true;
            case SpecialType.System_DateTime: m.Kind = Kind.DateTime; return true;
        }

        if (t.TypeKind == TypeKind.Enum && t is INamedTypeSymbol e)
        {
            m.Kind = Kind.Enum;
            m.EnumType = e;
            return true;
        }

        switch (t.ToDisplayString())
        {
            case "System.DateOnly": m.Kind = Kind.DateOnly; return true;
            case "System.TimeOnly": m.Kind = Kind.TimeOnly; return true;
            case "System.DateTimeOffset": m.Kind = Kind.DateTimeOffset; return true;
            case "System.Guid": m.Kind = Kind.Guid; return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ emission

    private static string Lit(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static string ColumnTypeName(Member m)
    {
        string name = m.Kind switch
        {
            Kind.Text => "Text",
            Kind.Bool => "Boolean",
            Kind.Int or Kind.UInt or Kind.ULong or Kind.Float or Kind.Double or Kind.Decimal => "Number",
            Kind.DateOnly => "Date",
            Kind.DateTime or Kind.DateTimeOffset => "DateTime",
            Kind.TimeOnly => "Time",
            Kind.Guid => "Identifier",
            _ => "Enum",
        };
        return "global::" + Ns + ".ColumnType." + name;
    }

    private static string Emit(INamedTypeSymbol type, List<INamedTypeSymbol> chain, List<Member> cols, IMethodSymbol? ctor, List<(string Param, Member? Bound)> ctorArgs, string? sheet, bool strict, bool hasHeader)
    {
        string self = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string rt = "global::" + Ns;
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable");
        string ns = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        if (ns.Length > 0)
        {
            sb.Append("namespace ").Append(ns).AppendLine(" {");
        }

        int depth = 0;
        foreach (INamedTypeSymbol t in chain)
        {
            string keyword = KeywordOf(t);
            sb.Append("partial ").Append(keyword).Append(' ').Append(t.Name);
            if (SymbolEqualityComparer.Default.Equals(t, type))
            {
                sb.Append(" : ").Append(rt).Append(".ISpreadsheetRow<").Append(self).Append('>');
            }

            sb.AppendLine(" {");
            depth++;
        }

        // Enum helpers: one parser per distinct enum type.
        var enums = new List<INamedTypeSymbol>();
        foreach (Member c in cols.Where(c => c.Kind == Kind.Enum))
        {
            int at = enums.FindIndex(e => SymbolEqualityComparer.Default.Equals(e, c.EnumType));
            if (at < 0)
            {
                enums.Add(c.EnumType!);
                at = enums.Count - 1;
            }

            c.EnumHelper = at;
        }

        // Schema.
        sb.Append("private static readonly ").Append(rt).Append(".RowSchema __OneOfficeSchema = new(").Append(Lit(type.Name)).Append(", ")
            .Append(sheet is null ? "null" : Lit(sheet)).Append(", ").Append(strict ? "true" : "false").Append(", ").Append(hasHeader ? "true" : "false").AppendLine(", [");
        foreach (Member c in cols)
        {
            bool canRead = c.HasSetter || c.CtorBound;
            sb.Append("    new ").Append(rt).Append(".ColumnSpec(").Append(Lit((c.ColumnName ?? c.Name).Trim())).Append(", ").Append(ColumnTypeName(c)).Append(", ")
                .Append(c.Nullable ? "true" : "false").Append(", ").Append(c.Index.ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(c.Format is null ? "null" : Lit(c.Format)).Append(", ").Append(canRead ? "true" : "false").Append(", ").Append(c.HasGetter ? "true" : "false").AppendLine("),");
        }

        sb.AppendLine("]);");
        sb.Append("static ").Append(rt).Append(".RowSchema ").Append(rt).Append(".ISpreadsheetRow<").Append(self).AppendLine(">.Schema => __OneOfficeSchema;");

        // Read.
        sb.Append("static ").Append(self).Append(' ').Append(rt).Append(".ISpreadsheetRow<").Append(self).Append(">.Read(").Append(rt).AppendLine(".RowReader r) {");
        for (int i = 0; i < cols.Count; i++)
        {
            Member c = cols[i];
            if (!(c.HasSetter || c.CtorBound))
            {
                continue;
            }

            EmitRead(sb, c, i);
        }

        sb.Append("    return new ").Append(self).Append('(');
        if (ctor is not null)
        {
            sb.Append(string.Join(", ", ctorArgs.Select(a =>
            {
                int index = a.Bound is null ? -1 : cols.IndexOf(a.Bound);
                return index >= 0 ? "c" + index.ToString(CultureInfo.InvariantCulture) : "default!";
            })));
        }

        sb.Append(')');
        List<string> inits = new();
        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].HasSetter && !cols[i].CtorBound)
            {
                inits.Add(cols[i].Name + " = c" + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (inits.Count > 0)
        {
            sb.Append(" { ").Append(string.Join(", ", inits)).Append(" }");
        }

        sb.AppendLine(";");
        sb.AppendLine("}");

        // Write.
        sb.Append("static void ").Append(rt).Append(".ISpreadsheetRow<").Append(self).Append(">.Write(in ").Append(self).Append(" value, ").Append(rt).AppendLine(".RowWriter w) {");
        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].HasGetter)
            {
                EmitWrite(sb, cols[i], i);
            }
        }

        sb.AppendLine("}");

        for (int e = 0; e < enums.Count; e++)
        {
            EmitEnumParser(sb, enums[e], e);
        }

        for (int i = 0; i < depth; i++)
        {
            sb.AppendLine("}");
        }

        if (ns.Length > 0)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string KeywordOf(INamedTypeSymbol t)
    {
        switch (t.TypeKind)
        {
            case TypeKind.Struct:
                return t.IsRecord ? "record struct" : "struct";
            case TypeKind.Interface:
                return "interface";
            default:
                return t.IsRecord ? "record" : "class";
        }
    }

    private static void EmitRead(StringBuilder sb, Member c, int i)
    {
        string n = i.ToString(CultureInfo.InvariantCulture);
        string v = "c" + n;
        string t = c.TypeName;
        string col = n;
        switch (c.Kind)
        {
            case Kind.Text:
                if (c.Nullable)
                {
                    sb.Append("    r.Read(").Append(col).Append(", out string? ").Append(v).AppendLine(");");
                }
                else
                {
                    sb.Append("    r.Read(").Append(col).Append(", out string? t").Append(n).AppendLine(");");
                    sb.Append("    string ").Append(v).Append(" = t").Append(n).AppendLine(" ?? string.Empty;");
                }

                break;
            case Kind.Enum:
                string nullableMark = c.Nullable ? "?" : string.Empty;
                sb.Append("    ").Append(t).Append(nullableMark).Append(' ').Append(v).AppendLine(c.Nullable ? " = null;" : " = default;");
                sb.Append("    if (r.ReadEnumToken(").Append(col).Append(", out global::System.ReadOnlySpan<char> n").Append(n).Append(", out long k").Append(n).Append(", out bool b").Append(n).AppendLine(")) {");
                sb.Append("        if (__ParseEnum").Append(c.EnumHelper.ToString(CultureInfo.InvariantCulture)).Append("(n").Append(n).Append(", k").Append(n).Append(", b").Append(n).Append(", out ").Append(t).Append(" e").Append(n).Append(")) ")
                    .Append(v).Append(" = e").Append(n).AppendLine(";");
                sb.Append("        else r.Invalid(").Append(col).Append(", ").Append(Lit(c.EnumType!.Name)).AppendLine(");");
                sb.AppendLine("    }");
                break;
            default:
                if (c.Nullable)
                {
                    sb.Append("    ").Append(t).Append("? ").Append(v).Append(" = r.Read(").Append(col).Append(", out ").Append(t).Append(" t").Append(n).Append(") ? t").Append(n).Append(" : null;").AppendLine();
                }
                else
                {
                    sb.Append("    r.Read(").Append(col).Append(", out ").Append(t).Append(' ').Append(v).AppendLine(");");
                }

                break;
        }
    }

    private static void EmitWrite(StringBuilder sb, Member c, int i)
    {
        string n = i.ToString(CultureInfo.InvariantCulture);
        string access = "value." + c.Name;
        string call(string e) => c.Kind switch
        {
            Kind.Int => "w.Write(" + n + ", (long)" + e + ");",
            Kind.ULong => "w.Write(" + n + ", (ulong)" + e + ");",
            Kind.Float => "w.Write(" + n + ", (float)" + e + ");",
            _ => "w.Write(" + n + ", " + e + ");",
        };

        if (c.Kind == Kind.Text)
        {
            sb.Append("    w.Write(").Append(n).Append(", ").Append(access).AppendLine(");");
            return;
        }

        string expr = access;
        if (c.Nullable)
        {
            sb.Append("    if (").Append(access).Append(" is { } v").Append(n).AppendLine(") {");
            expr = "v" + n;
        }
        else
        {
            sb.AppendLine("    {");
        }

        if (c.Kind == Kind.Enum)
        {
            if (c.EnumAsNumber)
            {
                sb.Append("        w.Write(").Append(n).Append(", unchecked((long)").Append(expr).AppendLine("));");
            }
            else
            {
                sb.Append("        switch (").Append(expr).AppendLine(") {");
                var done = new HashSet<string>();
                foreach (IFieldSymbol f in c.EnumType!.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst && f.HasConstantValue))
                {
                    string key = Convert.ToString(f.ConstantValue, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (done.Add(key))
                    {
                        sb.Append("            case ").Append(c.TypeName).Append('.').Append(f.Name).Append(": w.WriteName(").Append(n).Append(", ").Append(Lit(f.Name)).AppendLine("); break;");
                    }
                }

                sb.Append("            default: w.Write(").Append(n).Append(", unchecked((long)").Append(expr).AppendLine(")); break;");
                sb.AppendLine("        }");
            }
        }
        else
        {
            sb.Append("        ").AppendLine(call(expr));
        }

        if (c.Nullable)
        {
            sb.Append("    } else w.WriteEmpty(").Append(n).AppendLine(");");
        }
        else
        {
            sb.AppendLine("    }");
        }
    }

    private static void EmitEnumParser(StringBuilder sb, INamedTypeSymbol e, int index)
    {
        string t = e.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        List<IFieldSymbol> fields = e.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst && f.HasConstantValue).ToList();
        sb.Append("private static bool __ParseEnum").Append(index.ToString(CultureInfo.InvariantCulture))
            .Append("(global::System.ReadOnlySpan<char> name, long number, bool isNumber, out ").Append(t).AppendLine(" value) {");
        sb.AppendLine("    if (isNumber) {");
        sb.AppendLine("        switch (number) {");
        var done = new HashSet<long>();
        foreach (IFieldSymbol f in fields)
        {
            long k = f.ConstantValue is ulong u ? unchecked((long)u) : Convert.ToInt64(f.ConstantValue, CultureInfo.InvariantCulture);
            if (done.Add(k))
            {
                sb.Append("            case ").Append(k.ToString(CultureInfo.InvariantCulture)).Append("L: value = ").Append(t).Append('.').Append(f.Name).AppendLine("; return true;");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine("        value = default; return false;");
        sb.AppendLine("    }");
        foreach (IFieldSymbol f in fields)
        {
            sb.Append("    if (global::System.MemoryExtensions.Equals(name, ").Append(Lit(f.Name)).Append(", global::System.StringComparison.OrdinalIgnoreCase)) { value = ").Append(t).Append('.').Append(f.Name).AppendLine("; return true; }");
        }

        sb.AppendLine("    value = default; return false;");
        sb.AppendLine("}");
    }
}
