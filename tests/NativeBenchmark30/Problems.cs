// ============================================================================
// File: tests/NativeBenchmark30/Problems.cs
// Description: The 30 textbook problems of the native benchmark. Every problem
//              is an independent textbook-style question:
//                * known circuit parameters are declared explicitly;
//                * the hand derivation / textbook source is written in the
//                  comment block (三要素法 / 二阶特征根 / 微变等效模型 /
//                  节点分析与电路定理等经典结论);
//                * the expected value is computed from those independent
//                  textbook formulas, never copied from an engine implementation;
//                * the engine is driven through its public API
//                  (TopologyClassifier -> strategy, or LinearNetworkAnalyzer /
//                  MnaLinearStrategy structured results) and the numeric output
//                  is compared with the hand value at the stated tolerance.
//
//              Coverage (30 questions):
//                F01-F06  一阶 RC/RL            6
//                S01-S06  二阶 RLC              6
//                B01-B08  单管 NPN BJT 放大     8
//                M01-M10  通用线性 MNA 2.0     10
// ============================================================================

using System.Globalization;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace NativeBenchmark30;

internal static class Problems
{
    public static List<BenchmarkCase> BuildAll() => new()
    {
        // ---------------- 一阶 RC / RL（6 题） ----------------
        Case("F01", "RC 低通 DC 阶跃：Req/τ/uC(3τ)/uC(5τ)/fc", FirstOrder01),
        Case("F02", "RC 低通 AC 滤波：τ/fc/-3dB 上限截止", FirstOrder02),
        Case("F03", "RC 高通 AC 滤波：τ/fc/-3dB 下限截止", FirstOrder03),
        Case("F04", "RC 双电阻串联 DC 阶跃：Req=ΣR /τ/时域值", FirstOrder04),
        Case("F05", "RL DC 阶跃：Req/τ/iL(3τ)/fc", FirstOrder05),
        Case("F06", "RL AC 滤波：τ/fc/iL(3τ)", FirstOrder06),

        // ---------------- 二阶 RLC（6 题） ----------------
        Case("S01", "串联 RLC 谐振（欠阻尼）：ω0/f0/Q/α/BW", SecondOrder01),
        Case("S02", "串联 RLC 过阻尼：α/Q/BW/阻尼类型", SecondOrder02),
        Case("S03", "串联 RLC 临界阻尼：α/Q/BW/阻尼类型", SecondOrder03),
        Case("S04", "并联 RLC 谐振（欠阻尼）：ω0/f0/Q/α/BW", SecondOrder04),
        Case("S05", "并联 RLC 过阻尼：α/Q/BW/阻尼类型", SecondOrder05),
        Case("S06", "并联 RLC 高 Q 谐振：f0/Q/α/BW", SecondOrder06),

        // ---------------- 单管 NPN BJT 放大（8 题） ----------------
        Case("B01", "固定偏置共射：Q点/Av/Ri/Ro", Bjt01),
        Case("B02", "固定偏置共射（第二组参数）：Q点/Av/Ri/Ro", Bjt02),
        Case("B03", "分压偏置无旁路：Q点/Av/Ri/Ro", Bjt03),
        Case("B04", "分压偏置带旁路：Q点/Av/Ri/Ro", Bjt04),
        Case("B05", "集电极反馈偏置：Q点/Av/Ri/Ro", Bjt05),
        Case("B06", "集电极反馈偏置带负载：Q点/Av/Ri/Ro", Bjt06),
        Case("B07", "共基极放大：Q点/Au/Ri/Ro", Bjt07),
        Case("B08", "共基极放大（第二组参数，Re=2 kΩ 教材估算法下仍处放大区）：Q点/Au/Ri/Ro", Bjt08),

        // ---------------- 通用线性网络 MNA 2.0（10 题） ----------------
        Case("M01", "双独立源 KCL 节点电位（MNA 结构化结果）", Mna01),
        Case("M02", "分压器戴维南等效 Voc/Req（MNA 结构化结果）", Mna02),
        Case("M03", "电桥任意端口 Req 与节点电位（MNA）", Mna03),
        Case("M04", "VCVS 受控源网络（MNA）", Mna04),
        Case("M05", "VCCS 受控源网络（MNA）", Mna05),
        Case("M06", "CCVS 受控源网络（MNA）", Mna06),
        Case("M07", "CCCS 受控源网络（MNA）", Mna07),
        Case("M08", "含 C/L 的直流网络：电容开路 / 电感短路（MNA）", Mna08),
        Case("M09", "含并联电容的交流网络：|H|/相位/节点/-3dB（MNA 结构化）", Mna09),
        Case("M10", "含并联电感的交流网络：|H|/相位/-3dB 扫频（MNA 结构化）", Mna10)
    };

    private static BenchmarkCase Case(string id, string topic, Func<IReadOnlyList<CheckOutcome>> run) =>
        new() { Id = id, Topic = topic, Run = run };

    // =====================================================================
    // Step extraction helpers (strategy outputs are display strings; the
    // benchmark only parses them, it never re-derives engine internals).
    // =====================================================================

    private static string StepResult(PedagogicalSolution solution, string titleFragment)
    {
        PedagogicalSolutionStep? step = solution.Steps.FirstOrDefault(
            s => s.Title.Contains(titleFragment, StringComparison.Ordinal));
        if (step == null)
        {
            throw new InvalidOperationException(
                $"策略输出缺少标题含 '{titleFragment}' 的推导步骤（现有：{string.Join("|", solution.Steps.Select(s => s.Title))}）。");
        }
        return step.ResultValue;
    }

    private static double StepValue(PedagogicalSolution solution, string titleFragment, string marker) =>
        Parsing.ExtractQuantity(StepResult(solution, titleFragment), marker);

    // =====================================================================
    // Section F: 一阶 RC / RL
    // =====================================================================

