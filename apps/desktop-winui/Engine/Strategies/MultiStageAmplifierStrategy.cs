// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/MultiStageAmplifierStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Placeholder strategy for multi-stage transistor amplifiers.
//
//              Multi-stage cascade analysis is intentionally deferred (user
//              requirement: keep it in the future development plan but reserve an
//              interface). This strategy never returns a numeric solution: it
//              reports the detected cascade and the documented implementation
//              order, so the application never fabricates a single-transistor
//              answer for a multi-stage circuit.
//
// OWNER: Lead.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine.MultiStage;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine.Strategies;

/// <summary>
/// Rejection strategy for circuits containing more than one transistor (BJT,
/// MOSFET or a mix). Implements the reserved <see cref="IMultiStageAnalysis"/>
/// contract so a future round can add cascade decomposition without changing the
/// classifier, UI or result contracts.
/// </summary>
public sealed class MultiStageAmplifierStrategy : ICircuitStrategy, IMultiStageAnalysis
{
    public CircuitTopologyType TopologyType => CircuitTopologyType.MultiStageAmplifier;

    public string SubtypeName => "Cascade";

    public bool TryPlan(ElectricalNetlist netlist, out MultiStagePlan plan, out string error)
    {
        plan = new MultiStagePlan();
        error = string.Empty;
        if (netlist == null || netlist.Transistors.Count == 0)
        {
            error = "网表中没有晶体管，无法分解多级放大电路。";
            return false;
        }

        foreach (TransistorDevice device in netlist.Transistors)
        {
            plan.Stages.Add(new MultiStageStage
            {
                DeviceId = device.Element.Id,
                DeviceType = device.DeviceType,
                SubtypeHint = device.DeviceType switch
                {
                    ElementType.BjtNpn or ElementType.BjtPnp => "待定（共射/共基/共集）",
                    ElementType.MosfetNEnh or ElementType.MosfetPEnh => "待定（共源/共漏/共栅）",
                    _ => "未支持的器件"
                }
            });
        }

        plan.TopologyNote = $"检测到 {plan.Stages.Count} 只晶体管（{string.Join(", ", plan.Stages.Select(s => s.DeviceId))}）。";
        return plan.Stages.Count > 1;
    }

    public Task<PedagogicalSolution> SolveAsync(
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryPlan(netlist, out MultiStagePlan plan, out _);
        string devices = plan.Stages.Count > 0
            ? string.Join("、", plan.Stages.Select(s => s.DeviceId))
            : "未知";
        string message =
            $"多级放大电路分析已列入后续开发规划，本轮只预留接口（IMultiStageAnalysis），尚未实现级联求解。"
            + $"检测到 {plan.Stages.Count} 只晶体管：{devices}。"
            + "当前版本只支持单管放大电路（单只 BJT 共射/共基/共集，或单只增强型 MOSFET 共源/共漏/共栅），"
            + "请把电路拆分为单级后分别求解。"
            + $"后续计划实现顺序：{plan.PlannedImplementationOrder}。";
        return Task.FromResult(PedagogicalSolution.Failure(TopologyType, message));
    }
}
