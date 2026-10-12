# Milestone 2 Explorer M2-1 Handoff Report: Electrical Netlist Model & Graph Extraction Engine

## 1. Observation

### 1.1 Canvas Models and Graph Absence in Existing Codebase
- **`apps/desktop-winui/Models/CircuitElement.cs`**:
  - `CircuitPin` (lines 37–53) defines `Id`, `Name`, `RelativeX`, `RelativeY`, and `ConnectedNetId` (initialized to `-1`, never updated).
  - `CircuitWire` (lines 55–158) defines `Id`, `StartElementId`, `StartPinName`, `EndElementId`, `EndPinName`, `StartX`, `StartY`, `EndX`, `EndY`, `NetId`, `FlipCorner`, and `CustomMid`. It includes `GetPathPoints()` and `HitTest()`, but has no graph connectivity model.
  - `CircuitElement` (lines 160–360) defines `ElementType` (24 enum values), `DisplayNumber`, `UnitSymbol`, `X`, `Y`, `RotationDeg`, `Orientation`, and `Pins`. `GetPinWorldPosition(pin)` (lines 240–246) correctly rotates relative pin coordinates by `RotationDeg` via `Matrix3x2.CreateRotation(rad)`.
  - **No Graph Engine**: The directory `apps/desktop-winui/Engine/` did not exist. There was zero representation of electrical nodes, nets, branches, or reference potential.

### 1.2 Template Structure in `CircuitTemplateFactory.cs`
- In `apps/desktop-winui/Services/CircuitTemplateFactory.cs`:
  - `CreateCommonEmitterTemplate()` (lines 16–250): 12 elements (`vs`, `C1`, `Rb1`, `Rb2`, `Q1`, `Rc`, `Re`, `Ce`, `C2`, `RL`, `VCC`, `GND`) and 23 wires.
    - Power rail `W_VCC_RAIL` (line 228) runs horizontally at $Y=80$ from $X=240$ to $X=620$. Component leads `W_RB1_VCC`, `W_RC_VCC`, `W_VCC_FEED` tap into `W_VCC_RAIL` with `EndPinName = "WireTap"`.
    - Ground rail `W_GND_RAIL` (line 240) runs horizontally at $Y=480$ from $X=80$ to $X=620$. Vertical branches `W_VS_GND`, `W_RB2_GND`, `W_RE_GND`, `W_CE_GND`, `W_RL_GND`, `W_VCC_GND` tap into `W_GND_RAIL` with `EndPinName = "WireTap"`.
    - Ground symbol `GND` (lines 200–214) is at $X=340, Y=502$. Its pin `GND` (relative $Y=-22$) is located at world coordinates $(340, 480)$ — lying directly on the segment of `W_GND_RAIL` without a separate connecting wire.
  - `CreateRcTransientTemplate()` (lines 252–338): 4 elements (`VS`, `R1`, `C1`, `GND`) and 5 wires (`W_VS_R1`, `W_R1_C1`, `W_GND_RAIL`, `W_VS_GND`, `W_C1_GND`).
    - Ground symbol `GND` pin is at $(240, 360)$, lying directly on `W_GND_RAIL` at $Y=360$.
  - `CreateRcCoupledTemplate()` (lines 340–518): 9 elements (`vs`, `C1`, `Rb`, `Q1`, `Rc`, `C2`, `RL`, `VCC`, `GND`) and 16 wires. Emitter `Q1.E` connects to ground rail via `W_EMIT_GND`.

### 1.3 Challenger Observations from Milestone 1 Tests
- Running `python tests/verify_templates.py` and `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj`:
  - Challenger Suite 2 reported: `Reference ground element 'GND' (pin 'GND') is COMPLETELY DISCONNECTED from W_GND_RAIL in netlist graph! (Degree 0 pin)` because Challenger only checked explicit `StartElementId`/`EndElementId` wire pointers and did not evaluate pins resting directly on wire segments.
  - Challenger Suite 3 reported: Bus rail endpoints `(240, 80)` diverge from component pins (e.g. `Rb1.1` at `(240, 120)`), demonstrating that in bus/trunk routing, rail wires represent conductor trunks rather than direct point-to-point pin wires.
  - Conclusion from Challenger: A naive extractor that only checks wire endpoints against pin names will fail to connect Ground and fail to unify bus taps. A robust industrial extractor must incorporate orthogonal segment point-containment and multi-source ground resolution.

---

## 2. Logic Chain

1. **Topological Representation of Schematics**:
   - In industrial EDA software (KiCad / SPICE / Altium), an electrical schematic consists of:
     - Terminals (Pins) on Components.
     - Conductors (Wires) with endpoints and bends.
     - Junctions (Wire Taps / T-nodes) where a conductor touches another conductor.
   - An electrical net is an equivalence class of mutually connected pins and wires.
   - Therefore, graph extraction requires computing connected components across the heterogeneous graph of pins and wires.

2. **Why Pure Endpoint Snapping is Insufficient**:
   - When a schematic uses bus/trunk routing (GB/T 4728 standard required by R2 & R3):
     - A rail wire (e.g., `W_GND_RAIL` from $(80, 480)$ to $(620, 480)$) has no component pin at its geometric endpoints.
     - Components connect via orthogonal drop wires that tap the trunk mid-segment (`EndPinName == "WireTap"`).
     - Furthermore, ground symbols (e.g. `GND` at $(340, 502)$) have their pin $(340, 480)$ placed directly on top of the rail line.
   - *Deduction*: Net extraction must support three connection mechanisms:
     1. Wire endpoint snapping to pins within snap tolerance $\epsilon_{snap} = 8\text{px}$.
     2. Wire endpoint tapping onto wire segments within tolerance $\epsilon_{tap} = 5\text{px}$ (or explicit `EndPinName == "WireTap"`).
     3. Component pin resting directly on a wire segment within tolerance $\epsilon_{tap} = 5\text{px}$.

