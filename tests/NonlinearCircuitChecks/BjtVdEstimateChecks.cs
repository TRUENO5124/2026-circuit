// ============================================================================
// File: tests/NonlinearCircuitChecks/BjtVdEstimateChecks.cs
// OWNER: engine-bjt.
//
// Regression checks for the second-round user request (2026-10-18):
//   * the DISPLAYED solution of a BJT voltage-divider bias circuit follows the
//     textbook estimation method (教材估算法), i.e. the unloaded divider
//     U_B = V_CC·R_b2/(R_b1+R_b2) is used and every intermediate rounded
//     display value is fed into the next formula (textbook hand calculation);
//   * the Thévenin-exact solution is kept in the backend only (step Values
//     IBQ_exact_uA/… and QPointInfo.Exact*), never shown side by side;
//   * the derivation steps are grouped DC equivalent circuit → AC equivalent
//     circuit (SolutionPhase), with the DC phase first and contiguous.
//
// Textbook problem 3-12 (PNP voltage-divider bias): VCC=12 V, RB1=15 kΩ,
// RB2=470 kΩ, RE=1.3 kΩ, RC=2 kΩ, germanium V_EB=0.3 V, β=100.
// Textbook hand chain:
//   U_B = 12·470/485 = 11.6289… → 11.63 V
//   I_EQ = (12 − 11.63 − 0.3)/1.3 kΩ = 53.846… µA → 53.85 µA
//   I_BQ = 53.85 µA/(1+100) = 0.5331… µA → 0.53 µA
//   I_CQ = β·I_BQ = 100×0.53 µA = 53 µA
//   U_ECQ = 12 − 53 µA×2 kΩ − 53.85 µA×1.3 kΩ = 11.82399… → 11.82 V
// Exact Thévenin backend values: IBQ≈0.4878 µA, ICQ≈0.04878 mA, U_ECQ≈11.8384 V.
// ============================================================================

