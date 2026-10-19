// ============================================================================
// File: tests/NonlinearCircuitChecks/TestNetlistBuilder.cs
// Description: Minimal in-memory netlist fixture builder for the nonlinear checks.
//              Builds ElectricalNetlist.Elements + Nets + Nodes from pin/net
//              declarations (same pattern as tests/EngineV2Checks.NetBuilder),
//              including BJT/MOSFET transistor pins.
//
//              Net 0 is ground by convention. Transistor nodes are resolved by
//              the engine strategies through ElectricalNetlist.FindNetForPin, so
//              this builder only needs the pin -> net mapping plus the Nodes.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal sealed class TestNetlistBuilder
{
    private readonly ElectricalNetlist _netlist = new();
    private readonly List<(string ElementId, string PinId, int NetId)> _pins = new();

    /// <summary>Adds a two-terminal or multi-terminal element (pins are initialized from the type).</summary>
    public TestNetlistBuilder Element(
        string id,
        ElementType type,
        double value,
        string unit = "",
        double frequencyHz = 1000.0,
        SemiconductorMaterial material = SemiconductorMaterial.Silicon,
        double mosfetK = 1e-3,
        double mosfetLambda = 0.0)
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = id,
            Type = type,
            DisplayNumber = value,
            UnitSymbol = unit,
            FrequencyHz = frequencyHz,
            Material = material,
            MosfetK = mosfetK,
            MosfetLambda = mosfetLambda
        };
        element.InitializeDefaultPins();
        _netlist.Elements[id] = element;
        return this;
    }

    /// <summary>Binds a component pin to a net id (0 = ground).</summary>
    public TestNetlistBuilder Pin(string elementId, string pinId, int netId)
    {
        _pins.Add((elementId, pinId, netId));
        return this;
    }

    /// <summary>Optional explicit net name / ground / power flag.</summary>
    public TestNetlistBuilder Net(int netId, string name, bool isGround = false, bool isPower = false)
    {
        ElectricalNet net = EnsureNet(netId);
        net.Name = name;
        net.IsGround |= isGround;
        net.IsPower |= isPower;
        return this;
    }

    public ElectricalNetlist Build()
    {
        foreach ((string elementId, string pinId, int netId) in _pins)
            EnsureNet(netId).ConnectedPins.Add(new ComponentPinRef(elementId, pinId, pinId));

        foreach (ElectricalNet net in _netlist.Nets.OrderBy(n => n.NetId))
        {
            if (string.IsNullOrWhiteSpace(net.Name))
                net.Name = net.NetId == 0 ? "GND" : "N" + net.NetId;
            _netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });
        }
        return _netlist;
    }

    private ElectricalNet EnsureNet(int netId)
    {
        ElectricalNet? existing = _netlist.Nets.FirstOrDefault(n => n.NetId == netId);
        if (existing != null) return existing;
        var net = new ElectricalNet
        {
            NetId = netId,
            Name = netId == 0 ? "GND" : "N" + netId,
            IsGround = netId == 0
        };
        _netlist.Nets.Add(net);
        return net;
    }
}
