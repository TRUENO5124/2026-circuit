// ============================================================================
// File: tests/NonlinearCircuitChecks/BjtSmallSignalParameterChecks.cs
// OWNER: engine-bjt.
//
// Round-9 user feedback (2026-10-18): textbook problems often give the BJT
// small-signal parameters r_bb' and/or r_be (h_ie) directly, so they must be
// adjustable per transistor:
//   * BjtRbb  = r_bb' in Ω; 0 keeps the previous teaching default (200 Ω for
//     common emitter / common collector, 50 Ω for common base).  A positive
//     value is used in r_be = r_bb' + (1+β)·U_T/I_EQ.
//   * BjtRbe  = directly-given r_be = h_ie in Ω; 0 means "compute from Q point".
//     A positive value overrides the formula and is used as-is downstream.
//   * invalid values (negative / NaN / Infinity) fail explicitly.
//   * every step's Values gains rbb_ohm / rbe_ohm / rbe_source (computed|given);
//     common base additionally keeps its hie key.
//
// The tests below hand-check the textbook chain (all display values rounded to
// two decimals in their engineering unit and fed into the next formula).
// ============================================================================

using System;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class BjtSmallSignalParameterChecks
{
    public static void Run()
    {
        CheckResult.Section("BJT 小信号参数 r_bb'/r_be(h_ie) 可调（默认自动 / 自定义 rbb / 题目给定 rbe / 非法值）");
        CheckCommonEmitterDefault();
        CheckCommonEmitterCustomRbb();
        CheckCommonEmitterGivenRbe();
        CheckCommonBaseParameters();
        CheckCommonCollectorParameters();
        CheckInvalidParameters();
    }

    // =====================================================================
    // 1) Common emitter (voltage-divider bias, no bypass): default automatic.
    //    VCC=12 V, Rb1=100k, Rb2=50k, Rc=2.2k, Re=1k, β=100.
    //    U_B=4 V; I_EQ=3.3 mA; I_BQ=32.67 µA; I_CQ=3.27 mA; U_CEQ=1.51 V.
    //    r_be = 200 + 101·26 mV/3.3 mA = 995.757… → 995.76 Ω (display Ω, 2 dp).
    //    R_i  = Rb1∥Rb2∥[r_be+(1+β)Re] = 25.12 kΩ; A_v = −β·Rc/[r_be+101·Re] = −2.16.
    // =====================================================================
    private static void CheckCommonEmitterDefault()
    {
        ElectricalNetlist netlist = BuildCommonEmitterDivider();
        PedagogicalSolution solution = Solve(netlist);
        CheckResult.Check(solution.IsSuccess, "默认自动：分压共射求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Close("默认自动：rbb_ohm = 200（教材默认）", 200.0, StepValue(solution, "rbb_ohm"), 1e-9);
        CheckResult.Close("默认自动：rbe_ohm = 995.76（r_bb'+(1+β)U_T/I_EQ 舍入）", 995.76, StepValue(solution, "rbe_ohm"), 1e-9);
        CheckResult.Check(StepValueText(solution, "rbe_source") == "computed",
            "默认自动：rbe_source = computed", StepValueText(solution, "rbe_source"));
        PedagogicalSolutionStep? rbeStep = StepByIndex(solution, 2);
        CheckResult.TextContains("默认自动：rbe 步骤文案显示实际 r_bb' 值", rbeStep?.Content, "r_bb' = 200 Ω");
        CheckResult.TextContains("默认自动：rbe 步骤代入公式用实际 rbb 值（非硬编码）",
            rbeStep?.SubstitutionFormula, "200", "26");
        CheckResult.Close("默认自动：Av=−2.16", -2.16, StepValue(solution, "Av"), 1e-3);
        CheckResult.Close("默认自动：Ri=25.12 kΩ", 25120.0, StepValue(solution, "Ri"), 1e-3);
        CheckResult.AssertDisplayConventions("默认自动", solution);
    }

    // =====================================================================
    // 2) Common emitter with custom r_bb' = 300 Ω.
    //    r_be = 300 + 101·26 mV/3.3 mA = 1095.757… → 1.1 kΩ (1100 Ω);
    //    A_v = −β·Rc/[rbe+101·Re] = −2.15; R_i = 25.13 kΩ.
    // =====================================================================
    private static void CheckCommonEmitterCustomRbb()
    {
        ElectricalNetlist netlist = BuildCommonEmitterDivider(rbb: 300.0);
        PedagogicalSolution solution = Solve(netlist);
        CheckResult.Check(solution.IsSuccess, "自定义 rbb=300：分压共射求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Close("自定义 rbb=300：rbb_ohm = 300", 300.0, StepValue(solution, "rbb_ohm"), 1e-9);
        CheckResult.Close("自定义 rbb=300：rbe_ohm = 1100（300+795.76 舍入到 1.1 kΩ）", 1100.0, StepValue(solution, "rbe_ohm"), 1e-9);
        CheckResult.Check(StepValueText(solution, "rbe_source") == "computed",
            "自定义 rbb=300：rbe_source = computed", StepValueText(solution, "rbe_source"));
        PedagogicalSolutionStep? rbeStep = StepByIndex(solution, 2);
        CheckResult.TextContains("自定义 rbb=300：步骤文案体现 300 Ω", rbeStep?.Content, "r_bb' = 300 Ω");
        CheckResult.TextContains("自定义 rbb=300：代入公式使用 300 Ω", rbeStep?.SubstitutionFormula, "300");
        CheckResult.Check(rbeStep != null && !rbeStep.Content.Contains("r_bb' = 200", StringComparison.Ordinal) &&
                          !rbeStep.SubstitutionFormula.Contains("200", StringComparison.Ordinal),
            "自定义 rbb=300：不再出现硬编码 200 Ω 文案", rbeStep?.Content ?? "missing");
        CheckResult.Close("自定义 rbb=300：Av=−2.15", -2.15, StepValue(solution, "Av"), 1e-3);
        CheckResult.Close("自定义 rbb=300：Ri=25.13 kΩ", 25130.0, StepValue(solution, "Ri"), 1e-3);
        CheckResult.AssertDisplayConventions("自定义 rbb=300", solution);
    }

    // =====================================================================
    // 3) Common emitter with directly-given r_be = h_ie = 1500 Ω.
    //    A_v = −β·Rc/[1500+101·Re] = −2.15; R_i = (Rb1∥Rb2)∥(1500+101·Re) = 25.15 kΩ;
    //    the given value must be used as-is (rbe_ohm = 1500, rbe_source = given) and
    //    differ from the computed chain.
    // =====================================================================
    private static void CheckCommonEmitterGivenRbe()
    {
        ElectricalNetlist netlist = BuildCommonEmitterDivider(rbe: 1500.0);
        PedagogicalSolution solution = Solve(netlist);
        CheckResult.Check(solution.IsSuccess, "题目给定 rbe=1500：分压共射求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Close("题目给定 rbe=1500：rbe_ohm = 1500（原值直接采用）", 1500.0, StepValue(solution, "rbe_ohm"), 1e-9);
        CheckResult.Check(StepValueText(solution, "rbe_source") == "given",
            "题目给定 rbe=1500：rbe_source = given", StepValueText(solution, "rbe_source"));
        CheckResult.Close("题目给定 rbe=1500：rbb 仍回落到默认 200", 200.0, StepValue(solution, "rbb_ohm"), 1e-9);
        PedagogicalSolutionStep? rbeStep = StepByIndex(solution, 2);
        CheckResult.TextContains("题目给定 rbe=1500：文案含“题目给定/直接采用”", rbeStep?.Content, "题目给定", "直接采用");
        CheckResult.TextContains("题目给定 rbe=1500：公式为 r_be = h_ie", rbeStep?.LatexFormula, "h_{ie}");
        CheckResult.TextContains("题目给定 rbe=1500：代入显示给定值", rbeStep?.ResultValue, "1.5 kΩ", "题目给定");
        CheckResult.Close("题目给定 rbe=1500：Av=−2.15（按给定 rbe 计算）", -2.15, StepValue(solution, "Av"), 1e-3);
        CheckResult.Close("题目给定 rbe=1500：Ri=25.15 kΩ（按给定 rbe 计算）", 25150.0, StepValue(solution, "Ri"), 1e-3);
        double computedRbe = 995.76;
        CheckResult.Check(Math.Abs(StepValue(solution, "rbe_ohm") - computedRbe) > 400.0,
            "题目给定 rbe=1500：与 computed 情形的 rbe 明显不同（可区分）",
            $"given={CheckResult.Format(StepValue(solution, "rbe_ohm"))}, computed≈{computedRbe}");
        CheckResult.AssertDisplayConventions("题目给定 rbe=1500", solution);
    }

    // =====================================================================
    // 4) Common base: default r_bb'=50 Ω; custom 100 Ω; given h_ie=1200 Ω.
    //    VCC=12, Rb1=100k, Rb2=50k, Rc=2k, Re=2k, β=100, Cb/C1=10 µF.
    //    U_B=4 V; I_EQ=1.65 mA; I_BQ=16.34 µA; I_CQ=1.63 mA; U_CEQ=5.44 V.
    //    default h_ie = 50 + 101·26 mV/1.65 mA = 1641.5… → 1.64 kΩ;
    //    custom  h_ie = 100 + … = 1691.5… → 1.69 kΩ;
    //    given   h_ie = 1200 Ω → h_ib = 1200/101 = 11.88 Ω; Au = 100·2k/1200 = 166.67.
    // =====================================================================
    private static void CheckCommonBaseParameters()
    {
        PedagogicalSolution defaultSolution = Solve(BuildCommonBase(rbb: 0, rbe: 0));
        CheckResult.Check(defaultSolution.IsSuccess, "共基默认自动：求解成功", defaultSolution.SummaryText);
        if (defaultSolution.IsSuccess)
        {
            CheckResult.Close("共基默认：rbb_ohm = 50（共基教材默认）", 50.0, StepValue(defaultSolution, "rbb_ohm"), 1e-9);
            CheckResult.Close("共基默认：hie = 1640", 1640.0, StepValue(defaultSolution, "hie"), 1e-9);
            CheckResult.Close("共基默认：rbe_ohm = hie = 1640", 1640.0, StepValue(defaultSolution, "rbe_ohm"), 1e-9);
            CheckResult.Check(StepValueText(defaultSolution, "rbe_source") == "computed",
                "共基默认：rbe_source = computed", StepValueText(defaultSolution, "rbe_source"));
            PedagogicalSolutionStep? step2 = StepByIndex(defaultSolution, 2);
            CheckResult.TextContains("共基默认：h_ie 步骤文案体现 50 Ω", step2?.Content, "r_bb' = 50 Ω");
            CheckResult.AssertDisplayConventions("共基默认", defaultSolution);
        }

        PedagogicalSolution customSolution = Solve(BuildCommonBase(rbb: 100.0, rbe: 0));
        CheckResult.Check(customSolution.IsSuccess, "共基自定义 rbb=100：求解成功", customSolution.SummaryText);
        if (customSolution.IsSuccess)
        {
            CheckResult.Close("共基自定义 rbb=100：rbb_ohm = 100", 100.0, StepValue(customSolution, "rbb_ohm"), 1e-9);
            CheckResult.Close("共基自定义 rbb=100：hie = 1690", 1690.0, StepValue(customSolution, "hie"), 1e-9);
            PedagogicalSolutionStep? step2 = StepByIndex(customSolution, 2);
            CheckResult.Check(step2 != null && step2.Content.Contains("r_bb' = 100 Ω", StringComparison.Ordinal) &&
                              !step2.SubstitutionFormula.Contains("50", StringComparison.Ordinal),
                "共基自定义 rbb=100：文案/代入使用 100 Ω，不再出现硬编码 50 Ω",
                step2?.Content ?? "missing");
            CheckResult.Close("共基自定义 rbb=100：Au=118.34", 118.34, StepValue(customSolution, "Av"), 1e-3);
            CheckResult.AssertDisplayConventions("共基自定义 rbb=100", customSolution);
        }

        PedagogicalSolution givenSolution = Solve(BuildCommonBase(rbb: 0, rbe: 1200.0));
        CheckResult.Check(givenSolution.IsSuccess, "共基题目给定 h_ie=1200：求解成功", givenSolution.SummaryText);
        if (givenSolution.IsSuccess)
        {
            CheckResult.Close("共基题目给定 h_ie=1200：hie = 1200", 1200.0, StepValue(givenSolution, "hie"), 1e-9);
            CheckResult.Check(StepValueText(givenSolution, "rbe_source") == "given",
                "共基题目给定 h_ie=1200：rbe_source = given", StepValueText(givenSolution, "rbe_source"));
            CheckResult.Close("共基题目给定 h_ie=1200：rib = 1200/101 = 11.88", 11.88, StepValue(givenSolution, "hib"), 1e-9);
            CheckResult.Close("共基题目给定 h_ie=1200：Au = 100·2k/1200 = 166.67", 166.67, StepValue(givenSolution, "Av"), 1e-3);
            PedagogicalSolutionStep? step2 = StepByIndex(givenSolution, 2);
            CheckResult.TextContains("共基题目给定 h_ie=1200：文案含题目给定", step2?.Content, "题目给定", "直接采用");
            CheckResult.AssertDisplayConventions("共基题目给定 h_ie=1200", givenSolution);
        }
    }

    // =====================================================================
    // 5) Common collector: default r_bb'=200; custom 400 (R_o changes); given
    //    r_be=1500 (R_o changes, A_v uses the given value).
    //    VCC=12, Rb1=51k, Rb2=15k, Re=1k, RL=10k, β=100, C1/C2=10 µF.
    // =====================================================================
    private static void CheckCommonCollectorParameters()
    {
        PedagogicalSolution defaultSolution = Solve(BuildCommonCollector(rbb: 0, rbe: 0));
        CheckResult.Check(defaultSolution.IsSuccess, "共集默认自动：求解成功", defaultSolution.SummaryText);
        if (!defaultSolution.IsSuccess) return;
        CheckResult.Close("共集默认：rbb_ohm = 200（共射/共集教材默认）", 200.0, StepValue(defaultSolution, "rbb_ohm"), 1e-9);
        CheckResult.Close("共集默认：rbe_ohm = 1490", 1490.0, StepValue(defaultSolution, "rbe_ohm"), 1e-9);
        CheckResult.Close("共集默认：Ro=114.66", 114.66, StepValue(defaultSolution, "Ro"), 1e-3);
        CheckResult.AssertDisplayConventions("共集默认", defaultSolution);

        PedagogicalSolution customSolution = Solve(BuildCommonCollector(rbb: 400.0, rbe: 0));
        CheckResult.Check(customSolution.IsSuccess, "共集自定义 rbb=400：求解成功", customSolution.SummaryText);
        if (customSolution.IsSuccess)
        {
            CheckResult.Close("共集自定义 rbb=400：rbb_ohm = 400", 400.0, StepValue(customSolution, "rbb_ohm"), 1e-9);
            CheckResult.Close("共集自定义 rbb=400：rbe_ohm = 1690", 1690.0, StepValue(customSolution, "rbe_ohm"), 1e-9);
            CheckResult.Close("共集自定义 rbb=400：Ro=116.2（用新 rbe 折算）", 116.2, StepValue(customSolution, "Ro"), 1e-3);
            CheckResult.AssertDisplayConventions("共集自定义 rbb=400", customSolution);
        }

        PedagogicalSolution givenSolution = Solve(BuildCommonCollector(rbb: 0, rbe: 1500.0));
        CheckResult.Check(givenSolution.IsSuccess, "共集题目给定 rbe=1500：求解成功", givenSolution.SummaryText);
        if (givenSolution.IsSuccess)
        {
            CheckResult.Close("共集题目给定 rbe=1500：rbe_ohm = 1500", 1500.0, StepValue(givenSolution, "rbe_ohm"), 1e-9);
            CheckResult.Check(StepValueText(givenSolution, "rbe_source") == "given",
                "共集题目给定 rbe=1500：rbe_source = given", StepValueText(givenSolution, "rbe_source"));
            CheckResult.Check(StepByIndex(givenSolution, 2)?.Content.Contains("题目给定", StringComparison.Ordinal) == true,
                "共集题目给定 rbe=1500：文案含题目给定", StepByIndex(givenSolution, 2)?.Content ?? "missing");
            CheckResult.Close("共集题目给定 rbe=1500：Ro=114.73（用给定 rbe）", 114.73, StepValue(givenSolution, "Ro"), 1e-3);
            CheckResult.AssertDisplayConventions("共集题目给定 rbe=1500", givenSolution);
        }
    }

    // =====================================================================
    // 6) Invalid parameters: negative / NaN / infinity must fail explicitly.
    //    Note: Models/CircuitElement.cs sanitizes the editable properties (a
    //    negative / non-finite edit is reset to 0 to protect the inspector), so
    //    this check bypasses the property setter through reflection to simulate a
    //    corrupt value reaching the engine (legacy/imported data), then verifies
    //    the strategy's defensive branch fails gracefully instead of throwing.
    // =====================================================================
    private static void CheckInvalidParameters()
    {
        CircuitElement sanitized = BuildCommonEmitterDivider().Elements["Q1"];
        sanitized.BjtRbb = -1.0;
        sanitized.BjtRbe = double.NaN;
        CheckResult.Check(sanitized.BjtRbb == 0 && sanitized.BjtRbe == 0,
            "非法编辑在模型层被归零保护（UI 防线，Lead 模型行为）",
            $"{sanitized.BjtRbb}/{sanitized.BjtRbe}");

        CheckInvalidRaw("rbb=-1", "_bjtRbb", -1.0);
        CheckInvalidRaw("rbe=-5", "_bjtRbe", -5.0);
        CheckInvalidRaw("rbb=NaN", "_bjtRbb", double.NaN);
        CheckInvalidRaw("rbe=NaN", "_bjtRbe", double.NaN);
        CheckInvalidRaw("rbb=∞", "_bjtRbb", double.PositiveInfinity);
    }

    private static void CheckInvalidRaw(string label, string backingFieldName, double rawValue)
    {
        ElectricalNetlist netlist = BuildCommonEmitterDivider();
        CircuitElement q1 = netlist.Elements["Q1"];
        System.Reflection.FieldInfo? field = typeof(CircuitElement).GetField(
            backingFieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null)
        {
            CheckResult.Check(false, $"非法参数 {label}：可定位后端字段 {backingFieldName}", "反射失败");
            return;
        }
        field.SetValue(q1, rawValue);

        PedagogicalSolution solution = Solve(netlist);
        CheckResult.Check(!solution.IsSuccess, $"非法参数 {label}：明确失败（不抛异常）", solution.SummaryText);
        CheckResult.TextContains($"非法参数 {label}：失败文案含“有限非负”", solution.SummaryText, "有限非负");
        CheckResult.Check(solution.QPoint == null && solution.AcParameters == null && solution.Steps.Count == 0,
            $"非法参数 {label}：未输出 Q 点/交流参数/步骤");
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    /// <summary>Voltage-divider common emitter: VCC=12, Rb1=100k, Rb2=50k, Rc=2.2k, Re=1k, β=100.</summary>
    private static ElectricalNetlist BuildCommonEmitterDivider(double rbb = 0, double rbe = 0)
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
        ApplySmallSignalParameters(netlist, rbb, rbe);
        return netlist;
    }

    /// <summary>Common base NPN: VCC=12, Rb1=100k, Rb2=50k, Rc=2k, Re=2k, β=100, Cb/C1=10 µF.</summary>
    private static ElectricalNetlist BuildCommonBase(double rbb = 0, double rbe = 0)
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
        ApplySmallSignalParameters(netlist, rbb, rbe);
        return netlist;
    }

    /// <summary>Common collector NPN divider bias: VCC=12, Rb1=51k, Rb2=15k, Re=1k, RL=10k, β=100.</summary>
    private static ElectricalNetlist BuildCommonCollector(double rbb = 0, double rbe = 0)
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
        ApplySmallSignalParameters(netlist, rbb, rbe);
        return netlist;
    }

    private static void ApplySmallSignalParameters(ElectricalNetlist netlist, double rbb, double rbe)
    {
        CircuitElement q1 = netlist.Elements["Q1"];
        q1.BjtRbb = rbb;
        q1.BjtRbe = rbe;
    }

    private static PedagogicalSolution Solve(ElectricalNetlist netlist)
    {
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        return classification.Strategy.SolveAsync(netlist, new AnalysisParameters()).GetAwaiter().GetResult();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static PedagogicalSolutionStep? StepByIndex(PedagogicalSolution solution, int stepIndex)
    {
        return solution.Steps.FirstOrDefault(step => step.StepIndex == stepIndex);
    }

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

    private static string StepValueText(PedagogicalSolution solution, string key)
    {
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            if (step.Values.TryGetValue(key, out string? raw)) return raw;
        }
        return string.Empty;
    }
}
