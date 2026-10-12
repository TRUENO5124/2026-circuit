// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/FirstOrderRcStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical Solving Strategy for First-Order RC / RL Circuits.
//              Emits Thevenin Req, tau, Three-Element differential equation,
//              H(s), and cutoff frequency fc with ZERO fictitious BJT parameters.
// ============================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

public class FirstOrderRcStrategy : ICircuitStrategy
{
    private readonly bool _isRl;
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => _isRl ? CircuitTopologyType.FirstOrderRl : CircuitTopologyType.FirstOrderRc;
    public string SubtypeName => _subtype;

    public FirstOrderRcStrategy(bool isRl = false, string subtype = "DcStepTransient")
    {
        _isRl = isRl;
        _subtype = subtype;
    }

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = _isRl ? "一阶 RL 电路时域暂态与频率响应严谨推导" : "一阶 RC 电路时域暂态与频率响应严谨推导"
        };

        // 1. Extract Components
        var resList = netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor).ToList();
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var dcSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.DcVoltageSource);
        var acSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);

        // Values in SI units
        double rVal = resList.Count > 0 ? GetSiValue(resList[0]) : 1000.0;
        string rName = resList.Count > 0 ? resList[0].Name : "R1";
        string rId = resList.Count > 0 ? resList[0].Id : "R1";

        double cVal = cap != null ? GetSiValue(cap) : 100e-6;
        string cId = cap != null ? cap.Id : "C1";

        double lVal = ind != null ? GetSiValue(ind) : 10e-3;
        string lId = ind != null ? ind.Id : "L1";

        double vsVal = dcSrc != null ? GetSiValue(dcSrc) : (acSrc != null ? GetSiValue(acSrc) : 10.0);
        string vsId = dcSrc != null ? dcSrc.Id : (acSrc != null ? acSrc.Id : "VS");

        // 2. Thevenin Equivalent Resistance Req and Time Constant tau
        // If there are multiple resistors, calculate equivalent resistance
        double req = rVal;
        if (resList.Count > 1)
        {
            req = resList.Sum(r => GetSiValue(r));
        }
        if (req < 1e-9) req = 1.0;

        double tau = _isRl ? (lVal / req) : (req * cVal);
        if (tau < 1e-15) tau = 1e-6;

        double omegaC = 1.0 / tau;
        double fc = omegaC / (2.0 * Math.PI);

        // Format values with scientific notation formatter
        string reqLatex = ScientificNotationFormatter.ToLatexScientific(req);
        string tauLatex = ScientificNotationFormatter.ToLatexScientific(tau);
        string tauMsLatex = ScientificNotationFormatter.ToLatexScientific(tau * 1000.0);
        string fcLatex = ScientificNotationFormatter.ToLatexScientific(fc);
        string vsLatex = ScientificNotationFormatter.ToLatexScientific(vsVal);

        solution.SummaryText = $"一阶 {(_isRl ? "RL" : "RC")} 电路拓扑分析完成：等效电阻 Req = {reqLatex} Ω，时间常数 τ = {tauLatex} s ({tauMsLatex} ms)，特征截止频率 fc = {fcLatex} Hz。";

        // Strictly suppress fictitious BJT parameters
        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = "无直流偏置 (无晶体管拓扑)",
            IsActiveRegion = false
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = "1.00 (0 dB)",
            InputResistance = ScientificNotationFormatter.FormatWithUnit(req, "Ω"),
            OutputResistance = "0 Ω",
            LowerCutoffFreq = "0 Hz",
            UpperCutoffFreq = ScientificNotationFormatter.FormatWithUnit(fc, "Hz"),
            Bandwidth = ScientificNotationFormatter.FormatWithUnit(fc, "Hz")
        };

        // ---------------------------------------------------------------------
        // Step 1: Thevenin Equivalent Resistance Req
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "端口戴维南等效电阻 Req 求解",
            MethodName = "戴维南等效法",
            Content = $"将电路中独立激励源（电压源短路，电流源开路）置零，从储能元件 {(_isRl ? lId : cId)} 两引脚端口看入的戴维南等效输入电阻。",
            LatexFormula = $@"R_{{eq}} = R_{{{rId}}}",
            SubstitutionFormula = $@"R_{{eq}} = {reqLatex}\;\Omega",
            ResultValue = $"{ScientificNotationFormatter.ToUnicodeScientific(req)} Ω",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 2: Three-Element Method Parameters (f(0+), f(inf), tau)
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "时域暂态三要素（初始值、稳态值、时间常数）分析",
            MethodName = "三要素分析法",
            Content = $"根据储能元件换路定律：换路瞬间电容电压不突变 u_C(0+) = u_C(0-) = 0V；换路达到新稳态时，电容视为开路，两端电压等于电源开路电压 u_C(∞) = {vsId}。",
            LatexFormula = _isRl
                ? @"i_L(0^+) = 0\text{ A},\quad i_L(\infty) = \frac{V_S}{R_{eq}},\quad \tau = \frac{L}{R_{eq}}"
                : @"u_C(0^+) = 0\text{ V},\quad u_C(\infty) = V_S,\quad \tau = R_{eq} \cdot C",
            SubstitutionFormula = _isRl
                ? $@"i_L(0^+) = 0\text{{ A}},\quad i_L(\infty) = {ScientificNotationFormatter.ToLatexScientific(vsVal / req)}\text{{ A}},\quad \tau = {tauMsLatex}\text{{ ms}}"
                : $@"u_C(0^+) = 0\text{{ V}},\quad u_C(\infty) = {vsLatex}\text{{ V}},\quad \tau = {tauLatex}\text{{ s}} = {tauMsLatex}\text{{ ms}}",
            ResultValue = $"τ = {ScientificNotationFormatter.ToUnicodeScientific(tau * 1000.0)} ms",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 3: Differential Equation & Time-Domain Solution
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "一阶常微分方程建立与时域充放电通解",
            MethodName = "一阶动态电路微分方程",
            Content = $"建立一阶常系数线性微分方程 τ(df/dt) + f(t) = f(∞)，代入三要素公式求解。当 t = 3τ 时充放电达 95.0%，t = 5τ 时达 99.3%（工程视作暂态过程结束）。",
            LatexFormula = _isRl
                ? @"i_L(t) = i_L(\infty) + \left[i_L(0^+) - i_L(\infty)\right] e^{-\frac{t}{\tau}} = \frac{V_S}{R_{eq}} \left(1 - e^{-\frac{t}{\tau}}\right)"
                : @"u_C(t) = u_C(\infty) + \left[u_C(0^+) - u_C(\infty)\right] e^{-\frac{t}{\tau}} = V_S \left(1 - e^{-\frac{t}{\tau}}\right)",
            SubstitutionFormula = _isRl
                ? $@"i_L(t) = {ScientificNotationFormatter.ToLatexScientific(vsVal / req)} \left(1 - e^{{-\frac{{t}}{{{tauLatex}}}}}\right)\text{{ A}}"
                : $@"u_C(t) = {vsLatex} \left(1 - e^{{-\frac{{t}}{{{tauLatex}}}}}\right)\text{{ V}}",
            ResultValue = $"u_C(3τ) = {ScientificNotationFormatter.ToUnicodeScientific(vsVal * 0.9502)} V, u_C(5τ) = {ScientificNotationFormatter.ToUnicodeScientific(vsVal * 0.9933)} V",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 4: Frequency Domain Transfer Function H(s) and fc
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "复频域传递函数 H(s) 与截止频率 fc 求解",
            MethodName = "复阻抗分压与 Laplace 变换",
            Content = $"在复频域中，电容复阻抗 Z_C(s) = 1/(sC)。输出电压取自电容两端，传递函数 H(s) 具有标准单极点低通响应形态，半功率转折点角频率为 ω_c = 1/τ。",
            LatexFormula = _isRl
                ? @"H(s) = \frac{1}{1 + s\tau},\quad \omega_c = \frac{R_{eq}}{L},\quad f_c = \frac{\omega_c}{2\pi} = \frac{R_{eq}}{2\pi L}"
                : @"H(s) = \frac{1}{1 + s\tau},\quad \omega_c = \frac{1}{R_{eq} C},\quad f_c = \frac{\omega_c}{2\pi} = \frac{1}{2\pi R_{eq} C}",
            SubstitutionFormula = $@"H(s) = \frac{{1}}{{1 + {tauLatex} s}},\quad \omega_c = {ScientificNotationFormatter.ToLatexScientific(omegaC)}\text{{ rad/s}},\quad f_c = {fcLatex}\text{{ Hz}}",
            ResultValue = $"fc = {ScientificNotationFormatter.ToUnicodeScientific(fc)} Hz",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 5: Sinusoidal Steady-State Bode Frequency Characteristics
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "正弦稳态幅频与相频特性分析 (渐近线波特图特性)",
            MethodName = "频域波特图渐近线分析",
            Content = $"分析正弦稳态幅频与相频特性：通带内幅值增益为 1 (0 dB)；截止频率处幅值衰减 3 dB (0.707)，产生 -45° 相位滞后；高频阻带以 -20 dB/dec (-6 dB/oct) 固定斜率滚降。",
            LatexFormula = @"|A_u(jf)| = \frac{1}{\sqrt{1 + (f/f_c)^2}},\quad 20\lg |A_u(jf)| = -10\lg\left[1 + \left(\frac{f}{f_c}\right)^2\right]\text{ dB},\quad \varphi(f) = -\arctan\left(\frac{f}{f_c}\right)",
            SubstitutionFormula = $@"|A_u(jf_c)| = \frac{{1}}{{\sqrt{{2}}}} \approx 0.707\;(-3.01\text{{ dB}}),\quad \varphi(f_c) = -45.0^\circ",
            ResultValue = "通带增益: 0 dB, 滚降斜率: -20 dB/dec",
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
        foreach (var opt in EngineeringUnits.CurrentUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