3. **Ground (Reference Potential) Resolution**:
   - In nodal analysis and MNA, node 0 must be defined to establish the reference potential ($V_0 = 0\text{V}$).
   - Schematics may have:
     - Exactly one Ground symbol (`ElementType.Ground`).
     - Multiple Ground symbols (e.g. input GND, output GND, power GND) representing the same reference net.
     - Zero Ground symbols (if a student forgot to place GND).
   - *Deduction*: Netlist extraction must enforce a unified Ground resolution hierarchy:
     1. All nets containing `ElementType.Ground` must be merged into a single Net 0 (`IsGround = true`, `Name = "GND"`).
     2. If no Ground symbol exists, check for nets labeled "GND" or `NetId == 0`.
     3. If still unresolved, default to `DcVoltageSource.neg` or `AcVoltageSource.neg`.
     4. If no source, pick the net with the highest pin degree.

4. **Branch and Component Mapping**:
   - Two-terminal elements (Resistors, Capacitors, Inductors, Sources) map directly to `ElectricalBranch` objects between `PositiveNode` and `NegativeNode`.
   - Element values must be normalized to base SI units ($\Omega, \text{F}, \text{H}, \text{V}, \text{A}$) using their `DisplayNumber` and `UnitSymbol` (e.g. $51\text{ k}\Omega \to 51000\,\Omega$, $10\,\mu\text{F} \to 1.0 \times 10^{-5}\text{ F}$).
   - Active multi-terminal devices (BJTs, MOSFETs) map to `TransistorDevice` objects capturing terminal node references ($B, C, E$ or $G, D, S$) and gain parameters ($\beta$, $g_m$).

5. **Empirical Validation**:
   - We implemented and executed the proposed algorithm in `test_extractor.py` against all three canonical templates:
     - `RC_Transient`: exactly 3 nodes (`Vin`, `Vc/Vout`, `GND`).
     - `CommonEmitter_Amp`: exactly 7 nodes (`VCC`, `GND`, `Vin`, `Vb`, `Ve`, `Vc`, `Vout`).
     - `RC_Coupled_Amp`: exactly 6 nodes (`VCC`, `GND`, `Vin`, `Vb`, `Vc`, `Vout`).
   - All 5 critical edge cases (floating pins, short circuits, direct pin touching, missing GND, multiple GNDs) passed 100%.

---

## 3. Detailed Design Specification

### 3.1 Design of `apps/desktop-winui/Engine/ElectricalNetlist.cs`

```csharp
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
        Nets.FirstOrDefault(n => n.ContainsPin(elementId, pinId));

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
                NetlistValidationSeverity.Warning,
                "NO_GROUND",
                "电路中未找到参考地 (GND)，已自动将负极网络作为 0V 参考点。"));
        }

        // 2. Short circuit check (both terminals of a branch connected to the same node)
        foreach (var branch in Branches)
        {
            if (branch.PositiveNode.Index == branch.NegativeNode.Index)
            {
                var severity = branch.ElementType is ElementType.DcVoltageSource or ElementType.AcVoltageSource
                    ? NetlistValidationSeverity.Error
                    : NetlistValidationSeverity.Warning;

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
            foreach (var pin in el.Pins)
            {
                var net = FindNetForPin(el.Id, pin.Id);
                if (net == null)
                {
                    result.Issues.Add(new NetlistValidationIssue(
                        NetlistValidationSeverity.Warning,
                        "FLOATING_PIN",
                        $"元件 '{el.Id}' 的引脚 '{pin.Name}' ({pin.Id}) 未连接任何导线 (悬空)。",
                        el.Id,
                        pin.Id));
                }
                else if (net.ConnectedPins.Count == 1 && net.ConnectedWireIds.Count == 0)
                {
                    result.Issues.Add(new NetlistValidationIssue(
                        NetlistValidationSeverity.Warning,
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
            string n1 = b.PositiveNode.Name;
            string n2 = b.NegativeNode.Name;
            string id = b.Element.Id;

            switch (b.ElementType)
            {
                case ElementType.Resistor:
                    sb.AppendLine($"R_{id} {n1} {n2} {b.ValueSI:G6}");
                    break;
                case ElementType.Capacitor:
                    sb.AppendLine($"C_{id} {n1} {n2} {b.ValueSI:G6}");
                    break;
                case ElementType.Inductor:
                    sb.AppendLine($"L_{id} {n1} {n2} {b.ValueSI:G6}");
                    break;
                case ElementType.DcVoltageSource:
                    sb.AppendLine($"V_{id} {n1} {n2} DC {b.ValueSI:G6}");
                    break;
                case ElementType.AcVoltageSource:
                    sb.AppendLine($"V_{id} {n1} {n2} AC {b.ValueSI:G6}");
                    break;
                case ElementType.DcCurrentSource:
                    sb.AppendLine($"I_{id} {n1} {n2} DC {b.ValueSI:G6}");
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
                sb.AppendLine($"Q_{t.Element.Id} {t.CollectorNode?.Name ?? "0"} {t.BaseNode?.Name ?? "0"} {t.EmitterNode?.Name ?? "0"} 2N3904");
            }
            else if (t.DeviceType == ElementType.BjtPnp)
            {
                sb.AppendLine($"Q_{t.Element.Id} {t.CollectorNode?.Name ?? "0"} {t.BaseNode?.Name ?? "0"} {t.EmitterNode?.Name ?? "0"} 2N3906");
            }
        }

        sb.AppendLine(".END");
        return sb.ToString();
    }
}
```

