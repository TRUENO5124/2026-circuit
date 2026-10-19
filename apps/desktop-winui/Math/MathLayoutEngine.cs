// ============================================================================
// File: apps/desktop-winui/Math/MathLayoutEngine.cs
// Description: Pure C# layout engine for the math syntax tree. Lays a formula
//              out into positioned text fragments plus rule/delimiter geometry:
//
//                * true sub/superscripts (0.72 x size, baseline shifted)
//                * two-dimensional fractions (stacked, centred, bar between)
//                * square roots (radical hook + overbar)
//                * matrix / cases / array grids (column alignment, hline rules)
//                * greedy automatic line wrapping at operators / punctuation
//                * uniform scale-to-fit fallback when one atom is wider than
//                  the available width (never clips horizontally)
//
//              No WinUI / Win2D dependency: the console harness can test it.
// ============================================================================

using System;
using System.Collections.Generic;

namespace CircuitAssistant.MathRendering;

/// <summary>One positioned text run produced by <see cref="MathLayoutEngine"/>.</summary>
public sealed class MathTextFragment
{
    public MathTextFragment(string text, float x, float baselineY, float fontSize, bool italic, bool bold, float width, int lineIndex)
    {
        Text = text;
        X = x;
        BaselineY = baselineY;
        FontSize = fontSize;
        Italic = italic;
        Bold = bold;
        Width = width;
        LineIndex = lineIndex;
    }

    public string Text { get; }

    /// <summary>Left edge of the run in layout coordinates (DIPs).</summary>
    public float X { get; internal set; }

    /// <summary>Baseline Y in layout coordinates (DIPs, y grows downward).</summary>
    public float BaselineY { get; internal set; }

    public float FontSize { get; internal set; }

    public bool Italic { get; internal set; }

    public bool Bold { get; internal set; }

    /// <summary>Measured advance width of the run.</summary>
    public float Width { get; internal set; }

    /// <summary>Zero-based wrapped-line index this fragment belongs to.</summary>
    public int LineIndex { get; internal set; }

    public float Right => X + Width;

    public override string ToString() =>
        $"'{Text}' x={X:0.##} baseline={BaselineY:0.##} fs={FontSize:0.##}{(Italic ? " italic" : string.Empty)} line={LineIndex}";
}

/// <summary>Kind of non-text geometry emitted by the layout engine.</summary>
public enum MathRuleKind
{
    /// <summary>Horizontal fraction bar.</summary>
    FractionBar,

    /// <summary>Horizontal overbar above a radicand.</summary>
    SqrtBar,

    /// <summary>Radical sign of a square root (drawn as a polyline inside the box).</summary>
    SqrtRadical,

    /// <summary>Circumflex accent of \hat.</summary>
    Hat,

    /// <summary>Single or double dot accent of \dot / \ddot (X/Y = centre, Width = diameter).</summary>
    Dot,

    /// <summary>Horizontal rule inside an array/matrix (\hline).</summary>
    MatrixRule,
}

/// <summary>Straight-line / polyline geometry emitted by the layout engine.</summary>
public sealed class MathRule
{
    public MathRule(MathRuleKind kind, float x, float y, float width, float height, float thickness, int lineIndex = 0)
    {
        Kind = kind;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Thickness = thickness;
        LineIndex = lineIndex;
    }

    public MathRuleKind Kind { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public float Thickness { get; }

    public int LineIndex { get; }
}

/// <summary>Delimiter style used by matrix-like environments.</summary>
public enum MathDelimiterStyle
{
    SquareBracket,
    Parenthesis,
    Brace,
    Bar,
}

/// <summary>Stretchable delimiter geometry (matrix brackets, cases brace, vmatrix bars).</summary>
public sealed class MathDelimiter
{
    public MathDelimiter(MathDelimiterStyle style, float x, float y, float width, float height, bool isClosing, int lineIndex = 0)
    {
        Style = style;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsClosing = isClosing;
        LineIndex = lineIndex;
    }

    public MathDelimiterStyle Style { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public bool IsClosing { get; }

    public int LineIndex { get; }
}

/// <summary>Result of laying out a formula for a specific available width / font size.</summary>
public sealed class MathLayoutResult
{
    public MathLayoutResult(
        IReadOnlyList<MathTextFragment> fragments,
        IReadOnlyList<MathRule> rules,
        IReadOnlyList<MathDelimiter> delimiters,
        float width,
        float height,
        int lineCount,
        float maxLineWidth,
        bool scaledToFit,
        float baseFontSize)
    {
        Fragments = fragments;
        Rules = rules;
        Delimiters = delimiters;
        Width = width;
        Height = height;
        LineCount = lineCount;
        MaxLineWidth = maxLineWidth;
        ScaledToFit = scaledToFit;
        BaseFontSize = baseFontSize;
    }

