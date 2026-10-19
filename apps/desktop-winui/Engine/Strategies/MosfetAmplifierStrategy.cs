// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/MosfetAmplifierStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical strategy for single-MOSFET amplifier circuits using
//              the Level-1 square-law model:
//                CommonSource, CommonSourceWithSourceResistance,
//                CommonSourceBypassed, CommonDrain, CommonGate.
//
//              Display convention (user feedback 2026-10-18, second round):
//                * The displayed derivation follows the textbook hand
//                  calculation: every intermediate result is rounded to at most
//                  two decimals in its textbook display unit (AwayFromZero) and
//                  the rounded displayed value is fed into the next formula
//                  (e.g. the rounded V_G is substituted into the square-law
//                  quadratic; the rounded I_D determines V_GS/V_OV/V_DS; the
//                  rounded V_OV determines gm; the rounded gm determines Av,
//                  Ri, Ro and f_L). Formulas keep every term (no algebraic
//                  simplification).
//                * The derivation is grouped into the two textbook equivalent
//                  circuits: DC equivalent circuit (V_G → I_D → V_DS/Q point) →
//                  AC equivalent circuit (gm, ro, Av, Ri/Ro, f_L). The AC
//                  phase replaces the device by the gm·v_gs + ro small-signal
//                  model.
//
//              Enhancement/depletion MOSFETs and JFETs share the topology solver;
//              JFET parameters and reverse gate bias are checked explicitly.
//
// OWNER: engine-mos (implemented; DetectSubtype is called by TopologyClassifier).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine.Nonlinear;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

/// <summary>
/// Single-MOSFET amplifier teaching strategy (Q point + small-signal
/// Av/Ri/Ro). Supports enhancement/depletion NMOS/PMOS and N/P JFETs.
/// </summary>
public class MosfetAmplifierStrategy : ICircuitStrategy
{
    /// <summary>Subtype strings fixed by the round-6 design contract (section 5.2).</summary>
    private static readonly string[] KnownSubtypes =
    [
        "CommonSource",
        "CommonSourceWithSourceResistance",
        "CommonSourceBypassed",
        "CommonDrain",
        "CommonGate"
    ];

    private readonly string _subtype;

    public MosfetAmplifierStrategy(string subtype)
    {
        _subtype = string.IsNullOrWhiteSpace(subtype) ? "CommonSource" : subtype;
    }

    public CircuitTopologyType TopologyType => CircuitTopologyType.MosfetAmplifier;

    public string SubtypeName => _subtype;

    /// <summary>
    /// Classifies the single-MOSFET amplifier topology from the extracted
    /// netlist. Called by <see cref="TopologyClassifier"/>; never returns null
    /// and never throws (unknown/partial netlists fall back to CommonSource).
    /// </summary>
    public static string DetectSubtype(ElectricalNetlist netlist)
    {
        try
        {
            return DetectSubtypeCore(netlist);
        }
        catch
        {
            // The classifier must always receive a fixed subtype string; the
            // solver will still reject an inconsistent/unknown topology.
            return "CommonSource";
        }
    }

    public Task<PedagogicalSolution> SolveAsync(
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(SolveCore(netlist, parameters ?? new AnalysisParameters()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PedagogicalSolution.Failure(
                TopologyType,
                $"MOSFET 教学求解过程中发生异常：{ex.Message}"));
        }
    }

    // =====================================================================
    // Topology detection (pure connectivity; element ids are never used).
    // =====================================================================

    private static string DetectSubtypeCore(ElectricalNetlist netlist)
    {
        if (netlist == null || netlist.Elements.Count == 0) return "CommonSource";

        var fet = netlist.Elements.Values.FirstOrDefault(e => IsFetElement(e.Type));
        if (fet == null) return "CommonSource";

        var gndNet = netlist.GroundNet;
        var vccNet = ResolveSupplyPositiveNet(netlist);
        if (gndNet == null || vccNet == null) return "CommonSource";

        bool isPChannel = IsPChannelType(fet.Type);
        var sourceRail = isPChannel ? vccNet : gndNet;
        var drainRail = isPChannel ? gndNet : vccNet;

        var gateNet = netlist.FindNetForPin(fet.Id, "G");
        var drainNet = netlist.FindNetForPin(fet.Id, "D");
        var sourceNet = netlist.FindNetForPin(fet.Id, "S");
        if (gateNet == null || drainNet == null || sourceNet == null) return "CommonSource";

        bool IsGroundNet(ElectricalNet? net) => net != null && (net.IsGround || net == gndNet);

        bool HasResistorBetween(ElectricalNet? first, ElectricalNet? second) =>
            netlist.Elements.Values.Any(e => e.Type == ElementType.Resistor && Connects(netlist, e, first, second));

        bool HasCapacitorBetween(ElectricalNet? first, ElectricalNet? second) =>
            netlist.Elements.Values.Any(e =>
                e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized && Connects(netlist, e, first, second));

        // (1) Common gate: the gate is AC-grounded through a bypass capacitor
        //     (to GND or to the supply rail) while the source is not a DC ground,
        //     or the input coupling capacitor feeds the source terminal.
        bool gateAcGrounded = IsGroundNet(gateNet)
                              || HasCapacitorBetween(gateNet, gndNet)
                              || HasCapacitorBetween(gateNet, vccNet);
        bool inputAtSource = false;
        var acSource = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);
        if (acSource != null)
        {
            var acPosNet = netlist.FindNetForPin(acSource.Id, "pos");
            inputAtSource = sourceNet == acPosNet || HasCapacitorBetween(sourceNet, acPosNet);
        }
        if (gateAcGrounded && (inputAtSource || !IsGroundNet(sourceNet))) return "CommonGate";

        // (2) Common drain (source follower): drain tied directly to its rail
        //     (VDD for NMOS, GND for PMOS) and the source has a resistor to its rail.
        bool drainDirectAtRail = drainNet == drainRail;
        bool hasSourceResistor = HasResistorBetween(sourceNet, sourceRail);
        if (drainDirectAtRail && hasSourceResistor) return "CommonDrain";

        // (3) Common source: source tied directly to its rail (GND for NMOS,
        //     VDD for PMOS) => no source degeneration resistor.
        if (!hasSourceResistor) return "CommonSource";