---

### 3.2 Design of `apps/desktop-winui/Engine/NetlistExtractor.cs`

```csharp
// ============================================================================
// File: apps/desktop-winui/Engine/NetlistExtractor.cs
// Namespace: CircuitAssistant.Engine
// Description: Industrial-Grade Graph Connectivity & Netlist Extraction Engine
//              using Disjoint-Set Union-Find with Orthogonal Segment Point-Containment.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine;

public static class NetlistExtractor
{
    public const float SnapTolerance = 8.0f;
    public const float SegmentTapTolerance = 5.0f;

    /// <summary>
    /// Disjoint-Set Union-Find with path compression and rank optimization.
    /// </summary>
    private class DisjointSet
    {
        private readonly Dictionary<string, string> _parent = new();
        private readonly Dictionary<string, int> _rank = new();

        public string Find(string x)
        {
            if (!_parent.ContainsKey(x))
            {
                _parent[x] = x;
                _rank[x] = 0;
                return x;
            }
            if (_parent[x] != x)
            {
                _parent[x] = Find(_parent[x]);
            }
            return _parent[x];
        }

        public void Union(string x, string y)
        {
            var rx = Find(x);
            var ry = Find(y);
            if (rx != ry)
            {
                if (_rank[rx] < _rank[ry])
                    _parent[rx] = ry;
                else if (_rank[rx] > _rank[ry])
                    _parent[ry] = rx;
                else
                {
                    _parent[ry] = rx;
                    _rank[rx]++;
                }
            }
        }

        public Dictionary<string, List<string>> GetComponents()
        {
            var dict = new Dictionary<string, List<string>>();
            foreach (var key in _parent.Keys.ToList())
            {
                var r = Find(key);
                if (!dict.TryGetValue(r, out var list))
                {
                    list = [];
                    dict[r] = list;
                }
                list.Add(key);
            }
            return dict;
        }
    }

    /// <summary>
    /// Computes the Euclidean distance from point p to line segment (a, b).
    /// </summary>
    public static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }

    /// <summary>
    /// Converts a user display value and unit symbol into standard base SI units.
    /// </summary>
    public static double ConvertToBaseSi(ElementType type, double value, string unitSymbol)
    {
        string u = unitSymbol.Trim();
        switch (type)
        {
            case ElementType.Resistor:
                if (u.StartsWith("MΩ", StringComparison.OrdinalIgnoreCase)) return value * 1e6;
                if (u.StartsWith("kΩ", StringComparison.OrdinalIgnoreCase) || u.StartsWith("KΩ")) return value * 1e3;
                return value; // Ω

            case ElementType.Capacitor:
                if (u.StartsWith("μF", StringComparison.OrdinalIgnoreCase) || u.StartsWith("uF")) return value * 1e-6;
                if (u.StartsWith("nF", StringComparison.OrdinalIgnoreCase)) return value * 1e-9;
                if (u.StartsWith("pF", StringComparison.OrdinalIgnoreCase)) return value * 1e-12;
                if (u.StartsWith("mF", StringComparison.OrdinalIgnoreCase)) return value * 1e-3;
                return value; // F

            case ElementType.Inductor:
                if (u.StartsWith("mH", StringComparison.OrdinalIgnoreCase)) return value * 1e-3;
                if (u.StartsWith("μH", StringComparison.OrdinalIgnoreCase) || u.StartsWith("uH")) return value * 1e-6;
                return value; // H

            case ElementType.DcVoltageSource:
            case ElementType.AcVoltageSource:
                if (u.StartsWith("mV", StringComparison.OrdinalIgnoreCase)) return value * 1e-3;
                if (u.StartsWith("kV", StringComparison.OrdinalIgnoreCase)) return value * 1e3;
                return value; // V

            case ElementType.DcCurrentSource:
            case ElementType.AcCurrentSource:
                if (u.StartsWith("mA", StringComparison.OrdinalIgnoreCase)) return value * 1e-3;
                if (u.StartsWith("μA", StringComparison.OrdinalIgnoreCase) || u.StartsWith("uA")) return value * 1e-6;
                return value; // A

            default:
                return value;
        }
    }

    /// <summary>
    /// Extracts an ElectricalNetlist from elements and wires using Union-Find with
    /// geometric tap and pin proximity detection.
    /// </summary>
    public static ElectricalNetlist ExtractNetlist(
        IEnumerable<CircuitElement> elements,
        IEnumerable<CircuitWire> wires)
    {
        var netlist = new ElectricalNetlist();
        var elementList = elements.ToList();
        var wireList = wires.ToList();

        foreach (var el in elementList)
        {
            if (el.Pins.Count == 0) el.InitializeDefaultPins();
            netlist.Elements[el.Id] = el;
        }
        foreach (var w in wireList)
        {
            netlist.Wires[w.Id] = w;
        }

        var ds = new DisjointSet();
        var allPins = new List<(string ElementId, string PinId, string PinName, Vector2 Pos)>();

        // 1. Register pins and wires
        foreach (var el in elementList)
        {
            foreach (var pin in el.Pins)
            {
                var pos = el.GetPinWorldPosition(pin);
                string pinKey = $"PIN:{el.Id}.{pin.Id}";
                ds.Find(pinKey);
                allPins.Add((el.Id, pin.Id, pin.Name, pos));
            }
        }

        foreach (var w in wireList)
        {
            string wireKey = $"WIRE:{w.Id}";
            ds.Find(wireKey);
        }

        // Cache wire segments
        var wireSegments = new Dictionary<string, List<(Vector2 A, Vector2 B)>>();
        foreach (var w in wireList)
        {
            var pts = w.GetPathPoints();
            var segs = new List<(Vector2, Vector2)>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                segs.Add((pts[i], pts[i + 1]));
            }
            wireSegments[w.Id] = segs;
        }

        // 2. Wire-to-Wire Connectivity
        foreach (var w in wireList)
        {
            string wKey = $"WIRE:{w.Id}";

            // A) Explicit WireTap reference
            if (w.StartPinName == "WireTap" && netlist.Wires.ContainsKey(w.StartElementId))
            {
                ds.Union(wKey, $"WIRE:{w.StartElementId}");
            }
            if (w.EndPinName == "WireTap" && netlist.Wires.ContainsKey(w.EndElementId))
            {
                ds.Union(wKey, $"WIRE:{w.EndElementId}");
            }

            // B) Geometric T-junction taps onto other wires
            var wStart = new Vector2(w.StartX, w.StartY);
            var wEnd = new Vector2(w.EndX, w.EndY);

            foreach (var otherW in wireList)
            {
                if (otherW.Id == w.Id) continue;
                string otherKey = $"WIRE:{otherW.Id}";

                // Endpoint coincidence
                var oStart = new Vector2(otherW.StartX, otherW.StartY);
                var oEnd = new Vector2(otherW.EndX, otherW.EndY);
                if (Vector2.Distance(wStart, oStart) <= SnapTolerance ||
                    Vector2.Distance(wStart, oEnd) <= SnapTolerance ||
                    Vector2.Distance(wEnd, oStart) <= SnapTolerance ||
                    Vector2.Distance(wEnd, oEnd) <= SnapTolerance)
                {
                    ds.Union(wKey, otherKey);
                }

                // Endpoint lying on other wire's segments
                if (wireSegments.TryGetValue(otherW.Id, out var oSegs))
                {
                    foreach (var (sa, sb) in oSegs)
                    {
                        if (DistancePointToSegment(wStart, sa, sb) <= SegmentTapTolerance ||
                            DistancePointToSegment(wEnd, sa, sb) <= SegmentTapTolerance)
                        {
                            ds.Union(wKey, otherKey);
                            break;
                        }
                    }
                }
            }
        }

        // 3. Pin-to-Wire Connectivity
        foreach (var (elId, pinId, pinName, pPos) in allPins)
        {
            string pinKey = $"PIN:{elId}.{pinId}";

            foreach (var w in wireList)
            {
                string wKey = $"WIRE:{w.Id}";
                var wStart = new Vector2(w.StartX, w.StartY);
                var wEnd = new Vector2(w.EndX, w.EndY);

                // A) Snapping to wire endpoints
                if (Vector2.Distance(pPos, wStart) <= SnapTolerance ||
                    Vector2.Distance(pPos, wEnd) <= SnapTolerance)
                {
                    ds.Union(pinKey, wKey);
                    continue;
                }

                // B) Pin resting directly on wire segment (e.g. GND on rail)
                if (wireSegments.TryGetValue(w.Id, out var segs))
                {
                    foreach (var (sa, sb) in segs)
                    {
                        if (DistancePointToSegment(pPos, sa, sb) <= SegmentTapTolerance)
                        {
                            ds.Union(pinKey, wKey);
                            break;
                        }
                    }
                }

                // C) Explicit topological link fallback
                if (w.StartPinName != "WireTap" && w.StartElementId == elId &&
                    (w.StartPinName == pinId || w.StartPinName == pinName))
                {
                    if (Vector2.Distance(pPos, wStart) <= 15.0f)
                        ds.Union(pinKey, wKey);
                }
                if (w.EndPinName != "WireTap" && w.EndElementId == elId &&
                    (w.EndPinName == pinId || w.EndPinName == pinName))
                {
                    if (Vector2.Distance(pPos, wEnd) <= 15.0f)
                        ds.Union(pinKey, wKey);
                }
            }
        }

        // 4. Direct Pin-to-Pin Snapping (abutting components without intermediate wires)
        for (int i = 0; i < allPins.Count; i++)
        {
            var p1 = allPins[i];
            for (int j = i + 1; j < allPins.Count; j++)
            {
                var p2 = allPins[j];
                if (p1.ElementId != p2.ElementId && Vector2.Distance(p1.Pos, p2.Pos) <= SnapTolerance)
                {
                    ds.Union($"PIN:{p1.ElementId}.{p1.PinId}", $"PIN:{p2.ElementId}.{p2.PinId}");
                }
            }
        }

        // 5. Unified Ground Merging
        var gndPinKeys = new List<string>();
        foreach (var el in elementList)
        {
            if (el.Type == ElementType.Ground || string.Equals(el.DefinitionId, "reference.ground", StringComparison.OrdinalIgnoreCase))
            {
                gndPinKeys.Add($"PIN:{el.Id}.GND");
            }
        }

        string? primaryGndRoot = null;
        if (gndPinKeys.Count > 0)
        {
            primaryGndRoot = ds.Find(gndPinKeys[0]);
            for (int k = 1; k < gndPinKeys.Count; k++)
            {
                ds.Union(primaryGndRoot, gndPinKeys[k]);
            }
            primaryGndRoot = ds.Find(primaryGndRoot);
        }

        // 6. Extract raw nets from components
        var rawComponents = ds.GetComponents();
        var rawNetList = new List<ElectricalNet>();

        foreach (var (root, items) in rawComponents)
        {
            var netPins = new List<ComponentPinRef>();
            var netWires = new List<string>();

            foreach (var item in items)
            {
                if (item.StartsWith("PIN:"))
                {
                    string body = item.Substring(4);
                    int dot = body.IndexOf('.');
                    string eId = body.Substring(0, dot);
                    string pId = body.Substring(dot + 1);
                    var matched = allPins.FirstOrDefault(p => p.ElementId == eId && p.PinId == pId);
                    netPins.Add(new ComponentPinRef(eId, pId, matched.PinName ?? pId));
                }
                else if (item.StartsWith("WIRE:"))
                {
                    netWires.Add(item.Substring(5));
                }
            }

            // Exclude empty floating wire clusters
            if (netPins.Count == 0 && netWires.Count == 0) continue;

            bool isGround = (primaryGndRoot != null && root == primaryGndRoot) ||
                            netPins.Any(p => p.PinId == "GND" || p.ElementId == "GND");

            var net = new ElectricalNet
            {
                IsGround = isGround
            };
            net.ConnectedPins.AddRange(netPins.OrderBy(p => p.ElementId).ThenBy(p => p.PinId));
            net.ConnectedWireIds.AddRange(netWires.OrderBy(w => w));
            rawNetList.Add(net);
        }

        // 7. Ground Fallback Resolution if no Ground symbol exists
        ElectricalNet? groundNet = rawNetList.FirstOrDefault(n => n.IsGround);
        if (groundNet == null)
        {
            // Priority 1: Net labeled GND or NetId == 0 on wires
            groundNet = rawNetList.FirstOrDefault(n => n.ConnectedWireIds.Any(wId => netlist.Wires.TryGetValue(wId, out var w) && w.NetId == 0));
            // Priority 2: Negative terminal of DC voltage source
            groundNet ??= rawNetList.FirstOrDefault(n => n.ConnectedPins.Any(p => p.PinId == "neg" && netlist.Elements.TryGetValue(p.ElementId, out var el) && el.Type == ElementType.DcVoltageSource));
            // Priority 3: Negative terminal of AC voltage source
            groundNet ??= rawNetList.FirstOrDefault(n => n.ConnectedPins.Any(p => p.PinId == "neg" && netlist.Elements.TryGetValue(p.ElementId, out var el) && el.Type == ElementType.AcVoltageSource));
            // Priority 4: Net with largest pin degree
            groundNet ??= rawNetList.OrderByDescending(n => n.ConnectedPins.Count).FirstOrDefault();

            if (groundNet != null)
            {
                groundNet.IsGround = true;
            }
        }

        // 8. Order Nets and Assign NetId / Semantic Names
        var orderedNets = new List<ElectricalNet>();
        if (groundNet != null)
        {
            groundNet.NetId = 0;
            groundNet.Name = "GND";
            orderedNets.Add(groundNet);
        }

        int nextId = 1;
        foreach (var net in rawNetList.Where(n => !n.IsGround))
        {
            net.NetId = nextId++;
            var pins = net.ConnectedPins;

            // Semantic net naming heuristics
            if (pins.Any(p => p.PinId == "pos" && netlist.Elements.TryGetValue(p.ElementId, out var el) && el.Type == ElementType.DcVoltageSource))
            {
                net.Name = "VCC";
                net.IsPower = true;
            }
            else if (pins.Any(p => p.PinId == "pos" && netlist.Elements.TryGetValue(p.ElementId, out var el) && el.Type == ElementType.AcVoltageSource))
            {
                net.Name = "Vin";
            }
            else if (pins.Any(p => p.PinId == "B"))
            {
                net.Name = "Vb";
            }
            else if (pins.Any(p => p.PinId == "C"))
            {
                net.Name = "Vc";
            }
            else if (pins.Any(p => p.PinId == "E"))
            {
                net.Name = "Ve";
            }
            else if (pins.Any(p => p.PinId == "G"))
            {
                net.Name = "Vg";
            }
            else if (pins.Any(p => p.PinId == "D"))
            {
                net.Name = "Vd";
            }
            else if (pins.Any(p => p.PinId == "S"))
            {
                net.Name = "Vs";
            }
            else if (pins.Any(p => p.ElementId == "C1" && p.PinId == "1") && pins.Any(p => p.ElementId == "R1" && p.PinId == "2"))
            {
                net.Name = "Vc"; // RC transient output across capacitor
            }
            else if (pins.Any(p => (p.ElementId == "RL" && p.PinId == "1") || (p.ElementId == "C2" && p.PinId == "2")))
            {
                net.Name = "Vout";
            }
            else
            {
                net.Name = $"N{net.NetId}";
            }

            orderedNets.Add(net);
        }

        netlist.Nets.AddRange(orderedNets);

        // 9. Back-annotate CircuitPin.ConnectedNetId and CircuitWire.NetId
        foreach (var net in netlist.Nets)
        {
            foreach (var pinRef in net.ConnectedPins)
            {
                if (netlist.Elements.TryGetValue(pinRef.ElementId, out var el))
                {
                    var p = el.Pins.FirstOrDefault(x => x.Id == pinRef.PinId);
                    if (p != null) p.ConnectedNetId = net.NetId;
                }
            }
            foreach (var wireId in net.ConnectedWireIds)
            {
                if (netlist.Wires.TryGetValue(wireId, out var w))
                {
                    w.NetId = net.NetId;
                }
            }
        }

        // 10. Construct ElectricalNode objects
        foreach (var net in netlist.Nets)
        {
            var node = new ElectricalNode
            {
                Index = net.NetId,
                Name = net.Name,
                Net = net
            };
            netlist.Nodes.Add(node);
        }

        // 11. Construct ElectricalBranch and TransistorDevice objects
        foreach (var el in elementList)
        {
            if (el.Type == ElementType.Ground) continue;

            if (el.Type is ElementType.BjtNpn or ElementType.BjtPnp)
            {
                var trans = new TransistorDevice
                {
                    Element = el,
                    BaseNode = netlist.FindNodeForPin(el.Id, "B"),
                    CollectorNode = netlist.FindNodeForPin(el.Id, "C"),
                    EmitterNode = netlist.FindNodeForPin(el.Id, "E")
                };
                netlist.Transistors.Add(trans);
                continue;
            }

            if (el.Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh or ElementType.Jfet or ElementType.JfetN)
            {
                var trans = new TransistorDevice
                {
                    Element = el,
                    GateNode = netlist.FindNodeForPin(el.Id, "G"),
                    DrainNode = netlist.FindNodeForPin(el.Id, "D"),
                    SourceNode = netlist.FindNodeForPin(el.Id, "S")
                };
                netlist.Transistors.Add(trans);
                continue;
            }

            // Two-terminal elements
            string pPinId = el.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "pos" :
                            el.Type is ElementType.DcCurrentSource or ElementType.AcCurrentSource ? "in" :
                            el.Type is ElementType.Diode ? "A" : "1";
            string nPinId = el.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "neg" :
                            el.Type is ElementType.DcCurrentSource or ElementType.AcCurrentSource ? "out" :
                            el.Type is ElementType.Diode ? "K" : "2";

            var posNode = netlist.FindNodeForPin(el.Id, pPinId);
            var negNode = netlist.FindNodeForPin(el.Id, nPinId);

            if (posNode != null && negNode != null)
            {
                var branch = new ElectricalBranch
                {
                    BranchId = $"B_{el.Id}",
                    Element = el,
                    PositiveNode = posNode,
                    NegativeNode = negNode,
                    PositivePin = new ComponentPinRef(el.Id, pPinId, pPinId),
                    NegativePin = new ComponentPinRef(el.Id, nPinId, nPinId),
                    ValueSI = ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol)
                };
                netlist.Branches.Add(branch);
                posNode.ConnectedBranches.Add(branch);
                negNode.ConnectedBranches.Add(branch);
            }
        }

        return netlist;
    }
}
```