    public IReadOnlyList<MathTextFragment> Fragments { get; }

    public IReadOnlyList<MathRule> Rules { get; }

    public IReadOnlyList<MathDelimiter> Delimiters { get; }

    /// <summary>Total layout width needed (may be smaller than the available width).</summary>
    public float Width { get; }

    /// <summary>Total layout height needed (content + insets).</summary>
    public float Height { get; }

    /// <summary>Number of wrapped visual lines.</summary>
    public int LineCount { get; }

    /// <summary>Widest line content width (excluding insets).</summary>
    public float MaxLineWidth { get; }

    /// <summary>True when uniform downscaling was required because a single atom exceeded the width.</summary>
    public bool ScaledToFit { get; }

    public float BaseFontSize { get; }

    public bool IsEmpty => Fragments.Count == 0 && Rules.Count == 0 && Delimiters.Count == 0;
}

/// <summary>Tunable typographic constants (em based unless documented otherwise).</summary>
public sealed class MathLayoutOptions
{
    public static MathLayoutOptions Default { get; } = new();

    public float ScriptScale { get; init; } = 0.72f;

    /// <summary>Subscript baseline shift below the base baseline, in em of the base size.</summary>
    public float ScriptDownShift { get; init; } = 0.28f;

    /// <summary>Superscript baseline shift above the base baseline, in em of the base size.</summary>
    public float ScriptUpShift { get; init; } = 0.44f;

    /// <summary>Font scale for numerator/denominator.</summary>
    public float FractionScale { get; init; } = 0.92f;

    public float FractionGap { get; init; } = 0.18f;

    /// <summary>Fraction bar elevation above the baseline (math axis), in em.</summary>
    public float FractionAxis { get; init; } = 0.26f;

    public float FractionBarThickness { get; init; } = 0.055f;

    public float SqrtRadicalWidth { get; init; } = 0.62f;

    public float SqrtTopPad { get; init; } = 0.16f;

    public float SqrtBottomPad { get; init; } = 0.10f;

    public float SqrtRuleThickness { get; init; } = 0.055f;

    /// <summary>Extra leading between wrapped lines, in em.</summary>
    public float LineLeading { get; init; } = 0.30f;

    public float MatrixScale { get; init; } = 0.94f;

    public float MatrixColumnGap { get; init; } = 0.85f;

    public float MatrixRowGap { get; init; } = 0.32f;

    public float MatrixBracketWidth { get; init; } = 0.42f;

    /// <summary>Width reserved for the left cases brace (drawn as a scalable polyline).</summary>
    public float MatrixBraceWidth { get; init; } = 0.72f;

    public float AccentHeight { get; init; } = 0.26f;

    public float LeftInset { get; init; } = 1.5f;

    public float RightInset { get; init; } = 1.5f;

    public float TopInset { get; init; } = 1.5f;

    public float BottomInset { get; init; } = 1.5f;

    /// <summary>Minimum usable layout width (below this the scale-to-fit fallback kicks in).</summary>
    public float MinLayoutWidth { get; init; } = 48f;
}

/// <summary>
/// Lays a parsed formula out into positioned fragments / rules / delimiters for a
/// given available width and base font size.
/// </summary>
public sealed class MathLayoutEngine
{
    private readonly IMathTextMeasurer _measurer;
    private readonly MathLayoutOptions _o;

    public MathLayoutEngine(IMathTextMeasurer measurer, MathLayoutOptions? options = null)
    {
        _measurer = measurer ?? EstimateMathTextMeasurer.Instance;
        _o = options ?? MathLayoutOptions.Default;
    }

    public MathLayoutOptions Options => _o;

    /// <summary>
    /// Lays <paramref name="root"/> out. <paramref name="availableWidth"/> is the total
    /// width the caller can hand to the formula (insets included); the engine wraps
    /// at that width and only ever scales down when a single atom cannot fit.
    /// </summary>
    public MathLayoutResult Layout(MathNode? root, float availableWidth, float baseFontSize)
    {
        root ??= new MathRow();
        float fs = baseFontSize > 0f ? baseFontSize : 16f;
        float width = availableWidth > 0f ? availableWidth : 640f;
        float usableWidth = Math.Max(_o.MinLayoutWidth, width - _o.LeftInset - _o.RightInset);

        var fragments = new List<MathTextFragment>();
        var rules = new List<MathRule>();
        var delimiters = new List<MathDelimiter>();

        List<Segment> segments = BuildSegments(root, fs);
        List<List<Atom>> lines = PackLines(segments, usableWidth, fs);

        float y = _o.TopInset;
        float widest = 0f;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            List<Atom> lineAtoms = lines[lineIndex];
            float ascent = fs * 0.85f;
            float descent = fs * 0.30f;
            foreach (Atom atom in lineAtoms)
            {
                if (!atom.IsGlue)
                {
                    ascent = Math.Max(ascent, atom.Ascent);
                    descent = Math.Max(descent, atom.Descent);
                }
            }

            float leading = fs * _o.LineLeading;
            float baseline = y + ascent + leading * 0.5f;
            float x = _o.LeftInset;
            foreach (Atom atom in lineAtoms)
            {
                if (atom.IsGlue)
                {
                    x += atom.Width;
                    continue;
                }

                x += Place(atom.Node, x, baseline, fs, lineIndex, fragments, rules, delimiters);
            }

            widest = Math.Max(widest, x - _o.LeftInset);
            y += ascent + descent + leading;
        }

