// ============================================================================
// File: tests/NonlinearCircuitChecks/MosChecks.cs
// OWNER: engine-mos.
//
// MOSFET regression checks described in
// projects/nonlinear-engine-and-circuit-models-design-20261018.md §10:
//   * M01-M06 topologies (CommonSource / CommonSourceWithSourceResistance /
//     CommonSourceBypassed / CommonDrain / CommonGate, NMOS + PMOS) against
//     independent hand-computed square-law Q points and small-signal
//     Av/Ri/Ro (1 % relative tolerance);
//   * Level-1 device-model checks (parameter mapping, overdrive, regions,
//     saturation/triode currents, gm/gds, lambda handling, K default);
//   * PMOS polarity / |VDS| sign convention;
//   * failure paths: cutoff / triode-region operating point must fail
//     explicitly, depletion MOSFET / JFET share the five amplifier topology checks.
//
// Every numeric expectation below was derived by hand from the textbook
// square-law equations (not from the solver under test):
//   NMOS: Vov = VGS - Vth, ID = K*Vov^2 (saturation),
//   PMOS mirror: Vov = Vth - VGS = (VDD - VG) + Vth, ID = K*Vov^2.
//   With Rs: ID = K*(a - ID*Rs)^2, a = VG - Vth (NMOS) / VDD - VG + Vth (PMOS).
//   gm = 2*K*Vov, Av = -gm*(Rd||RL) etc. per contract section 5.2.
//
// Fixture parameters follow the M01-M06 definitions announced by the .circuit
// generator owner (circuits-mos): K = 2 mA/V^2, Vth = +1.5 V (NMOS) /
// -1.5 V (PMOS), VDD = 15 V (M01-M04, M06) and 12 V (M05).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Nonlinear;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class MosChecks
{
    private static readonly AnalysisParameters Parameters = new();

    /// <summary>Stable Values dictionary keys required by the round-6 contract.</summary>
    private static readonly string[] RequiredValueKeys = ["ID", "VGS", "VDS", "gm", "Av", "Ri", "Ro"];

    public static void Run()
    {
        CheckResult.Section("MOSFET 平方律模型与放大电路教学策略（engine-mos）");
        CheckDeviceModel();
        CheckTopologies();
        CheckFailurePaths();
        CheckRejections();
        CheckDepletionAndJfet();
        CheckClassifierIntegration();
    }

    // =====================================================================
    // 1. Level-1 device model
    // =====================================================================

    private static void CheckDeviceModel()
    {
        // ---------- NMOS parameter mapping and mirror quantities ----------
        var nmosElement = NewMosElement("M1", ElementType.MosfetNEnh, 1.5, 2e-3, 0.0);
        var nmos = MosfetModel.FromElement(nmosElement, Parameters);
        CheckResult.Check(!nmos.IsPChannel && nmos.IsEnhancement,
            "MosfetModel：NMOS 增强型识别", $"P={nmos.IsPChannel}, enh={nmos.IsEnhancement}");
        CheckResult.Close("MosfetModel：Vth 映射为 DisplayNumber", 1.5, nmos.ThresholdVoltage);
        CheckResult.Close("MosfetModel：K 映射为 MosfetK", 2e-3, nmos.K);
        CheckResult.Close("MosfetModel：λ 映射为 MosfetLambda（0=忽略）", 0.0, nmos.Lambda);
        CheckResult.Close("MosfetModel：NMOS Overdrive = VGS − Vth", 1.0, nmos.OverdriveMagnitude(2.5));
        CheckResult.Check(nmos.OverdriveMagnitude(1.0) < 0.0,
            "MosfetModel：NMOS VGS<Vth 时 Overdrive<0（截止）", Format(nmos.OverdriveMagnitude(1.0)));

        CheckResult.Check(nmos.Region(1.0, 0.5) == MosfetRegion.Triode,
            "MosfetModel：VDS<Vov 判为可变电阻（三极管）区", nmos.Region(1.0, 0.5).ToString());
        CheckResult.Check(nmos.Region(1.0, 2.0) == MosfetRegion.Saturation,
            "MosfetModel：VDS>Vov 判为饱和区", nmos.Region(1.0, 2.0).ToString());
        CheckResult.Check(nmos.Region(-0.1, 2.0) == MosfetRegion.Cutoff,
            "MosfetModel：Overdrive≤0 判为截止区", nmos.Region(-0.1, 2.0).ToString());

        CheckResult.Close("MosfetModel：饱和区 ID = K·Vov²", 2e-3, nmos.DrainCurrentMagnitude(1.0, 2.0));
        CheckResult.Close("MosfetModel：三极管区 ID = K(2VovVds − Vds²)",
            2e-3 * (2.0 * 1.0 * 0.5 - 0.5 * 0.5), nmos.DrainCurrentMagnitude(1.0, 0.5));
        CheckResult.Close("MosfetModel：截止区 ID = 0", 0.0, nmos.DrainCurrentMagnitude(-0.5, 2.0));
        CheckResult.Close("MosfetModel：gm = 2·K·Vov", 4e-3, nmos.Transconductance(1.0));
        CheckResult.Close("MosfetModel：λ=0 时 gds = 0（ro→∞）", 0.0, nmos.OutputConductance(1.0));

        // ---------- Channel-length modulation lambda ----------
        var lambdaModel = MosfetModel.FromElement(
            NewMosElement("M2", ElementType.MosfetNEnh, 1.5, 1e-3, 0.1), Parameters);
        CheckResult.Close("MosfetModel：λ 参数映射", 0.1, lambdaModel.Lambda);
        CheckResult.Close("MosfetModel：饱和区 ID 含 (1+λ·Vds)",
            1e-3 * (1.0 + 0.1 * 2.0), lambdaModel.DrainCurrentMagnitude(1.0, 2.0));
        CheckResult.Close("MosfetModel：三极管区 ID 含 (1+λ·Vds)",
            1e-3 * (2.0 * 1.0 * 0.5 - 0.25) * (1.0 + 0.1 * 0.5), lambdaModel.DrainCurrentMagnitude(1.0, 0.5));
        CheckResult.Close("MosfetModel：gds = λ·K·Vov²", 1e-4, lambdaModel.OutputConductance(1.0));

        // ---------- PMOS mirror polarity ----------
        var pmosModel = MosfetModel.FromElement(
            NewMosElement("M3", ElementType.MosfetPEnh, -1.5, 2e-3, 0.0), Parameters);
        CheckResult.Check(pmosModel.IsPChannel && pmosModel.IsEnhancement,
            "MosfetModel：PMOS 增强型识别", $"P={pmosModel.IsPChannel}, enh={pmosModel.IsEnhancement}");
        CheckResult.Close("MosfetModel：PMOS Overdrive = Vth − VGS", 1.5, pmosModel.OverdriveMagnitude(-3.0));
        CheckResult.Check(pmosModel.OverdriveMagnitude(-1.0) < 0.0,
            "MosfetModel：PMOS |VGS|<|Vth| 时 Overdrive<0（截止）", Format(pmosModel.OverdriveMagnitude(-1.0)));
        CheckResult.Close("MosfetModel：PMOS 电流幅值用镜像量同式",
            2e-3, pmosModel.DrainCurrentMagnitude(1.0, 2.0));

        // ---------- K default and depletion-device model support ----------
        var defaultKModel = MosfetModel.FromElement(
            NewMosElement("M4", ElementType.MosfetNEnh, 1.5, 0.0, 0.0), Parameters);
        CheckResult.Close("MosfetModel：K 缺省回退 1 mA/V²", 1e-3, defaultKModel.K);

        var depletionModel = MosfetModel.FromElement(
            NewMosElement("M5", ElementType.MosfetNDep, -2.0, 1e-3, 0.0), Parameters);
        CheckResult.Check(!depletionModel.IsEnhancement,
            "MosfetModel：耗尽型 MOSFET 可识别（模型层按负 Vth 平方律）", $"enh={depletionModel.IsEnhancement}");
        CheckResult.Close("MosfetModel：耗尽型负 Vth 在 VGS=0 时已导通",
            2.0, depletionModel.OverdriveMagnitude(0.0));

        var jfetElement = NewMosElement("J1", ElementType.JfetN, -4, 99, 0);
        jfetElement.JfetIdss = 0.008;
        var jfet = MosfetModel.FromElement(jfetElement, Parameters);
        CheckResult.Close("JFET：K 从 IDSS/VGS(off)² 得到，不使用 MosfetK", 0.0005, jfet.K);
        CheckResult.Close("JFET：VGS=0 时 ID=IDSS", 0.008, jfet.DrainCurrentMagnitude(jfet.OverdriveMagnitude(0), 10));
        CheckResult.Close("JFET：VGS=-2 时 ID=2mA", 0.002, jfet.DrainCurrentMagnitude(jfet.OverdriveMagnitude(-2), 10));
        CheckResult.Close("JFET：VGS=VGS(off) 时截止", 0, jfet.DrainCurrentMagnitude(jfet.OverdriveMagnitude(-4), 10));

        // ---------- non-MOSFET element rejected ----------
        bool nonMosRejected = false;
        var resistor = new CircuitElement { Id = "R1", Type = ElementType.Resistor, DisplayNumber = 1000, UnitSymbol = "Ω" };
        resistor.InitializeDefaultPins();
        try
        {
            MosfetModel.FromElement(resistor, Parameters);
        }
        catch (ArgumentException)
        {
            nonMosRejected = true;
        }
        CheckResult.Check(nonMosRejected, "MosfetModel：非 MOSFET 器件以 ArgumentException 拒绝");
    }

    // =====================================================================
    // 2. M01-M06 topologies: hand-computed Q point + small signal
    // =====================================================================

    private static void CheckTopologies()
    {
        // M01 NMOS common source (divider bias, source grounded, Rd=3k, no RL):
        //   VDD=15, Vth=1.5, K=2 mA/V², VG=15*200k/1200k=2.5 V
        //   Vov=1 V -> ID=2 mA, VDS=15-2mA*3k=9 V, gm=4 mS,
        //   Av=-gm*Rd=-12, Ri=Rg1||Rg2=166.667 kΩ, Ro=Rd=3 kΩ.
        AssertTopology("M01 NMOS 共源（分压偏置）", M01CommonSource(), "CommonSource",
            vgs: 2.5, id: 2e-3, vds: 9.0, gm: 4e-3, av: -12.0,
            ri: 1e6 * 200e3 / (1e6 + 200e3), ro: 3e3, isPmos: false);

        // M02 NMOS common source with unbypassed source resistor:
        //   VDD=15, Rg1=700k, Rg2=300k -> VG=4.5 V, a=VG-Vth=3 V, Rs=1k, Rd=3k.
        //   quadratic: K*Rs²*ID² - (2K*a*Rs+1)*ID + K*a² = 0
        //   2000*ID² - 13*ID + 0.018 = 0 -> discriminant=25 -> ID=2 mA
        //   Vov=a-ID*Rs=1 V, VGS=VG-VS=2.5 V, VDS=15-2mA*4k=7 V, gm=4 mS,
        //   Av=-gm*Rd/(1+gm*Rs)=-12/5=-2.4, Ri=700k||300k=210 kΩ, Ro=3 kΩ.
        AssertTopology("M02 NMOS 共源 + Rs（解二次方程）", M02CommonSourceWithRs(), "CommonSourceWithSourceResistance",
            vgs: 2.5, id: 2e-3, vds: 7.0, gm: 4e-3, av: -2.4,
            ri: 700e3 * 300e3 / (700e3 + 300e3), ro: 3e3, isPmos: false);

        // M03 = M02 + Cs bypassing Rs: DC unchanged, Av=-gm*Rd=-12.
        AssertTopology("M03 NMOS 共源 + Rs 旁路", M03CommonSourceBypassed(), "CommonSourceBypassed",
            vgs: 2.5, id: 2e-3, vds: 7.0, gm: 4e-3, av: -12.0,
            ri: 700e3 * 300e3 / (700e3 + 300e3), ro: 3e3, isPmos: false);

        // M04 NMOS common drain (source follower):
        //   VDD=15, Rg1=Rg2=1M -> VG=7.5 V, a=6 V, Rs=2.5k, RL=10k.
        //   12500*ID² - 61*ID + 0.072 = 0 -> discriminant=121 -> ID=2 mA
        //   Vov=6-2mA*2.5k=1 V, VGS=2.5 V, VDS=15-5=10 V, gm=4 mS,
        //   Rs||RL=2 kΩ -> Av=8/9=0.888889, Ri=500 kΩ,
        //   Ro=Rs||(1/gm)=2.5k||250=227.2727 Ω.
        AssertTopology("M04 NMOS 共漏（源极跟随器）", M04CommonDrain(), "CommonDrain",
            vgs: 2.5, id: 2e-3, vds: 10.0, gm: 4e-3, av: 8.0 / 9.0,
            ri: 500e3, ro: 2.5e3 * (1.0 / 4e-3) / (2.5e3 + 1.0 / 4e-3), isPmos: false);

        // M05 PMOS common source:
        //   VDD=12, Vth=-1.5, Rg1(G-VDD)=200k, Rg2(G-GND)=800k -> VG=9.6 V
        //   Vov=(VDD-VG)+Vth=2.4-1.5=0.9 V -> ID=2mA/V²*0.81=1.62 mA
        //   VSD=12-1.62mA*3k=7.14 V, VGS=-2.4 V, gm=3.6 mS,
        //   Av=-gm*Rd=-10.8, Ri=200k||800k=160 kΩ, Ro=3 kΩ.
        AssertTopology("M05 PMOS 共源（极性镜像）", M05PmosCommonSource(), "CommonSource",
            vgs: -2.4, id: 1.62e-3, vds: 7.14, gm: 3.6e-3, av: -10.8,
            ri: 200e3 * 800e3 / (200e3 + 800e3), ro: 3e3, isPmos: true);

        // M06 NMOS common gate: gate AC-grounded via Cb, input at source.
        //   DC identical to M02 (VG=4.5, ID=2 mA, Vov=1 V, VDS=7 V, gm=4 mS),
        //   Av=+gm*Rd=+12 (同相), Ri=(1/gm)||Rs=250||1000=200 Ω, Ro=Rd=3 kΩ.
        AssertTopology("M06 NMOS 共栅（栅极交流接地）", M06CommonGate(), "CommonGate",
            vgs: 2.5, id: 2e-3, vds: 7.0, gm: 4e-3, av: 12.0,
            ri: (1.0 / 4e-3) * 1e3 / (1.0 / 4e-3 + 1e3), ro: 3e3, isPmos: false);
    }

    // =====================================================================
    // 3. Failure paths
    // =====================================================================

    private static void CheckFailurePaths()
    {
        // ---------- cutoff: VG=1.333 V < Vth=1.5 V ----------
        var cutoffNetlist = NmosCutoffCircuit();
        CheckResult.Check(DetectSubtype(cutoffNetlist) == "CommonSource",
            "截止失败：拓扑仍识别为 CommonSource", DetectSubtype(cutoffNetlist));
        var cutoffSolution = Solve(cutoffNetlist, "CommonSource");
        CheckResult.Check(!cutoffSolution.IsSuccess, "截止工作点：求解明确失败（不伪造增益）");
        CheckResult.TextContains("截止工作点：失败原因点明截止区", cutoffSolution.SummaryText, "截止");
        CheckResult.Check(cutoffSolution.QPoint == null && cutoffSolution.AcParameters == null,
            "截止工作点：未输出 Q 点与小信号参数");

        // ---------- triode region: VG=6 V -> ID=20.25 mA, VDS=-89.25 V ----------
        var triodeNetlist = NmosTriodeCircuit();
        CheckResult.Check(DetectSubtype(triodeNetlist) == "CommonSource",
            "非饱和失败：拓扑仍识别为 CommonSource", DetectSubtype(triodeNetlist));
        var triodeSolution = Solve(triodeNetlist, "CommonSource");
        CheckResult.Check(!triodeSolution.IsSuccess, "非饱和工作点：求解明确失败（不输出错误增益）");
        CheckResult.TextContains("非饱和工作点：失败原因点明饱和区/三极管区",
            triodeSolution.SummaryText, "饱和区", "三极管");
        CheckResult.Check(triodeSolution.AcParameters == null,
            "非饱和工作点：未输出小信号参数");

        // ---------- subtype / connectivity mismatch ----------
        var mismatch = Solve(M01CommonSource(), "CommonDrain");
        CheckResult.Check(!mismatch.IsSuccess, "子类型与拓扑不符：求解明确失败");
        CheckResult.TextContains("子类型与拓扑不符：失败信息说明漏极连接要求", mismatch.SummaryText, "漏极");
    }

    // =====================================================================
    // 4. Invalid depletion operating point / missing JFET parameters
    // =====================================================================

    private static void CheckRejections()
    {
        var depletionNetlist = DepletionMosfetCircuit();
        string depletionDetected = DetectSubtype(depletionNetlist);
        CheckResult.Check(!string.IsNullOrWhiteSpace(depletionDetected),
            "耗尽型：DetectSubtype 不抛异常并返回固定子类型", depletionDetected);
        var depletionSolution = Solve(depletionNetlist, "CommonSource");
        CheckResult.Check(!depletionSolution.IsSuccess, "耗尽型 MOSFET：此电路过驱动过大，明确报告非饱和");
        CheckResult.TextContains("耗尽型 MOSFET：失败信息点名器件与耗尽型",
            depletionSolution.SummaryText, "M1", "饱和区");

        var jfetNetlist = JfetCircuit();
        string jfetDetected = DetectSubtype(jfetNetlist);
        CheckResult.Check(!string.IsNullOrWhiteSpace(jfetDetected),
            "JFET：DetectSubtype 不抛异常并返回固定子类型", jfetDetected);
        var jfetSolution = Solve(jfetNetlist, jfetDetected);
        CheckResult.Check(!jfetSolution.IsSuccess, "JFET：缺少 IDSS 时提示补参");
        CheckResult.TextContains("JFET：失败信息点名器件与 JFET", jfetSolution.SummaryText, "M1", "JFET", "IDSS");
    }

    // =====================================================================
    // 5. Classifier wiring (contract section 3): a single MOSFET must be
    //    routed to MosfetAmplifier with the subtype returned by DetectSubtype.
    // =====================================================================

    private static void CheckClassifierIntegration()
    {
        var commonSource = TopologyClassifier.Classify(M01CommonSource());
        CheckResult.Check(commonSource.TopologyType == CircuitTopologyType.MosfetAmplifier,
            "分类器接线：单只 NMOS 分类为 MosfetAmplifier", commonSource.TopologyType.ToString());
        CheckResult.Check(commonSource.SubtypeName == "CommonSource",
            "分类器接线：子类型取自 MosfetAmplifierStrategy.DetectSubtype", commonSource.SubtypeName);
        CheckResult.Check(commonSource.Strategy is MosfetAmplifierStrategy,
            "分类器接线：策略为 MosfetAmplifierStrategy", commonSource.Strategy.GetType().Name);

        var commonGate = TopologyClassifier.Classify(M06CommonGate());
        CheckResult.Check(commonGate.TopologyType == CircuitTopologyType.MosfetAmplifier &&
                          commonGate.SubtypeName == "CommonGate" &&
                          commonGate.Strategy is MosfetAmplifierStrategy,
            "分类器接线：共栅拓扑路由到 MosfetAmplifierStrategy/CommonGate",
            $"{commonGate.TopologyType}/{commonGate.SubtypeName}/{commonGate.Strategy.GetType().Name}");
    }

    private static void CheckDepletionAndJfet()
    {
        // Independent hand calculation: Voff=±4 V, K=0.5 mA/V², IDSS=8 mA.
        // With Rs=1k: x=0.5(4-x)² (x in mA), roots 2 and 8.
        // Only 2 mA has positive overdrive (2 V); the 8 mA root is extraneous.
        // Rd=500Ω gives VDS=9V, gm=2mS, CS+Rs Av=-1/3; bypassed Av=-1.
        foreach (var type in new[] { ElementType.MosfetNDep, ElementType.MosfetPDep,
                     ElementType.JfetN, ElementType.JfetP, ElementType.Jfet })
        foreach (var subtype in new[] { "CommonSource", "CommonSourceWithSourceResistance",
                     "CommonSourceBypassed", "CommonDrain", "CommonGate" })
        {
            var netlist = FamilyCircuit(type, subtype);
            bool p = type is ElementType.MosfetPDep or ElementType.JfetP;
            bool bare = subtype == "CommonSource";
            double id = bare ? 0.008 : 0.002;
            double gm = bare ? 0.004 : 0.002;
            double av = subtype switch { "CommonSource" => -2, "CommonSourceWithSourceResistance" => -0.33,
                "CommonDrain" => 0.67, "CommonGate" => 1, _ => -1 };
            AssertTopology($"{type}/{subtype}", netlist, subtype,
                vgs: bare ? 0 : p ? 2 : -2, id: id,
                vds: bare ? 8 : subtype == "CommonDrain" ? 10 : 9,
                gm: gm, av: av, ri: subtype == "CommonGate" ? 333.33 : 1e6,
                ro: subtype == "CommonDrain" ? 333.33 : 500, isPmos: false);
            var classified = TopologyClassifier.Classify(netlist);
            var solution = classified.Strategy.SolveAsync(netlist, Parameters).GetAwaiter().GetResult();
            CheckResult.Check(solution.IsSuccess, $"{type}/{subtype}：经分类器实际求解", solution.SummaryText);
            if (solution.IsSuccess)
            {
                CheckResult.AssertDisplayConventions($"{type}/{subtype}", solution);
                if (netlist.Elements["M1"].IsJfet)
                    CheckResult.TextContains("JFET 教学推导显示实际参数与 Shockley 方程",
                        solution.Steps[1].Content + solution.Steps[1].LatexFormula, "Shockley", "I_{DSS}", "V_{GS(off)}");
                CheckResult.Close("N/P 沟道 VDS 符号正确", p ? -(bare ? 8 : subtype == "CommonDrain" ? 10 : 9) : bare ? 8 : subtype == "CommonDrain" ? 10 : 9,
                    double.Parse(solution.Steps[1].Values["VDS"], CultureInfo.InvariantCulture));
            }
        }

        foreach (var type in new[] { ElementType.JfetN, ElementType.JfetP })
        {
            // Reverse bias is a physical constraint, not merely a square-law root check.
            var forward = FamilyCircuit(type, "CommonSource", forwardGate: true);
            var result = Solve(forward, DetectSubtype(forward));
            CheckResult.Check(!result.IsSuccess && result.AcParameters == null,
                $"{type}：栅结正偏禁止输出增益", result.SummaryText);
            CheckResult.TextContains("JFET 栅结正偏有明确原因", result.SummaryText, "JFET", "正向偏置");
        }
        foreach (var type in new[] { ElementType.MosfetNDep, ElementType.MosfetPDep })
        {
            var enhanced = FamilyCircuit(type, "CommonSource", forwardGate: true);
            var result = Solve(enhanced, DetectSubtype(enhanced));
            CheckResult.Check(result.IsSuccess, $"{type}：绝缘栅允许增强方向偏置", result.SummaryText);
        }
        foreach (double idss in new[] { 0.0, -0.001, double.NaN, double.PositiveInfinity })
        {
            var invalid = FamilyCircuit(ElementType.JfetN, "CommonSource");
            invalid.Elements["M1"].JfetIdss = idss;
            var result = Solve(invalid, DetectSubtype(invalid));
            CheckResult.Check(!result.IsSuccess && result.SummaryText.Contains("IDSS"), "JFET：缺失/非法 IDSS 拒绝", result.SummaryText);
        }
        foreach (var type in new[] { ElementType.MosfetNDep, ElementType.MosfetPDep, ElementType.JfetN, ElementType.JfetP })
        {
            var invalid = FamilyCircuit(type, "CommonSource");
            invalid.Elements["M1"].DisplayNumber *= -1;
            var result = Solve(invalid, DetectSubtype(invalid));
            CheckResult.Check(!result.IsSuccess && result.SummaryText.Contains("符号"), $"{type}：夹断电压符号校验", result.SummaryText);
        }
        foreach (var type in new[] { ElementType.MosfetNDep, ElementType.MosfetPDep, ElementType.JfetN, ElementType.JfetP })
        {
            var circuit = FamilyCircuit(type, "CommonSourceWithSourceResistance");
            circuit.Elements["M1"].MosfetLambda = 0.02;
            var result = Solve(circuit, DetectSubtype(circuit));
            CheckResult.Check(result.IsSuccess, $"{type}：含 λ 的自偏置数值求解", result.SummaryText);
            if (!result.IsSuccess) continue;
            var values = result.Steps[1].Values;
            double id = double.Parse(values["ID"], CultureInfo.InvariantCulture);
            double vov = double.Parse(values["Vov"], CultureInfo.InvariantCulture);
            double vds = Math.Abs(double.Parse(values["VDS"], CultureInfo.InvariantCulture));
            CheckResult.Close("含 λ 的 Q 点满足独立电流方程", id, 0.0005 * vov * vov * (1 + 0.02 * vds));
            double gm = double.Parse(values["gm"], CultureInfo.InvariantCulture);
            CheckResult.Close("含 λ 的 gm 等于电流对栅压的导数", 0.001 * vov * (1 + 0.02 * vds), gm);
        }
        var element = FamilyCircuit(ElementType.JfetP, "CommonSource").Elements["M1"];
        CheckResult.Close("JFET 克隆保留 IDSS", 0.008, element.Clone().JfetIdss);
        var data = CircuitProjectData.From("JFET", "jfet.circuit", new[] { element }, Array.Empty<CircuitWire>());
        string json = System.Text.Json.JsonSerializer.Serialize(data);
        var restored = System.Text.Json.JsonSerializer.Deserialize<CircuitProjectData>(json)!.ToModel().Elements.Single();
        CheckResult.Close("JFET 工程 JSON 往返保留 IDSS", 0.008, restored.JfetIdss);
        CheckResult.Close("JFET 工程往返保留正夹断电压", 4, restored.DisplayNumber);
    }

    private static ElectricalNetlist FamilyCircuit(ElementType type, string subtype, bool forwardGate = false)
    {
        bool p = type is ElementType.MosfetPDep or ElementType.JfetP;
        int sourceRail = p ? 1 : 0, drainRail = p ? 0 : 1;
        bool bare = subtype == "CommonSource", follower = subtype == "CommonDrain";
        int source = bare ? sourceRail : 4, drain = follower ? drainRail : 3;
        var b = new TestNetlistBuilder();
        b.Element("VDD", ElementType.DcVoltageSource, 12, "V")
            .Element("VS", ElementType.AcVoltageSource, 10, "mV")
            .Element("M1", type, p ? 4 : -4, "V", mosfetK: 0.0005)
            .Element("Rg", ElementType.Resistor, 1e6, "Ω")
            .Element("C1", ElementType.Capacitor, 10e-6, "F");
        b.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        b.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0).Pin("VS", "pos", 9).Pin("VS", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "S", source).Pin("M1", "D", drain)
            .Pin("Rg", "1", 2).Pin("Rg", "2", sourceRail)
            .Pin("C1", "1", 9).Pin("C1", "2", subtype == "CommonGate" ? source : 2);
        if (!follower) b.Element("Rd", ElementType.Resistor, forwardGate ? 100 : 500, "Ω").Pin("Rd", "1", drain).Pin("Rd", "2", drainRail);
        if (!bare) b.Element("Rs", ElementType.Resistor, 1000, "Ω").Pin("Rs", "1", source).Pin("Rs", "2", sourceRail);
        if (subtype is "CommonSourceBypassed" or "CommonGate")
            b.Element("Cb", ElementType.Capacitor, 47e-6, "F").Pin("Cb", "1", subtype == "CommonGate" ? 2 : source).Pin("Cb", "2", sourceRail);
        // Divider puts N-channel VG=+1 V, P-channel VG=11 V (both forward directions).
        if (forwardGate) b.Element("RgOther", ElementType.Resistor, 11e6, "Ω").Pin("RgOther", "1", 2).Pin("RgOther", "2", drainRail);
        var netlist = b.Build();
        netlist.Elements["M1"].JfetIdss = 0.008;
        return netlist;
    }

    // =====================================================================
    // Assertion helper
    // =====================================================================

    private static void AssertTopology(
        string name,
        ElectricalNetlist netlist,
        string expectedSubtype,
        double vgs,
        double id,
        double vds,
        double gm,
        double av,
        double ri,
        double ro,
        bool isPmos)
    {
        string detected = DetectSubtype(netlist);
        CheckResult.Check(detected == expectedSubtype,
            $"{name}：DetectSubtype = {expectedSubtype}", $"got {detected}");

        var solution = Solve(netlist, expectedSubtype);
        CheckResult.Check(solution.IsSuccess, $"{name}：求解成功", solution.SummaryText);
        if (!solution.IsSuccess) return;

        CheckResult.Check(solution.SubtypeName == expectedSubtype,
            $"{name}：结果子类型保持一致", solution.SubtypeName);
        CheckResult.Check(solution.Steps.Count >= 5,
            $"{name}：教学推导步骤 ≥ 5", $"count={solution.Steps.Count}");
        CheckResult.Check(solution.Steps.All(step =>
                !string.IsNullOrWhiteSpace(step.Title) &&
                !string.IsNullOrWhiteSpace(step.LatexFormula) &&
                !string.IsNullOrWhiteSpace(step.ResultValue)),
            $"{name}：每个步骤含标题/LaTeX 公式/结果值");
        CheckResult.Check(solution.Steps.All(step =>
                RequiredValueKeys.All(key => step.Values.ContainsKey(key) && !string.IsNullOrWhiteSpace(step.Values[key]))),
            $"{name}：每个步骤 Values 含稳定键 ID/VGS/VDS/gm/Av/Ri/Ro");

        var qPoint = solution.QPoint;
        CheckResult.Check(qPoint != null, $"{name}：填写 Q 点", "QPoint is null");
        if (qPoint == null) return;

        CheckResult.Check(qPoint.Ibq == "—", $"{name}：Ibq = —（MOSFET 无栅极电流）", qPoint.Ibq);
        CheckResult.Close($"{name}：Icq = ID", id, ParseMetric(qPoint.Icq), 0.01, " A");
        CheckResult.Close($"{name}：|VDS|/VSD = 手算值", vds, ParseMetric(qPoint.Vceq), 0.01, " V");
        CheckResult.Check(qPoint.Vceq.Length > 0 && !qPoint.Vceq.Contains('-', StringComparison.Ordinal),
            $"{name}：Vceq 以正幅值显示（|VDS| / VSD 约定）", qPoint.Vceq);
        CheckResult.Check(qPoint.WorkingRegion.Contains("饱和区 (Saturation)", StringComparison.Ordinal),
            $"{name}：WorkingRegion 标注饱和区 (Saturation)", qPoint.WorkingRegion);
        CheckResult.Check(qPoint.IsActiveRegion, $"{name}：IsActiveRegion = true");

        var ac = solution.AcParameters;
        CheckResult.Check(ac != null, $"{name}：填写交流小信号参数", "AcParameters is null");
        if (ac == null) return;

        CheckResult.Close($"{name}：Av = 手算值", av, ParseMetric(ac.VoltageGain), 0.01);
        CheckResult.Close($"{name}：Ri = 手算值", ri, ParseMetric(ac.InputResistance), 0.01, " Ω");
        CheckResult.Close($"{name}：Ro = 手算值", ro, ParseMetric(ac.OutputResistance), 0.01, " Ω");

        // gm / VGS via the stable machine-readable step Values dictionary.
        var stepValues = solution.Steps[1].Values;
        CheckResult.Close($"{name}：gm（步骤 Values）= 手算值",
            gm, double.Parse(stepValues["gm"], CultureInfo.InvariantCulture), 0.01, " S");
        CheckResult.Close($"{name}：VGS（步骤 Values）= 手算值",
            vgs, double.Parse(stepValues["VGS"], CultureInfo.InvariantCulture), 0.01, " V");

        if (isPmos)
        {
            CheckResult.Check(vgs < 0.0, $"{name}：PMOS VGS 为负（N 型衬底极性镜像）", Format(vgs));
            CheckResult.Check(qPoint.WorkingRegion.Contains("PMOS", StringComparison.Ordinal) &&
                              qPoint.WorkingRegion.Contains("V_SD", StringComparison.Ordinal),
                $"{name}：PMOS 工作区文本注明 V_SD 方向约定", qPoint.WorkingRegion);
            CheckResult.Check(solution.SummaryText.Contains("PMOS", StringComparison.Ordinal),
                $"{name}：摘要注明 PMOS 极性");
        }
    }

    // =====================================================================
    // Fixtures: M01-M06 (parameters announced by circuits-mos)
    // =====================================================================

    /// <summary>M01: NMOS common source, VDD=15, Rg1=1M, Rg2=200k, Rd=3k, no RL.</summary>
    private static ElectricalNetlist M01CommonSource()
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
        return builder.Build();
    }

    /// <summary>M02: NMOS CS + Rs=1k, VDD=15, VG=4.5 V, Rd=3k, no bypass capacitor.</summary>
    private static ElectricalNetlist M02CommonSourceWithRs()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 700_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 300_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("Rs", ElementType.Resistor, 1_000.0, "Ω");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 4)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3)
            .Pin("Rs", "1", 4).Pin("Rs", "2", 0)
            .Pin("C1", "1", 9).Pin("C1", "2", 2);
        return builder.Build();
    }

    /// <summary>M03: M02 plus Cs=47 uF bypassing Rs (source to GND).</summary>
    private static ElectricalNetlist M03CommonSourceBypassed()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 700_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 300_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("Rs", ElementType.Resistor, 1_000.0, "Ω");
        builder.Element("Cs", ElementType.Capacitor, 47e-6, "F");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 4)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3)
            .Pin("Rs", "1", 4).Pin("Rs", "2", 0)
            .Pin("Cs", "1", 4).Pin("Cs", "2", 0)
            .Pin("C1", "1", 9).Pin("C1", "2", 2);
        return builder.Build();
    }

    /// <summary>M04: NMOS common drain, VDD=15, drain tied to VDD, Rs=2.5k, RL=10k via C2.</summary>
    private static ElectricalNetlist M04CommonDrain()
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
        return builder.Build();
    }

    /// <summary>M05: PMOS common source, VDD=12, Vth=-1.5 V, Rd=3k to GND, no RL.</summary>
    private static ElectricalNetlist M05PmosCommonSource()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetPEnh, -1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 200_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 800_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 1)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 3).Pin("Rd", "2", 0)
            .Pin("C1", "1", 9).Pin("C1", "2", 2);
        return builder.Build();
    }

    /// <summary>M06: NMOS common gate, gate AC-grounded via Cb, input at source, VDD=15.</summary>
    private static ElectricalNetlist M06CommonGate()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 15.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 2e-3);
        builder.Element("Rg1", ElementType.Resistor, 700_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 300_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("Rs", ElementType.Resistor, 1_000.0, "Ω");
        builder.Element("Cb", ElementType.Capacitor, 47e-6, "F");
        builder.Element("C1", ElementType.Capacitor, 10e-6, "F");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true).Net(9, "Vin");
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("VS", "pos", 9).Pin("VS", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 4)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3)
            .Pin("Rs", "1", 4).Pin("Rs", "2", 0)
            .Pin("Cb", "1", 2).Pin("Cb", "2", 0)
            .Pin("C1", "1", 9).Pin("C1", "2", 4);
        return builder.Build();
    }

    // =====================================================================
    // Fixtures: failure paths and explicit rejections
    // =====================================================================

    /// <summary>NMOS CS with VG=1.333 V < Vth=1.5 V -> cutoff must be rejected.</summary>
    private static ElectricalNetlist NmosCutoffCircuit()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 1e-3);
        builder.Element("Rg1", ElementType.Resistor, 400_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 50_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 5_000.0, "Ω");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true);
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 0)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3);
        return builder.Build();
    }

    /// <summary>NMOS CS with VG=6 V -> ID=20.25 mA but VDS=-89.25 V (triode) -> must fail.</summary>
    private static ElectricalNetlist NmosTriodeCircuit()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("M1", ElementType.MosfetNEnh, 1.5, "V", mosfetK: 1e-3);
        builder.Element("Rg1", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 5_000.0, "Ω");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true);
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 0)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3);
        return builder.Build();
    }

    /// <summary>Depletion-mode NMOS in the same common-source topology -> explicit rejection.</summary>
    private static ElectricalNetlist DepletionMosfetCircuit()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("M1", ElementType.MosfetNDep, -2.0, "V", mosfetK: 1e-3);
        builder.Element("Rg1", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true);
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 0)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3);
        return builder.Build();
    }

    /// <summary>JFET in a common-source-like topology -> explicit rejection.</summary>
    private static ElectricalNetlist JfetCircuit()
    {
        var builder = new TestNetlistBuilder();
        builder.Element("VDD", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("M1", ElementType.JfetN, -2.0, "V");
        builder.Element("Rg1", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rg2", ElementType.Resistor, 100_000.0, "Ω");
        builder.Element("Rd", ElementType.Resistor, 3_000.0, "Ω");
        builder.Element("Rs", ElementType.Resistor, 1_000.0, "Ω");
        builder.Net(0, "GND", isGround: true).Net(1, "VCC", isPower: true);
        builder.Pin("VDD", "pos", 1).Pin("VDD", "neg", 0)
            .Pin("M1", "G", 2).Pin("M1", "D", 3).Pin("M1", "S", 4)
            .Pin("Rg1", "1", 1).Pin("Rg1", "2", 2)
            .Pin("Rg2", "1", 2).Pin("Rg2", "2", 0)
            .Pin("Rd", "1", 1).Pin("Rd", "2", 3)
            .Pin("Rs", "1", 4).Pin("Rs", "2", 0);
        return builder.Build();
    }

    // =====================================================================
    // Shared helpers
    // =====================================================================

    private static CircuitElement NewMosElement(
        string id,
        ElementType type,
        double thresholdVoltage,
        double k,
        double lambda)
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = id,
            Type = type,
            DisplayNumber = thresholdVoltage,
            MosfetK = k,
            MosfetLambda = lambda
        };
        element.InitializeDefaultPins();
        return element;
    }

    private static PedagogicalSolution Solve(ElectricalNetlist netlist, string subtype) =>
        new MosfetAmplifierStrategy(subtype).SolveAsync(netlist, Parameters).GetAwaiter().GetResult();

    private static string DetectSubtype(ElectricalNetlist netlist) =>
        MosfetAmplifierStrategy.DetectSubtype(netlist);

    private static string Format(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture);

    private static readonly Regex MetricRegex = new(
        @"^\s*([-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)\s*(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a displayed engineering value ("2 mA", "166.667 kΩ", "-12", "9 V")
    /// back to base SI units for numeric assertions.
    /// </summary>
    private static double ParseMetric(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        var match = MetricRegex.Match(text);
        if (!match.Success) return double.NaN;
        double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string suffix = match.Groups[2].Value.Trim();
        if (suffix.Length == 0) return value;
        char prefix = suffix[0];
        double scale = suffix switch
        {
            _ when suffix.Contains("Ω", StringComparison.Ordinal) => prefix switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'm' => 1e-3,
                _ => 1.0
            },
            _ when suffix.Contains("A", StringComparison.Ordinal) => prefix switch
            {
                'μ' or 'µ' or 'u' => 1e-6,
                'n' => 1e-9,
                'm' => 1e-3,
                'k' => 1e3,
                _ => 1.0
            },
            _ when suffix.Contains("Hz", StringComparison.Ordinal) => prefix switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'G' => 1e9,
                _ => 1.0
            },
            _ => 1.0
        };
        return value * scale;
    }
}