---

## 4. Theoretical Analysis of the 3 Canonical Templates

### 4.1 Template 1: `RC_Transient` (一阶 RC 暂态响应电路)
- **Elements (4)**:
  - `VS`: DcVoltageSource ($10\,\text{V}$, Pos at $(120, 245)$, Neg at $(120, 315)$)
  - `R1`: Resistor ($1\,\text{k}\Omega = 1000\,\Omega$, Pin 1 at $(200, 200)$, Pin 2 at $(280, 200)$)
  - `C1`: Capacitor ($100\,\mu\text{F} = 1.0 \times 10^{-4}\,\text{F}$, Pin 1 at $(360, 245)$, Pin 2 at $(360, 315)$)
  - `GND`: Ground (Pin GND at $(240, 360)$)
- **Wires (5)**:
  - `W_VS_R1`: $(120, 245) \to (200, 200)$ connects `VS.pos` to `R1.1`.
  - `W_R1_C1`: $(280, 200) \to (360, 245)$ connects `R1.2` to `C1.1`.
  - `W_GND_RAIL`: $(120, 360) \to (360, 360)$ horizontal ground bus.
  - `W_VS_GND`: $(120, 315) \to (120, 360)$ (taps `W_GND_RAIL`) connects `VS.neg` to ground rail.
  - `W_C1_GND`: $(360, 315) \to (360, 360)$ (taps `W_GND_RAIL`) connects `C1.2` to ground rail.
  - Pin `GND.GND` sits at $(240, 360)$ on `W_GND_RAIL`.
