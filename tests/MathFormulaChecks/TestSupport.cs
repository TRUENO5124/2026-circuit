// ============================================================================
// File: tests/MathFormulaChecks/TestSupport.cs
// Description: Deterministic fake measurer + shared helpers for the math
//              renderer checks. The fake measurer is purely geometric (no
//              DirectWrite) so layout results are reproducible headlessly.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.MathRendering;

namespace MathFormulaChecks;

internal static class TestSupport
{
    public const float BaseFontSize = 16f;

    public static readonly FakeMathTextMeasurer Measurer = new();

    public static readonly MathLayoutEngine Engine = new(Measurer);

    public static MathNode Parse(string latex) => MathLatexParser.Parse(latex);

    public static MathRow ParseRow(string latex) => MathLatexParser.ParseRow(latex);

    public static MathLayoutResult Layout(string latex, float width = 640f, float fontSize = BaseFontSize) =>
        Engine.Layout(MathLatexParser.Parse(latex), width, fontSize);

    public static bool HasFragment(MathLayoutResult result, string text) =>
        result.Fragments.Any(f => string.Equals(f.Text, text, StringComparison.Ordinal));

    public static MathTextFragment? Fragment(MathLayoutResult result, string text) =>
        result.Fragments.FirstOrDefault(f => string.Equals(f.Text, text, StringComparison.Ordinal));

    public static MathTextFragment NeedFragment(MathLayoutResult result, string text, string checkName)
    {
        MathTextFragment? fragment = Fragment(result, text);
        if (fragment is null)
        {
            CheckResult.Check(false, checkName, $"未找到文本片元 \"{text}\"，实际片元：{Describe(result)}");
            throw new InvalidOperationException($"missing fragment {text}");
        }

        return fragment;
    }

    public static string JoinedText(MathLayoutResult result) =>
        string.Concat(result.Fragments.Select(f => f.Text));

    public static bool ContainsAnywhere(MathLayoutResult result, string token) =>
        result.Fragments.Any(f => f.Text.Contains(token, StringComparison.Ordinal));

    public static int DistinctLineCount(MathLayoutResult result) =>
        result.Fragments.Select(f => f.LineIndex).Distinct().Count();

    public static float MaxRightEdge(MathLayoutResult result) =>
        result.Fragments.Select(f => f.Right).DefaultIfEmpty(0f).Max();

    public static float MaxRightEdgeOnLine(MathLayoutResult result, int lineIndex) =>
        result.Fragments.Where(f => f.LineIndex == lineIndex).Select(f => f.Right).DefaultIfEmpty(0f).Max();

    /// <summary>Asserts no LaTeX scaffolding survives into the rendered glyphs.</summary>
    public static void CheckNoLatexMarkup(MathLayoutResult result, string name)
    {
        bool ok = true;
        string detail = string.Empty;
        foreach (MathTextFragment fragment in result.Fragments)
        {
            foreach (char bad in new[] { '_', '^', '\\', '{', '}' })
            {
                if (fragment.Text.IndexOf(bad) >= 0)
                {
                    ok = false;
                    detail = $"片元 \"{fragment.Text}\" 中仍含未处理字符 '{bad}'";
                    break;
                }
            }

            if (!ok)
            {
                break;
            }
        }

        CheckResult.Check(ok, name, detail);
    }

    /// <summary>Asserts every fragment stays inside the width handed to the layout engine.</summary>
    public static void CheckFitsWidth(MathLayoutResult result, float width, string name)
    {
        bool ok = true;
        string detail = string.Empty;
        foreach (MathTextFragment fragment in result.Fragments)
        {
            if (fragment.X < -0.01f || fragment.Right > width + 0.5f)
            {
                ok = false;
                detail = $"片元 \"{fragment.Text}\" x={fragment.X:0.##} right={fragment.Right:0.##} 超出宽度 {width}";
                break;
            }
        }

        CheckResult.Check(ok, name, detail);
    }

    /// <summary>Asserts that no line's content overflows the available width.</summary>
    public static void CheckLinesFitWidth(MathLayoutResult result, float width, string name)
    {
        bool ok = true;
        string detail = string.Empty;
        foreach (int line in result.Fragments.Select(f => f.LineIndex).Distinct())
        {
            float right = MaxRightEdgeOnLine(result, line);
            if (right > width + 0.5f)
            {
                ok = false;
                detail = $"第 {line} 行右边缘 {right:0.##} 超出宽度 {width}";
                break;
            }
        }

        CheckResult.Check(ok, name, detail);
    }

    public static string Describe(MathLayoutResult result) =>
        result.Fragments.Count == 0
            ? "（空）"
            : string.Join(" | ", result.Fragments.Select(f => $"'{f.Text}'@{f.FontSize:0.#}"));

    /// <summary>Greedy-width, metric-stable fake: advance = sum of em widths, line ascent/descent fixed per size.</summary>
    internal sealed class FakeMathTextMeasurer : IMathTextMeasurer
    {
        public MathTextMetrics Measure(string text, float fontSize, bool italic, bool bold)
        {
            if (fontSize <= 0f)
            {
                return new MathTextMetrics(0f, 0f, 0f);
            }

            float ascent = fontSize * 0.78f;
            float descent = fontSize * 0.24f;
            if (string.IsNullOrEmpty(text))
            {
                return new MathTextMetrics(0f, ascent, descent);
            }

            float em = 0f;
            foreach (char c in text)
            {
                em += EstimateMathTextMeasurer.CharWidthEm(c);
            }

            if (italic)
            {
                em += 0.02f;
            }

            if (bold)
            {
                em += 0.03f * text.Length;
            }

            return new MathTextMetrics(em * fontSize, ascent, descent);
        }
    }
}
