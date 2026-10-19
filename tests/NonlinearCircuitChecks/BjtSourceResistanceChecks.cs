// ============================================================================
// File: tests/NonlinearCircuitChecks/BjtSourceResistanceChecks.cs
// OWNER: engine-bjt.
//
// Round-8 user feedback (2026-10-18): the user edited
// examples/circuit-models/B07-common-emitter-bypassed.circuit by inserting a
// signal-source internal resistance R6 = 50 Ω between the AC source and a
// polarized 47 µF electrolytic input coupling capacitor C3 (positive terminal
// at the base) and re-parameterising the amplifier. The engine used to reject
// the network as an "uncovered resistor branch".
//
// This check file verifies:
//   1) the user's B07 netlist (13 elements, C3 electrolytic) classifies as
//      BjtAmplifier/VoltageDividerSelfBias and solves; the textbook estimation
//      chain gives U_B=2.79 V, I_EQ=2.09 mA, I_BQ=20.69 µA, I_CQ=2.07 mA,
//      U_CEQ=7.84 V, rbe=1.46 kΩ, Av=-34.25, Ri=1.23 kΩ, Ro=1 kΩ,
//      Rs=50 Ω, Aus=Av·Ri/(Rs+Ri)=-32.91 and fL includes τ1=(Rs+Ri)C1
//      (hand: fl1=1/[2π(Rs+Ri)C1]=2.65 Hz, fl2=1/[2π(Rc+RL)C2]=7.96 Hz,
//      fl_e=1/[2πRe'·Ce]=40.85 Hz → fL=√(fl1²+fl2²+fl_e²)≈41.7 Hz);
//      the derivation has 6 steps (5 teaching + 1 C3 electrolytic polarity
//      check in the DC phase), DC(2)/AC(4) groups, continuous StepIndex;
//   2) the same topology without R6 and with a normal C1 keeps the previous
//      behaviour (HasSourceResistance=false, no Aus, same Av/Ri/fL);
//   3) an extra uncovered resistor R9 is rejected with a diagnostic message
//      that names the element and its nets plus the supported resistor roles;
//   4) the electrolytic-capacitor polarity check stays in the DC phase, and a
//      reversed C3 blocks AC analysis and preserves DC polarity diagnostics;
//   5) common-base and common-collector also report Rs/Aus (self-consistency
//      against their own rounded Au/Av and ri).
// ============================================================================