        if (lines.Count == 0)
        {
            y = _o.TopInset + fs * 1.2f;
        }

        float height = y + _o.BottomInset;
        bool scaled = false;
        if (widest > usableWidth + 0.5f)
        {
            float scale = usableWidth / widest;
            ScaleGeometry(fragments, rules, delimiters, scale);
            height = _o.TopInset + (height - _o.TopInset - _o.BottomInset) * scale + _o.BottomInset;
            widest = usableWidth;
            scaled = true;
        }

        float totalWidth = widest + _o.LeftInset + _o.RightInset;
        return new MathLayoutResult(fragments, rules, delimiters, totalWidth, height, Math.Max(1, lines.Count), widest, scaled, fs);
    }

    private void ScaleGeometry(List<MathTextFragment> fragments, List<MathRule> rules, List<MathDelimiter> delimiters, float scale)
    {
        foreach (MathTextFragment fragment in fragments)
        {
            fragment.X = _o.LeftInset + (fragment.X - _o.LeftInset) * scale;
            fragment.BaselineY = _o.TopInset + (fragment.BaselineY - _o.TopInset) * scale;
            fragment.FontSize *= scale;
            fragment.Width *= scale;
        }

        for (int i = 0; i < rules.Count; i++)
        {
            MathRule rule = rules[i];
            rules[i] = new MathRule(
                rule.Kind,
                _o.LeftInset + (rule.X - _o.LeftInset) * scale,
                _o.TopInset + (rule.Y - _o.TopInset) * scale,
                rule.Width * scale,
                rule.Height * scale,
                Math.Max(1f, rule.Thickness * scale),
                rule.LineIndex);
        }

        for (int i = 0; i < delimiters.Count; i++)
        {
            MathDelimiter delimiter = delimiters[i];
            delimiters[i] = new MathDelimiter(
                delimiter.Style,
                _o.LeftInset + (delimiter.X - _o.LeftInset) * scale,
                _o.TopInset + (delimiter.Y - _o.TopInset) * scale,
                delimiter.Width * scale,
                delimiter.Height * scale,
                delimiter.IsClosing,
                delimiter.LineIndex);
        }
    }

    // ==================================================================
    // Measurement
    // ==================================================================
    private readonly struct Box
    {
        public Box(float width, float ascent, float descent)
        {
            Width = width;
            Ascent = ascent;
            Descent = descent;
        }

        public float Width { get; }

        public float Ascent { get; }

        public float Descent { get; }

        public static Box Empty => new(0f, 0f, 0f);

        /// <summary>Horizontal concatenation: widths add, heights take the max.</summary>
        public Box Concat(Box other) =>
            new(Math.Max(0f, Width) + Math.Max(0f, other.Width), Math.Max(Ascent, other.Ascent), Math.Max(Descent, other.Descent));
    }

