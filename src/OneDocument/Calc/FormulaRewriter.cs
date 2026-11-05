using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>
/// Rebuilds a formula tree with some nodes replaced. A caller that moves cells (inserting rows, expanding a template, renaming a sheet) maps
/// the references it knows about and leaves everything else, spaces and spellings included, as it was.
/// </summary>
public static class FormulaRewriter
{
    /// <summary>Walks the tree bottom-up and gives every node to <paramref name="map"/>.</summary>
    /// <param name="node">The formula.</param>
    /// <param name="map">Called for each node after its children were rebuilt; returns a replacement or null to keep the node. A replacement takes over the node's leading and trailing spaces when it has none of its own.</param>
    /// <returns>The new tree. Unchanged subtrees are shared with the input; the input is returned when nothing changed.</returns>
    public static FormulaNode Rewrite(FormulaNode node, Func<FormulaNode, FormulaNode?> map)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(map);
        return Visit(node, map);
    }

    private static T Trivia<T>(T copy, FormulaNode from)
        where T : FormulaNode
    {
        copy.LeadingSpace = from.LeadingSpace;
        copy.TrailingSpace = from.TrailingSpace;
        copy.Offset = from.Offset;
        copy.Length = from.Length;
        return copy;
    }

    private static FormulaNode Visit(FormulaNode n, Func<FormulaNode, FormulaNode?> map)
    {
        FormulaNode rebuilt = n;
        switch (n)
        {
            case FunctionCallNode x:
                {
                    FormulaNode[]? args = VisitAll(x.Arguments, map);
                    if (args is not null)
                    {
                        rebuilt = Trivia(new FunctionCallNode(x.RawName, args) { InnerSpace = x.InnerSpace, NameIsFileForm = x.NameIsFileForm }, x);
                    }

                    break;
                }

            case InvokeNode x:
                {
                    FormulaNode callee = Visit(x.Callee, map);
                    FormulaNode[]? args = VisitAll(x.Arguments, map);
                    if (!ReferenceEquals(callee, x.Callee) || args is not null)
                    {
                        rebuilt = Trivia(new InvokeNode(callee, args ?? [.. x.Arguments]) { InnerSpace = x.InnerSpace }, x);
                    }

                    break;
                }

            case UnaryNode x:
                {
                    FormulaNode o = Visit(x.Operand, map);
                    if (!ReferenceEquals(o, x.Operand))
                    {
                        rebuilt = Trivia(new UnaryNode(x.Operator, o) { IsFileForm = x.IsFileForm }, x);
                    }

                    break;
                }

            case PercentNode x:
                {
                    FormulaNode o = Visit(x.Operand, map);
                    if (!ReferenceEquals(o, x.Operand))
                    {
                        rebuilt = Trivia(new PercentNode(o), x);
                    }

                    break;
                }

            case SpillNode x:
                {
                    FormulaNode o = Visit(x.Operand, map);
                    if (!ReferenceEquals(o, x.Operand))
                    {
                        rebuilt = Trivia(new SpillNode(o) { IsFileForm = x.IsFileForm }, x);
                    }

                    break;
                }

            case BinaryNode x:
                {
                    FormulaNode l = Visit(x.Left, map);
                    FormulaNode r = Visit(x.Right, map);
                    if (!ReferenceEquals(l, x.Left) || !ReferenceEquals(r, x.Right))
                    {
                        rebuilt = Trivia(new BinaryNode(x.Operator, l, r, x.OperatorText), x);
                    }

                    break;
                }

            case ParenNode x:
                {
                    FormulaNode i = Visit(x.Inner, map);
                    if (!ReferenceEquals(i, x.Inner))
                    {
                        rebuilt = Trivia(new ParenNode(i), x);
                    }

                    break;
                }
        }

        FormulaNode? replaced = map(rebuilt);
        if (replaced is null || ReferenceEquals(replaced, rebuilt))
        {
            return rebuilt;
        }

        if (replaced.LeadingSpace is null)
        {
            replaced.LeadingSpace = n.LeadingSpace;
        }

        if (replaced.TrailingSpace is null)
        {
            replaced.TrailingSpace = n.TrailingSpace;
        }

        return replaced;
    }

    private static FormulaNode[]? VisitAll(IReadOnlyList<FormulaNode> args, Func<FormulaNode, FormulaNode?> map)
    {
        FormulaNode[]? result = null;
        for (int i = 0; i < args.Count; i++)
        {
            FormulaNode v = Visit(args[i], map);
            if (!ReferenceEquals(v, args[i]) && result is null)
            {
                result = new FormulaNode[args.Count];
                for (int k = 0; k < i; k++)
                {
                    result[k] = args[k];
                }
            }

            if (result is not null)
            {
                result[i] = v;
            }
        }

        return result;
    }
}
