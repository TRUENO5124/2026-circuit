// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/SecondOrderRlcStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical Solving Strategy for Second-Order RLC Resonance &
//              Transient Circuits (Natural frequency, Q factor, roots, damping).
// ============================================================================

using System;
using System.Linq;
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

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"二阶 RLC {(_subtype == "ParallelRlc" ? "并联" : "串联")} 谐振与瞬态特性严谨推导"
        };

        var res = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Resistor);
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);
        var src = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource);

        double r = (res != null) ? GetSiValue(res) : 100.0;
        double l = (ind != null) ? GetSiValue(ind) : 10e-3; // 10 mH
        double c = (cap != null) ? GetSiValue(cap) : 1e-6;  // 1 uF
        double vs = (src != null) ? GetSiValue(src) : 10.0;

        if (r < 1e-6) r = 0.01;
        if (l < 1e-12) l = 1e-6;
        if (c < 1e-15) c = 1e-12;

        bool isParallel = _subtype == "ParallelRlc";

        // 1. Natural Frequency
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);

        // 2. Attenuation alpha & Quality factor Q
        double alpha = isParallel ? (1.0 / (2.0 * r * c)) : (r / (2.0 * l));
        double q = isParallel ? (r * Math.Sqrt(c / l)) : ((1.0 / r) * Math.Sqrt(l / c));
        if (q < 1e-9) q = 0.01;
        double bw = f0 / q;

        // 3. Characteristic Roots & Damping Regime
        double discriminant = alpha * alpha - omega0 * omega0;
        string regime;
        string rootsLatex;
        string rootsValue;

        if (Math.Abs(discriminant) < 1e-6 || Math.Abs(alpha - omega0) < 1e-4)
        {
            regime = "临界阻尼 (Critically Damped)";
            rootsLatex = $@"s_1 = s_2 = -\alpha = -{ScientificNotationFormatter.ToLatexScientific(alpha)}\text{{ s}}^{{-1}}";
            rootsValue = $"重根 s = -{ScientificNotationFormatter.ToUnicodeScientific(alpha)} s⁻¹";
        }
        else if (discriminant > 0)
        {
            regime = "过阻尼 (Overdamped)";
            double s1 = -alpha + Math.Sqrt(discriminant);
            double s2 = -alpha - Math.Sqrt(discriminant);
            rootsLatex = $@"s_1 = {ScientificNotationFormatter.ToLatexScientific(s1)},\quad s_2 = {ScientificNotationFormatter.ToLatexScientific(s2)}\text{{ s}}^{{-1}}";
            rootsValue = $"两实根 s1={ScientificNotationFormatter.ToUnicodeScientific(s1)}, s2={ScientificNotationFormatter.ToUnicodeScientific(s2)}";
        }
        else // Underdamped
        {
            regime = "欠阻尼 (Underdamped)";
            double omegaD = Math.Sqrt(omega0 * omega0 - alpha * alpha);
            double overshoot = Math.Exp(-Math.PI * alpha / omegaD) * 100.0;
            rootsLatex = $@"s_{{1, 2}} = -\alpha \pm j\omega_d = -{ScientificNotationFormatter.ToLatexScientific(alpha)} \pm j{ScientificNotationFormatter.ToLatexScientific(omegaD)}\text{{ rad/s}}";
            rootsValue = $"共轭复根: -{ScientificNotationFormatter.ToUnicodeScientific(alpha)} ± j{ScientificNotationFormatter.ToUnicodeScientific(omegaD)} rad/s (超调量 {overshoot:F1}%)";
        }

        solution.SummaryText = $"二阶 RLC 谐振网络解算完成：固有角频率 ω0 = {ScientificNotationFormatter.ToLatexScientific(omega0)} rad/s (f0 = {ScientificNotationFormatter.ToLatexScientific(f0)} Hz)，衰减常数 α = {ScientificNotationFormatter.ToLatexScientific(alpha)}，品质因数 Q = {q:F2}。工作状态为【{regime}】。";

        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = $"二阶回路阻尼态: {regime}",
            IsActiveRegion = false
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = $"Q = {q:F2}",
            InputResistance = ScientificNotationFormatter.FormatWithUnit(r, "Ω"),
            OutputResistance = ScientificNotationFormatter.FormatWithUnit(r, "Ω"),
            LowerCutoffFreq = ScientificNotationFormatter.FormatWithUnit(Math.Max(0, f0 - bw / 2), "Hz"),
            UpperCutoffFreq = ScientificNotationFormatter.FormatWithUnit(f0 + bw / 2, "Hz"),
            Bandwidth = ScientificNotationFormatter.FormatWithUnit(bw, "Hz")
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
            SubstitutionFormula = $@"\omega_0 = \frac{{1}}{{\sqrt{{{ScientificNotationFormatter.ToLatexScientific(l)} \times {ScientificNotationFormatter.ToLatexScientific(c)}}}}} = {ScientificNotationFormatter.ToLatexScientific(omega0)}\text{{ rad/s}},\quad f_0 = {ScientificNotationFormatter.ToLatexScientific(f0)}\text{{ Hz}}",
            ResultValue = $"f0 = {ScientificNotationFormatter.ToUnicodeScientific(f0)} Hz",
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
            SubstitutionFormula = $@"\alpha = {ScientificNotationFormatter.ToLatexScientific(alpha)}\text{{ s}}^{{-1}},\quad Q = {q:F3}",
            ResultValue = $"Q = {q:F2}, α = {ScientificNotationFormatter.ToUnicodeScientific(alpha)} s⁻¹",
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
                : (Math.Abs(discriminant) < 1e-6
                    ? @"f(t) = f(\infty) + (A_1 + A_2 t) e^{-\alpha t}\quad (\alpha = \omega_0,\text{ 最快无超调恢复})"
                    : @"f(t) = f(\infty) + A e^{-\alpha t} \sin(\omega_d t + \theta)\quad (\alpha < \omega_0,\text{ 衰减正弦振荡})"),
            SubstitutionFormula = $@"Q = {q:F2} \implies \text{{{regime}}}",
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
            SubstitutionFormula = $@"BW = \frac{{{ScientificNotationFormatter.ToLatexScientific(f0)}}}{{{q:F2}}} = {ScientificNotationFormatter.ToLatexScientific(bw)}\text{{ Hz}}",
            ResultValue = $"BW = {ScientificNotationFormatter.ToUnicodeScientific(bw)} Hz",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static double GetSiValue(CircuitElement el)
    {
        double multiplier = 1.0;
        foreach (var opt in EngineeringUnits.ResistanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.CapacitanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.InductanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.VoltageUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
