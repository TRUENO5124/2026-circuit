// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/BjtAmplifierStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical Solving Strategy for BJT Common-Emitter Amplifiers
//              (DC Q-Point, small-signal rbe, Av, Ri, Ro, Bode frequency response).
// ============================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

public class BjtAmplifierStrategy : ICircuitStrategy
{
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => CircuitTopologyType.BjtAmplifier;
    public string SubtypeName => _subtype;

    public BjtAmplifierStrategy(string subtype = "VoltageDividerSelfBias")
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
            CircuitTitle = $"双极型晶体管 (BJT) {GetSubtypeDisplayName(_subtype)}交流小信号与频响分析"
        };

        // Extract components from netlist
        var bjt = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        double beta = bjt != null ? Math.Max(1.0, bjt.DisplayNumber) : 100.0;

        double vcc = 12.0;
        double rb1 = 51000.0;
        double rb2 = 15000.0;
        double rc = 3000.0;
        double re = 1000.0;
        double rl = 10000.0;
        double c1 = 10e-6;
        double c2 = 10e-6;
        double ce = 47e-6;

        foreach (var el in netlist.Elements.Values)
        {
            double val = GetSiValue(el);
            if (el.Id == "VCC") vcc = val;
            else if (el.Id == "Rb1" || el.Id == "R1") rb1 = val;
            else if (el.Id == "Rb2" || el.Id == "R2") rb2 = val;
            else if (el.Id == "Rb") { rb1 = val; rb2 = 0; }
            else if (el.Id == "Rc") rc = val;
            else if (el.Id == "Re") re = val;
            else if (el.Id == "RL") rl = val;
            else if (el.Id == "C1") c1 = val;
            else if (el.Id == "C2") c2 = val;
            else if (el.Id == "Ce") ce = val;
        }

        // DC Calculations
        double vth, rth, ibq, icq, vceq;
        if (_subtype == "FixedBias" || rb2 <= 0)
        {
            vth = vcc;
            rth = rb1;
            ibq = (vcc - 0.7) / Math.Max(1.0, rb1);
            icq = beta * ibq;
            vceq = vcc - icq * rc;
        }
        else if (_subtype == "CollectorFeedbackBias")
        {
            vth = vcc;
            rth = rb1;
            ibq = (vcc - 0.7) / Math.Max(1.0, rb1 + (1 + beta) * rc);
            icq = beta * ibq;
            vceq = vcc - (icq + ibq) * rc;
        }
        else // VoltageDividerSelfBias
        {
            vth = vcc * (rb2 / (rb1 + rb2));
            rth = (rb1 * rb2) / (rb1 + rb2);
            ibq = (vth - 0.7) / Math.Max(1.0, rth + (1 + beta) * re);
            icq = beta * ibq;
            vceq = vcc - icq * rc - (1 + beta) * ibq * re;
        }

        // Ensure values remain physical
        if (ibq < 0) ibq = 1e-6;
        if (icq < 0) icq = beta * ibq;
        bool isActive = vceq > 0.2;
        string regionName = isActive ? "放大区 (Active Region)" : "饱和区 (Saturation)";

        // Small-signal calculations
        double ut = parameters.ThermalVoltageUt > 0 ? parameters.ThermalVoltageUt : 0.026;
        double rbe = 200.0 + (1 + beta) * (ut / Math.Max(icq, 1e-6));
        double rlPrime = (rc * rl) / (rc + rl);

        bool hasCe = netlist.Elements.Values.Any(e => e.Id == "Ce");
        double av = hasCe ? (-beta * rlPrime / rbe) : (-beta * rlPrime / (rbe + (1 + beta) * re));

        double ri;
        if (_subtype == "FixedBias" || rb2 <= 0)
        {
            ri = (rb1 * rbe) / (rb1 + rbe);
        }
        else
        {
            double rbEquiv = (rb1 * rb2) / (rb1 + rb2);
            ri = (rbEquiv * rbe) / (rbEquiv + rbe);
        }
        double ro = rc;

        // Cutoff Frequencies
        double fl1 = 1.0 / (2 * Math.PI * Math.Max(1.0, ri) * Math.Max(1e-12, c1));
        double fl2 = 1.0 / (2 * Math.PI * Math.Max(1.0, rc + rl) * Math.Max(1e-12, c2));
        double rePrime = (re * (rbe + rth)) / Math.Max(1.0, (1 + beta) * re + rbe + rth);
        double fle = hasCe ? (1.0 / (2 * Math.PI * Math.Max(0.1, rePrime) * Math.Max(1e-12, ce))) : 0;
        double fl = Math.Sqrt(fl1 * fl1 + fl2 * fl2 + fle * fle);

        double cPi = 30e-12;
        double cMu = 3e-12;
        double cm = cMu * (1 + Math.Abs(av));
        double cInPrime = cPi + cm;
        double rInPrime = (ri * rbe) / Math.Max(1.0, ri + rbe);
        double fh = 1.0 / (2 * Math.PI * Math.Max(1.0, rInPrime) * Math.Max(1e-15, cInPrime));
        double bw = Math.Max(0, fh - fl);

        solution.QPoint = new QPointInfo
        {
            Ibq = $"{ScientificNotationFormatter.ToUnicodeScientific(ibq * 1e6)} μA",
            Icq = $"{ScientificNotationFormatter.ToUnicodeScientific(icq * 1e3)} mA",
            Vceq = $"{ScientificNotationFormatter.ToUnicodeScientific(vceq)} V",
            WorkingRegion = regionName,
            IsActiveRegion = isActive
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = $"{av:F2}",
            InputResistance = ScientificNotationFormatter.FormatWithUnit(ri, "Ω"),
            OutputResistance = ScientificNotationFormatter.FormatWithUnit(ro, "Ω"),
            LowerCutoffFreq = ScientificNotationFormatter.FormatWithUnit(fl, "Hz"),
            UpperCutoffFreq = ScientificNotationFormatter.FormatWithUnit(fh, "Hz"),
            Bandwidth = ScientificNotationFormatter.FormatWithUnit(bw, "Hz")
        };

        solution.SummaryText = $"BJT 放大电路求解完成：直流 Q 点 (IBQ={solution.QPoint.Ibq}, ICQ={solution.QPoint.Icq}, VCEQ={solution.QPoint.Vceq})；交流放大倍数 Av = {av:F2}，输入电阻 Ri = {solution.AcParameters.InputResistance}，通频带 BW = {solution.AcParameters.Bandwidth}。";

        // Step 1: DC Q-point
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "基极戴维南等效与直流静态工作点 (Q-Point) 求解",
            MethodName = "戴维南等效定理与基极回路 KVL",
            Content = "对分压式基极偏置电路应用戴维南定理求解基极开路等效电压 Vth 与等效电阻 Rth，列写基极-发射极回路 KVL 方程求得静态工作点。",
            LatexFormula = @"V_{th} = V_{CC} \frac{R_{b2}}{R_{b1} + R_{b2}},\quad R_{th} = R_{b1} \parallel R_{b2},\quad I_{BQ} = \frac{V_{th} - V_{BE}}{R_{th} + (1+\beta)R_e}",
            SubstitutionFormula = $@"I_{{BQ}} = {ScientificNotationFormatter.ToLatexScientific(ibq * 1e6)}\;\mu\text{{A}},\quad I_{{CQ}} = {ScientificNotationFormatter.ToLatexScientific(icq * 1e3)}\;\text{{mA}},\quad V_{{CEQ}} = {ScientificNotationFormatter.ToLatexScientific(vceq)}\;\text{{V}}",
            ResultValue = $"Q点: IBQ={ScientificNotationFormatter.ToUnicodeScientific(ibq * 1e6)} μA, ICQ={ScientificNotationFormatter.ToUnicodeScientific(icq * 1e3)} mA, VCEQ={ScientificNotationFormatter.ToUnicodeScientific(vceq)} V",
            Status = "Completed"
        });

        // Step 2: rbe
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "晶体管低频小信号微变等效参数 rbe 计算",
            MethodName = "晶体管 h 参数简化模型",
            Content = "根据 PN 结伏安特性与扩散电阻理论，求解晶体管基区体电阻 rbb' 与发射结微变电阻之和。",
            LatexFormula = @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} \approx 200\,\Omega + (1+\beta)\frac{26\,\text{mV}}{I_{CQ}}",
            SubstitutionFormula = $@"r_{{be}} = 200 + (1 + {beta:F0}) \frac{{26\text{{ mV}}}}{{{ScientificNotationFormatter.ToLatexScientific(icq * 1e3)}\text{{ mA}}}} = {ScientificNotationFormatter.ToLatexScientific(rbe)}\;\Omega",
            ResultValue = $"rbe = {ScientificNotationFormatter.ToUnicodeScientific(rbe)} Ω ({ScientificNotationFormatter.ToUnicodeScientific(rbe / 1e3)} kΩ)",
            Status = "Completed"
        });

        // Step 3: Av
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "中频段交流电压放大倍数 Av 推导与求解",
            MethodName = "微变等效电路交流分析",
            Content = "绘制交流等效电路，隔直电容与旁路电容对交流短路，输出端并联交流等效负载 RL' = Rc // RL，计算输出电压对输入电压的比值。",
            LatexFormula = @"A_v = \frac{\dot{U}_o}{\dot{U}_i} = -\frac{\beta (R_c \parallel R_L)}{r_{be}}",
            SubstitutionFormula = $@"A_v = -\frac{{{beta:F0} \times ({ScientificNotationFormatter.ToLatexScientific(rc)} \parallel {ScientificNotationFormatter.ToLatexScientific(rl)})}}{{{ScientificNotationFormatter.ToLatexScientific(rbe)}}} = {av:F2}",
            ResultValue = $"Av = {av:F2}",
            Status = "Completed"
        });

        // Step 4: Ri and Ro
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "放大电路输入电阻 Ri 与输出电阻 Ro 计算",
            MethodName = "输入/输出阻抗定义法",
            Content = "输入电阻为从放大器输入端看入的等效交流电阻；输出电阻为信号源置零、去除负载后从输出端看入的等效交流电阻。",
            LatexFormula = @"R_i = R_{b1} \parallel R_{b2} \parallel r_{be},\quad R_o = R_c",
            SubstitutionFormula = $@"R_i = {ScientificNotationFormatter.ToLatexScientific(ri / 1e3)}\;\text{{k}}\Omega,\quad R_o = {ScientificNotationFormatter.ToLatexScientific(ro / 1e3)}\;\text{{k}}\Omega",
            ResultValue = $"Ri = {ScientificNotationFormatter.ToUnicodeScientific(ri / 1e3)} kΩ, Ro = {ScientificNotationFormatter.ToUnicodeScientific(ro / 1e3)} kΩ",
            Status = "Completed"
        });

        // Step 5: Bode Frequency Characteristics
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "频率特性分析（密勒效应、极点时间常数与通频带 BW）",
            MethodName = "开路/短路时间常数法 (SCTC/OCTC)",
            Content = "下限截止频率 fL 由耦合电容与旁路电容的时间常数决定；上限截止频率 fH 由极间寄生结电容与密勒倍增效应形成的主极点决定。",
            LatexFormula = @"f_L = \sqrt{f_{L1}^2 + f_{L2}^2 + f_{Le}^2},\quad f_H \approx \frac{1}{2\pi R_{in}' C_{in}'},\quad BW = f_H - f_L",
            SubstitutionFormula = $@"f_L = {ScientificNotationFormatter.ToLatexScientific(fl)}\;\text{{Hz}},\quad f_H = {ScientificNotationFormatter.ToLatexScientific(fh / 1e6)}\;\text{{MHz}},\quad BW = {ScientificNotationFormatter.ToLatexScientific(bw / 1e6)}\;\text{{MHz}}",
            ResultValue = $"BW = {ScientificNotationFormatter.ToUnicodeScientific(bw / 1e6)} MHz (fL={fl:F1} Hz, fH={fh / 1e6:F2} MHz)",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static string GetSubtypeDisplayName(string subtype) => subtype switch
    {
        "VoltageDividerSelfBias" => "分压式自偏置共射",
        "FixedBias" => "固定偏置阻容耦合共射",
        "CollectorFeedbackBias" => "集电极反馈偏置共射",
        _ => "共射极"
    };

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