using System;
using System.Globalization;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class BjtVdEstimateChecks
{
    public static void Run()
    {
        CheckResult.Section("BJT 分压偏置：教材估算法显示口径（题3-12 场景）");

        // 按题3-12搭建 PNP 分压偏置网络：VCC=12 V，RB1=15k(B–VCC)，RB2=470k(B–GND)，
        // RE=1.3k(E–VCC)，RC=2k(C–GND)，PNP 锗管 VEB=0.3 V，β=100。
        ElectricalNetlist netlist = new TestNetlistBuilder()
            .Element("VCC1", ElementType.VccPort, 12.0, "V")
            .Element("RB1", ElementType.Resistor, 15.0, "kΩ")
            .Element("RB2", ElementType.Resistor, 470.0, "kΩ")
            .Element("RE", ElementType.Resistor, 1.3, "kΩ")
            .Element("RC", ElementType.Resistor, 2.0, "kΩ")
            .Element("Q1", ElementType.BjtPnp, 100.0, "β", material: SemiconductorMaterial.Germanium)
            .Element("GND1", ElementType.Ground, 0.0, "V")
            .Pin("VCC1", "VCC", 1).Pin("RB1", "1", 1).Pin("RE", "1", 1)
            .Pin("RE", "2", 2).Pin("Q1", "E", 2)
            .Pin("Q1", "B", 3).Pin("RB1", "2", 3).Pin("RB2", "1", 3)
            .Pin("Q1", "C", 4).Pin("RC", "1", 4)
            .Pin("RC", "2", 0).Pin("RB2", "2", 0).Pin("GND1", "GND", 0)
            .Net(0, "GND", isGround: true)
            .Net(1, "VCC", isPower: true)
            .Build();

        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        CheckResult.Check(classification.TopologyType == CircuitTopologyType.BjtAmplifier &&
                          classification.SubtypeName == "VoltageDividerSelfBias",
            "题3-12 网络分类为 BjtAmplifier/VoltageDividerSelfBias",
            $"{classification.TopologyType}/{classification.SubtypeName}");

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
        CheckResult.Check(solution.IsSuccess, "题3-12 网络求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        // 1) Q 点面板：教材估算法显示链（最多两位小数、单位规范）。
        CheckResult.Check(solution.QPoint?.Ibq == "0.53 μA",
            "Q 点 IBQ 显示为教材估算链 0.53 μA", solution.QPoint?.Ibq ?? "(null)");
        CheckResult.Check(solution.QPoint?.Icq == "53 μA",
            "Q 点 ICQ 显示为教材估算链 53 μA（=β·I_BQ，而非未舍入的 53.31 μA）", solution.QPoint?.Icq ?? "(null)");
        CheckResult.Check(solution.QPoint?.Vceq == "11.82 V",
            "Q 点 U_ECQ 显示为教材估算链 11.82 V", solution.QPoint?.Vceq ?? "(null)");
        CheckResult.Close("Q 点 IBQ 数值 = 0.53 μA", 5.3e-7, TestQuantity.Parse(solution.QPoint?.Ibq), 0.01);
        CheckResult.Close("Q 点 ICQ 数值 = 53 μA", 5.3e-5, TestQuantity.Parse(solution.QPoint?.Icq), 0.01);
        CheckResult.Close("Q 点 U_ECQ 数值 = 11.82 V", 11.82, TestQuantity.Parse(solution.QPoint?.Vceq), 0.01);
        CheckResult.Check(solution.QPoint?.WorkingRegion.Contains("放大区", StringComparison.Ordinal) == true &&
                          solution.QPoint?.WorkingRegion.Contains("PNP", StringComparison.Ordinal) == true,
            "Q 点工作区注明放大区与 PNP 极性约定", solution.QPoint?.WorkingRegion ?? "(null)");

        // 2) 摘要显示完整教材估算链：U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V。
        CheckResult.TextContains("摘要显示教材估算链 U_B=11.63 V、I_EQ=53.85 μA、I_BQ=0.53 μA、I_CQ=53 μA、U_ECQ=11.82 V",
            solution.SummaryText,
            "U_B=11.63 V", "I_EQ=53.85 μA", "I_BQ=0.53 μA", "I_CQ=53 μA", "U_ECQ=11.82 V");
        CheckResult.TextContains("摘要/推导注明来源为直流等效电路与交流等效电路",
            solution.SummaryText, "直流等效电路", "交流等效电路");

        // 3) 不得再出现“戴维南精确 vs 教材估算”双口径文案。
        CheckResult.TextNotContains("显示摘要不含双口径对照文案",
            solution.SummaryText, "戴维南", "教材估算对照", "精确解", "11.1%");

        // 4) 后端精确值：第 1 步 Values 保留戴维南精确解（≈0.488 µA / 0.049 mA / 11.838 V）。
        PedagogicalSolutionStep? step1 = solution.Steps.FirstOrDefault();
        CheckResult.Check(step1 != null, "存在第 1 步（直流等效电路 Q 点）步骤");
        if (step1 != null)
        {
            CheckResult.Check(step1.Values.ContainsKey("IBQ_exact_uA") &&
                              step1.Values.ContainsKey("ICQ_exact_mA") &&
                              step1.Values.ContainsKey("UCE_exact_V") &&
                              step1.Values.ContainsKey("Vth_V") &&
                              step1.Values.ContainsKey("Rth_ohm") &&
                              step1.Values.ContainsKey("UB_actual_V"),
                "第 1 步 Values 含全部后端精确键", string.Join(",", step1.Values.Keys));
            CheckResult.Close("后端精确 IBQ ≈ 0.4878 µA", 0.4878, StepValue(step1, "IBQ_exact_uA"), 0.005, " µA");
            CheckResult.Close("后端精确 ICQ ≈ 0.04878 mA", 0.04878, StepValue(step1, "ICQ_exact_mA"), 0.005, " mA");
            CheckResult.Close("后端精确 U_ECQ ≈ 11.8384 V", 11.8384, StepValue(step1, "UCE_exact_V"), 0.005, " V");
            CheckResult.Close("后端戴维南 Vth = 12·470/485 ≈ 11.6289 V", 11.6289, StepValue(step1, "Vth_V"), 0.005, " V");
            CheckResult.Close("后端戴维南 Rth = Rb1∥Rb2 ≈ 14.536 kΩ", 14536.1, StepValue(step1, "Rth_ohm"), 0.005, " Ω");
            CheckResult.Close("后端实际基极电位 U_B_actual ≈ 11.636 V", 11.636, StepValue(step1, "UB_actual_V"), 0.005, " V");

            // 5) 显示链的结构化 Values 也必须是舍入后回代的值。
            CheckResult.Near("第 1 步 Values U_B = 11.63 V", 11.63, StepValue(step1, "UB"), 1e-6);
            CheckResult.Near("第 1 步 Values I_EQ = 53.85 µA", 5.385e-5, StepValue(step1, "IEQ"), 1e-12);
            CheckResult.Near("第 1 步 Values I_BQ = 0.53 µA", 5.3e-7, StepValue(step1, "IBQ"), 1e-12);
            CheckResult.Near("第 1 步 Values I_CQ = β·I_BQ = 53 µA", 5.3e-5, StepValue(step1, "ICQ"), 1e-12);
            CheckResult.Near("第 1 步 Values U_ECQ = 11.82 V", 11.82, StepValue(step1, "VCEQ"), 1e-9);
        }

        // 6) QPointInfo.Exact* 后端字段非空且与戴维南精确值一致。
        CheckResult.Close("QPoint.ExactIbq ≈ 0.4878 µA（后端，不显示）", 0.4878e-6,
            TestQuantity.Parse(solution.QPoint?.ExactIbq), 0.005);
        CheckResult.Close("QPoint.ExactIcq ≈ 0.04878 mA（后端，不显示）", 0.04878e-3,
            TestQuantity.Parse(solution.QPoint?.ExactIcq), 0.005);
        CheckResult.Close("QPoint.ExactVceq ≈ 11.8384 V（后端，不显示）", 11.8384,
            TestQuantity.Parse(solution.QPoint?.ExactVceq), 0.005);

        // 7) DC/AC 相位分组：DC 步连续在前、AC 步连续在后，步骤数仍为 5。
        CheckResult.Check(solution.Steps.Count == 5, "BJT 教学步骤仍为 5 步", $"实际 {solution.Steps.Count}");
        CheckPhaseContract("题3-12 PNP 分压偏置共射", solution, expectedDcSteps: 1);

        // 8) ToAnalysisResult().StepGroups：两个分组（DC 段 1 步 / AC 段 4 步）。
        AnalysisResult result = solution.ToAnalysisResult();
        CheckResult.Check(result.StepGroups.Count == 2,
            "AnalysisResult.StepGroups 分为直流/交流两组", $"count={result.StepGroups.Count}");
        if (result.StepGroups.Count == 2)
        {
            CheckResult.Check(result.StepGroups[0].Phase == SolutionPhase.DcEquivalent &&
                              result.StepGroups[0].HasHeader &&
                              result.StepGroups[0].Title.Contains("直流等效电路", StringComparison.Ordinal) &&
                              result.StepGroups[0].Steps.Count == 1,
                "直流分组：标题/表头正确且只有 Q 点步骤",
                $"{result.StepGroups[0].Title}/{result.StepGroups[0].Steps.Count}");
            CheckResult.Check(result.StepGroups[1].Phase == SolutionPhase.AcEquivalent &&
                              result.StepGroups[1].HasHeader &&
                              result.StepGroups[1].Title.Contains("交流等效电路", StringComparison.Ordinal) &&
                              result.StepGroups[1].Steps.Count == 4,
                "交流分组：标题/表头正确且含 rbe/Av/RiRo/频率 4 步",
                $"{result.StepGroups[1].Title}/{result.StepGroups[1].Steps.Count}");
        }

        // 9) 显示文本数字小数位 ≤ 2（QPoint/AcParameters/SummaryText/步骤公式与结果）。
        CheckResult.AssertDisplayConventions("题3-12", solution);
    }

    /// <summary>Shared structural check: every step has a phase, DC steps are contiguous and come first.</summary>
    internal static void CheckPhaseContract(string name, PedagogicalSolution solution, int expectedDcSteps)
    {
        CheckResult.Check(solution.Steps.Count > 0, $"{name}：存在推导步骤");
        if (solution.Steps.Count == 0) return;

        bool allPhased = solution.Steps.All(step => step.Phase is SolutionPhase.DcEquivalent or SolutionPhase.AcEquivalent);
        CheckResult.Check(allPhased, $"{name}：每一步都标注 DC/AC 相位",
            string.Join(",", solution.Steps.Select(step => step.Phase)));

        bool sequential = solution.Steps.Select((step, index) => step.StepIndex == index + 1).All(ok => ok);
        CheckResult.Check(sequential, $"{name}：StepIndex 从 1 连续编号",
            string.Join(",", solution.Steps.Select(step => step.StepIndex)));

        var phases = solution.Steps.Select(step => step.Phase).ToList();
        int firstAc = phases.IndexOf(SolutionPhase.AcEquivalent);
        bool dcContiguousFirst = firstAc > 0 && phases.Take(firstAc).All(phase => phase == SolutionPhase.DcEquivalent) &&
                                 phases.Skip(firstAc).All(phase => phase == SolutionPhase.AcEquivalent);
        CheckResult.Check(dcContiguousFirst, $"{name}：直流步连续在前、交流步连续在后",
            string.Join("→", phases));
        CheckResult.Check(phases.Count(phase => phase == SolutionPhase.DcEquivalent) == expectedDcSteps,
            $"{name}：直流相位步骤数 = {expectedDcSteps}",
            $"实际 {phases.Count(phase => phase == SolutionPhase.DcEquivalent)}");

        PedagogicalSolutionStep firstDc = solution.Steps.First(step => step.Phase == SolutionPhase.DcEquivalent);
        CheckResult.TextContains($"{name}：DC 相第一步写明“画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络”",
            firstDc.Content, "画直流等效电路", "耦合/旁路电容开路", "交流信号源置零", "VCC/偏置网络");

        PedagogicalSolutionStep? firstAcStep = solution.Steps.FirstOrDefault(step => step.Phase == SolutionPhase.AcEquivalent);
        CheckResult.Check(firstAcStep != null, $"{name}：存在交流相位第一步");
        if (firstAcStep != null)
        {
            CheckResult.TextContains($"{name}：AC 相第一步写明“画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路、微变模型替代”",
                firstAcStep.Content, "画交流等效电路", "直流电源置零", "VCC 交流接地", "耦合/旁路电容短路", "微变模型替代");
        }
    }

    internal static double StepValue(PedagogicalSolutionStep step, string key)
    {
        return step.Values.TryGetValue(key, out string? raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.NaN;
    }
}
