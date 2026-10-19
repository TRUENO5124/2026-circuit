// ============================================================================
// File: apps/desktop-winui/Engine/ElectricalNetlist.cs
// Namespace: CircuitAssistant.Engine
// Description: Core Electrical Netlist, Graph Topology, Nodes, Branches, and
//              SPICE Deck Data Model for Industrial EDA Analysis.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine;

/// <summary>
/// Immutable reference to a specific pin of a circuit element.
/// </summary>
public sealed record ComponentPinRef(string ElementId, string PinId, string PinName)
{
    public override string ToString() => $"{ElementId}.{PinId}";
}

/// <summary>
/// Represents an electrical equipotential net (connected component of conductors and pins).
/// </summary>
public class ElectricalNet
{
    public int NetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGround { get; set; }
    public bool IsPower { get; set; }

    public List<ComponentPinRef> ConnectedPins { get; } = [];
    public List<string> ConnectedWireIds { get; } = [];

    public bool ContainsPin(string elementId, string pinId) =>
        ConnectedPins.Any(p => p.ElementId == elementId && p.PinId == pinId);

    public bool ContainsElement(string elementId) =>
        ConnectedPins.Any(p => p.ElementId == elementId);

    public IEnumerable<ComponentPinRef> GetPinsForElement(string elementId) =>
        ConnectedPins.Where(p => p.ElementId == elementId);

    public override string ToString() =>
        $"Net[{NetId}: {Name}, Pins={ConnectedPins.Count}, IsGnd={IsGround}]";
}

/// <summary>
/// Represents an electrical node in the circuit graph (used by MNA, nodal analysis, and ODE solvers).
/// Node 0 is canonically the reference Ground (0 V).
/// </summary>
public class ElectricalNode
{
    public int Index { get; set; } // 0 = GND, 1..N
    public string Name { get; set; } = string.Empty;
    public ElectricalNet Net { get; set; } = null!;
    public bool IsReference => Index == 0;

    public List<ElectricalBranch> ConnectedBranches { get; } = [];

    public override string ToString() => $"Node[{Index}: {Name}]";
}

/// <summary>
/// Orientation of branch current / voltage reference direction.
/// </summary>
public enum BranchDirection
{
    FromPositiveToNegative,
    Bidirectional,
    Unspecified
}

/// <summary>
/// Represents a two-terminal circuit branch connecting two electrical nodes.
/// </summary>
public class ElectricalBranch
{
    public string BranchId { get; set; } = string.Empty;
    public CircuitElement Element { get; set; } = null!;
    public ElementType ElementType => Element.Type;

    public ElectricalNode PositiveNode { get; set; } = null!;
    public ElectricalNode NegativeNode { get; set; } = null!;

    public ComponentPinRef? PositivePin { get; set; }
    public ComponentPinRef? NegativePin { get; set; }

    /// <summary>
    /// Component numerical value in base SI units (Ohms, Farads, Henries, Volts, Amperes).
    /// </summary>
    public double ValueSI { get; set; }
    public double DisplayNumber => Element.DisplayNumber;
    public string UnitSymbol => Element.UnitSymbol;
    public BranchDirection Direction { get; set; } = BranchDirection.FromPositiveToNegative;

    /// <summary>
    /// Indicates whether this branch introduces an auxiliary current variable in the MNA matrix equation.
    /// (Independent voltage sources, inductors, controlled voltage sources).
    /// </summary>
    public bool RequiresBranchCurrentVariable =>
        ElementType is ElementType.DcVoltageSource 
                    or ElementType.AcVoltageSource 
                    or ElementType.Inductor 
                    or ElementType.Vcvs 
                    or ElementType.Ccvs;

    public override string ToString() =>
        $"{BranchId} ({ElementType}) [{PositiveNode.Name} -> {NegativeNode.Name}] Value={ValueSI:G4}{UnitSymbol}";
}

/// <summary>
/// Encapsulates multi-terminal active semiconductor devices (BJT, MOSFET).
/// </summary>
public class TransistorDevice
{
    public CircuitElement Element { get; set; } = null!;
    public ElementType DeviceType => Element.Type;

    // BJT terminals
    public ElectricalNode? BaseNode { get; set; }
    public ElectricalNode? CollectorNode { get; set; }
    public ElectricalNode? EmitterNode { get; set; }

    // MOSFET terminals
    public ElectricalNode? GateNode { get; set; }
    public ElectricalNode? DrainNode { get; set; }
    public ElectricalNode? SourceNode { get; set; }

    /// <summary>
    /// Forward current gain beta (BJT) or transconductance factor (MOSFET).
    /// </summary>
    public double GainParameter => Element.DisplayNumber;