        // (4) Source degeneration: bypassed by a capacitor across Rs?
        bool hasBypassCapacitor = HasCapacitorBetween(sourceNet, sourceRail)
                                  || HasCapacitorBetween(sourceNet, gndNet)
                                  || HasCapacitorBetween(sourceNet, vccNet);
        return hasBypassCapacitor ? "CommonSourceBypassed" : "CommonSourceWithSourceResistance";
    }

    // =====================================================================
    // Solver
    // =====================================================================

    private PedagogicalSolution SolveCore(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        PedagogicalSolution Fail(string message) => PedagogicalSolution.Failure(TopologyType, message);

        if (netlist == null || netlist.Elements.Count == 0)
            return Fail("网表中没有可分析的元件，无法进行 MOSFET 放大电路求解。");

        // ---- 1. Device discovery and scope checks -------------------------
        var fetDevices = netlist.Elements.Values.Where(e => IsFetElement(e.Type)).ToList();
        if (fetDevices.Count == 0)
            return Fail("网表中未找到 MOSFET 器件，无法使用 MOSFET 教学策略。");
        if (fetDevices.Count > 1)
            return Fail($"当前 MOSFET 教学策略只支持单只场效应管，检测到 {fetDevices.Count} 只（{string.Join("、", fetDevices.Select(d => d.Id))}）；多级/级联放大已列入后续开发规划，请先拆分为单管电路。");

        var mos = fetDevices[0];
        foreach (var el in netlist.Elements.Values)
        {
            if (el.Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh
                or ElementType.MosfetNDep or ElementType.MosfetPDep
                or ElementType.Jfet or ElementType.JfetN or ElementType.JfetP
                or ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized
                or ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.VccPort
                or ElementType.Ground)
            {
                continue;
            }

            return Fail($"当前 MOSFET 教学策略不支持器件 {el.Id}（{el.Type}）：只支持 MOSFET/JFET、电阻、电容、直流/交流电压源、VDD 与地参考；含二极管/电感/受控源/双极型晶体管的网络请拆分后分别求解。");
        }

        var validation = netlist.Validate();
        if (!validation.IsValid)
        {
            return Fail(string.Join("\n", validation.Issues
                .Where(i => i.Severity == NetlistValidationSeverity.Error)
                .Select(i => i.Message)));
        }

        // ---- 2. Single supply rail and ground reference -------------------
        var gndNet = netlist.GroundNet;
        if (gndNet == null) return Fail("电路缺少参考地（GND），无法确定直流电位。");

        var supplies = netlist.Elements.Values
            .Where(e => e.Type is ElementType.DcVoltageSource or ElementType.VccPort)
            .ToList();
        if (supplies.Count != 1)
            return Fail("当前 MOSFET 教学策略要求单一正电源 VDD（一个直流电压源或 VCC 端口）。");

        var supply = supplies[0];
        var supplyPosNet = supply.Type == ElementType.VccPort
            ? netlist.FindNetForPin(supply.Id, "VCC")
            : netlist.FindNetForPin(supply.Id, "pos");
        var supplyNegNet = supply.Type == ElementType.VccPort
            ? gndNet
            : netlist.FindNetForPin(supply.Id, "neg");
        if (supplyPosNet == null)
            return Fail($"电源 {supply.Id} 的正端（VDD）未连接任何网络。");
        if (supplyNegNet == null || !(supplyNegNet.IsGround || supplyNegNet == gndNet))
            return Fail($"电源 {supply.Id} 的负端必须接参考地 GND（当前教学策略只支持单电源共地放大电路）。");

        var vccNet = supplyPosNet;
        double vdd = GetSiValue(supply);
        if (!double.IsFinite(vdd) || vdd <= 0)
            return Fail($"VDD 数值无效（电源 {supply.Id}）：必须为有限正电压。");

        if (netlist.Elements.Values.Count(e => e.Type == ElementType.AcVoltageSource) > 1)
            return Fail("当前 MOSFET 教学策略只支持一个交流输入源，用于识别输入耦合电容。");

        // ---- 3. Transistor terminal nets and polarity mirror --------------
        var gateNet = netlist.FindNetForPin(mos.Id, "G");
        var drainNet = netlist.FindNetForPin(mos.Id, "D");
        var sourceNet = netlist.FindNetForPin(mos.Id, "S");
        if (gateNet == null || drainNet == null || sourceNet == null)
            return Fail($"MOSFET {mos.Id} 的栅极/漏极/源极存在未连接引脚，无法确定拓扑。");
        if (gateNet == drainNet || gateNet == sourceNet || drainNet == sourceNet)
            return Fail($"MOSFET {mos.Id} 的栅极/漏极/源极之间存在短路连接，无法进行放大电路分析。");

        bool isPChannel = IsPChannelType(mos.Type);
        var sourceRail = isPChannel ? vccNet : gndNet;
        var drainRail = isPChannel ? gndNet : vccNet;

        bool IsGroundNet(ElectricalNet? net) => net != null && (net.IsGround || net == gndNet);
        bool IsAcGroundNet(ElectricalNet? net) => net != null && (IsGroundNet(net) || net == vccNet);

        // ---- 4. Bias network identification (connectivity only) -----------
        var resistors = netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor).ToList();
        var capacitors = netlist.Elements.Values
            .Where(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized)
            .ToList();
        var usedResistors = new HashSet<string>(StringComparer.Ordinal);
        var unclassifiedResistors = new List<CircuitElement>();

        double rgTop = 0.0;      // gate <-> supply rail
        double rgBottom = 0.0;   // gate <-> ground (signal reference)
        double rd = 0.0;         // drain <-> drain rail
        double rs = 0.0;         // source <-> source rail

        foreach (var resistor in resistors)
        {
            double value = GetSiValue(resistor);
            if (!double.IsFinite(value) || value <= 0)
                return Fail($"电阻 {resistor.Id} 数值无效（{resistor.DisplayNumber} {resistor.UnitSymbol}），无法求解。");

            if (rgTop <= 0 && Connects(netlist, resistor, gateNet, vccNet))
            {
                rgTop = value;
                usedResistors.Add(resistor.Id);
                continue;
            }
            if (rgBottom <= 0 && Connects(netlist, resistor, gateNet, gndNet))
            {
                rgBottom = value;
                usedResistors.Add(resistor.Id);
                continue;
            }
            if (rd <= 0 && Connects(netlist, resistor, drainNet, drainRail))
            {
                rd = value;
                usedResistors.Add(resistor.Id);
                continue;
            }
            if (rs <= 0 && Connects(netlist, resistor, sourceNet, sourceRail))
            {
                rs = value;
                usedResistors.Add(resistor.Id);
                continue;
            }
            if (Connects(netlist, resistor, gateNet, drainNet))
            {
                return Fail($"检测到栅-漏反馈电阻 {resistor.Id}：当前 MOSFET 教学模型未覆盖该偏置方式，已停止推导，不会套用错误公式。");
            }

            unclassifiedResistors.Add(resistor);
        }

        // ---- 5. Effective subtype (fixed strings from the contract) -------
        string subtype = KnownSubtypes.Contains(_subtype) ? _subtype : DetectSubtypeCore(netlist);
        var outputNode = subtype == "CommonDrain" ? sourceNet : drainNet;

        // ---- 6. Coupling / bypass capacitors and output load --------------
        double c1 = 0.0;   // input coupling capacitor
        double c2 = 0.0;   // output coupling capacitor
        double ce = 0.0;   // source bypass capacitor (CommonSourceBypassed)
        double cg = 0.0;   // gate bypass capacitor (CommonGate)
        double rl = double.PositiveInfinity;

        var acSource = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);
        var acSourcePosNet = acSource == null ? null : netlist.FindNetForPin(acSource.Id, "pos");
        var inputNode = subtype == "CommonGate" ? sourceNet : gateNet;

        var remainingCapacitors = new List<CircuitElement>();
        foreach (var capacitor in capacitors)
        {
            double value = GetSiValue(capacitor);
            if (!double.IsFinite(value) || value <= 0) continue;

            if (Connects(netlist, capacitor, inputNode, acSourcePosNet))
            {
                if (c1 <= 0) c1 = value;
                continue;
            }
            if (subtype is "CommonSourceWithSourceResistance" or "CommonSourceBypassed"
                && Connects(netlist, capacitor, sourceNet, sourceRail))
            {
                if (ce <= 0) ce = value;
                continue;
            }
            if (subtype == "CommonGate"
                && (Connects(netlist, capacitor, gateNet, gndNet) || Connects(netlist, capacitor, gateNet, vccNet)))
            {
                if (cg <= 0) cg = value;
                continue;
            }

            remainingCapacitors.Add(capacitor);
        }

        foreach (var capacitor in remainingCapacitors)
        {
            var (firstNet, secondNet) = GetTwoTerminalNets(netlist, capacitor);
            ElectricalNet? loadNet = null;
            if (firstNet == outputNode) loadNet = secondNet;
            else if (secondNet == outputNode) loadNet = firstNet;
            if (loadNet == null) continue;
            if (IsAcGroundNet(loadNet) || loadNet == vccNet || loadNet == gateNet
                || loadNet == drainNet || loadNet == sourceNet)
            {
                continue;
            }

            var loadResistor = unclassifiedResistors.FirstOrDefault(r =>
                !usedResistors.Contains(r.Id)
                && (Connects(netlist, r, loadNet, gndNet) || Connects(netlist, r, loadNet, vccNet)));

            c2 = GetSiValue(capacitor);
            if (loadResistor != null)
            {
                rl = GetSiValue(loadResistor);
                usedResistors.Add(loadResistor.Id);
            }
            break;
        }

        var unusedResistors = unclassifiedResistors.Where(r => !usedResistors.Contains(r.Id)).ToList();
        if (unusedResistors.Count > 0)
        {
            return Fail($"存在教学模型未覆盖的电阻支路（{string.Join("、", unusedResistors.Select(r => r.Id))}），已停止推导，不会使用缺失/未知元件默认值。");
        }

        // ---- 7. Topology consistency checks -------------------------------
        bool gateAcGrounded = IsGroundNet(gateNet) || cg > 0;
        switch (subtype)
        {
            case "CommonSource":
                if (rd <= 0)
                    return Fail("未能识别漏极电阻 Rd（漏极应经电阻接 VDD/GND 电源轨），不会使用缺失元件默认值。");
                if (rs > 0 || sourceNet != sourceRail)
                    return Fail("实际连接在源极与参考轨之间存在电阻 Rs，与 CommonSource 子类型不符；请使用 CommonSourceWithSourceResistance/CommonSourceBypassed 子类型。");
                break;

            case "CommonSourceWithSourceResistance":
                if (rd <= 0)
                    return Fail("未能识别漏极电阻 Rd（漏极应经电阻接电源轨），不会使用缺失元件默认值。");
                if (rs <= 0)
                    return Fail("未能识别源极电阻 Rs（源极应经电阻接 GND/VDD 参考轨），不会使用缺失元件默认值。");
                if (ce > 0)
                    return Fail("检测到跨接 Rs 的旁路电容，实际拓扑应为 CommonSourceBypassed，请由分类器使用对应子类型。");
                break;

            case "CommonSourceBypassed":
                if (rd <= 0)
                    return Fail("未能识别漏极电阻 Rd（漏极应经电阻接电源轨），不会使用缺失元件默认值。");
                if (rs <= 0)
                    return Fail("未能识别源极电阻 Rs（源极应经电阻接 GND/VDD 参考轨），不会使用缺失元件默认值。");
                if (ce <= 0)
                    return Fail("未能找到与 Rs 并联的旁路电容 Cs（源极交流短路电容），与 CommonSourceBypassed 子类型不符。");
                break;

            case "CommonDrain":
                if (drainNet != drainRail)
                    return Fail("共漏（源极跟随器）要求漏极直接接电源轨（NMOS 接 VDD / PMOS 接 GND），中间不得有电阻；当前连接不符。");
                if (rd > 0)
                    return Fail("共漏电路的漏极不应再串联 Rd，当前连接更接近共源+Rs 拓扑。");
                if (rs <= 0)
                    return Fail("未能识别共漏电路的源极电阻 Rs（源极应经电阻接参考轨），不会使用缺失元件默认值。");
                break;

            case "CommonGate":
                if (rd <= 0)
                    return Fail("未能识别漏极电阻 Rd（漏极应经电阻接电源轨），不会使用缺失元件默认值。");
                if (rs <= 0)
                    return Fail("未能识别共栅电路的源极电阻 Rs（源极应经电阻接参考轨，输入从源极注入）。");
                if (!gateAcGrounded)
                    return Fail("共栅拓扑要求栅极交流接地（栅极经旁路电容接 GND/VDD，或栅极直接接交流地），当前未找到栅极旁路电容。");
                break;

            default:
                return Fail($"不支持的 MOSFET 子类型 '{subtype}'；当前支持 CommonSource、CommonSourceWithSourceResistance、CommonSourceBypassed、CommonDrain、CommonGate。");
        }

        // ---- 8. Device model and DC operating point -----------------------
        MosfetModel model;
        try
        {
            model = MosfetModel.FromElement(mos, parameters);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        double vth = model.ThresholdVoltage;
        double k = model.K;
        double lambda = model.Lambda;

        PedagogicalSolution CutoffFailure(double overdriveValue, string detail) =>
            Fail($"器件 {mos.Id} 工作点处于截止区（V_OV = {TxtVoltage(overdriveValue)} ≤ 0）：{detail}。当前教学模型只对饱和区工作点给出小信号参数，不做截止区非线性重求解。");

        // =================================================================
        // 1) 直流等效电路（DC）：教材平方律估算法
        //    V_G 按显示单位两位小数舍入后回代到平方律方程；
        //    I_D、V_ov、V_GS、V_DS 同样舍入后用于下一步。
        // =================================================================
        bool singleGateResistor = !(rgTop > 0 && rgBottom > 0);
        if (!singleGateResistor && !double.IsFinite(rgTop + rgBottom))
            return Fail("栅极分压电阻之和超出数值范围，无法确定工作区；未将数值溢出判为截止。");
        double gateVoltage;
        if (rgTop > 0 && rgBottom > 0)
        {
            gateVoltage = RoundVoltage(vdd * rgBottom / (rgTop + rgBottom));
        }
        else if (rgTop > 0 || gateNet == vccNet)
        {
            gateVoltage = RoundVoltage(vdd);
        }
        else if (rgBottom > 0 || gateNet == gndNet)
        {
            gateVoltage = RoundVoltage(0.0);
        }
        else
        {
            return Fail($"未能识别 {mos.Id} 的栅极偏置网络（Rg1 接栅极–VDD、Rg2 接栅极–GND 的分压，或单只栅极电阻）；不会使用缺失元件默认值。");
        }

        // Physical region check: use the actual gate divider and the full
        // piecewise model with the DC load line. Display rounding is independent.
        double rawGate = rgTop > 0 && rgBottom > 0
            ? vdd * (rgBottom / (rgTop + rgBottom))
            : rgTop > 0 || gateNet == vccNet ? vdd : 0;
        double rawDrive = isPChannel ? vdd - rawGate + vth : rawGate - vth;
        if (!double.IsFinite(rawDrive))
            return Fail("场效应管偏置方程产生非有限数值，无法判断工作区；数值失败不等于截止。");
        if (rawDrive <= 0)
            return CutoffFailure(rawDrive, "未舍入栅源偏置满足截止条件");
        double dcResistance = rd + rs;
        if (!double.IsFinite(dcResistance) || dcResistance <= 0)
            return Fail("场效应管直流负载回路必须具有有限正电阻，无法确定工作点。");
        double physicalLow = 0, physicalHigh = vdd / dcResistance;
        for (int i = 0; i < 200; i++)
        {
            double current = physicalLow + (physicalHigh - physicalLow) * 0.5;
            double currentFromModel = model.DrainCurrentMagnitude(rawDrive - current * rs,
                vdd - current * dcResistance);
            if (!double.IsFinite(currentFromModel))
                return Fail("场效应管模型计算超出数值范围，无法判断工作区。");
            if (current > currentFromModel) physicalHigh = current;
            else physicalLow = current;
        }
        double physicalCurrent = physicalLow + (physicalHigh - physicalLow) * 0.5;
        double physicalOverdrive = rawDrive - physicalCurrent * rs;
        double physicalVds = vdd - physicalCurrent * dcResistance;
        double physicalVgs = rawGate - (isPChannel ? vdd - physicalCurrent * rs : physicalCurrent * rs);
        if (model.IsJfet && (isPChannel ? -physicalVgs : physicalVgs) > 1e-10)
            return Fail($"器件 {mos.Id}（JFET）的栅源结正向偏置（VGS={TxtVoltage(physicalVgs)}）：Shockley 方程要求栅结反偏或零偏，不能继续按栅极电流为零求解。");
        // Boundary belongs to saturation; a tiny solver residual is tolerated.
        if (physicalVds + 1e-10 * Math.Max(1, vdd) < physicalOverdrive)
            return Fail($"器件 {mos.Id} 工作点位于可变电阻/三极管区（非饱和区）：" +
                $"分段模型与直流负载线求得 ID={TxtCurrent(physicalCurrent)}、|VDS|={TxtVoltage(physicalVds)}、V_OV={TxtVoltage(physicalOverdrive)}。" +
                "该工作区尚不输出小信号增益；教学舍入不会改变上述物理判定。");

        PedagogicalSolution RoundingFailure() => Fail("未舍入场效应管偏置解满足饱和放大条件，但教材舍入链越过工作区边界或分辨率不足，停止推导；不能据此判成截止或三极管区。");
        double overdrive;
        double drainCurrent;
        double vgs;
        double vdsMagnitude;
        double solvedCurrent;

        if (rs > 0)
        {
            // Source degeneration: VGS depends on ID, so the square law becomes
            //  ID = K*(V_G - Vth - ID*Rs)^2  (lambda = 0)  -> quadratic in ID.
            // The rounded displayed V_G is fed into the quadratic exactly like the
            // textbook hand calculation.
            double gateDrive = RoundVoltage(isPChannel ? vdd - gateVoltage + vth : gateVoltage - vth);
            if (!(gateDrive > 0))
                return RoundingFailure();

            double drainCurrentRaw;
            if (lambda <= 0)
            {
                double a = k * rs * rs;
                double b = -(2.0 * k * gateDrive * rs + 1.0);
                double c = k * gateDrive * gateDrive;
                double discriminant = b * b - 4.0 * a * c;
                if (discriminant < 0)
                    return Fail($"源极电阻偏置的二次方程无实数解（判别式 {Plain(discriminant)} < 0），无法确定静态工作点。");
                // Rationalized smaller root avoids subtraction cancellation for small Rs/K.
                drainCurrentRaw = 2.0 * c / (-b + Math.Sqrt(discriminant));
            }
            else
            {
                double low = 0.0;
                double high = gateDrive / rs;
                for (int i = 0; i < 200; i++)
                {
                    double mid = 0.5 * (low + high);
                    double midOverdrive = gateDrive - mid * rs;
                    double midVds = vdd - mid * (rd + rs);
                    double residual = mid - k * midOverdrive * midOverdrive * (1.0 + lambda * midVds);
                    if (residual > 0) high = mid;
                    else low = mid;
                }
                drainCurrentRaw = 0.5 * (low + high);
            }

            if (!(drainCurrentRaw > 0) || !double.IsFinite(drainCurrentRaw))
                return Fail($"源极电阻偏置方程求得的漏极电流非正（ID = {TxtCurrent(drainCurrentRaw)}），工作点不成立。");

            solvedCurrent = drainCurrentRaw;
            drainCurrent = RoundCurrent(drainCurrentRaw);
            overdrive = RoundVoltage(gateDrive - drainCurrent * rs);
            if (!(overdrive > 0))
                return RoundingFailure();

            double sourceVoltage = isPChannel ? vdd - drainCurrent * rs : drainCurrent * rs;
            vgs = RoundVoltage(gateVoltage - sourceVoltage);
            vdsMagnitude = RoundVoltage(vdd - drainCurrent * (rd + rs));
        }
        else
        {
            overdrive = RoundVoltage(isPChannel ? vdd - gateVoltage + vth : gateVoltage - vth);
            if (!(overdrive > 0))
                return RoundingFailure();

            if (lambda <= 0)
            {
                solvedCurrent = k * overdrive * overdrive;
                drainCurrent = RoundCurrent(solvedCurrent);
            }
            else
            {
                // ID = K*vov^2*(1 + lambda*(VDD - ID*Rd)) -> linear in ID.
                solvedCurrent = k * overdrive * overdrive * (1.0 + lambda * vdd)
                                            / (1.0 + lambda * k * overdrive * overdrive * rd);
                drainCurrent = RoundCurrent(solvedCurrent);
            }

            vgs = isPChannel ? RoundVoltage(gateVoltage - vdd) : RoundVoltage(gateVoltage);
            vdsMagnitude = RoundVoltage(vdd - drainCurrent * (rd + rs));
        }

        if (!double.IsFinite(solvedCurrent) || !double.IsFinite(overdrive) || !double.IsFinite(vdsMagnitude))
            return Fail("场效应管教学方程产生非有限数值，无法继续推导；未将数值错误解释为物理工作区。");
        if (!(vdsMagnitude > 0) || vdsMagnitude + 1e-10 * Math.Max(1, vdd) < overdrive)
            return RoundingFailure();

        // Internal consistency: the model's own overdrive mapping must reproduce
        // the (rounded) overdrive used by the DC solve. Two-decimal display
        // rounding allows up to a few mV difference.
        double modelOverdrive = model.OverdriveMagnitude(vgs);
        if (Math.Abs(modelOverdrive - overdrive) > 0.02)
        {
            return Fail($"内部一致性检查失败：模型 overdrive（{TxtVoltage(modelOverdrive)}）与直流解（{TxtVoltage(overdrive)}）不一致，已停止推导。");
        }

        // =================================================================
        // 2) 交流等效电路（AC）：gm/ro → Av → Ri/Ro → fL
        //    下游一律使用按显示单位两位小数舍入后的值（教材手算顺序）。
        // =================================================================
        double gm = RoundTransconductance(model.Transconductance(overdrive, vdsMagnitude));
        double gds = lambda > 0 ? lambda * k * overdrive * overdrive : 0.0;
        double roDevice = gds > 0 ? RoundResistance(1.0 / gds) : double.PositiveInfinity;
        if (!(gm > 0) || !double.IsFinite(gm))
            return Fail("解出的跨导 gm 非正或非有限，无法进行小信号分析。");

        double effectiveDrainLoad = RoundResistance(Parallel(rd, rl));      // Rd || RL
        double sourceLoad = RoundResistance(Parallel(rs, rl));             // Rs || RL
        double rgEquivalent = RoundResistance(rgTop > 0 && rgBottom > 0
            ? Parallel(rgTop, rgBottom)
            : (rgTop > 0 ? rgTop : rgBottom));

        double av;
        double ri;
        double roOutput;
        switch (subtype)
        {
            case "CommonSource":
            case "CommonSourceBypassed":
                av = RoundPlain(-gm * effectiveDrainLoad);
                ri = rgEquivalent;
                roOutput = RoundResistance(rd);
                break;

            case "CommonSourceWithSourceResistance":
                av = RoundPlain(-gm * effectiveDrainLoad / (1.0 + gm * rs));
                ri = rgEquivalent;
                roOutput = RoundResistance(rd);
                break;

            case "CommonDrain":
                av = RoundPlain(gm * sourceLoad / (1.0 + gm * sourceLoad));
                ri = rgEquivalent;
                roOutput = RoundResistance(Parallel(rs, 1.0 / gm));
                break;

            case "CommonGate":
                av = RoundPlain(gm * effectiveDrainLoad);
                ri = RoundResistance(Parallel(1.0 / gm, rs));
                roOutput = RoundResistance(rd);
                break;

            default:
                return Fail($"不支持的 MOSFET 子类型 '{subtype}'。");
        }

        if (!double.IsFinite(av) || !double.IsFinite(ri) || !double.IsFinite(roOutput) || ri <= 0 || roOutput <= 0)
            return Fail("MOSFET 小信号参数（Av/Ri/Ro）超出数值范围，未生成有效结果。");

        // ---- 3. Lower cut-off frequency estimate (optional, labelled) -----
        double fl1 = c1 > 0 && ri > 0 ? RoundFrequency(1.0 / (2.0 * Math.PI * ri * c1)) : 0.0;
        double fl2 = 0.0;
        if (c2 > 0 && roOutput > 0)
        {
            double outputLoopResistance = roOutput + (double.IsPositiveInfinity(rl) ? 0.0 : rl);
            if (outputLoopResistance > 0)
                fl2 = RoundFrequency(1.0 / (2.0 * Math.PI * outputLoopResistance * c2));
        }
        double fl = RoundFrequency(Math.Sqrt(fl1 * fl1 + fl2 * fl2));

        // ---- 4. Formatting (textbook units, ≤ 2 decimals) -----------------
        string polarityName = model.IsJfet ? (isPChannel ? "P 沟道 JFET" : "N 沟道 JFET")
            : (model.IsEnhancement ? "增强型 " : "耗尽型 ") + (isPChannel ? "PMOS" : "NMOS");
        string vgText = TxtVoltage(gateVoltage);
        string vgsText = TxtVoltage(vgs);
        string vovText = TxtVoltage(overdrive);
        string idText = TxtCurrent(drainCurrent);
        string vdsText = TxtVoltage(vdsMagnitude);
        string gmText = TxtTransconductance(gm);
        string gdsText = gds > 0 ? TxtTransconductance(gds) : "0 S（λ=0）";
        string roText = double.IsPositiveInfinity(roDevice)
            ? "∞（λ=0，忽略沟道长度调制）"
            : TxtResistance(roDevice);
        string avText = Plain(av);
        string riText = TxtResistance(ri);
        string roOutputText = TxtResistance(roOutput);
        string regionText = "饱和区 (Saturation)";
        string workingRegion = isPChannel
            ? $"{regionText}；{polarityName}: V_SD=+{vdsText}（V_DS=-{vdsText}）"
            : regionText;

        static string Numeric(double value) =>
            double.IsPositiveInfinity(value)
                ? "Infinity"
                : double.IsNegativeInfinity(value)
                    ? "-Infinity"
                    : value.ToString("G9", CultureInfo.InvariantCulture);

        // Stable machine-readable step Values keys required by the contract:
        // ID / VGS / VDS / gm / Av / Ri / Ro (SI units, invariant culture; the
        // numeric chain follows the rounded textbook-display values above).
        var stepValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ID_physical_A"] = physicalCurrent.ToString("G17", CultureInfo.InvariantCulture),
            ["VGS_physical_V"] = physicalVgs.ToString("G17", CultureInfo.InvariantCulture),
            ["VDS_physical_V"] = physicalVds.ToString("G17", CultureInfo.InvariantCulture),
            ["Vov_physical_V"] = physicalOverdrive.ToString("G17", CultureInfo.InvariantCulture),
            ["ID"] = Numeric(drainCurrent),
            ["DeviceFamily"] = model.IsJfet ? "JFET" : model.IsEnhancement ? "EnhancementMOS" : "DepletionMOS",
            ["IDSS"] = Numeric(model.IsJfet ? model.Idss : k * vth * vth),
            ["VGSoff"] = Numeric(vth),
            ["K"] = Numeric(k),
            ["VGS"] = Numeric(vgs),
            ["VDS"] = Numeric(isPChannel ? -vdsMagnitude : vdsMagnitude),
            ["gm"] = Numeric(gm),
            ["Av"] = Numeric(av),
            ["Ri"] = Numeric(ri),
            ["Ro"] = Numeric(roOutput),
            ["VG"] = Numeric(gateVoltage),
            ["Vov"] = Numeric(overdrive),
            ["ro"] = Numeric(roDevice),
            ["fL"] = Numeric(fl)
        };

        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"{polarityName} {GetSubtypeDisplayName(subtype)}教学求解（Q 点与中频小信号）"
        };

        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = idText,
            Vceq = vdsText,
            WorkingRegion = workingRegion,
            IsActiveRegion = true
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = avText,
            InputResistance = riText,
            OutputResistance = roOutputText,
            LowerCutoffFreq = fl > 0 ? TxtFrequency(fl) + "（估算）" : "未求解（缺少耦合电容）",
            UpperCutoffFreq = "未求解（缺少高频寄生参数）",
            Bandwidth = "未求解"
        };

        string polarityNote = isPChannel
            ? $"，{polarityName} 极性约定：V_SD=+{vdsText}（V_DS=-{vdsText}），V_GS={vgsText}"
            : string.Empty;
        solution.SummaryText =
            $"{polarityName} {GetSubtypeDisplayName(subtype)}求解完成（教材平方律估算法 + 微变等效电路）：" +
            $"直流等效电路求得 Q 点（V_G={vgText}，V_GS={vgsText}，I_D={idText}，{(isPChannel ? "V_SD" : "V_DS")}={vdsText}），工作区 {regionText}；" +
            $"交流等效电路求得 gm={gmText}，ro={roText}，Av={avText}，Ri={riText}，Ro={roOutputText}{polarityNote}。";

        // =================================================================
        // 5) 教学推导步骤：DC 等效电路（1-3）→ AC 等效电路（4-7）
        // =================================================================
        bool gateAtRail = gateNet == vccNet || gateNet == gndNet;
        string biasContent = gateAtRail
            ? $"栅极直接连接 {(gateNet == vccNet ? "VDD" : "GND")}，VG={vgText}；按源极实际电位求 VGS。"
            : singleGateResistor
            ? $"识别到单只栅极电阻的简化偏置（Rg = {TxtResistance(rgTop > 0 ? rgTop : rgBottom)}）：场效应管栅极电流近似为零，该电阻把栅极直流电位钳位到 {(rgTop > 0 ? "VDD" : "GND")}；再按源极实际直流电位求 VGS（含 Rs 压降）。"
            : "场效应管栅极电流近似为零，Rg1/Rg2 分压支路的电流完全由电源提供，栅极直流电位 V_G 由分压公式直接给出；随后按源极直流电位求 VGS。";

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "直流等效电路：栅极偏置网络识别与直流栅极电位 VG 计算",
            MethodName = "栅极分压偏置（栅极电流为零）",
            Content = "画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络。" + biasContent,
            LatexFormula = singleGateResistor
                ? (rgTop > 0 || gateNet == vccNet ? @"V_G = V_{DD}" : @"V_G = 0")
                : @"V_G = V_{DD}\,\frac{R_{g2}}{R_{g1}+R_{g2}},\quad V_{GS} = V_G - V_S",
            SubstitutionFormula = singleGateResistor
                ? $@"V_G = {LxVoltage(gateVoltage)}"
                : $@"V_G = {LxVoltage(vdd)}\times\frac{{{LxResistance(rgBottom)}}}{{{LxResistance(rgTop)}+{LxResistance(rgBottom)}}} = {LxVoltage(gateVoltage)}",
            ResultValue = $"VG = {vgText}，V_G 与源极参考电位之差决定 VGS",
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        // Device-level square law written with the polarity-specific overdrive.
        string overdriveDefinition = isPChannel
            ? (rs > 0
                ? @"a=V_{DD}-V_G+V_{th},\quad V_{ov}=V_{th}-V_{GS}=a-I_D R_s"
                : @"V_{ov}=V_{th}-V_{GS}=(V_{DD}-V_G)+V_{th}")
            : (rs > 0
                ? @"a=V_G-V_{th},\quad V_{ov}=V_{GS}-V_{th}=a-I_D R_s"
                : @"V_{ov}=V_{GS}-V_{th}");
        string currentLatex = rs > 0
            ? $@"I_D=K V_{{ov}}^2,\quad {overdriveDefinition}\;\Rightarrow\;K R_s^2 I_D^2-\left(2KaR_s+1\right)I_D+Ka^2=0"
            : $@"I_D=K V_{{ov}}^2\left(1+\lambda V_{{(SD/DS)}}\right)\approx K V_{{ov}}^2,\quad {overdriveDefinition}";
        string currentSubstitution = rs > 0
            ? $@"a = {LxVoltage(isPChannel ? vdd - gateVoltage + vth : gateVoltage - vth)},\quad I_D = {LxCurrent(drainCurrent)},\quad V_{{ov}} = a - I_D R_s = {LxVoltage(overdrive)}"
            : $@"V_{{ov}} = {LxVoltage(overdrive)},\quad I_D = K V_{{ov}}^2 = {KText(k)}\times\left({LxVoltage(overdrive)}\right)^2 = {LxCurrent(drainCurrent)}";
        string currentExplanation = lambda > 0
            ? "采用 N/P 沟道镜像量 Vov 与正漏源电压幅值，联立直流通路方程求电流。"
            : isPChannel
            ? "按 PMOS 镜像量处理：Vov = Vth − VGS = (VDD − VG) + Vth，VSD = VDD − ID(Rd + Rs)，平方律方程与 NMOS 同形。"
            : "按 NMOS 平方律方程求解；若源极有电阻 Rs，则 VGS 随 ID 变化，需要解一元二次方程并取物理上合理的较小根。";
        if (rs > 0 && lambda <= 0)
            currentExplanation += " 本电路存在源极电阻 Rs，已解二次方程求 ID。";

        if (model.IsJfet || !model.IsEnhancement)
        {
            string shockley = @"I_D=I_{DSS}\left(1-\frac{V_{GS}}{V_{GS(off)}}\right)^2,\quad K=\frac{I_{DSS}}{V_{GS(off)}^2}";
            currentLatex = shockley + @",\quad " + currentLatex;
            currentSubstitution = $@"V_{{GS(off)}}={LxVoltage(vth)},\quad I_{{DSS}}={LxCurrent(model.IsJfet ? model.Idss : k * vth * vth)},\quad K={KText(k)},\quad " + currentSubstitution;
            currentExplanation = (model.IsJfet
                ? "JFET 使用题目给定 IDSS 与带符号 VGS(off)，由 Shockley 方程换算 K=IDSS/VGS(off)²；仅在栅结反偏或零偏下采用 IG≈0。"
                : "耗尽型 MOS 在 VGS=0 时已有沟道，N/P 沟道 VGS(off) 分别为负/正；IDSS=K·VGS(off)²，允许向增强方向偏置。") + currentExplanation;
        }
        if (lambda > 0)
        {
            currentLatex = $@"I_D=K V_{{ov}}^2(1+\lambda V_{{(SD/DS)}}),\quad {overdriveDefinition}"
                + (model.IsJfet || !model.IsEnhancement ? @",\quad K=\frac{I_{DSS}}{V_{GS(off)}^2}" : "");
            currentSubstitution = $@"K={KText(k)},\quad \lambda={Plain(lambda)},\quad I_D={LxCurrent(drainCurrent)},\quad V_{{ov}}={LxVoltage(overdrive)},\quad V_{{(SD/DS)}}={LxVoltage(vdsMagnitude)}";
            currentExplanation += " 电流方程包含沟道长度调制；有 Rs 时数值求根，无 Rs 时解线性反馈方程。";
        }

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = model.IsJfet ? "Shockley 方程求静态漏极电流 ID" : "平方律直流电流方程求静态漏极电流 ID",
            MethodName = model.IsJfet ? "Shockley 方程（饱和区与栅结反偏校验）" : "Level-1 平方律模型（饱和区假设）",
            Content = currentExplanation,
            LatexFormula = currentLatex,
            SubstitutionFormula = currentSubstitution,
            ResultValue = $"ID = {idText}，Vov = {vovText}",
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "漏源电压计算与饱和区校验（Q 点确定）",
            MethodName = "直流通路 KVL 与饱和区判据",
            Content = $"直流等效电路中电容开路，漏极回路满足 VDD 与管压降的 KVL 关系。校验判据：饱和区要求 {(isPChannel ? "V_SD ≥ Vov（即 V_DS 幅值大于过驱动电压）" : "V_DS ≥ Vov")}，否则进入可变电阻（三极管）区，本教学模型将明确失败而不是输出错误增益。",
            LatexFormula = isPChannel
                ? @"V_{SD}=V_{DD}-I_D\left(R_d+R_s\right),\quad V_{SD}\ge V_{ov}\;\Rightarrow\;\text{饱和区}"
                : @"V_{DS}=V_{DD}-I_D\left(R_d+R_s\right),\quad V_{DS}\ge V_{ov}\;\Rightarrow\;\text{饱和区}",
            SubstitutionFormula = $@"V_{{(SD/DS)}} = {LxVoltage(vdd)} - {LxCurrent(drainCurrent)}\times\left({LxResistance(rd)}+{LxResistance(rs)}\right) = {LxVoltage(vdsMagnitude)} > V_{{ov}} = {LxVoltage(overdrive)}",
            ResultValue = $"Q 点：ID = {idText}，VGS = {vgsText}，{(isPChannel ? "VSD" : "VDS")} = {vdsText}，Vov = {vovText}；工作区 {regionText}",
            Status = "Completed",
            Phase = SolutionPhase.DcEquivalent,
            Values = stepValues
        });

        string acIntro = "画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路，场效应管用 gm·v_gs 与 ro 微变模型替代。";

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "交流等效电路：小信号跨导 gm、输出电导 gds 与输出电阻 ro",
            MethodName = "平方律模型微变等效参数",
            Content = acIntro + $"由直流等效电路已求得的 V_ov = {vovText} 计算 gm = 2K·V_ov·(1+λ·VDS幅值)；沟道长度调制 λ 引入 gds = λK·V_ov²，对应输出电阻 ro = 1/gds（λ=0 时 ro→∞）。公式不做代数化简。",
            LatexFormula = @"g_m = 2K V_{ov}(1+\lambda V_{(SD/DS)}),\quad g_{ds} = \lambda K V_{ov}^2,\quad r_o = \frac{1}{g_{ds}}",
            SubstitutionFormula = $@"g_m = 2\times{KText(k)}\times{LxVoltage(overdrive)}\times(1+{Plain(lambda)}\times{LxVoltage(vdsMagnitude)}) = {LxTransconductance(gm)},\quad r_o = {LxResistanceOrInfinity(roDevice)}",
            ResultValue = $"gm = {gmText}，gds = {gdsText}，ro = {roText}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        string avLatex = subtype switch
        {
            "CommonSource" or "CommonSourceBypassed" => @"A_v=-g_m\left(R_d\parallel R_L\right)",
            "CommonSourceWithSourceResistance" => @"A_v=-\frac{g_m\left(R_d\parallel R_L\right)}{1+g_m R_s}",
            "CommonDrain" => @"A_v=\frac{g_m\left(R_s\parallel R_L\right)}{1+g_m\left(R_s\parallel R_L\right)}",
            _ => @"A_v=+g_m\left(R_d\parallel R_L\right)"
        };
        string avSubstitution = subtype switch
        {
            "CommonSourceWithSourceResistance" => $@"A_v = -\frac{{{LxTransconductance(gm)}\times{DrainLoadLatex(rd, rl)}}}{{1+{LxTransconductance(gm)}\times{LxResistance(rs)}}} = {LxRatio(av)}",
            "CommonDrain" => $@"A_v = \frac{{{LxTransconductance(gm)}\times{LxResistance(sourceLoad)}}}{{1+{LxTransconductance(gm)}\times{LxResistance(sourceLoad)}}} = {LxRatio(av)}",
            _ => $@"A_v = {LxTransconductance(gm)}\times{DrainLoadLatex(rd, rl)} = {LxRatio(av)}"
        };
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "中频段小信号电压放大倍数 Av 推导与求解",
            MethodName = "微变等效电路（中频段，耦合/旁路电容交流短路）",
            Content = acIntro + (subtype == "CommonDrain"
                ? "输入信号作用于栅极，源极跟随器以源极电阻 Rs 上的电压作为输出，受控电流源 gm·v_gs 与 Rs（及负载 RL）形成输出回路。按 §5.2 教材公式计算（忽略体效应；ro 已在上一步给出，本教学增益式按 λ=0 的教材近似）。"
                : "输入信号作用于栅极（共源/共漏）或源极（共栅），受控电流源 gm·v_gs 在漏极负载上产生输出。按 §5.2 教材公式计算（忽略体效应；ro 已在上一步给出，本教学增益式按 λ=0 的教材近似）。"),
            LatexFormula = avLatex,
            SubstitutionFormula = avSubstitution,
            ResultValue = $"Av = {avText}{(isPChannel ? "（PMOS 与 NMOS 同形，符号按镜像量确定）" : string.Empty)}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        string riRoLatex = subtype switch
        {
            "CommonDrain" => @"R_i=R_{g1}\parallel R_{g2},\quad R_o=R_s\parallel\frac{1}{g_m}",
            "CommonGate" => @"R_i=\frac{1}{g_m}\parallel R_s,\quad R_o=R_d",
            _ => @"R_i=R_{g1}\parallel R_{g2},\quad R_o=R_d"
        };
        string riNote = subtype == "CommonGate"
            ? "共栅电路输入在源极，输入电阻工程近似为 1/gm，计入源极偏置电阻 Rs 的并联影响后为 Ri = (1/gm) ∥ Rs；输出端从漏极看入，Ro = Rd。"
            : subtype == "CommonDrain"
                ? "共漏电路输出在源极，输出电阻为 Rs ∥ (1/gm)；输入电阻由栅极偏置电阻的并联等效决定。"
                : "输入电阻由栅极偏置电阻的并联等效（Rg1 ∥ Rg2，或单只 Rg）决定；输出电阻从漏极看入等于 Rd。";
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 6,
            BadgeText = "第 6 步",
            Title = "输入电阻 Ri 与输出电阻 Ro 计算",
            MethodName = "输入/输出端口等效电阻定义法",
            Content = acIntro + riNote,
            LatexFormula = riRoLatex,
            SubstitutionFormula = $@"R_i = {LxResistance(ri)},\quad R_o = {LxResistance(roOutput)}",
            ResultValue = $"Ri = {riText}，Ro = {roOutputText}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        string flText = fl > 0 ? TxtFrequency(fl) + "（估算）" : "未求解（缺少耦合电容）";
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 7,
            BadgeText = "第 7 步",
            Title = "低频截止频率估算（可选，标注为估算）",
            MethodName = "耦合电容时间常数法（估算）",
            Content = acIntro + "本教学模型只做中频段小信号分析；这里用输入/输出耦合电容的时间常数粗略估算下限截止频率 fL，仅用于说明耦合电容取值的影响，不承诺与 SPICE 频响一致。上限截止频率与带宽需要高频寄生电容参数，本轮未求解。",
            LatexFormula = @"f_L\approx\sqrt{f_{L1}^2+f_{L2}^2},\quad f_{L1}=\frac{1}{2\pi R_i C_1},\quad f_{L2}=\frac{1}{2\pi\left(R_o+R_L\right)C_2}",
            SubstitutionFormula = $@"f_L \approx {LxFrequency(fl)}\quad(\text{{估算}})",
            ResultValue = $"fL ≈ {flText}",
            Status = "Completed",
            Phase = SolutionPhase.AcEquivalent,
            Values = stepValues
        });

        RenumberSteps(solution.Steps);
        return solution;
    }

    // =====================================================================
    // Small helpers (display formatting / rounding / topology utilities)
    // =====================================================================

    /// <summary>Every displayed number goes through TeachingDisplay (textbook unit, ≤ 2 decimals).</summary>
    private static double RoundVoltage(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Voltage);
    private static double RoundCurrent(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Current);
    private static double RoundResistance(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Resistance);
    private static double RoundFrequency(double value) => TeachingDisplay.RoundTo(value, QuantityKind.Frequency);
    private static double RoundPlain(double value) => TeachingDisplay.RoundPlain(value);

    /// <summary>Transconductance / conductance display unit: mS, rounded to two decimals.</summary>
    private static double RoundTransconductance(double siemens) =>
        Math.Round(siemens * 1e3, 2, MidpointRounding.AwayFromZero) / 1e3;

    private static string TxtVoltage(double value) => TeachingDisplay.Text(value, QuantityKind.Voltage);
    private static string TxtCurrent(double value) => TeachingDisplay.Text(value, QuantityKind.Current);
    private static string TxtResistance(double value) => TeachingDisplay.Text(value, QuantityKind.Resistance);
    private static string TxtFrequency(double value) => TeachingDisplay.Text(value, QuantityKind.Frequency);
    private static string TxtTransconductance(double siemens) => $"{TeachingDisplay.Plain(siemens * 1e3)} mS";
    private static string Plain(double value) => TeachingDisplay.Plain(value);

    private static string LxVoltage(double value) => TeachingDisplay.Latex(value, QuantityKind.Voltage);
    private static string LxCurrent(double value) => TeachingDisplay.Latex(value, QuantityKind.Current);
    private static string LxResistance(double value) => TeachingDisplay.Latex(value, QuantityKind.Resistance);
    private static string LxFrequency(double value) => TeachingDisplay.Latex(value, QuantityKind.Frequency);
    private static string LxRatio(double value) => TeachingDisplay.LatexNumber(value, QuantityKind.Ratio);
    private static string LxTransconductance(double siemens) => $"{TeachingDisplay.Plain(siemens * 1e3)}\\;\\text{{mS}}";
    private static string LxResistanceOrInfinity(double resistance) =>
        double.IsPositiveInfinity(resistance) ? @"\infty" : LxResistance(resistance);

    /// <summary>Device constant K display (mA/V²), two decimals.</summary>
    private static string KText(double k) => $"{TeachingDisplay.Plain(k * 1e3)}\\;\\text{{mA/V}}^2";

    private static string DrainLoadLatex(double rd, double rl)
    {
        string rdText = LxResistance(rd);
        if (double.IsPositiveInfinity(rl)) return $"({rdText})";
        return $"({rdText}\\parallel {LxResistance(rl)})";
    }

    private static void RenumberSteps(List<PedagogicalSolutionStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].StepIndex = i + 1;
            if (string.IsNullOrEmpty(steps[i].BadgeText) || steps[i].BadgeText.StartsWith("第", StringComparison.Ordinal))
                steps[i].BadgeText = $"第 {i + 1} 步";
        }
    }

    private static string GetSubtypeDisplayName(string subtype) => subtype switch
    {
        "CommonSource" => "共源极放大电路",
        "CommonSourceWithSourceResistance" => "共源极放大电路（源极电阻未旁路）",
        "CommonSourceBypassed" => "共源极放大电路（源极电阻旁路）",
        "CommonDrain" => "共漏极放大电路（源极跟随器）",
        "CommonGate" => "共栅极放大电路",
        _ => "放大电路"
    };

    private static bool IsFetElement(ElementType type) =>
        type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh
            or ElementType.MosfetNDep or ElementType.MosfetPDep
            or ElementType.Jfet or ElementType.JfetN or ElementType.JfetP;

    private static bool IsPChannelType(ElementType type) =>
        type is ElementType.MosfetPEnh or ElementType.MosfetPDep or ElementType.JfetP;

    /// <summary>
    /// Resolves the positive supply rail: explicit power flag / "VCC" name first,
    /// then the positive terminal of the single DC source / VCC port.
    /// </summary>
    private static ElectricalNet? ResolveSupplyPositiveNet(ElectricalNetlist netlist)
    {
        ElectricalNet? vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower)
            ?? netlist.Nets.FirstOrDefault(n => string.Equals(n.Name, "VCC", StringComparison.OrdinalIgnoreCase));

        var supply = netlist.Elements.Values
            .FirstOrDefault(e => e.Type is ElementType.DcVoltageSource or ElementType.VccPort);
        if (supply != null)
        {
            var posNet = supply.Type == ElementType.VccPort
                ? netlist.FindNetForPin(supply.Id, "VCC")
                : netlist.FindNetForPin(supply.Id, "pos");
            if (posNet != null) vccNet = posNet;
        }

        return vccNet;
    }

    /// <summary>Nets of the two pins of a two-terminal element ("pos"/"neg" for voltage sources).</summary>
    private static (ElectricalNet? First, ElectricalNet? Second) GetTwoTerminalNets(
        ElectricalNetlist netlist,
        CircuitElement element)
    {
        string firstPin = element.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "pos" : "1";
        string secondPin = element.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "neg" : "2";
        return (netlist.FindNetForPin(element.Id, firstPin), netlist.FindNetForPin(element.Id, secondPin));
    }

    /// <summary>True when the two-terminal element is connected exactly between the two nets (either orientation).</summary>
    private static bool Connects(
        ElectricalNetlist netlist,
        CircuitElement element,
        ElectricalNet? first,
        ElectricalNet? second)
    {
        if (first == null || second == null) return false;
        var (net1, net2) = GetTwoTerminalNets(netlist, element);
        return (net1 == first && net2 == second) || (net1 == second && net2 == first);
    }

    /// <summary>Parallel combination; +infinity denotes an open branch.</summary>
    private static double ParallelResistance(double first, double second)
    {
        if (double.IsPositiveInfinity(first)) return second;
        if (double.IsPositiveInfinity(second)) return first;
        if (first <= 0 || second <= 0) return 0.0;
        return first * second / (first + second);
    }

    /// <summary>Parallel combination; +infinity denotes an open branch.</summary>
    private static double Parallel(double first, double second) => ParallelResistance(first, second);

    private static double GetSiValue(CircuitElement element) =>
        NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
}
