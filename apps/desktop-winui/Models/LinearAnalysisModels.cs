// ============================================================================
// File: apps/desktop-winui/Models/LinearAnalysisModels.cs
// Namespace: CircuitAssistant.Models
// Description: Structured result contract shared by the C# linear-network
//              solver (MNA 2.0) and the WinUI presentation layer. These types
//              contain data only; numerical conventions are documented in
//              Engine/Linear/LinearNetworkAnalyzer.cs.
// ============================================================================

using System.Collections.Generic;

namespace CircuitAssistant.Models;

/// <summary>
/// Solved DC potential of one non-ground electrical net.
/// </summary>
public sealed class NodePotentialInfo
{
    public string NetName { get; set; } = string.Empty;
    public int NetId { get; set; }
    public double VoltageV { get; set; }
    public string Display { get; set; } = string.Empty;
    public string LatexFormula { get; set; } = string.Empty;
}

/// <summary>
/// Thévenin/Norton style equivalent of an arbitrary two-terminal port of the
/// linear network. <see cref="ResistanceOhm"/> is obtained with every
/// independent source zeroed; <see cref="OpenCircuitVoltageV"/> is the DC
/// open-circuit voltage between the two nets when sources are active.
/// </summary>
public sealed class PortEquivalentInfo
{
    public string PortLabel { get; set; } = string.Empty;
    public string PositiveNet { get; set; } = string.Empty;
    public string NegativeNet { get; set; } = string.Empty;
    public double ResistanceOhm { get; set; }
    public bool HasOpenCircuitVoltage { get; set; }
    public double OpenCircuitVoltageV { get; set; }
    public string ResistanceDisplay { get; set; } = string.Empty;
    public string OpenCircuitVoltageDisplay { get; set; } = string.Empty;
    public bool IsAcSourcePort { get; set; }
    public string Note { get; set; } = string.Empty;

    /// <summary>Compact one-line description used by the metrics panel.</summary>
    public string DetailDisplay => HasOpenCircuitVoltage
        ? $"Req = {ResistanceDisplay} · Voc = {OpenCircuitVoltageDisplay}"
        : $"Req = {ResistanceDisplay}";
}

/// <summary>
/// One point of a computed AC magnitude/phase sweep.
/// </summary>
public sealed class AcSweepPointInfo
{
    public double FrequencyHz { get; set; }
    public double GainRatio { get; set; }
    public double GainDb { get; set; }
    public double PhaseDeg { get; set; }
    public string FrequencyDisplay { get; set; } = string.Empty;
    public string GainDbDisplay { get; set; } = string.Empty;
    public string PhaseDisplay { get; set; } = string.Empty;
}

/// <summary>
/// Small-signal AC response of a selected input/output port pair. When the
/// network has no explicit output port the solver uses the non-ground net with
/// the largest open-circuit response magnitude as the heuristic output.
/// </summary>
public sealed class FrequencyResponseInfo
{
    public string InputPortLabel { get; set; } = string.Empty;
    public string OutputPortLabel { get; set; } = string.Empty;
    public double OperatingFrequencyHz { get; set; }
    public double GainRatio { get; set; }
    public double GainDb { get; set; }
    public double PhaseDeg { get; set; }
    public string GainRatioDisplay { get; set; } = string.Empty;
    public string GainDbDisplay { get; set; } = string.Empty;
    public string PhaseDisplay { get; set; } = string.Empty;
    public double? LowerCutoffHz { get; set; }
    public double? UpperCutoffHz { get; set; }
    public double? BandwidthHz { get; set; }
    public string LowerCutoffDisplay { get; set; } = "—";
    public string UpperCutoffDisplay { get; set; } = "—";
    public string BandwidthDisplay { get; set; } = "—";
    public string BoundaryNote { get; set; } = string.Empty;
    public List<AcSweepPointInfo> Points { get; set; } = new();
}

/// <summary>
/// Complete structured linear-network result emitted by MnaLinearStrategy.
/// </summary>
public sealed class LinearNetworkInfo
{
    public string ModelSummary { get; set; } = string.Empty;
    public string OperatingMode { get; set; } = string.Empty;
    public int MatrixDimension { get; set; }
    public List<NodePotentialInfo> NodePotentials { get; set; } = new();
    public List<PortEquivalentInfo> PortEquivalents { get; set; } = new();
    public FrequencyResponseInfo? FrequencyResponse { get; set; }
    public string BoundaryNote { get; set; } = string.Empty;
}