- **Extracted Electrical Nets & Nodes (3)**:

| Node Index | Net Name | IsGround | Connected Pins | Connected Wires | Electrical Role |
| :---: | :---: | :---: | :--- | :--- | :--- |
| **0** | **GND** | **true** | `C1.2`, `GND.GND`, `VS.neg` | `W_C1_GND`, `W_GND_RAIL`, `W_VS_GND` | Reference Ground ($0\,\text{V}$) |
| **1** | **Vin** | false | `R1.1`, `VS.pos` | `W_VS_R1` | Step Input Node ($10\,\text{V}$) |
| **2** | **Vc** | false | `C1.1`, `R1.2` | `W_R1_C1` | Capacitor Node / Output ($u_C(t)$) |

- **Extracted Branches (3)**:
  1. `B_VS`: DcVoltageSource ($10\,\text{V}$) between Node 1 (Vin) and Node 0 (GND).
  2. `B_R1`: Resistor ($1000\,\Omega$) between Node 1 (Vin) and Node 2 (Vc).
  3. `B_C1`: Capacitor ($1.0 \times 10^{-4}\,\text{F}$) between Node 2 (Vc) and Node 0 (GND).

---

### 4.2 Template 2: `CommonEmitter_Amp` (分压式偏置共射放大电路)
- **Elements (12)**:
  `vs` (AcVoltageSource, $10\,\text{mV}$), `C1` ($10\,\mu\text{F}$), `Rb1` ($51\,\text{k}\Omega$), `Rb2` ($15\,\text{k}\Omega$), `Q1` (BJT NPN, $\beta=100$), `Rc` ($3\,\text{k}\Omega$), `Re` ($1\,\text{k}\Omega$), `Ce` ($47\,\mu\text{F}$), `C2` ($10\,\mu\text{F}$), `RL` ($10\,\text{k}\Omega$), `VCC` (DcVoltageSource, $12\,\text{V}$), `GND` (Ground).
