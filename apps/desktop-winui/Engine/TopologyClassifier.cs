// ============================================================================
// File: apps/desktop-winui/Engine/TopologyClassifier.cs
// Namespace: CircuitAssistant.Engine
// Description: Hierarchical Circuit Topology Classifier. Automatically detects
//              1st-order RC/RL, 2nd-order RLC, BJT/MOSFET amplifier, and MNA networks.
// ============================================================================

using System;
using System.Linq;
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

        // 1. Check for Active Devices (Transistors)
        var bjtList = elements.Where(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp).ToList();
        var mosList = elements.Where(e => e.Type is ElementType.Mosfet or ElementType.MosfetNEnh 
                                          or ElementType.MosfetPEnh or ElementType.MosfetNDep 
                                          or ElementType.MosfetPDep or ElementType.Jfet
                                          or ElementType.JfetN or ElementType.JfetP).ToList();

        if (bjtList.Count >= 1)
        {
            var bjt = bjtList[0];
            var biasSubtype = DetectBjtBiasTopology(netlist, bjt);
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.BjtAmplifier,
                SubtypeName = biasSubtype,
                Description = $"双极型晶体管 (BJT) 放大电路 [{biasSubtype}]",
                Strategy = new BjtAmplifierStrategy(biasSubtype)
            };
        }

        if (mosList.Count >= 1)
        {
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.MosfetAmplifier,
                SubtypeName = "CommonSource",
                Description = "场效应管 (MOSFET) 共源极放大电路",
                Strategy = new MnaLinearStrategy() // General linear / MNA fallback
            };
        }

        // 2. Count Reactive Elements (Capacitors and Inductors)
        int capCount = elements.Count(e => e.Type == ElementType.Capacitor);
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
            Description = "通用线性网络 (基于 SPICE MNA 矩阵方程分析)",
            Strategy = new MnaLinearStrategy()
        };
    }

    private static string DetectBjtBiasTopology(ElectricalNetlist netlist, CircuitElement bjt)
    {
        var bNet = netlist.FindNetForPin(bjt.Id, "B");
        var cNet = netlist.FindNetForPin(bjt.Id, "C");
        var eNet = netlist.FindNetForPin(bjt.Id, "E");

        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));

        if (bNet == null || cNet == null || eNet == null) return "VoltageDividerSelfBias";

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
        if (hasBaseToVccResistor) return "FixedBias";

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
            var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);
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
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);

        if (ind == null || cap == null) return "SeriesRlc";

        var indNets = new[] { netlist.FindNetForPin(ind.Id, "1"), netlist.FindNetForPin(ind.Id, "2") };
        var capNets = new[] { netlist.FindNetForPin(cap.Id, "1"), netlist.FindNetForPin(cap.Id, "2") };

        int sharedNets = indNets.Count(inNet => inNet != null && capNets.Contains(inNet));
        return (sharedNets >= 2) ? "ParallelRlc" : "SeriesRlc";
    }
}