    private Box Measure(MathNode? node, float fs)
    {
        if (node is null)
        {
            return Box.Empty;
        }

        switch (node.Kind)
        {
            case MathNodeKind.Run:
            {
                var run = (MathRun)node;
                if (string.IsNullOrEmpty(run.Text))
                {
                    return Box.Empty;
                }

                MathTextMetrics metrics = _measurer.Measure(run.Text, fs, run.Italic, run.Bold);
                return new Box(Math.Max(0f, metrics.Width), metrics.Ascent, metrics.Descent);
            }

            case MathNodeKind.Space:
            {
                var space = (MathSpace)node;
                return new Box(Math.Max(0f, space.Em) * fs, 0f, 0f);
            }

            case MathNodeKind.Row:
            {
                var row = (MathRow)node;
                Box box = Box.Empty;
                foreach (MathNode child in row.Children)
                {
                    box = box.Concat(Measure(child, fs));
                }

                return box;
            }

            case MathNodeKind.LineBreak:
                return Box.Empty;

            case MathNodeKind.Fraction:
            {
                var fraction = (MathFraction)node;
                float childSize = fs * _o.FractionScale;
                Box numerator = Measure(fraction.Numerator, childSize);
                Box denominator = Measure(fraction.Denominator, childSize);
                float bar = Math.Max(1f, fs * _o.FractionBarThickness);
                float gap = fs * _o.FractionGap;
                float width = Math.Max(numerator.Width, denominator.Width) + fs * 0.14f;
                float ascent = fs * _o.FractionAxis + gap + numerator.Ascent + bar * 0.5f;
                float descent = Math.Max(0f, -fs * _o.FractionAxis + gap + denominator.Ascent + denominator.Descent) + bar * 0.5f;
                return new Box(width, ascent, descent);
            }

            case MathNodeKind.Sqrt:
            {
                var sqrt = (MathSqrt)node;
                Box radicand = Measure(sqrt.Radicand, fs * 0.98f);
                float radicalWidth = fs * _o.SqrtRadicalWidth;
                float indexWidth = 0f;
                float indexExtra = 0f;
                if (sqrt.Index is not null)
                {
                    Box indexBox = Measure(sqrt.Index, fs * 0.55f);
                    indexWidth = indexBox.Width * 0.7f;
                    indexExtra = indexBox.Ascent * 0.6f;
                }

                float width = indexWidth + radicalWidth + radicand.Width + fs * 0.06f;
                float ascent = radicand.Ascent + fs * _o.SqrtTopPad + indexExtra;
                float descent = radicand.Descent + fs * _o.SqrtBottomPad;
                return new Box(width, ascent, descent);
            }

            case MathNodeKind.Script:
            {
                var script = (MathScript)node;
                Box baseBox = Measure(script.Base, fs);
                float scriptSize = fs * _o.ScriptScale;
                Box sub = script.Subscript is null ? Box.Empty : Measure(script.Subscript, scriptSize);
                Box sup = script.Superscript is null ? Box.Empty : Measure(script.Superscript, scriptSize);
                float kern = fs * 0.03f;
                float width = baseBox.Width + kern + Math.Max(sub.Width, sup.Width);
                float ascent = Math.Max(baseBox.Ascent, fs * _o.ScriptUpShift + sup.Ascent);
                float descent = Math.Max(baseBox.Descent, fs * _o.ScriptDownShift + sub.Descent);
                return new Box(width, Math.Max(ascent, fs * 0.55f), Math.Max(descent, fs * 0.12f));
            }

            case MathNodeKind.Accent:
            {
                var accent = (MathAccent)node;
                Box baseBox = Measure(accent.Base, fs);
                float extra = accent.Accent == MathAccentKind.Hat ? fs * _o.AccentHeight : fs * 0.20f;
                return new Box(baseBox.Width, baseBox.Ascent + extra, baseBox.Descent);
            }

            case MathNodeKind.Matrix:
            {
                MatrixMetrics metrics = ComputeMatrix((MathMatrix)node, fs);
                return metrics.Box;
            }

            default:
                return Box.Empty;
        }
    }

    // ==================================================================
    // Placement
    // ==================================================================
    private float Place(
        MathNode? node,
        float x,
        float baselineY,
        float fs,
        int lineIndex,
        List<MathTextFragment> fragments,
        List<MathRule> rules,
        List<MathDelimiter> delimiters)
    {
        if (node is null)
        {
            return 0f;
        }

        switch (node.Kind)
        {
            case MathNodeKind.Run:
            {
                var run = (MathRun)node;
                if (string.IsNullOrEmpty(run.Text))
                {
                    return 0f;
                }

                MathTextMetrics metrics = _measurer.Measure(run.Text, fs, run.Italic, run.Bold);
                fragments.Add(new MathTextFragment(run.Text, x, baselineY, fs, run.Italic, run.Bold, Math.Max(0f, metrics.Width), lineIndex));
                return Math.Max(0f, metrics.Width);
            }

            case MathNodeKind.Space:
                return Math.Max(0f, ((MathSpace)node).Em) * fs;

            case MathNodeKind.LineBreak:
                return 0f;

            case MathNodeKind.Row:
            {
                var row = (MathRow)node;
                float cursor = x;
                foreach (MathNode child in row.Children)
                {
                    cursor += Place(child, cursor, baselineY, fs, lineIndex, fragments, rules, delimiters);
                }

                return cursor - x;
            }

            case MathNodeKind.Fraction:
                return PlaceFraction((MathFraction)node, x, baselineY, fs, lineIndex, fragments, rules, delimiters);

            case MathNodeKind.Sqrt:
                return PlaceSqrt((MathSqrt)node, x, baselineY, fs, lineIndex, fragments, rules, delimiters);

            case MathNodeKind.Script:
            {
                var script = (MathScript)node;
                float baseWidth = Place(script.Base, x, baselineY, fs, lineIndex, fragments, rules, delimiters);
                float scriptSize = fs * _o.ScriptScale;
                float kern = fs * 0.03f;
                float scriptX = x + baseWidth + kern;
                if (script.Subscript is not null)
                {
                    Place(script.Subscript, scriptX, baselineY + fs * _o.ScriptDownShift, scriptSize, lineIndex, fragments, rules, delimiters);
                }

                if (script.Superscript is not null)
                {
                    Place(script.Superscript, scriptX, baselineY - fs * _o.ScriptUpShift, scriptSize, lineIndex, fragments, rules, delimiters);
                }

                return Measure(script, fs).Width;
            }

            case MathNodeKind.Accent:
                return PlaceAccent((MathAccent)node, x, baselineY, fs, lineIndex, fragments, rules, delimiters);

            case MathNodeKind.Matrix:
                return PlaceMatrix((MathMatrix)node, x, baselineY, fs, lineIndex, fragments, rules, delimiters);

            default:
                return 0f;
        }
    }

