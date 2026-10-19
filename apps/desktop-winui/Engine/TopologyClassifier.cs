// ============================================================================
// File: apps/desktop-winui/Engine/TopologyClassifier.cs
// Namespace: CircuitAssistant.Engine
// Description: Hierarchical Circuit Topology Classifier. Automatically detects
//              1st-order RC/RL, 2nd-order RLC, BJT/MOSFET amplifier, and MNA networks.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine;

public class ClassificationResult
{
    public CircuitTopologyType TopologyType { get; set; }
    public string SubtypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ICircuitStrategy Strategy { get; set; } = null!;
}

public static class TopologyClassifier
{
    /// <summary>
    /// Classifies the netlist and provides the appropriate solving strategy via out parameter.
    /// </summary>
    public static CircuitTopologyType Classify(ElectricalNetlist netlist, out ICircuitStrategy strategy)
    {
        var result = Classify(netlist);
        strategy = result.Strategy;
        return result.TopologyType;
    }

    /// <summary>
    /// Full classification with descriptive result object.
    /// </summary>
    public static ClassificationResult Classify(ElectricalNetlist netlist)
    {
        if (netlist == null || netlist.Elements.Count == 0)
        {
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.Unsupported,
                SubtypeName = "Empty",
                Description = "画布无有效电路元件",
                Strategy = new MnaLinearStrategy()
            };
        }

        var elements = netlist.Elements.Values.ToList();

        // ------------------------------------------------------------------
        // 1. Active devices first (contract §3 priority order):
        //    transistor total = BJT count + MOSFET/JFET count
        //      > 1 (BJT/MOS mixed included) -> MultiStageAmplifier / "Cascade"
        //      BJT == 1 -> BjtAmplifier: CommonBase -> CommonCollector -> bias types
        //      MOSFET/JFET == 1 -> MosfetAmplifier (subtype from the MOS strategy)
        // ------------------------------------------------------------------
        var bjtList = elements.Where(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp).ToList();
        var mosList = elements.Where(e => e.Type is ElementType.Mosfet or ElementType.MosfetNEnh 
                                          or ElementType.MosfetPEnh or ElementType.MosfetNDep 
                                          or ElementType.MosfetPDep or ElementType.Jfet
                                          or ElementType.JfetN or ElementType.JfetP).ToList();

