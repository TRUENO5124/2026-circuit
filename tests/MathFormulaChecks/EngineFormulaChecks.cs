// ============================================================================
// File: tests/MathFormulaChecks/EngineFormulaChecks.cs
// Description: End-to-end regressions over the real LaTeX strings produced by
//              the teaching engine (Engine/Strategies/*). Asserts that no raw
//              LaTeX scaffolding reaches the renderer and that every formula
//              fits the available width at both wide and narrow sizes.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.MathRendering;

namespace MathFormulaChecks;

internal static class EngineFormulaChecks
{
    private static readonly string[] ForbiddenScaffolding = { "begin", "end{", "frac", "sqrt", "mathbf", "mathrm", "left", "right", "qquad", "quad" };

    private static readonly (string Latex, string Label)[] EngineFormulas =
    {
        (@"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} =200\,\Omega + (1+\beta)\frac{U_T}{I_{EQ}}", "BJT r_be 推导"),
        (@"r_{be} = 200 + (1 + 200) \frac{26\text{ mV}}{2.5\text{ mA}} = 2.2\,\text{k}\Omega", "BJT r_be 数值代入"),
        (@"V_{th}=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}}", "分压偏置 V_th"),
        (@"I_{BQ}=\frac{V_{th}-V_{BE}}{R_{th}+(1+\beta)R_e}", "分压偏置 I_BQ"),
        (@"R_e'=R_E\parallel R_L,\quad A_v=\frac{(1+\beta)R_e'}{r_{be}+(1+\beta)R_e'}", "共集电极 A_v"),
        (@"R_i=R_b\parallel\left[r_{be}+(1+\beta)R_e'\right]", "共集电极 R_i"),
        (@"A_u = \frac{u_o}{u_i} = \frac{\beta (R_C \parallel R_L)}{h_{ie}} = -\frac{h_{fb}(R_C \parallel R_L)}{h_{ib}}", "共基极 A_u"),
        (@"r_i' = R_E \parallel h_{ib} = R_E \parallel \frac{h_{ie}}{1+\beta}", "共基极 r_i"),
        (@"f_L = \sqrt{f_{L1}^2 + f_{L2}^2 + f_{Le}^2},\quad f_H \approx \frac{1}{2\pi R_{in}' C_{in}'},\quad BW = f_H - f_L", "RC 低频/高频"),
        (@"f_{L1}=\frac{1}{2\pi R_i C_1},\quad f_{L2}=\frac{1}{2\pi (R_o+R_L) C_2},\quad f_L=\sqrt{f_{L1}^2+f_{L2}^2}", "RC 截止频率"),
        (@"\omega_0 = \frac{1}{\sqrt{LC}},\quad f_0 = \frac{\omega_0}{2\pi} = \frac{1}{2\pi\sqrt{LC}}", "RLC 谐振"),
        (@"\alpha = \frac{1}{2RC},\quad Q = \omega_0 RC = R\sqrt{\frac{C}{L}} = \frac{\omega_0}{2\alpha}", "RLC 阻尼/品质因数"),
        (@"s^2 + 2\alpha s + \omega_0^2 = 0 \implies s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}", "RLC 特征根"),
        (@"|A_u(jf)| = \frac{1}{\sqrt{1 + (f/f_c)^2}},\quad 20\lg |A_u(jf)| = -10\lg\left[1 + \left(\frac{f}{f_c}\right)^2\right]\text{ dB},\quad \varphi(f) = -\arctan\left(\frac{f}{f_c}\right)", "一阶 RC 幅频/相频"),
        (@"|H(j\omega)|=\frac{\omega\tau}{\sqrt{1+(\omega\tau)^2}},\quad \varphi=90^\circ-\arctan(\omega\tau)", "高通 Bode"),
        (@"\hat{H}(j\omega) = \frac{\hat{V}_{out}(j\omega)}{\hat{V}_{in}(j\omega)},\qquad 20\log_{10}\left|\hat{H}\right| [\mathrm{dB}]", "MNA 传递函数"),
        (@"\hat{V}_{out} = 12.3\angle 45.0^\circ\text{ V},\quad \hat{V}_{in}=6.28\times 10^{3}\text{ mV}", "MNA 相量节点电压"),
        (@"\mathbf{x} = \mathbf{A}^{-1}\mathbf{z}", "MNA 矩阵方程简式"),
        (@"\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix}\begin{bmatrix} \mathbf{v} \\ \mathbf{i} \end{bmatrix} = \begin{bmatrix} \mathbf{J} \\ \mathbf{E} \end{bmatrix}", "MNA 分块矩阵方程"),
        (@"V_o=\begin{cases}kV_s, & V_s\le V_{th}/k\\ V_{th}, & V_s>V_{th}/k\end{cases}", "二极管限幅分段模型"),
        (@"I_D=I_S(e^{V_D/(nV_T)}-1),\quad \text{state}=\begin{cases}\text{导通}, & I_D>0\\ \text{截止}, & I_D\le 0\end{cases}", "二极管 Shockley 分段"),
        (@"U_{cap} = V(+) - V(-) \ge 0\text{ V}\quad (\text{Safe Forward Bias})", "二极管偏置安全判据"),
        (@"V_{oc} = V_A - V_B\big|_{\text{open}},\qquad R_{eq} = \frac{V_A - V_B}{1\ \text{A}}\bigg|_{\text{sources zeroed}},\qquad I_N = \frac{V_{oc}}{R_{eq}}", "戴维南等效"),
        (@"\begin{array}{c|c|c} \text{Port} & R_{eq} & V_{oc} \\ \hline 1 & 2.2\ \text{k}\Omega & 3.3\text{ V} \end{array}", "MNA 端口表"),
        (@"V_{avg}=\frac{2V_{om}}{\pi},\quad V_{rms}=\frac{V_{om}}{\sqrt{2}}", "整流平均/有效值"),
        (@"20\log_{10}\left|\hat{H}\right| [\mathrm{dB}]", "dB 表达式"),
        (@"\text{状态}=\text{饱和区},\quad I_D = \frac{1}{2}K(V_{GS}-V_{th})^2", "MOS 饱和区判据"),
        (@"h_{ie} = r_{bb'} + (1+\beta)\frac{26\,\text{mV}}{I_{EQ}},\quad h_{ib} = \frac{h_{ie}}{1+\beta},\quad h_{fb} = \frac{-\beta}{1+\beta} \approx -1", "BJT h 参数"),
    };

