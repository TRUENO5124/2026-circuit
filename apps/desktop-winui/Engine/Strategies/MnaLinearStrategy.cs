// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/MnaLinearStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical solving strategy for general linear networks built on
//              the MNA 2.0 analyzer (Engine/Linear/LinearNetworkAnalyzer.cs).
//              Covers R/C/L, independent DC/AC sources and the four controlled
//              sources, and fills the structured LinearNetwork result contract:
//              node potentials, arbitrary-port Thevenin equivalents and the AC
//              phasor / frequency response. Unsupported nonlinear devices are
//              rejected explicitly; no teaching step is fabricated.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

public class MnaLinearStrategy : ICircuitStrategy
{
    private const double SweepStartHz = 10.0;
    private const double SweepStopHz = 10_000_000.0; // 10 MHz
    private const int SweepPointsPerDecade = 10;

    /// <summary>Source port + output port + the first 12 arbitrary node pairs.</summary>
    private const int MaxPortEquivalentEntries = 14;

    /// <summary>Largest numeric matrix printed inside a step formula.</summary>
    private const int MaxMatrixDisplayOrder = 8;

    public CircuitTopologyType TopologyType => CircuitTopologyType.MnaLinear;
    public string SubtypeName => "GeneralLinearMna";

    public Task<PedagogicalSolution> SolveAsync(
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = SubtypeName,
            CircuitTitle = "通用线性网络 MNA 2.0：动态元件 / 受控源 / 任意端口戴维南等效 / 相量频响"
        };

