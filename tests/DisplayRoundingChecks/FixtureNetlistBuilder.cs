// ============================================================================
// File: tests/DisplayRoundingChecks/FixtureNetlistBuilder.cs
// Description: Minimal in-memory netlist builder for the display-rounding checks.
//              Binds component pins to nets and materializes the ElectricalNetlist
//              shape (Nets, Nodes, two-terminal Branches) exactly like
//              tests/NativeBenchmark30/Problems.cs does for its textbook fixtures,
//              so the production TopologyClassifier / strategies / MNA analyzer
//              consume a normally-extracted netlist.
//
//              Net 0 is the reference ground by convention.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace DisplayRoundingChecks;

internal sealed class FixtureNetlistBuilder
{
    private readonly ElectricalNetlist _netlist = new();
    private readonly List<(string ElementId, string PinId, int NetId)> _pins = new();
    private readonly List<CircuitElement> _elements = new();
    private readonly Dictionary<int, ElectricalNet> _nets = new();

    /// <summary>Adds an element and initializes the pins for its type.</summary>
    public FixtureNetlistBuilder Element(string id, ElementType type, double value, string unit = "", double frequencyHz = 1000.0)
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = id,
            Type = type,
            DisplayNumber = value,
            UnitSymbol = unit,
            FrequencyHz = frequencyHz
        };
        element.InitializeDefaultPins();
        _netlist.Elements[id] = element;
        _elements.Add(element);
        return this;
    }

    /// <summary>Binds one component pin to a net id (0 = ground).</summary>
    public FixtureNetlistBuilder Pin(string elementId, string pinId, int netId)
    {
        _pins.Add((elementId, pinId, netId));
        return this;
    }

    public ElectricalNetlist Build()
    {
        // 1. Bind pins to nets (same as NetlistExtractor's net construction).
        foreach ((string elementId, string pinId, int netId) in _pins)
        {
            ElectricalNet net = EnsureNet(netId);
            CircuitElement element = _netlist.Elements[elementId];
            CircuitPin pin = element.Pins.First(p => p.Id == pinId);
            net.ConnectedPins.Add(new ComponentPinRef(elementId, pinId, pin.Name));
            pin.ConnectedNetId = netId;
        }

        // 2. Nets / nodes ordered by NetId (0 = reference ground).
        foreach (ElectricalNet net in _nets.Values.OrderBy(n => n.NetId)) _netlist.Nets.Add(net);
        foreach (ElectricalNet net in _nets.Values.OrderBy(n => n.NetId))
        {
            _netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });
        }

        // 3. Two-terminal branches with SI values (multi-terminal devices are not
        //    needed by the RC/RL/RLC/MNA/diode fixtures; BJT/MOS fixtures are loaded
        //    from examples/circuit-models instead).
        foreach (CircuitElement element in _elements)
        {
            if (element.Type == ElementType.Ground) continue;
            (string positivePin, string negativePin) = TwoTerminalPins(element.Type);
            if (positivePin.Length == 0) continue;
            ElectricalNode? positiveNode = _netlist.FindNodeForPin(element.Id, positivePin);
            ElectricalNode? negativeNode = _netlist.FindNodeForPin(element.Id, negativePin);
            if (positiveNode == null || negativeNode == null || positiveNode == negativeNode) continue;

            double valueSi;
            try
            {
                valueSi = NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
            }
            catch (ArgumentException)
            {
                valueSi = element.DisplayNumber; // e.g. a teaching diode stored without a unit symbol
            }

            var branch = new ElectricalBranch
            {
                BranchId = "B_" + element.Id,
                Element = element,
                PositiveNode = positiveNode,
                NegativeNode = negativeNode,
                PositivePin = new ComponentPinRef(element.Id, positivePin, positivePin),
                NegativePin = new ComponentPinRef(element.Id, negativePin, negativePin),
                ValueSI = valueSi
            };
            _netlist.Branches.Add(branch);
            positiveNode.ConnectedBranches.Add(branch);
            negativeNode.ConnectedBranches.Add(branch);
        }

        return _netlist;
    }

    private static (string PositivePin, string NegativePin) TwoTerminalPins(ElementType type) => type switch
    {
        ElementType.Resistor or ElementType.Capacitor or ElementType.Inductor => ("1", "2"),
        ElementType.CapacitorPolarized => ("+", "-"),
        ElementType.DcVoltageSource or ElementType.AcVoltageSource => ("pos", "neg"),
        ElementType.DcCurrentSource or ElementType.AcCurrentSource => ("in", "out"),
        ElementType.Diode => ("A", "K"),
        _ => (string.Empty, string.Empty)
    };

    private ElectricalNet EnsureNet(int netId)
    {
        if (_nets.TryGetValue(netId, out ElectricalNet? existing)) return existing;
        var net = new ElectricalNet
        {
            NetId = netId,
            Name = netId == 0 ? "GND" : "N" + netId,
            IsGround = netId == 0
        };
        _nets[netId] = net;
        return net;
    }
}