        // 晶体管总数 > 1（含 BJT + MOSFET 混合）→ 多级级联：本轮只预留接口并明确拒绝，
        // 禁止用第一只晶体管套用单管教学公式（契约 §3 / §7）。
        if (bjtList.Count + mosList.Count > 1)
        {
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.MultiStageAmplifier,
                SubtypeName = "Cascade",
                Description = $"多级放大电路（BJT {bjtList.Count} 只、场效应管 {mosList.Count} 只）[Cascade]：级联求解列入后续开发规划，本轮只预留 IMultiStageAnalysis 接口",
                Strategy = new MultiStageAmplifierStrategy()
            };
        }

        // 单只 BJT：共基极（沿用现有判定）→ 共集电极（射极跟随器，新增）→ 现有三类偏置。
        if (bjtList.Count == 1)
        {
            var bjt = bjtList[0];
            bool isPnp = bjt.Type == ElementType.BjtPnp;
            bool isCommonBase = IsBjtCommonBase(netlist, bjt);
            bool isCommonCollector = !isCommonBase && IsBjtCommonCollector(netlist, bjt);
            var biasSubtype = DetectBjtBiasTopology(netlist, bjt);
            string subtype = isCommonBase ? "CommonBase" : isCommonCollector ? "CommonCollector" : biasSubtype;
            // NPN 描述保持原有文案；PNP 单管在同一教学策略下求解，仅在描述中标注极性。
            string desc = isCommonBase
                ? (isPnp ? "PNP 双极型晶体管 (BJT) 共基极放大电路 [CommonBase]" : "双极型晶体管 (BJT) 共基极放大电路 [CommonBase]")
                : isCommonCollector
                    ? (isPnp ? "PNP 双极型晶体管 (BJT) 射极跟随器（共集电极）放大电路 [CommonCollector]" : "双极型晶体管 (BJT) 射极跟随器（共集电极）放大电路 [CommonCollector]")
                    : (isPnp ? $"PNP 双极型晶体管 (BJT) 放大电路 [{biasSubtype}]" : $"双极型晶体管 (BJT) 放大电路 [{biasSubtype}]");
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.BjtAmplifier,
                SubtypeName = subtype,
                Description = desc,
                Strategy = new BjtAmplifierStrategy(subtype)
            };
        }

        // 单只 MOSFET/JFET：子类型由 MOSFET 教学策略统一识别；
        // 耗尽型/JFET 由策略内部明确拒绝并由失败信息点名器件（契约 §3 / §5.2）。
        if (mosList.Count == 1)
        {
            string mosSubtype = MosfetAmplifierStrategy.DetectSubtype(netlist);
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.MosfetAmplifier,
                SubtypeName = mosSubtype,
                Description = $"场效应管 (MOSFET) 放大电路 [{mosSubtype}]",
                Strategy = new MosfetAmplifierStrategy(mosSubtype)
            };
        }

        // 无晶体管且有二极管 → 二极管非线性教学电路（子类型由二极管策略识别，契约 §3 / §4.2）。
        if (elements.Any(e => e.Type == ElementType.Diode))
        {
            string diodeSubtype = DiodeCircuitStrategy.DetectSubtype(netlist) ?? "DiodeBias";
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.DiodeCircuit,
                SubtypeName = diodeSubtype,
                Description = $"二极管非线性电路 [{diodeSubtype}]",
                Strategy = new DiodeCircuitStrategy(diodeSubtype)
            };
        }

        // 2. Count Reactive Elements (Capacitors and Inductors)
        int capCount = elements.Count(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized);
        int indCount = elements.Count(e => e.Type == ElementType.Inductor);
        int reactiveCount = capCount + indCount;

        // 3. First-Order Circuit (Exactly 1 reactive element)
        if (reactiveCount == 1)
        {
            if (capCount == 1)
            {
                var sub = DetectFirstOrderRcSubtype(netlist);
                return new ClassificationResult
                {
                    TopologyType = CircuitTopologyType.FirstOrderRc,
                    SubtypeName = sub,
                    Description = $"一阶 RC 动态与频率响应电路 [{sub}]",
                    Strategy = new FirstOrderRcStrategy(isRl: false, sub)
                };
            }
            else // indCount == 1
            {
                return new ClassificationResult
                {
                    TopologyType = CircuitTopologyType.FirstOrderRl,
                    SubtypeName = "RlTransient",
                    Description = "一阶 RL 动态与频率响应电路",
                    Strategy = new FirstOrderRcStrategy(isRl: true, "RlTransient")
                };
            }
        }

        // 4. Second-Order RLC Resonance (Exactly 2 reactive elements, e.g. 1 L + 1 C)
        if (reactiveCount == 2 && capCount >= 1 && indCount >= 1)
        {
            var rlcSubtype = DetectRlcSubtype(netlist);
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.SecondOrderRlc,
                SubtypeName = rlcSubtype,
                Description = $"二阶 RLC 谐振与瞬态电路 [{rlcSubtype}]",
                Strategy = new SecondOrderRlcStrategy(rlcSubtype)
            };
        }

        // 5. General Linear Network (Multi-loop, purely resistive, or general MNA)
        return new ClassificationResult
        {
            TopologyType = CircuitTopologyType.MnaLinear,
            SubtypeName = "GeneralLinear",
            Description = "通用线性网络（受控源/动态元件/戴维南等效/频响）",
            Strategy = new MnaLinearStrategy()
        };
    }

    private static bool IsBjtCommonBase(ElectricalNetlist netlist, CircuitElement bjt)
    {
        var bNet = netlist.FindNetForPin(bjt.Id, "B");
        var eNet = netlist.FindNetForPin(bjt.Id, "E");
        var cNet = netlist.FindNetForPin(bjt.Id, "C");
        if (bNet == null || eNet == null || cNet == null) return false;

        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));

        // Base is AC grounded via bypass capacitor to ground or VCC
        bool baseHasBypass = netlist.Elements.Values.Any(e => (e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized) &&
            ((netlist.FindNetForPin(e.Id, "1") == bNet && (netlist.FindNetForPin(e.Id, "2")?.IsGround == true || netlist.FindNetForPin(e.Id, "2") == gndNet || netlist.FindNetForPin(e.Id, "2") == vccNet)) ||
             (netlist.FindNetForPin(e.Id, "2") == bNet && (netlist.FindNetForPin(e.Id, "1")?.IsGround == true || netlist.FindNetForPin(e.Id, "1") == gndNet || netlist.FindNetForPin(e.Id, "1") == vccNet))));

        // Also check if base is directly connected to ground
        bool baseIsGround = bNet.IsGround || bNet == gndNet;

        // Emitter is not directly grounded (it's the input terminal in CB)
        bool emitterIsDirectGround = eNet.IsGround || eNet == gndNet;

        return (baseHasBypass || baseIsGround) && !emitterIsDirectGround;
    }

    /// <summary>
    /// Resolves the positive supply rail of the netlist: an explicitly flagged power
    /// net / net named "VCC", else the positive terminal of the (single) DC source,
    /// else the net of a VCC port symbol.
    /// </summary>
    private static ElectricalNet? ResolveSupplyNet(ElectricalNetlist netlist)
    {
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));
        if (vccNet != null) return vccNet;

        var dcSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.DcVoltageSource);
        if (dcSrc != null)
        {
            var posNet = netlist.FindNetForPin(dcSrc.Id, "pos");
            if (posNet != null && !posNet.IsGround) return posNet;
        }

        var vccPort = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.VccPort);
        return vccPort != null ? netlist.FindNetForPin(vccPort.Id, "VCC") : null;
    }

    /// <summary>True when a resistor is connected directly between the two nets (order agnostic).</summary>
    private static bool HasResistorAcross(ElectricalNetlist netlist, ElectricalNet? first, ElectricalNet? second)
    {
        if (first == null || second == null) return false;
        return netlist.Elements.Values.Any(e => e.Type == ElementType.Resistor &&
            ((netlist.FindNetForPin(e.Id, "1") == first && netlist.FindNetForPin(e.Id, "2") == second) ||
             (netlist.FindNetForPin(e.Id, "2") == first && netlist.FindNetForPin(e.Id, "1") == second)));
    }

    /// <summary>
    /// Detects the emitter-follower (common-collector) connection (contract §3/§6):
    /// the collector sits directly on the supply rail (PNP mirror: directly on GND,
    /// because its collector is the AC-ground terminal), the emitter has a DC return
    /// through a resistor and is the output (not directly on its AC-ground rail), and
    /// the base is an input (input coupling capacitor or a base bias network).
    /// Requires at least Re plus a base bias/coupling path, so a bare or degenerate
    /// BJT network is never classified as CommonCollector.
    /// </summary>
    private static bool IsBjtCommonCollector(ElectricalNetlist netlist, CircuitElement bjt)
    {
        var bNet = netlist.FindNetForPin(bjt.Id, "B");
        var eNet = netlist.FindNetForPin(bjt.Id, "E");
        var cNet = netlist.FindNetForPin(bjt.Id, "C");
        if (bNet == null || eNet == null || cNet == null) return false;

        var gndNet = netlist.GroundNet;
        var vccNet = ResolveSupplyNet(netlist);
        if (vccNet == null) return false;

        bool IsGndNet(ElectricalNet? net) => net != null && (net.IsGround || net == gndNet);
        bool isPnp = bjt.Type == ElementType.BjtPnp;

        // 集电极直接接电源轨（NPN: C–VCC；PNP 镜像: C–GND），与电源轨之间没有电阻。
        bool collectorAtRail = isPnp ? IsGndNet(cNet) : cNet == vccNet;
        if (!collectorAtRail) return false;

        // 发射极是输出端，不能直接落在其交流地轨上（NPN: GND；PNP: VCC）。
        bool emitterAtAcRail = isPnp ? eNet == vccNet : IsGndNet(eNet);
        if (emitterAtAcRail) return false;

        // 发射极经 Re 有直流通路：NPN 到 GND，PNP 镜像到 VCC。
        var emitterRail = isPnp ? vccNet : gndNet;
        if (!HasResistorAcross(netlist, eNet, emitterRail)) return false;

        // 基极为输入：存在输入耦合电容（基极到交流源端子）或基极偏置电阻网络。
        bool hasInputCoupling = false;
        foreach (var cap in netlist.Elements.Values.Where(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized))
        {
            var capNet1 = netlist.FindNetForPin(cap.Id, "1");
            var capNet2 = netlist.FindNetForPin(cap.Id, "2");
            ElectricalNet? otherNet = capNet1 == bNet ? capNet2 : capNet2 == bNet ? capNet1 : null;
            if (otherNet == null) continue;
            if (netlist.Elements.Values.Any(src => src.Type == ElementType.AcVoltageSource &&
                    (netlist.FindNetForPin(src.Id, "pos") == otherNet || netlist.FindNetForPin(src.Id, "neg") == otherNet)))
            {
                hasInputCoupling = true;
                break;
            }
        }

        bool hasBaseBiasNetwork = HasResistorAcross(netlist, bNet, vccNet) ||
                                  HasResistorAcross(netlist, bNet, gndNet) ||
                                  HasResistorAcross(netlist, bNet, cNet);
        return hasInputCoupling || hasBaseBiasNetwork;
    }

    private static string DetectBjtBiasTopology(ElectricalNetlist netlist, CircuitElement bjt)
    {
        var bNet = netlist.FindNetForPin(bjt.Id, "B");
        var cNet = netlist.FindNetForPin(bjt.Id, "C");
        var eNet = netlist.FindNetForPin(bjt.Id, "E");

        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));

        if (bNet == null || cNet == null || eNet == null) return "VoltageDividerSelfBias";

        bool isPnp = bjt.Type == ElementType.BjtPnp;
        bool hasBaseToGndResistor = false;
        bool hasBaseToVccResistor = false;
        bool hasCollectorToBaseResistor = false;

        foreach (var pinRef in bNet.ConnectedPins)
        {
            if (pinRef.ElementId == bjt.Id) continue;
            if (netlist.Elements.TryGetValue(pinRef.ElementId, out var el) && el.Type == ElementType.Resistor)
            {
                var otherPin = el.Pins.FirstOrDefault(p => p.Id != pinRef.PinId);
                if (otherPin != null)
                {
                    var otherNet = netlist.FindNetForPin(el.Id, otherPin.Id);
                    if (otherNet != null)
                    {
                        if (otherNet.IsGround || otherNet == gndNet) hasBaseToGndResistor = true;
                        if (otherNet.IsPower || otherNet == vccNet) hasBaseToVccResistor = true;
                        if (otherNet == cNet) hasCollectorToBaseResistor = true;
                    }
                }
            }
        }

        if (hasCollectorToBaseResistor) return "CollectorFeedbackBias";
        if (hasBaseToVccResistor && hasBaseToGndResistor) return "VoltageDividerSelfBias";
        // 固定偏置的基极电阻接在低电位侧：NPN 经 RB 接 VCC，PNP 经 RB 接 GND（基极电流由基极流出）。
        if (isPnp ? hasBaseToGndResistor : hasBaseToVccResistor) return "FixedBias";

        // Check if Rb2 exists in elements
        if (netlist.Elements.ContainsKey("Rb2") || netlist.Elements.ContainsKey("R2"))
        {
            return "VoltageDividerSelfBias";
        }
        if (netlist.Elements.ContainsKey("Rb") && !netlist.Elements.ContainsKey("Rb2"))
        {
            return "FixedBias";
        }

        return "VoltageDividerSelfBias";
    }

    private static string DetectFirstOrderRcSubtype(ElectricalNetlist netlist)
    {
        bool hasAc = netlist.Elements.Values.Any(e => e.Type == ElementType.AcVoltageSource);
        bool hasDc = netlist.Elements.Values.Any(e => e.Type == ElementType.DcVoltageSource);

        if (hasDc && !hasAc) return "DcStepTransient";
        if (hasAc)
        {
            var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized);
            if (cap != null)
            {
                var pin1Net = netlist.FindNetForPin(cap.Id, "1");
                var pin2Net = netlist.FindNetForPin(cap.Id, "2");
                bool isGrounded = (pin1Net?.IsGround ?? false) || (pin2Net?.IsGround ?? false);
                return isGrounded ? "LowPassFilter" : "HighPassFilter";
            }
        }
        return "DcStepTransient";
    }

    private static string DetectRlcSubtype(ElectricalNetlist netlist)
    {
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized);

        if (ind == null || cap == null) return "SeriesRlc";

        var indNets = new[] { netlist.FindNetForPin(ind.Id, "1"), netlist.FindNetForPin(ind.Id, "2") };
        var capNets = new[] { netlist.FindNetForPin(cap.Id, "1"), netlist.FindNetForPin(cap.Id, "2") };

        int sharedNets = indNets.Count(inNet => inNet != null && capNets.Contains(inNet));
        return (sharedNets >= 2) ? "ParallelRlc" : "SeriesRlc";
    }
}

internal sealed class UnsupportedCircuitStrategy(string reason) : ICircuitStrategy
{
    public CircuitTopologyType TopologyType => CircuitTopologyType.Unsupported;
    public string SubtypeName => "Unsupported";
    public System.Threading.Tasks.Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(PedagogicalSolution.Failure(TopologyType, reason));
}