    // F01 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第7章“一阶电路的时域分析”——
    //           三要素法 uC(t)=uC(∞)+[uC(0+)-uC(∞)]e^(-t/τ)；
    //           第11章“电路的频率响应”——一阶低通截止频率 fc=1/(2πRC)。
    // 已知：Vs=12 V 直流，R=2 kΩ，C=1 μF，零初始状态（电容电压连续）。
    // 手算：τ = R·C = 2e3 × 1e-6 = 2 ms；
    //       uC(∞) = Vs = 12 V；
    //       uC(3τ) = 12(1-e^-3) = 12 × 0.9502129 = 11.4026 V；
    //       uC(5τ) = 12(1-e^-5) = 12 × 0.9932621 = 11.9191 V；
    //       fc = 1/(2πτ) = 1/(2π × 2e-3) = 79.5775 Hz。
    // 期望值容差：默认 1%（显示文本按 3 位有效数字/3 位小数格式化，误差远小于 1%）。
    private static IReadOnlyList<CheckOutcome> FirstOrder01()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 12, "V")
            .Element("R1", ElementType.Resistor, 2, "kΩ")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("C1", "1", 2).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 2e3, c = 1e-6, vs = 12.0;
        double tau = r * c;
        double u3 = vs * (1.0 - Math.Exp(-3.0));
        double u5 = vs * (1.0 - Math.Exp(-5.0));
        double fc = 1.0 / (2.0 * Math.PI * tau);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRc, "DcStepTransient"),
            Checks.Number("Req", r, Parsing.ParseQuantity(StepResult(solution, "戴维南等效电阻")), unit: "Ω"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("uC(3τ)", u3, StepValue(solution, "微分方程", "u_C(3τ) = "), unit: "V"),
            Checks.Number("uC(5τ)", u5, StepValue(solution, "微分方程", "u_C(5τ) = "), unit: "V"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz")
        };
    }

    // F02 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第11章“电路的频率响应”——RC 低通
    //           H(jω)=1/(1+jωRC)，截止角频率 ωc=1/(RC)，fc=1/(2πRC)。
    // 已知：交流源 1 V（1 kHz 工作点），R=1 kΩ，C=100 nF（输出取电容电压）。
    // 手算：τ = RC = 1e3 × 100e-9 = 1e-4 s = 0.1 ms；
    //       fc = 1/(2π × 1e-4) = 1591.549 Hz。
    //       -3 dB 增益发生在 |H|=1/√2 处，频率即 fc（教科书半功率点定义）。
    // 期望值容差：τ/fc 用默认 1%；-3 dB 截止由 AC 参数显示文本给出。
    private static IReadOnlyList<CheckOutcome> FirstOrder02()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("C1", ElementType.Capacitor, 100, "nF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("C1", "1", 2).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 1e3, c = 100e-9;
        double tau = r * c;
        double fc = 1.0 / (2.0 * Math.PI * tau);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRc, "LowPassFilter"),
            Checks.Number("Req", r, Parsing.ParseQuantity(StepResult(solution, "戴维南等效电阻")), unit: "Ω"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz"),
            Checks.Number("fH(-3dB)", fc, Parsing.ParseQuantity(solution.AcParameters!.UpperCutoffFreq), unit: "Hz")
        };
    }

    // F03 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第11章——RC 高通 H(jω)=jωRC/(1+jωRC)，
    //           截止频率同样为 fc=1/(2πRC)，通带在 f≫fc，低频以 +20 dB/dec 上升。
    // 已知：交流源 1 V，C=100 nF 串在信号源与输出节点之间，R=1 kΩ 接输出到地。
    // 手算：τ = RC = 1e-4 s = 0.1 ms；fc = 1/(2π × 1e-4) = 1591.549 Hz；
    //       高通模式在 AcParameters 中应给出下限截止 fL = fc（上限定性为“无上限”）。
    // 期望值容差：默认 1%。
    private static IReadOnlyList<CheckOutcome> FirstOrder03()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 1, "V")
            .Element("C1", ElementType.Capacitor, 100, "nF")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 2)
            .Pin("R1", "1", 2).Pin("R1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 1e3, c = 100e-9;
        double tau = r * c;
        double fc = 1.0 / (2.0 * Math.PI * tau);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRc, "HighPassFilter"),
            Checks.Number("Req", r, Parsing.ParseQuantity(StepResult(solution, "戴维南等效电阻")), unit: "Ω"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz"),
            Checks.Number("fL(-3dB)", fc, Parsing.ParseQuantity(solution.AcParameters!.LowerCutoffFreq), unit: "Hz")
        };
    }

    // F04 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第7章——串联回路一阶电路，从储能元件
    //           端口看入的戴维南等效电阻 Req 等于回路中各电阻之和（源置零）。
    // 已知：Vs=10 V 直流，R1=1 kΩ、R2=3 kΩ 与 C=1 μF 串联，零初始状态。
    // 手算：Req = R1+R2 = 4 kΩ；τ = Req·C = 4e3 × 1e-6 = 4 ms；
    //       uC(3τ) = 10(1-e^-3) = 9.50213 V；uC(5τ) = 10(1-e^-5) = 9.93262 V；
    //       fc = 1/(2πτ) = 39.7887 Hz。
    // 期望值容差：默认 1%。
    private static IReadOnlyList<CheckOutcome> FirstOrder04()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 10, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("R2", ElementType.Resistor, 3, "kΩ")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("R2", "1", 2).Pin("R2", "2", 3)
            .Pin("C1", "1", 3).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r1 = 1e3, r2 = 3e3, c = 1e-6, vs = 10.0;
        double req = r1 + r2;
        double tau = req * c;
        double u3 = vs * (1.0 - Math.Exp(-3.0));
        double u5 = vs * (1.0 - Math.Exp(-5.0));
        double fc = 1.0 / (2.0 * Math.PI * tau);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRc, "DcStepTransient"),
            Checks.Number("Req", req, Parsing.ParseQuantity(StepResult(solution, "戴维南等效电阻")), unit: "Ω"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("uC(3τ)", u3, StepValue(solution, "微分方程", "u_C(3τ) = "), unit: "V"),
            Checks.Number("uC(5τ)", u5, StepValue(solution, "微分方程", "u_C(5τ) = "), unit: "V"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz")
        };
    }

    // F05 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第7章——RL 一阶电路，电感电流连续；
    //           三要素法 iL(t)=iL(∞)(1-e^(-t/τ))，τ=L/Req。
    // 已知：Vs=10 V 直流，R=100 Ω，L=500 mH，零初始电流。
    // 手算：Req = R = 100 Ω；τ = L/Req = 0.5/100 = 5 ms；
    //       iL(∞) = Vs/Req = 0.1 A；iL(3τ) = 0.1 × 0.9502129 = 95.0213 mA；
    //       RL 低通（取电阻电压）截止频率 fc = Req/(2πL) = 100/(2π × 0.5) = 31.8310 Hz。
    // 期望值容差：默认 1%。
    private static IReadOnlyList<CheckOutcome> FirstOrder05()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 10, "V")
            .Element("R1", ElementType.Resistor, 100, "Ω")
            .Element("L1", ElementType.Inductor, 500, "mH")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 100.0, l = 500e-3, vs = 10.0;
        double tau = l / r;
        double i3 = vs / r * (1.0 - Math.Exp(-3.0));
        double fc = r / (2.0 * Math.PI * l);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRl, "RlTransient"),
            Checks.Number("Req", r, Parsing.ParseQuantity(StepResult(solution, "戴维南等效电阻")), unit: "Ω"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("iL(3τ)", i3, StepValue(solution, "微分方程", "i_L(3τ) = "), unit: "A"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz")
        };
    }

    // F06 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第11章——RL 单时间常数电路
    //           H(jω)=1/(1+jωL/R)，fc=R/(2πL)；τ=L/R。
    // 已知：交流源 1 V，R=1 kΩ，L=100 mH，零初始电流。
    // 手算：τ = L/R = 0.1/1000 = 1e-4 s = 0.1 ms；
    //       fc = R/(2πL) = 1000/(2π × 0.1) = 1591.549 Hz；
    //       iL(∞)=1/1000 A，iL(3τ)=1e-3 × 0.9502129 = 9.50213e-4 A。
    // 期望值容差：默认 1%。
    private static IReadOnlyList<CheckOutcome> FirstOrder06()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("L1", ElementType.Inductor, 100, "mH")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 1e3, l = 100e-3, vs = 1.0;
        double tau = l / r;
        double i3 = vs / r * (1.0 - Math.Exp(-3.0));
        double fc = r / (2.0 * Math.PI * l);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.FirstOrderRl, "RlTransient"),
            Checks.Number("τ", tau, StepValue(solution, "三要素", "τ = "), unit: "s"),
            Checks.Number("iL(3τ)", i3, StepValue(solution, "微分方程", "i_L(3τ) = "), unit: "A"),
            Checks.Number("fc", fc, StepValue(solution, "截止频率", "fc = "), unit: "Hz")
        };
    }

    // =====================================================================
    // Section S: 二阶 RLC
    // =====================================================================
    // 教材来源（S01-S06）：邱关源《电路》（第5版）第7章“二阶电路的时域分析”与
    // 第11章“谐振电路”——
    //   串联 RLC：ω0=1/√(LC)，α=R/(2L)，Q=(1/R)√(L/C)=ω0/(2α)，BW=f0/Q；
    //   并联 RLC：ω0=1/√(LC)，α=1/(2RC)，Q=R√(C/L)=ω0/(2α)，BW=f0/Q；
    //   阻尼判据：α>ω0 过阻尼、α=ω0 临界阻尼、α<ω0 欠阻尼。
    // 所有期望值由上述教科书关系直接计算。

    // S01 ------------------------------------------------------------------
    // 已知（串联）：R=50 Ω，L=10 mH，C=1 μF。
    // 手算：ω0 = 1/√(1e-2 × 1e-6) = 10000 rad/s；f0 = 10000/(2π) = 1591.549 Hz；
    //       α = R/(2L) = 50/0.02 = 2500 s^-1；Q = (1/R)√(L/C) = (1/50)√10000 = 2；
    //       BW = f0/Q = 795.775 Hz；α<ω0 → 欠阻尼。
    private static IReadOnlyList<CheckOutcome> SecondOrder01()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 50, "Ω")
            .Element("L1", ElementType.Inductor, 10, "mH")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 3)
            .Pin("C1", "1", 3).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 50.0, l = 10e-3, c = 1e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);
        double alpha = r / (2.0 * l);
        double q = Math.Sqrt(l / c) / r;
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "SeriesRlc"),
            Checks.Number("f0", f0, StepValue(solution, "固有谐振角频率", "f0 = "), unit: "Hz"),
            Checks.Number("ω0", omega0, Parsing.ExtractLatexBetween(solution.SummaryText, "ω0 = ", " rad/s"), unit: "rad/s"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "欠阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // S02 ------------------------------------------------------------------
    // 已知（串联）：R=500 Ω，L=10 mH，C=1 μF。
    // 手算：ω0 = 10000 rad/s；f0 = 1591.549 Hz；α = 500/0.02 = 25000 s^-1；
    //       Q = (1/500)√10000 = 0.2；BW = f0/Q = 7957.75 Hz；α>ω0 → 过阻尼。
    private static IReadOnlyList<CheckOutcome> SecondOrder02()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 500, "Ω")
            .Element("L1", ElementType.Inductor, 10, "mH")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 3)
            .Pin("C1", "1", 3).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 500.0, l = 10e-3, c = 1e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);
        double alpha = r / (2.0 * l);
        double q = Math.Sqrt(l / c) / r;
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "SeriesRlc"),
            Checks.Number("f0", f0, StepValue(solution, "固有谐振角频率", "f0 = "), unit: "Hz"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "过阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // S03 ------------------------------------------------------------------
    // 已知（串联）：R=200 Ω = 2√(L/C) = 2√(1e-2/1e-6)，L=10 mH，C=1 μF。
    // 手算：ω0 = 10000 rad/s；α = 200/0.02 = 10000 s^-1 = ω0 → 临界阻尼；
    //       Q = (1/200)√10000 = 0.5；f0 = 1591.549 Hz；BW = f0/Q = 3183.10 Hz。
    private static IReadOnlyList<CheckOutcome> SecondOrder03()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 200, "Ω")
            .Element("L1", ElementType.Inductor, 10, "mH")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 3)
            .Pin("C1", "1", 3).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 200.0, l = 10e-3, c = 1e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double alpha = r / (2.0 * l);
        double q = Math.Sqrt(l / c) / r;
        double f0 = omega0 / (2.0 * Math.PI);
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "SeriesRlc"),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "临界阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // S04 ------------------------------------------------------------------
    // 已知（并联，电流源激励）：R=1 kΩ，L=100 mH，C=10 μF。
    // 手算：ω0 = 1/√(0.1 × 1e-5) = 1000 rad/s；f0 = 159.155 Hz；
    //       α = 1/(2RC) = 1/(2 × 1e3 × 1e-5) = 50 s^-1；
    //       Q = R√(C/L) = 1000 × √(1e-4) = 10；BW = f0/Q = 15.9155 Hz；
    //       α<ω0 → 欠阻尼。
    private static IReadOnlyList<CheckOutcome> SecondOrder04()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("I1", ElementType.DcCurrentSource, 1, "mA")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("L1", ElementType.Inductor, 100, "mH")
            .Element("C1", ElementType.Capacitor, 10, "μF")
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 0)
            .Pin("L1", "1", 1).Pin("L1", "2", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 1e3, l = 100e-3, c = 10e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);
        double alpha = 1.0 / (2.0 * r * c);
        double q = r * Math.Sqrt(c / l);
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "ParallelRlc"),
            Checks.Number("ω0", omega0, Parsing.ExtractLatexBetween(solution.SummaryText, "ω0 = ", " rad/s"), unit: "rad/s"),
            Checks.Number("f0", f0, StepValue(solution, "固有谐振角频率", "f0 = "), unit: "Hz"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "欠阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // S05 ------------------------------------------------------------------
    // 已知（并联）：R=25 Ω，L=100 mH，C=10 μF。
    // 手算：ω0 = 1000 rad/s；α = 1/(2 × 25 × 1e-5) = 2000 s^-1 > ω0 → 过阻尼；
    //       Q = 25 × √(1e-4) = 0.25；f0 = 159.155 Hz；BW = f0/Q = 636.62 Hz。
    private static IReadOnlyList<CheckOutcome> SecondOrder05()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("I1", ElementType.DcCurrentSource, 1, "mA")
            .Element("R1", ElementType.Resistor, 25, "Ω")
            .Element("L1", ElementType.Inductor, 100, "mH")
            .Element("C1", ElementType.Capacitor, 10, "μF")
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 0)
            .Pin("L1", "1", 1).Pin("L1", "2", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 25.0, l = 100e-3, c = 10e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);
        double alpha = 1.0 / (2.0 * r * c);
        double q = r * Math.Sqrt(c / l);
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "ParallelRlc"),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "过阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // S06 ------------------------------------------------------------------
    // 已知（并联）：R=10 kΩ，L=1 mH，C=1 μF。
    // 手算：ω0 = 1/√(1e-3 × 1e-6) = 31622.8 rad/s；f0 = 5032.92 Hz；
    //       α = 1/(2 × 1e4 × 1e-6) = 50 s^-1；
    //       Q = R√(C/L) = 1e4 × √(1e-3) = 316.228；BW = f0/Q = 15.9155 Hz；
    //       α≪ω0 → 欠阻尼。
    private static IReadOnlyList<CheckOutcome> SecondOrder06()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("I1", ElementType.DcCurrentSource, 1, "mA")
            .Element("R1", ElementType.Resistor, 10, "kΩ")
            .Element("L1", ElementType.Inductor, 1, "mH")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 0)
            .Pin("L1", "1", 1).Pin("L1", "2", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        const double r = 10e3, l = 1e-3, c = 1e-6;
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);
        double alpha = 1.0 / (2.0 * r * c);
        double q = r * Math.Sqrt(c / l);
        double bw = f0 / q;

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.SecondOrderRlc, "ParallelRlc"),
            Checks.Number("f0", f0, StepValue(solution, "固有谐振角频率", "f0 = "), unit: "Hz"),
            Checks.Number("Q", q, StepValue(solution, "衰减常数", "Q = ")),
            Checks.Number("α", alpha, StepValue(solution, "衰减常数", "α = "), unit: "s⁻¹"),
            Checks.Number("BW", bw, StepValue(solution, "通频带", "BW = "), unit: "Hz"),
            Checks.TextContains("阻尼类型", "欠阻尼", StepResult(solution, "阻尼响应工作区"))
        };
    }

    // =====================================================================
    // Section B: 单管 NPN BJT 放大电路
    // =====================================================================
    // 统一模型假设（经典教材微变等效模型，等价于童诗白《模拟电子技术基础》
    // 第2章“基本放大电路”与康华光《电子技术基础（模拟部分）》的 h 参数模型）：
    //   VBE(on) = 0.7 V；热电压 UT = 26 mV；基区体电阻 rbb' = 200 Ω
    //   （共基极经典例题取 rbb' = 50 Ω）；ICQ = β·IBQ，IEQ = (1+β)IBQ；
    //   rbe = rbb' + (1+β)UT/IEQ；中频段耦合/旁路电容视作短路。
    // 期望值全部由这些教科书关系手工计算（见各题推导），不复制引擎实现。

    // B01 ------------------------------------------------------------------
    // 教材来源：童诗白《模拟电子技术基础》第2章 固定偏置共射放大电路。
    // 已知：VCC=12 V，Rb=400 kΩ，Rc=3 kΩ，β=100，发射极直接接地，无负载。
    // 手算：IBQ=(VCC-0.7)/Rb=11.3/400k=28.25 μA；ICQ=βIBQ=2.825 mA；
    //       IEQ=101×28.25 μA=2.85325 mA；
    //       VCEQ=VCC-ICQ·Rc=12-8.475=3.525 V（>0.2 V，放大区）；
    //       rbe=200+101×0.026/IEQ=200+920.36=1120.36 Ω；
    //       Av=-βRc/rbe=-100×3000/1120.36=-267.77；
    //       Ri=Rb∥rbe=400k∥1120.36=1117.2 Ω；Ro=Rc=3 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt01() =>
        BjtFixedBias(vcc: 12.0, rb: 400e3, rc: 3e3, beta: 100.0, expectedSubtype: "FixedBias");

    // B02 ------------------------------------------------------------------
    // 教材来源：同上，固定偏置第二组参数（检验参数敏感性）。
    // 已知：VCC=15 V，Rb=500 kΩ，Rc=5 kΩ，β=80，发射极接地，无负载。
    // 手算：IBQ=14.3/500k=28.6 μA；ICQ=2.288 mA；IEQ=81×28.6 μA=2.3166 mA；
    //       VCEQ=15-2.288m×5k=3.56 V；
    //       rbe=200+81×0.026/IEQ=200+909.1=1109.1 Ω；
    //       Av=-80×5000/1109.1=-360.66；Ri=500k∥1109.1=1106.6 Ω；Ro=5 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt02() =>
        BjtFixedBias(vcc: 15.0, rb: 500e3, rc: 5e3, beta: 80.0, expectedSubtype: "FixedBias");

    private static IReadOnlyList<CheckOutcome> BjtFixedBias(double vcc, double rb, double rc, double beta, string expectedSubtype)
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, vcc, "V")
            .Element("Rb", ElementType.Resistor, rb, "Ω")
            .Element("Rc", ElementType.Resistor, rc, "Ω")
            .Element("Q1", ElementType.BjtNpn, beta, "")
            .Net(2, "VCC", isPower: true)
            .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
            .Pin("Rb", "1", 2).Pin("Rb", "2", 1)
            .Pin("Rc", "1", 2).Pin("Rc", "2", 3)
            .Pin("Q1", "B", 1).Pin("Q1", "C", 3).Pin("Q1", "E", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        // 独立手算（教材公式，见题目注释）
        double ibq = (vcc - 0.7) / rb;
        double icq = beta * ibq;
        double ieq = (1.0 + beta) * ibq;
        double vceq = vcc - icq * rc;
        double rbe = 200.0 + (1.0 + beta) * 0.026 / ieq;
        double av = -beta * rc / rbe;
        double ri = rb * rbe / (rb + rbe);
        double ro = rc;

        return BjtChecks(classification, solution, expectedSubtype, ibq, icq, vceq, av, ri, ro);
    }

    // B03 ------------------------------------------------------------------
    // 教材来源：童诗白《模拟电子技术基础》第2章 分压式偏置共射放大电路（无旁路）。
    // 已知：VCC=12 V，Rb1=100 kΩ（B–VCC），Rb2=50 kΩ（B–GND），Rc=2.2 kΩ，
    //       Re=1 kΩ，β=100，无发射极旁路电容，无负载。
    // 教材估算法（用户口径 2026-10-18，中间量按显示单位两位小数舍入后回代）：
    //       U_B=VCC·Rb2/(Rb1+Rb2)=4 V（空载分压，忽略基极电流加载）；
    //       I_EQ=(U_B-0.7)/Re=3.3 mA；I_BQ=I_EQ/(1+β)=32.67 μA；
    //       I_CQ=β·I_BQ=3.27 mA；U_CEQ=VCC-I_CQ·Rc-I_EQ·Re=1.51 V（>0.2 V，放大区）；
    //       rbe=200+(1+β)×0.026/I_EQ=995.76 Ω；输入支路 rbe+(1+β)Re=102 kΩ；
    //       Av=-βRc/[rbe+(1+β)Re]=-2.16；Ri=Rb1∥Rb2∥[rbe+(1+β)Re]=25.12 kΩ；Ro=Rc=2.2 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt03() =>
        BjtVoltageDivider(vcc: 12.0, rb1: 100e3, rb2: 50e3, rc: 2.2e3, re: 1e3, beta: 100.0,
            bypass: false, expectedSubtype: "VoltageDividerSelfBias");

    // B04 ------------------------------------------------------------------
    // 教材来源：同上，带发射极旁路电容（Ce 使交流 Re 被短路，Av 提高）。
    // 已知：VCC=12 V，Rb1=120 kΩ，Rb2=40 kΩ，Rc=3 kΩ，Re=1.5 kΩ，Ce=10 μF，β=100。
    // 教材估算法：U_B=12×40/160=3 V；I_EQ=(3-0.7)/1.5k=1.53 mA；
    //       I_BQ=I_EQ/101=15.15 μA；I_CQ=β·I_BQ=1.52 mA；U_CEQ=12-1.52m×3k-1.53m×1.5k=5.15 V；
    //       rbe=200+101×0.026/I_EQ=1.92 kΩ；Ce 旁路后 Av=-βRc/rbe=-156.25；
    //       Ri=Rb1∥Rb2∥rbe=30k∥1.92k=1.80 kΩ；Ro=Rc=3 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt04() =>
        BjtVoltageDivider(vcc: 12.0, rb1: 120e3, rb2: 40e3, rc: 3e3, re: 1.5e3, beta: 100.0,
            bypass: true, expectedSubtype: "VoltageDividerSelfBias");

    private static IReadOnlyList<CheckOutcome> BjtVoltageDivider(
        double vcc, double rb1, double rb2, double rc, double re, double beta, bool bypass, string expectedSubtype)
    {
        var builder = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, vcc, "V")
            .Element("Rb1", ElementType.Resistor, rb1, "Ω")
            .Element("Rb2", ElementType.Resistor, rb2, "Ω")
            .Element("Rc", ElementType.Resistor, rc, "Ω")
            .Element("Re", ElementType.Resistor, re, "Ω")
            .Element("Q1", ElementType.BjtNpn, beta, "")
            .Net(2, "VCC", isPower: true)
            .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
            .Pin("Rb1", "1", 2).Pin("Rb1", "2", 1)
            .Pin("Rb2", "1", 1).Pin("Rb2", "2", 0)
            .Pin("Rc", "1", 2).Pin("Rc", "2", 3)
            .Pin("Re", "1", 4).Pin("Re", "2", 0)
            .Pin("Q1", "B", 1).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        if (bypass)
        {
            builder.Element("Ce", ElementType.Capacitor, 10, "μF")
                   .Pin("Ce", "1", 4).Pin("Ce", "2", 0);
        }
        ElectricalNetlist netlist = builder.Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        // 教材估算法手算（见题目注释）：每个中间量按显示单位两位小数舍入后回代，
        // 与显示约定（Services/TeachingDisplay.cs）一致；期望值不复制引擎实现。
        double vth = vcc * rb2 / (rb1 + rb2);
        double ub = TeachingDisplay.RoundTo(vth, QuantityKind.Voltage);
        double ieq = TeachingDisplay.RoundTo((ub - 0.7) / re, QuantityKind.Current);
        double ibq = TeachingDisplay.RoundTo(ieq / (1.0 + beta), QuantityKind.Current);
        double icq = TeachingDisplay.RoundTo(beta * ibq, QuantityKind.Current);
        double vceq = TeachingDisplay.RoundTo(vcc - icq * rc - ieq * re, QuantityKind.Voltage);
        double rbe = TeachingDisplay.RoundTo(200.0 + (1.0 + beta) * 0.026 / ieq, QuantityKind.Resistance);
        double rbAc = TeachingDisplay.RoundTo(rb1 * rb2 / (rb1 + rb2), QuantityKind.Resistance);
        double rlPrime = TeachingDisplay.RoundTo(rc, QuantityKind.Resistance); // 无负载 RL=∞ → Rc
        double inputDeviceResistance = bypass
            ? rbe
            : TeachingDisplay.RoundTo(rbe + (1.0 + beta) * re, QuantityKind.Resistance);
        double av = TeachingDisplay.RoundPlain(-beta * rlPrime / inputDeviceResistance);
        double ri = TeachingDisplay.RoundTo(rbAc * inputDeviceResistance / (rbAc + inputDeviceResistance),
            QuantityKind.Resistance);
        double ro = TeachingDisplay.RoundTo(rc, QuantityKind.Resistance);

        return BjtChecks(classification, solution, expectedSubtype, ibq, icq, vceq, av, ri, ro);
    }

    // B05 ------------------------------------------------------------------
    // 教材来源：童诗白《模拟电子技术基础》第2章 / 康华光《电子技术基础（模拟部分）》
    //           “集电极-基极电阻反馈偏置放大电路”（电压并联负反馈）。
    // 已知：VCC=12 V，Rb=180 kΩ（接在 B 与 C 之间），Rc=2.4 kΩ，β=100，发射极接地，无负载。
    // 手算（基极回路 KVL：VCC-(ICQ+IBQ)Rc-IBQ·Rb-0.7=0）：
    //       IBQ=(VCC-0.7)/[Rb+(1+β)Rc]=11.3/(180k+242.4k)=26.7519 μA；
    //       ICQ=βIBQ=2.67519 mA；IEQ=101IBQ=2.70194 mA；
    //       VCEQ=VCC-(ICQ+IBQ)Rc=5.51534 V；
    //       rbe=200+101×0.026/IEQ=1171.9 Ω；
    //       交流节点法：Av=(1/Rb-β/rbe)/(1/Rc+1/Rb)=-202.1（无负载 RL=∞）；
    //       Ri=1/[1/rbe+(1-Av)/Rb]=504.6 Ω；Ro=Rc∥Rb=2.368 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt05() =>
        BjtCollectorFeedback(vcc: 12.0, rb: 180e3, rc: 2.4e3, beta: 100.0, rl: 0.0, expectedSubtype: "CollectorFeedbackBias");

    // B06 ------------------------------------------------------------------
    // 教材来源：同上，集电极反馈偏置带交流负载 RL（经输出耦合电容接负载）。
    // 已知：VCC=12 V，Rb=150 kΩ，Rc=3 kΩ，RL=3 kΩ，β=120，发射极接地。
    // 手算：IBQ=11.3/(150k+121×3k)=11.3/513k=22.0273 μA；
    //       ICQ=2.64328 mA；IEQ=2.66530 mA；VCEQ=12-(ICQ+IBQ)Rc=4.00407 V；
    //       rbe=200+121×0.026/IEQ=1380.4 Ω；
    //       Av=(1/Rb-β/rbe)/(1/Rc+1/RL+1/Rb)=-129.10；
    //       Ri=1/[1/rbe+(1-Av)/Rb]=628.2 Ω；Ro=Rc∥Rb=2.941 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt06() =>
        BjtCollectorFeedback(vcc: 12.0, rb: 150e3, rc: 3e3, beta: 120.0, rl: 3e3, expectedSubtype: "CollectorFeedbackBias");

    private static IReadOnlyList<CheckOutcome> BjtCollectorFeedback(
        double vcc, double rb, double rc, double beta, double rl, string expectedSubtype)
    {
        var builder = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, vcc, "V")
            .Element("Rb", ElementType.Resistor, rb, "Ω")
            .Element("Rc", ElementType.Resistor, rc, "Ω")
            .Element("Q1", ElementType.BjtNpn, beta, "")
            .Net(2, "VCC", isPower: true)
            .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
            .Pin("Rb", "1", 1).Pin("Rb", "2", 3)
            .Pin("Rc", "1", 2).Pin("Rc", "2", 3)
            .Pin("Q1", "B", 1).Pin("Q1", "C", 3).Pin("Q1", "E", 0);
        if (rl > 0)
        {
            builder.Element("Cout", ElementType.Capacitor, 10, "μF")
                   .Element("RL", ElementType.Resistor, rl, "Ω")
                   .Pin("Cout", "1", 3).Pin("Cout", "2", 5)
                   .Pin("RL", "1", 5).Pin("RL", "2", 0);
        }
        ElectricalNetlist netlist = builder.Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        // 独立手算（教材集电极反馈偏置公式，见题目注释）
        double ibq = (vcc - 0.7) / (rb + (1.0 + beta) * rc);
        double icq = beta * ibq;
        double ieq = (1.0 + beta) * ibq;
        double vceq = vcc - (icq + ibq) * rc;
        double rbe = 200.0 + (1.0 + beta) * 0.026 / ieq;
        double inverseLoad = rl > 0 ? 1.0 / rl : 0.0;
        double av = (1.0 / rb - beta / rbe) / (1.0 / rc + inverseLoad + 1.0 / rb);
        double ri = 1.0 / (1.0 / rbe + (1.0 - av) / rb);
        double ro = rc * rb / (rc + rb);

        return BjtChecks(classification, solution, expectedSubtype, ibq, icq, vceq, av, ri, ro);
    }

    // B07 ------------------------------------------------------------------
    // 教材来源：童诗白《模拟电子技术基础》第2章 共基极放大电路；经典 h 参数
    //           共基模型 hie=rbb'+(1+β)UT/IEQ（此处 rbb'=50 Ω），hib=hie/(1+β)，
    //           Au=β(Rc∥RL)/hie，ri'=Re∥hib，ro'=Rc。
    // 已知：VCC=12 V，Rb1=100 kΩ，Rb2=50 kΩ（基极分压），Cb=10 μF 基极旁路，
    //       Rc=2 kΩ，Re=2 kΩ，β=100，无负载。
    // 教材估算法（用户口径 2026-10-18，中间量按显示单位两位小数舍入后回代）：
    //       U_B=4 V；I_EQ=(U_B-0.7)/Re=1.65 mA；I_BQ=I_EQ/(1+β)=16.34 μA；
    //       I_CQ=β·I_BQ=1.63 mA；U_CEQ=12-1.63m×2k-1.65m×2k=5.44 V（>0.2 V，放大区）；
    //       hie=50+101×0.026/I_EQ=1.64 kΩ；hib=hie/(1+β)=16.24 Ω；
    //       Au=β·Rc/hie=121.95；Ri'=Re∥hib=16.11 Ω；Ro'=Rc=2 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt07() =>
        BjtCommonBase(vcc: 12.0, rb1: 100e3, rb2: 50e3, rc: 2e3, re: 2e3, beta: 100.0);

    // B08 ------------------------------------------------------------------
    // 教材来源：同上，共基极第二组参数（检验不同 VCC/β/分压比下的 h 参数模型）。
    // 教材取值：VCC=15 V，Rb1=120 kΩ，Rb2=60 kΩ，Cb=10 μF，Rc=3 kΩ，β=80。
    // 参数调整说明（2026-10-18）：教材原取值 Re=1 kΩ 时，按“空载分压忽略基极电流
    // 加载”的教材估算法 I_EQ=(5-0.7)/1k=4.3 mA、U_CEQ≈-2.05 V，工作点已进饱和区，
    // 策略会按放大区假设明确拒绝（该负向行为由 tests/NonlinearCircuitChecks 的
    // BjtCcChecks/BjtDcAcPhaseChecks 覆盖）。本 benchmark 只承担“30 题成功求解 +
    // 数值对拍”，故将本题 Re 取 2 kΩ，使教材估算法下工作点仍处放大区：
    //       U_B=15×60/180=5 V；I_EQ=(5-0.7)/2k=2.15 mA；I_BQ=2.15m/81=26.54 μA；
    //       I_CQ=80×26.54μ=2.12 mA；U_CEQ=15-2.12m×3k-2.15m×2k=4.34 V（>0.2 V，放大区）；
    //       hie=50+81×0.026/I_EQ=1.03 kΩ；hib=hie/(1+β)=12.72 Ω；
    //       Au=β·Rc/hie=233.01；Ri'=Re∥hib=12.64 Ω；Ro'=Rc=3 kΩ。
    private static IReadOnlyList<CheckOutcome> Bjt08() =>
        BjtCommonBase(vcc: 15.0, rb1: 120e3, rb2: 60e3, rc: 3e3, re: 2e3, beta: 80.0);

    private static IReadOnlyList<CheckOutcome> BjtCommonBase(
        double vcc, double rb1, double rb2, double rc, double re, double beta)
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, vcc, "V")
            .Element("Rb1", ElementType.Resistor, rb1, "Ω")
            .Element("Rb2", ElementType.Resistor, rb2, "Ω")
            .Element("Rc", ElementType.Resistor, rc, "Ω")
            .Element("Re", ElementType.Resistor, re, "Ω")
            .Element("Cb", ElementType.Capacitor, 10, "μF")
            .Element("Q1", ElementType.BjtNpn, beta, "")
            .Net(2, "VCC", isPower: true)
            .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
            .Pin("Rb1", "1", 2).Pin("Rb1", "2", 1)
            .Pin("Rb2", "1", 1).Pin("Rb2", "2", 0)
            .Pin("Rc", "1", 2).Pin("Rc", "2", 3)
            .Pin("Re", "1", 4).Pin("Re", "2", 0)
            .Pin("Cb", "1", 1).Pin("Cb", "2", 0)
            .Pin("Q1", "B", 1).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);

        // 教材估算法手算（见题目注释）：每个中间量按显示单位两位小数舍入后回代。
        double vth = vcc * rb2 / (rb1 + rb2);
        double ub = TeachingDisplay.RoundTo(vth, QuantityKind.Voltage);
        double ieq = TeachingDisplay.RoundTo((ub - 0.7) / re, QuantityKind.Current);
        double ibq = TeachingDisplay.RoundTo(ieq / (1.0 + beta), QuantityKind.Current);
        double icq = TeachingDisplay.RoundTo(beta * ibq, QuantityKind.Current);
        double vceq = TeachingDisplay.RoundTo(vcc - icq * rc - ieq * re, QuantityKind.Voltage);
        double hie = TeachingDisplay.RoundTo(50.0 + (1.0 + beta) * 0.026 / ieq, QuantityKind.Resistance);
        double hib = TeachingDisplay.RoundTo(hie / (1.0 + beta), QuantityKind.Resistance);
        double rlPrime = TeachingDisplay.RoundTo(rc, QuantityKind.Resistance); // 无负载 RL=∞ → Rc
        double au = TeachingDisplay.RoundPlain(beta * rlPrime / hie);
        double ri = TeachingDisplay.RoundTo(re * hib / (re + hib), QuantityKind.Resistance);
        double ro = TeachingDisplay.RoundTo(rc, QuantityKind.Resistance);

        return BjtChecks(classification, solution, "CommonBase", ibq, icq, vceq, au, ri, ro,
            gainLabel: "Au", gainIsPositive: true);
    }

    private static IReadOnlyList<CheckOutcome> BjtChecks(
        ClassificationResult classification, PedagogicalSolution solution, string expectedSubtype,
        double ibq, double icq, double vceq, double av, double ri, double ro,
        string gainLabel = "Av", bool gainIsPositive = false)
    {
        AcParametersInfo ac = solution.AcParameters ?? throw new InvalidOperationException("BJT 求解未返回交流参数。");
        QPointInfo q = solution.QPoint ?? throw new InvalidOperationException("BJT 求解未返回 Q 点。");

        var checks = new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.BjtAmplifier, expectedSubtype),
            Checks.Number("IBQ", ibq, Parsing.ParseQuantity(q.Ibq), unit: "A", note: "显示字符串按 μA 解析"),
            Checks.Number("ICQ", icq, Parsing.ParseQuantity(q.Icq), unit: "A", note: "显示字符串按 mA 解析"),
            Checks.Number("VCEQ", vceq, Parsing.ParseQuantity(q.Vceq), unit: "V"),
            Checks.Number(gainLabel, av, Parsing.ParseQuantity(ac.VoltageGain), note: "中频电压增益"),
            Checks.Number("Ri", ri, Parsing.ParseQuantity(ac.InputResistance), unit: "Ω"),
            Checks.Number("Ro", ro, Parsing.ParseQuantity(ac.OutputResistance), unit: "Ω"),
            Checks.TextContains("工作区", "放大区", q.WorkingRegion)
        };
        if (gainIsPositive)
        {
            checks.Add(Checks.Flag("Au>0（共基同相）", true, Parsing.ParseQuantity(ac.VoltageGain) > 0));
        }
        return checks;
    }

    // =====================================================================
    // Section M: 通用线性网络 MNA 2.0
    // =====================================================================

    // M01 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章“电阻电路的一般分析”——节点电压法/KCL。
    // 已知：V1=10 V（节点 N2 对地），R0=1 kΩ（N2→N1），I1=4 mA（注入 N1），
    //       R1=1 kΩ（N1→地）。
    // 手算：N2=V1=10 V；对 N1 列 KCL：(V1-10)/1k + V1/1k = 4 mA
    //       → 2V1 - 10 = 4 → V1 = 7 V。矩阵阶数 = 2 个节点 + 1 个电压源支路电流 = 3。
    private static IReadOnlyList<CheckOutcome> Mna01()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 10, "V")
            .Element("R0", ElementType.Resistor, 1, "kΩ")
            .Element("I1", ElementType.DcCurrentSource, 4, "mA")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
            .Pin("R0", "1", 2).Pin("R0", "2", 1)
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);
        LinearNetworkInfo network = solution.LinearNetwork ?? throw new InvalidOperationException("MnaLinearStrategy 未返回结构化 LinearNetwork。");

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Number("N2", 10.0, Pipeline.Node(network, "N2").VoltageV, unit: "V"),
            Checks.Number("N1", 7.0, Pipeline.Node(network, "N1").VoltageV, unit: "V"),
            Checks.Flag("矩阵阶数=3", true, network.MatrixDimension == 3),
            Checks.Flag("非地节点电位=2", true, network.NodePotentials.Count == 2)
        };
    }

    // M02 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第4章“电路定理”——戴维南定理。
    // 已知：V1=12 V，R1=2 kΩ（N1→N2），R2=3 kΩ（N2→地）。
    // 手算：N1=12 V；N2=12×R2/(R1+R2)=12×3/5=7.2 V；
    //       Voc(N2–GND)=7.2 V；Req=R1∥R2=2k∥3k=1.2 kΩ；矩阵阶数=3。
    private static IReadOnlyList<CheckOutcome> Mna02()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 12, "V")
            .Element("R1", ElementType.Resistor, 2, "kΩ")
            .Element("R2", ElementType.Resistor, 3, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("R2", "1", 2).Pin("R2", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);
        LinearNetworkInfo network = solution.LinearNetwork ?? throw new InvalidOperationException("MnaLinearStrategy 未返回结构化 LinearNetwork。");
        PortEquivalentInfo port = Pipeline.Port(network, "N2", "GND");

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Number("N1", 12.0, Pipeline.Node(network, "N1").VoltageV, unit: "V"),
            Checks.Number("N2", 7.2, Pipeline.Node(network, "N2").VoltageV, unit: "V"),
            Checks.Number("Voc", 7.2, port.OpenCircuitVoltageV, unit: "V"),
            Checks.Number("Req", 1200.0, port.ResistanceOhm, unit: "Ω"),
            Checks.Flag("矩阵阶数=3", true, network.MatrixDimension == 3)
        };
    }

    // M03 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章节点法 + 第4章等效电阻（电桥）。
    // 已知：V1=12 V 接 N1–地；桥臂 R1=1 kΩ(N1–N2)、R2=2 kΩ(N1–N3)、
    //       R3=3 kΩ(N2–地)、R4=2 kΩ(N3–地)，检流计支路 R5=1 kΩ(N2–N3)。
    // 手算（节点法，N1=12 V）：
    //   N2: (V2-12)/1k + V2/3k + (V2-V3)/1k = 0 → 7V2 - 3V3 = 36
    //   N3: (V3-12)/2k + V3/2k + (V3-V2)/1k = 0 → -V2 + 2V3 = 6
    //   → V2 = 90/11 = 8.18182 V，V3 = 78/11 = 7.09091 V；
    //   Voc(N2–N3) = V2 - V3 = 12/11 = 1.09091 V；
    //   Req(N2–N3)（电压源置零） = R5 ∥ [(R1∥R3)+(R2∥R4)]
    //                             = 1k ∥ (0.75k+1k) = 0.636364 kΩ = 7000/11 Ω。
    private static IReadOnlyList<CheckOutcome> Mna03()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 12, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("R2", ElementType.Resistor, 2, "kΩ")
            .Element("R3", ElementType.Resistor, 3, "kΩ")
            .Element("R4", ElementType.Resistor, 2, "kΩ")
            .Element("R5", ElementType.Resistor, 1, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("R2", "1", 1).Pin("R2", "2", 3)
            .Pin("R3", "1", 2).Pin("R3", "2", 0)
            .Pin("R4", "1", 3).Pin("R4", "2", 0)
            .Pin("R5", "1", 2).Pin("R5", "2", 3)
            .Build();

        LinearNetworkAnalyzer model = Pipeline.CreateModel(netlist);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        LinearSolution dc = Pipeline.SolveDc(model);
        if (!model.TryGetThevenin(2, 3, out double voc, out double req, out string error))
        {
            throw new InvalidOperationException("MNA 戴维南等效求解失败：" + error);
        }

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=4", true, model.MatrixDimension == 4),
            Checks.Number("V(N2)", 90.0 / 11.0, dc.NodeVoltage(2), unit: "V"),
            Checks.Number("V(N3)", 78.0 / 11.0, dc.NodeVoltage(3), unit: "V"),
            Checks.Number("Voc(N2-N3)", 12.0 / 11.0, voc, unit: "V"),
            Checks.Number("Req(N2-N3)", 7000.0 / 11.0, req, unit: "Ω")
        };
    }

    // M04 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章含受控源电路节点法；VCVS 定义 V_OUT=μ·V_C。
    // 已知：V1=1 V 直流（控制电压 Vc=V(N1)），E1 为 VCVS，μ=2.5，
    //       输出端 OUT1=N2、OUT2=地，负载 RL=2 kΩ。
    // 手算：V(N2)=μ·V(N1)=2.5×1=2.5 V；
    //       受控源输出支路电流（OUT1→OUT2 为正）= -V(N2)/RL = -1.25 mA（向负载供电）；
    //       矩阵阶数 = 2 节点 + V1 支路 + E1 支路 = 4。
    private static IReadOnlyList<CheckOutcome> Mna04()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 1, "V")
            .Element("E1", ElementType.Vcvs, 2.5, "倍")
            .Element("RL", ElementType.Resistor, 2, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("E1", "IN1", 1).Pin("E1", "IN2", 0)
            .Pin("E1", "OUT1", 2).Pin("E1", "OUT2", 0)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

        LinearNetworkAnalyzer model = Pipeline.CreateModel(netlist);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        LinearSolution dc = Pipeline.SolveDc(model);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=4", true, model.MatrixDimension == 4),
            Checks.Number("V(N1)", 1.0, dc.NodeVoltage(1), unit: "V"),
            Checks.Number("V(N2)=μVc", 2.5, dc.NodeVoltage(2), unit: "V", note: "μ=2.5, Vc=1 V"),
            Checks.Number("I(E1)", -1.25e-3, dc.BranchCurrentAmpere("E1"), unit: "A", note: "OUT1→OUT2 为正")
        };
    }

    // M05 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章含受控源电路；VCCS 定义 I_OUT=gm·V_C。
    // 已知：V1=2 V 直流（控制电压 Vc=V(N1)），G1 为 VCCS，gm=1.5 mS，
    //       输出端 OUT1=地、OUT2=N2（电流由地注入 N2），负载 RL=2 kΩ。
    // 手算：I_OUT=gm·Vc=1.5m×2=3 mA 注入 N2 → V(N2)=3m×2k=6 V；矩阵阶数=3。
    private static IReadOnlyList<CheckOutcome> Mna05()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 2, "V")
            .Element("G1", ElementType.Vccs, 1.5, "mS")
            .Element("RL", ElementType.Resistor, 2, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("G1", "IN1", 1).Pin("G1", "IN2", 0)
            .Pin("G1", "OUT1", 0).Pin("G1", "OUT2", 2)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

        LinearNetworkAnalyzer model = Pipeline.CreateModel(netlist);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        LinearSolution dc = Pipeline.SolveDc(model);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=3", true, model.MatrixDimension == 3),
            Checks.Number("V(N1)", 2.0, dc.NodeVoltage(1), unit: "V"),
            Checks.Number("V(N2)", 6.0, dc.NodeVoltage(2), unit: "V", note: "gm·Vc·RL=1.5m×2×2k")
        };
    }

    // M06 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章；CCVS 定义 V_OUT=r·I_C（控制电流由
    //           控制支路 IN1→IN2 的 0 V 支路测定）。
    // 已知：I1=1.5 mA 注入 N1（in=N1、out=地）；H1 为 CCVS，r=2 kΩ，
    //       控制端 IN1=N1、IN2=地，输出端 OUT1=N2、OUT2=地，负载 RL=2 kΩ。
    // 手算：全部注入电流 1.5 mA 只能经 H1 控制支路流回地 → I_C=+1.5 mA；
    //       V(N2)=r·I_C=2k×1.5m=3 V；节点 N1 被 0 V 控制支路钳位到 0 V；
    //       矩阵阶数 = 2 节点 + CCVS 输出支路 + 控制支路 = 4。
    private static IReadOnlyList<CheckOutcome> Mna06()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("I1", ElementType.DcCurrentSource, 1.5, "mA")
            .Element("H1", ElementType.Ccvs, 2, "kΩ")
            .Element("RL", ElementType.Resistor, 2, "kΩ")
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("H1", "IN1", 1).Pin("H1", "IN2", 0)
            .Pin("H1", "OUT1", 2).Pin("H1", "OUT2", 0)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

        LinearNetworkAnalyzer model = Pipeline.CreateModel(netlist);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        LinearSolution dc = Pipeline.SolveDc(model);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=4", true, model.MatrixDimension == 4),
            Checks.NearZero("V(N1)", dc.NodeVoltage(1), 1e-9, unit: "V", note: "控制支路为 0 V 支路"),
            Checks.Number("V(N2)=r·I_C", 3.0, dc.NodeVoltage(2), unit: "V", note: "r=2kΩ, I_C=1.5mA")
        };
    }

    // M07 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3章；CCCS 定义 I_OUT=β·I_C。
    // 已知：I1=0.5 mA 注入 N1；F1 为 CCCS，β=8，控制端 IN1=N1、IN2=地，
    //       输出端 OUT1=地、OUT2=N2（电流由地注入 N2），负载 RL=2 kΩ。
    // 手算：I_C=0.5 mA → I_OUT=8×0.5m=4 mA 注入 N2 → V(N2)=4m×2k=8 V；
    //       节点 N1 被 0 V 控制支路钳位到 0 V；矩阵阶数=3。
    private static IReadOnlyList<CheckOutcome> Mna07()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("I1", ElementType.DcCurrentSource, 0.5, "mA")
            .Element("F1", ElementType.Cccs, 8, "倍")
            .Element("RL", ElementType.Resistor, 2, "kΩ")
            .Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("F1", "IN1", 1).Pin("F1", "IN2", 0)
            .Pin("F1", "OUT1", 0).Pin("F1", "OUT2", 2)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

        LinearNetworkAnalyzer model = Pipeline.CreateModel(netlist);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        LinearSolution dc = Pipeline.SolveDc(model);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=3", true, model.MatrixDimension == 3),
            Checks.NearZero("V(N1)", dc.NodeVoltage(1), 1e-9, unit: "V", note: "控制支路为 0 V 支路"),
            Checks.Number("V(N2)=β·I_C·RL", 8.0, dc.NodeVoltage(2), unit: "V", note: "β=8, I_C=0.5mA")
        };
    }

    // M08 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第3/7章——直流稳态下电容开路、电感短路。
    // 已知：(a) V1=10 V、C=1 μF 串到 N2、R=1 kΩ 由 N2 接地 → 电容直流开路，
    //           N2 无直流电流，V(N2)=0；
    //       (b) V1=10 V、L=10 mH 串到 N2、R=1 kΩ 由 N2 接地 → 电感直流短路，
    //           V(N2)=10 V，I_L=V(N2)/R=10 mA。
    private static IReadOnlyList<CheckOutcome> Mna08()
    {
        ElectricalNetlist capacitorNetlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 10, "V")
            .Element("C1", ElementType.Capacitor, 1, "μF")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 2)
            .Pin("R1", "1", 2).Pin("R1", "2", 0)
            .Build();
        LinearSolution capacitorDc = Pipeline.SolveDc(Pipeline.CreateModel(capacitorNetlist));

        ElectricalNetlist inductorNetlist = new NetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 10, "V")
            .Element("L1", ElementType.Inductor, 10, "mH")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("L1", "1", 1).Pin("L1", "2", 2)
            .Pin("R1", "1", 2).Pin("R1", "2", 0)
            .Build();
        LinearNetworkAnalyzer inductorModel = Pipeline.CreateModel(inductorNetlist);
        LinearSolution inductorDc = Pipeline.SolveDc(inductorModel);

        return new List<CheckOutcome>
        {
            Checks.Number("V(N1) 电容网络", 10.0, capacitorDc.NodeVoltage(1), unit: "V"),
            Checks.NearZero("V(N2) 电容开路", capacitorDc.NodeVoltage(2), 1e-12, unit: "V"),
            Checks.Flag("电感网络阶数=4", true, inductorModel.MatrixDimension == 4),
            Checks.Number("V(N2) 电感短路", 10.0, inductorDc.NodeVoltage(2), unit: "V"),
            Checks.Number("I(L1)", 0.01, inductorDc.BranchCurrentAmpere("L1"), unit: "A")
        };
    }

    // M09 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第8/9/11章——正弦稳态相量与 RC 低通频响。
    // 已知：交流源 V1=1 V（f=1 kHz，作为 MNA 工作频率），R=1 kΩ 串在 N1–N2 之间，
    //       两个 100 nF 电容并联在 N2–地（等效 C=200 nF）。
    // 手算：ω=2πf=6283.185 rad/s，ωRC=2π×1000×1000×200e-9=1.256637；
    //       |H|=1/√(1+(ωRC)²)=1/√(1+1.579137)=0.622681；
    //       ∠H=-arctan(ωRC)=-51.4882°；V(N2)=|H|×1 V=0.622681 V；
    //       截止频率 fc=1/(2πRC)=1/(2π×1000×200e-9)=795.775 Hz（单极点低通）；
    //       扫频 10 Hz–10 MHz、10 点/十倍频程 → 61 点；低频通带延伸到 DC，BW=fH。
    // 期望值容差：|H|/相位/节点电位 1%；扫频 -3 dB 截止为 2%（插值估计）。
    private static IReadOnlyList<CheckOutcome> Mna09()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("C1", ElementType.Capacitor, 100, "nF")
            .Element("C2", ElementType.Capacitor, 100, "nF")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("C1", "1", 2).Pin("C1", "2", 0)
            .Pin("C2", "1", 2).Pin("C2", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);
        LinearNetworkInfo network = solution.LinearNetwork ?? throw new InvalidOperationException("MnaLinearStrategy 未返回结构化 LinearNetwork。");
        FrequencyResponseInfo response = network.FrequencyResponse ?? throw new InvalidOperationException("交流 MNA 网络未返回 FrequencyResponse。");

        const double r = 1e3, cEquivalent = 200e-9, frequency = 1000.0;
        double omegaRC = 2.0 * Math.PI * frequency * r * cEquivalent;
        double gain = 1.0 / Math.Sqrt(1.0 + omegaRC * omegaRC);
        double phaseDeg = -Math.Atan(omegaRC) * 180.0 / Math.PI;
        double fc = 1.0 / (2.0 * Math.PI * r * cEquivalent);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=3", true, network.MatrixDimension == 3),
            Checks.Number("|H|", gain, response.GainRatio, unit: "V/V", note: "工作频率 1 kHz"),
            Checks.Number("∠H", phaseDeg, response.PhaseDeg, unit: "°", note: "-arctan(ωRC)"),
            Checks.Number("|V(N2)|", gain, Pipeline.Node(network, "N2").VoltageV, unit: "V"),
            Checks.Number("fH(-3dB)", fc, response.UpperCutoffHz ?? double.NaN, relativeTolerance: 0.02, unit: "Hz"),
            Checks.Number("BW", fc, response.BandwidthHz ?? double.NaN, relativeTolerance: 0.02, unit: "Hz"),
            Checks.Flag("扫频点数=61", true, response.Points.Count == 61)
        };
    }

    // M10 ------------------------------------------------------------------
    // 教材来源：邱关源《电路》（第5版）第9/11章——RL 高通频响与 -3 dB 带宽估计。
    // 已知：交流源 V1=1 V（f=1 kHz），R=1 kΩ 串在 N1–N2 之间，两个 10 mH 电感
    //       并联在 N2–地（等效 Leq=5 mH）。
    // 手算：ω=2π×1000；ωLeq=31.4159 Ω；
    //       |H|=ωLeq/√(R²+(ωLeq)²)=31.4159/√(1e6+987.0)=0.0314004；
    //       ∠H=90°-arctan(ωLeq/R)=88.2006°；V(N2)=0.0314004 V；
    //       高通截止频率 fc=R/(2πLeq)=1000/(2π×0.005)=31831.0 Hz；
    //       扫频 10 Hz–10 MHz、10 点/十倍频程 → 61 点；高频通带无上限 → fH/BW 均为空。
    // 期望值容差：|H|/相位/节点电位 1%；扫频 -3 dB 截止为 2%（插值估计）。
    private static IReadOnlyList<CheckOutcome> Mna10()
    {
        ElectricalNetlist netlist = new NetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 1, "V")
            .Element("R1", ElementType.Resistor, 1, "kΩ")
            .Element("L1", ElementType.Inductor, 10, "mH")
            .Element("L2", ElementType.Inductor, 10, "mH")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("L1", "1", 2).Pin("L1", "2", 0)
            .Pin("L2", "1", 2).Pin("L2", "2", 0)
            .Build();

        (ClassificationResult classification, PedagogicalSolution solution) = Pipeline.SolveSucceeded(netlist);
        LinearNetworkInfo network = solution.LinearNetwork ?? throw new InvalidOperationException("MnaLinearStrategy 未返回结构化 LinearNetwork。");
        FrequencyResponseInfo response = network.FrequencyResponse ?? throw new InvalidOperationException("交流 MNA 网络未返回 FrequencyResponse。");

        const double r = 1e3, lEquivalent = 5e-3, frequency = 1000.0;
        double omegaL = 2.0 * Math.PI * frequency * lEquivalent;
        double gain = omegaL / Math.Sqrt(r * r + omegaL * omegaL);
        double phaseDeg = 90.0 - Math.Atan(omegaL / r) * 180.0 / Math.PI;
        double fc = r / (2.0 * Math.PI * lEquivalent);

        return new List<CheckOutcome>
        {
            Pipeline.Topology(classification, CircuitTopologyType.MnaLinear, "GeneralLinear"),
            Checks.Flag("矩阵阶数=5", true, network.MatrixDimension == 5),
            Checks.Number("|H|", gain, response.GainRatio, unit: "V/V", note: "工作频率 1 kHz"),
            Checks.Number("∠H", phaseDeg, response.PhaseDeg, unit: "°", note: "90°-arctan(ωL/R)"),
            Checks.Number("|V(N2)|", gain, Pipeline.Node(network, "N2").VoltageV, unit: "V"),
            Checks.Number("fL(-3dB)", fc, response.LowerCutoffHz ?? double.NaN, relativeTolerance: 0.02, unit: "Hz"),
            Checks.Flag("无上限截止 fH", false, response.UpperCutoffHz.HasValue),
            Checks.Flag("无有限带宽 BW", false, response.BandwidthHz.HasValue),
            Checks.Flag("扫频点数=61", true, response.Points.Count == 61)
        };
    }
}