    private float PlaceFraction(
        MathFraction fraction,
        float x,
        float baselineY,
        float fs,
        int lineIndex,
        List<MathTextFragment> fragments,
        List<MathRule> rules,
        List<MathDelimiter> delimiters)
    {
        float childSize = fs * _o.FractionScale;
        Box numerator = Measure(fraction.Numerator, childSize);
        Box denominator = Measure(fraction.Denominator, childSize);
        float width = Math.Max(numerator.Width, denominator.Width) + fs * 0.14f;
        float barThickness = Math.Max(1f, fs * _o.FractionBarThickness);
        float gap = fs * _o.FractionGap;
        float barY = baselineY - fs * _o.FractionAxis;

        float numeratorBaseline = barY - gap - numerator.Descent;
        float denominatorBaseline = barY + gap + denominator.Ascent;
        Place(fraction.Numerator, x + (width - numerator.Width) * 0.5f, numeratorBaseline, childSize, lineIndex, fragments, rules, delimiters);
        Place(fraction.Denominator, x + (width - denominator.Width) * 0.5f, denominatorBaseline, childSize, lineIndex, fragments, rules, delimiters);
        rules.Add(new MathRule(
            MathRuleKind.FractionBar,
            x + fs * 0.04f,
            barY,
            width - fs * 0.08f,
            0f,
            barThickness,
            lineIndex));
        return width;
    }

    private float PlaceSqrt(
        MathSqrt sqrt,
        float x,
        float baselineY,
        float fs,
        int lineIndex,
        List<MathTextFragment> fragments,
        List<MathRule> rules,
        List<MathDelimiter> delimiters)
    {
        Box radicand = Measure(sqrt.Radicand, fs * 0.98f);
        float radicalWidth = fs * _o.SqrtRadicalWidth;
        float indexWidth = 0f;
        float indexBaseline = 0f;
        if (sqrt.Index is not null)
        {
            Box indexBox = Measure(sqrt.Index, fs * 0.55f);
            indexWidth = indexBox.Width * 0.7f;
            indexBaseline = baselineY - radicand.Ascent - fs * _o.SqrtTopPad + indexBox.Descent;
            Place(sqrt.Index, x, indexBaseline, fs * 0.55f, lineIndex, fragments, rules, delimiters);
        }

        float top = baselineY - radicand.Ascent - fs * _o.SqrtTopPad;
        float bottom = baselineY + radicand.Descent + fs * _o.SqrtBottomPad;
        float thickness = Math.Max(1f, fs * _o.SqrtRuleThickness);
        float radicalX = x + indexWidth;
        rules.Add(new MathRule(MathRuleKind.SqrtRadical, radicalX, top, radicalWidth, bottom - top, thickness, lineIndex));
        rules.Add(new MathRule(
            MathRuleKind.SqrtBar,
            radicalX + radicalWidth,
            top,
            radicand.Width + fs * 0.06f,
            0f,
            thickness,
            lineIndex));
        Place(sqrt.Radicand, radicalX + radicalWidth, baselineY, fs * 0.98f, lineIndex, fragments, rules, delimiters);
        return indexWidth + radicalWidth + radicand.Width + fs * 0.06f;
    }

