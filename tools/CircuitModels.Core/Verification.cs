// ============================================================================
// File: tools/CircuitModels.Core/Verification.cs
// Description: Read-only verification of generated .circuit files:
//                load (same JSON options as the desktop app) -> Validate ->
//                ToModel -> NetlistExtractor -> TopologyClassifier -> strategy.
//
//              Two overloads:
//                * Verify(directory, fileNames): the generic generator mode; it
//                  derives the solve expectation from the classification
//                  (multi-stage files must be rejected with the future-plan
//                  message, everything else must solve successfully);
//                * Verify(directory, specs): additionally compares the documented
//                  ExpectedOutcome (TopologyType + SubtypeName) of every spec.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels;

public static class Verification
{
    /// <summary>Generic verification: load / classify / solve expectations derived from the classification.</summary>
    public static int Verify(string outputDirectory, IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        string directory = Path.GetFullPath(outputDirectory);
        int passed = 0;
        int failed = 0;

        foreach (string fileName in fileNames)
        {
            string path = Path.Combine(directory, fileName);
            try
            {
                CircuitProjectData data = Load(path);
                (ClassificationResult classification, PedagogicalSolution solution) = ClassifyAndSolve(data);
                string problem = string.Empty;
                if (classification.TopologyType == CircuitTopologyType.MultiStageAmplifier)
                {
                    if (solution.IsSuccess) problem = "多级电路不应求解成功";
                    else if (!solution.SummaryText.Contains("多级", StringComparison.Ordinal) ||
                             !solution.SummaryText.Contains("后续开发规划", StringComparison.Ordinal))
                        problem = "多级拒绝信息缺少“多级/后续开发规划”";
                }
                else
                {
                    if (!solution.IsSuccess) problem = "求解失败：" + FirstLine(solution.SummaryText);
                    else if (solution.Steps.Count < 4) problem = $"推导步骤不足（{solution.Steps.Count}）";
                }

                if (problem.Length == 0)
                {
                    passed++;
                    Console.WriteLine($"[PASS] {fileName}  [{classification.TopologyType}/{classification.SubtypeName}]");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"[FAIL] {fileName}  {problem}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[FAIL] {fileName}  {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Verify: Total={passed + failed} Passed={passed} Failed={failed}");
        return failed;
    }

    /// <summary>Verification against the documented per-spec expectations (contract §9).</summary>
    public static int Verify(string outputDirectory, IEnumerable<CircuitSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        string directory = Path.GetFullPath(outputDirectory);
        int passed = 0;
        int failed = 0;

        foreach (CircuitSpec spec in specs)
        {
            string path = Path.Combine(directory, spec.FileName);
            try
            {
                CircuitProjectData data = Load(path);
                (ClassificationResult classification, PedagogicalSolution solution) = ClassifyAndSolve(data);

                var problems = new List<string>();
                if (!string.IsNullOrWhiteSpace(data.ProjectName) && data.ProjectName != spec.Title)
                    problems.Add($"工程名 '{data.ProjectName}' != '{spec.Title}'");
                ExpectedOutcome? expected = spec.Expected;
                if (expected != null)
                {
                    if (classification.TopologyType != expected.Topology)
                        problems.Add($"Topology={classification.TopologyType}（期望 {expected.Topology}）");
                    if (classification.SubtypeName != expected.Subtype)
                        problems.Add($"Subtype={classification.SubtypeName}（期望 {expected.Subtype}）");
                    if (solution.IsSuccess != expected.SolveSucceeds)
                        problems.Add($"IsSuccess={solution.IsSuccess}（期望 {expected.SolveSucceeds}）：{FirstLine(solution.SummaryText)}");
                    if (expected.SolveSucceeds && solution.IsSuccess && solution.Steps.Count < 4)
                        problems.Add($"推导步骤不足（{solution.Steps.Count}）");
                    if (!expected.SolveSucceeds && !solution.IsSuccess &&
                        (!solution.SummaryText.Contains("多级", StringComparison.Ordinal) ||
                         !solution.SummaryText.Contains("后续开发规划", StringComparison.Ordinal)))
                        problems.Add("拒绝信息缺少“多级/后续开发规划”");
                }

                if (problems.Count == 0)
                {
                    passed++;
                    Console.WriteLine($"[PASS] {spec.FileName}  [{classification.TopologyType}/{classification.SubtypeName}]");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"[FAIL] {spec.FileName}  " + string.Join("; ", problems));
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[FAIL] {spec.FileName}  {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Verify: Total={passed + failed} Passed={passed} Failed={failed}");
        return failed;
    }

    // ------------------------------------------------------------------

    private static CircuitProjectData Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("工程文件不存在。", path);
        string json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !new[] { "Version", "Elements", "Wires" }.All(name => document.RootElement.EnumerateObject()
                .Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("文件不是 CircuitAssistant 工程（缺少 Version/Elements/Wires）。");

        CircuitProjectData? data = JsonSerializer.Deserialize<CircuitProjectData>(json, CircuitFileWriter.JsonOptions);
        if (data == null) throw new InvalidDataException("无法解析工程文件数据。");
        data.Validate();
        return data;
    }

    private static (ClassificationResult Classification, PedagogicalSolution Solution) ClassifyAndSolve(CircuitProjectData data)
    {
        (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
        ElectricalNetlist netlist = NetlistExtractor.Extract(elements, wires);
        NetlistValidationResult validation = netlist.Validate();
        if (!validation.IsValid)
        {
            string issues = string.Join("; ", validation.Issues
                .Where(i => i.Severity == NetlistValidationSeverity.Error)
                .Select(i => i.Message));
            throw new InvalidOperationException("网表校验失败：" + issues);
        }
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
        return (classification, solution);
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "（空摘要）";
        string line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return line.Length > 100 ? line[..100] + "…" : line;
    }
}
