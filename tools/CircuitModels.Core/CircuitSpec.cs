// ============================================================================
// File: tools/CircuitModels.Core/CircuitSpec.cs
// Description: Declarative circuit-project specification shared by the four
//              CircuitModels.* generator projects (contract
//              projects/nonlinear-engine-and-circuit-models-design-20261018.md §8.2).
//
//              A CircuitSpec describes a teaching circuit in schematic terms:
//                * elements with type / value / grid position / rotation;
//                * named electrical nets with their component pins, optionally
//                  routed along an explicit VCC/GND rail or bus;
//                * the expected classification and solve outcome used by the
//                  read-only verification pass.
//
//              The builder (SchematicBuilder.cs) turns the specification into a
//              native CircuitProjectData (Elements + Wires) and validates the
//              declared net partition against NetlistExtractor.
// ============================================================================

using System.Collections.Generic;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels;

/// <summary>How a net is routed: free orthogonal routing, or an explicit rail/bus.</summary>
public enum NetRail
{
    /// <summary>Free routing: 2 pins get one L-shaped wire, 3+ pins get an automatic trunk.</summary>
    None,
    /// <summary>Horizontal trunk at <see cref="NetSpec.RailCoordinate"/> (Y).</summary>
    HorizontalTop,
    /// <summary>Horizontal trunk at <see cref="NetSpec.RailCoordinate"/> (Y).</summary>
    HorizontalBottom,
    /// <summary>Vertical trunk at <see cref="NetSpec.RailCoordinate"/> (X).</summary>
    VerticalLeft,
    /// <summary>Vertical trunk at <see cref="NetSpec.RailCoordinate"/> (X).</summary>
    VerticalRight
}

/// <summary>
/// Declarative description of one circuit element. Pin geometry comes from
/// <see cref="CircuitElement.InitializeDefaultPins"/> (the exact same source used
/// by the WinUI canvas), so the builder never duplicates symbol geometry.
/// </summary>
public sealed class ElementSpec
{
    /// <summary>Element id without '.' or ':' (e.g. "V1", "Q1").</summary>
    public string Id { get; init; } = string.Empty;

    public ElementType Type { get; init; }

    /// <summary>Display name (Chinese teaching label shown on the canvas).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Web definition id (e.g. "passive.resistor"); defaults are type-based.</summary>
    public string DefinitionId { get; init; } = string.Empty;

    /// <summary>Symbol category (e.g. 无源器件); defaults are type-based.</summary>
    public string Category { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>DisplayNumber (resistance in Ω, capacitance in F, VCC in V, β, Vth...).</summary>
    public double Number { get; init; }

    public string Unit { get; init; } = string.Empty;

    public float X { get; init; }

    public float Y { get; init; }

    public int RotationDeg { get; init; }

    public bool MirrorX { get; init; }

    public float Width { get; init; } = 80;

    public float Height { get; init; } = 50;

    public SemiconductorMaterial Material { get; init; } = SemiconductorMaterial.Silicon;

    /// <summary>AC excitation frequency (Hz); ignored for non-AC elements.</summary>
    public double FrequencyHz { get; init; } = 1000;

    /// <summary>MOSFET square-law parameter K in A/V² (default 1 mA/V²).</summary>
    public double MosfetK { get; init; } = 1e-3;

    /// <summary>MOSFET channel-length modulation λ in 1/V (0 = ignored).</summary>
    public double MosfetLambda { get; init; }
}

/// <summary>
/// Declarative description of one electrical net. Pins are written as
/// "ElementId.PinId" (e.g. "V1.pos", "Q1.B", "GND.GND"). The net named "GND"
/// must contain the ground element pin and becomes net id 0 in the project file.
/// </summary>
public sealed class NetSpec
{
    public string Name { get; init; } = string.Empty;

    public string[] Pins { get; init; } = [];

    public NetRail Rail { get; init; } = NetRail.None;

    /// <summary>Rail coordinate: Y for horizontal rails, X for vertical rails.</summary>
    public float RailCoordinate { get; init; }

    public float Margin { get; init; } = 20;
}

/// <summary>Documented classification/solve expectation for one generated file.</summary>
public sealed record ExpectedOutcome(
    CircuitTopologyType Topology,
    string Subtype,
    bool SolveSucceeds);

/// <summary>Complete declarative description of one teaching-circuit project file.</summary>
public sealed class CircuitSpec
{
    /// <summary>Circuit id, e.g. "D01".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Project name shown in the application title (Chinese).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>File name, e.g. "D01-half-wave-rectifier.circuit".</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Teaching description (knowledge point / textbook source).</summary>
    public string Description { get; init; } = string.Empty;

    public List<ElementSpec> Elements { get; init; } = [];

    public List<NetSpec> Nets { get; init; } = [];

    public ExpectedOutcome? Expected { get; init; }
}
