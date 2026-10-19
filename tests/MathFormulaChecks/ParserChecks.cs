// ============================================================================
// File: tests/MathFormulaChecks/ParserChecks.cs
// Description: Syntax-tree checks for the LaTeX parser: fractions, scripts,
//              roots, Greek/operator mapping, environments, style commands and
//              graceful degradation for unknown commands.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.MathRendering;

namespace MathFormulaChecks;

internal static class ParserChecks
{
    public static void Run()
    {
        CheckResult.Section("解析：frac / 上下标 / sqrt / 分组");

        MathRow fractionRow = TestSupport.ParseRow(@"\frac{a}{b}");
        CheckResult.Check(fractionRow.Count == 1 && fractionRow[0] is MathFraction, @"\frac{a}{b} 解析为单个分式节点", $"count={fractionRow.Count}");
        var fraction = fractionRow[0] as MathFraction;
        CheckResult.Check(
            fraction is not null
            && fraction.Numerator is MathRow num && num.Count == 1 && num[0] is MathRun numRun && numRun.Text == "a" && numRun.Italic
            && fraction.Denominator is MathRow den && den.Count == 1 && den[0] is MathRun denRun && denRun.Text == "b" && denRun.Italic,
            "分式分子/分母分别解析为斜体变量 a 与 b");

        MathRow subscriptRow = TestSupport.ParseRow(@"V_{th}");
        CheckResult.Check(
            subscriptRow.Count == 1 && subscriptRow[0] is MathScript script
            && script.Base is MathRun scriptBase && scriptBase.Text == "V" && scriptBase.Italic
            && script.Subscript is MathRow sub && sub.Count == 1
            && sub[0] is MathRun subRun && subRun.Text == "th" && subRun.Italic
            && script.Superscript is null,
            "V_{th} 解析为 基底 V + 下标 th",
            $"count={subscriptRow.Count}");

        MathRow powerRow = TestSupport.ParseRow(@"x^2");
        CheckResult.Check(
            powerRow.Count == 1 && powerRow[0] is MathScript power
            && power.Base is MathRun powerBase && powerBase.Text == "x"
            && power.Superscript is MathRow sup && sup.Count == 1,
            "x^2 单字符上标（无花括号）解析成功");

        MathRow sqrtRow = TestSupport.ParseRow(@"\sqrt{x+1}");
        CheckResult.Check(sqrtRow.Count == 1 && sqrtRow[0] is MathSqrt, @"\sqrt{x+1} 解析为根号节点");
        var sqrt = sqrtRow.Count == 1 ? sqrtRow[0] as MathSqrt : null;
        CheckResult.Check(
            sqrt?.Radicand is MathRow radicand
            && radicand.Children.Any(n => n is MathRun r && r.Text == "x")
            && radicand.Children.Any(n => n is MathRun op && op.Text is "+" or "\u2212")
            && radicand.Children.Any(n => n is MathRun one && one.Text == "1"),
            "根号内 x+1 全部保留");

        MathRow sqrtIndexRow = TestSupport.ParseRow(@"\sqrt[3]{x}");
        CheckResult.Check(
            sqrtIndexRow.Count == 1 && sqrtIndexRow[0] is MathSqrt indexed && indexed.Index is not null,
            @"\sqrt[3]{x} 可选根指数被解析");

        MathRow groupRow = TestSupport.ParseRow(@"{a+b}");
        CheckResult.Check(groupRow.Count >= 3, "花括号分组被展开进父行", $"count={groupRow.Count}");

        CheckResult.Section("解析：希腊字母与运算符");

        var glyphCases = new (string Latex, string Glyph, string Label)[]
        {
            (@"\alpha", "\u03B1", "alpha"),
            (@"\beta", "\u03B2", "beta"),
            (@"\gamma", "\u03B3", "gamma"),
            (@"\delta", "\u03B4", "delta"),
            (@"\epsilon", "\u03B5", "epsilon"),
            (@"\eta", "\u03B7", "eta"),
            (@"\theta", "\u03B8", "theta"),
            (@"\lambda", "\u03BB", "lambda"),
            (@"\mu", "\u03BC", "mu"),
            (@"\pi", "\u03C0", "pi"),
            (@"\sigma", "\u03C3", "sigma"),
            (@"\tau", "\u03C4", "tau"),
            (@"\omega", "\u03C9", "omega"),
            (@"\varphi", "\u03C6", "varphi"),
            (@"\phi", "\u03D5", "phi"),
            (@"\Omega", "\u03A9", "Omega"),
            (@"\Delta", "\u0394", "Delta"),
            (@"\parallel", "\u2225", "parallel"),
            (@"\approx", "\u2248", "approx"),
            (@"\times", "\u00D7", "times"),
            (@"\cdot", "\u00B7", "cdot"),
            (@"\pm", "\u00B1", "pm"),
            (@"\le", "\u2264", "le"),
            (@"\ge", "\u2265", "ge"),
            (@"\ne", "\u2260", "ne"),
            (@"\infty", "\u221E", "infty"),
            (@"\rightarrow", "\u2192", "rightarrow"),
            (@"\Rightarrow", "\u21D2", "Rightarrow"),
            (@"\implies", "\u21D2", "implies"),
            (@"\circ", "\u00B0", "circ"),
            (@"\partial", "\u2202", "partial"),
            (@"\int", "\u222B", "int"),
            (@"\sum", "\u2211", "sum"),
            (@"\propto", "\u221D", "propto"),
            (@"\angle", "\u2220", "angle"),
            (@"\cdots", "\u22EF", "cdots"),
            (@"\vdots", "\u22EE", "vdots"),
        };

        foreach ((string latex, string glyph, string label) in glyphCases)
        {
            MathLayoutResult layout = TestSupport.Layout(latex);
            CheckResult.Check(
                TestSupport.ContainsAnywhere(layout, glyph),
                $"\\{label} 映射为 {glyph}",
                TestSupport.Describe(layout));
        }

        CheckResult.Section("解析：空格 / 文本 / 样式");

        MathRow thinSpaceRow = TestSupport.ParseRow(@"a\,b");
        MathRow midSpaceRow = TestSupport.ParseRow(@"a\;b");
        MathRow quadRow = TestSupport.ParseRow(@"a\quad b");
        MathRow qquadRow = TestSupport.ParseRow(@"a\qquad b");
        CheckResult.Check(
            thinSpaceRow.Children.Any(n => n is MathSpace s && Math.Abs(s.Em - 0.1667f) < 0.001f),
            @"\, 解析为 0.1667em 空白");
        CheckResult.Check(
            midSpaceRow.Children.Any(n => n is MathSpace s && Math.Abs(s.Em - 0.2778f) < 0.001f),
            @"\; 解析为 0.2778em 空白");
        CheckResult.Check(
            quadRow.Children.Any(n => n is MathSpace s && Math.Abs(s.Em - 1.0f) < 0.001f),
            @"\quad 解析为 1em 空白");
        CheckResult.Check(
            qquadRow.Children.Any(n => n is MathSpace s && Math.Abs(s.Em - 2.0f) < 0.001f),
            @"\qquad 解析为 2em 空白");
        CheckResult.Check(
            quadRow.Children.Any(n => n is MathSpace s && s.CanBreakAfter),
            @"\quad 之后允许自动换行");

        MathRow textRow = TestSupport.ParseRow(@"\text{mA}");
        CheckResult.Check(
            textRow.Count == 1 && textRow[0] is MathRun textRun && textRun.Text == "mA" && !textRun.Italic,
            @"\text{mA} 解析为直立的 mA 文本");

        MathRow rmRow = TestSupport.ParseRow(@"\mathrm{dB}");
        CheckResult.Check(
            rmRow.Count == 1 && rmRow[0] is MathRun dB && dB.Text == "dB" && !dB.Italic && !dB.Bold,
            @"\mathrm{dB} 直立不变粗");

        MathRow boldRow = TestSupport.ParseRow(@"\mathbf{G}");
        CheckResult.Check(
            boldRow.Count == 1 && boldRow[0] is MathRun boldRun && boldRun.Text == "G" && boldRun.Bold && !boldRun.Italic,
            @"\mathbf{G} 解析为粗体直立 G");

        MathRow italicRow = TestSupport.ParseRow(@"\mathit{v}");
        CheckResult.Check(
            italicRow.Count == 1 && italicRow[0] is MathRun italicRun && italicRun.Text == "v" && italicRun.Italic,
            @"\mathit{v} 解析为斜体 v");

        MathRow hatRow = TestSupport.ParseRow(@"\hat{V}_{out}");
        CheckResult.Check(
            hatRow.Count == 1 && hatRow[0] is MathScript hatScript
            && hatScript.Base is MathAccent hatAccent && hatAccent.Accent == MathAccentKind.Hat
            && hatScript.Subscript is MathRow hatSub && hatSub.Children.Any(n => n is MathRun v && v.Text == "out"),
            @"\hat{V}_{out} 解析为 带帽 V + 下标 out");

        MathRow primeRow = TestSupport.ParseRow(@"R_{in}'");
        CheckResult.Check(
            primeRow.Count == 1 && primeRow[0] is MathScript primeScript
            && primeScript.Subscript is not null
            && primeScript.Superscript is MathRow primeSup
            && primeSup.Children.Any(n => n is MathRun p && p.Text == "\u2032"),
            "R_{in}' 撇号解析为上标 ′");

        MathRow dotRow = TestSupport.ParseRow(@"\dot{U}");
        CheckResult.Check(
            dotRow.Count == 1 && dotRow[0] is MathAccent dotAccent && dotAccent.Accent == MathAccentKind.Dot,
            @"\dot{U} 解析为点号重音");

        MathRow caretRow = TestSupport.ParseRow(@"3\textasciicircum 2");
        CheckResult.Check(
            caretRow.Children.Any(n => n is MathRun caret && caret.Text == "^"),
            @"\textasciicircum 映射为 ^ 字符");

        MathRow cjkRow = TestSupport.ParseRow(@"\text{状态}");
        CheckResult.Check(
            cjkRow.Count == 1 && cjkRow[0] is MathRun cjk && cjk.Text == "状态" && !cjk.Italic,
            @"\text{状态} 保留中文且直立");

        CheckResult.Section("解析：运算符间距与一元上下文");

        MathRow binaryRow = TestSupport.ParseRow(@"a+b");
        bool binarySpaced = false;
        int plusIndex = -1;
        for (int i = 0; i < binaryRow.Count; i++)
        {
            if (binaryRow[i] is MathRun r && r.Text == "+")
            {
                plusIndex = i;
                break;
            }
        }

        binarySpaced = plusIndex > 0 && plusIndex < binaryRow.Count - 1
                       && binaryRow[plusIndex - 1] is MathSpace beforeSpace && Math.Abs(beforeSpace.Em - 0.22f) < 0.001f
                       && binaryRow[plusIndex + 1] is MathSpace afterSpace && Math.Abs(afterSpace.Em - 0.22f) < 0.001f;
        CheckResult.Check(binarySpaced, "二元运算符 + 两侧各 0.22em 间距");

        MathRow unaryRow = TestSupport.ParseRow(@"-b");
        CheckResult.Check(
            unaryRow.Count > 0 && unaryRow[0] is MathRun minus && minus.Text == "\u2212",
            "行首 - 作为一元负号（无前置空白）");

        CheckResult.Section("解析：环境矩阵");

        string casesLatex = @"V_o=\begin{cases}kV_s, & V_s\le V_{th}/k\\ V_{th}, & V_s>V_{th}/k\end{cases}";
        MathRow casesRow = TestSupport.ParseRow(casesLatex);
        var casesMatrix = casesRow.Children.LastOrDefault() as MathMatrix;
        CheckResult.Check(
            casesMatrix is not null
            && casesMatrix.MatrixKind == MathMatrixKind.Cases
            && casesMatrix.Rows.Count == 2
            && casesMatrix.Rows[0].Count == 2
            && casesMatrix.Rows[1].Count == 2,
            @"cases 环境解析为 2 行 2 列并带左花括号",
            casesMatrix is null ? "未找到 MathMatrix" : $"kind={casesMatrix.MatrixKind} rows={casesMatrix.Rows.Count}");

        string bmatrixLatex = @"\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix}";
        MathRow bmatrixRow = TestSupport.ParseRow(bmatrixLatex);
        var bmatrix = bmatrixRow.Children.LastOrDefault() as MathMatrix;
        CheckResult.Check(
            bmatrix is not null && bmatrix.MatrixKind == MathMatrixKind.Bracket && bmatrix.Rows.Count == 2,
            @"bmatrix 解析为方括号矩阵");
        CheckResult.Check(
            bmatrix is not null
            && bmatrix.Rows[0][0].Children.Any(n => n is MathRun g && g.Text == "G" && g.Bold)
            && bmatrix.Rows[0][1].Children.Any(n => n is MathRun b && b.Text == "B" && b.Bold),
            "矩阵单元格中的 \\mathbf 粗体保留");

        string arrayLatex = @"\begin{array}{c|c|c} \text{Port} & R_{eq} & V_{oc} \\ \hline 1 & 2 & 3 \end{array}";
        MathRow arrayRow = TestSupport.ParseRow(arrayLatex);
        var array = arrayRow.Children.LastOrDefault() as MathMatrix;
        CheckResult.Check(
            array is not null && array.Rows.Count == 2 && array.Rows[0].Count == 3 && array.Rows[1].Count == 3,
            @"array{c|c|c} 解析为 2 行 3 列",
            array is null ? "未找到 MathMatrix" : $"rows={array.Rows.Count}");
        CheckResult.Check(
            array is not null && array.RulesAboveRows.Contains(1),
            @"array 中的 \hline 记录为第 1 行上方的横线",
            array is null ? "无矩阵" : $"rules=[{string.Join(",", array.RulesAboveRows)}]");

        CheckResult.Section("解析：未知命令优雅降级");

        MathLayoutResult unknownLayout = TestSupport.Layout(@"\foobar{x}+1");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(unknownLayout, "x") && TestSupport.ContainsAnywhere(unknownLayout, "1"),
            @"未知命令 \foobar{x} 保留参数 x");
        TestSupport.CheckNoLatexMarkup(unknownLayout, @"未知命令不泄露反斜杠/花括号");

        MathLayoutResult strayBrace = TestSupport.Layout(@"a}b");
        CheckResult.Check(
            TestSupport.ContainsAnywhere(strayBrace, "a") && TestSupport.ContainsAnywhere(strayBrace, "b"),
            "多余右花括号不会吞掉后续内容");
        TestSupport.CheckNoLatexMarkup(strayBrace, "多余右花括号不残留 {");

        MathLayoutResult emptyParse = TestSupport.Layout(string.Empty);
        CheckResult.Check(emptyParse.IsEmpty, "空公式解析为空布局");
    }
}