    private float PlaceAccent(
        MathAccent accent,
        float x,
        float baselineY,
        float fs,
        int lineIndex,
        List<MathTextFragment> fragments,
        List<MathRule> rules,
        List<MathDelimiter> delimiters)
    {
        Box baseBox = Measure(accent.Base, fs);
        float width = Place(accent.Base, x, baselineY, fs, lineIndex, fragments, rules, delimiters);
        float markWidth = Math.Max(fs * 0.22f, Math.Min(baseBox.Width * 0.75f, fs * 0.55f));
        float markX = x + (baseBox.Width - markWidth) * 0.5f;
        float thickness = Math.Max(1f, fs * 0.055f);
        switch (accent.Accent)
        {
            case MathAccentKind.Hat:
                rules.Add(new MathRule(
                    MathRuleKind.Hat,
                    markX,
                    baselineY - baseBox.Ascent - fs * _o.AccentHeight,
                    markWidth,
                    fs * 0.13f,
                    thickness,
                    lineIndex));
                break;

            case MathAccentKind.Dot:
                rules.Add(new MathRule(
                    MathRuleKind.Dot,
                    x + baseBox.Width * 0.5f,
                    baselineY - baseBox.Ascent - fs * 0.13f,
                    Math.Max(1.5f, fs * 0.11f),
                    0f,
                    thickness,
                    lineIndex));
                break;

            case MathAccentKind.DoubleDot:
                float diameter = Math.Max(1.5f, fs * 0.10f);
                float dotY = baselineY - baseBox.Ascent - fs * 0.13f;
                rules.Add(new MathRule(MathRuleKind.Dot, x + baseBox.Width * 0.5f - fs * 0.10f, dotY, diameter, 0f, thickness, lineIndex));
                rules.Add(new MathRule(MathRuleKind.Dot, x + baseBox.Width * 0.5f + fs * 0.10f, dotY, diameter, 0f, thickness, lineIndex));
                break;
        }

        return width;
    }

    // ==================================================================
    // Matrices
    // ==================================================================
    private sealed class MatrixMetrics
    {
        public float CellFontSize;
        public int RowCount;
        public int ColumnCount;
        public Box[][] Cells = Array.Empty<Box[]>();
        public float[] ColumnWidths = Array.Empty<float>();
        public float[] RowAscents = Array.Empty<float>();
        public float[] RowDescents = Array.Empty<float>();
        public float[] RowTops = Array.Empty<float>();
        public float ColumnGap;
        public float RowGap;
        public float ContentWidth;
        public float ContentHeight;
        public float LeftBracketWidth;
        public float RightBracketWidth;
        public float RuleThickness;
        public Box Box;
    }

    private MatrixMetrics ComputeMatrix(MathMatrix matrix, float fs)
    {
        float cellSize = fs * _o.MatrixScale;
        int rowCount = matrix.Rows.Count;
        int columnCount = 0;
        foreach (IReadOnlyList<MathRow> row in matrix.Rows)
        {
            columnCount = Math.Max(columnCount, row.Count);
        }

        var metrics = new MatrixMetrics
        {
            CellFontSize = cellSize,
            RowCount = rowCount,
            ColumnCount = columnCount,
            RuleThickness = Math.Max(1f, fs * 0.045f),
            ColumnGap = fs * _o.MatrixColumnGap,
            RowGap = fs * _o.MatrixRowGap,
        };

        bool hasLeft = matrix.MatrixKind is MathMatrixKind.Bracket or MathMatrixKind.Parenthesis or MathMatrixKind.Cases or MathMatrixKind.Bar;
        bool hasRight = matrix.MatrixKind is MathMatrixKind.Bracket or MathMatrixKind.Parenthesis or MathMatrixKind.Bar;
        // A cases brace is drawn as a wider scalable polyline, brackets/parens are thinner.
        float leftBracketEm = matrix.MatrixKind == MathMatrixKind.Cases ? _o.MatrixBraceWidth : _o.MatrixBracketWidth;
        metrics.LeftBracketWidth = hasLeft ? fs * leftBracketEm : 0f;
        metrics.RightBracketWidth = hasRight ? fs * _o.MatrixBracketWidth : 0f;

        if (rowCount == 0 || columnCount == 0)
        {
            metrics.Box = new Box(0f, fs * 0.6f, fs * 0.2f);
            return metrics;
        }

        metrics.Cells = new Box[rowCount][];
        metrics.ColumnWidths = new float[columnCount];
        metrics.RowAscents = new float[rowCount];
        metrics.RowDescents = new float[rowCount];
        metrics.RowTops = new float[rowCount];

        for (int r = 0; r < rowCount; r++)
        {
            metrics.Cells[r] = new Box[columnCount];
            IReadOnlyList<MathRow> row = matrix.Rows[r];
            float rowAscent = fs * 0.4f;
            float rowDescent = fs * 0.1f;
            for (int c = 0; c < columnCount; c++)
            {
                Box cellBox = c < row.Count ? Measure(row[c], cellSize) : Box.Empty;
                metrics.Cells[r][c] = cellBox;
                metrics.ColumnWidths[c] = Math.Max(metrics.ColumnWidths[c], cellBox.Width);
                rowAscent = Math.Max(rowAscent, cellBox.Ascent);
                rowDescent = Math.Max(rowDescent, cellBox.Descent);
            }

            metrics.RowAscents[r] = rowAscent;
            metrics.RowDescents[r] = rowDescent;
        }

        float contentWidth = 0f;
        for (int c = 0; c < columnCount; c++)
        {
            contentWidth += metrics.ColumnWidths[c];
            if (c < columnCount - 1)
            {
                contentWidth += metrics.ColumnGap;
            }
        }

        float contentHeight = 0f;
        for (int r = 0; r < rowCount; r++)
        {
            contentHeight += metrics.RowAscents[r] + metrics.RowDescents[r];
            if (r < rowCount - 1)
            {
                contentHeight += metrics.RowGap;
            }
        }

        metrics.ContentWidth = contentWidth;
        metrics.ContentHeight = contentHeight;
        float axis = fs * 0.28f;
        float ascent = contentHeight * 0.5f + axis;
        float descent = contentHeight * 0.5f - axis;
        float minDescent = fs * 0.12f;
        if (descent < minDescent)
        {
            ascent += minDescent - descent;
            descent = minDescent;
        }

        metrics.Box = new Box(
            metrics.LeftBracketWidth + contentWidth + metrics.RightBracketWidth,
            ascent,
            descent);
        return metrics;
    }

