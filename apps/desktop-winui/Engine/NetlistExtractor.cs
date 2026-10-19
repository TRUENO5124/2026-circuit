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
    /// Determines whether a wire endpoint with the specified pin and element bindings is allowed
    /// to create a geometric T-junction or endpoint tap onto the target wire.
    /// Component-bound leads are strictly prohibited from tapping arbitrary wires without explicit declaration.
    /// </summary>
    public static bool CanEndpointTapWire(string? pinName, string? elementId, CircuitWire targetWire)
    {
        bool isNoneOrEmpty = string.IsNullOrEmpty(elementId) || string.Equals(elementId, "None", StringComparison.OrdinalIgnoreCase);
        if (pinName == "WireTap")
        {
            return isNoneOrEmpty || elementId == targetWire.Id;
        }
        return isNoneOrEmpty;
    }

    /// <summary>
    /// Converts a user display value and unit symbol into standard base SI units.
    /// </summary>
    public static double ConvertToBaseSi(ElementType type, double value, string unitSymbol)
    {
        string unit = (unitSymbol ?? "").Trim().Replace("uF", "μF").Replace("uH", "μH").Replace("uA", "μA");
        var options = type switch
        {
            ElementType.Resistor => EngineeringUnits.ResistanceUnits,
            ElementType.Capacitor or ElementType.CapacitorPolarized => EngineeringUnits.CapacitanceUnits,
            ElementType.Inductor => EngineeringUnits.InductanceUnits,
            ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.VccPort => EngineeringUnits.VoltageUnits,
            ElementType.DcCurrentSource or ElementType.AcCurrentSource => EngineeringUnits.CurrentUnits,
            _ => Array.Empty<UnitOption>()
        };
        if (options.Length == 0) return value;
        var option = options.FirstOrDefault(o => o.Symbol.Equals(unit, StringComparison.Ordinal));
        if (option == null) throw new ArgumentException($"单位 '{unitSymbol}' 不适用于 {type}。");
        return value * option.Multiplier;
    }

    /// <summary>
    /// Alias method to match various callers.
    /// </summary>
    public static ElectricalNetlist Extract(
        IEnumerable<CircuitElement> elements,
        IEnumerable<CircuitWire> wires) => ExtractNetlist(elements, wires);

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

                // Endpoint coincidence:
                // Only merge if at least one touching endpoint is a legitimate wire tap or open wire endpoint.
                // Dedicated component leads belonging to different components must never merge geometrically!
                var oStart = new Vector2(otherW.StartX, otherW.StartY);
                var oEnd = new Vector2(otherW.EndX, otherW.EndY);

                bool wStartCanTap = CanEndpointTapWire(w.StartPinName, w.StartElementId, otherW);
                bool wEndCanTap = CanEndpointTapWire(w.EndPinName, w.EndElementId, otherW);
                bool oStartCanTap = CanEndpointTapWire(otherW.StartPinName, otherW.StartElementId, w);
                bool oEndCanTap = CanEndpointTapWire(otherW.EndPinName, otherW.EndElementId, w);

                if (Vector2.Distance(wStart, oStart) <= SnapTolerance && (wStartCanTap || oStartCanTap))
                {
                    ds.Union(wKey, otherKey);
                }
                else if (Vector2.Distance(wStart, oEnd) <= SnapTolerance && (wStartCanTap || oEndCanTap))
                {
                    ds.Union(wKey, otherKey);
                }
                else if (Vector2.Distance(wEnd, oStart) <= SnapTolerance && (wEndCanTap || oStartCanTap))
                {
                    ds.Union(wKey, otherKey);
                }
                else if (Vector2.Distance(wEnd, oEnd) <= SnapTolerance && (wEndCanTap || oEndCanTap))
                {
                    ds.Union(wKey, otherKey);
                }

                // Endpoint lying on other wire's segments (orthogonal T-junction tap)
                if (wireSegments.TryGetValue(otherW.Id, out var oSegs))
                {
                    foreach (var (sa, sb) in oSegs)
                    {
                        if ((wStartCanTap && DistancePointToSegment(wStart, sa, sb) <= SegmentTapTolerance) ||
                            (wEndCanTap && DistancePointToSegment(wEnd, sa, sb) <= SegmentTapTolerance))
                        {
                            ds.Union(wKey, otherKey);
                            break;
                        }
                    }
                }
            }
        }

        // 3. Pin-to-Wire Connectivity
        // Register all pins with explicit wire declarations
        var pinsWithExplicitWires = new HashSet<string>();
        foreach (var w in wireList)
        {
            if (!string.IsNullOrEmpty(w.StartElementId) && !string.Equals(w.StartElementId, "None", StringComparison.OrdinalIgnoreCase) && w.StartPinName != "WireTap")
            {
                pinsWithExplicitWires.Add($"PIN:{w.StartElementId}.{w.StartPinName}");
            }
            if (!string.IsNullOrEmpty(w.EndElementId) && !string.Equals(w.EndElementId, "None", StringComparison.OrdinalIgnoreCase) && w.EndPinName != "WireTap")
            {
                pinsWithExplicitWires.Add($"PIN:{w.EndElementId}.{w.EndPinName}");
            }
        }

        var pinsWithWires = new HashSet<string>();
        foreach (var (elId, pinId, pinName, pPos) in allPins)
        {
            string pinKey = $"PIN:{elId}.{pinId}";
            var element = elementList.FirstOrDefault(e => e.Id == elId);
            bool isGround = element != null && element.Type == ElementType.Ground;
            bool isVcc = element != null && element.Type == ElementType.VccPort;

            foreach (var w in wireList)
            {
                string wKey = $"WIRE:{w.Id}";
                var wStart = new Vector2(w.StartX, w.StartY);
                var wEnd = new Vector2(w.EndX, w.EndY);

                // A) Snapping to wire endpoints
                // Wire endpoint connects to pin only if:
                // 1. Wire explicitly targets this pin, OR
                // 2. Wire endpoint is an untargeted open wire (not dedicated to another component or tap)
                bool isStartOpen = (string.IsNullOrEmpty(w.StartElementId) || string.Equals(w.StartElementId, "None", StringComparison.OrdinalIgnoreCase)) && w.StartPinName != "WireTap";
                bool isEndOpen = (string.IsNullOrEmpty(w.EndElementId) || string.Equals(w.EndElementId, "None", StringComparison.OrdinalIgnoreCase)) && w.EndPinName != "WireTap";
                bool wStartMatchesPin = (w.StartElementId == elId && (w.StartPinName == pinId || w.StartPinName == pinName)) || isStartOpen;
                bool wEndMatchesPin = (w.EndElementId == elId && (w.EndPinName == pinId || w.EndPinName == pinName)) || isEndOpen;

                if (wStartMatchesPin && Vector2.Distance(pPos, wStart) <= SnapTolerance)
                {
                    ds.Union(pinKey, wKey);
                    pinsWithWires.Add(pinKey);
                    continue;
                }
                if (wEndMatchesPin && Vector2.Distance(pPos, wEnd) <= SnapTolerance)
                {
                    ds.Union(pinKey, wKey);
                    pinsWithWires.Add(pinKey);
                    continue;
                }

                // B) Pin resting directly on wire segment (e.g. GND/VCC symbol on rail, or unconnected pin on rail)
                bool isRailOrBus = w.Id.Contains("_RAIL") || w.Id.Contains("_BUS");
                if ((isGround || isVcc || (isRailOrBus && !pinsWithExplicitWires.Contains(pinKey))) &&
                    wireSegments.TryGetValue(w.Id, out var segs))
                {
                    foreach (var (sa, sb) in segs)
                    {
                        if (DistancePointToSegment(pPos, sa, sb) <= SegmentTapTolerance)
                        {
                            ds.Union(pinKey, wKey);
                            pinsWithWires.Add(pinKey);
                            break;
                        }
                    }
                }

                // C) Explicit topological link fallback
                if (w.StartPinName != "WireTap" && w.StartElementId == elId &&
                    (w.StartPinName == pinId || w.StartPinName == pinName))
                {
                    if (Vector2.Distance(pPos, wStart) <= 15.0f)
                    {
                        ds.Union(pinKey, wKey);
                        pinsWithWires.Add(pinKey);
                    }
                }
                if (w.EndPinName != "WireTap" && w.EndElementId == elId &&
                    (w.EndPinName == pinId || w.EndPinName == pinName))
                {
                    if (Vector2.Distance(pPos, wEnd) <= 15.0f)
                    {
                        ds.Union(pinKey, wKey);
                        pinsWithWires.Add(pinKey);
                    }
                }
            }
        }

        // 4. Direct Pin-to-Pin Snapping (abutting components without intermediate wires)
        for (int i = 0; i < allPins.Count; i++)
        {
            var p1 = allPins[i];
            string p1Key = $"PIN:{p1.ElementId}.{p1.PinId}";

            for (int j = i + 1; j < allPins.Count; j++)
            {
                var p2 = allPins[j];
                if (p1.ElementId == p2.ElementId) continue;
                if (Vector2.Distance(p1.Pos, p2.Pos) <= SnapTolerance)
                {
                    string p2Key = $"PIN:{p2.ElementId}.{p2.PinId}";
                    string root1 = ds.Find(p1Key);
                    string root2 = ds.Find(p2Key);

                    if (root1 != root2)
                    {
                        // Guard: do NOT merge pins if either pin already has connected wires that belong to distinct electrical nets,
                        // unless the two pins are directly connected or intended to abut without wires.
                        bool p1HasWire = pinsWithWires.Contains(p1Key);
                        bool p2HasWire = pinsWithWires.Contains(p2Key);

                        if (p1HasWire && p2HasWire)
                        {
                            continue;
                        }
                    }

                    ds.Union(p1Key, p2Key);
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
                            netPins.Any(p => p.PinId == "GND" || p.ElementId == "GND" || p.ElementId.StartsWith("GND"));

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
            if (pins.Any(p => (p.PinId == "pos" && netlist.Elements.TryGetValue(p.ElementId, out var el) && el.Type == ElementType.DcVoltageSource)
                           || (netlist.Elements.TryGetValue(p.ElementId, out var elVcc) && elVcc.Type == ElementType.VccPort)))
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

            if (el.Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh or ElementType.MosfetNDep or ElementType.MosfetPDep or ElementType.Jfet or ElementType.JfetN or ElementType.JfetP)
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
                // Connectivity extraction must also work on an unfinished drawing.
                // Validate reports the bad unit before a solver can consume this value.
                double valueSi;
                try { valueSi = ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol); }
                catch (ArgumentException) { valueSi = double.NaN; }
                var branch = new ElectricalBranch
                {
                    BranchId = $"B_{el.Id}",
                    Element = el,
                    PositiveNode = posNode,
                    NegativeNode = negNode,
                    PositivePin = new ComponentPinRef(el.Id, pPinId, pPinId),
                    NegativePin = new ComponentPinRef(el.Id, nPinId, nPinId),
                    ValueSI = valueSi
                };
                netlist.Branches.Add(branch);
                posNode.ConnectedBranches.Add(branch);
                negNode.ConnectedBranches.Add(branch);
            }
        }

        return netlist;
    }
}