// ============================================================================
// Netlist fixture builder.
//
// The benchmark defines circuits by explicit pin→net bindings instead of canvas
// geometry (numeric accuracy is what is under test, not wire snapping). Nets,
// nodes, branches and transistor devices are constructed with exactly the same
// pin conventions as Engine/NetlistExtractor.cs step 10-11, so the production
// TopologyClassifier / strategies / LinearNetworkAnalyzer consume the result as a
// normal extracted netlist. Net 0 is GND; nets named "VCC" can be flagged as the
// power rail so the BJT strategy identifies the supply rail exactly as it does
// for a real schematic with a VCC symbol.
// ============================================================================

internal sealed class NetlistBuilder
{
    private readonly ElectricalNetlist _netlist = new();
    private readonly Dictionary<int, ElectricalNet> _nets = new();
    private readonly List<(string ElementId, string PinId, int NetId)> _pinBindings = new();
    private readonly List<CircuitElement> _elements = new();
    private readonly Dictionary<int, (string Name, bool IsGround, bool IsPower)> _netOverrides = new();

    /// <summary>
    /// Every topology produced by <see cref="Build"/> is appended here (one entry per
    /// built netlist). The runner clears it per benchmark case and prints the entries in
    /// --topology audit mode so the extracted net→pin incidence of every textbook
    /// circuit can be checked by hand against the schematic described in the comments.
    /// </summary>
    public static List<string> Audit { get; } = new();