        // 1. Structural validation: keeps the pre-MNA-2.0 failure semantics for
        //    shorted / floating / disconnected drawings.
        var validation = netlist.Validate();
        if (!validation.IsValid)
        {
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                string.Join("\n", validation.Issues
                    .Where(i => i.Severity == NetlistValidationSeverity.Error)
                    .Select(i => i.Message))));
        }

        // 2. Build the linear model (explicit rejection of unsupported devices).
        if (!LinearNetworkAnalyzer.TryCreate(netlist, out var model, out var createError, cancellationToken) || model == null)
        {
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType, createError));
        }

        try
        {
            return Task.FromResult(BuildSolution(parameters, model, solution, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(PedagogicalSolution.Failure(TopologyType,
                "线性网络求解出现未预期错误，已停止计算：" + ex.Message));
        }
    }

    // =====================================================================
    // Main pipeline
    // =====================================================================

    private PedagogicalSolution BuildSolution(
        AnalysisParameters? parameters,
        LinearNetworkAnalyzer model,
        PedagogicalSolution solution,
        CancellationToken cancellationToken)
    {
        bool isAcMode = model.HasAcExcitation;

        // Operating frequency: prefer the first AC source's FrequencyHz, then the
        // user analysis parameter, then 1 kHz as a safe teaching default.
        double operatingFrequency = model.PreferredOperatingFrequencyHz
            ?? parameters?.OperatingFrequency
            ?? 1000.0;
        if (!double.IsFinite(operatingFrequency) || operatingFrequency <= 0) operatingFrequency = 1000.0;

        // 3. Primary solve: AC phasor when an AC source exists, otherwise DC.
        LinearSolution primary;
        LinearSolution? dcSolution = null;
        if (isAcMode)
        {
            if (!model.TrySolveAc(operatingFrequency, out var acSolution, out var acError, cancellationToken) || acSolution == null)
            {
                return PedagogicalSolution.Failure(TopologyType, "交流相量求解失败：" + acError);
            }
            primary = acSolution;
            if (model.TrySolveDc(out dcSolution, out _, cancellationToken))
            {
                // DC operating point available for Thevenin open-circuit voltages.
            }
        }
        else
        {
            if (!model.TrySolveDc(out var dcOnly, out var dcError, cancellationToken) || dcOnly == null)
            {
                return PedagogicalSolution.Failure(TopologyType, "直流 MNA 求解失败：" + dcError);
            }
            primary = dcOnly;
            dcSolution = dcOnly;
        }

        // 4. Ports: excitation source port (required) + heuristic output port.
        var source = DetermineSourcePort(model, isAcMode);
        var excludedForOutput = new List<int>();
        if (source != null)
        {
            excludedForOutput.Add(source.PositiveNet.NetId);
            excludedForOutput.Add(source.NegativeNet.NetId);
        }
        bool hasOutputPort = model.TryFindOutputNode(primary, excludedForOutput, out var outputNode) && outputNode != null;
        int outputNetId = hasOutputPort ? outputNode!.NetId : model.GroundNetId;

        var boundaryNotes = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.BoundaryNote)) boundaryNotes.Add(model.BoundaryNote);

        // 5. Frequency response (AC only).
        FrequencyResponseInfo? frequencyResponse = null;
        AcSweepResult? sweep = null;
        if (isAcMode)
        {
            frequencyResponse = TryBuildFrequencyResponse(
                model, source, hasOutputPort ? outputNetId : -1, operatingFrequency,
                boundaryNotes, out sweep, cancellationToken);
        }
        else
        {
            boundaryNotes.Add("未检测到交流激励（无 AC 电压源/电流源）：仅输出直流工作点与直流戴维南等效，频响字段为空");
        }

        // 6. Structured node potentials.
        var nodePotentials = BuildNodePotentials(model, primary, isAcMode);

        // 7. Arbitrary-port Thevenin/Norton equivalents.
        var portEquivalents = BuildPortEquivalents(
            model, dcSolution, source, hasOutputPort ? outputNetId : -1, boundaryNotes, cancellationToken);

        // 8. AC parameter display strings.
        var acParameters = BuildAcParameters(model, dcSolution, source, hasOutputPort ? outputNetId : -1, isAcMode,
            operatingFrequency, frequencyResponse, boundaryNotes, cancellationToken);

        // 9. Assemble the structured contract.
        string modeText = isAcMode
            ? $"交流正弦稳态相量 (AC, f = {FormatFrequency(operatingFrequency)})"
            : "直流 (DC)";

        var info = new LinearNetworkInfo
        {
            ModelSummary = model.ModelSummary,
            OperatingMode = modeText,
            MatrixDimension = model.MatrixDimension,
            NodePotentials = nodePotentials,
            PortEquivalents = portEquivalents,
            FrequencyResponse = frequencyResponse
        };

        solution.SummaryText =
            $"SPICE MNA 2.0 通用线性网络求解成功（{modeText}）：共解算 {model.NodeVariableCount} 个独立节点电位" +
            $"与 {model.BranchVariableCount} 个支路电流变量，矩阵阶数 {model.MatrixDimension}×{model.MatrixDimension}。";

        // Raw log keeps the legacy format for DC networks (regression tests parse it).
        solution.RawMnaLog = dcSolution != null
            ? BuildDcLog(model, dcSolution)
            : BuildAcLog(model, primary, operatingFrequency);

        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = "通用线性网络 (MNA 2.0)",
            IsActiveRegion = false
        };
        solution.AcParameters = acParameters;

        // 10. Scope statement is part of the boundary note so UI text is honest.
        boundaryNotes.Add("适用范围：R/C/L、独立 DC/AC 电压源与电流源、四种理想线性受控源（VCVS/VCCS/CCVS/CCCS）；" +
                          "二极管、变压器、BJT、MOSFET/JFET、VCC 端口不参与本线性求解器，遇到时求解直接失败而不是静默忽略");
        info.BoundaryNote = string.Join("；", boundaryNotes.Where(n => !string.IsNullOrWhiteSpace(n)));

        solution.LinearNetwork = info;

        // 11. Derivation steps (Chinese teaching tone).
        AppendSteps(solution, model, primary, dcSolution, isAcMode, operatingFrequency, frequencyResponse, sweep,
            portEquivalents, boundaryNotes, cancellationToken);

        return solution;
    }

    // =====================================================================
    // Port selection
    // =====================================================================

    private static LinearDevice? DetermineSourcePort(LinearNetworkAnalyzer model, bool isAcMode)
    {
        if (isAcMode)
        {
            // FirstAcSource already prefers AC voltage sources over AC current sources.
            return model.FirstAcSource;
        }

        // DC mode: voltage sources take priority (devices are ordered deterministically by
        // element Id inside the analyzer), so the reported excitation port and the DC
        // voltage ratio do not depend on whether "I1" happens to sort before "V1".
        // A current source is used only when the network has no independent voltage source.
        return model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.DcVoltageSource)
               ?? model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.DcCurrentSource);
    }

    /// <summary>
    /// Cutoff display text. When the sweep window cannot provide a trustworthy -3 dB
    /// estimate (passband outside the window / crossing outside the window) the value is
    /// null and the display explicitly says the estimate is outside the sweep range
    /// instead of presenting a fabricated cutoff.
    /// </summary>
    private static string DescribeCutoffDisplay(double? cutoffHz, bool crossingOutsideWindow, bool estimateUnavailable)
    {
        if (cutoffHz.HasValue)
        {
            return TeachingDisplay.Text(cutoffHz.Value, QuantityKind.Frequency);
        }
        return estimateUnavailable || crossingOutsideWindow ? "未检测到（超出扫描范围）" : "未检测到";
    }

    // =====================================================================
    // Frequency response
    // =====================================================================

    private FrequencyResponseInfo? TryBuildFrequencyResponse(
        LinearNetworkAnalyzer model,
        LinearDevice? source,
        int outputNetId,
        double operatingFrequency,
        List<string> boundaryNotes,
        out AcSweepResult? sweep,
        CancellationToken cancellationToken)
    {
        sweep = null;
        if (source == null)
        {
            boundaryNotes.Add("存在交流激励但无法确定激励源端口，未生成频响");
            return null;
        }
        if (outputNetId < 0 || outputNetId == model.GroundNetId ||
            outputNetId == source.PositiveNet.NetId || outputNetId == source.NegativeNet.NetId)
        {
            boundaryNotes.Add("无法确定独立于激励端口的输出端口（网络中除激励端口外没有其他非地节点），未生成频响");
            return null;
        }

        int inputPositive = source.PositiveNet.NetId;
        int inputNegative = source.NegativeNet.NetId;
        if (!model.TryGetTransferGain(operatingFrequency, inputPositive, inputNegative, outputNetId, model.GroundNetId,
                out var gain, out var gainUnit, out _, out var gainError, cancellationToken))
        {
            boundaryNotes.Add("工作频率增益计算失败，未生成频响：" + gainError);
            return null;
        }

        double magnitude = gain.Magnitude;
        double gainDb = LinearNetworkAnalyzer.ToDecibel(magnitude);
        double phaseDeg = magnitude < 1e-300 ? 0.0 : Math.Atan2(gain.Imaginary, gain.Real) * 180.0 / Math.PI;

        var response = new FrequencyResponseInfo
        {
            InputPortLabel = $"{NetNameOf(model, inputPositive)} – {NetNameOf(model, inputNegative)}（激励源 {source.ElementId}）",
            OutputPortLabel = $"{NetNameOf(model, outputNetId)} – {model.GroundNetName}",
            OperatingFrequencyHz = operatingFrequency,
            GainRatio = magnitude,
            GainDb = gainDb,
            PhaseDeg = phaseDeg,
            GainRatioDisplay = gainUnit == "V/A"
                ? TeachingDisplay.Text(magnitude, QuantityKind.Resistance)
                : TeachingDisplay.Plain(magnitude),
            GainDbDisplay = TeachingDisplay.Plain(gainDb) + " dB",
            PhaseDisplay = TeachingDisplay.Plain(phaseDeg) + "°"
        };

        if (gainUnit == "V/A")
        {
            response.BoundaryNote = "激励源为交流电流源：增益字段为跨阻（V/A），输入端口以电流源两端标注。";
        }

        if (model.TrySweep(inputPositive, inputNegative, outputNetId, model.GroundNetId,
                SweepStartHz, SweepStopHz, SweepPointsPerDecade, out var sweepResult, out var sweepError, cancellationToken)
            && sweepResult != null)
        {
            sweep = sweepResult;
            foreach (var point in sweepResult.Points)
            {
                response.Points.Add(new AcSweepPointInfo
                {
                    FrequencyHz = point.FrequencyHz,
                    GainRatio = point.GainMagnitude,
                    GainDb = point.GainDb,
                    PhaseDeg = point.PhaseDeg,
                    FrequencyDisplay = TeachingDisplay.Text(point.FrequencyHz, QuantityKind.Frequency),
                    GainDbDisplay = TeachingDisplay.Plain(point.GainDb) + " dB",
                    PhaseDisplay = TeachingDisplay.Plain(point.PhaseDeg) + "°"
                });
            }

            response.LowerCutoffHz = sweepResult.LowerCutoffHz;
            response.UpperCutoffHz = sweepResult.UpperCutoffHz;
            response.BandwidthHz = sweepResult.BandwidthHz;
            bool estimateUnavailable = !sweepResult.CutoffEstimateAvailable;
            response.LowerCutoffDisplay = DescribeCutoffDisplay(
                sweepResult.LowerCutoffHz, sweepResult.LowerCutoffBelowWindow, estimateUnavailable);
            response.UpperCutoffDisplay = DescribeCutoffDisplay(
                sweepResult.UpperCutoffHz, sweepResult.UpperCutoffAboveWindow, estimateUnavailable);
            response.BandwidthDisplay = sweepResult.BandwidthHz.HasValue
                ? TeachingDisplay.Text(sweepResult.BandwidthHz.Value, QuantityKind.Frequency)
                : estimateUnavailable ? "未检测到（超出扫描范围）" : "—";

            var sweepNotes = sweepResult.Notes.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            string cutoffNote = string.Join("；", sweepNotes);
            response.BoundaryNote = string.IsNullOrWhiteSpace(response.BoundaryNote)
                ? cutoffNote
                : string.IsNullOrWhiteSpace(cutoffNote) ? response.BoundaryNote : response.BoundaryNote + "；" + cutoffNote;

            if (!estimateUnavailable)
            {
                if (sweepNotes.Count > 0) boundaryNotes.Add("频响截止估计：" + cutoffNote);
            }
            else
            {
                boundaryNotes.Add("扫频窗口未覆盖真实截止频率，截止估计不可用：" +
                                  (string.IsNullOrWhiteSpace(sweepResult.CutoffUnavailableReason) ? cutoffNote : sweepResult.CutoffUnavailableReason));
            }
        }
        else
        {
            boundaryNotes.Add("频率扫描失败，频响仅含工作频率单点：" + sweepError);
        }

        return response;
    }

    // =====================================================================
    // Node potentials / port equivalents / AC parameters
    // =====================================================================

    private static List<NodePotentialInfo> BuildNodePotentials(
        LinearNetworkAnalyzer model,
        LinearSolution primary,
        bool isAcMode)
    {
        var result = new List<NodePotentialInfo>(model.Nodes.Count);
        foreach (var node in model.Nodes)
        {
            if (isAcMode)
            {
                var phasor = primary.NodePhasor(node.NetId);
                double magnitude = phasor.Magnitude;
                double phase = primary.NodePhaseDeg(node.NetId);
                result.Add(new NodePotentialInfo
                {
                    NetName = node.Name,
                    NetId = node.NetId,
                    VoltageV = magnitude,
                    Display = TeachingDisplay.Text(magnitude, QuantityKind.Voltage) +
                              " ∠ " + TeachingDisplay.Plain(phase) + "°",
                    LatexFormula = $@"\hat{{V}}_{{{node.Name}}} = {TeachingDisplay.Latex(magnitude, QuantityKind.Voltage)}\angle {TeachingDisplay.LatexNumber(phase, QuantityKind.Ratio)}^\circ"
                });
            }
            else
            {
                double voltage = primary.NodeVoltage(node.NetId);
                result.Add(new NodePotentialInfo
                {
                    NetName = node.Name,
                    NetId = node.NetId,
                    VoltageV = voltage,
                    Display = TeachingDisplay.Text(voltage, QuantityKind.Voltage),
                    LatexFormula = $@"v_{{{node.Name}}} = {TeachingDisplay.Latex(voltage, QuantityKind.Voltage)}"
                });
            }
        }
        return result;
    }

    private List<PortEquivalentInfo> BuildPortEquivalents(
        LinearNetworkAnalyzer model,
        LinearSolution? dcSolution,
        LinearDevice? source,
        int outputNetId,
        List<string> boundaryNotes,
        CancellationToken cancellationToken)
    {
        var pairNets = model.Nodes.Select(n => (Id: n.NetId, Name: n.Name)).ToList();
        pairNets.Add((model.GroundNetId, model.GroundNetName));

        var allPairs = new List<(int PositiveId, int NegativeId)>();
        for (int i = 0; i < pairNets.Count; i++)
        {
            for (int j = i + 1; j < pairNets.Count; j++)
            {
                allPairs.Add((pairNets[i].Id, pairNets[j].Id));
            }
        }

        var entries = new List<(int PositiveId, int NegativeId, string Label, bool IsSourcePort)>();
        var entryKeys = new HashSet<string>(StringComparer.Ordinal);

        void TryAddEntry(int positiveId, int negativeId, string label, bool isSourcePort)
        {
            if (positiveId == negativeId) return;
            string key = positiveId <= negativeId ? $"{positiveId}|{negativeId}" : $"{negativeId}|{positiveId}";
            if (!entryKeys.Add(key)) return;
            entries.Add((positiveId, negativeId, label, isSourcePort));
        }

        if (source != null)
        {
            TryAddEntry(source.PositiveNet.NetId, source.NegativeNet.NetId,
                $"激励源端口（{source.ElementId}）", true);
        }
        if (outputNetId >= 0 && outputNetId != model.GroundNetId)
        {
            TryAddEntry(outputNetId, model.GroundNetId, "输出端口（对地）", false);
        }

        foreach (var pair in allPairs)
        {
            if (entries.Count >= MaxPortEquivalentEntries) break;
            cancellationToken.ThrowIfCancellationRequested();
            TryAddEntry(pair.PositiveId, pair.NegativeId,
                $"端口 {NetNameOf(model, pair.PositiveId)} – {NetNameOf(model, pair.NegativeId)}", false);
        }

        if (allPairs.Count > entries.Count)
        {
            boundaryNotes.Add($"任意节点对等效共 {allPairs.Count} 对，已按节点 NetId 顺序输出前 {entries.Count} 对" +
                              "（含激励源端口与输出端口），其余端口因表格长度省略");
        }

        var result = new List<PortEquivalentInfo>(entries.Count);
        bool anyInfinite = false;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new PortEquivalentInfo
            {
                PortLabel = entry.Label,
                PositiveNet = NetNameOf(model, entry.PositiveId),
                NegativeNet = NetNameOf(model, entry.NegativeId),
                IsAcSourcePort = entry.IsSourcePort && (source?.IsAcSource ?? false)
            };

            if (model.TryGetEquivalentResistance(entry.PositiveId, entry.NegativeId,
                    out double resistance, out string resistanceError, cancellationToken)
                && double.IsFinite(resistance))
            {
                info.ResistanceOhm = resistance;
                info.ResistanceDisplay = TeachingDisplay.Text(resistance, QuantityKind.Resistance);
            }
            else
            {
                anyInfinite = true;
                info.ResistanceOhm = double.PositiveInfinity;
                info.ResistanceDisplay = "∞（无直流通路）";
                info.Note = "端口在直流等效下没有直流通路或网络奇异：" + resistanceError;
            }

            if (dcSolution != null)
            {
                double openCircuitVoltage = dcSolution.NodePhasor(entry.PositiveId).Real -
                                            dcSolution.NodePhasor(entry.NegativeId).Real;
                if (double.IsFinite(openCircuitVoltage))
                {
                    info.HasOpenCircuitVoltage = true;
                    info.OpenCircuitVoltageV = openCircuitVoltage;
                    info.OpenCircuitVoltageDisplay = TeachingDisplay.Text(openCircuitVoltage, QuantityKind.Voltage);
                }
            }
            if (!info.HasOpenCircuitVoltage)
            {
                info.OpenCircuitVoltageDisplay = "—";
                if (string.IsNullOrEmpty(info.Note)) info.Note = "未获得直流开路电压（网络无直流解）。";
            }

            result.Add(info);
        }

        if (anyInfinite)
        {
            boundaryNotes.Add("部分端口在直流等效下无直流通路（电容隔直或网络奇异），表中 R_eq 记为 ∞");
        }

        return result;
    }

    private static AcParametersInfo BuildAcParameters(
        LinearNetworkAnalyzer model,
        LinearSolution? dcSolution,
        LinearDevice? source,
        int outputNetId,
        bool isAcMode,
        double operatingFrequency,
        FrequencyResponseInfo? frequencyResponse,
        List<string> boundaryNotes,
        CancellationToken cancellationToken)
    {
        var ac = new AcParametersInfo
        {
            VoltageGain = "—",
            InputResistance = "—",
            OutputResistance = "—",
            LowerCutoffFreq = "—",
            UpperCutoffFreq = "—",
            Bandwidth = "—"
        };

        if (frequencyResponse != null)
        {
            ac.VoltageGain = $"{frequencyResponse.GainRatioDisplay}（{frequencyResponse.GainDbDisplay}, ∠{frequencyResponse.PhaseDisplay}）";
            ac.LowerCutoffFreq = frequencyResponse.LowerCutoffDisplay;
            ac.UpperCutoffFreq = frequencyResponse.UpperCutoffDisplay;
            ac.Bandwidth = frequencyResponse.BandwidthDisplay;
        }
        else if (!isAcMode && dcSolution != null && source != null && source.IsVoltageSource &&
                 outputNetId >= 0 && outputNetId != model.GroundNetId)
        {
            // DC-only network: report the DC voltage ratio when both ports are known.
            double inputVoltage = dcSolution.NodeVoltage(source.PositiveNet.NetId) -
                                  dcSolution.NodeVoltage(source.NegativeNet.NetId);
            double outputVoltage = dcSolution.NodeVoltage(outputNetId) - dcSolution.NodeVoltage(model.GroundNetId);
            if (double.IsFinite(inputVoltage) && Math.Abs(inputVoltage) > 1e-12 && double.IsFinite(outputVoltage))
            {
                double ratio = outputVoltage / inputVoltage;
                ac.VoltageGain = "DC 电压比 " + TeachingDisplay.Plain(ratio) +
                                 "（V_out = " + TeachingDisplay.Text(outputVoltage, QuantityKind.Voltage) + "）";
            }
        }
        else if (!isAcMode && source != null && source.IsCurrentSource)
        {
            ac.VoltageGain = "—（激励为电流源，无电压增益定义）";
        }

        // Input resistance seen by the excitation source (driving-point impedance).
        if (source != null)
        {
            if (model.TryGetDrivingPointImpedance(source, isAcMode, operatingFrequency,
                    out var impedance, out _, cancellationToken))
            {
                if (isAcMode)
                {
                    ac.InputResistance = TeachingDisplay.Text(impedance.Magnitude, QuantityKind.Resistance);
                    if (Math.Abs(impedance.Imaginary) > 1e-12 * Math.Max(1.0, impedance.Magnitude))
                    {
                        ac.InputResistance += "（工作频率驱动点阻抗幅值）";
                    }
                }
                else
                {
                    ac.InputResistance = TeachingDisplay.Text(impedance.Real, QuantityKind.Resistance);
                }
            }
            else
            {
                ac.InputResistance = "—（端口无直流通路）";
            }
        }

        // Output resistance: DC Thevenin resistance at the output port (sources zeroed).
        if (outputNetId >= 0 && outputNetId != model.GroundNetId)
        {
            if (model.TryGetEquivalentResistance(outputNetId, model.GroundNetId, out double outputResistance, out _, cancellationToken)
                && double.IsFinite(outputResistance))
            {
                ac.OutputResistance = TeachingDisplay.Text(outputResistance, QuantityKind.Resistance);
                if (isAcMode)
                {
                    ac.OutputResistance += "（直流等效，电容开路）";
                }
            }
            else
            {
                ac.OutputResistance = "—（端口无直流通路）";
            }
        }

        if (!isAcMode)
        {
            ac.LowerCutoffFreq = "不适用（无交流激励）";
            ac.UpperCutoffFreq = "不适用（无交流激励）";
            ac.Bandwidth = "不适用（无交流激励）";
        }
        else if (frequencyResponse == null)
        {
            ac.LowerCutoffFreq = ac.UpperCutoffFreq = ac.Bandwidth = "—";
            boundaryNotes.Add("未能建立相量频响，截止频率/带宽无法估计");
        }

        return ac;
    }

    // =====================================================================
    // Logs
    // =====================================================================

    private static string BuildDcLog(LinearNetworkAnalyzer model, LinearSolution dc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MNA 求解日志 ===");
        sb.AppendLine($"系统矩阵阶数: {dc.MatrixDimension} (节点变量: {model.NodeVariableCount}, 支路电流变量: {model.BranchVariableCount})");
        for (int i = 0; i < model.Nodes.Count; i++)
        {
            var node = model.Nodes[i];
            sb.AppendLine($"  节点 {node.Name} 电位 v_{i + 1} = {FormatLogValue(dc.NodeVoltage(node.NetId))} V");
        }
        foreach (var device in model.Devices)
        {
            if (!device.HasOwnBranchVariable) continue;
            double current = dc.BranchPhasor(device.ElementId).Real;
            string label = device.Kind switch
            {
                LinearDeviceKind.DcVoltageSource or LinearDeviceKind.AcVoltageSource => "电源",
                LinearDeviceKind.Inductor => "电感",
                _ => "受控源"
            };
            sb.AppendLine($"  {label} {device.ElementId} 支路电流 i_{device.ElementId} = {FormatLogValue(current * 1000.0)} mA");
        }
        foreach (var branch in dc.BranchCurrents.Where(b => b.IsControlBranch))
        {
            sb.AppendLine($"  控制支路 {branch.ElementId} 电流 i_ctrl_{branch.ElementId} = {FormatLogValue(branch.Value.Real * 1000.0)} mA");
        }
        return sb.ToString();
    }

    private static string BuildAcLog(LinearNetworkAnalyzer model, LinearSolution ac, double frequencyHz)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MNA 求解日志（交流相量）===");
        sb.AppendLine($"工作频率: {FormatFrequency(frequencyHz)}；系统矩阵阶数: {ac.MatrixDimension} (节点变量: {model.NodeVariableCount}, 支路电流变量: {model.BranchVariableCount})");
        for (int i = 0; i < model.Nodes.Count; i++)
        {
            var node = model.Nodes[i];
            double magnitude = ac.NodeMagnitude(node.NetId);
            double phase = ac.NodePhaseDeg(node.NetId);
            sb.AppendLine($"  节点 {node.Name} 相量 v_{i + 1} = {FormatLogValue(magnitude)} ∠ {FormatLogValue(phase)}° V");
        }
        foreach (var device in model.Devices)
        {
            if (!device.HasOwnBranchVariable) continue;
            var current = ac.BranchPhasor(device.ElementId);
            sb.AppendLine($"  支路 {device.ElementId} 电流相量 i_{device.ElementId} = {FormatLogValue(current.Magnitude)} ∠ {FormatLogValue(Math.Atan2(current.Imaginary, current.Real) * 180.0 / Math.PI)}° A");
        }
        foreach (var branch in ac.BranchCurrents.Where(b => b.IsControlBranch))
        {
            sb.AppendLine($"  控制支路 {branch.ElementId} 电流相量 = {FormatLogValue(branch.Value.Magnitude)} A");
        }
        return sb.ToString();
    }

    // =====================================================================
    // Derivation steps
    // =====================================================================

    private void AppendSteps(
        PedagogicalSolution solution,
        LinearNetworkAnalyzer model,
        LinearSolution primary,
        LinearSolution? dcSolution,
        bool isAcMode,
        double operatingFrequency,
        FrequencyResponseInfo? frequencyResponse,
        AcSweepResult? sweep,
        IReadOnlyList<PortEquivalentInfo> portEquivalents,
        IReadOnlyList<string> boundaryNotes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // ---------- Step 1: matrix formulation ----------
        string matrixLatex = BuildMatrixEquationLatex(model, isAcMode, operatingFrequency, cancellationToken);
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "通用线性网络 MNA 2.0 矩阵方程建立（动态元件与受控源完整印记）",
            MethodName = "改进节点分析法 (MNA)",
            Content = isAcMode
                ? "按 SPICE 改进节点法把电导/导纳印记填入节点方程：电阻 G=1/R、电容导纳 jωC、电感以支路电流变量处理（阻抗 jωL）；" +
                  "独立电压源与受控电压源新增支路电流变量并写入约束行；独立电流源与受控电流源只进入右端注入项。交流源取幅值实数相量，直流源置零（电压源短路、电流源开路）。" +
                  "由此得到复系数矩阵方程 A·x = z，未知量包括全部非地节点电位与支路电流。"
                : "按 SPICE 改进节点法把电导印记填入节点方程：电阻 G=1/R 进入 G 矩阵；独立电压源与受控电压源新增支路电流变量并写入约束行；" +
                  "独立电流源与受控电流源只进入右端注入项。直流模式下电容开路（不产生印记）、电感按 0 V 支路短路、交流源置零。" +
                  "由此得到矩阵方程 A·x = z，未知量包括全部非地节点电位与支路电流。",
            LatexFormula = @"\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix}\begin{bmatrix} \mathbf{v} \\ \mathbf{i} \end{bmatrix} = \begin{bmatrix} \mathbf{J} \\ \mathbf{E} \end{bmatrix}",
            SubstitutionFormula = matrixLatex,
            ResultValue = $"矩阵阶数: {model.MatrixDimension}×{model.MatrixDimension}（节点变量 {model.NodeVariableCount}，支路电流变量 {model.BranchVariableCount}）",
            Status = "Completed",
            MnaLog = solution.RawMnaLog
        });

        // ---------- Step 2: node potentials and branch currents ----------
        var resultBuilder = new StringBuilder();
        foreach (var node in model.Nodes)
        {
            if (isAcMode)
            {
                var phasor = primary.NodePhasor(node.NetId);
                double phase = primary.NodePhaseDeg(node.NetId);
                resultBuilder.Append($@"\hat{{V}}_{{{node.Name}}} = {TeachingDisplay.Latex(phasor.Magnitude, QuantityKind.Voltage)}\angle {TeachingDisplay.LatexNumber(phase, QuantityKind.Ratio)}^\circ,\quad ");
            }
            else
            {
                resultBuilder.Append($@"v_{{{node.Name}}} = {TeachingDisplay.Latex(primary.NodeVoltage(node.NetId), QuantityKind.Voltage)},\quad ");
            }
        }
        foreach (var device in model.Devices.Where(d => d.HasOwnBranchVariable))
        {
            var current = primary.BranchPhasor(device.ElementId);
            resultBuilder.Append($@"i_{{{device.ElementId}}} = {TeachingDisplay.Latex(isAcMode ? current.Magnitude : current.Real, QuantityKind.Current)},\quad ");
        }
        foreach (var branch in primary.BranchCurrents.Where(b => b.IsControlBranch))
        {
            resultBuilder.Append($@"i_{{ctrl,{branch.ElementId}}} = {TeachingDisplay.Latex(isAcMode ? branch.Value.Magnitude : branch.Value.Real, QuantityKind.Current)},\quad ");
        }
        string substitution = resultBuilder.ToString().TrimEnd(',', ' ', ';', '\\');

        string firstNodeSummary = model.Nodes.Count > 0
            ? $"{model.Nodes[0].Name} = {TeachingDisplay.Text(isAcMode ? primary.NodeMagnitude(model.Nodes[0].NetId) : primary.NodeVoltage(model.Nodes[0].NetId), QuantityKind.Voltage)}"
            : "无节点变量";

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "复数主元高斯消元求解节点电位与支路电流",
            MethodName = isAcMode ? "复数相量主元高斯消元" : "实数主元高斯消元",
            Content = "对 MNA 系统矩阵执行按行均衡与部分选主元的复数高斯消去及回代，得到全部非地节点电位与各支路电流" +
                      (isAcMode ? "的相量（幅值与相位）" : "；矩阵奇异、病态或出现非有限值时直接报告失败而不输出伪结果") + "。",
            LatexFormula = @"\mathbf{x} = \mathbf{A}^{-1}\mathbf{z}",
            SubstitutionFormula = substitution,
            ResultValue = firstNodeSummary,
            Status = "Completed",
            MnaLog = solution.RawMnaLog
        });

        // ---------- Step 3: arbitrary-port Thevenin / Norton table ----------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "任意端口戴维南 / 诺顿等效表",
            MethodName = "端口等效电路法",
            Content = "对任意两个电气节点组成的端口：把全部独立源置零（电压源短路、电流源开路），电容开路、电感短路，" +
                      "注入 1 A 直流测试电流求端口电压得到等效电阻 R_eq；保留独立源时端口开路电压即 V_oc。" +
                      $"诺顿等效电流 I_N = V_oc / R_eq。共生成 {portEquivalents.Count} 个端口条目。",
            LatexFormula = @"V_{oc} = V_A - V_B\big|_{\text{open}},\qquad R_{eq} = \frac{V_A - V_B}{1\ \text{A}}\bigg|_{\text{sources zeroed}},\qquad I_N = \frac{V_{oc}}{R_{eq}}",
            SubstitutionFormula = BuildPortTableLatex(portEquivalents),
            ResultValue = $"端口等效条目: {portEquivalents.Count}",
            Status = "Completed"
        });

        // ---------- Step 4: AC phasor and frequency response ----------
        if (frequencyResponse != null)
        {
            string cutoffText = $"下限截止 {frequencyResponse.LowerCutoffDisplay}，上限截止 {frequencyResponse.UpperCutoffDisplay}，带宽 {frequencyResponse.BandwidthDisplay}";
            solution.Steps.Add(new PedagogicalSolutionStep
            {
                StepIndex = 4,
                BadgeText = "第 4 步",
                Title = "交流相量求解与 Bode 频率响应估计",
                MethodName = "正弦稳态相量法 + 对数扫频",
                Content = $"在激励源工作频率 f = {FormatFrequency(operatingFrequency)} 下求复数相量：输入端口 {frequencyResponse.InputPortLabel}，" +
                          $"输出端口 {frequencyResponse.OutputPortLabel}；电压增益 |H| = {frequencyResponse.GainRatioDisplay}（{frequencyResponse.GainDbDisplay}），" +
                          $"相移 {frequencyResponse.PhaseDisplay}。随后在 {FormatFrequency(SweepStartHz)}–{FormatFrequency(SweepStopHz)} 范围按每十倍频程 " +
                          $"{SweepPointsPerDecade} 点扫描，共 {(sweep?.Points.Count ?? frequencyResponse.Points.Count)} 个频点，用 -3 dB 相对最大增益判据估计 {cutoffText}。",
                LatexFormula = @"\hat{H}(j\omega) = \frac{\hat{V}_{out}(j\omega)}{\hat{V}_{in}(j\omega)},\qquad 20\log_{10}\left|\hat{H}\right| [\mathrm{dB}]",
                SubstitutionFormula = BuildFrequencyResponseLatex(frequencyResponse, operatingFrequency),
                ResultValue = $"|H| = {frequencyResponse.GainRatioDisplay}，∠H = {frequencyResponse.PhaseDisplay}",
                Status = "Completed"
            });
        }
        else
        {
            solution.Steps.Add(new PedagogicalSolutionStep
            {
                StepIndex = 4,
                BadgeText = "第 4 步",
                Title = "交流相量与频率响应（本网络不适用）",
                MethodName = "正弦稳态相量法",
                Content = isAcMode
                    ? "网络中虽然存在交流激励，但无法确定独立于激励端口的输出端口或无法完成相量求解，因此不生成频响曲线；具体原因见适用范围说明。"
                    : "本网络未检测到交流电压源或交流电流源，因此没有正弦稳态激励。当前结果严格限定为直流工作点与直流戴维南等效，" +
                      "不编造 Bode 频响或截止频率。如需频率响应，请添加 AC 电压源/电流源后再求解。",
                LatexFormula = @"\text{AC excitation not present}",
                SubstitutionFormula = string.Empty,
                ResultValue = "交流频响: 不适用",
                Status = "Completed"
            });
        }

        // ---------- Step 5: applicability statement ----------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "适用范围与数值边界声明",
            MethodName = "求解器能力边界",
            Content = "本策略使用理想线性模型：R/C/L 为理想元件；独立源为理想电压/电流源；四种受控源为理想线性压控/流控受控源；" +
                      "电容、电感不包含寄生参数，也没有非线性器件。直流模式电容开路、电感短路；交流模式为单一频率的正弦稳态相量。" +
                      "矩阵变量上限 256，奇异/病态/非有限值一律显式失败。以下为本次求解的实际边界：" +
                      (boundaryNotes.Count > 0 ? string.Join("；", boundaryNotes) : "无附加边界。"),
            LatexFormula = @"\mathbf{x} = \mathbf{A}^{-1}\mathbf{z}\quad(\text{linear R/C/L + independent + controlled sources only})",
            SubstitutionFormula = string.Empty,
            ResultValue = "已声明适用器件、符号约定与数值边界",
            Status = "Completed"
        });
    }

    private static string BuildMatrixEquationLatex(
        LinearNetworkAnalyzer model,
        bool isAcMode,
        double operatingFrequency,
        CancellationToken cancellationToken)
    {
        Complex[,]? matrix;
        Complex[]? rhs;
        string error;
        bool assembled = isAcMode
            ? model.TryAssembleAcSystem(operatingFrequency, out matrix, out rhs, out error, cancellationToken)
            : model.TryAssembleDcSystem(out matrix, out rhs, out error, cancellationToken);
        if (!assembled || matrix == null || rhs == null)
        {
            return $@"\text{{MNA matrix unavailable: {EscapeLatexText(error)}}}";
        }

        var labels = model.VariableLabels;
        int dimension = labels.Count;
        int visible = Math.Min(dimension, MaxMatrixDisplayOrder);
        bool truncated = dimension > visible;
        var sb = new StringBuilder();
        sb.Append(@"\begin{bmatrix} ");
        for (int row = 0; row < visible; row++)
        {
            for (int column = 0; column < visible; column++)
            {
                if (column > 0) sb.Append(" & ");
                sb.Append(FormatComplexLatex(matrix[row, column]));
            }
            if (truncated) sb.Append(@" & \cdots");
            sb.Append(@" \\ ");
        }
        if (truncated)
        {
            for (int column = 0; column < visible; column++) sb.Append(@"\vdots & ");
            sb.Append(@"\ddots");
        }
        sb.Append(@" \end{bmatrix} \begin{bmatrix} ");
        for (int i = 0; i < visible; i++)
        {
            if (i > 0) sb.Append(@" \\ ");
            sb.Append(labels[i]);
        }
        if (truncated) sb.Append(@" \\ \vdots");
        sb.Append(@" \end{bmatrix} = \begin{bmatrix} ");
        for (int i = 0; i < visible; i++)
        {
            if (i > 0) sb.Append(@" \\ ");
            sb.Append(FormatComplexLatex(rhs[i]));
        }
        if (truncated) sb.Append(@" \\ \vdots");
        sb.Append(@" \end{bmatrix}");
        if (truncated)
        {
            sb.Append($@"\quad\text{{(matrix order {dimension}; first {visible} rows/columns shown)}}");
        }
        return sb.ToString();
    }

    private static string BuildPortTableLatex(IReadOnlyList<PortEquivalentInfo> ports)
    {
        if (ports.Count == 0) return @"\text{no independent port found}";
        var sb = new StringBuilder();
        sb.Append(@"\begin{array}{c|c|c} \text{Port} & R_{eq} & V_{oc} \\ \hline ");
        foreach (var port in ports)
        {
            string resistance = double.IsPositiveInfinity(port.ResistanceOhm)
                ? @"\infty\ \Omega"
                : double.IsNaN(port.ResistanceOhm)
                    ? @"\text{n/a}"
                    : TeachingDisplay.Latex(port.ResistanceOhm, QuantityKind.Resistance);
            string voltage = port.HasOpenCircuitVoltage
                ? TeachingDisplay.Latex(port.OpenCircuitVoltageV, QuantityKind.Voltage)
                : @"\text{n/a}";
            sb.Append($@"{port.PositiveNet} - {port.NegativeNet} & {resistance} & {voltage} \\ ");
        }
        sb.Append(@"\end{array}");
        return sb.ToString();
    }

    private static string BuildFrequencyResponseLatex(FrequencyResponseInfo response, double operatingFrequency)
    {
        var sb = new StringBuilder();
        sb.Append($@"\hat{{H}}(j2\pi\cdot {TeachingDisplay.Latex(operatingFrequency, QuantityKind.Frequency)}) = ");
        sb.Append($@"{ScientificNotationFormatter.ToLatexScientific(response.GainRatio)}\angle {TeachingDisplay.LatexNumber(response.PhaseDeg, QuantityKind.Ratio)}^\circ");
        if (response.LowerCutoffHz.HasValue)
        {
            sb.Append($@",\quad f_L = {TeachingDisplay.Latex(response.LowerCutoffHz.Value, QuantityKind.Frequency)}");
        }
        if (response.UpperCutoffHz.HasValue)
        {
            sb.Append($@",\quad f_H = {TeachingDisplay.Latex(response.UpperCutoffHz.Value, QuantityKind.Frequency)}");
        }
        if (response.BandwidthHz.HasValue)
        {
            sb.Append($@",\quad BW = {TeachingDisplay.Latex(response.BandwidthHz.Value, QuantityKind.Frequency)}");
        }
        return sb.ToString();
    }

    private static string FormatComplexLatex(Complex value)
    {
        double magnitude = value.Magnitude;
        if (magnitude < 1e-300) return "0";
        if (Math.Abs(value.Imaginary) < 1e-12 * Math.Max(1.0, Math.Abs(value.Real)))
        {
            return ScientificNotationFormatter.ToLatexScientific(value.Real);
        }
        string sign = value.Imaginary >= 0 ? "+" : "-";
        return $@"({ScientificNotationFormatter.ToLatexScientific(value.Real)} {sign} " +
               $@"{ScientificNotationFormatter.ToLatexScientific(Math.Abs(value.Imaginary))}\mathrm{{j}})";
    }

    private static string EscapeLatexText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            // Keep the generated LaTeX safe: drop math-mode specials instead of
            // half-escaping them (error text is diagnostic only).
            sb.Append(ch switch
            {
                '{' or '}' or '\\' or '&' or '%' or '#' or '$' or '^' or '~' or '_' => ' ',
                _ => ch
            });
        }
        return sb.ToString().Trim();
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    private static string NetNameOf(LinearNetworkAnalyzer model, int netId)
    {
        if (netId == model.GroundNetId) return model.GroundNetName;
        var node = model.Nodes.FirstOrDefault(n => n.NetId == netId);
        return node?.Name ?? $"N{netId}";
    }

    private static string FormatFrequency(double frequencyHz)
    {
        return TeachingDisplay.Text(frequencyHz, QuantityKind.Frequency);
    }

    /// <summary>
    /// Raw MNA log value: at most two decimals, using scientific notation when the
    /// magnitude is tiny or huge so the log keeps useful information.
    /// </summary>
    private static string FormatLogValue(double value)
    {
        return ScientificNotationFormatter.ToUnicodeScientific(value);
    }
}
