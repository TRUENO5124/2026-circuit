// ============================================================================
// File: apps/desktop-winui/Math/MathNode.cs
// Description: Pure C# syntax tree for the LaTeX subset emitted by the teaching
//              engine. No WinUI / Win2D dependency so the console harness
//              tests/MathFormulaChecks can link these sources directly.
//
//              Produced by MathLatexParser, consumed by MathLayoutEngine.
// ============================================================================

using System;
using System.Collections.Generic;

namespace CircuitAssistant.MathRendering;

/// <summary>Discriminator for the syntax tree node kinds.</summary>
public enum MathNodeKind
{
    Row,
    Run,
    Fraction,
    Sqrt,
    Script,
    Space,
    LineBreak,
    Matrix,
    Accent,
}

/// <summary>Delimiter style of a matrix-like environment (\begin{bmatrix} etc.).</summary>
public enum MathMatrixKind
{
    /// <summary>matrix / array / aligned -- no delimiters.</summary>
    Plain,

    /// <summary>bmatrix / Bmatrix -- square brackets on both sides.</summary>
    Bracket,

    /// <summary>pmatrix -- round parentheses on both sides.</summary>
    Parenthesis,

    /// <summary>cases -- single left curly brace.</summary>
    Cases,

    /// <summary>vmatrix -- vertical bars on both sides.</summary>
    Bar,
}

/// <summary>Decoration drawn above a base node.</summary>
public enum MathAccentKind
{
    /// <summary>\hat / \widehat -- circumflex accent.</summary>
    Hat,

    /// <summary>\dot -- single dot (phasor notation).</summary>
    Dot,

    /// <summary>\ddot -- double dot.</summary>
    DoubleDot,
}

/// <summary>Base class of every math syntax tree node.</summary>
public abstract class MathNode
{
    /// <summary>Node kind discriminator.</summary>
    public abstract MathNodeKind Kind { get; }

    /// <summary>True when the node contributes no glyphs and no width.</summary>
    public virtual bool IsEmpty => false;
}

/// <summary>
/// Horizontal sequence of nodes. Mirrors a LaTeX group; the root of every parsed
/// formula is a <see cref="MathRow"/>.
/// </summary>
public sealed class MathRow : MathNode
{
    private readonly List<MathNode> _children;

    public MathRow()
    {
        _children = new List<MathNode>();
    }

    public MathRow(IEnumerable<MathNode>? children)
    {
        _children = children is null ? new List<MathNode>() : new List<MathNode>(children);
    }

    public MathRow(params MathNode[] children)
    {
        _children = children is null ? new List<MathNode>() : new List<MathNode>(children);
    }

    /// <summary>Child nodes, left to right.</summary>
    public IReadOnlyList<MathNode> Children => _children;

    public int Count => _children.Count;

    public MathNode this[int index] => _children[index];

    public override MathNodeKind Kind => MathNodeKind.Row;

    public override bool IsEmpty => _children.Count == 0;

    public void Add(MathNode node)
    {
        if (node is not null)
        {
            _children.Add(node);
        }
    }

    public void AddRange(IEnumerable<MathNode> nodes)
    {
        if (nodes is null)
        {
            return;
        }

        foreach (MathNode node in nodes)
        {
            Add(node);
        }
    }

    public MathNode? Last => _children.Count == 0 ? null : _children[^1];

    /// <summary>Replaces the child at <paramref name="index"/> (used for style transforms).</summary>
    public void Replace(int index, MathNode node)
    {
        if (index >= 0 && index < _children.Count && node is not null)
        {
            _children[index] = node;
        }
    }

    public MathNode? RemoveLast()
    {
        if (_children.Count == 0)
        {
            return null;
        }

        MathNode last = _children[^1];
        _children.RemoveAt(_children.Count - 1);
        return last;
    }

    /// <summary>Removes trailing glue nodes (used before scripts / closing delimiters).</summary>
    public int TrimTrailingSpaces()
    {
        int removed = 0;
        while (_children.Count > 0 && _children[^1] is MathSpace)
        {
            _children.RemoveAt(_children.Count - 1);
            removed++;
        }

        return removed;
    }
}

/// <summary>Literal glyph run. Italic marks math variables, upright marks digits/operators/text.</summary>
public sealed class MathRun : MathNode
{
    public MathRun(string text, bool italic = false, bool bold = false, bool canBreakAfter = false)
    {
        Text = text ?? string.Empty;
        Italic = italic;
        Bold = bold;
        CanBreakAfter = canBreakAfter;
    }

    public string Text { get; }

    public bool Italic { get; }

