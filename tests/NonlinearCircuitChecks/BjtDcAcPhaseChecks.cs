// ============================================================================
// File: tests/NonlinearCircuitChecks/BjtDcAcPhaseChecks.cs
// OWNER: engine-bjt.
//
// Second-round user request (2026-10-18) verification for every BJT bias topologie
// (fixed / voltage divider / collector feedback / common base / common collector,
// NPN and PNP) and two MOSFET subtypes:
//   ① Every derivation step carries a DC/AC equivalent-circuit phase; all DC steps
//      are contiguous and come before the AC steps, and the DC/AC phase intros are
//      present (画直流等效电路 / 画交流等效电路).
//   ② The step-1 numeric chain follows the textbook estimation method with the
//      rounded displayed value fed into the next formula (checked against a hand
//      computed chain, tighter than 1 %).
//   ③ Every display text (Q point / AC parameters / summary / step formulas and
//      results) keeps at most two decimal places.
//   ④ The backend Thévenin-exact fields are populated (and, for voltage-divider
//      bias, matches the independently computed exact values; the displayed value
//      is the textbook estimate, so the two differ noticeably).
// ============================================================================

using System;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class BjtDcAcPhaseChecks
{
    public static void Run()
    {
        CheckResult.Section("BJT/MOSFET 教材估算法 + DC/AC 等效电路分组（四类偏置/共基/共集/2 个 MOS 子类型）");
        CheckFixedBiasNpn();
        CheckVoltageDividerNpn();
        CheckCollectorFeedbackNpn();
        CheckVoltageDividerPnp();
        CheckCommonBaseNpn();
        CheckCommonCollectorNpnDividerBias();
        CheckCommonCollectorPnpFixedBias();
        CheckElectrolyticPhasePlacement();
        CheckBjtSaturationRejection();
        CheckMosCommonSource();
        CheckMosCommonDrain();
    }

    // =====================================================================
    // Soft divider: the unloaded estimate gives -2.05 V, but the loaded DC
    // solution is forward-active (Ib=4.3/121k A, Vce=3.59256 V).
    // Recover using the loaded bias formula, keeping the rounded teaching chain.
    // =====================================================================
    private static void CheckBjtSaturationRejection()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("Rb1", ElementType.Resistor, 120.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 60.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Q1", ElementType.BjtNpn, 80.0, "");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        ElectricalNetlist netlist = builder.Build();

        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        CheckResult.Check(classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
                          classification.SubtypeName == "VoltageDividerSelfBias",
            "教材估算法饱和：仍分类为 BjtAmplifier/VoltageDividerSelfBias",
            $"{classification.TopologyType}/{classification.SubtypeName}");
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess,
            "软分压器：含负载偏置解在放大区时不能误报饱和", solution.SummaryText);
        CheckResult.TextContains("软分压器：明确切换为含基极负载方程", solution.SummaryText, "含基极负载");
        CheckResult.Check(solution.QPoint != null && solution.AcParameters != null,
            "软分压器：输出经过工作区核验的教学结果");
        CheckResult.Near("软分压器：舍入链管压降", 3.6, TestQuantity.Parse(solution.QPoint?.Vceq), .001);
        CheckResult.Near("软分压器：未舍入管压降", 3.59256198347, TestQuantity.Parse(solution.QPoint?.ExactVceq), .00001);

    }

    // =====================================================================
    // BJT: electrolytic-capacitor polarity checks belong to the DC phase and must
    // immediately follow the Q-point step (contract item 4); StepIndex is renumbered.
    // =====================================================================
    private static void CheckElectrolyticPhasePlacement()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 50.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.2, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("C1", ElementType.CapacitorPolarized, 10.0, "μF");
        builder.Element("C2", ElementType.CapacitorPolarized, 10.0, "μF");
        builder.Element("Ce", ElementType.CapacitorPolarized, 47.0, "μF");
        builder.Element("RL", ElementType.Resistor, 10.0, "kΩ");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("C1", "+", 2).Pin("C1", "-", 5)
               .Pin("C2", "+", 3).Pin("C2", "-", 6)
               .Pin("Ce", "+", 4).Pin("Ce", "-", 0)
               .Pin("RL", "1", 6).Pin("RL", "2", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        ElectricalNetlist netlist = builder.Build();

        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        CheckResult.Check(classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
                          classification.SubtypeName == "VoltageDividerSelfBias",
            "电解电容共射：分类 BjtAmplifier/VoltageDividerSelfBias",
            $"{classification.TopologyType}/{classification.SubtypeName}");
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess, "电解电容共射：求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Check(solution.Steps.Count == 8,
            "电解电容共射：5 个教学步骤 + 3 个极性核验步骤 = 8 步", $"实际 {solution.Steps.Count}");
        CheckResult.Check(solution.Steps[0].Phase == SolutionPhase.DcEquivalent &&
                          solution.Steps[0].Title.Contains("Q-Point", StringComparison.Ordinal),
            "电解电容共射：第 1 步为 DC 相 Q 点步骤", solution.Steps[0].Title);
        bool polarityStepsInDcPhase = true;
        for (int i = 1; i <= 3 && i < solution.Steps.Count; i++)
        {
            polarityStepsInDcPhase &= solution.Steps[i].Phase == SolutionPhase.DcEquivalent &&
                                      (solution.Steps[i].BadgeText is "极性安全" or "元件特性");
        }
        CheckResult.Check(polarityStepsInDcPhase,
            "电解电容共射：极性核验步骤紧跟 Q 点且归入 DC 相",
            string.Join("→", solution.Steps.Select(step => $"{step.StepIndex}:{step.Phase}/{step.BadgeText}")));
        CheckResult.Check(solution.Steps.Count(step => step.Phase == SolutionPhase.DcEquivalent) == 4 &&
                          solution.Steps.Count(step => step.Phase == SolutionPhase.AcEquivalent) == 4,
            "电解电容共射：DC 相 4 步（Q 点 + 3 个极性核验）、AC 相 4 步",
            $"DC={solution.Steps.Count(step => step.Phase == SolutionPhase.DcEquivalent)}, AC={solution.Steps.Count(step => step.Phase == SolutionPhase.AcEquivalent)}");
        bool sequential = solution.Steps.Select((step, index) => step.StepIndex == index + 1).All(ok => ok);
        CheckResult.Check(sequential, "电解电容共射：StepIndex 连续重排",
            string.Join(",", solution.Steps.Select(step => step.StepIndex)));
        CheckResult.AssertDisplayConventions("电解电容共射", solution);
    }

    // =====================================================================
    // BJT: fixed bias NPN — VCC=12 V, RB=400 kΩ, RC=3 kΩ, β=100
    //   IBQ=11.3/400k=28.25 µA; ICQ=100·28.25 µA=2.825 mA→2.83 mA;
    //   IEQ=101·28.25 µA=2.85325 mA→2.85 mA; VCEQ=12−2.83 mA·3 kΩ=3.51 V.
    // =====================================================================
    private static void CheckFixedBiasNpn()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Rb", ElementType.Resistor, 400.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb", "1", 1).Pin("Rb", "2", 2)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 0);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("NPN 固定偏置共射", netlist, "FixedBias",
            ub: double.NaN, ieq: 2.85e-3, ibq: 2.825e-5, icq: 2.83e-3, vceq: 3.51,
            exactIbq: 2.825e-5, exactIcq: 2.83e-3, exactVceq: 3.51, divider: false);
    }

    // =====================================================================
    // BJT: voltage-divider bias NPN — VCC=12 V, Rb1=100k, Rb2=50k, Rc=2.2k, Re=1k, β=100
    //   U_B=4 V; IEQ=(4−0.7)/1k=3.3 mA; IBQ=3.3 mA/101=32.67 µA;
    //   ICQ=100·32.67 µA=3.27 mA; UCEQ=12−3.27 mA·2.2k−3.3 mA·1k=1.51 V.
    //   Thévenin exact: Vth=4 V, Rth=33.333 kΩ → IBQ=3.3/134333.33=24.566 µA,
    //   ICQ=2.4566 mA, UCEQ=4.114 V.
    // =====================================================================
    private static void CheckVoltageDividerNpn()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Rb1", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 50.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.2, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("NPN 分压偏置共射", netlist, "VoltageDividerSelfBias",
            ub: 4.0, ieq: 3.3e-3, ibq: 3.267e-5, icq: 3.27e-3, vceq: 1.51,
            exactIbq: 2.45657e-5, exactIcq: 2.45657e-3, exactVceq: 4.1144, divider: true);
    }

    // =====================================================================
    // BJT: collector-feedback bias NPN — VCC=12 V, RB=180k (B–C), RC=2.4k, β=100
    //   IBQ=11.3/(180k+101·2.4k)=26.75 µA; ICQ=2.68 mA; IEQ=2.7 mA;
    //   UCEQ=12−(2.68 mA+26.75 µA)·2.4k=5.5 V.
    // =====================================================================
    private static void CheckCollectorFeedbackNpn()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Rb", ElementType.Resistor, 180.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.4, "kΩ");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 3)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 0);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("NPN 集电极反馈偏置共射", netlist, "CollectorFeedbackBias",
            ub: double.NaN, ieq: 2.7e-3, ibq: 2.675e-5, icq: 2.68e-3, vceq: 5.5,
            exactIbq: 2.67519e-5, exactIcq: 2.67519e-3, exactVceq: 5.5038, divider: false);
    }

    // =====================================================================
    // BJT: PNP voltage-divider bias — VCC=12 V, RB1=100k(B–VCC), RB2=200k(B–GND),
    //   RE=1k(E–VCC), RC=2k(C–GND), β=100, silicon V_EB=0.7 V (B06 型).
    //   U_B=8 V; IEQ=(12−8−0.7)/1k=3.3 mA; IBQ=32.67 µA; ICQ=3.27 mA;
    //   UECQ=12−3.27 mA·2k−3.3 mA·1k=2.16 V.
    //   Thévenin exact: Vth=8 V, Rth=66.667 kΩ → IBQ=3.3/167666.67=19.682 µA,
    //   ICQ=1.9682 mA, UECQ=6.0758 V.
    // =====================================================================
    private static void CheckVoltageDividerPnp()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC1", ElementType.VccPort, 12.0, "V");
        builder.Element("RB1", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("RB2", ElementType.Resistor, 200.0, "kΩ");
        builder.Element("RE", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("RC", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("VCC1", "VCC", 1).Pin("RB1", "1", 1).Pin("RE", "1", 1)
               .Pin("RE", "2", 2).Pin("Q1", "E", 2)
               .Pin("Q1", "B", 3).Pin("RB1", "2", 3).Pin("RB2", "1", 3)
               .Pin("Q1", "C", 4).Pin("RC", "1", 4)
               .Pin("RC", "2", 0).Pin("RB2", "2", 0);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("PNP 分压偏置共射", netlist, "VoltageDividerSelfBias",
            ub: 8.0, ieq: 3.3e-3, ibq: 3.267e-5, icq: 3.27e-3, vceq: 2.16,
            exactIbq: 1.96819e-5, exactIcq: 1.96819e-3, exactVceq: 6.07576, divider: true);
    }

    // =====================================================================
    // BJT: common base NPN — VCC=12 V, Rb1=100k, Rb2=50k, Rc=2k, Re=2k, β=100,
    //   Cb=10 µF (base AC ground), C1=10 µF input coupling to the emitter.
    //   U_B=4 V; IEQ=(4−0.7)/2k=1.65 mA; IBQ=1.65 mA/101=16.34 µA;
    //   ICQ=100·16.34 µA=1.63 mA; UCEQ=12−1.63 mA·2k−1.65 mA·2k=5.44 V.
    // =====================================================================
    private static void CheckCommonBaseNpn()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 50.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Cb", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Cb", "1", 2).Pin("Cb", "2", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("C1", "1", 5).Pin("C1", "2", 4)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("NPN 共基极", netlist, "CommonBase",
            ub: 4.0, ieq: 1.65e-3, ibq: 1.634e-5, icq: 1.63e-3, vceq: 5.44,
            exactIbq: 1.40227e-5, exactIcq: 1.40227e-3, exactVceq: 6.3629, divider: true);
    }

    // =====================================================================
    // BJT: common collector (emitter follower) NPN divider bias — VCC=12 V,
    //   Rb1=51k(B–VCC), Rb2=15k(B–GND), Re=1k, RL=10k, β=100.
    //   U_B=2.73 V; IEQ=(2.73−0.7)/1k=2.03 mA; IBQ=2.03 mA/101=20.1 µA;
    //   ICQ=2.01 mA; UCEQ=12−2.03 mA·1k=9.97 V.
    // =====================================================================
    private static void CheckCommonCollectorNpnDividerBias()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("RL", ElementType.Resistor, 10.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        builder.Net(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 3)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 3).Pin("Re", "2", 0)
               .Pin("C1", "1", 5).Pin("C1", "2", 2)
               .Pin("C2", "1", 3).Pin("C2", "2", 4)
               .Pin("RL", "1", 4).Pin("RL", "2", 0);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("NPN 分压偏置共集电极", netlist, "CommonCollector",
            ub: 2.73, ieq: 2.03e-3, ibq: 2.01e-5, icq: 2.01e-3, vceq: 9.97,
            exactIbq: 1.80057e-5, exactIcq: 1.80057e-3, exactVceq: 10.1814, divider: true,
            expectedSteps: 6);
    }

    // =====================================================================
    // BJT: common collector PNP fixed bias — VCC=12 V, β=80, RB=470k(B–GND),
    //   Re=1k(E–VCC), collector to GND, no load.
    //   IBQ=11.3/(470k+81·1k)=20.51 µA; IEQ=81·20.51 µA=1.66 mA;
    //   ICQ=80·20.51 µA=1.64 mA; UECQ=12−1.66 mA·1k=10.34 V.
    // =====================================================================
    private static void CheckCommonCollectorPnpFixedBias()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 80.0, "");
        builder.Element("Rb", ElementType.Resistor, 470.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Net(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 0).Pin("Q1", "E", 3)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 0)
               .Pin("Re", "1", 3).Pin("Re", "2", 1);
        ElectricalNetlist netlist = builder.Build();
        CheckBjtCase("PNP 固定偏置共集电极", netlist, "CommonCollector",
            ub: double.NaN, ieq: 1.66e-3, ibq: 2.051e-5, icq: 1.64e-3, vceq: 10.34,
            exactIbq: 2.05082e-5, exactIcq: 1.64065e-3, exactVceq: 10.3388, divider: false,
            expectedSteps: 6);
    }

    // =====================================================================
    // MOSFET: M01 NMOS common source — VDD=15 V, Vth=1.5 V, K=2 mA/V²,
    //   Rg1=1M, Rg2=200k, Rd=3k.
    //   VG=2.5 V; Vov=1 V; ID=2 mA; VDS=9 V; gm=4 mS; Av=−12; Ri=166.67 kΩ; Ro=3 kΩ.
    // =====================================================================
    private static void CheckMosCommonSource()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 1_000_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 200_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
               .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
               .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 0)
               .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
               .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
               .Pin("Rd", "1", 1).Pin("Rd", "2", 3)
               .Pin("C1", "1", 9).Pin("C1", "2", 2);
        ElectricalNetlist netlist = builder.Build();
        CheckMosCase("NMOS 共源极（M01）", netlist, "CommonSource",
            vg: 2.5, id: 2e-3, vgs: 2.5, vds: 9.0, vov: 1.0, gm: 4e-3,
            av: -12.0, ri: 166_670.0, ro: 3000.0);
    }

    // =====================================================================
    // MOSFET: M04 NMOS common drain — VDD=15 V, Rg1=Rg2=1M (VG=7.5 V),
    //   Rs=2.5k, RL=10k, K=2 mA/V², Vth=1.5 V.
    //   a=6 V; quadratic 12500·ID²−61·ID+0.072=0 → ID=2 mA; Vov=1 V;
    //   VGS=2.5 V; VDS=10 V; gm=4 mS; Rs∥RL=2 kΩ → Av=8/9→0.89;
    //   Ri=500 kΩ; Ro=2.5k∥250=227.27 Ω.
    // =====================================================================
    private static void CheckMosCommonDrain()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 1_000_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 1_000_000.0, "Ω");
        builder.Element("Rs", ElementType.Resistor, 2_500.0, "Ω");
        builder.Element("RL", ElementType.Resistor, 10_000.0, "Ω");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Element("C2", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
               .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
               .Pin("M1", "G", 2).Pin("M1", "D", 1).Pin("M1", "S", 4)
               .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
               .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
               .Pin("Rs", "1", 4).Pin("Rs", "2", 0)
               .Pin("C1", "1", 9).Pin("C1", "2", 2)
               .Pin("C2", "1", 4).Pin("C2", "2", 6)
               .Pin("RL", "1", 6).Pin("RL", "2", 0);
        ElectricalNetlist netlist = builder.Build();
        CheckMosCase("NMOS 共漏极（M04）", netlist, "CommonDrain",
            vg: 7.5, id: 2e-3, vgs: 2.5, vds: 10.0, vov: 1.0, gm: 4e-3,
            av: 0.89, ri: 500_000.0, ro: 227.27);
    }

    // =====================================================================
    // Shared BJT assertions
    // =====================================================================
    private static void CheckBjtCase(
        string name,
        ElectricalNetlist netlist,
        string expectedSubtype,
        double ub,
        double ieq,
        double ibq,
        double icq,
        double vceq,
        double exactIbq,
        double exactIcq,
        double exactVceq,
        bool divider,
        int expectedSteps = 5)
    {
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        CheckResult.Check(classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
                          classification.SubtypeName == expectedSubtype,
            $"{name}：分类 BjtAmplifier/{expectedSubtype}",
            $"{classification.TopologyType}/{classification.SubtypeName}");

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess, $"{name}：求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        // ① DC/AC 相位分组（共射/共基 5 步、共集 6 步）
        CheckResult.Check(solution.Steps.Count == expectedSteps,
            $"{name}：教学步骤仍为 {expectedSteps} 步",
            $"实际 {solution.Steps.Count}");
        BjtVdEstimateChecks.CheckPhaseContract(name, solution, expectedDcSteps: 1);
        PedagogicalSolutionStep? firstAcStep = null;
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            if (step.Phase == SolutionPhase.AcEquivalent) { firstAcStep = step; break; }
        }
        CheckResult.TextContains($"{name}：AC 相第一步注明晶体管用 h 参数微变模型替代",
            firstAcStep?.Content, "h 参数", "βi_b");

        // ② 第 1 步数值 = 教材估算法舍入回代链（独立手算）
        PedagogicalSolutionStep step1 = solution.Steps[0];
        CheckResult.Close($"{name}：教材链 I_BQ", ibq, BjtVdEstimateChecks.StepValue(step1, "IBQ"), 1e-6);
        CheckResult.Close($"{name}：教材链 I_CQ", icq, BjtVdEstimateChecks.StepValue(step1, "ICQ"), 1e-6);
        CheckResult.Close($"{name}：教材链 I_EQ", ieq, BjtVdEstimateChecks.StepValue(step1, "IEQ"), 1e-6);
        CheckResult.Close($"{name}：教材链 U_CEQ", vceq, BjtVdEstimateChecks.StepValue(step1, "VCEQ"), 1e-6);
        if (divider)
        {
            CheckResult.Close($"{name}：教材链 U_B（空载分压）", ub, BjtVdEstimateChecks.StepValue(step1, "UB"), 1e-6);
            // 显示层确实采用教材估算法（与戴维南精确解明显不同，仅后端保留精确值）
            CheckResult.Check(Math.Abs(BjtVdEstimateChecks.StepValue(step1, "IBQ") - exactIbq) > 0.05 * exactIbq,
                $"{name}：显示链 I_BQ 为教材估算值（≠后端精确值）",
                $"estimate={CheckResult.Format(BjtVdEstimateChecks.StepValue(step1, "IBQ"))}, exact={CheckResult.Format(exactIbq)}");
        }

        // ② Q 点显示值与教材链一致（显示级）
        CheckResult.Close($"{name}：Q 点面板 I_BQ 显示值", ibq, TestQuantity.Parse(solution.QPoint?.Ibq), 0.01);
        CheckResult.Close($"{name}：Q 点面板 I_CQ 显示值", icq, TestQuantity.Parse(solution.QPoint?.Icq), 0.01);
        CheckResult.Close($"{name}：Q 点面板 U_CEQ 显示值", vceq, TestQuantity.Parse(solution.QPoint?.Vceq), 0.01);

        // ④ 后端 Exact* 字段非空（分压偏置再核对戴维南精确值）
        CheckResult.Check(!string.IsNullOrWhiteSpace(solution.QPoint?.ExactIbq) &&
                          !string.IsNullOrWhiteSpace(solution.QPoint?.ExactIcq) &&
                          !string.IsNullOrWhiteSpace(solution.QPoint?.ExactVceq),
            $"{name}：后端 Exact* 字段非空",
            $"{solution.QPoint?.ExactIbq} / {solution.QPoint?.ExactIcq} / {solution.QPoint?.ExactVceq}");
        if (divider)
        {
            CheckResult.Close($"{name}：后端 ExactIbq = 戴维南精确值", exactIbq,
                TestQuantity.Parse(solution.QPoint?.ExactIbq), 0.005);
            CheckResult.Close($"{name}：后端 ExactIcq = 戴维南精确值", exactIcq,
                TestQuantity.Parse(solution.QPoint?.ExactIcq), 0.005);
            CheckResult.Close($"{name}：后端 ExactVceq = 戴维南精确值", exactVceq,
                TestQuantity.Parse(solution.QPoint?.ExactVceq), 0.005);
        }

        // ③ 所有显示文本最多两位小数，且无“戴维南 vs 教材”双口径文案
        CheckResult.AssertDisplayConventions(name, solution);
    }

    // =====================================================================
    // Shared MOSFET assertions
    // =====================================================================
    private static void CheckMosCase(
        string name,
        ElectricalNetlist netlist,
        string expectedSubtype,
        double vg,
        double id,
        double vgs,
        double vds,
        double vov,
        double gm,
        double av,
        double ri,
        double ro)
    {
        string detected = MosfetAmplifierStrategy.DetectSubtype(netlist);
        CheckResult.Check(detected == expectedSubtype, $"{name}：DetectSubtype = {expectedSubtype}", $"got {detected}");

        PedagogicalSolution solution = new MosfetAmplifierStrategy(expectedSubtype)
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess, $"{name}：求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        // ① DC 步（V_G/I_D/V_DS）连续在前，AC 步（gm/Av/RiRo/fL）连续在后
        CheckResult.Check(solution.Steps.Count == 7, $"{name}：教学步骤仍为 7 步", $"实际 {solution.Steps.Count}");
        BjtVdEstimateChecks.CheckPhaseContract(name, solution, expectedDcSteps: 3);
        PedagogicalSolutionStep? firstAcStep = null;
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            if (step.Phase == SolutionPhase.AcEquivalent) { firstAcStep = step; break; }
        }
        CheckResult.TextContains($"{name}：AC 相第一步注明 MOSFET 用 gm·v_gs 与 ro 微变模型替代",
            firstAcStep?.Content, "gm·v_gs", "ro", "微变模型替代");

        // ② 第 1 步数值 = 教材估算法舍入回代链（独立手算）
        PedagogicalSolutionStep step1 = solution.Steps[0];
        CheckResult.Close($"{name}：教材链 V_G", vg, BjtVdEstimateChecks.StepValue(step1, "VG"), 1e-6);
        CheckResult.Close($"{name}：教材链 I_D", id, BjtVdEstimateChecks.StepValue(step1, "ID"), 1e-6);
        CheckResult.Close($"{name}：教材链 V_GS", vgs, BjtVdEstimateChecks.StepValue(step1, "VGS"), 1e-6);
        CheckResult.Close($"{name}：教材链 |V_DS|/V_SD", vds, Math.Abs(BjtVdEstimateChecks.StepValue(step1, "VDS")), 1e-6);
        CheckResult.Close($"{name}：教材链 V_ov", vov, BjtVdEstimateChecks.StepValue(step1, "Vov"), 1e-6);
        CheckResult.Close($"{name}：教材链 gm", gm, BjtVdEstimateChecks.StepValue(step1, "gm"), 1e-6);
        CheckResult.Close($"{name}：教材链 Av", av, BjtVdEstimateChecks.StepValue(step1, "Av"), 1e-6);
        CheckResult.Close($"{name}：教材链 Ri", ri, BjtVdEstimateChecks.StepValue(step1, "Ri"), 1e-6);
        CheckResult.Close($"{name}：教材链 Ro", ro, BjtVdEstimateChecks.StepValue(step1, "Ro"), 1e-6);
        CheckResult.Close($"{name}：Q 点面板 I_D 显示值", id, TestQuantity.Parse(solution.QPoint?.Icq), 0.01);
        CheckResult.Close($"{name}：Q 点面板 V_DS 显示值", vds, TestQuantity.Parse(solution.QPoint?.Vceq), 0.01);
        CheckResult.TextContains($"{name}：摘要注明直流/交流等效电路", solution.SummaryText,
            "直流等效电路", "交流等效电路");

        // ③ 所有显示文本最多两位小数（无“戴维南 vs 教材”双口径文案）
        CheckResult.AssertDisplayConventions(name, solution);
    }
}
