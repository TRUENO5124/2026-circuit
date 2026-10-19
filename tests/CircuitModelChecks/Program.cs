// ============================================================================
// File: tests/CircuitModelChecks/Program.cs
// Description: Verifies the 29 native teaching circuit files under
//              examples/circuit-models/ (D01-D07, B01-B16, M01-M06).
//
//              For every file:
//                1. deserialize with the same JSON options as
//                   Services/ProjectManagerService.cs;
//                2. CircuitProjectData.Validate -> ToModel;
//                3. NetlistExtractor.Extract -> TopologyClassifier.Classify;
//                4. compare classification with the documented expectation;
//                5. run the strategy and check the documented success/failure
//                   outcome (multi-stage files must fail with the future-plan
//                   message; everything else must succeed with >= 4 steps).
//
//              Run with:
//                dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModelChecks;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Expectation(
        string FileName,
        CircuitTopologyType Topology,
        string Subtype,
        bool SolveSucceeds);

    // Documented in projects/nonlinear-engine-and-circuit-models-design-20261018.md §9.
    private static readonly Expectation[] Expectations =
    [
        new("D01-half-wave-rectifier.circuit", CircuitTopologyType.DiodeCircuit, "HalfWaveRectifier", true),
        new("D02-half-wave-rectifier-filtered.circuit", CircuitTopologyType.DiodeCircuit, "HalfWaveRectifierFiltered", true),
        new("D03-bridge-rectifier.circuit", CircuitTopologyType.DiodeCircuit, "BridgeRectifier", true),
        new("D04-bridge-rectifier-filtered.circuit", CircuitTopologyType.DiodeCircuit, "BridgeRectifierFiltered", true),
        new("D05-series-limiter.circuit", CircuitTopologyType.DiodeCircuit, "SeriesLimiter", true),
        new("D06-double-limiter.circuit", CircuitTopologyType.DiodeCircuit, "DoubleLimiter", true),
        new("D07-clamper.circuit", CircuitTopologyType.DiodeCircuit, "Clamper", true),
        new("B01-fixed-bias-common-emitter.circuit", CircuitTopologyType.BjtAmplifier, "FixedBias", true),
        new("B02-voltage-divider-bias.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B03-voltage-divider-bypassed.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B04-collector-feedback-bias.circuit", CircuitTopologyType.BjtAmplifier, "CollectorFeedbackBias", true),
        new("B05-common-base-bias.circuit", CircuitTopologyType.BjtAmplifier, "CommonBase", true),
        new("B06-pnp-voltage-divider-bias.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B07-common-emitter-bypassed.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B08-common-emitter-unbypassed.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B09-common-emitter-loaded.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B10-common-collector.circuit", CircuitTopologyType.BjtAmplifier, "CommonCollector", true),
        new("B11-common-base-amplifier.circuit", CircuitTopologyType.BjtAmplifier, "CommonBase", true),
        new("B12-rc-coupled-two-stage.circuit", CircuitTopologyType.MultiStageAmplifier, "Cascade", false),
        new("B13-direct-coupled-two-stage.circuit", CircuitTopologyType.MultiStageAmplifier, "Cascade", false),
        new("B14-ce-cc-two-stage.circuit", CircuitTopologyType.MultiStageAmplifier, "Cascade", false),
        new("B15-low-frequency-response.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("B16-high-frequency-response.circuit", CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", true),
        new("M01-nmos-common-source.circuit", CircuitTopologyType.MosfetAmplifier, "CommonSource", true),
        new("M02-nmos-common-source-rs.circuit", CircuitTopologyType.MosfetAmplifier, "CommonSourceWithSourceResistance", true),
        new("M03-nmos-common-source-bypassed.circuit", CircuitTopologyType.MosfetAmplifier, "CommonSourceBypassed", true),
        new("M04-nmos-common-drain.circuit", CircuitTopologyType.MosfetAmplifier, "CommonDrain", true),
        new("M05-pmos-common-source.circuit", CircuitTopologyType.MosfetAmplifier, "CommonSource", true),
        new("M06-nmos-common-gate.circuit", CircuitTopologyType.MosfetAmplifier, "CommonGate", true)
    ];

    private static int _passed;
    private static int _failed;
    private static bool _details;

    private static int Main(string[] args)
    {
        _details = args.Any(a => string.Equals(a, "--details", StringComparison.OrdinalIgnoreCase));
        string? inspectFile = ReadOptionValue(args, "--file");
        if (!string.IsNullOrWhiteSpace(inspectFile)) return InspectFile(inspectFile!);
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may be locked down */ }
        Console.WriteLine("================================================================================");
        Console.WriteLine("29 个教学电路工程文件校验（D01-D07 / B01-B16 / M01-M06）");
        Console.WriteLine("================================================================================");

        string? directory = FindCircuitModelsDirectory();
        if (directory == null)
        {
            Console.WriteLine("[FAIL] 找不到 examples/circuit-models 目录。");
            return 1;
        }
        Console.WriteLine($"目录：{directory}");

        string[] actualFiles = Directory.GetFiles(directory, "*.circuit")
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Check(actualFiles.Length == Expectations.Length,
            $"文件数量为 {Expectations.Length}",
            $"实际 {actualFiles.Length} 个：{string.Join(", ", actualFiles)}");

        foreach (Expectation expectation in Expectations)
        {
            string path = Path.Combine(directory, expectation.FileName);
            if (!File.Exists(path))
            {
                Check(false, expectation.FileName, "文件不存在");
                continue;
            }

            try
            {
                CheckFile(path, expectation);
            }
            catch (Exception ex)
            {
                Check(false, $"{expectation.FileName} 运行异常", ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Total={_passed + _failed} Passed={_passed} Failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void CheckFile(string path, Expectation expectation)
    {
        string json = File.ReadAllText(path);
        CircuitProjectData? data = JsonSerializer.Deserialize<CircuitProjectData>(json, JsonOptions);
        Check(data != null, $"{expectation.FileName} 反序列化", "数据为空");
        if (data == null) return;

        data.Validate();
        Check(true, $"{expectation.FileName} Validate");
        Check(data.Version is "1.2.0", $"{expectation.FileName} 版本兼容", $"Version={data.Version}");

        (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
        ElectricalNetlist netlist = NetlistExtractor.Extract(elements, wires);
        NetlistValidationResult validation = netlist.Validate();
        Check(validation.IsValid, $"{expectation.FileName} 网表校验",
            string.Join("; ", validation.Issues.Select(i => i.Message)));

        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        Check(classification.TopologyType == expectation.Topology,
            $"{expectation.FileName} 分类类型为 {expectation.Topology}",
            $"实际 {classification.TopologyType}");
        Check(classification.SubtypeName == expectation.Subtype,
            $"{expectation.FileName} 子类型为 {expectation.Subtype}",
            $"实际 {classification.SubtypeName}");

        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();

        if (expectation.SolveSucceeds)
        {
            Check(solution.IsSuccess, $"{expectation.FileName} 求解成功", solution.SummaryText);
            if (solution.IsSuccess)
            {
                Check(solution.Steps.Count >= 4, $"{expectation.FileName} 推导步骤 >= 4",
                    $"实际 {solution.Steps.Count} 步");
                Check(!string.IsNullOrWhiteSpace(solution.SummaryText),
                    $"{expectation.FileName} 摘要非空");
                if (_details) PrintDetails(expectation.FileName, solution);
            }
        }
        else
        {
            Check(!solution.IsSuccess, $"{expectation.FileName} 按计划拒绝求解", solution.SummaryText);
            Check(solution.SummaryText.Contains("多级", StringComparison.Ordinal) &&
                  solution.SummaryText.Contains("后续开发规划", StringComparison.Ordinal),
                $"{expectation.FileName} 拒绝信息说明多级后续规划", solution.SummaryText);
        }
    }

    private static void PrintDetails(string fileName, PedagogicalSolution solution)
    {
        Console.WriteLine($"        · 摘要：{solution.SummaryText}");
        if (solution.QPoint != null)
            Console.WriteLine($"        · Q点：IBQ={solution.QPoint.Ibq} ICQ={solution.QPoint.Icq} VCEQ={solution.QPoint.Vceq} 工作区={solution.QPoint.WorkingRegion}");
        if (solution.AcParameters != null)
            Console.WriteLine($"        · 交流：Av={solution.AcParameters.VoltageGain} Ri={solution.AcParameters.InputResistance} Ro={solution.AcParameters.OutputResistance} fL={solution.AcParameters.LowerCutoffFreq} fH={solution.AcParameters.UpperCutoffFreq}");
        foreach (PedagogicalSolutionStep step in solution.Steps)
            Console.WriteLine($"        · 步骤{step.StepIndex} {step.Title} → {step.ResultValue}");
    }

    // ------------------------------------------------------------------
    // --file <path>: inspect any native .circuit project (diagnostics helper).
    // ------------------------------------------------------------------

    private static string? ReadOptionValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int InspectFile(string path)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("单文件分析（--file）：加载 → 校验 → 网表 → 分类 → 求解，打印全部推导步骤");
        Console.WriteLine("================================================================================");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[FAIL] 文件不存在：{path}");
            return 2;
        }

        CircuitProjectData data = JsonSerializer.Deserialize<CircuitProjectData>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("无法解析工程 JSON。");
        data.Validate();
        (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
        ElectricalNetlist netlist = NetlistExtractor.Extract(elements, wires);
        NetlistValidationResult validation = netlist.Validate();
        Console.WriteLine($"工程：{data.ProjectName}（{data.ProjectFileName}，版本 {data.Version}）");
        Console.WriteLine($"元件 {elements.Count} 个 / 导线 {wires.Count} 根 / 网表校验：{(validation.IsValid ? "通过" : "失败")}");
        if (!validation.IsValid)
        {
            foreach (NetlistValidationIssue issue in validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Error))
                Console.WriteLine($"  [错误] {issue.Message}");
            return 2;
        }

        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        Console.WriteLine($"分类：{classification.TopologyType} / {classification.SubtypeName}（{classification.Description}）");
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
        Console.WriteLine($"求解：{(solution.IsSuccess ? "成功" : "失败")}");
        if (!solution.IsSuccess)
        {
            Console.WriteLine($"失败原因：{solution.SummaryText}");
            return 1;
        }

        Console.WriteLine($"摘要：{solution.SummaryText}");
        if (solution.QPoint != null)
            Console.WriteLine($"Q点：IBQ={solution.QPoint.Ibq} | ICQ={solution.QPoint.Icq} | VCEQ={solution.QPoint.Vceq} | 工作区={solution.QPoint.WorkingRegion}");
        if (solution.AcParameters != null)
            Console.WriteLine($"交流：Av={solution.AcParameters.VoltageGain} | Ri={solution.AcParameters.InputResistance} | Ro={solution.AcParameters.OutputResistance} | fL={solution.AcParameters.LowerCutoffFreq} | fH={solution.AcParameters.UpperCutoffFreq} | BW={solution.AcParameters.Bandwidth}");
        Console.WriteLine();
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            Console.WriteLine($"--- 步骤 {step.StepIndex}：{step.Title} [{step.BadgeText}] ---");
            Console.WriteLine($"方法：{step.MethodName}");
            Console.WriteLine($"内容：{step.Content}");
            Console.WriteLine($"公式：{step.LatexFormula}");
            Console.WriteLine($"代入：{step.SubstitutionFormula}");
            Console.WriteLine($"结果：{step.ResultValue}");
            if (step.Values.Count > 0)
                Console.WriteLine("数值：" + string.Join("；", step.Values.Select(kv => $"{kv.Key}={kv.Value}")));
            Console.WriteLine();
        }
        return 0;
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
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
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
            Console.WriteLine($"  [FAIL] {name}{(string.IsNullOrWhiteSpace(detail) ? "" : " -> " + detail)}");
        }
    }
}