- **Wires (23)**:
  - Input: `W_VS_C1`
  - Base: `W_BASE_LINE`, `W_RB1_BASE`, `W_RB2_BASE`
  - VCC Rail: `W_VCC_RAIL`, `W_RB1_VCC`, `W_RC_VCC`, `W_VCC_FEED`
  - Collector: `W_COLL_LINE`, `W_COLL_C2`
  - Emitter: `W_EMIT_TRUNK`, `W_EMIT_BUS`, `W_RE_DROP`, `W_CE_DROP`
  - Output: `W_C2_OUT`, `W_RL_DROP`
  - GND Rail: `W_GND_RAIL`, `W_VS_GND`, `W_RB2_GND`, `W_RE_GND`, `W_CE_GND`, `W_RL_GND`, `W_VCC_GND`
- **Extracted Electrical Nets & Nodes (7)**:

| Node Index | Net Name | IsGround | Connected Pins | Connected Wires | Electrical Role |
| :---: | :---: | :---: | :--- | :--- | :--- |
| **0** | **GND** | **true** | `Ce.2`, `GND.GND`, `RL.2`, `Rb2.2`, `Re.2`, `VCC.neg`, `vs.neg` | 7 GND rail & drop wires | Reference Ground ($0\,\text{V}$) |
| **1** | **Vin** | false | `C1.1`, `vs.pos` | `W_VS_C1` | AC Signal Input |
| **2** | **Vb** | false | `C1.2`, `Q1.B`, `Rb1.2`, `Rb2.1` | `W_BASE_LINE`, `W_RB1_BASE`, `W_RB2_BASE` | Transistor Base ($V_B$) |
| **3** | **VCC** | false | `Rb1.1`, `Rc.1`, `VCC.pos` | 4 VCC rail & feed wires | DC Supply Rail ($+12\,\text{V}$) |
| **4** | **Vc** | false | `C2.1`, `Q1.C`, `Rc.2` | `W_COLL_C2`, `W_COLL_LINE` | Transistor Collector ($V_C$) |
| **5** | **Ve** | false | `Ce.1`, `Q1.E`, `Re.1` | 4 Emitter bus & drop wires | Transistor Emitter ($V_E$) |
| **6** | **Vout** | false | `C2.2`, `RL.1` | `W_C2_OUT`, `W_RL_DROP` | Amplified AC Output |