    /// <summary>
    /// Every netlist produced by <see cref="Build"/> (one entry per built netlist).
    /// The --roundtrip mode clears this list before re-running each benchmark case and
    /// captures the exact fixture netlists the 30 checked problems use, so the
    /// CircuitGraph export/import equivalence check compares against the same circuit
    /// definitions as the hand-expectation checks (no duplicated fixture definitions).
    /// </summary>
    public static List<ElectricalNetlist> BuiltNetlists { get; } = new();

    public NetlistBuilder Element(string id, ElementType type, double value, string unit = "", double frequencyHz = 1000.0)
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = id,
            Type = type,
            DisplayNumber = value,
            UnitSymbol = unit,
            FrequencyHz = frequencyHz
        };
        element.InitializeDefaultPins();
        _netlist.Elements[id] = element;
        _elements.Add(element);
        return this;
    }

    public NetlistBuilder Pin(string elementId, string pinId, int netId)
    {
        _pinBindings.Add((elementId, pinId, netId));
        return this;
    }

    public NetlistBuilder Net(int netId, string name, bool isGround = false, bool isPower = false)
    {
        _netOverrides[netId] = (name, isGround, isPower);
        return this;
    }

    private ElectricalNet EnsureNet(int netId)
    {
        if (_nets.TryGetValue(netId, out ElectricalNet? existing)) return existing;
        var net = new ElectricalNet { NetId = netId };
        if (_netOverrides.TryGetValue(netId, out (string Name, bool IsGround, bool IsPower) over))
        {
            net.Name = over.Name;
            net.IsGround = over.IsGround;
            net.IsPower = over.IsPower;
        }
        else
        {
            net.Name = netId == 0 ? "GND" : "N" + netId;
            net.IsGround = netId == 0;
        }
        _nets[netId] = net;
        return net;
    }

    public ElectricalNetlist Build()
    {
        // 1. Bind every declared pin to its net (mirrors NetlistExtractor net construction).
        foreach ((string elementId, string pinId, int netId) in _pinBindings)
        {
            ElectricalNet net = EnsureNet(netId);
            CircuitElement element = _netlist.Elements[elementId];
            CircuitPin pin = element.Pins.First(p => p.Id == pinId);
            net.ConnectedPins.Add(new ComponentPinRef(elementId, pinId, pin.Name));
            pin.ConnectedNetId = netId;
        }
        foreach ((int netId, (string Name, bool IsGround, bool IsPower) over) in _netOverrides)
        {
            ElectricalNet net = EnsureNet(netId);
            net.Name = over.Name;
            net.IsGround = over.IsGround;
            net.IsPower = over.IsPower;
        }

        // 2. Nets / nodes ordered by NetId (0 = reference ground).
        foreach (ElectricalNet net in _nets.Values.OrderBy(n => n.NetId)) _netlist.Nets.Add(net);
        foreach (ElectricalNet net in _nets.Values.OrderBy(n => n.NetId))
        {
            _netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });
        }

        // 3. Branches and transistor devices, same pin conventions as NetlistExtractor.cs.
        foreach (CircuitElement element in _elements)
        {
            if (element.Type is ElementType.BjtNpn or ElementType.BjtPnp)
            {
                _netlist.Transistors.Add(new TransistorDevice
                {
                    Element = element,
                    BaseNode = _netlist.FindNodeForPin(element.Id, "B"),
                    CollectorNode = _netlist.FindNodeForPin(element.Id, "C"),
                    EmitterNode = _netlist.FindNodeForPin(element.Id, "E")
                });
                continue;
            }
            if (element.Type == ElementType.Ground) continue;

            string positivePin = element.Type switch
            {
                ElementType.DcVoltageSource or ElementType.AcVoltageSource => "pos",
                ElementType.DcCurrentSource or ElementType.AcCurrentSource => "in",
                ElementType.Diode => "A",
                _ => "1"
            };
            string negativePin = element.Type switch
            {
                ElementType.DcVoltageSource or ElementType.AcVoltageSource => "neg",
                ElementType.DcCurrentSource or ElementType.AcCurrentSource => "out",
                ElementType.Diode => "K",
                _ => "2"
            };
            ElectricalNode? positiveNode = _netlist.FindNodeForPin(element.Id, positivePin);
            ElectricalNode? negativeNode = _netlist.FindNodeForPin(element.Id, negativePin);
            if (positiveNode == null || negativeNode == null) continue;

            double valueSi;
            try
            {
                valueSi = NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
            }
            catch (ArgumentException)
            {
                valueSi = element.DisplayNumber; // controlled sources resolve their own unit table
            }

            var branch = new ElectricalBranch
            {
                BranchId = "B_" + element.Id,
                Element = element,
                PositiveNode = positiveNode,
                NegativeNode = negativeNode,
                PositivePin = new ComponentPinRef(element.Id, positivePin, positivePin),
                NegativePin = new ComponentPinRef(element.Id, negativePin, negativePin),
                ValueSI = valueSi
            };
            _netlist.Branches.Add(branch);
            positiveNode.ConnectedBranches.Add(branch);
            negativeNode.ConnectedBranches.Add(branch);
        }

        VerifyTopology();
        Audit.Add(DescribeTopology());
        BuiltNetlists.Add(_netlist);
        return _netlist;
    }

    /// <summary>
    /// Hard structural self-check of every fixture circuit. Any malformed topology
    /// (floating pin, element shorted onto one net, duplicate pin binding, branch/net
    /// mismatch, missing/duplicated ground) throws instead of silently feeding the
    /// engine a wrong circuit, so a benchmark PASS can never be produced by a
    /// mis-wired netlist.
    /// </summary>
    private void VerifyTopology()
    {
        var problems = new List<string>();

        // (1) Every pin of every element must be bound exactly once.
        var bindingCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string elementId, string pinId, int _) in _pinBindings)
        {
            string key = elementId + "." + pinId;
            bindingCount[key] = bindingCount.TryGetValue(key, out int count) ? count + 1 : 1;
        }
        foreach (CircuitElement element in _elements)
        {
            if (element.Type == ElementType.Ground) continue;
            foreach (CircuitPin pin in element.Pins)
            {
                string key = element.Id + "." + pin.Id;
                if (!bindingCount.TryGetValue(key, out int count))
                {
                    problems.Add($"元件 {element.Id} 的引脚 {pin.Id} 未连接到任何网络");
                }
                else if (count > 1)
                {
                    problems.Add($"元件 {element.Id} 的引脚 {pin.Id} 被重复连接到 {count} 个网络");
                }
            }
        }

        // (2) No element may be short-circuited. Two-terminal elements need both pins on
        //     distinct nets; BJT needs B/C/E distinct; the four-terminal controlled
        //     sources need a genuine control pair (IN1≠IN2) and output pair (OUT1≠OUT2)
        //     but may legitimately share a reference terminal (e.g. IN2=OUT2=GND).
        foreach (CircuitElement element in _elements)
        {
            if (element.Type == ElementType.Ground) continue;
            var netsByPin = _pinBindings
                .Where(b => b.ElementId == element.Id)
                .GroupBy(b => b.PinId)
                .ToDictionary(g => g.Key, g => g.Select(b => b.NetId).Distinct().ToList());

            bool IsControlledSource = element.Type is ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs;
            if (IsControlledSource)
            {
                bool controlDistinct = netsByPin.TryGetValue("IN1", out var in1) && in1.Count == 1 &&
                                       netsByPin.TryGetValue("IN2", out var in2) && in2.Count == 1 && in1[0] != in2[0];
                bool outputDistinct = netsByPin.TryGetValue("OUT1", out var out1) && out1.Count == 1 &&
                                      netsByPin.TryGetValue("OUT2", out var out2) && out2.Count == 1 && out1[0] != out2[0];
                if (!controlDistinct)
                {
                    problems.Add($"受控源 {element.Id} 的控制端 IN1/IN2 未落在两个不同网络上");
                }
                if (!outputDistinct)
                {
                    problems.Add($"受控源 {element.Id} 的输出端 OUT1/OUT2 未落在两个不同网络上");
                }
                int distinctNets = netsByPin.Values.SelectMany(n => n).Distinct().Count();
                if (distinctNets < 3)
                {
                    problems.Add($"受控源 {element.Id} 的四个引脚仅落在 {distinctNets} 个网络上（少于 3 个，拓扑退化）");
                }
            }
            else
            {
                int distinctNets = netsByPin.Values.SelectMany(n => n).Distinct().Count();
                if (distinctNets != element.Pins.Count)
                {
                    problems.Add($"元件 {element.Id} 存在引脚短接（{element.Pins.Count} 个引脚只落在 {distinctNets} 个网络上）");
                }
            }
        }

        // (3) The production netlist validator must see an error-free circuit
        //     (the implicit-ground Info/Warning is expected because fixtures have no GND symbol).
        NetlistValidationResult validation = _netlist.Validate();
        if (!validation.IsValid)
        {
            problems.Add("Netlist.Validate 失败：" + string.Join(" | ",
                validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Error).Select(i => i.Message)));
        }

        // (4) Branch / transistor device list must match the element list, and every
        //     branch endpoint must equal the node of its own pin. Controlled sources are
        //     four-terminal devices: the production NetlistExtractor does not register a
        //     two-terminal ElectricalBranch for them (MnaLinearStrategy models them from
        //     their four pins), so they are excluded here exactly like BJTs.
        int expectedBranches = _elements.Count(e =>
            e.Type != ElementType.Ground &&
            e.Type is not (ElementType.BjtNpn or ElementType.BjtPnp
                or ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs));
        int expectedTransistors = _elements.Count(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        if (_netlist.Branches.Count != expectedBranches)
        {
            problems.Add($"支路数 {_netlist.Branches.Count} 与两端元件数 {expectedBranches} 不一致");
        }
        if (_netlist.Transistors.Count != expectedTransistors)
        {
            problems.Add($"晶体管数 {_netlist.Transistors.Count} 与 BJT 元件数 {expectedTransistors} 不一致");
        }
        foreach (ElectricalBranch branch in _netlist.Branches)
        {
            ElectricalNet? positiveNet = _netlist.FindNetForPin(branch.Element.Id, branch.PositivePin!.PinId);
            ElectricalNet? negativeNet = _netlist.FindNetForPin(branch.Element.Id, branch.NegativePin!.PinId);
            if (positiveNet == null || negativeNet == null ||
                positiveNet != branch.PositiveNode.Net || negativeNet != branch.NegativeNode.Net)
            {
                problems.Add($"支路 {branch.BranchId} 的端点网络与其引脚所在网络不一致");
            }
            if (!double.IsFinite(branch.ValueSI))
            {
                problems.Add($"支路 {branch.BranchId} 的 SI 数值非有限");
            }
        }

        // (5) Exactly one reference ground, with NetId 0.
        List<ElectricalNet> grounds = _netlist.Nets.Where(n => n.IsGround).ToList();
        if (grounds.Count != 1 || grounds[0].NetId != 0)
        {
            problems.Add($"参考地不唯一或 NetId 不为 0（grounds={grounds.Count}）");
        }

        // (6) Node/branch incidence must be symmetric (ConnectedBranches is what the
        //     dedicated RC/RL/RLC strategies traverse for their single-loop checks).
        foreach (ElectricalNode node in _netlist.Nodes)
        {
            int incident = _netlist.Branches.Count(b => b.PositiveNode == node || b.NegativeNode == node);
            if (node.ConnectedBranches.Count != incident)
            {
                problems.Add($"节点 {node.Name} 的 ConnectedBranches({node.ConnectedBranches.Count}) 与实际关联支路数 {incident} 不一致");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("网表拓扑自检失败：" + string.Join("；", problems));
        }
    }

    private string DescribeTopology()
    {
        var nets = new List<string>();
        foreach (ElectricalNet net in _netlist.Nets.OrderBy(n => n.NetId))
        {
            string pins = string.Join(",", net.ConnectedPins.Select(p => p.ElementId + "." + p.PinId));
            nets.Add($"{net.Name}[{pins}]");
        }
        var elements = new List<string>();
        foreach (CircuitElement element in _elements)
        {
            string value = element.DisplayNumber.ToString("G6", CultureInfo.InvariantCulture) + element.UnitSymbol;
            string extra = element.Type is ElementType.BjtNpn or ElementType.BjtPnp ? " β=" + value : "=" + value;
            elements.Add(element.Id + ":" + element.Type + extra);
        }
        return $"nets={_netlist.Nets.Count} branches={_netlist.Branches.Count} transistors={_netlist.Transistors.Count} | " +
               string.Join(" ", nets) + " | " + string.Join(", ", elements);
    }
}
