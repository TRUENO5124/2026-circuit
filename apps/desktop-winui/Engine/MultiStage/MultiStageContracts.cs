// ============================================================================
// File: apps/desktop-winui/Engine/MultiStage/MultiStageContracts.cs
// Namespace: CircuitAssistant.Engine.MultiStage
// Description: RESERVED INTERFACE for future multi-stage amplifier analysis.
//
//              Multi-stage cascade analysis is deliberately NOT implemented in
//              this round (user requirement: keep it in the future development
//              plan but reserve an interface). The contract below defines the
//              planned planning model so a later implementation can add stage
//              decomposition without changing the classifier or UI contracts.
//
//              Planned implementation order (documented in the project spec):
//                1. single-transistor teaching models (all supported types);
//                2. multi-stage cascade (RC-coupled / direct-coupled, per-stage
//                   Q point + cascaded small-signal);
//                3. diode nonlinear extension (Newton-Raphson large signal);
//                4. mixed BJT/MOS nonlinear networks;
//                5. large-signal / distortion analysis.
//
// OWNER: Lead. Do not implement cascade solving here.
// ============================================================================

using System;
using System.Collections.Generic;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine.MultiStage;

/// <summary>
/// One planned amplification stage of a future multi-stage analysis.
/// </summary>
public sealed class MultiStageStage
{
    /// <summary>Circuit element id of the stage's active device (BJT or MOSFET).</summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>Element type of the stage's active device.</summary>
    public ElementType DeviceType { get; init; }

    /// <summary>
    /// Planned per-stage subtype hint (e.g. CommonEmitter / CommonCollector /
    /// CommonBase / CommonSource / CommonDrain), filled in by the future planner.
    /// </summary>
    public string SubtypeHint { get; init; } = string.Empty;

    /// <summary>Planned stage gain (V/V); NaN until implemented.</summary>
    public double PlannedGain { get; init; } = double.NaN;

    /// <summary>Planned stage input resistance in ohms; NaN until implemented.</summary>
    public double PlannedInputResistanceOhm { get; init; } = double.NaN;

    /// <summary>Planned stage output resistance in ohms; NaN until implemented.</summary>
    public double PlannedOutputResistanceOhm { get; init; } = double.NaN;
}

/// <summary>
/// Decomposition plan produced by a future multi-stage implementation.
/// </summary>
public sealed class MultiStagePlan
{
    public List<MultiStageStage> Stages { get; } = new();

    /// <summary>Human-readable coupling description (RC-coupled / direct-coupled / mixed).</summary>
    public string TopologyNote { get; set; } = string.Empty;

    /// <summary>Documented implementation order, surfaced to the UI when rejected.</summary>
    public string PlannedImplementationOrder { get; set; } =
        "单管全类型 → 多级级联 → 二极管非线性扩展 → 混合管 → 大信号/失真";
}

/// <summary>
/// Reserved contract for future multi-stage cascade analysis. A future strategy
/// implements <see cref="TryPlan"/> to decompose the netlist into stages; the
/// present runtime only uses it to explain why multi-stage solving is deferred.
/// </summary>
public interface IMultiStageAnalysis
{
    /// <summary>
    /// Attempts to decompose the given netlist into amplifier stages. Returns
    /// false (with a non-empty <paramref name="error"/>) when the topology is not
    /// a supported cascade. This contract is reserved for a future round.
    /// </summary>
    bool TryPlan(ElectricalNetlist netlist, out MultiStagePlan plan, out string error);
}
