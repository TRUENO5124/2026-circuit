// ============================================================================
// File: tests/NonlinearCircuitChecks/BjtCcChecks.cs
// OWNER: engine-integration.
// Regression checks for the BJT common-collector (emitter-follower) support and
// the reserved multi-stage contract (projects/nonlinear-engine-and-circuit-models-design-20261018.md §6/§7/§10):
//   * classifier returns BjtAmplifier/CommonCollector for an NPN divider-bias
//     emitter follower (collector tied directly to VCC, output taken at emitter)
//     and for the PNP mirror variant (collector directly to GND, emitter resistor
//     to VCC); CommonCollector must not be confused with CommonEmitter/CommonBase;
//   * Q point, mid-band Av (0 < Av < 1), Ri and Ro against hand calculations
//     (relative tolerance 1 %), for one NPN divider-bias and one PNP divider-bias
//     follower. The numeric values below are textbook formula results computed
//     independently of the strategy implementation:
//       IBQ=(V_B_drive-V_BE)/(Rth+(1+beta)Re), IEQ=(1+beta)IBQ, VCEQ=VCC-IEQ*Re,
//       rbe=200+(1+beta)*UT/IEQ, Re'=Re||RL, Av=(1+beta)Re'/(rbe+(1+beta)Re'),
//       Ri=Rb||[rbe+(1+beta)Re'], Ro=Re||(rbe+Rb)/(1+beta).
//   * failure paths: cutoff (IBQ<=0) and missing base bias fail explicitly;
//   * multi-stage reserved contract: >= 2 transistors classifies as
//     MultiStageAmplifier/"Cascade", SolveAsync fails with "多级"/"后续开发规划"/
//     "预留", IMultiStageAnalysis.TryPlan returns the two detected stages, and a
//     mixed BJT+MOSFET network also routes to the multi-stage strategy.
// The strategy also publishes its raw numeric results through
// PedagogicalSolutionStep.Values (IBQ/ICQ/IEQ/VCEQ/rbe/RePrime/Av/Rb/Ri/Ro/fL)
// so the checks compare numbers instead of parsing localized display strings.
// ============================================================================

