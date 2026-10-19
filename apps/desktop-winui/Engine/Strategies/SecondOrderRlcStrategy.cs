// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/SecondOrderRlcStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical Solving Strategy for Second-Order RLC Resonance &
//              Transient Circuits (Natural frequency, Q factor, roots, damping).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

public class SecondOrderRlcStrategy : ICircuitStrategy
{
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => CircuitTopologyType.SecondOrderRlc;
    public string SubtypeName => _subtype;

    public SecondOrderRlcStrategy(string subtype = "SeriesRlc")
    {
        _subtype = subtype;
    }

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"二阶 RLC {(_subtype == "ParallelRlc" ? "并联" : "串联")} 谐振与瞬态特性严谨推导",
        };

        var res = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Resistor);
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized);

        bool isParallel = _subtype == "ParallelRlc";
        var branches = netlist.Branches;
        var passive = branches.Where(b => b.ElementType is ElementType.Resistor or ElementType.Inductor or ElementType.Capacitor or ElementType.CapacitorPolarized).ToList();
        bool samePair = passive.Count == 3 && passive.All(b =>
            new[] { b.PositiveNode.Index, b.NegativeNode.Index }.OrderBy(n => n)
            .SequenceEqual(new[] { passive[0].PositiveNode.Index, passive[0].NegativeNode.Index }.OrderBy(n => n)));
        var allowed = isParallel
            ? new[] { ElementType.Resistor, ElementType.Inductor, ElementType.Capacitor, ElementType.CapacitorPolarized, ElementType.Ground, ElementType.DcCurrentSource, ElementType.AcCurrentSource }
            : new[] { ElementType.Resistor, ElementType.Inductor, ElementType.Capacitor, ElementType.CapacitorPolarized, ElementType.Ground, ElementType.DcVoltageSource, ElementType.AcVoltageSource };
        if (res == null || ind == null || cap == null || passive.Count != 3 || branches.Count - passive.Count > 1 ||
            netlist.Elements.Values.Any(e => !allowed.Contains(e.Type)) ||
            (isParallel ? !samePair || branches.Any(b => b.PositiveNode.Index != passive[0].PositiveNode.Index && b.PositiveNode.Index != passive[0].NegativeNode.Index ||
                b.NegativeNode.Index != passive[0].PositiveNode.Index && b.NegativeNode.Index != passive[0].NegativeNode.Index)
                : netlist.Nodes.Any(n => n.ConnectedBranches.Count != 2)))
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                "当前 RLC 策略只支持一个 R、L、C 的标准串联回路或并联回路（并联采用电流源激励），不支持复杂二阶网络。"));
        double r = GetSiValue(res), l = GetSiValue(ind), c = GetSiValue(cap);
        if (!double.IsFinite(r) || !double.IsFinite(l) || !double.IsFinite(c) || r <= 0 || l <= 0 || c <= 0)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "R、L、C 必须是有限正数。"));

        // 1. Natural Frequency
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);

        // 2. Attenuation alpha & Quality factor Q
        double alpha = isParallel ? (1.0 / (2.0 * r * c)) : (r / (2.0 * l));
        double q = isParallel ? (r * Math.Sqrt(c / l)) : ((1.0 / r) * Math.Sqrt(l / c));
        if (!double.IsFinite(omega0) || !double.IsFinite(alpha) || !double.IsFinite(q) || omega0 <= 0 || alpha <= 0 || q <= 0)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "RLC 参数超出可计算范围。"));
        double bw = f0 / q;

        // 3. Characteristic Roots & Damping Regime
        double discriminant = alpha * alpha - omega0 * omega0;
        if (!double.IsFinite(bw) || !double.IsFinite(discriminant))
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "RLC 频率或判别式超出数值范围。"));
        bool critical = Math.Abs(alpha - omega0) <= 1e-8 * Math.Max(alpha, omega0);
        if (critical) discriminant = 0;
        string regime;
        string rootsLatex;
        string rootsValue;

        // Display formatting (user convention 2026-10-18): every visible number keeps
        // at most two decimals and uses the textbook engineering unit; the structured
        // ω0/α/Q/f0/BW values above stay full precision for the report/benchmark layer.
        string f0Text = TeachingDisplay.Text(f0, QuantityKind.Frequency);
        string bwText = TeachingDisplay.Text(bw, QuantityKind.Frequency);
        string qText = TeachingDisplay.Plain(q);
        string omega0Text = TeachingDisplay.Plain(omega0);
        string alphaText = TeachingDisplay.Plain(alpha);
        string omega0Latex = TeachingDisplay.LatexNumber(omega0, QuantityKind.Ratio) + @"\text{ rad/s}";
        string alphaLatex = TeachingDisplay.LatexNumber(alpha, QuantityKind.Ratio) + @"\text{ s}^{-1}";
        string f0Latex = TeachingDisplay.Latex(f0, QuantityKind.Frequency);
        string bwLatex = TeachingDisplay.Latex(bw, QuantityKind.Frequency);

        if (critical)
        {
            regime = "临界阻尼 (Critically Damped)";
            rootsLatex = $@"s_1 = s_2 = -\alpha = -{alphaLatex}";
            rootsValue = $"重根 s = -{alphaText} s⁻¹";
        }
        else if (discriminant > 0)
        {
            regime = "过阻尼 (Overdamped)";
            double s1 = -alpha + Math.Sqrt(discriminant);
            double s2 = -alpha - Math.Sqrt(discriminant);
            rootsLatex = $@"s_1 = {TeachingDisplay.LatexNumber(s1, QuantityKind.Ratio)},\quad s_2 = {TeachingDisplay.LatexNumber(s2, QuantityKind.Ratio)}\text{{ s}}^{{-1}}";
            rootsValue = $"两实根 s1={TeachingDisplay.Plain(s1)} s⁻¹, s2={TeachingDisplay.Plain(s2)} s⁻¹";
        }
        else // Underdamped
        {
            regime = "欠阻尼 (Underdamped)";
            double omegaD = Math.Sqrt(omega0 * omega0 - alpha * alpha);
            double overshoot = Math.Exp(-Math.PI * alpha / omegaD) * 100.0;
            rootsLatex = $@"s_{{1, 2}} = -\alpha \pm j\omega_d = -{alphaLatex} \pm j{TeachingDisplay.LatexNumber(omegaD, QuantityKind.Ratio)}\text{{ rad/s}}";
            rootsValue = $"共轭复根: -{alphaText} ± j{TeachingDisplay.Plain(omegaD)} rad/s (超调量 {TeachingDisplay.Plain(overshoot)}%)";
        }

        solution.SummaryText = $"二阶 RLC 谐振网络解算完成：固有角频率 ω0 = {omega0Text} rad/s (f0 = {f0Text})，衰减常数 α = {alphaText} s⁻¹，品质因数 Q = {qText}。工作状态为【{regime}】。";

        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = $"二阶回路阻尼态: {regime}",
            IsActiveRegion = false
        };

        double lowerCutoff = 2 * f0 * f0 / (Math.Sqrt(bw * bw + 4 * f0 * f0) + bw);
        double upperCutoff = (Math.Sqrt(bw * bw + 4 * f0 * f0) + bw) / 2;
        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = $"Q = {qText}",
            InputResistance = TeachingDisplay.Text(r, QuantityKind.Resistance),
            OutputResistance = TeachingDisplay.Text(r, QuantityKind.Resistance),
            LowerCutoffFreq = TeachingDisplay.Text(lowerCutoff, QuantityKind.Frequency),
            UpperCutoffFreq = TeachingDisplay.Text(upperCutoff, QuantityKind.Frequency),
            Bandwidth = bwText
        };

        // Step 1: Natural angular frequency
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "二阶回路固有谐振角频率 ω0 与频率 f0 求解",
            MethodName = "LC 无阻尼固有谐振原理",
            Content = "求解无阻尼固有角频率 ω0 = 1/√(LC)。该参数仅由电感与电容的储能特性决定，代表回路无阻尼自由振荡角频率。",
            LatexFormula = @"\omega_0 = \frac{1}{\sqrt{LC}},\quad f_0 = \frac{\omega_0}{2\pi} = \frac{1}{2\pi\sqrt{LC}}",
            SubstitutionFormula = $@"\omega_0 = \frac{{1}}{{\sqrt{{{TeachingDisplay.Latex(l, QuantityKind.Inductance)} \times {TeachingDisplay.Latex(c, QuantityKind.Capacitance)}}}}} = {omega0Latex},\quad f_0 = {f0Latex}",
            ResultValue = $"f0 = {f0Text}",
            Status = "Completed"
        });

        // Step 2: Attenuation alpha & Quality factor Q
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "回路衰减常数 α 与品质因数 Q 求解",
            MethodName = isParallel ? "并联 RLC 耗散能量分析" : "串联 RLC 阻尼分析",
            Content = isParallel
                ? "对于并联 RLC 电路，电导 G = 1/R 提供阻尼损耗，衰减常数 α = 1/(2RC)，品质因数 Q = R√(C/L)。"
                : "对于串联 RLC 电路，电阻 R 提供耗能阻尼，衰减常数 α = R/(2L)，品质因数 Q = (1/R)√(L/C)。",
            LatexFormula = isParallel
                ? @"\alpha = \frac{1}{2RC},\quad Q = \omega_0 RC = R\sqrt{\frac{C}{L}} = \frac{\omega_0}{2\alpha}"
                : @"\alpha = \frac{R}{2L},\quad Q = \frac{\omega_0 L}{R} = \frac{1}{R}\sqrt{\frac{L}{C}} = \frac{\omega_0}{2\alpha}",
            SubstitutionFormula = $@"\alpha = {alphaLatex},\quad Q = {qText}",
            ResultValue = $"Q = {qText}, α = {alphaText} s⁻¹",
            Status = "Completed"
        });

        // Step 3: Characteristic Equation & Roots
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "二阶特征方程建立与特征根解算",
            MethodName = "二阶常微分方程特征根法",
            Content = "根据 KVL/KCL 建立二阶齐次微分方程 d²f/dt² + 2α(df/dt) + ω0²f = 0，特征方程 s² + 2αs + ω0² = 0。",
            LatexFormula = @"s^2 + 2\alpha s + \omega_0^2 = 0 \implies s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}",
            SubstitutionFormula = rootsLatex,
            ResultValue = rootsValue,
            Status = "Completed"
        });

        // Step 4: Damping Regime Classification
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "阻尼响应工作区判定与时域表达式形态",
            MethodName = "阻尼判别准则",
            Content = $"比较衰减常数 α 与固有角频率 ω0（判别式 Δ = α² - ω0²）：当前系统处于【{regime}】。在此状态下阶跃响应表现为特定的瞬态时域曲线。",
            LatexFormula = discriminant > 0
                ? @"f(t) = f(\infty) + A_1 e^{s_1 t} + A_2 e^{s_2 t}\quad (\alpha > \omega_0,\text{ 无振荡单调缓变})"
                : (critical
                    ? @"f(t) = f(\infty) + (A_1 + A_2 t) e^{-\alpha t}\quad (\alpha = \omega_0,\text{ 最快无超调恢复})"
                    : @"f(t) = f(\infty) + A e^{-\alpha t} \sin(\omega_d t + \theta)\quad (\alpha < \omega_0,\text{ 衰减正弦振荡})"),
            SubstitutionFormula = $@"Q = {qText} \implies \text{{{regime}}}",
            ResultValue = regime,
            Status = "Completed"
        });

        // Step 5: Resonance Frequency Response & Bandwidth
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "谐振通频带 BW 与选择性评估",
            MethodName = "频域选择性与通频带分析",
            Content = "品质因数 Q 决定谐振回路的频率选择性：Q 越大，谐振曲线越尖锐，选择性越好，但通频带 BW 越窄。",
            LatexFormula = @"BW = \Delta f = \frac{f_0}{Q} = \frac{\alpha}{\pi}",
            SubstitutionFormula = $@"BW = \frac{{{f0Latex}}}{{{qText}}} = {bwLatex}",
            ResultValue = $"BW = {bwText}",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static double GetSiValue(CircuitElement el)
    {
        return NetlistExtractor.ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol);
    }
}
