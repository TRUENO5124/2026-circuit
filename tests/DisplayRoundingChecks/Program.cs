// ============================================================================
// File: tests/DisplayRoundingChecks/Program.cs
// Description: Console regression harness for the teaching display convention
//              (2026-10-18): EVERY displayed computed result keeps AT MOST TWO
//              decimal places (rounded AwayFromZero). This is a display rule, not
//              an algebraic simplification — formulas keep all terms.
//
//              The harness builds/loads representative teaching circuits of every
//              strategy family, solves them, then scans every display text field
//              (summary, Q point, AC parameters, derivation steps, MNA log, linear
//              network node/port/frequency-response displays) for numbers with
//              three or more decimal places. Structured Values dictionaries keep
//              full precision and are deliberately NOT scanned. A few key
//              quantities are also asserted literally (text + textbook unit).
//
//              NOT a dotnet-test project. Run with:
//                dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace DisplayRoundingChecks;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    /// <summary>Any number with 3+ digits after the decimal point ("0.707", "1.2345").</summary>
    private static readonly Regex ThreeDecimalPlaces = new(
        @"(?<![\d.])[-\u2212]?(?:\d*\.\d{3,})(?!\d)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may be locked down */ }
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("教学结果显示精度回归：所有显示文本最多两位小数（AwayFromZero）");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        Check(ThreeDecimalPlaces.IsMatch("τ = 1.234 s"), "扫描器自检：能识别 3 位小数");
        Check(!ThreeDecimalPlaces.IsMatch("τ = 1.23 s") &&
              !ThreeDecimalPlaces.IsMatch("1.23 × 10⁻⁵") &&
              !ThreeDecimalPlaces.IsMatch(@"s_{1, 2} = -2500 \pm j9682.46") &&
              !ThreeDecimalPlaces.IsMatch(@"\frac{1}{\sqrt{2}}"),
            "扫描器自检：2 位小数 / 科学计数法 / LaTeX 下标与命令不误报");

        RunManualScenario("RC 一阶低通（AC 分压）", BuildRcLowPass(), CircuitTopologyType.FirstOrderRc, CheckRcLowPass);
        RunManualScenario("RL 一阶电路（DC 阶跃）", BuildRlStep(), CircuitTopologyType.FirstOrderRl, CheckRlStep);
        RunManualScenario("串联 RLC（欠阻尼）", BuildSeriesRlc(), CircuitTopologyType.SecondOrderRlc, CheckSeriesRlc);
        RunManualScenario("并联 RLC（欠阻尼）", BuildParallelRlc(), CircuitTopologyType.SecondOrderRlc, CheckParallelRlc);
        RunManualScenario("MNA 直流分压网络", BuildMnaDivider(), CircuitTopologyType.MnaLinear, CheckMnaDivider);
        RunManualScenario("MNA 受控源网络（VCVS）", BuildMnaVcvs(), CircuitTopologyType.MnaLinear, CheckMnaVcvs);
        RunManualScenario("二极管直流偏置", BuildDiodeBias(), CircuitTopologyType.DiodeCircuit, CheckDiodeBias);

        RunFileScenario("二极管半波整流（D01 教学文件）", "D01-half-wave-rectifier.circuit", CheckDiodeRectifier);
        RunFileScenario("二极管串联限幅（D05 教学文件）", "D05-series-limiter.circuit", CheckDiodeLimiter);
        RunFileScenario("BJT 分压偏置放大电路（B02 教学文件）", "B02-voltage-divider-bias.circuit", CheckBjtTemplate);
        RunFileScenario("BJT 共集电极放大电路（B10 教学文件）", "B10-common-collector.circuit", CheckBjtTemplate);
        RunFileScenario("MOSFET 共源放大电路（M01 教学文件）", "M01-nmos-common-source.circuit", CheckMosTemplate);

        Console.WriteLine();
        Console.WriteLine($"Total={_passed + _failed} Passed={_passed} Failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // =====================================================================
    // Scenario runners
    // =====================================================================

    private static void RunManualScenario(
        string name,
        ElectricalNetlist netlist,
        CircuitTopologyType expectedTopology,
        Action<PedagogicalSolution>? spotChecks = null)
    {
        try
        {
            ClassificationResult classification = TopologyClassifier.Classify(netlist);
            Check(classification.TopologyType == expectedTopology,
                $"{name}：分类为 {expectedTopology}",
                $"实际 {classification.TopologyType}/{classification.SubtypeName}");

            PedagogicalSolution solution = classification.Strategy
                .SolveAsync(netlist, new AnalysisParameters())
                .GetAwaiter()
                .GetResult();
            Check(solution.IsSuccess, $"{name}：求解成功", solution.SummaryText);
            if (!solution.IsSuccess) return;

            ScanAllDisplayTexts(name, solution);
            spotChecks?.Invoke(solution);
        }
        catch (Exception ex)
        {
            Check(false, $"{name}：运行异常", ex.Message);
        }
    }

    private static void RunFileScenario(string name, string fileName, Action<PedagogicalSolution>? spotChecks = null)
    {
        try
        {
            PedagogicalSolution solution = SolveCircuitFile(fileName);
            Check(solution.IsSuccess, $"{name}：求解成功", solution.SummaryText);
            if (!solution.IsSuccess) return;

            ScanAllDisplayTexts(name, solution);
            spotChecks?.Invoke(solution);
        }
        catch (Exception ex)
        {
            Check(false, $"{name}：运行异常", ex.Message);
        }
    }

    // =====================================================================
    // Fixtures (manual netlists)
    // =====================================================================

    /// <summary>AC V1 = 1 V / 1 kHz, R = 1 kΩ, C = 100 nF: τ = 100 μs, fc = 1.59 kHz.</summary>
    private static ElectricalNetlist BuildRcLowPass() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.AcVoltageSource, 1, "V")
        .Element("R1", ElementType.Resistor, 1, "kΩ")
        .Element("C1", ElementType.Capacitor, 100, "nF")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("C1", "1", 2).Pin("C1", "2", 0)
        .Build();

    /// <summary>DC V1 = 10 V, R = 100 Ω, L = 500 mH: τ = 5 ms, iL(3τ) = 95.02 mA.</summary>
    private static ElectricalNetlist BuildRlStep() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.DcVoltageSource, 10, "V")
        .Element("R1", ElementType.Resistor, 100, "Ω")
        .Element("L1", ElementType.Inductor, 500, "mH")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("L1", "1", 2).Pin("L1", "2", 0)
        .Build();

    /// <summary>R = 50 Ω, L = 10 mH, C = 1 μF: ω0 = 10000 rad/s, Q = 2, BW = 795.77 Hz.</summary>
    private static ElectricalNetlist BuildSeriesRlc() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.DcVoltageSource, 1, "V")
        .Element("R1", ElementType.Resistor, 50, "Ω")
        .Element("L1", ElementType.Inductor, 10, "mH")
        .Element("C1", ElementType.Capacitor, 1, "μF")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("L1", "1", 2).Pin("L1", "2", 3)
        .Pin("C1", "1", 3).Pin("C1", "2", 0)
        .Build();

    /// <summary>Parallel RLC, I = 1 mA, R = 1 kΩ, L = 100 mH, C = 10 μF: ω0 = 1000 rad/s, Q = 10.</summary>
    private static ElectricalNetlist BuildParallelRlc() => new FixtureNetlistBuilder()
        .Element("I1", ElementType.DcCurrentSource, 1, "mA")
        .Element("R1", ElementType.Resistor, 1, "kΩ")
        .Element("L1", ElementType.Inductor, 100, "mH")
        .Element("C1", ElementType.Capacitor, 10, "μF")
        .Pin("I1", "in", 1).Pin("I1", "out", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 0)
        .Pin("L1", "1", 1).Pin("L1", "2", 0)
        .Pin("C1", "1", 1).Pin("C1", "2", 0)
        .Build();

    /// <summary>V = 12 V, R1 = 1 kΩ, R2 = 2 kΩ: V(N2) = 8 V, output port Req = 666.67 Ω.</summary>
    private static ElectricalNetlist BuildMnaDivider() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.DcVoltageSource, 12, "V")
        .Element("R1", ElementType.Resistor, 1, "kΩ")
        .Element("R2", ElementType.Resistor, 2, "kΩ")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("R2", "1", 2).Pin("R2", "2", 0)
        .Build();

    /// <summary>V = 10 V, E1 gain 2 (VCVS), R1 = 1 kΩ, R2 = 1 kΩ: V(N3) = 20 V.</summary>
    private static ElectricalNetlist BuildMnaVcvs() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.DcVoltageSource, 10, "V")
        .Element("R1", ElementType.Resistor, 1, "kΩ")
        .Element("E1", ElementType.Vcvs, 2, "")
        .Element("R2", ElementType.Resistor, 1, "kΩ")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("E1", "IN1", 2).Pin("E1", "IN2", 0)
        .Pin("E1", "OUT1", 3).Pin("E1", "OUT2", 0)
        .Pin("R2", "1", 3).Pin("R2", "2", 0)
        .Build();

    /// <summary>V = 5 V, R = 1 kΩ, silicon diode to ground: Shockley DC bias solves ID/VD.</summary>
    private static ElectricalNetlist BuildDiodeBias() => new FixtureNetlistBuilder()
        .Element("V1", ElementType.DcVoltageSource, 5, "V")
        .Element("R1", ElementType.Resistor, 1, "kΩ")
        .Element("D1", ElementType.Diode, 0.7, "V")
        .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
        .Pin("R1", "1", 1).Pin("R1", "2", 2)
        .Pin("D1", "A", 2).Pin("D1", "K", 0)
        .Build();

    // =====================================================================
    // Spot checks: 2-3 key quantities per family, asserted literally (text+unit)
    // =====================================================================

    private static void CheckRcLowPass(PedagogicalSolution solution)
    {
        PedagogicalSolutionStep? step2 = FindStep(solution, "三要素");
        Check(step2?.ResultValue == "τ = 100 μs",
            "RC 低通：时间常数显示为 'τ = 100 μs'（教材单位）", step2?.ResultValue ?? "缺少三要素步骤");
        Check(solution.AcParameters?.UpperCutoffFreq == "1.59 kHz",
            "RC 低通：截止频率显示为 '1.59 kHz'", solution.AcParameters?.UpperCutoffFreq ?? "null");
    }

    private static void CheckRlStep(PedagogicalSolution solution)
    {
        PedagogicalSolutionStep? step2 = FindStep(solution, "三要素");
        Check(step2?.ResultValue == "τ = 5 ms",
            "RL 电路：时间常数显示为 'τ = 5 ms'", step2?.ResultValue ?? "缺少三要素步骤");
        PedagogicalSolutionStep? step3 = FindStep(solution, "微分方程");
        Check(step3 != null && step3.ResultValue.Contains("95.02 mA", StringComparison.Ordinal),
            "RL 电路：iL(3τ) 显示为 '95.02 mA'", step3?.ResultValue ?? "缺少微分方程步骤");
    }

    private static void CheckSeriesRlc(PedagogicalSolution solution)
    {
        Check(solution.SummaryText.Contains("ω0 = 10000 rad/s", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("品质因数 Q = 2。", StringComparison.Ordinal),
            "串联 RLC：摘要显示 'ω0 = 10000 rad/s' 与 'Q = 2'", solution.SummaryText);
        PedagogicalSolutionStep? step5 = FindStep(solution, "通频带");
        Check(step5?.ResultValue == "BW = 795.77 Hz",
            "串联 RLC：带宽显示为 'BW = 795.77 Hz'", step5?.ResultValue ?? "缺少通频带步骤");
    }

    private static void CheckParallelRlc(PedagogicalSolution solution)
    {
        Check(solution.SummaryText.Contains("ω0 = 1000 rad/s", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("品质因数 Q = 10。", StringComparison.Ordinal),
            "并联 RLC：摘要显示 'ω0 = 1000 rad/s' 与 'Q = 10'", solution.SummaryText);
        PedagogicalSolutionStep? step5 = FindStep(solution, "通频带");
        Check(step5?.ResultValue == "BW = 15.92 Hz",
            "并联 RLC：带宽显示为 'BW = 15.92 Hz'", step5?.ResultValue ?? "缺少通频带步骤");
    }

    private static void CheckMnaDivider(PedagogicalSolution solution)
    {
        LinearNetworkInfo? network = solution.LinearNetwork;
        NodePotentialInfo? node = network?.NodePotentials.FirstOrDefault(n => n.NetName == "N2");
        Check(node?.Display == "8 V", "MNA 分压：节点 N2 显示为 '8 V'", node?.Display ?? "缺少 N2 节点");
        PortEquivalentInfo? port = network?.PortEquivalents
            .FirstOrDefault(p => p.PositiveNet == "N2" && p.NegativeNet == "GND");
        Check(port?.ResistanceDisplay == "666.67 Ω" && port.OpenCircuitVoltageDisplay == "8 V",
            "MNA 分压：输出端口戴维南等效显示 '666.67 Ω / 8 V'",
            port == null ? "缺少输出端口" : $"{port.ResistanceDisplay} / {port.OpenCircuitVoltageDisplay}");
        Check(solution.AcParameters?.VoltageGain == "DC 电压比 0.67（V_out = 8 V）",
            "MNA 分压：直流电压比显示 'DC 电压比 0.67（V_out = 8 V）'",
            solution.AcParameters?.VoltageGain ?? "null");
    }

    private static void CheckMnaVcvs(PedagogicalSolution solution)
    {
        LinearNetworkInfo? network = solution.LinearNetwork;
        NodePotentialInfo? node = network?.NodePotentials.FirstOrDefault(n => n.NetName == "N3");
        Check(node?.Display == "20 V", "MNA 受控源：VCVS 输出节点 N3 显示为 '20 V'", node?.Display ?? "缺少 N3 节点");
        Check(network != null && network.ModelSummary.Contains("MNA", StringComparison.Ordinal),
            "MNA 受控源：模型摘要说明 MNA 模型", network?.ModelSummary ?? "null");
    }

    private static void CheckDiodeBias(PedagogicalSolution solution)
    {
        Check(solution.SummaryText.Contains("ID=", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("VD=", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("状态=", StringComparison.Ordinal),
            "二极管直流偏置：摘要含 Shockley 工作点 ID/VD/状态", solution.SummaryText);
    }

    private static void CheckDiodeRectifier(PedagogicalSolution solution)
    {
        Check(solution.SummaryText.Contains("二极管", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("Vavg", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("PIV", StringComparison.Ordinal),
            "二极管半波整流：摘要含 Vavg/PIV 指标", solution.SummaryText);
    }

    private static void CheckDiodeLimiter(PedagogicalSolution solution)
    {
        Check(solution.SummaryText.Contains("门限", StringComparison.Ordinal) &&
              solution.SummaryText.Contains("Vth", StringComparison.Ordinal),
            "二极管串联限幅：摘要含门限电平 Vth", solution.SummaryText);
    }

    private static void CheckBjtTemplate(PedagogicalSolution solution)
    {
        Check(solution.QPoint != null &&
              !string.IsNullOrWhiteSpace(solution.QPoint.Ibq) && solution.QPoint.Ibq != "—" &&
              !string.IsNullOrWhiteSpace(solution.QPoint.Icq) && solution.QPoint.Icq != "—" &&
              !string.IsNullOrWhiteSpace(solution.QPoint.Vceq) && solution.QPoint.Vceq != "—",
            "BJT 教学模板：Q 点三项均有显示值",
            solution.QPoint == null ? "QPoint=null" : $"{solution.QPoint.Ibq} / {solution.QPoint.Icq} / {solution.QPoint.Vceq}");
        Check(solution.AcParameters != null &&
              !string.IsNullOrWhiteSpace(solution.AcParameters.VoltageGain) && solution.AcParameters.VoltageGain != "—",
            "BJT 教学模板：电压增益有显示值", solution.AcParameters?.VoltageGain ?? "AcParameters=null");
    }

    private static void CheckMosTemplate(PedagogicalSolution solution)
    {
        Check(solution.QPoint != null &&
              !string.IsNullOrWhiteSpace(solution.QPoint.Icq) && solution.QPoint.Icq != "—" &&
              !string.IsNullOrWhiteSpace(solution.QPoint.Vceq) && solution.QPoint.Vceq != "—",
            "MOSFET 教学模板：漏极电流/压降有显示值",
            solution.QPoint == null ? "QPoint=null" : $"{solution.QPoint.Icq} / {solution.QPoint.Vceq}");
        Check(solution.AcParameters != null &&
              !string.IsNullOrWhiteSpace(solution.AcParameters.VoltageGain) && solution.AcParameters.VoltageGain != "—",
            "MOSFET 教学模板：电压增益有显示值", solution.AcParameters?.VoltageGain ?? "AcParameters=null");
    }

    // =====================================================================
    // Display-text scan
    // =====================================================================

    private static void ScanAllDisplayTexts(string scenario, PedagogicalSolution solution)
    {
        var hits = new List<string>();
        foreach ((string field, string? text) in EnumerateDisplayTexts(solution))
        {
            if (string.IsNullOrEmpty(text)) continue;
            Match match = ThreeDecimalPlaces.Match(text);
            if (match.Success)
            {
                hits.Add($"{field} → '{match.Value}'");
            }
        }

        Check(hits.Count == 0,
            $"{scenario}：全部显示文本最多两位小数",
            hits.Count == 0 ? string.Empty : string.Join("；", hits.Take(8)));
    }

    private static IEnumerable<(string Field, string? Text)> EnumerateDisplayTexts(PedagogicalSolution solution)
    {
        yield return ("SummaryText", solution.SummaryText);
        yield return ("CircuitTitle", solution.CircuitTitle);

        if (solution.QPoint != null)
        {
            yield return ("QPoint.Ibq", solution.QPoint.Ibq);
            yield return ("QPoint.Icq", solution.QPoint.Icq);
            yield return ("QPoint.Vceq", solution.QPoint.Vceq);
            yield return ("QPoint.WorkingRegion", solution.QPoint.WorkingRegion);
        }

        if (solution.AcParameters != null)
        {
            yield return ("AcParameters.VoltageGain", solution.AcParameters.VoltageGain);
            yield return ("AcParameters.InputResistance", solution.AcParameters.InputResistance);
            yield return ("AcParameters.OutputResistance", solution.AcParameters.OutputResistance);
            yield return ("AcParameters.LowerCutoffFreq", solution.AcParameters.LowerCutoffFreq);
            yield return ("AcParameters.UpperCutoffFreq", solution.AcParameters.UpperCutoffFreq);
            yield return ("AcParameters.Bandwidth", solution.AcParameters.Bandwidth);
        }

        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            string prefix = $"Step{step.StepIndex}";
            yield return ($"{prefix}.Title", step.Title);
            yield return ($"{prefix}.MethodName", step.MethodName);
            yield return ($"{prefix}.Content", step.Content);
            yield return ($"{prefix}.LatexFormula", step.LatexFormula);
            yield return ($"{prefix}.SubstitutionFormula", step.SubstitutionFormula);
            yield return ($"{prefix}.ResultValue", step.ResultValue);
            yield return ($"{prefix}.MnaLog", step.MnaLog);
        }

        yield return ("RawMnaLog", solution.RawMnaLog);

        LinearNetworkInfo? network = solution.LinearNetwork;
        if (network == null) yield break;

        yield return ("LinearNetwork.ModelSummary", network.ModelSummary);
        yield return ("LinearNetwork.OperatingMode", network.OperatingMode);
        yield return ("LinearNetwork.BoundaryNote", network.BoundaryNote);

        foreach (NodePotentialInfo node in network.NodePotentials)
        {
            yield return ($"Node[{node.NetName}].Display", node.Display);
            yield return ($"Node[{node.NetName}].LatexFormula", node.LatexFormula);
        }

        foreach (PortEquivalentInfo port in network.PortEquivalents)
        {
            yield return ($"Port[{port.PortLabel}].ResistanceDisplay", port.ResistanceDisplay);
            yield return ($"Port[{port.PortLabel}].OpenCircuitVoltageDisplay", port.OpenCircuitVoltageDisplay);
            yield return ($"Port[{port.PortLabel}].DetailDisplay", port.DetailDisplay);
            yield return ($"Port[{port.PortLabel}].Note", port.Note);
        }

        FrequencyResponseInfo? response = network.FrequencyResponse;
        if (response == null) yield break;

        yield return ("FrequencyResponse.InputPortLabel", response.InputPortLabel);
        yield return ("FrequencyResponse.OutputPortLabel", response.OutputPortLabel);
        yield return ("FrequencyResponse.GainRatioDisplay", response.GainRatioDisplay);
        yield return ("FrequencyResponse.GainDbDisplay", response.GainDbDisplay);
        yield return ("FrequencyResponse.PhaseDisplay", response.PhaseDisplay);
        yield return ("FrequencyResponse.LowerCutoffDisplay", response.LowerCutoffDisplay);
        yield return ("FrequencyResponse.UpperCutoffDisplay", response.UpperCutoffDisplay);
        yield return ("FrequencyResponse.BandwidthDisplay", response.BandwidthDisplay);
        yield return ("FrequencyResponse.BoundaryNote", response.BoundaryNote);
        foreach (AcSweepPointInfo point in response.Points)
        {
            yield return ($"SweepPoint[{point.FrequencyDisplay}].FrequencyDisplay", point.FrequencyDisplay);
            yield return ($"SweepPoint[{point.FrequencyDisplay}].GainDbDisplay", point.GainDbDisplay);
            yield return ($"SweepPoint[{point.FrequencyDisplay}].PhaseDisplay", point.PhaseDisplay);
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static PedagogicalSolutionStep? FindStep(PedagogicalSolution solution, string titleFragment) =>
        solution.Steps.FirstOrDefault(s => s.Title.Contains(titleFragment, StringComparison.Ordinal));

    private static PedagogicalSolution SolveCircuitFile(string fileName)
    {
        string? directory = FindCircuitModelsDirectory()
            ?? throw new DirectoryNotFoundException("找不到 examples/circuit-models 目录。");
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("缺少教学电路文件：" + fileName, path);

        CircuitProjectData data = JsonSerializer.Deserialize<CircuitProjectData>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("无法解析教学电路文件：" + fileName);
        data.Validate();
        (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
        ElectricalNetlist netlist = NetlistExtractor.Extract(elements, wires);
        NetlistValidationResult validation = netlist.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(fileName + " 网表校验失败：" +
                string.Join(" | ", validation.Issues
                    .Where(i => i.Severity == NetlistValidationSeverity.Error)
                    .Select(i => i.Message)));
        }

        return TopologyClassifier.Classify(netlist).Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
    }

    private static string? FindCircuitModelsDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "examples", "circuit-models"),
            Path.Combine(AppContext.BaseDirectory, "examples", "circuit-models")
        };
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current != null; i++)
        {
            candidates.Add(Path.Combine(current.FullName, "examples", "circuit-models"));
            current = current.Parent;
        }
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static void Check(bool condition, string name, string detail = "")
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : " -> " + detail)}");
        }
    }
}