    public static void Run()
    {
        CheckResult.Section("引擎真实公式：宽 640 布局");

        foreach ((string latex, string label) in EngineFormulas)
        {
            MathLayoutResult layout;
            try
            {
                layout = TestSupport.Layout(latex, 640f);
            }
            catch (Exception ex)
            {
                CheckResult.Check(false, $"{label}：布局未抛异常", ex.Message);
                continue;
            }

            CheckResult.Check(layout.Fragments.Count > 0, $"{label}：产生文本片元", TestSupport.Describe(layout));
            TestSupport.CheckNoLatexMarkup(layout, $"{label}：片元中无 LaTeX 残留字符");

            string joined = TestSupport.JoinedText(layout);
            bool noCommands = true;
            string leaked = string.Empty;
            foreach (string token in ForbiddenScaffolding)
            {
                if (joined.Contains(token, StringComparison.Ordinal))
                {
                    noCommands = false;
                    leaked = token;
                    break;
                }
            }

            CheckResult.Check(noCommands, $"{label}：没有把命令名当文本渲染", noCommands ? string.Empty : $"泄露 \"{leaked}\"");
            CheckResult.Check(layout.Height > 0f, $"{label}：高度为正", $"height={layout.Height:0.##}");
            CheckResult.Check(layout.LineCount >= 1, $"{label}：至少一行");
        }

        CheckResult.Section("引擎真实公式：窄宽 320 自动换行且不裁切");

        foreach ((string latex, string label) in EngineFormulas)
        {
            MathLayoutResult layout = TestSupport.Layout(latex, 320f);
            TestSupport.CheckFitsWidth(layout, 320f, $"{label}：宽 320 时所有片元不超出可用宽度");
            TestSupport.CheckNoLatexMarkup(layout, $"{label}：换行后无 LaTeX 残留");
            CheckResult.Check(
                TestSupport.DistinctLineCount(layout) <= layout.LineCount,
                $"{label}：行号不越界",
                $"lines={layout.LineCount} distinct={TestSupport.DistinctLineCount(layout)}");
        }

        CheckResult.Section("引擎真实公式：重点结构抽查");

        MathLayoutResult bjt = TestSupport.Layout(EngineFormulas[0].Latex);
        CheckResult.Check(
            bjt.Fragments.Count(f => f.FontSize < TestSupport.BaseFontSize * 0.9f) >= 4,
            "BJT r_be 公式含多处真角标（be / bb′ / EQ）",
            $"scriptFragments={bjt.Fragments.Count(f => f.FontSize < TestSupport.BaseFontSize * 0.9f)}");
        CheckResult.Check(
            bjt.Rules.Count(r => r.Kind == MathRuleKind.FractionBar) >= 2,
            "BJT r_be 公式含两个分式横线",
            $"bars={bjt.Rules.Count(r => r.Kind == MathRuleKind.FractionBar)}");

        MathLayoutResult rlc = TestSupport.Layout(EngineFormulas[10].Latex);
        CheckResult.Check(
            rlc.Rules.Count(r => r.Kind == MathRuleKind.SqrtRadical) >= 2,
            "RLC 公式含两个根号",
            $"radicals={rlc.Rules.Count(r => r.Kind == MathRuleKind.SqrtRadical)}");

        MathLayoutResult mna = TestSupport.Layout(EngineFormulas[18].Latex);
        CheckResult.Check(
            mna.Delimiters.Count == 6,
            "MNA 分块矩阵方程渲染 3 组方括号（6 个定界符）",
            $"delimiters={mna.Delimiters.Count}");
        CheckResult.Check(
            mna.Fragments.Where(f => f.Text is "G" or "B" or "C" or "D" or "v" or "i" or "J" or "E").All(f => f.Bold),
            "MNA 矩阵中的 \\mathbf 字母全部为粗体");

        MathLayoutResult cases = TestSupport.Layout(EngineFormulas[19].Latex);
        CheckResult.Check(
            cases.Delimiters.Count(d => d.Style == MathDelimiterStyle.Brace) == 1,
            "限幅分段模型渲染左花括号",
            $"delimiters={cases.Delimiters.Count}");

        MathLayoutResult portTable = TestSupport.Layout(EngineFormulas[23].Latex);
        CheckResult.Check(
            portTable.Rules.Any(r => r.Kind == MathRuleKind.MatrixRule),
            "MNA 端口表的 \\hline 渲染为横线",
            $"rules={string.Join(",", portTable.Rules.Select(r => r.Kind))}");

        MathLayoutResult cjk = TestSupport.Layout(EngineFormulas[26].Latex);
        CheckResult.Check(
            TestSupport.ContainsAnywhere(cjk, "饱和区") && TestSupport.ContainsAnywhere(cjk, "状态"),
            @"MOS 公式中的 \text 中文正常渲染",
            TestSupport.Describe(cjk));

        CheckResult.Section("引擎真实公式：不同字号保持结构");

        foreach (float fontSize in new[] { 12f, 14f, 16f, 18f })
        {
            MathLayoutResult scaled = TestSupport.Layout(EngineFormulas[3].Latex, 520f, fontSize);
            TestSupport.CheckNoLatexMarkup(scaled, $"I_BQ 公式 @ {fontSize:0}px 无 LaTeX 残留");
            CheckResult.Check(
                scaled.Fragments.Any(f => Math.Abs(f.FontSize - fontSize) < 0.01f),
                $"I_BQ 公式 @ {fontSize:0}px 顶层字号正确",
                TestSupport.Describe(scaled));
            CheckResult.Check(
                scaled.Fragments.Any(f => Math.Abs(f.FontSize - fontSize * 0.72f) < 0.1f),
                $"I_BQ 公式 @ {fontSize:0}px 角标按比例缩小");
        }
    }
}
