// ============================================================================
// File: apps/desktop-winui/Engine/PedagogicalSolution.cs
// Namespace: CircuitAssistant.Engine
// Description: Pedagogical Solution, Step Models, Analysis Parameters, and
//              Strategy Interface for Educational Circuit Solving.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine;

/// <summary>
/// Supported circuit topology categories.
/// </summary>
public enum CircuitTopologyType
{
    FirstOrderRc,
    FirstOrderRl,
    SecondOrderRlc,
    BjtAmplifier,
    MosfetAmplifier,
    MnaLinear,
    GeneralLinear = MnaLinear, // Alias for backward compatibility
    Disconnected,
    OpenCircuit,
    Unsupported
}

/// <summary>
/// Dynamic analysis parameters passed to circuit solving strategies.
/// </summary>
public class AnalysisParameters
{
    public double OperatingFrequency { get; set; } = 1000.0; // 1 kHz
    public double Frequency { get => OperatingFrequency; set => OperatingFrequency = value; }
    public double AmbientTemperatureKelvin { get; set; } = 300.0;
    public double ThermalVoltageUt { get; set; } = 0.026; // 26 mV at ~300 K
    public bool IncludeTransientResponse { get; set; } = true;
    public bool IncludeFrequencyResponse { get; set; } = true;
}

/// <summary>
/// Interface contract implemented by all dedicated circuit solving strategies.
/// </summary>
public interface ICircuitStrategy
{
    CircuitTopologyType TopologyType { get; }
    string SubtypeName { get; }
    Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters);
}

/// <summary>
/// Individual pedagogical derivation step with LaTeX formulas and engineering explanation.
/// </summary>
public class PedagogicalSolutionStep
{
    public int StepIndex { get; set; }
    public string BadgeText { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string LatexFormula { get; set; } = string.Empty;
    public string SubstitutionFormula { get; set; } = string.Empty;
    public string ResultValue { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
    public Dictionary<string, string> Values { get; set; } = [];
    public string MnaLog { get; set; } = string.Empty;
}

/// <summary>
/// Complete pedagogical solution emitted by a solving strategy.
/// </summary>
public class PedagogicalSolution
{
    public bool IsSuccess { get; set; } = true;
    public string CircuitTitle { get; set; } = string.Empty;
    public CircuitTopologyType TopologyType { get; set; }
    public string SubtypeName { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public List<PedagogicalSolutionStep> Steps { get; set; } = [];
    public QPointInfo? QPoint { get; set; }
    public AcParametersInfo? AcParameters { get; set; }
    public string RawMnaLog { get; set; } = string.Empty;

    /// <summary>
    /// Converts this PedagogicalSolution into the UI-bound AnalysisResult.
    /// </summary>
    public AnalysisResult ToAnalysisResult()
    {
        var result = new AnalysisResult
        {
            IsSuccess = IsSuccess,
            CircuitTitle = CircuitTitle,
            SummaryText = SummaryText,
            RawMnaLog = RawMnaLog
        };

        if (QPoint != null)
        {
            result.QPoint = QPoint;
        }
        else
        {
            result.QPoint = new QPointInfo
            {
                Ibq = "—",
                Icq = "—",
                Vceq = "—",
                WorkingRegion = "无晶体管 (线性网络)",
                IsActiveRegion = false
            };
        }

        if (AcParameters != null)
        {
            result.AcParameters = AcParameters;
        }

        var stepsCollection = new ObservableCollection<AnalysisStep>();
        foreach (var s in Steps)
        {
            stepsCollection.Add(new AnalysisStep
            {
                StepNumber = s.StepIndex,
                BadgeText = string.IsNullOrWhiteSpace(s.BadgeText) ? $"第 {s.StepIndex} 步" : s.BadgeText,
                Title = s.Title,
                MethodName = s.MethodName,
                Explanation = s.Content,
                LatexFormula = s.LatexFormula,
                SubstitutionFormula = s.SubstitutionFormula,
                ResultValue = s.ResultValue,
                MnaLog = s.MnaLog,
                HasMnaLog = !string.IsNullOrWhiteSpace(s.MnaLog),
                IsExpanded = true
            });
        }
        result.Steps = stepsCollection;

        return result;
    }
}