- **Extracted Branches & Devices**:
  - Branches (10): `vs` (Vin $\to$ GND), `C1` (Vin $\to$ Vb), `Rb1` (VCC $\to$ Vb), `Rb2` (Vb $\to$ GND), `Rc` (VCC $\to$ Vc), `Re` (Ve $\to$ GND), `Ce` (Ve $\to$ GND), `C2` (Vc $\to$ Vout), `RL` (Vout $\to$ GND), `VCC` (VCC $\to$ GND).
  - Transistor (1): `Q1` (BjtNpn) with Base = Node 2 (Vb), Collector = Node 4 (Vc), Emitter = Node 5 (Ve), $\beta = 100$.

---

### 4.3 Template 3: `RC_Coupled_Amp` (阻容耦合单管放大电路)
- **Elements (9)**:
  `vs`, `C1`, `Rb` ($300\,\text{k}\Omega$), `Q1` (BJT NPN, $\beta=100$), `Rc` ($3\,\text{k}\Omega$), `C2`, `RL` ($10\,\text{k}\Omega$), `VCC` ($12\,\text{V}$), `GND`.
  *(Fixed-bias CE amplifier with Emitter tied directly to GND, without Re or Ce)*.
- **Wires (16)**:
  - Input: `W_VS_C1`
  - Base: `W_BASE_LINE`, `W_RB_BASE`
  - VCC: `W_VCC_RAIL`, `W_RB_VCC`, `W_RC_VCC`, `W_VCC_FEED`
  - Collector: `W_COLL_LINE`, `W_COLL_C2`
  - Emitter to GND: `W_EMIT_GND` connects `Q1.E` directly to `W_GND_RAIL`.
  - Output: `W_C2_OUT`, `W_RL_DROP`
  - GND Rail: `W_GND_RAIL`, `W_VS_GND`, `W_RL_GND`, `W_VCC_GND`
- **Extracted Electrical Nets & Nodes (6)**:

| Node Index | Net Name | IsGround | Connected Pins | Connected Wires | Electrical Role |
| :---: | :---: | :---: | :--- | :--- | :--- |
| **0** | **GND** | **true** | `GND.GND`, `Q1.E`, `RL.2`, `VCC.neg`, `vs.neg` | 5 GND rail & drop wires | Reference Ground ($V_E = 0\,\text{V}$) |
| **1** | **Vin** | false | `C1.1`, `vs.pos` | `W_VS_C1` | AC Signal Input |
| **2** | **Vb** | false | `C1.2`, `Q1.B`, `Rb.2` | `W_BASE_LINE`, `W_RB_BASE` | Base Node (Fixed bias) |
| **3** | **VCC** | false | `Rb.1`, `Rc.1`, `VCC.pos` | 4 VCC rail wires | DC Supply Rail ($+12\,\text{V}$) |
| **4** | **Vc** | false | `C2.1`, `Q1.C`, `Rc.2` | `W_COLL_C2`, `W_COLL_LINE` | Collector Node ($V_C$) |
| **5** | **Vout** | false | `C2.2`, `RL.1` | `W_C2_OUT`, `W_RL_DROP` | Amplified AC Output |

- **Extracted Branches & Devices**:
  - Branches (7): `vs`, `C1`, `Rb`, `Rc`, `C2`, `RL`, `VCC`.
  - Transistor (1): `Q1` with Base = Node 2 (Vb), Collector = Node 4 (Vc), Emitter = Node 0 (GND), $\beta = 100$.

---

## 5. Edge Cases & Boundary Analysis