    private float PlaceMatrix(
        MathMatrix matrix,
        float x,
        float baselineY,
        float fs,
        int lineIndex,
        List<MathTextFragment> fragments,
        List<MathRule> rules,
        List<MathDelimiter> delimiters)
    {
        MatrixMetrics metrics = ComputeMatrix(matrix, fs);
        if (metrics.RowCount == 0 || metrics.ColumnCount == 0)
        {
            return metrics.Box.Width;
        }

        float contentTop = baselineY - metrics.Box.Ascent;
        float yTop = contentTop;
        for (int r = 0; r < metrics.RowCount; r++)
        {
            metrics.RowTops[r] = yTop;
            IReadOnlyList<MathRow> row = matrix.Rows[r];
            float rowBaseline = yTop + metrics.RowAscents[r];
            float cellX = x + metrics.LeftBracketWidth;
            for (int c = 0; c < metrics.ColumnCount; c++)
            {
                if (c < row.Count)
                {
                    Box cellBox = metrics.Cells[r][c];
                    float offset = (metrics.ColumnWidths[c] - cellBox.Width) * 0.5f;
                    Place(row[c], cellX + offset, rowBaseline, metrics.CellFontSize, lineIndex, fragments, rules, delimiters);
                }

                cellX += metrics.ColumnWidths[c] + metrics.ColumnGap;
            }

            yTop += metrics.RowAscents[r] + metrics.RowDescents[r] + metrics.RowGap;
        }

        float bracketTop = contentTop - fs * 0.06f;
        float bracketHeight = metrics.ContentHeight + fs * 0.12f;
        if (metrics.LeftBracketWidth > 0f)
        {
            delimiters.Add(new MathDelimiter(
                MapDelimiterStyle(matrix.MatrixKind),
                x,
                bracketTop,
                metrics.LeftBracketWidth,
                bracketHeight,
                isClosing: false,
                lineIndex));
        }

        if (metrics.RightBracketWidth > 0f)
        {
            delimiters.Add(new MathDelimiter(
                MapDelimiterStyle(matrix.MatrixKind),
                x + metrics.Box.Width - metrics.RightBracketWidth,
                bracketTop,
                metrics.RightBracketWidth,
                bracketHeight,
                isClosing: true,
                lineIndex));
        }

        float ruleX = x + metrics.LeftBracketWidth;
        foreach (int ruleRow in matrix.RulesAboveRows)
        {
            if (ruleRow < 0 || ruleRow > metrics.RowCount)
            {
                continue;
            }

            float ruleY = ruleRow switch
            {
                0 => contentTop - fs * 0.04f,
                _ when ruleRow >= metrics.RowCount => contentTop + metrics.ContentHeight + fs * 0.06f,
                _ => metrics.RowTops[ruleRow] - metrics.RowGap * 0.5f,
            };
            rules.Add(new MathRule(MathRuleKind.MatrixRule, ruleX, ruleY, metrics.ContentWidth, 0f, metrics.RuleThickness, lineIndex));
        }

        return metrics.Box.Width;
    }