using System;
using System.Globalization;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.MultiStage;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class BjtCcChecks
{
    private const double Tolerance = 0.01;

    public static void Run()
    {
        CheckResult.Section("BJT 共集电极与多级预留接口（engine-integration）");
        CheckNpnDividerCommonCollector();
        CheckPnpDividerCommonCollector();
        CheckCommonCollectorFixedBiasVariants();
        CheckNotConfusedWithCommonEmitterOrCommonBase();
        CheckCommonCollectorFailurePaths();
        CheckMultiStageReservedContract();
    }

    // =====================================================================
    // 1. NPN divider-bias emitter follower:
    //    VCC=12 V, beta=100, VBE=0.7 V, Rb1=51k (B-VCC), Rb2=15k (B-GND),
    //    Re=1k (E-GND), RL=10k (via output coupling capacitor C2),
    //    C1 input coupling from the AC source to the base.
    // =====================================================================
    private static void CheckNpnDividerCommonCollector()
    {
        ElectricalNetlist netlist = BuildNpnDividerCommonCollector();
        ClassificationResult classification = TopologyClassifier.Classify(netlist);

        CheckResult.Check(
            classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
            classification.SubtypeName == "CommonCollector" &&
            classification.Strategy is BjtAmplifierStrategy,
            "NPN 分压偏置共集：分类 BjtAmplifier/CommonCollector 并返回 BjtAmplifierStrategy",
            $"{classification.TopologyType}/{classification.SubtypeName}/{classification.Strategy.GetType().Name}");
        CheckResult.Check(classification.Description.Contains("共集电极", StringComparison.Ordinal),
            "NPN 分压偏置共集：分类描述注明共集电极", classification.Description);

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();

        CheckResult.Check(solution.IsSuccess, "NPN 分压偏置共集：求解成功", solution.SummaryText);
        CheckResult.Check(solution.Steps.Count >= 5, "NPN 分压偏置共集：教学推导步骤 >= 5", $"count={solution.Steps.Count}");
        CheckResult.Check(solution.QPoint != null && solution.QPoint.IsActiveRegion,
            "NPN 分压偏置共集：Q 点位于放大区", solution.QPoint?.WorkingRegion ?? "QPoint is null");
        CheckResult.Check(solution.QPoint != null && solution.QPoint.WorkingRegion.Contains("共集电极", StringComparison.Ordinal),
            "NPN 分压偏置共集：WorkingRegion 注明共集电极（射极跟随器）", solution.QPoint?.WorkingRegion ?? "null");

        // Hand calculations (textbook estimation chain, tolerance 1 %):
        //   U_B=VCC·Rb2/(Rb1+Rb2)=2.73 V; IEQ=(2.73−0.7)/1 kΩ=2.03 mA;
        //   IBQ=2.03 mA/101=20.1 µA; ICQ=β·IBQ=2.01 mA; VCEQ=12−2.03 mA·1 kΩ=9.97 V.
        CheckResult.Close("NPN 共集 IBQ=I_EQ/(1+β)=(2.73−0.7)/1k/101≈20.1 µA",
            2.01e-5, StepValue(solution, "IBQ"));
        CheckResult.Close("NPN 共集 ICQ=beta·IBQ≈2.01 mA",
            2.01e-3, StepValue(solution, "ICQ"));
        CheckResult.Close("NPN 共集 IEQ=(U_B−0.7)/Re≈2.03 mA",
            2.03e-3, StepValue(solution, "IEQ"));
        CheckResult.Close("NPN 共集 VCEQ=VCC-IEQ·Re≈9.97 V",
            9.97, StepValue(solution, "VCEQ"));
        CheckResult.Close("NPN 共集 rbe=200+101·UT/IEQ≈1490 Ω（I_EQ 取显示值 2.03 mA）",
            1490.0, StepValue(solution, "rbe"));
        CheckResult.Close("NPN 共集 Re'=Re||RL=909.09 Ω",
            909.09, StepValue(solution, "RePrime"));
        double av = StepValue(solution, "Av");
        CheckResult.Check(av > 0 && av < 1,
            "NPN 共集 Av 为正且略小于 1（射极跟随）", $"Av={CheckResult.Format(av)}");
        CheckResult.Close("NPN 共集 Av=(1+beta)Re'/(rbe+(1+beta)Re')≈0.98", 0.98, av);
        CheckResult.Close("NPN 共集 Ri=Rb||[rbe+(1+beta)Re']≈10.31 kΩ",
            10310.0, StepValue(solution, "Ri"));
        CheckResult.Close("NPN 共集 Ro=Re||(rbe+Rb)/(1+beta)≈114.66 Ω",
            114.66, StepValue(solution, "Ro"));

        CheckResult.Check(solution.AcParameters != null &&
                          ParseDisplay(solution.AcParameters.VoltageGain) > 0 &&
                          ParseDisplay(solution.AcParameters.VoltageGain) < 1,
            "NPN 共集 AcParameters.VoltageGain 显示 0<Av<1", solution.AcParameters?.VoltageGain ?? "null");
        CheckResult.Close("NPN 共集 AcParameters.VoltageGain 显示值与手算一致",
            0.98, ParseDisplay(solution.AcParameters!.VoltageGain));
        CheckResult.Check(!string.IsNullOrWhiteSpace(solution.AcParameters.InputResistance) &&
                          !string.IsNullOrWhiteSpace(solution.AcParameters.OutputResistance),
            "NPN 共集 AcParameters 正常填写 Ri/Ro",
            $"{solution.AcParameters.InputResistance} / {solution.AcParameters.OutputResistance}");
    }

    // =====================================================================
    // 2. PNP divider-bias emitter follower (NPN mirror):
    //    VCC=12 V, beta=80, VEB=0.7 V, Rb1=20k (B-VCC), Rb2=10k (B-GND),
    //    Re=2k (E-VCC, the PNP emitter is the output), collector directly to GND
    //    (the PNP "common" AC-ground terminal), RL=4.7k via C2.
    // =====================================================================
    private static void CheckPnpDividerCommonCollector()
    {
        ElectricalNetlist netlist = BuildPnpDividerCommonCollector();
        ClassificationResult classification = TopologyClassifier.Classify(netlist);

        CheckResult.Check(
            classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
            classification.SubtypeName == "CommonCollector" &&
            classification.Strategy is BjtAmplifierStrategy,
            "PNP 共集镜像：分类 BjtAmplifier/CommonCollector 并返回 BjtAmplifierStrategy",
            $"{classification.TopologyType}/{classification.SubtypeName}/{classification.Strategy.GetType().Name}");
        CheckResult.Check(classification.Description.Contains("PNP", StringComparison.Ordinal) &&
                          classification.Description.Contains("共集电极", StringComparison.Ordinal),
            "PNP 共集镜像：分类描述注明 PNP 极性与共集电极", classification.Description);

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();

        CheckResult.Check(solution.IsSuccess, "PNP 共集镜像：求解成功", solution.SummaryText);
        CheckResult.Check(solution.Steps.Count >= 5, "PNP 共集镜像：教学推导步骤 >= 5", $"count={solution.Steps.Count}");
        CheckResult.Check(solution.QPoint != null && solution.QPoint.IsActiveRegion,
            "PNP 共集镜像：Q 点位于放大区", solution.QPoint?.WorkingRegion ?? "QPoint is null");
        CheckResult.Check(solution.QPoint != null &&
                          solution.QPoint.WorkingRegion.Contains("PNP", StringComparison.Ordinal) &&
                          solution.QPoint.WorkingRegion.Contains("U_CE = -", StringComparison.Ordinal),
            "PNP 共集镜像：WorkingRegion 注明 PNP 的 U_CE 方向约定", solution.QPoint?.WorkingRegion ?? "null");

        // Hand calculations (textbook estimation chain, tolerance 1 %):
        //   U_B=VCC·Rb2/(Rb1+Rb2)=4 V; IEQ=(12−4−0.7)/2 kΩ=3.65 mA;
        //   IBQ=3.65 mA/81=45.06 µA; ICQ=β·IBQ=3.6 mA; UECQ=12−3.65 mA·2 kΩ=4.7 V.
        CheckResult.Close("PNP 共集 IBQ=I_EQ/(1+β)=(12−4−0.7)/2k/81≈45.06 µA",
            4.506e-5, StepValue(solution, "IBQ"));
        CheckResult.Close("PNP 共集 ICQ=beta·IBQ≈3.6 mA",
            3.6e-3, StepValue(solution, "ICQ"));
        CheckResult.Close("PNP 共集 IEQ=(12−U_B−0.7)/2k≈3.65 mA",
            3.65e-3, StepValue(solution, "IEQ"));
        CheckResult.Close("PNP 共集 VECQ=VCC-IEQ·Re≈4.7 V（|U_CE|）",
            4.7, StepValue(solution, "VCEQ"));
        CheckResult.Close("PNP 共集 rbe=200+81·UT/IEQ≈776.99 Ω（I_EQ 取显示值 3.65 mA）",
            776.99, StepValue(solution, "rbe"));
        CheckResult.Close("PNP 共集 Re'=Re||RL=1.4 kΩ（两位小数显示单位）",
            1400.0, StepValue(solution, "RePrime"));
        double av = StepValue(solution, "Av");
        CheckResult.Check(av > 0 && av < 1,
            "PNP 共集 Av 为正且略小于 1（射极跟随）", $"Av={CheckResult.Format(av)}");
        CheckResult.Close("PNP 共集 Av≈0.99", 0.99, av);
        CheckResult.Close("PNP 共集 Ri≈6.3 kΩ", 6300.0, StepValue(solution, "Ri"));
        CheckResult.Close("PNP 共集 Ro≈87.9 Ω", 87.9, StepValue(solution, "Ro"));

        // PNP QPoint.Vceq 显示 |U_CE| 正值，不带负号（方向在 WorkingRegion 说明）。
        CheckResult.Check(solution.QPoint != null && !solution.QPoint.Vceq.Contains('-', StringComparison.Ordinal),
            "PNP 共集：QPoint.Vceq 显示 |U_CE| 正值（不带负号）", solution.QPoint?.Vceq ?? "null");
        CheckResult.Close("PNP 共集 AcParameters.VoltageGain 显示值与手算一致",
            0.99, ParseDisplay(solution.AcParameters!.VoltageGain));
    }

    // =====================================================================
    // 3. Fixed-bias follower variants (no AC source / no load -> RL=infinity):
    //    NPN: Rb=200k (B-VCC), Re=2k (E-GND), beta=100;
    //    PNP mirror: Rb=470k (B-GND), Re=1k (E-VCC), beta=80.
    // =====================================================================
    private static void CheckCommonCollectorFixedBiasVariants()
    {
        var npnBuilder = new TestNetlistBuilder();
        npnBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        npnBuilder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        npnBuilder.Element("Rb", ElementType.Resistor, 200.0, "kΩ");
        npnBuilder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        npnBuilder.Net(1, "VCC");
        npnBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                  .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 3)
                  .Pin("Rb", "1", 1).Pin("Rb", "2", 2)
                  .Pin("Re", "1", 3).Pin("Re", "2", 0);
        ElectricalNetlist npnFixedNet = npnBuilder.Build();
        ClassificationResult npnFixedClass = TopologyClassifier.Classify(npnFixedNet);
        CheckResult.Check(npnFixedClass.SubtypeName == "CommonCollector",
            "NPN 固定偏置共集：识别为 CommonCollector（固定偏置）", npnFixedClass.SubtypeName);
        PedagogicalSolution npnFixed = npnFixedClass.Strategy
            .SolveAsync(npnFixedNet, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(npnFixed.IsSuccess, "NPN 固定偏置共集：无负载（RL=∞）仍求解成功", npnFixed.SummaryText);
        CheckResult.Close("NPN 固定偏置共集 IBQ=(VCC-0.7)/(Rb+101·Re)≈28.1095 µA",
            2.81094527363184e-05, StepValue(npnFixed, "IBQ"));
        CheckResult.Close("NPN 固定偏置共集 IEQ≈2.83905 mA", 2.83905472636816e-3, StepValue(npnFixed, "IEQ"));
        CheckResult.Close("NPN 固定偏置共集 VCEQ=VCC-IEQ·Re≈6.32189 V",
            6.32189054726368, StepValue(npnFixed, "VCEQ"));
        CheckResult.Close("NPN 固定偏置共集 Re'=Re=2000 Ω（RL=∞）", 2000.0, StepValue(npnFixed, "RePrime"));
        CheckResult.Close("NPN 固定偏置共集 Av≈0.994462（无负载时更高但仍 <1）",
            0.994461755090376, StepValue(npnFixed, "Av"));
        CheckResult.Close("NPN 固定偏置共集 Ri=Rb||[rbe+101·Re]≈100775 Ω",
            100775.18290734, StepValue(npnFixed, "Ri"));
        CheckResult.Close("NPN 固定偏置共集 Ro=Re||(rbe+Rb)/101≈997.83 Ω",
            997.829347364132, StepValue(npnFixed, "Ro"));

        var pnpBuilder = new TestNetlistBuilder();
        pnpBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        pnpBuilder.Element("Q1", ElementType.BjtPnp, 80.0, "");
        pnpBuilder.Element("Rb", ElementType.Resistor, 470.0, "kΩ");
        pnpBuilder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        pnpBuilder.Net(1, "VCC");
        pnpBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                  .Pin("Q1", "B", 2).Pin("Q1", "C", 0).Pin("Q1", "E", 3)
                  .Pin("Rb", "1", 2).Pin("Rb", "2", 0)
                  .Pin("Re", "1", 3).Pin("Re", "2", 1);
        ElectricalNetlist pnpFixedNet = pnpBuilder.Build();
        ClassificationResult pnpFixedClass = TopologyClassifier.Classify(pnpFixedNet);
        CheckResult.Check(pnpFixedClass.SubtypeName == "CommonCollector",
            "PNP 固定偏置共集镜像：识别为 CommonCollector（固定偏置）", pnpFixedClass.SubtypeName);
        PedagogicalSolution pnpFixed = pnpFixedClass.Strategy
            .SolveAsync(pnpFixedNet, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(pnpFixed.IsSuccess, "PNP 固定偏置共集镜像：无负载仍求解成功", pnpFixed.SummaryText);
        CheckResult.Close("PNP 固定偏置共集 IBQ=(VCC-0.7)/(Rb+81·Re)≈20.5082 µA",
            2.0508166969147e-05, StepValue(pnpFixed, "IBQ"));
        CheckResult.Close("PNP 固定偏置共集 VECQ≈10.3388 V", 10.3388384754991, StepValue(pnpFixed, "VCEQ"));
        CheckResult.Close("PNP 固定偏置共集 Re'=Re=1000 Ω（RL=∞）", 1000.0, StepValue(pnpFixed, "RePrime"));
        CheckResult.Close("PNP 固定偏置共集 Av≈0.982202", 0.982201685613906, StepValue(pnpFixed, "Av"));
        CheckResult.Close("PNP 固定偏置共集 Ri≈70157.7 Ω", 70157.6834816461, StepValue(pnpFixed, "Ri"));
        CheckResult.Close("PNP 固定偏置共集 Ro≈853.385 Ω", 853.38511707566, StepValue(pnpFixed, "Ro"));
    }

    // =====================================================================
    // 4. CommonCollector must not be confused with CommonEmitter/CommonBase.
    // =====================================================================
    private static void CheckNotConfusedWithCommonEmitterOrCommonBase()
    {
        // 3.1 NPN voltage-divider common-emitter (collector through Rc to VCC) stays CE.
        var ceBuilder = new TestNetlistBuilder();
        ceBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        ceBuilder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        ceBuilder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        ceBuilder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        ceBuilder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        ceBuilder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        ceBuilder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        ceBuilder.Element("RL", ElementType.Resistor, 10.0, "kΩ");
        ceBuilder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        ceBuilder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        ceBuilder.Element("Ce", ElementType.Capacitor, 47.0, "μF");
        ceBuilder.Net(1, "VCC");
        ceBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                 .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
                 .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
                 .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
                 .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
                 .Pin("Re", "1", 4).Pin("Re", "2", 0)
                 .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
                 .Pin("C1", "1", 5).Pin("C1", "2", 2)
                 .Pin("C2", "1", 3).Pin("C2", "2", 6)
                 .Pin("RL", "1", 6).Pin("RL", "2", 0)
                 .Pin("Ce", "1", 4).Pin("Ce", "2", 0);
        ElectricalNetlist ceNetlist = ceBuilder.Build();
        ClassificationResult ce = TopologyClassifier.Classify(ceNetlist);
        CheckResult.Check(ce.SubtypeName == "VoltageDividerSelfBias" && ce.SubtypeName != "CommonCollector",
            "共射分压偏置不被误判为共集（仍为 VoltageDividerSelfBias）", ce.SubtypeName);
        PedagogicalSolution ceSolution = ce.Strategy
            .SolveAsync(ceNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(ceSolution.IsSuccess &&
                          Math.Abs(ParseDisplay(ceSolution.AcParameters!.VoltageGain)) > 1.0,
            "共射分压偏置回归：仍求解成功且 |Av|>1（反相放大，未被共集公式替换）",
            ceSolution.IsSuccess ? ceSolution.AcParameters!.VoltageGain : ceSolution.SummaryText);

        // 3.2 NPN common-base (base AC-grounded, emitter input) stays CommonBase.
        var cbBuilder = new TestNetlistBuilder();
        cbBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        cbBuilder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        cbBuilder.Element("Q1", ElementType.BjtNpn, 80.0, "");
        cbBuilder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        cbBuilder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        cbBuilder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        cbBuilder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        cbBuilder.Element("Cb", ElementType.Capacitor, 47.0, "μF");
        cbBuilder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        cbBuilder.Net(1, "VCC");
        cbBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                 .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
                 .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
                 .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
                 .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
                 .Pin("Re", "1", 4).Pin("Re", "2", 0)
                 .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
                 .Pin("Cb", "1", 2).Pin("Cb", "2", 0)
                 .Pin("C1", "1", 5).Pin("C1", "2", 4);
        ElectricalNetlist cbNetlist = cbBuilder.Build();
        ClassificationResult cb = TopologyClassifier.Classify(cbNetlist);
        CheckResult.Check(cb.SubtypeName == "CommonBase",
            "共基极不被误判为共集（仍为 CommonBase）", cb.SubtypeName);
        PedagogicalSolution cbSolution = cb.Strategy
            .SolveAsync(cbNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(cbSolution.IsSuccess,
            "共基极回归：原有共基公式路径仍求解成功", cbSolution.SummaryText);
    }

    // =====================================================================
    // 5. Failure paths: cutoff / missing bias must fail explicitly.
    // =====================================================================
    private static void CheckCommonCollectorFailurePaths()
    {
        // 4.1 NPN follower cutoff: Rb2=1k makes Vth≈0.119 V < VBE -> IBQ<0.
        var npnCutoffBuilder = new TestNetlistBuilder();
        npnCutoffBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        npnCutoffBuilder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        npnCutoffBuilder.Element("Rb1", ElementType.Resistor, 100.0, "kΩ");
        npnCutoffBuilder.Element("Rb2", ElementType.Resistor, 1.0, "kΩ");
        npnCutoffBuilder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        npnCutoffBuilder.Net(1, "VCC");
        npnCutoffBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                       .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 3)
                       .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
                       .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
                       .Pin("Re", "1", 3).Pin("Re", "2", 0);
        ElectricalNetlist npnCutoffNet = npnCutoffBuilder.Build();
        ClassificationResult npnCutoffClass = TopologyClassifier.Classify(npnCutoffNet);
        CheckResult.Check(npnCutoffClass.SubtypeName == "CommonCollector",
            "NPN 截止共集：仍分类为 CommonCollector", npnCutoffClass.SubtypeName);
        PedagogicalSolution npnCutoff = npnCutoffClass.Strategy
            .SolveAsync(npnCutoffNet, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(!npnCutoff.IsSuccess,
            "NPN 共集截止：IBQ<=0 时明确拒绝求解", npnCutoff.SummaryText);
        CheckResult.TextContains("NPN 共集截止：失败信息说明截止区", npnCutoff.SummaryText, "截止");

        // 4.2 PNP mirror cutoff: (VCC-Vth)=0.119 V < VEB -> IBQ<0.
        var pnpCutoffBuilder = new TestNetlistBuilder();
        pnpCutoffBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        pnpCutoffBuilder.Element("Q1", ElementType.BjtPnp, 80.0, "");
        pnpCutoffBuilder.Element("Rb1", ElementType.Resistor, 1.0, "kΩ");
        pnpCutoffBuilder.Element("Rb2", ElementType.Resistor, 100.0, "kΩ");
        pnpCutoffBuilder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        pnpCutoffBuilder.Net(1, "VCC");
        pnpCutoffBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                       .Pin("Q1", "B", 2).Pin("Q1", "C", 0).Pin("Q1", "E", 3)
                       .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
                       .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
                       .Pin("Re", "1", 3).Pin("Re", "2", 1);
        ElectricalNetlist pnpCutoffNet = pnpCutoffBuilder.Build();
        ClassificationResult pnpCutoffClass = TopologyClassifier.Classify(pnpCutoffNet);
        CheckResult.Check(pnpCutoffClass.SubtypeName == "CommonCollector",
            "PNP 截止共集：仍分类为 CommonCollector", pnpCutoffClass.SubtypeName);
        PedagogicalSolution pnpCutoff = pnpCutoffClass.Strategy
            .SolveAsync(pnpCutoffNet, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(!pnpCutoff.IsSuccess,
            "PNP 共集截止：IBQ<=0 时明确拒绝求解", pnpCutoff.SummaryText);
        CheckResult.TextContains("PNP 共集截止：失败信息说明截止区", pnpCutoff.SummaryText, "截止");

        // 4.3 Missing base bias: only input coupling capacitor, no DC base path.
        var missingBiasBuilder = new TestNetlistBuilder();
        missingBiasBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        missingBiasBuilder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        missingBiasBuilder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        missingBiasBuilder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        missingBiasBuilder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        missingBiasBuilder.Net(1, "VCC");
        missingBiasBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                          .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
                          .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 3)
                          .Pin("Re", "1", 3).Pin("Re", "2", 0)
                          .Pin("C1", "1", 5).Pin("C1", "2", 2);
        ElectricalNetlist missingBiasNet = missingBiasBuilder.Build();
        ClassificationResult missingBiasClass = TopologyClassifier.Classify(missingBiasNet);
        PedagogicalSolution missingBias = missingBiasClass.Strategy
            .SolveAsync(missingBiasNet, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(!missingBias.IsSuccess,
            "NPN 共集缺偏置：没有基极直流偏置时明确拒绝求解", missingBias.SummaryText);
        CheckResult.TextContains("NPN 共集缺偏置：失败信息说明偏置网络不完整", missingBias.SummaryText, "偏置");
    }

    // =====================================================================
    // 6. Reserved multi-stage contract (§7): reject, explain, plan two stages.
    // =====================================================================
    private static void CheckMultiStageReservedContract()
    {
        ElectricalNetlist multi = BuildTwoStageCommonEmitter();

        ClassificationResult classification = TopologyClassifier.Classify(multi);
        CheckResult.Check(
            classification.TopologyType == CircuitTopologyType.MultiStageAmplifier &&
            classification.SubtypeName == "Cascade" &&
            classification.Strategy is MultiStageAmplifierStrategy,
            "多级放大：>=2 晶体管分类 MultiStageAmplifier/Cascade 并返回多级策略",
            $"{classification.TopologyType}/{classification.SubtypeName}/{classification.Strategy.GetType().Name}");
        CheckResult.Check(classification.Description.Contains("多级", StringComparison.Ordinal),
            "多级放大：分类描述注明多级", classification.Description);

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(multi, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(!solution.IsSuccess, "多级放大：SolveAsync 明确失败（不伪造单管结果）", solution.SummaryText);
        CheckResult.TextContains("多级放大：失败信息含“多级/后续开发规划/预留”",
            solution.SummaryText, "多级", "后续开发规划", "预留");
        CheckResult.TextContains("多级放大：失败信息说明当前只支持单管",
            solution.SummaryText, "单管");

        if (classification.Strategy is IMultiStageAnalysis planner)
        {
            bool planned = planner.TryPlan(multi, out MultiStagePlan plan, out string error);
            CheckResult.Check(planned && string.IsNullOrEmpty(error),
                "多级预留接口：IMultiStageAnalysis.TryPlan 成功分解", error);
            CheckResult.Check(plan.Stages.Count == 2,
                "多级预留接口：TryPlan 返回 2 个待级联阶段", $"count={plan.Stages.Count}");
            CheckResult.Check(
                plan.Stages.Select(s => s.DeviceId).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "Q1", "Q2" }),
                "多级预留接口：TryPlan 阶段器件为 Q1/Q2",
                string.Join(",", plan.Stages.Select(s => s.DeviceId)));
            CheckResult.TextContains("多级预留接口：TryPlan 给出耦合说明与后续实现顺序",
                plan.TopologyNote + " " + plan.PlannedImplementationOrder, "晶体管", "多级级联");
        }
        else
        {
            CheckResult.Check(false, "多级预留接口：策略实现 IMultiStageAnalysis", classification.Strategy.GetType().Name);
        }

        // 5.2 A mixed BJT+MOSFET network is also a multi-stage network (never a
        // single-transistor answer): transistor total = BJT count + MOS count.
        var mixedBuilder = new TestNetlistBuilder();
        mixedBuilder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        mixedBuilder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        mixedBuilder.Element("M1", ElementType.MosfetNEnh, 1.5, "V");
        mixedBuilder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        mixedBuilder.Net(1, "VCC");
        mixedBuilder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
                    .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 0)
                    .Pin("M1", "G", 2).Pin("M1", "D", 1).Pin("M1", "S", 0)
                    .Pin("R1", "1", 1).Pin("R1", "2", 0);
        ElectricalNetlist mixed = mixedBuilder.Build();
        ClassificationResult mixedClassification = TopologyClassifier.Classify(mixed);
        CheckResult.Check(
            mixedClassification.TopologyType == CircuitTopologyType.MultiStageAmplifier &&
            mixedClassification.SubtypeName == "Cascade" &&
            mixedClassification.Strategy is MultiStageAmplifierStrategy,
            "混合 BJT+MOS 网络：晶体管总数>1 时分类为 MultiStageAmplifier/Cascade（不套用单管公式）",
            $"{mixedClassification.TopologyType}/{mixedClassification.SubtypeName}");
    }

    // =====================================================================
    // Fixtures
    // =====================================================================
    private static ElectricalNetlist BuildNpnDividerCommonCollector()
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
        return builder.Build();
    }

    private static ElectricalNetlist BuildPnpDividerCommonCollector()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtPnp, 80.0, "");
        builder.Element("Rb1", ElementType.Resistor, 20.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 10.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("RL", ElementType.Resistor, 4.7, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        builder.Net(1, "VCC");
        // PNP 镜像：C 直接接地（PNP 的“公共”交流地端），E 经 Re 接 VCC，输出取自 E。
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 0).Pin("Q1", "E", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 1)
               .Pin("C1", "1", 5).Pin("C1", "2", 2)
               .Pin("C2", "1", 4).Pin("C2", "2", 6)
               .Pin("RL", "1", 6).Pin("RL", "2", 0);
        return builder.Build();
    }

    /// <summary>Two cascaded NPN common-emitter stages (RC-coupled): a real multi-stage netlist.</summary>
    private static ElectricalNetlist BuildTwoStageCommonEmitter()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Q2", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Rc1", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("Re1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("C3", ElementType.Capacitor, 10.0, "μF");
        builder.Element("Rb3", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb4", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Rc2", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("Re2", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("C4", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("RL2", ElementType.Resistor, 10.0, "kΩ");
        builder.Net(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 2).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 3).Pin("Q1", "C", 4).Pin("Q1", "E", 5)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 3)
               .Pin("Rb2", "1", 3).Pin("Rb2", "2", 0)
               .Pin("Rc1", "1", 1).Pin("Rc1", "2", 4)
               .Pin("Re1", "1", 5).Pin("Re1", "2", 0)
               .Pin("C1", "1", 2).Pin("C1", "2", 3)
               .Pin("C3", "1", 4).Pin("C3", "2", 6)
               .Pin("Rb3", "1", 1).Pin("Rb3", "2", 6)
               .Pin("Rb4", "1", 6).Pin("Rb4", "2", 0)
               .Pin("Q2", "B", 6).Pin("Q2", "C", 7).Pin("Q2", "E", 8)
               .Pin("Rc2", "1", 1).Pin("Rc2", "2", 7)
               .Pin("Re2", "1", 8).Pin("Re2", "2", 0)
               .Pin("C4", "1", 7).Pin("C4", "2", 9)
               .Pin("RL2", "1", 9).Pin("RL2", "2", 0);
        ElectricalNetlist netlist = builder.Build();
        // TestNetlistBuilder 只建立 Elements/Nets/Nodes（不跑 NetlistExtractor 的晶体管提取），
        // IMultiStageAnalysis.TryPlan 读取 netlist.Transistors，因此这里按抽取器同样的方式登记器件。
        RegisterTransistorDevices(netlist);
        return netlist;
    }

    // =====================================================================
    // Helpers
    // =====================================================================
    private static void RegisterTransistorDevices(ElectricalNetlist netlist)
    {
        foreach (CircuitElement element in netlist.Elements.Values.Where(e => e.Type is
            ElementType.BjtNpn or ElementType.BjtPnp or
            ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh or
            ElementType.MosfetNDep or ElementType.MosfetPDep or
            ElementType.Jfet or ElementType.JfetN or ElementType.JfetP))
        {
            netlist.Transistors.Add(new TransistorDevice { Element = element });
        }
    }

    private static double StepValue(PedagogicalSolution solution, string key)
    {
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            if (step.Values.TryGetValue(key, out string? raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
        }
        return double.NaN;
    }

    /// <summary>Parses a leading numeric value from a display string such as "0.982" or "1.23 kΩ".</summary>
    private static double ParseDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        int index = 0;
        while (index < text.Length &&
               (char.IsDigit(text[index]) || text[index] is '.' or '-' or '+' or 'e' or 'E'))
            index++;
        return double.TryParse(text[..index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.NaN;
    }
}