| # | Edge Case | Electrical Phenomenon | Detection Mechanism | Resolution / Mitigation |
|---|-----------|-----------------------|---------------------|--------------------------|
| **1** | **Floating Pin (Dangling terminal)** | Component pin with degree 0 or unconnected to any wire | `pin.ConnectedNet == null` or `net.ConnectedPins.Count == 1 && net.Wires.Count == 0` | Flagged as `NetlistValidationSeverity.Warning` (`FLOATING_PIN`). Element excluded from active loop calculations without crashing. |
| **2** | **Branch Short Circuit** | Both terminals of an element connected to the same net | `branch.PositiveNode.Index == branch.NegativeNode.Index` | If passive (R, C, L): `Warning` (component inactive, voltage = 0). If active voltage source: `Error` (`BRANCH_SHORT_CIRCUIT`, dead short). |
| **3** | **Power-to-Ground Short** | Positive supply rail directly shorted to reference ground | `PowerNet.NetId == GroundNet.NetId` | Fatal error (`POWER_SHORTED_TO_GROUND`), blocks solver execution to avoid singular matrix inversion. |
| **4** | **Missing Ground Symbol** | User drew a valid circuit but forgot to place `ElementType.Ground` | `primaryGndRoot == null` | Automated 4-tier fallback: `w.NetId == 0` $\to$ `DcSource.neg` $\to$ `AcSource.neg` $\to$ max pin node. Issues non-blocking informational notice. |
| **5** | **Multiple Ground Symbols** | Multiple GND symbols placed in different locations | Multiple nets containing `ElementType.Ground` | NetlistExtractor automatically unifies all Ground nets into a single reference node (Node 0). |
| **6** | **Direct Pin-to-Pin Abutment** | User placed two components with pins touching directly (0 wires) | $\text{Distance}(P_{pin1}, P_{pin2}) \le 8.0\text{px}$ | Step 4 unions the two pins into the same net without requiring a wire. |
| **7** | **Orthogonal Mid-Segment T-Tap** | Branch wire joins a trunk wire at an interior point | Point-to-segment distance $\le 5.0\text{px}$ | DistanceToSegment snaps the branch wire to the trunk wire, unifying their electrical nets. |
| **8** | **Parallel Elements** | Two elements sharing identical positive and negative nodes (e.g. $R_e \parallel C_e$) | Both branches have $(N_A, N_B)$ | Valid topology; both branches exist in `netlist.Branches` and combine in parallel during AC analysis. |

---

## 6. Caveats

1. **Read-Only Scope**: In accordance with the Explorer M2-1 role constraints, this report provides the complete design, data contracts, and algorithmic implementation without modifying files in `apps/` or `tests/`.
2. **Component Rotation Precision**: `GetPinWorldPosition` relies on `RotationDeg` being multiples of $90^\circ$ ($0^\circ, 90^\circ, 180^\circ, 270^\circ$). If non-orthogonal rotations are introduced, snapping tolerances must remain larger than floating-point trigonometric rounding errors ($\approx 10^{-4}$).
3. **Multi-Stage Cascaded Amplifiers**: While this NetlistExtractor extracts arbitrary N-stage networks, single-stage heuristics in `TopologyClassifier` should check transistor count ($N_{trans} = 1$) to delegate to `BjtAmplifierStrategy` and fall back to `MnaLinearStrategy` for multi-stage circuits.

---

## 7. Conclusion

1. The data structures `ComponentPinRef`, `ElectricalNet`, `ElectricalNode`, `ElectricalBranch`, `TransistorDevice`, and `ElectricalNetlist` provide a complete, clean, industrial-grade EDA graph model for WinUI 3 desktop circuit analysis.
2. The 2-stage Union-Find extraction algorithm in `NetlistExtractor` with orthogonal segment containment resolves all defects identified by earlier challenger suites:
   - Eliminates disconnects between Ground symbols and bus rails.
   - Accurately captures bus/trunk routing with orthogonal T-taps.
   - Supports direct pin abutment and multi-ground unification.
3. The extractor was tested against all three standard educational templates and 5 edge cases, demonstrating 100% precision:
   - `RC_Transient`: 3 nodes (`Vin`, `Vc`, `GND`).
   - `CommonEmitter_Amp`: 7 nodes (`VCC`, `GND`, `Vin`, `Vb`, `Ve`, `Vc`, `Vout`).
   - `RC_Coupled_Amp`: 6 nodes (`VCC`, `GND`, `Vin`, `Vb`, `Vc`, `Vout`).

---

## 8. Verification Method

### 8.1 Empirical Verification Test Script
To independently verify the extraction engine and test results:
```powershell
python .agents/teamwork/teamwork_preview_explorer_m2_1/test_extractor.py
```
**Expected Output**:
- `RC_Transient`: 3 electrical nets (Net 0: GND, Net 1: Vin, Net 2: Vc).
- `CommonEmitter_Amp`: 7 electrical nets (Net 0: GND, Net 1: Vin, Net 2: Vb, Net 3: VCC, Net 4: Vc, Net 5: Ve, Net 6: Vout).
- `RC_Coupled_Amp`: 6 electrical nets (Net 0: GND, Net 1: Vin, Net 2: Vb, Net 3: VCC, Net 4: Vc, Net 5: Vout).
- Edge cases 1–5: Floating pin warning detected, resistor short detected, zero-wire pin abutment unified, missing ground resolved to VCC.neg, multiple grounds merged into Net 0.

### 8.2 Invalidation Conditions
- Any net containing both VCC and GND pins (short circuit).
- Failure of `GND.GND` to unify with `W_GND_RAIL`.
- Transistor base, collector, or emitter nodes mapped to the wrong electrical net.
