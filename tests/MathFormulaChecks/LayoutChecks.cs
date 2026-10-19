// ============================================================================
// File: tests/MathFormulaChecks/LayoutChecks.cs
// Description: Layout-engine checks: true sub/superscripts, stacked fractions,
//              roots, matrix grids, automatic wrapping, fit-to-width fallback,
//              italics/upright rules and baseline alignment.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.MathRendering;

namespace MathFormulaChecks;

internal static class LayoutChecks
{
    private const string FormulaBe =
        @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}}";

    private const string FormulaVth =
        @"V_{th}=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}}";

    private const string FormulaIbq =
        @"I_{BQ}=\frac{V_{th}-V_{BE}}{R_{th}+(1+\beta)R_e}";

    public static void Run()
    {
        CheckResult.Section("布局：基础字号与真角标");

        MathLayoutResult simple = TestSupport.Layout(@"x=1");
        CheckResult.Check(simple.LineCount == 1, "x=1 单行布局", $"lines={simple.LineCount}");
        MathTextFragment? xFragment = TestSupport.Fragment(simple, "x");
        CheckResult.Check(
            xFragment is not null && Math.Abs(xFragment.FontSize - TestSupport.BaseFontSize) < 0.01f,
            "顶层变量使用基础字号 16",
            xFragment is null ? "缺 x 片元" : $"fs={xFragment.FontSize}");
        CheckResult.Check(
            xFragment is not null && xFragment.Italic,
            "数学变量 x 使用斜体");

        MathLayoutResult scripts = TestSupport.Layout(@"x^2 + y_1");
        MathTextFragment? baseX = TestSupport.Fragment(scripts, "x");
        MathTextFragment? supTwo = TestSupport.Fragment(scripts, "2");
        MathTextFragment? baseY = TestSupport.Fragment(scripts, "y");
        MathTextFragment? subOne = TestSupport.Fragment(scripts, "1");
        CheckResult.Check(supTwo is not null && baseX is not null, "x^2 上标片元存在");
        CheckResult.Check(subOne is not null && baseY is not null, "y_1 下标片元存在");
        if (supTwo is not null && baseX is not null)
        {
            CheckResult.Check(
                Math.Abs(supTwo.FontSize - TestSupport.BaseFontSize * 0.72f) < 0.05f,
                "上标字号缩小到约 0.72 倍",
                $"fs={supTwo.FontSize:0.###}");
            CheckResult.Check(
                supTwo.BaselineY < baseX.BaselineY - 1.0f,
                "上标基线高于主基线（真正的上标，不是下划线）",
                $"supY={supTwo.BaselineY:0.##} baseY={baseX.BaselineY:0.##}");
        }

        if (subOne is not null && baseY is not null)
        {
            CheckResult.Check(
                Math.Abs(subOne.FontSize - TestSupport.BaseFontSize * 0.72f) < 0.05f,
                "下标字号缩小到约 0.72 倍",
                $"fs={subOne.FontSize:0.###}");
            CheckResult.Check(
                subOne.BaselineY > baseY.BaselineY + 1.0f,
                "下标基线低于主基线（真正的下标，没有下划线字符）",
                $"subY={subOne.BaselineY:0.##} baseY={baseY.BaselineY:0.##}");
        }

        MathLayoutResult bothScripts = TestSupport.Layout(@"\omega_0^2");
        CheckResult.Check(
            bothScripts.Fragments.Count(f => f.FontSize < TestSupport.BaseFontSize * 0.9f) == 2,
            "同一基底同时带上下标时产生两个缩小片元",
            TestSupport.Describe(bothScripts));

        MathLayoutResult primeLayout = TestSupport.Layout(@"R_{in}'");
        MathTextFragment? rFragment = TestSupport.Fragment(primeLayout, "R");
        MathTextFragment? primeFragment = TestSupport.Fragment(primeLayout, "\u2032");
        CheckResult.Check(
            rFragment is not null && primeFragment is not null
            && primeFragment.FontSize < rFragment.FontSize
            && primeFragment.BaselineY < rFragment.BaselineY,
            "撇号渲染为右上角真正的上标 ′");

        CheckResult.Section("布局：典型公式核心验收（无 LaTeX 残留 / 真角标）");

        foreach ((string latex, string label) in new[]
                 {
                     (FormulaBe, "r_be 公式"),
                     (FormulaVth, "V_th 公式"),
                     (FormulaIbq, "I_BQ 公式"),
                 })
        {
            MathLayoutResult layout = TestSupport.Layout(latex);
            TestSupport.CheckNoLatexMarkup(layout, $"{label}：片元中无 _ ^ \\ {{ }}");
            CheckResult.Check(
                layout.Fragments.Any(f => f.FontSize < TestSupport.BaseFontSize * 0.9f),
                $"{label}：存在缩小字号的角标片元");
            CheckResult.Check(
                layout.Rules.Any(r => r.Kind == MathRuleKind.FractionBar),
                $"{label}：存在分式横线");
            CheckResult.Check(
                layout.LineCount == 1,
                $"{label}：宽 640 时单行显示",
                $"lines={layout.LineCount}");
        }

        CheckResult.Section("布局：分式上下叠放与居中");

        MathLayoutResult fractionLayout = TestSupport.Layout(@"\frac{U_T}{I_{EQ}}");
        MathTextFragment? numU = TestSupport.Fragment(fractionLayout, "U");
        MathTextFragment? numT = TestSupport.Fragment(fractionLayout, "T");
        MathTextFragment? denI = TestSupport.Fragment(fractionLayout, "I");
        MathTextFragment? denE = TestSupport.Fragment(fractionLayout, "EQ");
        CheckResult.Check(numU is not null && numT is not null && denI is not null && denE is not null, "分子 U_T 与分母 I_EQ 的片元都出现", TestSupport.Describe(fractionLayout));
        if (numU is not null && denI is not null)
        {
            CheckResult.Check(
                numU.BaselineY < denI.BaselineY - 2f,
                "分子基线在分母基线之上（上下叠放）",
                $"numY={numU.BaselineY:0.##} denY={denI.BaselineY:0.##}");
        }

        MathRule? bar = fractionLayout.Rules.FirstOrDefault(r => r.Kind == MathRuleKind.FractionBar);
        CheckResult.Check(bar is not null, "分式存在横线规则");
        if (bar is not null && numU is not null && denI is not null)
        {
            CheckResult.Check(
                bar.Y > numU.BaselineY && bar.Y < denI.BaselineY,
                "分式横线位于分子基线与分母基线之间",
                $"barY={bar.Y:0.##} numY={numU.BaselineY:0.##} denY={denI.BaselineY:0.##}");
            CheckResult.Check(bar.Width > 8f, "分式横线宽度覆盖内容", $"width={bar.Width:0.##}");
        }

        MathLayoutResult centeredFraction = TestSupport.Layout(@"\frac{abc}{de}");
        MathTextFragment? centeredNumerator = TestSupport.Fragment(centeredFraction, "abc");
        MathTextFragment? centeredDenominator = TestSupport.Fragment(centeredFraction, "de");
        if (centeredNumerator is not null && centeredDenominator is not null)
        {
            float numeratorCenter = (centeredNumerator.X + centeredNumerator.Right) * 0.5f;
            float denominatorCenter = (centeredDenominator.X + centeredDenominator.Right) * 0.5f;
            CheckResult.Check(
                Math.Abs(numeratorCenter - denominatorCenter) < 1.0f,
                "分子与分母水平居中于分式横线",
                $"numCenter={numeratorCenter:0.##} denCenter={denominatorCenter:0.##}");
        }

        CheckResult.Check(
            TestSupport.Layout(@"\frac{U_T}{I_{EQ}}", 640f).Height > TestSupport.Layout(@"x=1", 640f).Height,
            "分式行高高于普通单行（公式框自动更高）");

        CheckResult.Section("布局：根号");

        MathLayoutResult sqrtLayout = TestSupport.Layout(@"\sqrt{LC}");
        MathRule? radical = sqrtLayout.Rules.FirstOrDefault(r => r.Kind == MathRuleKind.SqrtRadical);
        MathRule? sqrtBar = sqrtLayout.Rules.FirstOrDefault(r => r.Kind == MathRuleKind.SqrtBar);
        MathTextFragment? lFragment = TestSupport.Fragment(sqrtLayout, "L");
        CheckResult.Check(radical is not null, @"\sqrt 生成根号符号几何");
        CheckResult.Check(sqrtBar is not null, @"\sqrt 生成上划线横线");
        if (radical is not null && lFragment is not null)
        {
            CheckResult.Check(
                lFragment.X > radical.X + radical.Width * 0.9f,
                "被开方数位于根号右侧",
                $"L.x={lFragment.X:0.##} radicalRight={(radical.X + radical.Width):0.##}");
        }

        if (sqrtBar is not null && lFragment is not null)
        {
            CheckResult.Check(
                sqrtBar.Y < lFragment.BaselineY - lFragment.FontSize * 0.5f,
                "根号上划线在被开方数上方",
                $"barY={sqrtBar.Y:0.##} L.baseline={lFragment.BaselineY:0.##}");
        }

        MathLayoutResult sqrtFraction = TestSupport.Layout(@"f_0=\frac{1}{2\pi\sqrt{LC}}");
        CheckResult.Check(
            sqrtFraction.Rules.Any(r => r.Kind == MathRuleKind.SqrtRadical)
            && sqrtFraction.Rules.Any(r => r.Kind == MathRuleKind.FractionBar),
            "根号与分式可嵌套");
        TestSupport.CheckNoLatexMarkup(sqrtFraction, "嵌套根号/分式无 LaTeX 残留");

        CheckResult.Section("布局：自动换行（不横向裁切）");

        foreach ((string latex, string label) in new[]
                 {
                     (FormulaBe, "r_be 公式"),
                     (FormulaVth, "V_th 公式"),
                     (FormulaIbq, "I_BQ 公式"),
                 })
        {
            MathLayoutResult narrow = TestSupport.Layout(latex, 110f);
            MathLayoutResult wide = TestSupport.Layout(latex, 640f);
            CheckResult.Check(
                narrow.LineCount >= 2,
                $"{label}：宽 110 时自动换成多行",
                $"lines={narrow.LineCount}");
            CheckResult.Check(
                TestSupport.DistinctLineCount(narrow) == narrow.LineCount,
                $"{label}：LineCount 与实际行数一致",
                $"lines={narrow.LineCount} distinct={TestSupport.DistinctLineCount(narrow)}");
            CheckResult.Check(
                !narrow.ScaledToFit,
                $"{label}：窄宽度下通过换行而非缩小字号实现",
                $"scaled={narrow.ScaledToFit}");
            TestSupport.CheckLinesFitWidth(narrow, 110f, $"{label}：每一行都不超过可用宽度");
            TestSupport.CheckNoLatexMarkup(narrow, $"{label}：换行后无 LaTeX 残留");
            CheckResult.Check(
                narrow.Height > wide.Height,
                $"{label}：换行后公式框高度增加",
                $"narrow={narrow.Height:0.##} wide={wide.Height:0.##}");
        }

        MathLayoutResult quadWrap = TestSupport.Layout(@"a\quad b\quad c", 52f);
        CheckResult.Check(
            quadWrap.LineCount >= 2,
            @"\quad 处允许换行",
            $"lines={quadWrap.LineCount} maxRight={TestSupport.MaxRightEdge(quadWrap):0.##}");

        MathLayoutResult overflow = TestSupport.Layout(@"\frac{a+b+c+d+e+f+g+h}{1}", 60f);
        CheckResult.Check(
            overflow.ScaledToFit,
            "单一不可拆分原子过宽时整体等比缩小而非裁切",
            $"scaled={overflow.ScaledToFit} maxRight={TestSupport.MaxRightEdge(overflow):0.##}");
        TestSupport.CheckFitsWidth(overflow, 60f, "等比缩小后仍不超出可用宽度");

        CheckResult.Section("布局：直立/斜体与数字");

        MathLayoutResult mixed = TestSupport.Layout(@"I_D=12.3\text{ mA}");
        MathTextFragment? number = TestSupport.Fragment(mixed, "12.3");
        MathTextFragment? unit = mixed.Fragments.FirstOrDefault(f => f.Text.Contains("mA", StringComparison.Ordinal));
        CheckResult.Check(number is not null && !number.Italic, "数字 12.3 直立", TestSupport.Describe(mixed));
        CheckResult.Check(unit is not null && !unit.Italic, @"\text{ mA} 单位直立");
        CheckResult.Check(
            mixed.Fragments.FirstOrDefault(f => f.Text == "I")?.Italic == true,
            "数学变量 I 斜体");

        MathLayoutResult scientific = TestSupport.Layout(@"6.28 \times 10^{3}");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(scientific, "10")
            && scientific.Fragments.Any(f => f.Text == "3" && f.FontSize < TestSupport.BaseFontSize * 0.9f)
            && TestSupport.ContainsAnywhere(scientific, "\u00D7"),
            "科学计数法 6.28 × 10³ 渲染为真上标",
            TestSupport.Describe(scientific));
        TestSupport.CheckNoLatexMarkup(scientific, "科学计数法无 LaTeX 残留");

        MathLayoutResult boldLayout = TestSupport.Layout(@"\mathbf{A}^{-1}\mathbf{z}");
        MathTextFragment? boldA = TestSupport.Fragment(boldLayout, "A");
        MathTextFragment? boldZ = TestSupport.Fragment(boldLayout, "z");
        CheckResult.Check(
            boldA is not null && boldA.Bold && !boldA.Italic,
            @"\mathbf{A} 粗体直立",
            boldA is null ? "缺 A" : $"bold={boldA.Bold} italic={boldA.Italic}");
        CheckResult.Check(
            boldZ is not null && boldZ.Bold,
            @"\mathbf{z} 粗体");

        MathLayoutResult cjkLayout = TestSupport.Layout(@"\text{状态}=\text{饱和区}");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(cjkLayout, "状态")
            && TestSupport.ContainsAnywhere(cjkLayout, "饱和区"),
            @"\text 中的中文被保留",
            TestSupport.Describe(cjkLayout));
        TestSupport.CheckNoLatexMarkup(cjkLayout, "中文公式无 LaTeX 残留");

        CheckResult.Section("布局：重音与希腊/运算符组合");

        MathLayoutResult hatLayout = TestSupport.Layout(@"\hat{V}_{out}");
        CheckResult.Check(
            hatLayout.Rules.Any(r => r.Kind == MathRuleKind.Hat),
            @"\hat 生成帽子几何");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(hatLayout, "out")
            && hatLayout.Fragments.FirstOrDefault(f => f.Text == "out")?.FontSize < TestSupport.BaseFontSize * 0.9f,
            @"\hat{V}_{out} 的 out 为缩小下标");

        MathLayoutResult dotLayout = TestSupport.Layout(@"\dot{U}");
        CheckResult.Check(
            dotLayout.Rules.Any(r => r.Kind == MathRuleKind.Dot),
            @"\dot 生成点号几何");

        MathLayoutResult greekMixed = TestSupport.Layout(@"\alpha\le\omega_0\tau\parallel\beta\approx\infty");
        string joined = TestSupport.JoinedText(greekMixed);
        CheckResult.Check(
            joined.Contains('\u03B1') && joined.Contains('\u2264') && joined.Contains('\u03C9')
            && joined.Contains('\u03C4') && joined.Contains('\u2225') && joined.Contains('\u03B2')
            && joined.Contains('\u2248') && joined.Contains('\u221E'),
            "希腊字母与关系运算符组合渲染正确",
            joined);
        TestSupport.CheckNoLatexMarkup(greekMixed, "希腊字母组合无 LaTeX 残留");

        CheckResult.Section("布局：矩阵 / cases / array");

        MathLayoutResult casesLayout = TestSupport.Layout(@"V_o=\begin{cases}kV_s, & V_s\le V_{th}/k\\ V_{th}, & V_s>V_{th}/k\end{cases}");
        CheckResult.Check(
            casesLayout.Delimiters.Count(d => d.Style == MathDelimiterStyle.Brace && !d.IsClosing) == 1,
            "cases 只有一个左花括号定界符",
            $"delimiters={casesLayout.Delimiters.Count}");
        CheckResult.Check(casesLayout.LineCount == 1, "cases 矩阵作为整体不被拆行");
        TestSupport.CheckNoLatexMarkup(casesLayout, "cases 无 LaTeX 残留");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(casesLayout, "k") && TestSupport.ContainsAnywhere(casesLayout, "V"),
            "cases 单元格内容已渲染");

        MathLayoutResult bmatrixLayout = TestSupport.Layout(@"\begin{bmatrix} a & bbb \\ ccc & d \end{bmatrix}");
        CheckResult.Check(
            bmatrixLayout.Delimiters.Count == 2
            && bmatrixLayout.Delimiters.All(d => d.Style == MathDelimiterStyle.SquareBracket)
            && bmatrixLayout.Delimiters.Count(d => !d.IsClosing) == 1
            && bmatrixLayout.Delimiters.Count(d => d.IsClosing) == 1,
            "bmatrix 渲染左右方括号",
            $"delimiters={bmatrixLayout.Delimiters.Count}");
        MathTextFragment? aFragment = TestSupport.Fragment(bmatrixLayout, "a");
        MathTextFragment? bbbFragment = TestSupport.Fragment(bmatrixLayout, "bbb");
        MathTextFragment? cccFragment = TestSupport.Fragment(bmatrixLayout, "ccc");
        MathTextFragment? dFragment = TestSupport.Fragment(bmatrixLayout, "d");
        CheckResult.Check(
            aFragment is not null && bbbFragment is not null && cccFragment is not null && dFragment is not null,
            "bmatrix 四个单元格都已渲染",
            TestSupport.Describe(bmatrixLayout));
        if (bbbFragment is not null && dFragment is not null)
        {
            float bbbCenter = (bbbFragment.X + bbbFragment.Right) * 0.5f;
            float dCenter = (dFragment.X + dFragment.Right) * 0.5f;
            CheckResult.Check(
                Math.Abs(bbbCenter - dCenter) < 1.5f,
                "同列单元格居中对齐",
                $"bbbCenter={bbbCenter:0.##} dCenter={dCenter:0.##}");
        }

        if (aFragment is not null && cccFragment is not null)
        {
            CheckResult.Check(
                aFragment.BaselineY < cccFragment.BaselineY - 2f,
                "矩阵按行自上而下排列",
                $"row1={aFragment.BaselineY:0.##} row2={cccFragment.BaselineY:0.##}");
        }

        MathLayoutResult arrayLayout = TestSupport.Layout(@"\begin{array}{c|c|c} \text{Port} & R_{eq} & V_{oc} \\ \hline 1 & 2 & 3 \end{array}");
        CheckResult.Check(
            arrayLayout.Rules.Any(r => r.Kind == MathRuleKind.MatrixRule),
            @"array 中的 \hline 渲染为横线",
            $"rules={string.Join(",", arrayLayout.Rules.Select(r => r.Kind))}");
        TestSupport.CheckNoLatexMarkup(arrayLayout, "array 无 LaTeX 残留");

        CheckResult.Section("布局：单原子/空公式的尺寸健壮性");

        MathLayoutResult singleAtom = TestSupport.Layout(@"x", 640f);
        CheckResult.Check(
            singleAtom.LineCount == 1 && !singleAtom.ScaledToFit,
            "单字符公式单行且不缩放",
            $"lines={singleAtom.LineCount} scaled={singleAtom.ScaledToFit}");
        CheckResult.Check(
            singleAtom.Height >= TestSupport.BaseFontSize * 1.2f,
            "单字符公式仍有足够行高（不会 0 高度）",
            $"height={singleAtom.Height:0.##}");
        MathLayoutResult singleAtomWide = TestSupport.Layout(@"x", 1400f);
        CheckResult.Check(
            Math.Abs(singleAtom.Height - singleAtomWide.Height) < 0.01f,
            "容器变宽不改变单行公式高度",
            $"h640={singleAtom.Height:0.##} h1400={singleAtomWide.Height:0.##}");

        MathLayoutResult singleFraction = TestSupport.Layout(@"\frac{U_T}{I_{EQ}}", 120f);
        CheckResult.Check(
            singleFraction.LineCount == 1 && !singleFraction.ScaledToFit,
            "窄容器中的单个分式不被缩放",
            $"lines={singleFraction.LineCount} scaled={singleFraction.ScaledToFit}");
        CheckResult.Check(
            singleFraction.Height > singleAtom.Height,
            "分式公式高度大于普通单行（公式框自动更高）",
            $"frac={singleFraction.Height:0.##} plain={singleAtom.Height:0.##}");

        MathLayoutResult emptySize = TestSupport.Layout(string.Empty, 640f);
        CheckResult.Check(emptySize.Height > 0f, "空公式布局高度为正（控件可自行折叠）", $"height={emptySize.Height:0.##}");

        CheckResult.Section("布局：宽容器充分利用宽度");

        string combined =
            @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} + V_{th}=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}} + I_{BQ}=\frac{V_{th}-V_{BE}}{R_{th}+(1+\beta)R_e}";
        MathLayoutResult combinedWide = TestSupport.Layout(combined, 1400f);
        MathLayoutResult combinedNarrow = TestSupport.Layout(combined, 360f);
        CheckResult.Check(
            combinedWide.LineCount == 1,
            "宽容器（1400px）下长公式不被过度换行",
            $"lines={combinedWide.LineCount}");
        CheckResult.Check(
            combinedNarrow.LineCount >= 2,
            "同样公式在窄容器（360px）下确实换行",
            $"lines={combinedNarrow.LineCount}");
        CheckResult.Check(
            combinedWide.MaxLineWidth > combinedNarrow.MaxLineWidth,
            "宽容器下公式实际使用了更多宽度",
            $"wide={combinedWide.MaxLineWidth:0.##} narrow={combinedNarrow.MaxLineWidth:0.##}");
        CheckResult.Check(
            !combinedWide.ScaledToFit && !combinedNarrow.ScaledToFit,
            "宽/窄容器均无需等比缩小",
            $"wideScaled={combinedWide.ScaledToFit} narrowScaled={combinedNarrow.ScaledToFit}");
        TestSupport.CheckNoLatexMarkup(combinedWide, "宽容器长公式无 LaTeX 残留");

        MathLayoutResult veryWide = TestSupport.Layout(FormulaVth, 2000f);
        CheckResult.Check(
            veryWide.LineCount == 1 && !veryWide.ScaledToFit,
            "超宽容器（2000px）公式保持单行",
            $"lines={veryWide.LineCount} scaled={veryWide.ScaledToFit}");

        CheckResult.Section("布局：基线与尺寸");

        MathLayoutResult sameBaseline = TestSupport.Layout(@"a+b=c");
        float[] baselines = sameBaseline.Fragments.Select(f => f.BaselineY).Distinct().ToArray();
        CheckResult.Check(baselines.Length == 1, "同一行的片元共享同一条基线", $"baselines={string.Join(",", baselines)}");

        MathLayoutResult empty = TestSupport.Layout(string.Empty);
        CheckResult.Check(empty.IsEmpty && empty.Fragments.Count == 0, "空公式不产生片元");
        CheckResult.Check(empty.LineCount == 1, "空公式行数为 1");

        CheckResult.Check(simple.Height > 0f, "布局高度为正", $"height={simple.Height:0.##}");
        CheckResult.Check(simple.Width <= 640f, "布局宽度不超过可用宽度", $"width={simple.Width:0.##}");
    }
}