    public override string ToString() =>
        $"{Element.Id} ({DeviceType}) B/G={BaseNode?.Name ?? GateNode?.Name}, C/D={CollectorNode?.Name ?? DrainNode?.Name}, E/S={EmitterNode?.Name ?? SourceNode?.Name}";
}

public enum NetlistValidationSeverity
{
    Info,
    Warning,
    Error
}

public record NetlistValidationIssue(
    NetlistValidationSeverity Severity,
    string Code,
    string Message,
    string? ElementId = null,
    string? PinId = null);

public class NetlistValidationResult
{
    public bool IsValid => Issues.All(i => i.Severity != NetlistValidationSeverity.Error);
    public List<NetlistValidationIssue> Issues { get; } = [];
}

/// <summary>
/// Canonical Electrical Netlist graph representation of a schematic.
/// Node 0 is strictly Ground reference (V_0 = 0 V).
/// </summary>
public class ElectricalNetlist
{
    public List<ElectricalNet> Nets { get; } = [];
    public List<ElectricalNode> Nodes { get; } = [];
    public List<ElectricalBranch> Branches { get; } = [];
    public List<TransistorDevice> Transistors { get; } = [];

    public Dictionary<string, CircuitElement> Elements { get; } = [];
    public Dictionary<string, CircuitWire> Wires { get; } = [];

    public ElectricalNet? GroundNet => Nets.FirstOrDefault(n => n.IsGround);
    public ElectricalNode? GroundNode => Nodes.FirstOrDefault(n => n.IsReference);
    public ElectricalNet? PowerNet => Nets.FirstOrDefault(n => n.IsPower);

    public int NodeCount => Nodes.Count;
    public int BranchCount => Branches.Count;
    public bool HasGround => GroundNet != null;

    public ElectricalNet? FindNetForPin(string elementId, string pinId) =>
        Nets.FirstOrDefault(n => n.ContainsPin(elementId, pinId) ||
            (pinId is "1" or "pos" && n.ContainsPin(elementId, "+")) ||
            (pinId is "2" or "neg" && n.ContainsPin(elementId, "-")) ||
            (pinId == "+" && (n.ContainsPin(elementId, "pos") || n.ContainsPin(elementId, "1"))) ||
            (pinId == "-" && (n.ContainsPin(elementId, "neg") || n.ContainsPin(elementId, "2"))));

    public ElectricalNode? FindNodeForPin(string elementId, string pinId)
    {
        var net = FindNetForPin(elementId, pinId);
        return net == null ? null : Nodes.FirstOrDefault(node => node.Net == net);
    }

    public ElectricalNet? FindNetByName(string name) =>
        Nets.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    public ElectricalNode? FindNodeByName(string name) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ElectricalBranch> GetBranchesIncidentToNode(int nodeIndex) =>
        Branches.Where(b => b.PositiveNode.Index == nodeIndex || b.NegativeNode.Index == nodeIndex);

    public IEnumerable<ElectricalBranch> GetBranchesBetweenNodes(int node1, int node2) =>
        Branches.Where(b => (b.PositiveNode.Index == node1 && b.NegativeNode.Index == node2) ||
                            (b.PositiveNode.Index == node2 && b.NegativeNode.Index == node1));

    public IEnumerable<CircuitElement> GetElementsByType(ElementType type) =>
        Elements.Values.Where(e => e.Type == type);

    /// <summary>
    /// Performs topological validation on the netlist, catching floating pins, short circuits,
    /// and missing ground references.
    /// </summary>
    public NetlistValidationResult Validate()
    {
        var result = new NetlistValidationResult();

        // 1. Ground check
        if (!HasGround)
        {
            result.Issues.Add(new NetlistValidationIssue(
                NetlistValidationSeverity.Error,
                "NO_GROUND",
                "电路中没有可用的参考地，请添加 GND 并连接到电路。"));
        }

        // 2. Short circuit check (both terminals of a branch connected to the same node)
        foreach (var branch in Branches)
        {
            if (branch.PositiveNode.Index == branch.NegativeNode.Index)
            {
                var severity = NetlistValidationSeverity.Error;

                result.Issues.Add(new NetlistValidationIssue(
                    severity,
                    "BRANCH_SHORT_CIRCUIT",
                    $"元件 '{branch.Element.Id}' 的两个引脚短路到同一节点 '{branch.PositiveNode.Name}'！",
                    branch.Element.Id));
            }
        }

        // 3. Floating pins check
        foreach (var el in Elements.Values)
        {
            if (el.Type == ElementType.Ground) continue;
            try
            {
                var value = NetlistExtractor.ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol);
                if (!double.IsFinite(value) ||
                    ((el.Type is ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized or ElementType.Inductor or ElementType.BjtNpn or ElementType.BjtPnp) && value <= 0) ||
                    (el.IsAcSource && (!double.IsFinite(el.FrequencyHz) || el.FrequencyHz <= 0)))
                    throw new InvalidOperationException("数值必须有限，电阻/储能元件/β/频率须大于零。");
            }
            catch (Exception ex)
            {
                result.Issues.Add(new(NetlistValidationSeverity.Error, "INVALID_PARAMETER", $"元件 {el.Id}: {ex.Message}", el.Id));
            }
            foreach (var pin in el.Pins)
            {
                var net = FindNetForPin(el.Id, pin.Id);
                if (net == null)
                {
                    result.Issues.Add(new NetlistValidationIssue(
                        NetlistValidationSeverity.Error,
                        "FLOATING_PIN",
                        $"元件 '{el.Id}' 的引脚 '{pin.Name}' ({pin.Id}) 未连接任何导线 (悬空)。",
                        el.Id,
                        pin.Id));
                }
                else if (net.ConnectedPins.Count == 1 && !net.IsGround)
                {
                    result.Issues.Add(new NetlistValidationIssue(
                        NetlistValidationSeverity.Error,
                        "DEGREE_ZERO_PIN",
                        $"元件 '{el.Id}' 的引脚 '{pin.Name}' 未形成有效电气回路。",
                        el.Id,
                        pin.Id));
                }
            }
        }