using System;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class BjtSourceResistanceChecks
{
    public static void Run()
    {
        CheckResult.Section("BJT 信号源内阻 Rs 与电解输入耦合电容（用户 B07 变体）");
        CheckUserB07();
        CheckNoSourceResistanceControl();
        CheckUncoveredResistorDiagnostic();
        CheckElectrolyticPolarityReverse();
        CheckCommonBaseWithSourceResistance();
        CheckCommonCollectorWithSourceResistance();
    }

    // =====================================================================
    // 1) User B07: 13 elements, C3 electrolytic input coupling, R6=50 Ω.
    // =====================================================================
    private static void CheckUserB07()
    {
        ElectricalNetlist netlist = BuildUserB07(withSourceResistance: true, reverseInputCap: false);
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        CheckResult.Check(classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
                          classification.SubtypeName == "VoltageDividerSelfBias",
            "用户 B07：分类 BjtAmplifier/VoltageDividerSelfBias",
            $"{classification.TopologyType}/{classification.SubtypeName}");
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess, "用户 B07：求解成功（已支持信号源内阻 Rs + 电解输入耦合电容）", solution.SummaryText);
        if (!solution.IsSuccess) return;

        // ---- Q 点教材估算法链 ----
        CheckResult.Close("用户 B07：U_B=12·10/43→2.79 V", 2.79, StepValue(solution, "UB"), 0.01);
        CheckResult.Close("用户 B07：I_EQ=(U_B−0.7)/Re=2.09 mA", 2.09e-3, StepValue(solution, "IEQ"), 0.01);
        CheckResult.Close("用户 B07：I_BQ=I_EQ/(1+β)=20.69 μA", 2.069e-5, StepValue(solution, "IBQ"), 0.01);
        CheckResult.Close("用户 B07：I_CQ=β·I_BQ=2.07 mA", 2.07e-3, StepValue(solution, "ICQ"), 0.01);
        CheckResult.Close("用户 B07：U_CEQ=12−I_CQ·Rc−I_EQ·Re=7.84 V", 7.84, StepValue(solution, "VCEQ"), 0.01);
        CheckResult.Close("用户 B07：rbe=200+101·26mV/2.09mA→1.46 kΩ", 1460.0, StepValue(solution, "rbe"), 0.01);
        CheckResult.Close("用户 B07：Av=−β(Rc∥RL)/rbe→−34.25", -34.25, StepValue(solution, "Av"), 0.01);
        CheckResult.Close("用户 B07：Ri=Rb1∥Rb2∥rbe→1.23 kΩ", 1230.0, StepValue(solution, "Ri"), 0.01);
        CheckResult.Close("用户 B07：Ro=Rc=1 kΩ", 1000.0, StepValue(solution, "Ro"), 0.01);
        CheckResult.Close("用户 B07：Q 点面板 IBQ 显示值", 2.069e-5, TestQuantity.Parse(solution.QPoint?.Ibq), 0.01);
        CheckResult.Close("用户 B07：Q 点面板 ICQ 显示值", 2.07e-3, TestQuantity.Parse(solution.QPoint?.Icq), 0.01);
        CheckResult.Close("用户 B07：Q 点面板 VCEQ 显示值", 7.84, TestQuantity.Parse(solution.QPoint?.Vceq), 0.01);

        // ---- 信号源内阻 Rs 与源电压放大倍数 Aus ----
        CheckResult.Check(solution.AcParameters != null && solution.AcParameters.HasSourceResistance,
            "用户 B07：AcParameters.HasSourceResistance=true", solution.AcParameters?.HasSourceResistance.ToString() ?? "null");
        CheckResult.Close("用户 B07：SourceResistance 显示 50 Ω", 50.0,
            TestQuantity.Parse(solution.AcParameters?.SourceResistance), 0.01);
        CheckResult.Close("用户 B07：SourceVoltageGain 显示 −32.91", -32.91,
            TestQuantity.Parse(solution.AcParameters?.SourceVoltageGain), 0.01);
        CheckResult.Close("用户 B07：Values Rs_ohm=50", 50.0, StepValue(solution, "Rs_ohm"), 1e-9);
        CheckResult.Close("用户 B07：Values Aus=−32.91", -32.91, StepValue(solution, "Aus"), 0.01);
        CheckResult.TextContains("用户 B07：摘要写明 Rs 与 Aus=Av·Ri/(Rs+Ri)", solution.SummaryText,
            "信号源内阻 Rs = 50 Ω", "Aus = Av·Ri/(Rs+Ri) = -32.91");
        CheckSharedDisplayConventions(solution);

        // ---- 下限截止频率包含 (Rs+Ri) ----
        CheckResult.Close("用户 B07：fL 含 τ1=(Rs+Ri)C1 →41.7 Hz", 41.7, StepValue(solution, "fL"), 0.01);
        CheckResult.TextContains("用户 B07：交流指标面板 fL 已显示（含 Rs 时间常数）",
            solution.AcParameters?.LowerCutoffFreq, "Hz");

        // ---- 电解电容 C3 极性核验步骤归 DC 相 ----
        CheckResult.Check(solution.Steps.Count == 6,
            "用户 B07：步骤 = 5 教学步 + 1 个 C3 电解极性核验步 = 6", $"实际 {solution.Steps.Count}");
        CheckResult.Check(solution.Steps.Count(step => step.Phase == SolutionPhase.DcEquivalent) == 2 &&
                          solution.Steps.Count(step => step.Phase == SolutionPhase.AcEquivalent) == 4,
            "用户 B07：DC 相 2 步（Q 点 + 极性核验）、AC 相 4 步",
            $"DC={solution.Steps.Count(s => s.Phase == SolutionPhase.DcEquivalent)}, AC={solution.Steps.Count(s => s.Phase == SolutionPhase.AcEquivalent)}");
        bool sequential = solution.Steps.Select((step, index) => step.StepIndex == index + 1).All(ok => ok);
        CheckResult.Check(sequential, "用户 B07：StepIndex 连续重排",
            string.Join(",", solution.Steps.Select(s => s.StepIndex)));
        PedagogicalSolutionStep? polarityStep = solution.Steps.FirstOrDefault(s => s.BadgeText is "元件特性" or "极性安全");
        CheckResult.Check(polarityStep != null && polarityStep.Phase == SolutionPhase.DcEquivalent &&
                          polarityStep.StepIndex == 2,
            "用户 B07：C3 极性核验步骤紧跟 Q 点（StepIndex=2）且 Phase=DC",
            polarityStep == null ? "missing" : $"{polarityStep.StepIndex}/{polarityStep.Phase}/{polarityStep.BadgeText}");
        CheckResult.TextContains("用户 B07：C3 极性正常（+ 在基极侧 2.64 V）",
            solution.SummaryText, "电解电容 C3 正偏正常");

        // ---- 步骤 3 的 Av 步骤附带 Aus 公式/代入/结果 ----
        PedagogicalSolutionStep? avStep = solution.Steps.FirstOrDefault(s => s.StepIndex == 4);
        CheckResult.Check(avStep != null && avStep.LatexFormula.Contains("A_{us}", StringComparison.Ordinal),
            "用户 B07：Av 步骤（第 4 步）公式附带 A_us=Av·Ri/(Rs+Ri)",
            avStep?.LatexFormula ?? "missing");
        CheckResult.Check(avStep != null && avStep.SubstitutionFormula.Contains("A_{us}", StringComparison.Ordinal) &&
                          avStep.ResultValue.Contains("Aus = -32.91", StringComparison.Ordinal),
            "用户 B07：Av 步骤代入 Aus 数值且结果文案含 Aus",
            avStep == null ? "missing" : $"{avStep.SubstitutionFormula} | {avStep.ResultValue}");

        // ---- StepGroups DC(2)/AC(4) ----
        AnalysisResult result = solution.ToAnalysisResult();
        CheckResult.Check(result.StepGroups.Count == 2 &&
                          result.StepGroups[0].Phase == SolutionPhase.DcEquivalent && result.StepGroups[0].Steps.Count == 2 &&
                          result.StepGroups[1].Phase == SolutionPhase.AcEquivalent && result.StepGroups[1].Steps.Count == 4,
            "用户 B07：StepGroups 分组 DC(2)/AC(4)",
            string.Join(" | ", result.StepGroups.Select(g => $"{g.Phase}:{g.Steps.Count}")));

        // ---- 显示文本口径：≤2 位小数、无旧双口径文案 ----
        CheckResult.AssertDisplayConventions("用户 B07", solution);
    }

    // =====================================================================
    // 2) Same topology without Rs (R6 removed, C3 replaced by normal C1):
    //    previous behaviour must be preserved.
    // =====================================================================
    private static void CheckNoSourceResistanceControl()
    {
        ElectricalNetlist netlist = BuildUserB07(withSourceResistance: false, reverseInputCap: false);
        PedagogicalSolution solution = TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess, "无 Rs 对照：求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Check(solution.AcParameters != null && !solution.AcParameters.HasSourceResistance,
            "无 Rs 对照：HasSourceResistance=false", solution.AcParameters?.HasSourceResistance.ToString() ?? "null");
        CheckResult.Check(solution.AcParameters?.SourceResistance == "—" &&
                          solution.AcParameters?.SourceVoltageGain == "—",
            "无 Rs 对照：SourceResistance/SourceVoltageGain 保持默认不显示",
            $"{solution.AcParameters?.SourceResistance} / {solution.AcParameters?.SourceVoltageGain}");
        CheckResult.Check(!solution.Steps[0].Values.ContainsKey("Rs_ohm") &&
                          !solution.Steps[0].Values.ContainsKey("Aus"),
            "无 Rs 对照：Values 不含 Rs_ohm/Aus 稳定键", string.Join(",", solution.Steps[0].Values.Keys));
        CheckResult.Close("无 Rs 对照：Av 与上一轮一致（−34.25）", -34.25, StepValue(solution, "Av"), 0.01);
        CheckResult.Close("无 Rs 对照：Ri 与上一轮一致（1.23 kΩ）", 1230.0, StepValue(solution, "Ri"), 0.01);
        // fL 不含 Rs：fl1=1/(2π·Ri·C1)=2.75 Hz, fl2=7.96 Hz, fl_e=40.85 Hz → 41.71 Hz。
        CheckResult.Close("无 Rs 对照：fL=√(2.75²+7.96²+40.85²)≈41.71 Hz", 41.71, StepValue(solution, "fL"), 0.01);
        CheckResult.Check(solution.Steps.Count == 5 &&
                          solution.Steps.Count(s => s.Phase == SolutionPhase.DcEquivalent) == 1,
            "无 Rs 对照：普通电容时不生成极性核验步（5 步，DC 1 步）",
            $"count={solution.Steps.Count}, dc={solution.Steps.Count(s => s.Phase == SolutionPhase.DcEquivalent)}");
        PedagogicalSolutionStep? avStep = solution.Steps.FirstOrDefault(s => s.StepIndex == 3);
        CheckResult.Check(avStep != null && !avStep.LatexFormula.Contains("A_{us}", StringComparison.Ordinal) &&
                          !avStep.ResultValue.Contains("Aus", StringComparison.Ordinal),
            "无 Rs 对照：Av 步骤不含 Aus 文案", avStep?.ResultValue ?? "missing");
        CheckResult.TextNotContains("无 Rs 对照：摘要不显示 Aus", solution.SummaryText, "源电压放大倍数");
        CheckResult.AssertDisplayConventions("无 Rs 对照", solution);
    }

    // =====================================================================
    // 3) Uncovered resistor branch → diagnostic rejection naming R9 + nets.
    // =====================================================================
    private static void CheckUncoveredResistorDiagnostic()
    {
        ElectricalNetlist netlist = BuildUserB07(withSourceResistance: true, reverseInputCap: false, extraResistor: true);
        PedagogicalSolution solution = TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(!solution.IsSuccess, "未覆盖电阻：明确拒绝求解（不静默忽略）", solution.SummaryText);
        CheckResult.TextContains("未覆盖电阻：失败文案含“未覆盖”+ 元件 Id R9 + 两端网名",
            solution.SummaryText, "未覆盖", "R9", "（N2–N4）");
        CheckResult.TextContains("未覆盖电阻：失败文案列出当前支持的电阻位置（Rc/Re/Rb1/Rb2/RL/Rs）",
            solution.SummaryText, "Rc（集电极–电源轨）", "Re（发射极–参考轨）", "Rb1/Rb2", "RL（输出耦合电容之后的负载）",
            "Rs（交流信号源与输入耦合电容之间的信号源内阻");
    }

    // =====================================================================
    // 4) Reverse C3 (+ toward the source side, – toward the base) → blocked
    //    polarity step still belongs to the DC phase.
    // =====================================================================
    private static void CheckElectrolyticPolarityReverse()
    {
        ElectricalNetlist netlist = BuildUserB07(withSourceResistance: true, reverseInputCap: true);
        PedagogicalSolution solution = TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckReverseBlocked("反接 C3", solution);
        CheckResult.TextContains("反接 C3：摘要含“极性反接”警告", solution.SummaryText, "极性反接");
        PedagogicalSolutionStep? warningStep = solution.Steps.FirstOrDefault(s => s.BadgeText == "极性安全");
        CheckResult.Check(warningStep != null && warningStep.Status == "Error" &&
                          warningStep.Phase == SolutionPhase.DcEquivalent,
            "反接 C3：极性核验步骤 Status=Error 且 Phase=DC",
            warningStep == null ? "missing" : $"{warningStep.Status}/{warningStep.Phase}");
        CheckResult.Check(solution.Steps.Count == 2 &&
                          solution.Steps.Count(s => s.Phase == SolutionPhase.DcEquivalent) == 2,
            "反接 C3：仅保留 DC 两步",
            $"count={solution.Steps.Count}, dc={solution.Steps.Count(s => s.Phase == SolutionPhase.DcEquivalent)}");
        CheckResult.AssertDisplayConventions("反接 C3", solution);
    }

    private static void CheckReverseBlocked(string name, PedagogicalSolution solution)
    {
        CheckResult.Check(!solution.IsSuccess, name + "：反接阻止成功状态", solution.SummaryText);
        CheckResult.Check(solution.QPoint != null && solution.Steps.Any(s => s.Phase == SolutionPhase.DcEquivalent),
            name + "：保留直流偏置诊断");
        CheckResult.Check(solution.AcParameters == null && !solution.Steps.Any(s => s.Phase == SolutionPhase.AcEquivalent),
            name + "：不输出交流指标或交流推导");
        CheckResult.Check(!solution.SummaryText.Contains("求解完成") && solution.SummaryText.Contains("停止交流分析"),
            name + "：摘要说明停止交流分析", solution.SummaryText);
        var display = solution.ToAnalysisResult();
        CheckResult.Check(!display.IsSuccess && display.AcParameters.VoltageGain == "—" &&
            display.StepGroups.All(g => g.Phase != SolutionPhase.AcEquivalent), name + "：界面映射不保留成功或交流结果");
        CheckResult.Check(solution.Steps.SelectMany(s => s.Values.Keys).All(k => k is not ("Av" or "Aus" or "fL" or "fH" or "BW")),
            name + "：诊断数据不夹带交流增益和频率");
    }

    // =====================================================================
    // 5a) Common base + Rs: Aus = Au·ri'/(Rs+ri') must be self-consistent.
    // =====================================================================
    private static void CheckCommonBaseWithSourceResistance()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("R6", ElementType.Resistor, 50.0, "Ω");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rc", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Rb1", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 50.0, "kΩ");
        builder.Element("Cb", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("R6", "1", 6).Pin("R6", "2", 5)
               .Pin("C1", "1", 6).Pin("C1", "2", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Cb", "1", 2).Pin("Cb", "2", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4);
        ElectricalNetlist netlist = builder.Build();

        PedagogicalSolution solution = TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess && solution.SubtypeName == "CommonBase",
            "共基 + Rs：求解成功且子类型 CommonBase", solution.SummaryText);
        if (!solution.IsSuccess) return;
        CheckResult.Check(solution.AcParameters?.HasSourceResistance == true,
            "共基 + Rs：HasSourceResistance=true", solution.AcParameters?.HasSourceResistance.ToString() ?? "null");
        CheckResult.Close("共基 + Rs：Values Rs_ohm=50", 50.0, StepValue(solution, "Rs_ohm"), 1e-9);
        double au = TestQuantity.Parse(solution.AcParameters?.VoltageGain);
        double ri = TestQuantity.Parse(solution.AcParameters?.InputResistance);
        double aus = TestQuantity.Parse(solution.AcParameters?.SourceVoltageGain);
        CheckResult.Close("共基 + Rs：Aus=Au·ri'/(Rs+ri')（与显示值自洽）", au * ri / (50.0 + ri), aus, 0.01);
        PedagogicalSolutionStep? auStep = solution.Steps.FirstOrDefault(s => s.StepIndex == 3);
        CheckResult.Check(auStep != null && auStep.LatexFormula.Contains("A_{us}", StringComparison.Ordinal) &&
                          auStep.ResultValue.Contains("Aus", StringComparison.Ordinal),
            "共基 + Rs：Au 步骤附带 Aus 公式与结果", auStep?.ResultValue ?? "missing");
        CheckResult.AssertDisplayConventions("共基 + Rs", solution);
        ReplaceWithReversedCapacitor(netlist, "C1", 6, 4);
        CheckReverseBlocked("共基输入电容反接", TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult());
    }

    // =====================================================================
    // 5b) Common collector + Rs: Aus = Av·Ri/(Rs+Ri) must be self-consistent.
    // =====================================================================
    private static void CheckCommonCollectorWithSourceResistance()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("R6", ElementType.Resistor, 50.0, "Ω");
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
               .Pin("R6", "1", 6).Pin("R6", "2", 5)
               .Pin("C1", "1", 6).Pin("C1", "2", 2)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 1).Pin("Q1", "E", 3)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 3).Pin("Re", "2", 0)
               .Pin("C2", "1", 3).Pin("C2", "2", 4)
               .Pin("RL", "1", 4).Pin("RL", "2", 0);
        ElectricalNetlist netlist = builder.Build();

        PedagogicalSolution solution = TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
        CheckResult.Check(solution.IsSuccess && solution.SubtypeName == "CommonCollector",
            "共集 + Rs：求解成功且子类型 CommonCollector", solution.SummaryText);
        if (!solution.IsSuccess) return;
        CheckResult.Check(solution.AcParameters?.HasSourceResistance == true,
            "共集 + Rs：HasSourceResistance=true", solution.AcParameters?.HasSourceResistance.ToString() ?? "null");
        CheckResult.Close("共集 + Rs：Values Rs_ohm=50", 50.0, StepValue(solution, "Rs_ohm"), 1e-9);
        double av = TestQuantity.Parse(solution.AcParameters?.VoltageGain);
        double ri = TestQuantity.Parse(solution.AcParameters?.InputResistance);
        double aus = TestQuantity.Parse(solution.AcParameters?.SourceVoltageGain);
        CheckResult.Check(av > 0 && av < 1, "共集 + Rs：0<Av<1（射极跟随）", CheckResult.Format(av));
        CheckResult.Close("共集 + Rs：Aus=Av·Ri/(Rs+Ri)（与显示值自洽）", av * ri / (50.0 + ri), aus, 0.01);
        PedagogicalSolutionStep? avStep = solution.Steps.FirstOrDefault(s => s.StepIndex == 3);
        CheckResult.Check(avStep != null && avStep.LatexFormula.Contains("A_{us}", StringComparison.Ordinal) &&
                          avStep.ResultValue.Contains("Aus", StringComparison.Ordinal),
            "共集 + Rs：Av 步骤附带 Aus 公式与结果", avStep?.ResultValue ?? "missing");
        CheckResult.AssertDisplayConventions("共集 + Rs", solution);
        ReplaceWithReversedCapacitor(netlist, "C2", 4, 3);
        CheckReverseBlocked("共集输出电容反接", TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult());
    }

    private static void ReplaceWithReversedCapacitor(ElectricalNetlist netlist, string id, int positiveNet, int negativeNet)
    {
        netlist.Elements[id].Type = ElementType.CapacitorPolarized;
        foreach (var net in netlist.Nets) net.ConnectedPins.RemoveAll(p => p.ElementId == id);
        netlist.Nets.Single(n => n.NetId == positiveNet).ConnectedPins.Add(new ComponentPinRef(id, "+", "+"));
        netlist.Nets.Single(n => n.NetId == negativeNet).ConnectedPins.Add(new ComponentPinRef(id, "-", "-"));
    }

    // =====================================================================
    // Netlist fixture: the user's edited B07 + no-Rs control + extra R9.
    //   vs(10 mV) — R6(50 Ω) — C3(47 µF 电解, + at base) — 基极
    //   Rb1=33k(B–VCC), Rb2=10k(B–GND), Rc=1k(C–VCC), Re=1k(E–GND),
    //   Ce=47 µF 旁路, C2=10 µF 输出耦合, RL=1k, VCC=12 V, β=100.
    // =====================================================================
    private static ElectricalNetlist BuildUserB07(bool withSourceResistance, bool reverseInputCap, bool extraResistor = false)
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("vs", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 33.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 10.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Ce", ElementType.Capacitor, 47.0, "μF");
        builder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        builder.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        builder.Net(1, "VCC", isPower: true);
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("vs", "pos", 5).Pin("vs", "neg", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Ce", "1", 4).Pin("Ce", "2", 0)
               .Pin("C2", "1", 3).Pin("C2", "2", 7)
               .Pin("RL", "1", 7).Pin("RL", "2", 0);
        if (withSourceResistance)
        {
            builder.Element("R6", ElementType.Resistor, 50.0, "Ω");
            builder.Element("C3", ElementType.CapacitorPolarized, 47.0, "μF");
            builder.Pin("R6", "1", 6).Pin("R6", "2", 5);
            if (reverseInputCap)
            {
                // 反接：+ 接信号源侧、− 接基极。
                builder.Pin("C3", "+", 6).Pin("C3", "-", 2);
            }
            else
            {
                // 用户布局：+ 在基极侧、− 接 R6。
                builder.Pin("C3", "+", 2).Pin("C3", "-", 6);
            }
        }
        else
        {
            // 对照：去掉 R6，用普通 47 µF 输入耦合电容 C1 直接接交流源。
            builder.Element("C1", ElementType.Capacitor, 47.0, "μF");
            builder.Pin("C1", "1", 5).Pin("C1", "2", 2);
        }
        if (extraResistor)
        {
            // 基极与发射极之间的额外电阻（教学模型未覆盖的支路，不影响已验证支路）。
            builder.Element("R9", ElementType.Resistor, 10.0, "kΩ");
            builder.Pin("R9", "1", 2).Pin("R9", "2", 4);
        }
        return builder.Build();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static double StepValue(PedagogicalSolution solution, string key)
    {
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            if (step.Values.TryGetValue(key, out string? raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                return value;
        }
        return double.NaN;
    }

    /// <summary>Display conventions shared by the Rs scenarios.</summary>
    private static void CheckSharedDisplayConventions(PedagogicalSolution solution)
    {
        CheckResult.TextNotContains("用户 B07：显示文本不含旧双口径文案", solution.SummaryText,
            "戴维南", "教材估算对照");
        CheckResult.Check(solution.Steps.First().Values.ContainsKey("UB") &&
                          solution.Steps.First().Values.ContainsKey("Rs_ohm"),
            "用户 B07：第 1 步 Values 同时含教材 Q 点键与 Rs_ohm/Aus 键",
            string.Join(",", solution.Steps.First().Values.Keys));
        CheckResult.Check(!string.IsNullOrWhiteSpace(solution.QPoint?.ExactIbq),
            "用户 B07：后端 Exact* 精确值仍保留", solution.QPoint?.ExactIbq ?? "null");
    }
}