    private static MathDelimiterStyle MapDelimiterStyle(MathMatrixKind kind) => kind switch
    {
        MathMatrixKind.Bracket => MathDelimiterStyle.SquareBracket,
        MathMatrixKind.Parenthesis => MathDelimiterStyle.Parenthesis,
        MathMatrixKind.Cases => MathDelimiterStyle.Brace,
        MathMatrixKind.Bar => MathDelimiterStyle.Bar,
        _ => MathDelimiterStyle.SquareBracket,
    };

    // ==================================================================
    // Line breaking
    // ==================================================================
    private sealed class Atom
    {
        public MathNode? Node;
        public bool IsGlue;
        public float Width;
        public float Ascent;
        public float Descent;
    }

    private sealed class Segment
    {
        public List<Atom> Atoms = new();
        public bool ForceBreakBefore;
    }

    private List<Segment> BuildSegments(MathNode root, float fs)
    {
        var segments = new List<Segment>();
        var current = new Segment();
        bool pendingForce = false;

        void Flush()
        {
            current.ForceBreakBefore = pendingForce;
            pendingForce = false;
            segments.Add(current);
            current = new Segment();
        }

        void AddAtom(MathNode node)
        {
            if (node is MathLineBreak)
            {
                Flush();
                pendingForce = true;
                return;
            }

            if (node is MathSpace space)
            {
                current.Atoms.Add(new Atom
                {
                    Node = node,
                    IsGlue = true,
                    Width = Math.Max(0f, space.Em) * fs,
                });
                if (space.CanBreakAfter)
                {
                    Flush();
                }

                return;
            }

            Box box = Measure(node, fs);
            current.Atoms.Add(new Atom
            {
                Node = node,
                IsGlue = false,
                Width = box.Width,
                Ascent = box.Ascent,
                Descent = box.Descent,
            });

            if (node is MathRun run && run.CanBreakAfter)
            {
                Flush();
            }
        }

        if (root is MathRow row)
        {
            foreach (MathNode child in row.Children)
            {
                if (child is MathRow nested)
                {
                    foreach (MathNode nestedChild in nested.Children)
                    {
                        AddAtom(nestedChild);
                    }
                }
                else
                {
                    AddAtom(child);
                }
            }
        }
        else
        {
            AddAtom(root);
        }

        Flush();
        return segments;
    }

    private List<List<Atom>> PackLines(List<Segment> segments, float maxWidth, float fs)
    {
        var lines = new List<List<Atom>>();
        var line = new List<Atom>();

        foreach (Segment segment in segments)
        {
            if (segment.ForceBreakBefore && (line.Count > 0 || lines.Count > 0))
            {
                lines.Add(TrimLine(line));
                line = new List<Atom>();
            }

            bool lineEmpty = line.Count == 0;
            float lineWidth = SumWidths(line);

            // Width the segment contributes when appended to the current line.
            // Leading glue is dropped when the segment starts a fresh line, and a
            // trailing glue run is trimmed if the line ends there, so it does not
            // count against the limit.
            float addition = 0f;
            foreach (Atom atom in segment.Atoms)
            {
                if (lineEmpty && atom.IsGlue)
                {
                    continue;
                }

                addition += atom.Width;
            }

            float trailingGlue = TrailingGlueWidth(segment.Atoms);
            if (!lineEmpty && lineWidth + addition - trailingGlue > maxWidth + 0.01f)
            {
                lines.Add(TrimLine(line));
                line = new List<Atom>();
                addition = 0f;
                foreach (Atom atom in segment.Atoms)
                {
                    if (atom.IsGlue)
                    {
                        continue;
                    }

                    addition += atom.Width;
                }
            }

            foreach (Atom atom in segment.Atoms)
            {
                if (line.Count == 0 && atom.IsGlue)
                {
                    continue;
                }

                line.Add(atom);
            }
        }

        if (line.Count > 0 || lines.Count == 0)
        {
            lines.Add(TrimLine(line));
        }

        return lines;
    }

    private static float SumWidths(List<Atom> atoms)
    {
        float width = 0f;
        foreach (Atom atom in atoms)
        {
            width += atom.Width;
        }

        return width;
    }

    private static float TrailingGlueWidth(List<Atom> atoms)
    {
        float width = 0f;
        for (int i = atoms.Count - 1; i >= 0 && atoms[i].IsGlue; i--)
        {
            width += atoms[i].Width;
        }

        return width;
    }

    private static List<Atom> TrimLine(List<Atom> line)
    {
        var trimmed = new List<Atom>(line);
        while (trimmed.Count > 0 && trimmed[^1].IsGlue)
        {
            trimmed.RemoveAt(trimmed.Count - 1);
        }

        while (trimmed.Count > 0 && trimmed[0].IsGlue)
        {
            trimmed.RemoveAt(0);
        }

        return trimmed;
    }
}