        // 4. Power supply short to ground check
        if (PowerNet != null && GroundNet != null && PowerNet.NetId == GroundNet.NetId)
        {
            result.Issues.Add(new NetlistValidationIssue(
                NetlistValidationSeverity.Error,
                "POWER_SHORTED_TO_GROUND",
                "致命错误：电源网络 (VCC) 与参考地网络 (GND) 发生直通短路！"));
        }

        if (HasGround && !Elements.Values.Any(e => e.Type == ElementType.Ground))
            result.Issues.Add(new(NetlistValidationSeverity.Warning, "IMPLICIT_GROUND", "未放置 GND 符号，已选择参考网络；请核对参考节点是否符合电路定义。"));

        // Reachability through devices catches isolated loops and orphan conductors.
        if (GroundNet != null)
        {
            var reached = new HashSet<int> { GroundNet.NetId };
            bool changed;
            do
            {
                changed = false;
                foreach (var el in Elements.Values)
                {
                    var nets = el.Pins.Select(p => FindNetForPin(el.Id, p.Id)).Where(n => n != null).Select(n => n!.NetId).Distinct().ToList();
                    if (nets.Any(reached.Contains)) foreach (var id in nets) changed |= reached.Add(id);
                }
            } while (changed);
            if (Nets.Any(n => !reached.Contains(n.NetId)))
                result.Issues.Add(new(NetlistValidationSeverity.Error, "DISCONNECTED_NETWORK", "存在与参考电路分离的元件或悬空导线，请连接或删除后再求解。"));
        }

        return result;
    }

    /// <summary>
    /// Exports the netlist to a standard SPICE deck string representation.
    /// </summary>
    public string ToSpiceNetlist()
    {
        var sb = new StringBuilder();
        sb.AppendLine("* CircuitAssistant Generated SPICE Netlist");
        sb.AppendLine($"* Nodes: {Nodes.Count}, Branches: {Branches.Count}, Transistors: {Transistors.Count}");
        sb.AppendLine();

        foreach (var b in Branches)
        {
            string n1 = b.PositiveNode.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string n2 = b.NegativeNode.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string id = b.Element.Id;

            switch (b.ElementType)
            {
                case ElementType.Resistor:
                    sb.AppendLine($"R_{id} {n1} {n2} {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.Capacitor:
                case ElementType.CapacitorPolarized:
                    sb.AppendLine($"C_{id} {n1} {n2} {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.Inductor:
                    sb.AppendLine($"L_{id} {n1} {n2} {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.DcVoltageSource:
                    sb.AppendLine($"V_{id} {n1} {n2} DC {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.AcVoltageSource:
                    sb.AppendLine($"V_{id} {n1} {n2} AC {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.DcCurrentSource:
                    sb.AppendLine($"I_{id} {n2} {n1} DC {b.ValueSI.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}");
                    break;
                case ElementType.Diode:
                    sb.AppendLine($"D_{id} {n1} {n2} 1N4148");
                    break;
            }
        }

        foreach (var t in Transistors)
        {
            if (t.DeviceType == ElementType.BjtNpn)
            {
                sb.AppendLine($"Q_{t.Element.Id} {t.CollectorNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} {t.BaseNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} {t.EmitterNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} 2N3904");
            }
            else if (t.DeviceType == ElementType.BjtPnp)
            {
                sb.AppendLine($"Q_{t.Element.Id} {t.CollectorNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} {t.BaseNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} {t.EmitterNode?.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"} 2N3906");
            }
        }

        sb.AppendLine(".END");
        return sb.ToString();
    }
}
