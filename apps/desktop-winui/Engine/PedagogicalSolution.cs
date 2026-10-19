// ============================================================================
// File: apps/desktop-winui/Engine/PedagogicalSolution.cs
// Namespace: CircuitAssistant.Engine
// Description: Pedagogical Solution, Step Models, Analysis Parameters, and
//              Strategy Interface for Educational Circuit Solving.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
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
    DiodeCircuit,
    MultiStageAmplifier,
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
/// Implementations should observe <paramref name="cancellationToken"/> at
/// operation boundaries so the workbench "取消求解" command can stop a
/// long-running numerical pass cooperatively. This is cooperative cancellation,
/// not a process-level hard kill.
/// </summary>
public interface ICircuitStrategy
{
    CircuitTopologyType TopologyType { get; }
    string SubtypeName { get; }
    Task<PedagogicalSolution> SolveAsync(
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Teaching phase of a derivation step for amplifier problems. Amplifier analysis is
/// split into the two equivalent circuits that a textbook uses: the DC equivalent
/// circuit (capacitors open, AC sources zeroed) fixes the quiescent point, and the AC
/// equivalent circuit (DC sources zeroed, coupling/bypass capacitors shorted, device
/// replaced by its small-signal model) yields the mid-band small-signal metrics.
/// </summary>
public enum SolutionPhase
{
    /// <summary>Not part of the DC/AC equivalent-circuit workflow (RC/RLC/MNA/diode/…).</summary>
    None = 0,

    /// <summary>直流等效电路（DC 分析）：求静态工作点 Q。</summary>
    DcEquivalent = 1,

    /// <summary>交流等效电路（AC 分析）：求中频小信号指标。</summary>
    AcEquivalent = 2
}

/// <summary>Display metadata for <see cref="SolutionPhase"/> groups in the result UI.</summary>
public static class SolutionPhaseInfo
{
    public static string GetTitle(SolutionPhase phase) => phase switch
    {
        SolutionPhase.DcEquivalent => "直流等效电路（DC 分析）",
        SolutionPhase.AcEquivalent => "交流等效电路（AC 分析）",
        _ => string.Empty
    };

    public static string GetSubtitle(SolutionPhase phase) => phase switch
    {
        SolutionPhase.DcEquivalent => "耦合/旁路电容视为开路、交流信号源置零，只保留直流电源与偏置网络，确定静态工作点 Q。",
        SolutionPhase.AcEquivalent => "直流电源置零（VCC 交流接地）、耦合/旁路电容视为短路，晶体管用微变等效模型替代，计算中频小信号指标。",
        _ => string.Empty
    };
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

    /// <summary>
    /// DC/AC equivalent-circuit phase used to group the step in the UI. Teaching
    /// strategies for single-transistor amplifiers set this; other strategies keep
    /// <see cref="SolutionPhase.None"/> and are rendered as a flat step list.
    /// </summary>
    public SolutionPhase Phase { get; set; } = SolutionPhase.None;
}

/// <summary>
/// Complete pedagogical solution emitted by a solving strategy.
/// </summary>
public class PedagogicalSolution
{
    public static PedagogicalSolution Failure(CircuitTopologyType type, string message) => new()
    {
        IsSuccess = false,
        TopologyType = type,
        CircuitTitle = "无法完成分析",
        SummaryText = message
    };

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
    /// Structured result of the general linear-network analyzer. Populated by
    /// MnaLinearStrategy; null for the dedicated teaching strategies.
    /// </summary>
    public LinearNetworkInfo? LinearNetwork { get; set; }

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

        // Structured MNA 2.0 result: node potentials, port equivalents, AC sweep.
        if (LinearNetwork != null)
        {
            result.LinearNetwork = LinearNetwork;
            result.FrequencyResponse = LinearNetwork.FrequencyResponse;
            foreach (var node in LinearNetwork.NodePotentials) result.NodePotentials.Add(node);
            foreach (var port in LinearNetwork.PortEquivalents) result.PortEquivalents.Add(port);
            if (LinearNetwork.FrequencyResponse != null)
            {
                foreach (var point in LinearNetwork.FrequencyResponse.Points) result.AcSweepPoints.Add(point);
            }
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

        // Group the steps by teaching phase (DC / AC equivalent circuit). Steps are
        // emitted by the amplifier strategies in phase order; strategies without a
        // phase (RC/RLC/MNA/diode) fall into one header-less group and keep the
        // original flat list appearance.
        var groupOrder = new List<SolutionPhase>();
        var groupMap = new Dictionary<SolutionPhase, AnalysisStepGroup>();
        for (int i = 0; i < stepsCollection.Count; i++)
        {
            var phase = i < Steps.Count ? Steps[i].Phase : SolutionPhase.None;
            if (!groupMap.TryGetValue(phase, out var group))
            {
                group = new AnalysisStepGroup
                {
                    Phase = phase,
                    Title = SolutionPhaseInfo.GetTitle(phase),
                    Subtitle = SolutionPhaseInfo.GetSubtitle(phase),
                    HasHeader = phase != SolutionPhase.None
                };
                groupMap[phase] = group;
                groupOrder.Add(phase);
            }
            group.Steps.Add(stepsCollection[i]);
        }
        var stepGroupsCollection = new ObservableCollection<AnalysisStepGroup>();
        foreach (var phase in groupOrder) stepGroupsCollection.Add(groupMap[phase]);
        result.StepGroups = stepGroupsCollection;

        return result;
    }
}
