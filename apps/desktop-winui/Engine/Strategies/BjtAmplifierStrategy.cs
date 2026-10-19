// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/BjtAmplifierStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical solving strategy for single-BJT amplifiers (fixed
//              bias / voltage-divider self-bias / collector-feedback bias common
//              emitter, common base and common collector, NPN or PNP).
//
//              Display convention (user feedback 2026-10-18, second round):
//                * The displayed solution follows the TEXTBOOK ESTIMATION method
//                  (教材估算法) exactly as a student would hand-calculate it:
//                  for voltage-divider bias the base potential is taken from the
//                  unloaded divider U_B = V_CC·R_b2/(R_b1+R_b2), then
//                  I_EQ = (U_B − V_BE)/R_E (PNP mirrored), I_BQ = I_EQ/(1+β),
//                  I_CQ = β·I_BQ, U_CEQ = V_CC − I_CQ·R_C − I_EQ·R_E.
//                * Every intermediate result is rounded to at most two decimals
//                  in its textbook display unit (AwayFromZero) and the rounded
//                  displayed value is fed into the next formula, exactly like a
//                  handwritten derivation. Formulas keep every term: (1+β) is
//                  never reduced to β and I_CQ is never assumed equal to I_EQ.
//                * The Thévenin-exact solution is still computed in the backend
//                  and kept only in the step Values dictionary (IBQ_exact_uA,
//                  ICQ_exact_mA, UCE_exact_V, Vth_V, Rth_ohm, UB_actual_V) and in
//                  QPointInfo.ExactIbq/ExactIcq/ExactVceq (not bound in the UI).
//                  No "Thévenin vs textbook" comparison text is emitted.
//                * The derivation is grouped into the two textbook equivalent
//                  circuits: DC equivalent circuit (Q point) → AC equivalent
//                  circuit (rbe, Av, Ri/Ro, f_L/f_H/BW). Electrolytic-capacitor
//                  polarity checks belong to the DC phase and immediately follow
//                  the Q-point step; StepIndex values are renumbered upfront.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"双极型晶体管 (BJT) {GetSubtypeDisplayName(_subtype)}交流小信号与频响分析"
        };

        // Topological extraction: exactly one BJT device (NPN *or* PNP), plus R/C/sources only.
        var bjt = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        var validation = netlist.Validate();
        if (!validation.IsValid)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, string.Join("\n", validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Error).Select(i => i.Message))));
        if (bjt == null ||
            netlist.Elements.Values.Count(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp) != 1 ||
            netlist.Elements.Values.Any(e => e.Type is not (ElementType.BjtNpn or ElementType.BjtPnp or ElementType.Resistor or
                ElementType.Capacitor or ElementType.CapacitorPolarized or ElementType.DcVoltageSource or
                ElementType.AcVoltageSource or ElementType.VccPort or ElementType.Ground)))
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                "当前 BJT 教学策略仅支持单个 NPN 或单个 PNP 放大电路；多级级联、NPN-PNP 混合网络以及含二极管等非线性器件的网络尚未支持，请拆分为单管放大电路后分别求解。"));
        bool isPnp = bjt.Type == ElementType.BjtPnp;
        double beta = bjt.DisplayNumber;
        // 硅管 0.7 V / 锗管 0.3 V：所有 VBE/VEB 计算统一读取器件材料语义，不再硬编码 0.7 V。
        double vdrop = bjt.ForwardVoltageDrop;
        string materialName = bjt.Material == SemiconductorMaterial.Germanium ? "锗管" : "硅管";

        // 小信号参数（第四轮反馈）：r_bb' 与可选“题目给定 r_be/h_ie”。
        // 0 保持上一轮教材默认（共射/共集 200 Ω、共基 50 Ω）；>0 直接采用题目值。
        if (!double.IsFinite(bjt.BjtRbb) || bjt.BjtRbb < 0 ||
            !double.IsFinite(bjt.BjtRbe) || bjt.BjtRbe < 0)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                "BJT 小信号参数 r_bb'/r_be(h_ie) 必须为有限非负数（0 表示自动：r_bb' 取教材默认值、r_be 按 Q 点计算）。"));
        double rbb = bjt.BjtRbb > 0 ? bjt.BjtRbb : (_subtype == "CommonBase" ? 50.0 : 200.0);
        bool rbeGiven = bjt.BjtRbe > 0;
        double rbeGivenValue = rbeGiven ? bjt.BjtRbe : 0.0;
        string rbeSource = rbeGiven ? "given" : "computed";
        string polarityName = isPnp ? "PNP" : "NPN";
        solution.CircuitTitle = $"{(isPnp ? "PNP " : "")}双极型晶体管 (BJT) {GetSubtypeDisplayName(_subtype)}交流小信号与频响分析";

        var bNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "B") : null;
        var cNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "C") : null;
        var eNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "E") : null;
        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));
        if (vccNet == null)
        {
            var dcSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.DcVoltageSource);
            if (dcSrc != null)
            {
                var posNet = netlist.FindNetForPin(dcSrc.Id, "pos");
                if (posNet != null && !posNet.IsGround) vccNet = posNet;
            }
        }

        double vcc = double.NaN;
        var supplies = netlist.Elements.Values.Where(e => e.Type is ElementType.DcVoltageSource or ElementType.VccPort).ToList();
        if (supplies.Count != 1)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "当前 BJT 教学策略要求单一偏置电源。"));
        var vccEl = supplies.FirstOrDefault(e => e.Type == ElementType.VccPort
            ? netlist.FindNetForPin(e.Id, "VCC") == vccNet
            : netlist.FindNetForPin(e.Id, "pos") == vccNet && netlist.FindNetForPin(e.Id, "neg")?.IsGround == true);
        if (vccEl != null) vcc = GetSiValue(vccEl);
        if (!double.IsFinite(vcc) || vcc <= 0 || !double.IsFinite(beta) || beta <= 0 || bNet == null || cNet == null || eNet == null ||
            bNet == cNet || bNet == eNet || cNet == eNet)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "缺少有效正电源、β 或晶体管引脚连接。"));

        double rb1 = 0;
        double rb2 = 0;
        double rc = 0;
        double re = 0;
        // No external output branch means an open load, not an invented resistor.
        double rl = double.PositiveInfinity;

        bool IsGndNet(ElectricalNet? net) => net != null && (net.IsGround || net == gndNet);

        var identifiedResistors = new System.Collections.Generic.HashSet<string>();
        foreach (var el in netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor))
        {
            double val = GetSiValue(el);
            if (!double.IsFinite(val) || val <= 0)
                return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                    $"电阻 {el.Id} 必须为有限正数；短路请使用导线，开路请移除支路，不能用非法参数判断器件工作区。"));
            var n1 = netlist.FindNetForPin(el.Id, "1");
            var n2 = netlist.FindNetForPin(el.Id, "2");
            if (n1 == null || n2 == null) continue;

            if (isPnp)
            {
                // PNP 镜像角色映射：Re 接 VCC 侧、Rc 接 GND 侧、Rb1(B–VCC)/Rb2(B–GND)；
                // 基极电流由基极向外流出，因此固定偏置的基极电阻落在 B–GND 一侧。
                if ((n1 == cNet && IsGndNet(n2)) || (n2 == cNet && IsGndNet(n1)))
                {
                    if (rc != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rc = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == eNet && n2 == vccNet) || (n2 == eNet && n1 == vccNet))
                {
                    if (re != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    re = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == bNet && n2 == vccNet) || (n2 == bNet && n1 == vccNet))
                {
                    if (rb1 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rb1 = val;
                    identifiedResistors.Add(el.Id);
                }
                else if (_subtype == "CollectorFeedbackBias" &&
                    ((n1 == bNet && n2 == cNet) || (n2 == bNet && n1 == cNet)))
                {
                    if (rb1 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "反馈偏置支路存在多个电阻。"));
                    rb1 = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == bNet && IsGndNet(n2)) || (n2 == bNet && IsGndNet(n1)))
                {
                    if (rb2 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rb2 = val;
                    identifiedResistors.Add(el.Id);
                }
            }
            else
            {
                if ((n1 == cNet && n2 == vccNet) || (n2 == cNet && n1 == vccNet))
                {
                    if (rc != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rc = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == eNet && (n2.IsGround || n2 == gndNet)) || (n2 == eNet && (n1.IsGround || n1 == gndNet)))
                {
                    if (re != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    re = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == bNet && n2 == vccNet) || (n2 == bNet && n1 == vccNet))
                {
                    if (rb1 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rb1 = val;
                    identifiedResistors.Add(el.Id);
                }
                else if (_subtype == "CollectorFeedbackBias" &&
                    ((n1 == bNet && n2 == cNet) || (n2 == bNet && n1 == cNet)))
                {
                    if (rb1 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "反馈偏置支路存在多个电阻。"));
                    rb1 = val;
                    identifiedResistors.Add(el.Id);
                }
                else if ((n1 == bNet && (n2.IsGround || n2 == gndNet)) || (n2 == bNet && (n1.IsGround || n1 == gndNet)))
                {
                    if (rb2 != 0) return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "同一偏置支路存在多个电阻，当前教学模型尚不支持该拓扑。"));
                    rb2 = val;
                    identifiedResistors.Add(el.Id);
                }
            }
        }

        // 电解/普通电容统一取两端网络：电解电容的引脚 Id 是 "+"/"-"，不能按 "1"/"2" 查找。
        (ElectricalNet? First, ElectricalNet? Second) CapNets(CircuitElement cap)
        {
            string firstPin = cap.Type == ElementType.CapacitorPolarized ? "+" : "1";
            string secondPin = cap.Type == ElementType.CapacitorPolarized ? "-" : "2";
            return (netlist.FindNetForPin(cap.Id, firstPin), netlist.FindNetForPin(cap.Id, secondPin));
        }

        bool Connects(CircuitElement element, ElectricalNet? first, ElectricalNet? second)
        {
            var n1 = netlist.FindNetForPin(element.Id, "1");
            var n2 = netlist.FindNetForPin(element.Id, "2");
            return first != null && second != null && ((n1 == first && n2 == second) || (n1 == second && n2 == first));
        }

        // Output Load RL: resistor on the output net of the output coupling capacitor.
        // 共射/共基的输出在集电极；共集电极（射极跟随器）的输出在发射极（契约 §6）。
        var outputNodeNet = _subtype == "CommonCollector" ? eNet : cNet;
        var outCap = netlist.Elements.Values.FirstOrDefault(e =>
        {
            if (e.Type is not (ElementType.Capacitor or ElementType.CapacitorPolarized)) return false;
            var (first, second) = CapNets(e);
            return first == outputNodeNet || second == outputNodeNet;
        });
        if (outCap != null)
        {
            var (outFirst, outSecond) = CapNets(outCap);
            var outNet = outFirst == outputNodeNet ? outSecond : outFirst;
            if (outNet != null && !outNet.IsGround && outNet != vccNet && outNet != outputNodeNet)
            {
                var rlEl = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Resistor &&
                    ((netlist.FindNetForPin(e.Id, "1") == outNet && (netlist.FindNetForPin(e.Id, "2")?.IsGround == true || netlist.FindNetForPin(e.Id, "2") == gndNet)) ||
                     (netlist.FindNetForPin(e.Id, "2") == outNet && (netlist.FindNetForPin(e.Id, "1")?.IsGround == true || netlist.FindNetForPin(e.Id, "1") == gndNet))));
                if (rlEl != null) { rl = GetSiValue(rlEl); identifiedResistors.Add(rlEl.Id); }
            }
        }

        var caps = netlist.Elements.Values.Where(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized).ToList();
        bool Across(CircuitElement cap, ElectricalNet? first, ElectricalNet? second)
        {
            var (n1, n2) = CapNets(cap);
            return first != null && second != null && ((n1 == first && n2 == second) || (n1 == second && n2 == first));
        }
        // PNP 的交流地参考位于 VCC 侧：发射极旁路电容跨接 E–VCC；NPN 仍为 E–GND。
        var acGroundNet = isPnp ? vccNet : gndNet;
        var bypass = caps.FirstOrDefault(cap => Across(cap, eNet, acGroundNet));

        // ---- 输入耦合电容 C1 与信号源内阻 Rs 识别（NPN/PNP、共射/共基/共集通用）----
        // 旧行为：C1 直接跨接 inputNet 与交流信号源 pos 网。
        // 教学变体：C1 一端接 inputNet、另一端 x；若 x 与交流源 pos 网之间恰有一只电阻，
        // 则该电阻即信号源内阻 Rs（例如 vs — R6 — C3 — 基极），C1 仍为输入耦合电容。
        var inputNet = _subtype == "CommonBase" ? eNet : bNet;
        var acSource = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);
        var acSourcePosNet = acSource == null ? null : netlist.FindNetForPin(acSource.Id, "pos");
        CircuitElement? inputCap = null;
        double rs = 0;
        if (inputNet != null && acSourcePosNet != null)
        {
            inputCap = caps.FirstOrDefault(cap => Across(cap, inputNet, acSourcePosNet));
            if (inputCap == null)
            {
                foreach (var cap in caps)
                {
                    var (capFirst, capSecond) = CapNets(cap);
                    ElectricalNet? farNet = capFirst == inputNet ? capSecond : capSecond == inputNet ? capFirst : null;
                    if (farNet == null || farNet == acSourcePosNet) continue;
                    var seriesResistors = netlist.Elements.Values
                        .Where(e => e.Type == ElementType.Resistor && Connects(e, farNet, acSourcePosNet))
                        .ToList();
                    // 串联多只电阻/复杂网络不猜测：保持明确失败（由下方未覆盖电阻诊断给出元件与网名）。
                    if (seriesResistors.Count != 1) continue;
                    inputCap = cap;
                    rs = GetSiValue(seriesResistors[0]);
                    identifiedResistors.Add(seriesResistors[0].Id);
                    break;
                }
            }
        }
        double c1 = inputCap == null ? 0 : GetSiValue(inputCap);
        double c2 = outCap == null ? 0 : GetSiValue(outCap);
        double ce = bypass == null ? 0 : GetSiValue(bypass);
        bool hasCe = bypass != null;

        // 未覆盖电阻：给出元件 Id 与两端网名，并列出当前模型支持的电阻位置（不静默忽略）。
        var unidentifiedResistors = netlist.Elements.Values
            .Where(e => e.Type == ElementType.Resistor && !identifiedResistors.Contains(e.Id))
            .ToList();
        if (unidentifiedResistors.Count > 0)
        {
            string details = string.Join("、", unidentifiedResistors.Select(r =>
            {
                string firstName = netlist.FindNetForPin(r.Id, "1")?.Name ?? "?";
                string secondName = netlist.FindNetForPin(r.Id, "2")?.Name ?? "?";
                return $"{r.Id}（{firstName}–{secondName}）";
            }));
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                $"存在教学模型未覆盖的电阻支路：{details}。当前 BJT 教学模型只识别以下电阻位置：" +
                "Rc（集电极–电源轨）、Re（发射极–参考轨）、Rb1/Rb2（基极–VCC / 基极–GND 分压偏置）、" +
                "集电极反馈偏置的 RB（基极–集电极）、RL（输出耦合电容之后的负载）、" +
                "Rs（交流信号源与输入耦合电容之间的信号源内阻，如 vs–Rs–耦合电容–基极）。" +
                "请检查该电阻的连接位置，或把电路简化为上述教学拓扑后重试。"));
        }

        // 发射极是否直接落在其交流地轨上（NPN：GND；PNP：VCC）。
        bool emitterAtAcRail = isPnp ? eNet == vccNet : (eNet.IsGround || eNet == gndNet);
        // 固定/反馈偏置的基极电阻：NPN 固定偏置在 B–VCC（rb1），PNP 固定偏置在 B–GND（rb2）。
        double fixedBiasResistor = isPnp ? rb2 : rb1;
        // 共集电极支持固定偏置（NPN: B–VCC；PNP 镜像: B–GND）与分压偏置（Rb1 接 VCC 侧、Rb2 接 GND 侧）。
        bool ccHasDivider = rb1 > 0 && rb2 > 0;
        bool ccHasFixedBias = fixedBiasResistor > 0;
        bool missingBaseBias = _subtype switch
        {
            "FixedBias" => fixedBiasResistor <= 0,
            "CollectorFeedbackBias" => rb1 <= 0,
            "VoltageDividerSelfBias" or "CommonBase" => rb1 <= 0 || rb2 <= 0,
            "CommonCollector" => !(ccHasDivider || ccHasFixedBias),
            _ => false
        };
        if (_subtype == "CommonCollector")
        {
            // 共集电极（射极跟随器）拓扑前提：集电极直接接电源轨（PNP 镜像为直接接地），
            // 集电极支路不得串电阻；发射极经 Re 构成直流通路且是输出端；RL 允许开路（RL=∞）。
            bool collectorAtRail = isPnp ? IsGndNet(cNet) : cNet == vccNet;
            if (!collectorAtRail || rc > 0)
                return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                    "共集电极（射极跟随器）要求集电极直接连接电源轨（PNP 镜像为直接接地）且集电极支路不得串接电阻；当前连接不满足该拓扑。"));
            if (missingBaseBias || re <= 0 || rl <= 0)
                return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                    "未能从实际连接识别完整的基极偏置、发射极电阻或输出负载网络；不会使用缺失元件默认值。"));
            return Task.FromResult(SolveCommonCollector(solution, netlist, parameters, beta, vcc, rb1, rb2, re, rl, c1, c2,
                rbb, rbeGiven, rbeGivenValue, rs, isPnp, vdrop, materialName, polarityName, ccHasDivider));
        }
        if (missingBaseBias || rc <= 0 || (!emitterAtAcRail && re <= 0) || rl <= 0)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                "未能从实际连接识别完整的偏置、集电极、发射极或输出负载网络；不会使用缺失元件默认值。"));
        if (_subtype == "CommonBase")
            return Task.FromResult(SolveCommonBase(solution, netlist, parameters, beta, vcc, rb1, rb2, rc, re, rl, c1, c2,
                rbb, rbeGiven, rbeGivenValue, rs, isPnp, vdrop, materialName, polarityName));
        if ((_subtype is "FixedBias" or "CollectorFeedbackBias") && !emitterAtAcRail)
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, isPnp
                ? "当前 PNP 固定/反馈偏置公式要求发射极直接接 VCC（交流地）。"
                : "当前固定/反馈偏置公式要求发射极直接接地。"));

        // =====================================================================
        // 1) 直流等效电路（DC）：教材估算法求静态工作点 Q
        // =====================================================================
        bool dividerBias = _subtype == "VoltageDividerSelfBias";
        DcSolution dc = ComputeDcPoint(_subtype, isPnp, dividerBias, commonCollector: false,
            vcc, rb1, rb2, rc, re, fixedBiasResistor, vdrop, beta);

        string? dcError = ValidateDcRegion(dc, isPnp, vdrop);
        if (dcError != null)
            return Task.FromResult(CollectDcFailureDiagnostics(solution, netlist, dc, dcError, isPnp, vcc, vdrop));

        string vceDisplay = TxtVoltage(dc.Vceq);
        string vdropText = DropText(vdrop);
        string vceTex = isPnp ? @"U_{ECQ}" : @"U_{CEQ}";
        string vbePlain = isPnp ? "V_EB" : "V_BE";
        // PNP 的 Q 点以电流/电压幅值与 |U_CE| 显示，并在工作区文本中明确 U_CE 的符号约定，避免与 NPN 混淆。
        string pnpDirectionNote = isPnp ? $"PNP: U_CE = -{vceDisplay}（V_EC=+{vceDisplay}）" : string.Empty;
        string regionName = isPnp ? $"放大区 (Active Region)；{pnpDirectionNote}" : "放大区 (Active Region)";
        string qPointText = dc.Divider
            ? $"U_B={TxtVoltage(dc.Ub)}，I_EQ={TxtCurrent(dc.Ieq)}，I_BQ={TxtCurrent(dc.Ibq)}，I_CQ={TxtCurrent(dc.Icq)}，{(isPnp ? "U_ECQ" : "U_CEQ")}={vceDisplay}"
            : $"I_BQ={TxtCurrent(dc.Ibq)}，I_CQ={TxtCurrent(dc.Icq)}，{(isPnp ? "U_ECQ" : "U_CEQ")}={vceDisplay}";

        // =====================================================================
        // 2) 交流等效电路（AC）：rbe → Av → Ri/Ro → fL/fH/BW
        //    下游一律使用“按显示单位两位小数舍入后的值”，复现教材手算顺序。
        // =====================================================================
        double ut = parameters.ThermalVoltageUt > 0 ? parameters.ThermalVoltageUt : 0.026;
        // 题目给定 r_be=h_ie 时直接采用（不再套 r_bb' 公式）；否则按教材链计算并舍入。
        double rbe = rbeGiven ? rbeGivenValue : RoundResistance(rbb + (1 + beta) * ut / dc.Ieq);
        double rlPrime = RoundResistance(Parallel(rc, rl));
        double rbAc = dividerBias ? RoundResistance(rb1 * rb2 / (rb1 + rb2)) : dc.Rth;
        double inputDeviceResistance = _subtype == "CollectorFeedbackBias"
            ? rbe
            : RoundResistance(rbe + (hasCe ? 0.0 : (1 + beta) * re));
        double av;
        if (_subtype == "CollectorFeedbackBias")
        {
            double inverseLoad = double.IsPositiveInfinity(rl) ? 0.0 : 1.0 / rl;
            av = RoundPlain((1.0 / rb1 - beta / rbe) / (1.0 / rc + inverseLoad + 1.0 / rb1));
        }
        else
        {
            av = RoundPlain(-beta * rlPrime / inputDeviceResistance);
        }
        double ri = _subtype switch
        {
            "CollectorFeedbackBias" => RoundResistance(1.0 / (1.0 / rbe + (1.0 - av) / rb1)),
            "FixedBias" => RoundResistance(Parallel(fixedBiasResistor, inputDeviceResistance)),
            _ => RoundResistance(Parallel(rbAc, inputDeviceResistance))
        };
        double ro = _subtype == "CollectorFeedbackBias"
            ? RoundResistance(Parallel(rc, rb1))
            : RoundResistance(rc);

        // 源电压放大倍数（教材口径）：Aus = Av·Ri/(Rs+Ri)，Av/Ri 均使用上方舍入后的显示值。
        double aus = rs > 0 ? RoundPlain(av * ri / (ri + rs)) : double.NaN;

        // 频率特性：时间常数法。rbe/Av/Ri 均使用上方已完成舍入的显示值。
        // 有信号源内阻 Rs 时，输入耦合电容看到的总电阻为 (Rs+Ri)。
        double fl1 = c1 > 0 ? RoundFrequency(1.0 / (2 * Math.PI * (ri + rs) * c1)) : 0;
        double fl2 = c2 > 0 && double.IsFinite(rl) ? RoundFrequency(1.0 / (2 * Math.PI * (rc + rl) * c2)) : 0;
        double rePrime = RoundResistance(re * (rbe + rbAc) / ((1 + beta) * re + rbe + rbAc));
        double fle = hasCe && ce > 0 ? RoundFrequency(1.0 / (2 * Math.PI * rePrime * ce)) : 0;
        double fl = RoundFrequency(Math.Sqrt(fl1 * fl1 + fl2 * fl2 + fle * fle));
        double cPi = 30e-12;
        double cMu = 3e-12;
        double cm = RoundCapacitance(cMu * (1 + Math.Abs(av)));
        double cInPrime = RoundCapacitance(cPi + cm);
        double rInPrime = RoundResistance(Parallel(ri, rbe));
        double fh = RoundFrequency(1.0 / (2 * Math.PI * rInPrime * cInPrime));
        double bw = RoundFrequency(Math.Max(0, fh - fl));
        if (new[] { rbe, av, ri, ro, fl, fh, bw }.Any(v => !double.IsFinite(v)) || ri <= 0 || ro <= 0 ||
            (rs > 0 && !double.IsFinite(aus)))
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, "BJT 模型参数超出数值范围，未生成有效结果。"));

        // =====================================================================
        // 3) 结果面板与摘要（只显示教材估算法数值）
        // =====================================================================
        solution.QPoint = new QPointInfo
        {
            Ibq = TxtCurrent(dc.Ibq),
            Icq = TxtCurrent(dc.Icq),
            // PNP 填 |U_CE| = V_EC 的正值；方向约定在 WorkingRegion/摘要中说明。
            Vceq = vceDisplay,
            WorkingRegion = regionName,
            IsActiveRegion = true,
            ExactIbq = ExactText(dc.IbqExact, QuantityKind.Current),
            ExactIcq = ExactText(dc.IcqExact, QuantityKind.Current),
            ExactVceq = ExactText(dc.VceqExact, QuantityKind.Voltage)
        };

        string freqLowerText = fl > 0 ? TxtFrequency(fl) : "0 Hz（无耦合/旁路电容）";
        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = Plain(av),
            InputResistance = TxtResistance(ri),
            OutputResistance = TxtResistance(ro),
            LowerCutoffFreq = freqLowerText,
            UpperCutoffFreq = TxtFrequency(fh) + "（模型估算）",
            Bandwidth = TxtFrequency(bw),
            // 只有识别到信号源内阻 Rs 时才显示源电阻与源电压放大倍数。
            HasSourceResistance = rs > 0,
            SourceResistance = rs > 0 ? TxtResistance(rs) : "—",
            SourceVoltageGain = rs > 0 ? Plain(aus) : "—"
        };

        solution.SummaryText =
            $"{(isPnp ? "PNP " : "")}双极型晶体管 (BJT) {GetSubtypeDisplayName(_subtype)}求解完成（{materialName}，{vbePlain}={vdropText}）：" +
            $"直流等效电路求得静态工作点 Q（{qPointText}，放大区）。" +
            $"交流等效电路求得中频电压放大倍数 Av = {Plain(av)}，输入电阻 Ri = {TxtResistance(ri)}，输出电阻 Ro = {TxtResistance(ro)}，" +
            $"下限截止频率 fL = {freqLowerText}，上限截止频率 fH = {TxtFrequency(fh)}（模型估算），通频带 BW = {TxtFrequency(bw)}。" +
            (rs > 0
                ? $" 信号源内阻 Rs = {TxtResistance(rs)}（交流电压源与输入耦合电容之间），源电压放大倍数 Aus = Av·Ri/(Rs+Ri) = {Plain(aus)}。"
                : string.Empty) +
            (isPnp ? $" PNP 极性约定：I_E 由 VCC 经发射极流入、I_B 自基极流出、I_C 由集电极流出经 R_C 到地；U_CE = -{vceDisplay}（V_EC = +{vceDisplay}）。" : string.Empty);

        // =====================================================================
        // 4) 教学推导步骤：DC 等效电路（第 1 步 + 极性核验）→ AC 等效电路
        // =====================================================================
        var stepValues = BuildStepValues(dc, rbe, av, ri, ro, fl, fh, bw, beta, vcc, fixedBiasResistor, rs, aus,
            rbb, rbe, rbeSource);
        var steps = solution.Steps;

        // ---- 第 1 步：直流等效电路 + 教材估算法 Q 点 ----
        string dcLatexFormula;
        string dcSubstitution;
        string dcMethodName;
        string dcContent;
        if (dc.Divider)
        {
            dcLatexFormula = isPnp
                ? @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{V_{CC}-U_B-V_{EB}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-I_{CQ}R_C-I_{EQ}R_E"
                : @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{U_B-V_{BE}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-I_{CQ}R_C-I_{EQ}R_E";
            string ieqNumerator = isPnp
                ? $@"{LxVoltage(vcc)}-{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}"
                : $@"{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}";
            dcSubstitution =
                $@"U_B={LxVoltage(vcc)}\times\frac{{{LxResistance(rb2)}}}{{{LxResistance(rb1)}+{LxResistance(rb2)}}}={LxVoltage(dc.Ub)},\quad " +
                $@"I_{{EQ}}=\frac{{{ieqNumerator}}}{{{LxResistance(re)}}}={LxCurrent(dc.Ieq)},\quad " +
                $@"I_{{BQ}}=\frac{{{LxCurrent(dc.Ieq)}}}{{1+{Plain(beta)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-{LxCurrent(dc.Icq)}\times{LxResistance(rc)}-{LxCurrent(dc.Ieq)}\times{LxResistance(re)}={LxVoltage(dc.Vceq)}";
            dcMethodName = "教材估算法：分压偏置 U_B=V_CC·R_b2/(R_b1+R_b2)（忽略基极电流加载）+ 发射极回路";
            dcContent =
                "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "按教材手算口径，忽略基极电流对分压器的加载，直接取 U_B=V_CC·R_b2/(R_b1+R_b2)（PNP 镜像同式，基极电位相对 VCC 侧）；" +
                "再由发射极回路求 I_EQ，按 I_BQ=I_EQ/(1+β)、I_CQ=β·I_BQ 求电流，最后用集电极–发射极回路 KVL 求管压降。" +
                "每个中间结果按显示单位保留两位小数后代入下一步，与教材手算顺序一致。";
        }
        else if (_subtype == "CollectorFeedbackBias")
        {
            dcLatexFormula = isPnp
                ? @"I_{BQ}=\frac{V_{CC}-V_{EB}}{R_B+(1+\beta)R_C},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-(I_{CQ}+I_{BQ})R_C"
                : @"I_{BQ}=\frac{V_{CC}-V_{BE}}{R_B+(1+\beta)R_C},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-(I_{CQ}+I_{BQ})R_C";
            dcSubstitution =
                $@"I_{{BQ}}=\frac{{{LxVoltage(vcc)}-{DropLatex(vdrop)}}}{{{LxResistance(rb1)}+(1+{Plain(beta)})\times{LxResistance(rc)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-\left({LxCurrent(dc.Icq)}+{LxCurrent(dc.Ibq)}\right)\times{LxResistance(rc)}={LxVoltage(dc.Vceq)}";
            dcMethodName = "教材估算法：集电极反馈偏置基极回路 KVL";
            dcContent =
                "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "集电极反馈偏置中基极电阻 R_B 跨接在基极与集电极之间，基极电流流经 R_B，同时 R_C 中流过 I_CQ+I_BQ；" +
                "列写基极–集电极回路 KVL 得 I_BQ=(V_CC−V_BE)/[R_B+(1+β)R_C]，再按 I_CQ=β·I_BQ、U_CEQ=V_CC−(I_CQ+I_BQ)R_C 求 Q 点。" +
                "每个中间结果按显示单位保留两位小数后代入下一步，与教材手算顺序一致。";
        }
        else
        {
            dcLatexFormula = isPnp
                ? @"I_{BQ}=\frac{V_{CC}-V_{EB}}{R_B},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-I_{CQ}R_C"
                : @"I_{BQ}=\frac{V_{CC}-V_{BE}}{R_B},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-I_{CQ}R_C";
            dcSubstitution =
                $@"I_{{BQ}}=\frac{{{LxVoltage(vcc)}-{DropLatex(vdrop)}}}{{{LxResistance(fixedBiasResistor)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-{LxCurrent(dc.Icq)}\times{LxResistance(rc)}={LxVoltage(dc.Vceq)}";
            dcMethodName = "教材估算法：固定偏置基极回路 KVL";
            dcContent =
                "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "固定偏置中基极电阻直接接偏置电源（NPN：B–VCC；PNP：B–GND），基极回路 KVL 得 I_BQ=(V_CC−V_BE)/R_B；" +
                "再按 I_CQ=β·I_BQ、U_CEQ=V_CC−I_CQ·R_C 求 Q 点。" +
                "每个中间结果按显示单位保留两位小数后代入下一步，与教材手算顺序一致。";
        }
        string dcResult = dc.Divider
            ? (isPnp
                ? $"Q点: U_B={TxtVoltage(dc.Ub)}, I_EQ={TxtCurrent(dc.Ieq)}, I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, U_ECQ={vceDisplay}（即 |U_CE|），放大区"
                : $"Q点: U_B={TxtVoltage(dc.Ub)}, I_EQ={TxtCurrent(dc.Ieq)}, I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, U_CEQ={vceDisplay}，放大区")
            : (isPnp
                ? $"Q点: I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, U_ECQ={vceDisplay}（即 |U_CE|），放大区"
                : $"Q点: I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, U_CEQ={vceDisplay}，放大区");
        steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "直流等效电路：静态工作点 (Q-Point) 求解（教材估算法）",
            MethodName = dcMethodName,
            Content = dcContent,
            LatexFormula = dcLatexFormula,
            SubstitutionFormula = dcSubstitution,
            ResultValue = dcResult,
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        // 电解电容极性校核归入 DC 相，紧跟 Q 点步骤（StepIndex 最后由 RenumberSteps 统一重排）。
        double vb, ve, vc;
        if (isPnp)
        {
            vb = dc.Divider ? dc.UbActual : vcc - vdrop;
            ve = vcc - dc.IeqExact * re;
            vc = _subtype == "CollectorFeedbackBias" ? (dc.IcqExact + dc.IbqExact) * rc : dc.IcqExact * rc;
        }
        else
        {
            vb = dc.Divider ? dc.UbActual : vdrop;
            ve = dc.IeqExact * re;
            vc = _subtype == "CollectorFeedbackBias" ? vcc - (dc.IcqExact + dc.IbqExact) * rc : vcc - dc.IcqExact * rc;
        }
        if (!CheckElectrolyticPolarity(solution, netlist, vb, vc, ve, vcc))
        {
            ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, rc, vdrop, beta);
            RenumberSteps(solution.Steps);
            return Task.FromResult(solution);
        }

        // ---- 第 2 步（AC 相第一步）：rbe ----
        string acIntro =
            "画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路，晶体管用 h 参数（r_be、βi_b）微变模型替代。";
        steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "交流等效电路：晶体管低频小信号微变等效参数 rbe 计算",
            MethodName = "晶体管 h 参数简化模型",
            Content = acIntro + (rbeGiven
                ? $"题目给定 r_be=h_ie={TxtResistance(rbe)}（直接采用，不再用 r_bb' 公式计算）；r_bb'={TxtResistance(rbb)} 仅作记录，下游 Av/Ri/Ro/fL 均使用给定的 r_be。"
                : $"由直流等效电路已求得的 I_EQ = {TxtCurrent(dc.Ieq)} 计算 r_be = r_bb' + (1+β)·U_T/I_EQ（r_bb' = {TxtResistance(rbb)}，可由元件参数 rbb' 调节；U_T=26 mV），不再对公式作任何代数化简。"),
            LatexFormula = rbeGiven
                ? @"r_{be}=h_{ie}"
                : @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}}",
            SubstitutionFormula = rbeGiven
                ? $@"r_{{be}}=h_{{ie}}={LxResistance(rbe)}\quad(\text{{题目给定，直接采用}})"
                : $@"r_{{be}} = {LxResistance(rbb)} + (1+{Plain(beta)})\frac{{{LxVoltage(ut)}}}{{{LxCurrent(dc.Ieq)}}} = {LxResistance(rbe)}",
            ResultValue = rbeGiven
                ? $"rbe = hie = {TxtResistance(rbe)}（题目给定，直接采用）"
                : $"rbe = {TxtResistance(rbe)}（r_bb' = {TxtResistance(rbb)}）",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        // ---- 第 3 步（AC）：Av（有信号源内阻 Rs 时附源电压放大倍数 Aus）----
        string loadLatex = double.IsPositiveInfinity(rl)
            ? LxResistance(rc)
            : $@"\left({LxResistance(rc)}\parallel{LxResistance(rl)}\right)";
        string avLatex;
        string avSub;
        string avContent;
        if (_subtype == "CollectorFeedbackBias")
        {
            avLatex = @"A_v=\frac{1/R_B-\beta/r_{be}}{1/R_C+1/R_L+1/R_B}";
            string inverseLoadLatex = double.IsPositiveInfinity(rl) ? "0" : $@"1/{LxResistance(rl)}";
            avSub = $@"A_v=\frac{{1/{LxResistance(rb1)}-{Plain(beta)}/{LxResistance(rbe)}}}{{1/{LxResistance(rc)}+{inverseLoadLatex}+1/{LxResistance(rb1)}}}={LxRatio(av)}";
            avContent = "集电极反馈偏置的 R_B 同时构成交流反馈支路：交流等效电路中由 KCL 解出 A_v=(1/R_B−β/r_be)/(1/R_C+1/R_L+1/R_B)。";
        }
        else
        {
            avLatex = hasCe
                ? @"A_v=-\frac{\beta(R_C\parallel R_L)}{r_{be}}"
                : @"A_v=-\frac{\beta(R_C\parallel R_L)}{r_{be}+(1+\beta)R_E}";
            avSub = hasCe
                ? $@"A_v=-\frac{{{Plain(beta)}\times{loadLatex}}}{{{LxResistance(rbe)}}}={LxRatio(av)}"
                : $@"A_v=-\frac{{{Plain(beta)}\times{loadLatex}}}{{{LxResistance(rbe)}+(1+{Plain(beta)})\times{LxResistance(re)}}}={LxRatio(av)}";
            avContent = hasCe
                ? "交流等效电路中发射极旁路电容把 R_E 交流短路，输入回路只剩 r_be，输出回路为 R_C∥R_L，故 A_v=−β(R_C∥R_L)/r_be。"
                : "交流等效电路中 R_E 未被旁路，发射极电阻折算到基极回路为 (1+β)R_E，与 r_be 串联构成输入回路，故 A_v=−β(R_C∥R_L)/[r_be+(1+β)R_E]。";
        }
        if (rs > 0)
        {
            avLatex += @",\quad A_{us}=A_v\frac{R_i}{R_s+R_i}";
            avSub += $@",\quad A_{{us}}={LxRatio(av)}\times\frac{{{LxResistance(ri)}}}{{{LxResistance(rs)}+{LxResistance(ri)}}}={LxRatio(aus)}";
            avContent += $" 信号源内阻 R_s={TxtResistance(rs)} 与放大器输入电阻 Ri 构成分压，故源电压放大倍数 A_us=A_v·Ri/(R_s+Ri)。";
        }
        steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "中频段交流电压放大倍数 Av 推导与求解",
            MethodName = "微变等效电路交流分析",
            Content = acIntro + avContent,
            LatexFormula = avLatex,
            SubstitutionFormula = avSub,
            ResultValue = rs > 0
                ? $"Av = {Plain(av)}，Aus = {Plain(aus)}（Rs={TxtResistance(rs)}）"
                : $"Av = {Plain(av)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        // ---- 第 4 步（AC）：Ri 与 Ro ----
        string riRoLatex;
        string riRoSub;
        string riRoContent;
        if (_subtype == "CollectorFeedbackBias")
        {
            riRoLatex = @"R_i=\frac{1}{1/r_{be}+(1-A_v)/R_B},\quad R_o=R_C\parallel R_B";
            riRoSub =
                $@"R_i=\frac{{1}}{{1/{LxResistance(rbe)}+(1-({LxRatio(av)}))/{LxResistance(rb1)}}}={LxResistance(ri)},\quad " +
                $@"R_o={LxResistance(rc)}\parallel{LxResistance(rb1)}={LxResistance(ro)}";
            riRoContent = "输入电阻按 R_i=1/[1/r_be+(1−A_v)/R_B] 计算（反馈支路把输出折算到输入）；输出电阻为 R_C∥R_B。";
        }
        else if (_subtype == "FixedBias")
        {
            riRoLatex = @"R_i=R_B\parallel r_{be},\quad R_o=R_C";
            riRoSub = $@"R_i={LxResistance(fixedBiasResistor)}\parallel{LxResistance(rbe)}={LxResistance(ri)},\quad R_o={LxResistance(ro)}";
            riRoContent = "固定偏置的基极电阻 R_B 与 r_be 并联构成输入电阻；输出电阻为 R_C。";
        }
        else if (hasCe)
        {
            riRoLatex = @"R_i=R_{b1}\parallel R_{b2}\parallel r_{be},\quad R_o=R_C";
            riRoSub = $@"R_i={LxResistance(rbAc)}\parallel{LxResistance(rbe)}={LxResistance(ri)},\quad R_o={LxResistance(ro)}";
            riRoContent = "分压偏置的两个基极电阻并联后与 r_be 并联构成输入电阻；输出电阻为 R_C。";
        }
        else
        {
            riRoLatex = @"R_i=R_{b1}\parallel R_{b2}\parallel\left[r_{be}+(1+\beta)R_E\right],\quad R_o=R_C";
            riRoSub =
                $@"r_{{be}}+(1+{Plain(beta)})R_E={LxResistance(rbe)}+(1+{Plain(beta)})\times{LxResistance(re)}={LxResistance(inputDeviceResistance)},\quad " +
                $@"R_i={LxResistance(rbAc)}\parallel{LxResistance(inputDeviceResistance)}={LxResistance(ri)},\quad R_o={LxResistance(ro)}";
            riRoContent = "R_E 未被旁路时，发射极电阻折算到基极回路为 (1+β)R_E 并与 r_be 串联；该串联支路再与 R_b1∥R_b2 并联得到输入电阻。";
        }
        steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "放大电路输入电阻 Ri 与输出电阻 Ro 计算",
            MethodName = "输入/输出阻抗定义法",
            Content = acIntro + riRoContent,
            LatexFormula = riRoLatex,
            SubstitutionFormula = riRoSub,
            ResultValue = $"Ri = {TxtResistance(ri)}，Ro = {TxtResistance(ro)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        // ---- 第 5 步（AC）：频率特性 ----
        steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "频率特性分析（密勒效应、极点时间常数与通频带 BW）",
            MethodName = "开路/短路时间常数法 (SCTC/OCTC)",
            Content = acIntro +
                "下限截止频率 f_L 由耦合电容与旁路电容的时间常数决定；上限截止频率 f_H 由极间寄生结电容与密勒倍增效应形成的主极点决定；通频带 BW=f_H−f_L。",
            LatexFormula = @"f_L = \sqrt{f_{L1}^2 + f_{L2}^2 + f_{Le}^2},\quad f_H \approx \frac{1}{2\pi R_{in}' C_{in}'},\quad BW = f_H - f_L",
            SubstitutionFormula =
                $@"f_L = {LxFrequency(fl)},\quad f_H = {LxFrequency(fh)},\quad BW = {LxFrequency(bw)}",
            ResultValue = $"fL = {freqLowerText}，fH = {TxtFrequency(fh)}，BW = {TxtFrequency(bw)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, rc, vdrop, beta);
        RenumberSteps(solution.Steps);
        return Task.FromResult(solution);
    }

    // =====================================================================
    // 直流等效电路：教材估算法链（每个中间量按显示单位两位小数舍入后回代）
    // =====================================================================

    /// <summary>Rounded textbook-display values plus the backend Thévenin-exact values.</summary>
    private sealed class DcSolution
    {
        public bool Divider;
        public bool LoadedBias;
        public double Vth;
        public double Rth;
        public double Ub;
        public double UbActual;
        public double Ieq;
        public double Ibq;
        public double Icq;
        public double Vceq;
        public double IeqExact;
        public double IbqExact;
        public double IcqExact;
        public double VceqExact;
    }

    private static DcSolution ComputeDcPoint(
        string subtype,
        bool isPnp,
        bool dividerBias,
        bool commonCollector,
        double vcc,
        double rb1,
        double rb2,
        double rc,
        double re,
        double fixedBiasResistor,
        double vdrop,
        double beta)
    {
        var dc = new DcSolution { Divider = dividerBias };

        if (dividerBias)
        {
            // 教材估算法：U_B=V_CC·R_b2/(R_b1+R_b2)（空载分压，忽略基极电流加载）。
            dc.Vth = vcc * (rb2 / (rb1 + rb2));
            dc.Rth = rb1 * rb2 / (rb1 + rb2);
            dc.Ub = RoundVoltage(dc.Vth);
            dc.Ieq = re > 0 ? RoundCurrent(isPnp ? (vcc - dc.Ub - vdrop) / re : (dc.Ub - vdrop) / re) : 0;
            dc.Ibq = RoundCurrent(dc.Ieq / (1.0 + beta));
            dc.Icq = RoundCurrent(beta * dc.Ibq);
            dc.Vceq = RoundVoltage(commonCollector
                ? vcc - dc.Ieq * re
                : vcc - dc.Icq * rc - dc.Ieq * re);

            // 未舍入加载解用于物理工作区核验，并保留于 Values/Exact*。
            double baseDrive = isPnp ? vcc - dc.Vth : dc.Vth;
            dc.IbqExact = (baseDrive - vdrop) / (dc.Rth + (1 + beta) * re);
            dc.IeqExact = (1 + beta) * dc.IbqExact;
            dc.IcqExact = beta * dc.IbqExact;
            dc.VceqExact = commonCollector
                ? vcc - dc.IeqExact * re
                : vcc - dc.IcqExact * rc - dc.IeqExact * re;
            dc.UbActual = isPnp ? dc.Vth + dc.IbqExact * dc.Rth : dc.Vth - dc.IbqExact * dc.Rth;
            // R_E=0 does not mean cutoff: use the loaded base loop instead.
            // Also recover when unloaded-divider estimation violates the region
            // but the unrounded loaded solution is physically forward-active.
            if (re == 0 || (dc.IbqExact > 0 && dc.VceqExact >= vdrop &&
                (!double.IsFinite(dc.Vceq) || dc.Vceq < vdrop || dc.Ieq <= 0)))
            {
                dc.LoadedBias = true;
                double drive = isPnp ? vcc - RoundVoltage(dc.Vth) : RoundVoltage(dc.Vth);
                dc.Ibq = RoundCurrent((drive - vdrop) / (RoundResistance(dc.Rth) + (1 + beta) * re));
                dc.Ieq = RoundCurrent((1 + beta) * dc.Ibq);
                dc.Icq = RoundCurrent(beta * dc.Ibq);
                dc.Vceq = RoundVoltage(vcc - (commonCollector ? 0 : dc.Icq * rc) - dc.Ieq * re);
                dc.Ub = RoundVoltage(isPnp ? vcc - dc.Ieq * re - vdrop : dc.Ieq * re + vdrop);
            }
        }
        else if (subtype == "CollectorFeedbackBias")
        {
            dc.Vth = vcc;
            dc.Rth = rb1;
            dc.Ibq = RoundCurrent((vcc - vdrop) / (rb1 + (1 + beta) * rc));
            dc.Ieq = RoundCurrent((1 + beta) * dc.Ibq);
            dc.Icq = RoundCurrent(beta * dc.Ibq);
            dc.Vceq = RoundVoltage(vcc - (dc.Icq + dc.Ibq) * rc);

            dc.IbqExact = (vcc - vdrop) / (rb1 + (1 + beta) * rc);
            dc.IeqExact = (1 + beta) * dc.IbqExact;
            dc.IcqExact = beta * dc.IbqExact;
            dc.VceqExact = vcc - (dc.IcqExact + dc.IbqExact) * rc;
            dc.UbActual = isPnp ? vcc - vdrop : vdrop;
        }
        else if (commonCollector)
        {
            // 共集电极固定偏置：I_BQ=(V_CC−V_BE)/[R_B+(1+β)R_E]（NPN/PNP 镜像同式）。
            dc.Vth = vcc;
            dc.Rth = fixedBiasResistor;
            dc.Ibq = RoundCurrent((vcc - vdrop) / (fixedBiasResistor + (1 + beta) * re));
            dc.Ieq = RoundCurrent((1 + beta) * dc.Ibq);
            dc.Icq = RoundCurrent(beta * dc.Ibq);
            dc.Vceq = RoundVoltage(vcc - dc.Ieq * re);

            dc.IbqExact = (vcc - vdrop) / (fixedBiasResistor + (1 + beta) * re);
            dc.IeqExact = (1 + beta) * dc.IbqExact;
            dc.IcqExact = beta * dc.IbqExact;
            dc.VceqExact = vcc - dc.IeqExact * re;
            dc.UbActual = isPnp ? vcc - vdrop : vdrop;
        }
        else
        {
            // 共射固定偏置：I_BQ=(V_CC−V_BE)/R_B。
            dc.Vth = vcc;
            dc.Rth = fixedBiasResistor;
            dc.Ibq = RoundCurrent((vcc - vdrop) / fixedBiasResistor);
            dc.Ieq = RoundCurrent((1 + beta) * dc.Ibq);
            dc.Icq = RoundCurrent(beta * dc.Ibq);
            dc.Vceq = RoundVoltage(vcc - dc.Icq * rc);

            dc.IbqExact = (vcc - vdrop) / fixedBiasResistor;
            dc.IeqExact = (1 + beta) * dc.IbqExact;
            dc.IcqExact = beta * dc.IbqExact;
            dc.VceqExact = vcc - dc.IcqExact * rc;
            dc.UbActual = isPnp ? vcc - vdrop : vdrop;
        }

        return dc;
    }


    // Region decisions use the unrounded, loaded DC candidate, never the display chain.
    private static string? ValidateDcRegion(DcSolution dc, bool isPnp, double vdrop)
    {
        string voltage = isPnp ? "U_ECQ" : "U_CEQ";
        string polarity = isPnp ? "PNP" : "NPN";
        if ((dc.Divider && (!double.IsFinite(dc.Vth) || !double.IsFinite(dc.Rth) || dc.Rth <= 0)) ||
            !double.IsFinite(dc.IbqExact) || !double.IsFinite(dc.IcqExact) ||
            !double.IsFinite(dc.IeqExact) || !double.IsFinite(dc.VceqExact))
            return $"{polarity} 直流方程产生非有限数值，无法确定工作区；请检查参数与连接。数值失败不等于截止或饱和。";
        if (dc.IbqExact <= 0)
            return $"{polarity} 基极偏置不足，工作点处于截止区：放大区试算 I_BQ≤0，实际截止近似 I_BQ=I_CQ=0；停止放大区小信号推导。";
        // Forward-active requires the BC junction not to be forward biased.
        // VCE>0.2 alone is insufficient: VCE<VBE already forward-biases BC.
        if (dc.VceqExact < vdrop - 1e-10 * Math.Max(1, Math.Abs(dc.VceqExact)))
            return $"{polarity} 工作点不满足放大区假设，处于饱和区或饱和过渡区：" +
                $"含基极负载的放大区试算 I_BQ={TxtCurrent(dc.IbqExact)}、I_CQ={TxtCurrent(dc.IcqExact)}、" +
                $"{voltage}={TxtVoltage(dc.VceqExact)}，小于结压降 {TxtVoltage(vdrop)}，集电结不再反偏。" +
                "这些是放大区假设下的试算值，不是饱和工作点；停止小信号推导，当前未重求解饱和区 Q 点。";
        if (!double.IsFinite(dc.Ibq) || !double.IsFinite(dc.Ieq) || !double.IsFinite(dc.Vceq))
            return "教材计算链产生非有限数值，未将数值错误解释为器件工作区；停止推导。";
        if (dc.Ieq <= 0 || dc.Vceq <= 0)
            return "未舍入偏置解满足放大条件，但教材舍入计算链越过工作区边界；当前显示精度不足，停止小信号推导。";
        return null;
    }

    private static void ApplyLoadedBiasDerivation(PedagogicalSolution solution, DcSolution dc,
        bool isPnp, double vcc, double re, double rc, double vdrop, double beta)
    {
        if (!dc.LoadedBias) return;
        var step = solution.Steps.First(s => s.Phase == SolutionPhase.DcEquivalent);
        string vce = isPnp ? "U_{ECQ}" : "U_{CEQ}";
        string drive = isPnp ? "V_{CC}-U_{th}" : "U_{th}";
        string drop = isPnp ? "V_{EB}" : "V_{BE}";
        step.MethodName = "含基极负载的分压偏置回路（戴维南等效）";
        step.Content = "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
            "发射极直接连接参考轨，或空载分压估算不适用时，必须计入基极电流对分压器的加载。" +
            "先取偏置网络戴维南等效，再列基极回路 KVL；R_E=0 时公式仍然成立。" +
            "保留教学舍入链：I_BQ→I_EQ、I_CQ→管压降；工作区另用未舍入解核验。";
        step.LatexFormula = @"U_{th}=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad R_{th}=R_{b1}\parallel R_{b2},\quad " +
            $@"I_{{BQ}}=\frac{{{drive}-{drop}}}{{R_{{th}}+(1+\beta)R_E}},\quad I_{{EQ}}=(1+\beta)I_{{BQ}},\quad I_{{CQ}}=\beta I_{{BQ}},\quad {vce}=V_{{CC}}-I_{{CQ}}R_C-I_{{EQ}}R_E";
        step.SubstitutionFormula = $@"U_{{th}}={LxVoltage(dc.Vth)},\quad R_{{th}}={LxResistance(dc.Rth)},\quad I_{{BQ}}={LxCurrent(dc.Ibq)},\quad I_{{EQ}}={LxCurrent(dc.Ieq)},\quad I_{{CQ}}={LxCurrent(dc.Icq)},\quad {vce}={LxVoltage(vcc)}-{LxCurrent(dc.Icq)}\times{LxResistance(rc)}-{LxCurrent(dc.Ieq)}\times{LxResistance(re)}={LxVoltage(dc.Vceq)}";
        solution.SummaryText += " 本次采用含基极负载的偏置方程，保留教学舍入；工作区由未舍入解核验。";
        step.Values["bias_method"] = "loaded-divider";
    }

    private static Dictionary<string, string> BuildStepValues(
        DcSolution dc,
        double rbe,
        double av,
        double ri,
        double ro,
        double fl,
        double fh,
        double bw,
        double beta,
        double vcc,
        double fixedBiasResistor,
        double rs,
        double aus,
        double rbb,
        double finalRbe,
        string rbeSource)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IBQ"] = Raw(dc.Ibq),
            ["ICQ"] = Raw(dc.Icq),
            ["IEQ"] = Raw(dc.Ieq),
            ["VCEQ"] = Raw(dc.Vceq),
            ["rbe"] = Raw(rbe),
            ["Av"] = Raw(av),
            ["Ri"] = Raw(ri),
            ["Ro"] = Raw(ro),
            ["fL"] = Raw(fl),
            ["fH"] = Raw(fh),
            ["BW"] = Raw(bw),
            ["beta"] = Raw(beta),
            ["VCC"] = Raw(vcc),
            // 小信号参数来源（第四轮反馈）：r_bb' 解析值、最终 r_be/h_ie、来源（computed/given）。
            ["rbb_ohm"] = Raw(rbb),
            ["rbe_ohm"] = Raw(finalRbe),
            ["rbe_source"] = rbeSource,
            // 后端保留的戴维南精确解（不参与显示；测试/诊断用）。
            ["IBQ_exact_uA"] = Raw(dc.IbqExact * 1e6),
            ["ICQ_exact_mA"] = Raw(dc.IcqExact * 1e3),
            ["UCE_exact_V"] = Raw(dc.VceqExact),
            ["IEQ_exact_uA"] = Raw(dc.IeqExact * 1e6)
        };
        if (dc.Divider)
        {
            values["UB"] = Raw(dc.Ub);
            values["Vth_V"] = Raw(dc.Vth);
            values["Rth_ohm"] = Raw(dc.Rth);
            values["UB_actual_V"] = Raw(dc.UbActual);
        }
        if (rs > 0)
        {
            // 稳定键名：信号源内阻（Ω）与源电压放大倍数。
            values["Rs_ohm"] = Raw(rs);
            values["Aus"] = Raw(aus);
        }
        return values;
    }

    // =====================================================================
    // 共基极（Common Base）：DC 同为分压偏置教材估算法链；AC 用 h 参数模型。
    // =====================================================================
    private static PedagogicalSolution SolveCommonBase(
        PedagogicalSolution solution,
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        double beta,
        double vcc,
        double rb1,
        double rb2,
        double rc,
        double re,
        double rl,
        double c1,
        double c2,
        double rbb,
        bool rbeGiven,
        double rbeGivenValue,
        double rs,
        bool isPnp,
        double vdrop,
        string materialName,
        string polarityName)
    {
        double ut = parameters.ThermalVoltageUt > 0 ? parameters.ThermalVoltageUt : 0.026;
        string rbeSource = rbeGiven ? "given" : "computed";

        // ---- 1) DC：分压偏置教材估算法 ----
        DcSolution dc = ComputeDcPoint("CommonBase", isPnp, dividerBias: true, commonCollector: false,
            vcc, rb1, rb2, rc, re, fixedBiasResistor: 0, vdrop, beta);
        string? dcError = ValidateDcRegion(dc, isPnp, vdrop);
        if (dcError != null)
            return CollectDcFailureDiagnostics(solution, netlist, dc, dcError, isPnp, vcc, vdrop);

        double vb = dc.UbActual;
        double ve = isPnp ? vb + vdrop : vb - vdrop;
        double vc = isPnp ? dc.IcqExact * rc : vcc - dc.IcqExact * rc;
        string vceDisplay = TxtVoltage(dc.Vceq);
        string vdropText = DropText(vdrop);
        string regionName = isPnp
            ? $"放大区 (Active Region)；{polarityName}: U_CE = -{vceDisplay}（V_EC=+{vceDisplay}）"
            : "放大区 (Active Region)";

        // ---- 2) AC：h 参数微变等效（rbe/hie, hib, hfb）→ Au / Ri / Ro ----
        // 题目给定 h_ie 时直接采用；否则 h_ie = r_bb' + (1+β)U_T/I_EQ（r_bb' 默认共基 50 Ω）。
        double hie = rbeGiven ? rbeGivenValue : RoundResistance(rbb + (1.0 + beta) * ut / dc.Ieq);
        double hib = RoundResistance(hie / (1.0 + beta));
        double hfb = RoundPlain(-beta / (1.0 + beta));
        double rlPrime = RoundResistance(Parallel(rc, rl));
        double au = RoundPlain(beta * rlPrime / hie);
        double ri = RoundResistance(Parallel(re, hib));
        double ro = RoundResistance(rc);
        // 源电压放大倍数：Aus = Au·ri'/(Rs+ri')，Au/ri' 均为舍入后的显示值。
        double aus = rs > 0 ? RoundPlain(au * ri / (ri + rs)) : double.NaN;
        double fl1 = c1 > 0 ? RoundFrequency(1.0 / (2.0 * Math.PI * (ri + rs) * c1)) : 0;
        double fl2 = c2 > 0 && double.IsFinite(rl) ? RoundFrequency(1.0 / (2.0 * Math.PI * (rc + rl) * c2)) : 0;
        double fl = RoundFrequency(Math.Sqrt(fl1 * fl1 + fl2 * fl2));
        if (new[] { hie, hib, ri, ro, au }.Any(v => !double.IsFinite(v)) || ri <= 0 || ro <= 0 ||
            (rs > 0 && !double.IsFinite(aus)))
            return PedagogicalSolution.Failure(CircuitTopologyType.BjtAmplifier, "共基极模型参数超出数值范围。");

        solution.QPoint = new QPointInfo
        {
            Ibq = TxtCurrent(dc.Ibq),
            Icq = TxtCurrent(dc.Icq),
            Vceq = vceDisplay,
            WorkingRegion = regionName,
            IsActiveRegion = true,
            ExactIbq = ExactText(dc.IbqExact, QuantityKind.Current),
            ExactIcq = ExactText(dc.IcqExact, QuantityKind.Current),
            ExactVceq = ExactText(dc.VceqExact, QuantityKind.Voltage)
        };
        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = Plain(au),
            InputResistance = TxtResistance(ri),
            OutputResistance = TxtResistance(ro),
            LowerCutoffFreq = fl > 0 ? TxtFrequency(fl) : "0 Hz（无耦合电容）",
            UpperCutoffFreq = "未求解（缺少高频器件参数）",
            Bandwidth = "未求解",
            HasSourceResistance = rs > 0,
            SourceResistance = rs > 0 ? TxtResistance(rs) : "—",
            SourceVoltageGain = rs > 0 ? Plain(aus) : "—"
        };
        solution.SummaryText = isPnp
            ? $"PNP 共基极放大电路求解完成（{materialName}，V_EB={vdropText}）：直流等效电路求得 Q 点（U_B={TxtVoltage(dc.Ub)}，I_EQ={TxtCurrent(dc.Ieq)}，I_CQ={TxtCurrent(dc.Icq)}，I_BQ={TxtCurrent(dc.Ibq)}，U_ECQ={vceDisplay}，即 |U_CE|）。PNP 方向：I_E 由 VCC 经发射极流入、I_B 自基极流出、I_C 由集电极流出经 R_C 到地；U_CE = -{vceDisplay}（V_EC = +{vceDisplay}）。交流等效电路求得 h_ie = {TxtResistance(hie)}，h_ib = {TxtResistance(hib)}，电压放大倍数 Au = {Plain(au)}（同相），输入电阻 ri' = {TxtResistance(ri)}，输出电阻 ro' = {TxtResistance(ro)}。" +
              (rs > 0 ? $" 信号源内阻 Rs = {TxtResistance(rs)}，源电压放大倍数 Aus = Au·ri'/(Rs+ri') = {Plain(aus)}。" : string.Empty)
            : $"BJT 共基极放大电路求解完成（{materialName}，V_BE={vdropText}）：直流等效电路求得 Q 点（U_B={TxtVoltage(dc.Ub)}，I_EQ={TxtCurrent(dc.Ieq)}，I_CQ={TxtCurrent(dc.Icq)}，I_BQ={TxtCurrent(dc.Ibq)}，U_CEQ={vceDisplay}）。交流等效电路求得 h_ie = {TxtResistance(hie)}，h_ib = {TxtResistance(hib)}，电压放大倍数 Au = {Plain(au)}（同相），输入电阻 ri' = {TxtResistance(ri)}，输出电阻 ro' = {TxtResistance(ro)}。" +
              (rs > 0 ? $" 信号源内阻 Rs = {TxtResistance(rs)}，源电压放大倍数 Aus = Au·ri'/(Rs+ri') = {Plain(aus)}。" : string.Empty);

        var stepValues = BuildStepValues(dc, hie, au, ri, ro, fl, 0, 0, beta, vcc, 0, rs, aus,
            rbb, hie, rbeSource);
        stepValues["hie"] = Raw(hie);
        stepValues["hib"] = Raw(hib);
        stepValues["hfb"] = Raw(hfb);

        // ---- 3) 教学推导步骤：DC →（极性核验）→ AC ----
        string vceTex = isPnp ? @"U_{ECQ}" : @"U_{CEQ}";
        string ieqNumerator = isPnp
            ? $@"{LxVoltage(vcc)}-{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}"
            : $@"{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}";
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "直流等效电路：静态工作点 (Q-Point) 求解（教材估算法）",
            MethodName = "教材估算法：分压偏置 U_B=V_CC·R_b2/(R_b1+R_b2)（忽略基极电流加载）",
            Content = "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "按教材手算口径取空载分压 U_B=V_CC·R_b2/(R_b1+R_b2)，由发射极回路求 I_EQ，再按 I_BQ=I_EQ/(1+β)、I_CQ=β·I_BQ 与集电极–发射极回路 KVL 求 Q 点。" +
                "每个中间结果按显示单位保留两位小数后代入下一步。",
            LatexFormula = isPnp
                ? @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{V_{CC}-U_B-V_{EB}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-I_{CQ}R_C-I_{EQ}R_E"
                : @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{U_B-V_{BE}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-I_{CQ}R_C-I_{EQ}R_E",
            SubstitutionFormula =
                $@"U_B={LxVoltage(vcc)}\times\frac{{{LxResistance(rb2)}}}{{{LxResistance(rb1)}+{LxResistance(rb2)}}}={LxVoltage(dc.Ub)},\quad " +
                $@"I_{{EQ}}=\frac{{{ieqNumerator}}}{{{LxResistance(re)}}}={LxCurrent(dc.Ieq)},\quad " +
                $@"I_{{BQ}}=\frac{{{LxCurrent(dc.Ieq)}}}{{1+{Plain(beta)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-{LxCurrent(dc.Icq)}\times{LxResistance(rc)}-{LxCurrent(dc.Ieq)}\times{LxResistance(re)}={LxVoltage(dc.Vceq)}",
            ResultValue = isPnp
                ? $"Q点: U_B={TxtVoltage(dc.Ub)}, I_EQ={TxtCurrent(dc.Ieq)}, I_CQ={TxtCurrent(dc.Icq)}, I_BQ={TxtCurrent(dc.Ibq)}, U_ECQ={vceDisplay}（即 |U_CE|），放大区"
                : $"Q点: U_B={TxtVoltage(dc.Ub)}, I_EQ={TxtCurrent(dc.Ieq)}, I_CQ={TxtCurrent(dc.Icq)}, I_BQ={TxtCurrent(dc.Ibq)}, U_CEQ={vceDisplay}，放大区",
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        if (!CheckElectrolyticPolarity(solution, netlist, vb, vc, ve, vcc))
        {
            ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, rc, vdrop, beta);
            RenumberSteps(solution.Steps);
            return solution;
        }

        string acIntro = "画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路，晶体管用 h 参数（r_be、βi_b）微变模型替代。";

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "交流等效电路：晶体管 h 参数微变等效参数求解",
            MethodName = "晶体管低频 h 参数模型",
            Content = acIntro + (rbeGiven
                ? $"题目给定 h_ie=r_be={TxtResistance(hie)}（直接采用，不再用 r_bb' 公式计算；r_bb'={TxtResistance(rbb)} 仅作记录），再求 h_ib=h_ie/(1+β)、h_fb=−β/(1+β)。"
                : $"由直流等效电路已求得的 I_EQ = {TxtCurrent(dc.Ieq)} 计算共射输入电阻 h_ie=r_bb'+(1+β)U_T/I_EQ（r_bb' = {TxtResistance(rbb)}，可由元件参数 rbb' 调节），再求 h_ib=h_ie/(1+β)、h_fb=−β/(1+β)。公式不做代数化简。"),
            LatexFormula = rbeGiven
                ? @"h_{ie}=r_{be}\;\text{（题目给定）},\quad h_{ib} = \frac{h_{ie}}{1+\beta},\quad h_{fb} = \frac{-\beta}{1+\beta}"
                : @"h_{ie} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}},\quad h_{ib} = \frac{h_{ie}}{1+\beta},\quad h_{fb} = \frac{-\beta}{1+\beta}",
            SubstitutionFormula = rbeGiven
                ? $@"h_{{ie}}={LxResistance(hie)}\quad(\text{{题目给定，直接采用}}),\quad h_{{ib}} = \frac{{{LxResistance(hie)}}}{{1+{Plain(beta)}}} = {LxResistance(hib)},\quad h_{{fb}} = \frac{{-{Plain(beta)}}}{{1+{Plain(beta)}}} = {LxRatio(hfb)}"
                : $@"h_{{ie}} = {LxResistance(rbb)} + (1+{Plain(beta)})\frac{{{LxVoltage(ut)}}}{{{LxCurrent(dc.Ieq)}}} = {LxResistance(hie)},\quad h_{{ib}} = \frac{{{LxResistance(hie)}}}{{1+{Plain(beta)}}} = {LxResistance(hib)},\quad h_{{fb}} = \frac{{-{Plain(beta)}}}{{1+{Plain(beta)}}} = {LxRatio(hfb)}",
            ResultValue = rbeGiven
                ? $"hie = {TxtResistance(hie)}（题目给定），hib = {TxtResistance(hib)}，hfb = {Plain(hfb)}"
                : $"hie = {TxtResistance(hie)}（r_bb' = {TxtResistance(rbb)}），hib = {TxtResistance(hib)}，hfb = {Plain(hfb)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "共基放大电路中频电压增益 Au 推导与求解",
            MethodName = "微变等效电路交流分析",
            Content = acIntro + "交流通路中基极经旁路电容对地交流短路，输入信号由发射极注入、输出由集电极引出；输出与输入同相，A_u=β(R_C∥R_L)/h_ie>0。" +
                (rs > 0 ? $" 信号源内阻 R_s={TxtResistance(rs)} 与输入电阻 ri' 构成分压，故源电压放大倍数 A_us=A_u·ri'/(R_s+ri')。" : string.Empty),
            LatexFormula = rs > 0
                ? @"A_u = \frac{u_o}{u_i} = \frac{\beta (R_C \parallel R_L)}{h_{ie}} = -\frac{h_{fb}(R_C \parallel R_L)}{h_{ib}},\quad A_{us}=A_u\frac{r_i'}{R_s+r_i'}"
                : @"A_u = \frac{u_o}{u_i} = \frac{\beta (R_C \parallel R_L)}{h_{ie}} = -\frac{h_{fb}(R_C \parallel R_L)}{h_{ib}}",
            SubstitutionFormula = rs > 0
                ? $@"A_u = \frac{{{Plain(beta)}\times\left({LxResistance(rc)}\parallel{LxResistance(rl)}\right)}}{{{LxResistance(hie)}}} = {LxRatio(au)},\quad A_{{us}}={LxRatio(au)}\times\frac{{{LxResistance(ri)}}}{{{LxResistance(rs)}+{LxResistance(ri)}}}={LxRatio(aus)}"
                : $@"A_u = \frac{{{Plain(beta)}\times\left({LxResistance(rc)}\parallel{LxResistance(rl)}\right)}}{{{LxResistance(hie)}}} = {LxRatio(au)}",
            ResultValue = rs > 0
                ? $"Au = {Plain(au)}（同相放大），Aus = {Plain(aus)}（Rs={TxtResistance(rs)}）"
                : $"Au = {Plain(au)}（同相放大）",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "放大电路输入电阻 Ri' 求解",
            MethodName = "发射极输入回路等效阻抗",
            Content = acIntro + "共基放大电路输入端由发射极与基极（交流地）构成，输入电阻为发射极偏置电阻 R_E 与晶体管共基输入电阻 h_ib 并联，因此输入电阻很低。",
            LatexFormula = @"r_i' = R_E \parallel h_{ib} = R_E \parallel \frac{h_{ie}}{1+\beta}",
            SubstitutionFormula = $@"r_i' = {LxResistance(re)}\parallel{LxResistance(hib)} = \frac{{{LxResistance(re)}\times{LxResistance(hib)}}}{{{LxResistance(re)}+{LxResistance(hib)}}} = {LxResistance(ri)}",
            ResultValue = $"ri' = {TxtResistance(ri)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "放大电路输出电阻 Ro' 求解",
            MethodName = "集电极输出回路等效阻抗",
            Content = acIntro + "将输入信号源置零，从输出端向电路内部看入：集电极受控电流源等效为开路，输出电阻等于集电极电阻 R_C。",
            LatexFormula = @"r_o' = R_C",
            SubstitutionFormula = $@"r_o' = {LxResistance(rc)} = {LxResistance(ro)}",
            ResultValue = $"ro' = {TxtResistance(ro)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, rc, vdrop, beta);
        RenumberSteps(solution.Steps);
        return solution;
    }

    // =====================================================================
    // 共集电极（射极跟随器）：DC 分压/固定偏置教材估算法；AC 微变等效。
    // =====================================================================
    private static PedagogicalSolution SolveCommonCollector(
        PedagogicalSolution solution,
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        double beta,
        double vcc,
        double rb1,
        double rb2,
        double re,
        double rl,
        double c1,
        double c2,
        double rbb,
        bool rbeGiven,
        double rbeGivenValue,
        double rs,
        bool isPnp,
        double vdrop,
        string materialName,
        string polarityName,
        bool dividerBias)
    {
        double ut = parameters.ThermalVoltageUt > 0 ? parameters.ThermalVoltageUt : 0.026;
        string rbeSource = rbeGiven ? "given" : "computed";
        double fixedBiasResistor = isPnp ? rb2 : rb1;
        string biasName = dividerBias ? "分压偏置" : "固定偏置";

        // ---- 1) DC：教材估算法 ----
        DcSolution dc = ComputeDcPoint("CommonCollector", isPnp, dividerBias, commonCollector: true,
            vcc, rb1, rb2, 0, re, fixedBiasResistor, vdrop, beta);
        string? dcError = ValidateDcRegion(dc, isPnp, vdrop);
        if (dcError != null)
            return CollectDcFailureDiagnostics(solution, netlist, dc, dcError, isPnp, vcc, vdrop);

        // ---- 2) AC：微变等效（教材公式，各中间量按显示单位舍入后回代）----
        double rbe = rbeGiven ? rbeGivenValue : RoundResistance(rbb + (1 + beta) * ut / dc.Ieq);
        double rePrime = RoundResistance(Parallel(re, rl));
        double emitterReflected = RoundResistance((1 + beta) * rePrime);
        double av = RoundPlain(emitterReflected / (rbe + emitterReflected));
        double rbEquiv = RoundResistance(dividerBias ? rb1 * rb2 / (rb1 + rb2) : fixedBiasResistor);
        double ri = RoundResistance(Parallel(rbEquiv, rbe + emitterReflected));
        double ro = RoundResistance(Parallel(re, (rbe + rbEquiv) / (1 + beta)));
        // 源电压放大倍数：Aus = Av·Ri/(Rs+Ri)，Av/Ri 均为舍入后的显示值（射极跟随器同理）。
        double aus = rs > 0 ? RoundPlain(av * ri / (ri + rs)) : double.NaN;
        double fl1 = c1 > 0 ? RoundFrequency(1.0 / (2.0 * Math.PI * (ri + rs) * c1)) : 0;
        double fl2 = c2 > 0 && double.IsFinite(rl) ? RoundFrequency(1.0 / (2.0 * Math.PI * (ro + rl) * c2)) : 0;
        double fl = RoundFrequency(Math.Sqrt(fl1 * fl1 + fl2 * fl2));
        if (new[] { rbe, rePrime, emitterReflected, av, ri, ro, fl }.Any(v => !double.IsFinite(v)) || ri <= 0 || ro <= 0 || av <= 0 || av >= 1 ||
            (rs > 0 && !double.IsFinite(aus)))
            return PedagogicalSolution.Failure(CircuitTopologyType.BjtAmplifier,
                "共集电极模型参数超出数值范围（要求 0 < Av < 1、Ri > 0、Ro > 0），未生成有效结果。");

        // ---- 3) 节点电位（电解电容极性校核）----
        double ve = isPnp ? vcc - dc.IeqExact * re : dc.IeqExact * re;
        double vb = isPnp ? ve - vdrop : ve + vdrop;
        double vc = isPnp ? 0.0 : vcc;

        string vceDisplay = TxtVoltage(dc.Vceq);
        string vdropText = DropText(vdrop);
        string regionName = isPnp
            ? $"放大区 (Active Region)；射极跟随器（共集电极，{biasName}）；PNP: U_CE = -{vceDisplay}（V_EC=+{vceDisplay}）"
            : $"放大区 (Active Region)；射极跟随器（共集电极，{biasName}）";

        solution.CircuitTitle = $"{(isPnp ? "PNP " : "")}双极型晶体管 (BJT) 射极跟随器（共集电极）交流小信号分析";
        solution.QPoint = new QPointInfo
        {
            Ibq = TxtCurrent(dc.Ibq),
            Icq = TxtCurrent(dc.Icq),
            Vceq = vceDisplay,
            WorkingRegion = regionName,
            IsActiveRegion = true,
            ExactIbq = ExactText(dc.IbqExact, QuantityKind.Current),
            ExactIcq = ExactText(dc.IcqExact, QuantityKind.Current),
            ExactVceq = ExactText(dc.VceqExact, QuantityKind.Voltage)
        };
        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = Plain(av),
            InputResistance = TxtResistance(ri),
            OutputResistance = TxtResistance(ro),
            LowerCutoffFreq = fl > 0 ? TxtFrequency(fl) : "0 Hz（无耦合电容）",
            UpperCutoffFreq = "未求解（共集电极高频极点未建模）",
            Bandwidth = "未求解",
            HasSourceResistance = rs > 0,
            SourceResistance = rs > 0 ? TxtResistance(rs) : "—",
            SourceVoltageGain = rs > 0 ? Plain(aus) : "—"
        };
        solution.SummaryText = isPnp
            ? $"PNP 共集电极（射极跟随器）求解完成（{materialName}，V_EB={vdropText}，{biasName}）：直流等效电路求得 Q 点（I_BQ={TxtCurrent(dc.Ibq)}，I_CQ={TxtCurrent(dc.Icq)}，I_EQ={TxtCurrent(dc.Ieq)}，U_ECQ={vceDisplay}，即 |U_CE|）。PNP 方向：I_E 由 VCC 经发射极电阻 R_E 流入发射极、I_B 自基极流出、I_C 由集电极流出至地；U_CE = -{vceDisplay}（V_EC = +{vceDisplay}）。交流等效电路求得中频电压放大倍数 Av = {Plain(av)}（同相、0 < Av < 1，射极跟随），R_e' = R_E∥R_L = {TxtResistance(rePrime)}，输入电阻 Ri = {TxtResistance(ri)}，输出电阻 Ro = {TxtResistance(ro)}。" +
              (rs > 0 ? $" 信号源内阻 Rs = {TxtResistance(rs)}，源电压放大倍数 Aus = Av·Ri/(Rs+Ri) = {Plain(aus)}。" : string.Empty)
            : $"BJT 共集电极（射极跟随器）求解完成（{materialName}，V_BE={vdropText}，{biasName}）：直流等效电路求得 Q 点（I_BQ={TxtCurrent(dc.Ibq)}，I_CQ={TxtCurrent(dc.Icq)}，I_EQ={TxtCurrent(dc.Ieq)}，U_CEQ={vceDisplay}）。交流等效电路求得中频电压放大倍数 Av = {Plain(av)}（同相、0 < Av < 1，射极跟随），R_e' = R_E∥R_L = {TxtResistance(rePrime)}，输入电阻 Ri = {TxtResistance(ri)}，输出电阻 Ro = {TxtResistance(ro)}。" +
              (rs > 0 ? $" 信号源内阻 Rs = {TxtResistance(rs)}，源电压放大倍数 Aus = Av·Ri/(Rs+Ri) = {Plain(aus)}。" : string.Empty);

        var stepValues = BuildStepValues(dc, rbe, av, ri, ro, fl, 0, 0, beta, vcc, fixedBiasResistor, rs, aus,
            rbb, rbe, rbeSource);
        stepValues["RePrime"] = Raw(rePrime);
        stepValues["EmitterReflected"] = Raw(emitterReflected);
        stepValues["Rb"] = Raw(rbEquiv);
        stepValues["fL1"] = Raw(fl1);
        stepValues["fL2"] = Raw(fl2);

        // ---- 4) 教学推导步骤：DC →（极性核验）→ AC ----
        string vceTex = isPnp ? @"U_{ECQ}" : @"U_{CEQ}";
        string dcLatexFormula;
        string dcSubstitution;
        string dcMethodName;
        string dcContent;
        if (dividerBias)
        {
            dcLatexFormula = isPnp
                ? @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{V_{CC}-U_B-V_{EB}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-I_{EQ}R_E"
                : @"U_B=V_{CC}\frac{R_{b2}}{R_{b1}+R_{b2}},\quad I_{EQ}=\frac{U_B-V_{BE}}{R_E},\quad I_{BQ}=\frac{I_{EQ}}{1+\beta},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-I_{EQ}R_E";
            string ieqNumerator = isPnp
                ? $@"{LxVoltage(vcc)}-{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}"
                : $@"{LxVoltage(dc.Ub)}-{DropLatex(vdrop)}";
            dcSubstitution =
                $@"U_B={LxVoltage(vcc)}\times\frac{{{LxResistance(rb2)}}}{{{LxResistance(rb1)}+{LxResistance(rb2)}}}={LxVoltage(dc.Ub)},\quad " +
                $@"I_{{EQ}}=\frac{{{ieqNumerator}}}{{{LxResistance(re)}}}={LxCurrent(dc.Ieq)},\quad " +
                $@"I_{{BQ}}=\frac{{{LxCurrent(dc.Ieq)}}}{{1+{Plain(beta)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-{LxCurrent(dc.Ieq)}\times{LxResistance(re)}={LxVoltage(dc.Vceq)}";
            dcMethodName = "教材估算法：分压偏置 U_B=V_CC·R_b2/(R_b1+R_b2)（忽略基极电流加载）+ 发射极回路";
            dcContent = "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "射极跟随器的集电极直接接电源轨（PNP 镜像为直接接地），输出取自发射极、发射极经 R_E 构成直流通路。" +
                "按教材手算口径取空载分压 U_B，由发射极回路求 I_EQ，再按 I_BQ=I_EQ/(1+β)、I_CQ=β·I_BQ 与 U_CEQ=V_CC−I_EQ·R_E 求 Q 点；每个中间结果按显示单位保留两位小数后代入下一步。";
        }
        else
        {
            dcLatexFormula = isPnp
                ? @"I_{BQ}=\frac{V_{CC}-V_{EB}}{R_B+(1+\beta)R_E},\quad I_{EQ}=(1+\beta)I_{BQ},\quad I_{CQ}=\beta I_{BQ},\quad U_{ECQ}=V_{CC}-I_{EQ}R_E"
                : @"I_{BQ}=\frac{V_{CC}-V_{BE}}{R_B+(1+\beta)R_E},\quad I_{EQ}=(1+\beta)I_{BQ},\quad I_{CQ}=\beta I_{BQ},\quad U_{CEQ}=V_{CC}-I_{EQ}R_E";
            dcSubstitution =
                $@"I_{{BQ}}=\frac{{{LxVoltage(vcc)}-{DropLatex(vdrop)}}}{{{LxResistance(fixedBiasResistor)}+(1+{Plain(beta)})\times{LxResistance(re)}}}={LxCurrent(dc.Ibq)},\quad " +
                $@"I_{{EQ}}=(1+{Plain(beta)})\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Ieq)},\quad " +
                $@"I_{{CQ}}={Plain(beta)}\times{LxCurrent(dc.Ibq)}={LxCurrent(dc.Icq)},\quad " +
                $@"{vceTex}={LxVoltage(vcc)}-{LxCurrent(dc.Ieq)}\times{LxResistance(re)}={LxVoltage(dc.Vceq)}";
            dcMethodName = "教材估算法：固定偏置基极回路 KVL（含发射极电阻折算）";
            dcContent = "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" +
                "固定偏置的唯一基极电阻 R_B 接偏置电源（NPN：B–VCC；PNP：B–GND），发射极电流在 R_E 上的压降折算到基极回路为 (1+β)R_E，故 I_BQ=(V_CC−V_BE)/[R_B+(1+β)R_E]；再求 I_EQ、I_CQ 与 U_CEQ。" +
                "每个中间结果按显示单位保留两位小数后代入下一步。";
        }
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "直流等效电路：射极跟随器静态工作点 (Q-Point) 求解（教材估算法）",
            MethodName = dcMethodName,
            Content = dcContent,
            LatexFormula = dcLatexFormula,
            SubstitutionFormula = dcSubstitution,
            ResultValue = isPnp
                ? $"Q点: I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, I_EQ={TxtCurrent(dc.Ieq)}, U_ECQ={vceDisplay}（即 |U_CE|），放大区"
                : $"Q点: I_BQ={TxtCurrent(dc.Ibq)}, I_CQ={TxtCurrent(dc.Icq)}, I_EQ={TxtCurrent(dc.Ieq)}, U_CEQ={vceDisplay}，放大区",
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        if (!CheckElectrolyticPolarity(solution, netlist, vb, vc, ve, vcc))
        {
            ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, 0, vdrop, beta);
            RenumberSteps(solution.Steps);
            return solution;
        }

        string acIntro = "画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路，晶体管用 h 参数（r_be、βi_b）微变模型替代。";

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "交流等效电路：晶体管低频小信号微变等效参数 rbe 计算",
            MethodName = "晶体管 h 参数简化模型",
            Content = acIntro + (rbeGiven
                ? $"题目给定 r_be=h_ie={TxtResistance(rbe)}（直接采用，不再用 r_bb' 公式计算；r_bb'={TxtResistance(rbb)} 仅作记录），下游 Av/Ri/Ro 均使用给定的 r_be。"
                : $"共集电极的 r_be 与共射电路同形，由直流等效电路求得的 I_EQ = {TxtCurrent(dc.Ieq)} 决定：r_be=r_bb'+(1+β)U_T/I_EQ（r_bb' = {TxtResistance(rbb)}，可由元件参数 rbb' 调节）。"),
            LatexFormula = rbeGiven
                ? @"r_{be}=h_{ie}"
                : @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}}",
            SubstitutionFormula = rbeGiven
                ? $@"r_{{be}}=h_{{ie}}={LxResistance(rbe)}\quad(\text{{题目给定，直接采用}})"
                : $@"r_{{be}} = {LxResistance(rbb)} + (1+{Plain(beta)})\frac{{{LxVoltage(ut)}}}{{{LxCurrent(dc.Ieq)}}} = {LxResistance(rbe)}",
            ResultValue = rbeGiven
                ? $"rbe = hie = {TxtResistance(rbe)}（题目给定，直接采用）"
                : $"rbe = {TxtResistance(rbe)}（r_bb' = {TxtResistance(rbb)}）",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "中频段交流电压放大倍数 Av 推导与求解（射极跟随）",
            MethodName = "微变等效电路交流分析",
            Content = acIntro + "交流等效电路中集电极对地（交流地）短路，基极输入、发射极输出，R_E 与 R_L 并联构成发射极等效负载 R_e'；输出电压几乎跟随输入电压，A_v=(1+β)R_e'/[r_be+(1+β)R_e'] 为正且略小于 1。" +
                (rs > 0 ? $" 信号源内阻 R_s={TxtResistance(rs)} 与输入电阻 Ri 构成分压，故源电压放大倍数 A_us=A_v·Ri/(R_s+Ri)。" : string.Empty),
            LatexFormula = rs > 0
                ? @"R_e'=R_E\parallel R_L,\quad A_v=\frac{(1+\beta)R_e'}{r_{be}+(1+\beta)R_e'},\quad A_{us}=A_v\frac{R_i}{R_s+R_i}"
                : @"R_e'=R_E\parallel R_L,\quad A_v=\frac{(1+\beta)R_e'}{r_{be}+(1+\beta)R_e'}",
            SubstitutionFormula = rs > 0
                ? $@"R_e' = {LxResistance(re)}\parallel{LxResistance(rl)} = {LxResistance(rePrime)},\quad " +
                  $@"A_v = \frac{{(1+{Plain(beta)})\times{LxResistance(rePrime)}}}{{{LxResistance(rbe)}+(1+{Plain(beta)})\times{LxResistance(rePrime)}}} = {LxRatio(av)},\quad " +
                  $@"A_{{us}}={LxRatio(av)}\times\frac{{{LxResistance(ri)}}}{{{LxResistance(rs)}+{LxResistance(ri)}}}={LxRatio(aus)}"
                : $@"R_e' = {LxResistance(re)}\parallel{LxResistance(rl)} = {LxResistance(rePrime)},\quad " +
                  $@"A_v = \frac{{(1+{Plain(beta)})\times{LxResistance(rePrime)}}}{{{LxResistance(rbe)}+(1+{Plain(beta)})\times{LxResistance(rePrime)}}} = {LxRatio(av)}",
            ResultValue = rs > 0
                ? $"Av = {Plain(av)}（同相，0 < Av < 1），Aus = {Plain(aus)}（Rs={TxtResistance(rs)}）"
                : $"Av = {Plain(av)}（同相，0 < Av < 1）",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "放大电路输入电阻 Ri 求解",
            MethodName = "基极回路输入阻抗折算",
            Content = acIntro + "输入电阻为从基极看入的等效电阻：偏置电阻等效 R_b 与「r_be+(1+β)R_e'」并联；发射极负载折算到基极回路后被放大 (1+β) 倍，故共集电极电路输入电阻很高。",
            LatexFormula = @"R_i=R_b\parallel\left[r_{be}+(1+\beta)R_e'\right]",
            SubstitutionFormula = $@"R_i = {LxResistance(rbEquiv)}\parallel\left[{LxResistance(rbe)}+(1+{Plain(beta)})\times{LxResistance(rePrime)}\right] = {LxResistance(ri)}",
            ResultValue = $"Ri = {TxtResistance(ri)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "放大电路输出电阻 Ro 求解",
            MethodName = "输出端等效电阻定义法",
            Content = acIntro + "将输入信号源置零并去除外接负载，从发射极输出端看入：R_E 与「(r_be+R_b)/(1+β)」并联；基极回路的 r_be 与 R_b 折算到发射极后被减小 (1+β) 倍，故共集电极电路输出电阻很低、带负载能力强。",
            LatexFormula = @"R_o=R_E\parallel\frac{r_{be}+R_b}{1+\beta}",
            SubstitutionFormula = $@"R_o = {LxResistance(re)}\parallel\frac{{{LxResistance(rbe)}+{LxResistance(rbEquiv)}}}{{1+{Plain(beta)}}} = {LxResistance(ro)}",
            ResultValue = $"Ro = {TxtResistance(ro)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 6,
            BadgeText = "第 6 步",
            Title = "下限截止频率估算（耦合电容时间常数）",
            MethodName = "开路/短路时间常数法（低频）",
            Content = acIntro + "共集电极电路无发射极旁路电容，下限截止频率由输入/输出耦合电容的时间常数决定；该值为教材近似估算，高频极点本轮未建模。",
            LatexFormula = @"f_{L1}=\frac{1}{2\pi R_i C_1},\quad f_{L2}=\frac{1}{2\pi (R_o+R_L) C_2},\quad f_L=\sqrt{f_{L1}^2+f_{L2}^2}",
            SubstitutionFormula = $@"f_L \approx {LxFrequency(fl)}",
            ResultValue = $"fL ≈ {TxtFrequency(fl)}（教材估算）",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        ApplyLoadedBiasDerivation(solution, dc, isPnp, vcc, re, 0, vdrop, beta);
        RenumberSteps(solution.Steps);
        return solution;
    }

    // =====================================================================
    // 电解电容极性核验（DC 相）：紧跟 Q 点步骤之后，Phase=DcEquivalent。
    // =====================================================================
    private static PedagogicalSolution CollectDcFailureDiagnostics(
        PedagogicalSolution solution, ElectricalNetlist netlist, DcSolution dc, string dcError,
        bool isPnp, double vcc, double vdrop)
    {
        solution.IsSuccess = false;
        solution.SummaryText = dcError;
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            BadgeText = "工作区错误", Title = "三极管直流工作区检查", Content = dcError,
            ResultValue = "不满足放大区分析条件", Status = "Error", Phase = SolutionPhase.DcEquivalent
        });
        // An invalid forward-active solution is not a physical Q point. Only
        // rails and provable base-voltage bounds are available for polarity checks.
        bool forwardDrive = double.IsFinite(dc.IbqExact) && dc.IbqExact > 0;
        var transistor = netlist.Elements.Values.First(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        double RailVoltage(ElectricalNet? net) => net == null ? double.NaN
            : net.IsGround || net == netlist.GroundNet ? 0
            : net.IsPower || net.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase) ? vcc : double.NaN;
        double emitter = RailVoltage(netlist.FindNetForPin(transistor.Id, "E"));
        double collector = RailVoltage(netlist.FindNetForPin(transistor.Id, "C"));
        double baseVoltage = forwardDrive && double.IsFinite(emitter)
            ? emitter + (isPnp ? -vdrop : vdrop) : double.NaN;
        CheckElectrolyticPolarity(solution, netlist, baseVoltage, collector, emitter, vcc,
            dcError, forwardDrive ? (isPnp ? 0 : vdrop) : double.NaN,
            forwardDrive ? (isPnp ? vcc - vdrop : vcc) : double.NaN);
        RenumberSteps(solution.Steps);
        return solution;
    }

    private static bool CheckElectrolyticPolarity(
        PedagogicalSolution solution,
        ElectricalNetlist netlist,
        double vB,
        double vC,
        double vE,
        double vcc,
        string? dcError = null,
        double baseMinimum = double.NaN,
        double baseMaximum = double.NaN)
    {
        var bjt = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        var bNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "B") : null;
        var cNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "C") : null;
        var eNet = bjt != null ? netlist.FindNetForPin(bjt.Id, "E") : null;
        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));

        var polarCaps = netlist.Elements.Values.Where(e => e.Type == ElementType.CapacitorPolarized).ToList();
        if (polarCaps.Count == 0 && dcError == null) return true;
        var errors = new List<string>();
        if (dcError != null) errors.Add(dcError);
        var pending = new List<string>();

        double DirectVoltage(ElectricalNet? net)
        {
            if (net == null) return double.NaN;
            if (net.IsGround || net == gndNet) return 0;
            if (net.IsPower || net == vccNet) return vcc;
            if (net == bNet) return vB;
            if (net == cNet) return vC;
            if (net == eNet) return vE;
            return double.NaN;
        }

        (double Min, double Max) VoltageRange(ElectricalNet? start)
        {
            double voltage = DirectVoltage(start);
            if (double.IsFinite(voltage)) return (voltage, voltage);
            if (start == bNet) return (baseMinimum, baseMaximum);
            if (start == null || start == cNet || start == eNet) return (double.NaN, double.NaN);
            // Capacitors are DC-open. A passive source/load island may inherit
            // one reference potential through resistors and zero-DC AC sources.
            // Never silently assign zero to an unknown or conflicting island.
            var visited = new HashSet<ElectricalNet>();
            var queue = new Queue<ElectricalNet>();
            queue.Enqueue(start);
            double reference = double.NaN;
            while (queue.Count > 0)
            {
                var net = queue.Dequeue();
                if (!visited.Add(net)) continue;
                double known = DirectVoltage(net);
                if (double.IsFinite(known))
                {
                    if (double.IsFinite(reference) && Math.Abs(reference - known) > 1e-9)
                        return (double.NaN, double.NaN);
                    reference = known;
                    continue;
                }
                if (net == bNet || net == cNet || net == eNet) return (double.NaN, double.NaN);
                foreach (var pin in net.ConnectedPins)
                {
                    if (!netlist.Elements.TryGetValue(pin.ElementId, out var el) ||
                        el.Type is not (ElementType.Resistor or ElementType.AcVoltageSource)) continue;
                    foreach (var other in netlist.Nets.Where(n => n != net && n.ContainsElement(el.Id)))
                        queue.Enqueue(other);
                }
            }
            return (reference, reference);
        }

        string RangeText((double Min, double Max) range) => range.Min == range.Max
            ? TxtVoltage(range.Min) : $"[{TxtVoltage(range.Min)}, {TxtVoltage(range.Max)}]（电位范围）";

        foreach (var cap in polarCaps)
        {
            var posNet = netlist.FindNetForPin(cap.Id, "+");
            var negNet = netlist.FindNetForPin(cap.Id, "-");

            var positive = VoltageRange(posNet);
            var negative = VoltageRange(negNet);
            bool knownRanges = double.IsFinite(positive.Min) && double.IsFinite(positive.Max) &&
                double.IsFinite(negative.Min) && double.IsFinite(negative.Max);
            bool reversed = knownRanges && positive.Max < negative.Min - 1e-9;
            bool forward = knownRanges && positive.Min >= negative.Max - 1e-9;
            if (!reversed && !forward)
            {
                string message = $"电解电容 {cap.Id} 极性暂无法核验：缺少有效直流工作点或可确定的两端电位，未将放大区试算值用作实际电位。";
                pending.Add(message);
                solution.Steps.Add(new PedagogicalSolutionStep
                {
                    BadgeText = "待核验", Title = $"电解电容 {cap.Id} 极性暂无法核验", Content = message,
                    ResultValue = "待修正其他问题后重新核验", Status = "Warning", Phase = SolutionPhase.DcEquivalent
                });
                continue;
            }
            double vPos = positive.Min, vNeg = negative.Max;
            bool exact = positive.Min == positive.Max && negative.Min == negative.Max;
            string vPosText = RangeText(positive);
            string vNegText = RangeText(negative);
            string deltaText = exact ? TxtVoltage(vPos - vNeg) : reversed
                ? $"≤{TxtVoltage(positive.Max - negative.Min)}" : $"≥{TxtVoltage(positive.Min - negative.Max)}";

            if (reversed)
            {
                errors.Add($"电解电容 {cap.Id} 极性反接：V+={vPosText}，V-={vNegText}，V+−V-={deltaText}");
                solution.Steps.Add(new PedagogicalSolutionStep
                {
                    StepIndex = 0,
                    BadgeText = "极性安全",
                    Title = $"电解电容 {cap.Id} 直流偏置极性安全核验 (反接错误)",
                    MethodName = "电解电容单向绝缘介质层耐压法则",
                    Content = $"检测到电解电容 {cap.Id} 正极网络电位为 {vPosText}，负极网络电位为 {vNegText}。由于铝/钽电解电容阳极氧化膜具有单向导电性，长期承受反向电压将导致氧化膜溶解破坏、漏电流急剧增大、过热产气鼓包甚至炸裂，工程中严禁反向偏置！",
                    LatexFormula = @"U_{cap} = V(+) - V(-) < 0\text{ V}\quad (\text{Reverse Bias Hazard!})",
                    SubstitutionFormula = exact
                        ? $@"U_{{{cap.Id}}} = {LxVoltage(vPos)} - {LxVoltage(vNeg)} = {LxVoltage(vPos - vNeg)} < 0\text{{ V}}"
                        : $@"U_{{{cap.Id}}}\le {LxVoltage(positive.Max - negative.Min)} < 0\text{{ V}}",
                    ResultValue = $"极性反接危险 (ΔV = {deltaText})",
                    Status = "Error",
                    Phase = SolutionPhase.DcEquivalent
                });
            }
            else
            {
                solution.SummaryText += $" [电解电容 {cap.Id} 正偏正常: V+={vPosText}, V-={vNegText}]";
                solution.Steps.Add(new PedagogicalSolutionStep
                {
                    StepIndex = 0,
                    BadgeText = "元件特性",
                    Title = $"电解电容 {cap.Id} 直流偏置与功能特性核验",
                    MethodName = "电解电容工作特性分析",
                    Content = $"电解电容 {cap.Id} 正极电位为 {vPosText}，负极电位为 {vNegText}，处于安全正向偏置状态。电解电容具有单位体积容量大（μF 量级）的特点，主要用于大容量低频滤波、交流旁路与低频隔直耦合；需注意其高频等效串联电阻 (ESR) 较高，高频去耦通常需并联高频陶瓷小电容。",
                    LatexFormula = @"U_{cap} = V(+) - V(-) \ge 0\text{ V}\quad (\text{Safe Forward Bias})",
                    SubstitutionFormula = exact
                        ? $@"U_{{{cap.Id}}} = {LxVoltage(vPos)} - {LxVoltage(vNeg)} = {LxVoltage(vPos - vNeg)} \ge 0\text{{ V}}"
                        : $@"U_{{{cap.Id}}}\ge {LxVoltage(positive.Min - negative.Max)}\ge 0\text{{ V}}",
                    ResultValue = $"极性正常 (耐压裕量 ΔV = {deltaText})",
                    Status = "Completed",
                    Phase = SolutionPhase.DcEquivalent
                });
            }
        }
        if (errors.Count == 0 && pending.Count == 0) return true;

        // Keep the ideal-capacitor DC calculation only as evidence for the
        // polarity diagnosis. Reverse-biased leakage/failure is not modeled;
        // neither the success summary nor any AC results may escape this gate.
        solution.IsSuccess = false;
        solution.CircuitTitle = "电路分析检查未通过";
        solution.SummaryText = (errors.Count > 0 ? $"检测到 {errors.Count} 项错误：\n" : "直流极性检查尚未完成：\n") +
            string.Join("\n", errors.Select((message, index) => $"{index + 1}. {message}")) +
            (pending.Count > 0 ? "\n待核验：\n" + string.Join("\n", pending) : string.Empty) +
            "\n停止交流分析，请修正上述问题后重新求解。保留的直流数值仅为理想电容假设下的偏置试算，不是实际工作点。";
        solution.AcParameters = null;
        if (solution.QPoint != null)
        {
            solution.QPoint.IsActiveRegion = false;
            solution.QPoint.WorkingRegion = "偏置试算（电容反接，实际工作点未求解）";
        }
        foreach (var step in solution.Steps.Where(s => s.Values.Count > 0))
        {
            step.Content += " 本步仅用于检查电容直流极性；反接后的漏电与失效未建模，不能视为实际工作点。";
            step.ResultValue = step.ResultValue.Replace("，放大区", "，仅为偏置试算");
            foreach (string key in step.Values.Keys.Where(k => k is not
                ("IBQ" or "ICQ" or "IEQ" or "VCEQ" or "UB" or "beta" or "VCC" or
                 "IBQ_exact_uA" or "ICQ_exact_mA" or "UCE_exact_V" or "IEQ_exact_uA" or
                 "Vth_V" or "Rth_ohm" or "UB_actual_V" or "Rb" or "Rs_ohm")).ToList())
                step.Values.Remove(key);
        }
        return false;
    }

    // =====================================================================
    // Formatting helpers: every displayed number goes through TeachingDisplay
    // (textbook unit + at most two decimals). LaTeX display uses the same rounding.
    // =====================================================================

    private static double RoundVoltage(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Voltage);
    private static double RoundCurrent(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Current);
    private static double RoundResistance(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Resistance);
    private static double RoundCapacitance(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Capacitance);
    private static double RoundFrequency(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Frequency);
    private static double RoundPlain(double value) => TeachingDisplay.RoundPlain(value);

    private static string TxtVoltage(double value) => TeachingDisplay.Text(value, QuantityKind.Voltage);
    private static string TxtCurrent(double value) => TeachingDisplay.Text(value, QuantityKind.Current);
    private static string TxtResistance(double value) => TeachingDisplay.Text(value, QuantityKind.Resistance);
    private static string TxtFrequency(double value) => TeachingDisplay.Text(value, QuantityKind.Frequency);
    private static string Plain(double value) => TeachingDisplay.Plain(value);

    private static string LxVoltage(double value) => TeachingDisplay.Latex(value, QuantityKind.Voltage);
    private static string LxCurrent(double value) => TeachingDisplay.Latex(value, QuantityKind.Current);
    private static string LxResistance(double value) => TeachingDisplay.Latex(value, QuantityKind.Resistance);
    private static string LxFrequency(double value) => TeachingDisplay.Latex(value, QuantityKind.Frequency);
    private static string LxRatio(double value) => TeachingDisplay.LatexNumber(value, QuantityKind.Ratio);

    /// <summary>PN 结导通压降按教材写法固定用 V（0.3 V / 0.7 V），保留最多两位小数。</summary>
    private static string DropText(double drop) => $"{drop.ToString("0.##", CultureInfo.InvariantCulture)} V";
    private static string DropLatex(double drop) => $@"{drop.ToString("0.##", CultureInfo.InvariantCulture)}\;\text{{V}}";

    /// <summary>
    /// Backend-only exact value text: highest available precision in the textbook display
    /// unit (never shown in the UI; excluded from the two-decimal display convention).
    /// </summary>
    private static string ExactText(double siValue, QuantityKind kind)
    {
        if (!double.IsFinite(siValue)) return siValue > 0 ? "∞" : "-∞";
        DisplayQuantity formatted = TeachingDisplay.Format(siValue, kind);
        if (formatted.Unit.Length == 0) return Raw(siValue);
        double scale = UnitScale(formatted.Unit);
        return $"{Raw(siValue / scale)} {formatted.Unit}";
    }

    private static double UnitScale(string unit) => unit.Length == 0 ? 1.0 : unit[0] switch
    {
        'k' => 1e3,
        'M' => 1e6,
        'm' => 1e-3,
        'μ' => 1e-6,
        'n' => 1e-9,
        'p' => 1e-12,
        _ => 1.0
    };

    private static string Raw(double value) => double.IsPositiveInfinity(value)
        ? "Infinity"
        : double.IsNegativeInfinity(value)
            ? "-Infinity"
            : value.ToString("G6", CultureInfo.InvariantCulture);

    private static void RenumberSteps(List<PedagogicalSolutionStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].StepIndex = i + 1;
            if (string.IsNullOrEmpty(steps[i].BadgeText) || steps[i].BadgeText.StartsWith("第", StringComparison.Ordinal))
                steps[i].BadgeText = $"第 {i + 1} 步";
        }
    }

    /// <summary>Parallel combination; +infinity denotes an open branch.</summary>
    private static double Parallel(double first, double second)
    {
        if (double.IsPositiveInfinity(first)) return second;
        if (double.IsPositiveInfinity(second)) return first;
        if (first <= 0 || second <= 0) return 0.0;
        return first * second / (first + second);
    }

    private static string GetSubtypeDisplayName(string subtype) => subtype switch
    {
        "CommonBase" => "共基极",
        "CommonCollector" => "射极跟随器（共集电极）",
        "VoltageDividerSelfBias" => "分压式自偏置共射",
        "FixedBias" => "固定偏置阻容耦合共射",
        "CollectorFeedbackBias" => "集电极反馈偏置共射",
        _ => "共射极"
    };

    private static double GetSiValue(CircuitElement el)
    {
        return NetlistExtractor.ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol);
    }
}