    public bool Bold { get; }

    /// <summary>Top-level greedy line breaking may break the line after this run (binary/relational operators, commas).</summary>
    public bool CanBreakAfter { get; }

    public override MathNodeKind Kind => MathNodeKind.Run;

    public override bool IsEmpty => string.IsNullOrEmpty(Text);

    public MathRun WithStyle(bool? italic = null, bool? bold = null) =>
        new(Text, italic ?? Italic, bold ?? Bold, CanBreakAfter);

    public override string ToString() => $"Run(\"{Text}\"{(Italic ? ", italic" : string.Empty)}{(Bold ? ", bold" : string.Empty)})";
}

/// <summary>Two-dimensional fraction: numerator stacked over denominator with a horizontal bar.</summary>
public sealed class MathFraction : MathNode
{
    public MathFraction(MathNode numerator, MathNode denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public MathNode Numerator { get; set; }

    public MathNode Denominator { get; set; }

    public override MathNodeKind Kind => MathNodeKind.Fraction;
}

/// <summary>Square root: radical sign + overbar above the radicand.</summary>
public sealed class MathSqrt : MathNode
{
    public MathSqrt(MathNode radicand, MathNode? index = null)
    {
        Radicand = radicand;
        Index = index;
    }

    public MathNode Radicand { get; set; }

    /// <summary>Optional root index (\sqrt[3]{x}); the engine does not emit it but the parser accepts it.</summary>
    public MathNode? Index { get; set; }

    public override MathNodeKind Kind => MathNodeKind.Sqrt;
}

/// <summary>Base with optional subscript and/or superscript.</summary>
public sealed class MathScript : MathNode
{
    public MathScript(MathNode baseNode, MathNode? subscript = null, MathNode? superscript = null)
    {
        Base = baseNode;
        Subscript = subscript;
        Superscript = superscript;
    }

    public MathNode Base { get; set; }

    public MathNode? Subscript { get; set; }

    public MathNode? Superscript { get; set; }

    public override MathNodeKind Kind => MathNodeKind.Script;
}

/// <summary>Horizontal glue measured in em of the surrounding font size.</summary>
public sealed class MathSpace : MathNode
{
    public MathSpace(float em, bool canBreakAfter = false)
    {
        Em = em;
        CanBreakAfter = canBreakAfter;
    }

    /// <summary>Width in em.</summary>
    public float Em { get; }

    /// <summary>Top-level greedy line breaking may break the line after this space (e.g. \quad).</summary>
    public bool CanBreakAfter { get; }

    public override MathNodeKind Kind => MathNodeKind.Space;

    public override bool IsEmpty => Em <= 0;

    public override string ToString() => $"Space({Em:0.###}em)";
}

/// <summary>Hard line break (LaTeX <c>\\</c>).</summary>
public sealed class MathLineBreak : MathNode
{
    public override MathNodeKind Kind => MathNodeKind.LineBreak;
}

/// <summary>Accent decoration over a base node.</summary>
public sealed class MathAccent : MathNode
{
    public MathAccent(MathAccentKind accent, MathNode baseNode)
    {
        Accent = accent;
        Base = baseNode;
    }

    public MathAccentKind Accent { get; }

    public MathNode Base { get; set; }

    public override MathNodeKind Kind => MathNodeKind.Accent;
}

/// <summary>Grid environment (bmatrix, pmatrix, cases, array, aligned...).</summary>
public sealed class MathMatrix : MathNode
{
    public MathMatrix(MathMatrixKind kind, IEnumerable<IReadOnlyList<MathRow>> rows, IReadOnlyList<int>? rulesAboveRows = null)
    {
        MatrixKind = kind;
        var list = new List<IReadOnlyList<MathRow>>();
        if (rows is not null)
        {
            foreach (IReadOnlyList<MathRow> row in rows)
            {
                list.Add(row);
            }
        }

        Rows = list;
        RulesAboveRows = rulesAboveRows is null ? Array.Empty<int>() : new List<int>(rulesAboveRows);
    }

    /// <summary>Delimiter style of the environment.</summary>
    public MathMatrixKind MatrixKind { get; }

    /// <summary>Row list; each row is a list of cells.</summary>
    public IReadOnlyList<IReadOnlyList<MathRow>> Rows { get; }

    /// <summary>
    /// Row indices that have a horizontal rule drawn above them (from \hline).
    /// An index equal to <see cref="Rows"/>.Count means "rule below the last row".
    /// </summary>
    public IReadOnlyList<int> RulesAboveRows { get; }

    public override MathNodeKind Kind => MathNodeKind.Matrix;
}
