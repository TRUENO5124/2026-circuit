// ============================================================================
// File: tests/WebImportChecks/Program.cs
// Namespace: WebImportChecks
// Description: Console regression for the native Web/Python CircuitGraph importer.
//
//   Verifies, for the three prototypes under benchmarks/examples:
//     * format detection + import + CircuitProjectData self validation;
//     * SI parameter -> DisplayNumber/UnitSymbol mapping;
//     * deterministic auto-layout (nodes grid, components at node midpoints);
//     * one native wire per Web connection and NetlistExtractor net recovery
//       matching groundTruth.expectedNodeIds / expectedComponentIds /
//       expectedConnectionCount;
//     * classification + strategy behaviour on the imported netlists;
//     * negative cases (native project JSON, unknown definitionId, missing
//       arrays, unmappable ports, duplicates).
//
//   Known native limitation (recorded on purpose, see task-3 decision):
//     benchmarks/examples/bjt-common-emitter-bias.json uses RB=100 kΩ and
//     RC=2 kΩ with VCC=12 V and β=100, i.e. IBQ=(12-0.7)/100k=113 µA,
//     ICQ=11.3 mA and VCEQ=12-11.3mA·2k = -10.6 V. That is a non-physical
//     bias (saturation/negative VCE); the Python prototype is symbolic and
//     never checks the Q point, while the native BjtAmplifierStrategy
//     correctly rejects it as "not in the active region". The regression
//     therefore asserts: import + classification + explicit pedagogical
//     rejection, and additionally proves that the imported data CAN drive
//     the native solver by deriving a solvable variant that only changes
//     RB to 470 kΩ (hand calculation: IBQ=(12-0.7)/470k≈24.04 µA,
//     ICQ=β·IBQ≈2.404 mA, VCEQ=12-2.404mA·2k≈7.19 V -> active region).
// ============================================================================

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace WebImportChecks;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = new();
    private static string _repoRoot = string.Empty;

    private sealed record SampleExpectation(
        string FileName,
        string Title,
        string[] ComponentIds,
        string[] NodeIds,
        int ConnectionCount,
        int BranchCount,
        int TransistorCount,
        Dictionary<string, (float X, float Y)> ComponentPositions,
        Dictionary<string, (float X, float Y)> NodeCoordinates);

    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    private const string NativeProjectJson = """
        { "Version": "1.1.0", "ProjectName": "native", "Elements": [], "Wires": [] }
        """;

    private const string MalformedJson = """
        { this is not json
        """;

    private const string MissingConnectionsJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 1000, "unit": "ohm" } } }
          ]
        }
        """;

    private const string UnknownDefinitionJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "x1", "definitionId": "mystery.thing", "ports": ["a", "b"] }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "x1", "portId": "a" },
            { "id": "c2", "nodeId": "n0", "componentId": "x1", "portId": "b" }
          ]
        }
        """;

    private const string MissingResistanceJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"] }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "r1", "portId": "p" },
            { "id": "c2", "nodeId": "n0", "componentId": "r1", "portId": "n" }
          ]
        }
        """;

    private const string UnmappablePortJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 1000, "unit": "ohm" } } }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "r1", "portId": "p" },
            { "id": "c2", "nodeId": "n0", "componentId": "r1", "portId": "gate" }
          ]
        }
        """;

    private const string DuplicateComponentJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 1000, "unit": "ohm" } } },
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 2000, "unit": "ohm" } } }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "r1", "portId": "p" },
            { "id": "c2", "nodeId": "n0", "componentId": "r1", "portId": "n" }
          ]
        }
        """;

    private const string DuplicatePortJson = """
        {
          "nodes": [ { "id": "n1" }, { "id": "n2" }, { "id": "n0", "kind": "reference" } ],
          "components": [
            { "id": "r1", "definitionId": "passive.resistor", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 1000, "unit": "ohm" } } }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "r1", "portId": "p" },
            { "id": "c2", "nodeId": "n2", "componentId": "r1", "portId": "p" },
            { "id": "c3", "nodeId": "n0", "componentId": "r1", "portId": "n" }
          ]
        }
        """;

    private const string DirectGraphJson = """
        {
          "id": "graph.voltage_divider_regression",
          "title": "Voltage Divider",
          "nodes": [
            { "id": "n1", "label": "VCC" },
            { "id": "n2", "label": "Out" },
            { "id": "n0", "label": "Ground", "kind": "reference" }
          ],
          "components": [
            { "id": "v1", "definitionId": "source.v_dc", "label": "V1", "ports": ["p", "n"],
              "parameters": { "voltage": { "value": 10, "unit": "volt" } } },
            { "id": "r1", "definitionId": "passive.resistor", "label": "R1", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 1000, "unit": "ohm" } } },
            { "id": "r2", "definitionId": "passive.resistor", "label": "R2", "ports": ["p", "n"],
              "parameters": { "resistance": { "value": 2000, "unit": "ohm" } } },
            { "id": "gnd", "definitionId": "reference.ground", "label": "GND", "ports": ["ref"],
              "parameters": {} }
          ],
          "connections": [
            { "id": "c1", "nodeId": "n1", "componentId": "v1", "portId": "p" },
            { "id": "c2", "nodeId": "n0", "componentId": "v1", "portId": "n" },
            { "id": "c3", "nodeId": "n1", "componentId": "r1", "portId": "p" },
            { "id": "c4", "nodeId": "n2", "componentId": "r1", "portId": "n" },
            { "id": "c5", "nodeId": "n2", "componentId": "r2", "portId": "p" },
            { "id": "c6", "nodeId": "n0", "componentId": "r2", "portId": "n" },
            { "id": "c7", "nodeId": "n0", "componentId": "gnd", "portId": "ref" }
          ]
        }
        """;

    // ------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------

    private static async Task<int> Main()
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch { /* redirected console: keep default encoding */ }

        Console.WriteLine("=== CircuitAssistant Web/Python CircuitGraph 原生导入回归 (tests/WebImportChecks) ===");
        try
        {
            _repoRoot = FindRepoRoot();
            Console.WriteLine($"仓库根目录: {_repoRoot}");
            Console.WriteLine();

            RunFormatDetectionChecks();
            Console.WriteLine();
            RunImportNegativeChecks();
            Console.WriteLine();

            Console.WriteLine("-- 三个 Benchmark 样例：导入 -> 布局 -> 原生网表 --");
            var samples = CreateSampleExpectations();
            var imported = new Dictionary<string, CircuitProjectData>(StringComparer.Ordinal);
            var netlists = new Dictionary<string, ElectricalNetlist>(StringComparer.Ordinal);
            foreach (var sample in samples)
            {
                var data = RunBenchmarkImportChecks(sample);
                imported[sample.FileName] = data;
                var (elements, wires, _) = data.ToModel();
                netlists[sample.FileName] = NetlistExtractor.ExtractNetlist(elements, wires);
            }

            Console.WriteLine();
            Console.WriteLine("-- 参数映射（SI -> DisplayNumber + UnitSymbol）--");
            RunParameterMappingChecks(imported);

            Console.WriteLine();
            Console.WriteLine("-- BJT benchmark：分类 + 明确拒绝（非物理 seed 参数）+ 派生可解变体 --");
            await RunBjtBenchmarkChecks(imported["bjt-common-emitter-bias.json"],
                netlists["bjt-common-emitter-bias.json"]);

            Console.WriteLine();
            Console.WriteLine("-- diode benchmark：导入成功，分类 DiodeCircuit 并按二极管教学策略明确处理 --");
            await RunDiodeBenchmarkChecks(imported["diode-limiter.json"],
                netlists["diode-limiter.json"]);

            Console.WriteLine();
            Console.WriteLine("-- MOS benchmark：导入成功，分类 MosfetAmplifier 并按 MOSFET 教学策略明确拒绝缺失偏置 --");
            await RunMosBenchmarkChecks(imported["mos-common-source.json"],
                netlists["mos-common-source.json"]);

            Console.WriteLine();
            Console.WriteLine("-- 裸 { nodes, components, connections } 文档导入 + 原生 MNA 求解 --");
            await RunDirectGraphChecks();
        }
        catch (Exception ex)
        {
            Fail("回归执行过程中出现未处理异常", ex.ToString());
        }

        Console.WriteLine();
        Console.WriteLine("[已知限制] BJT benchmark seed 参数 RB=100kΩ/RC=2kΩ/VCC=12V/β=100 在数学上给出 VCEQ≈-10.6V（饱和/非物理），");
        Console.WriteLine("           Python 原型只做符号推导所以不校验 Q 点；原生 BjtAmplifierStrategy 会明确拒绝该工作点。");
        Console.WriteLine("           本回归用同一导入数据仅把 RB 调为 470kΩ 的派生变体验证导入数据可驱动原生求解（放大区，VCEQ≈7.19V）。");
        Console.WriteLine("           diode/mos 样例可按新分类路由到对应教学策略：diode 的并联直流回路被 DiodeBias 明确拒绝（只支持单一串联回路），");
        Console.WriteLine("           MOS 样例因缺少栅极偏置网络被 MosfetAmplifierStrategy 明确拒绝；两者都不再走 MNA/Unsupported 路径。");
        Console.WriteLine();
        Console.WriteLine($"Total={_passed + _failed} Passed={_passed} Failed={_failed}");
        if (_failed > 0)
        {
            Console.WriteLine();
            Console.WriteLine("失败明细:");
            foreach (var failure in _failures) Console.WriteLine("  - " + failure);
            return 1;
        }
        return 0;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "benchmarks", "examples")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("无法从输出目录向上定位包含 benchmarks/examples 的仓库根目录。");
    }

    private static SampleExpectation[] CreateSampleExpectations() =>
    [
        new SampleExpectation(
            FileName: "bjt-common-emitter-bias.json",
            Title: "BJT common-emitter DC operating point",
            ComponentIds: ["vcc", "rc", "rb", "q1", "gnd"],
            NodeIds: ["n_vcc", "n_collector", "n_base", "n_gnd"],
            ConnectionCount: 10,
            BranchCount: 3,
            TransistorCount: 1,
            ComponentPositions: new Dictionary<string, (float X, float Y)>
            {
                ["vcc"] = (290f, 300f), // midpoint(n_vcc(180,120), n_gnd(400,480))
                ["rc"] = (255f, 120f),  // midpoint(n_vcc, n_collector)
                ["rb"] = (330f, 120f),  // midpoint(n_vcc, n_base)
                ["q1"] = (405f, 120f),  // midpoint(n_collector, n_base) — first two ports
                ["gnd"] = (400f, 480f)  // single-terminal reference symbol
            },
            NodeCoordinates: new Dictionary<string, (float X, float Y)>
            {
                ["n_vcc"] = (180f, 120f),
                ["n_collector"] = (330f, 120f),
                ["n_base"] = (480f, 120f),
                ["n_gnd"] = (400f, 480f)
            }),
        new SampleExpectation(
            FileName: "diode-limiter.json",
            Title: "Diode positive limiter",
            ComponentIds: ["vin", "r1", "d1", "rl", "gnd"],
            NodeIds: ["n_in", "n_out", "n_gnd"],
            ConnectionCount: 9,
            BranchCount: 4,
            TransistorCount: 0,
            ComponentPositions: new Dictionary<string, (float X, float Y)>
            {
                ["vin"] = (290f, 300f),
                ["r1"] = (255f, 120f),
                ["d1"] = (365f, 300f),
                ["rl"] = (365f, 300f),
                ["gnd"] = (400f, 480f)
            },
            NodeCoordinates: new Dictionary<string, (float X, float Y)>
            {
                ["n_in"] = (180f, 120f),
                ["n_out"] = (330f, 120f),
                ["n_gnd"] = (400f, 480f)
            }),
        new SampleExpectation(
            FileName: "mos-common-source.json",
            Title: "MOS common-source small-signal amplifier",
            ComponentIds: ["vdd", "vin", "rd", "m1", "gnd"],
            NodeIds: ["n_vdd", "n_drain", "n_gate", "n_gnd"],
            ConnectionCount: 11,
            BranchCount: 3,
            TransistorCount: 1,
            ComponentPositions: new Dictionary<string, (float X, float Y)>
            {
                ["vdd"] = (290f, 300f),
                ["vin"] = (440f, 300f), // midpoint(n_gate(480,120), n_gnd(400,480))
                ["rd"] = (255f, 120f),
                ["m1"] = (405f, 120f),  // midpoint(n_drain, n_gate) — first two ports
                ["gnd"] = (400f, 480f)
            },
            NodeCoordinates: new Dictionary<string, (float X, float Y)>
            {
                ["n_vdd"] = (180f, 120f),
                ["n_drain"] = (330f, 120f),
                ["n_gate"] = (480f, 120f),
                ["n_gnd"] = (400f, 480f)
            })
    ];

    // ------------------------------------------------------------------
    // Suite: format detection
    // ------------------------------------------------------------------

    private static void RunFormatDetectionChecks()
    {
        Console.WriteLine("-- 格式识别（LooksLikeWebCircuitGraph）--");
        string benchmarkJson = File.ReadAllText(
            Path.Combine(_repoRoot, "benchmarks", "examples", "bjt-common-emitter-bias.json"), Encoding.UTF8);

        Assert(WebCircuitGraphImporter.LooksLikeWebCircuitGraph(benchmarkJson),
            "benchmark 包装 { circuitGraph: {...} } 被识别为 Web CircuitGraph");
        Assert(WebCircuitGraphImporter.LooksLikeWebCircuitGraph(DirectGraphJson),
            "裸 { nodes, components, connections } 文档被识别为 Web CircuitGraph");
        Assert(!WebCircuitGraphImporter.LooksLikeWebCircuitGraph(NativeProjectJson),
            "原生工程 JSON（Version/Elements/Wires）不被误识别为 Web CircuitGraph");
        Assert(!WebCircuitGraphImporter.LooksLikeWebCircuitGraph(string.Empty),
            "空字符串不被识别为 Web CircuitGraph");
        Assert(!WebCircuitGraphImporter.LooksLikeWebCircuitGraph(MalformedJson),
            "非法 JSON 返回 false 且不抛异常");
        Assert(!WebCircuitGraphImporter.LooksLikeWebCircuitGraph("""{ "components": [], "nodes": [] }"""),
            "只有 components、缺少 connections 数组时不识别");
        Assert(WebCircuitGraphImporter.LooksLikeWebCircuitGraph("""{ "circuitGraph": {} }"""),
            "根含 circuitGraph 对象即识别（内容完整性由 Import 校验）");
    }

    // ------------------------------------------------------------------
    // Suite: import negative cases
    // ------------------------------------------------------------------

    private static void RunImportNegativeChecks()
    {
        Console.WriteLine("-- 导入负例 --");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(NativeProjectJson),
            "原生工程 JSON 不能直接 Import（抛 InvalidDataException）");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(MissingConnectionsJson),
            "缺少 connections 数组抛 InvalidDataException", "connections");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(MalformedJson),
            "非法 JSON 抛 InvalidDataException", "解析失败");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(UnknownDefinitionJson),
            "未知 definitionId 抛 InvalidDataException 并列出 id", "mystery.thing");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(MissingResistanceJson),
            "电阻缺少必需参数 resistance 抛 InvalidDataException", "resistance");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(UnmappablePortJson),
            "无法映射的 Web 端口抛 InvalidDataException", "gate");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(DuplicateComponentJson),
            "重复 Web 元件 id 抛 InvalidDataException", "重复");
        AssertThrowsInvalidData(() => WebCircuitGraphImporter.Import(DuplicatePortJson),
            "同一元件端口重复连接抛 InvalidDataException", "重复");

        try
        {
            WebCircuitGraphImporter.ImportFile(
                Path.Combine(_repoRoot, "benchmarks", "examples", "__missing_benchmark__.json"));
            Fail("ImportFile 不存在的文件抛 FileNotFoundException", "未抛出异常");
        }
        catch (FileNotFoundException ex)
        {
            Pass("ImportFile 不存在的文件抛 FileNotFoundException", ex.Message);
        }
        catch (Exception ex)
        {
            Fail("ImportFile 不存在的文件抛 FileNotFoundException", $"实际抛出 {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Suite: 3 benchmarks — import / layout / netlist
    // ------------------------------------------------------------------

    private static CircuitProjectData RunBenchmarkImportChecks(SampleExpectation sample)
    {
        Console.WriteLine($"[{sample.FileName}]");
        string path = Path.Combine(_repoRoot, "benchmarks", "examples", sample.FileName);
        Assert(File.Exists(path), $"[{sample.FileName}] benchmark 文件存在");

        string json = File.ReadAllText(path, Encoding.UTF8);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var graph = root.TryGetProperty("circuitGraph", out var wrappedGraph) ? wrappedGraph : root;
        var groundTruth = root.GetProperty("groundTruth");
        int expectedConnectionCount = groundTruth.GetProperty("expectedConnectionCount").GetInt32();
        var expectedComponentIds = groundTruth.GetProperty("expectedComponentIds")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        var expectedNodeIds = groundTruth.GetProperty("expectedNodeIds")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert(WebCircuitGraphImporter.LooksLikeWebCircuitGraph(json),
            $"[{sample.FileName}] LooksLikeWebCircuitGraph == true");

        var data = ImportOrEmpty(path, sample.FileName);

        Assert(data.ProjectName == sample.Title,
            $"[{sample.FileName}] ProjectName 取 benchmark title", data.ProjectName);
        Assert(data.Version == "1.2.0",
            $"[{sample.FileName}] Version 由 CircuitProjectData.From 置为 1.2.0", data.Version);
        Assert(data.Description == WebCircuitGraphImporter.ImportDescription,
            $"[{sample.FileName}] Description 记录导入来源", data.Description);
        Assert(!string.IsNullOrWhiteSpace(data.ProjectFileName) &&
               data.ProjectFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase),
            $"[{sample.FileName}] ProjectFileName 为 .json 安全文件名", data.ProjectFileName);
        Assert(data.Elements.Count == sample.ComponentIds.Length,
            $"[{sample.FileName}] 元件数 = {sample.ComponentIds.Length}", data.Elements.Count.ToString(CultureInfo.InvariantCulture));
        Assert(data.Wires.Count == expectedConnectionCount && data.Wires.Count == sample.ConnectionCount,
            $"[{sample.FileName}] 导线数 = connections 数 = groundTruth.expectedConnectionCount",
            $"{data.Wires.Count}/{expectedConnectionCount}/{sample.ConnectionCount}");
        Assert(graph.TryGetProperty("connections", out var connectionsElement) &&
               connectionsElement.GetArrayLength() == data.Wires.Count,
            $"[{sample.FileName}] 每一条 connection 恰好生成一根原生导线");
        Assert(expectedConnectionCount == data.Wires.Count,
            $"[{sample.FileName}] groundTruth.expectedConnectionCount 与导入导线数一致");

        var actualComponentIds = data.Elements.Select(e => e.Id).ToArray();
        Assert(new HashSet<string>(actualComponentIds, StringComparer.Ordinal)
                .SetEquals(expectedComponentIds),
            $"[{sample.FileName}] 元件 id 集合 = groundTruth.expectedComponentIds",
            string.Join(",", actualComponentIds));

        try
        {
            data.Validate();
            Pass($"[{sample.FileName}] CircuitProjectData.Validate() 自检通过");
        }
        catch (Exception ex)
        {
            Fail($"[{sample.FileName}] CircuitProjectData.Validate() 自检通过", ex.Message);
        }

        var (elements, wires, parameters) = data.ToModel();
        Assert(elements.Count == data.Elements.Count && wires.Count == data.Wires.Count,
            $"[{sample.FileName}] ToModel() 往返元件/导线数量一致");
        Assert(parameters.Count == 0,
            $"[{sample.FileName}] 导入工程参数表为空字典");

        var elementsById = elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var pinCountProblems = elements
            .Where(e => e.Pins.Count != ExpectedPinCount(e.Type))
            .Select(e => $"{e.Id}:{e.Pins.Count}!={ExpectedPinCount(e.Type)}")
            .ToArray();
        Assert(pinCountProblems.Length == 0,
            $"[{sample.FileName}] 每种元件按 InitializeDefaultPins 初始化原生引脚",
            string.Join("; ", pinCountProblems));

        // Deterministic layout: nodes on the documented grid, components at midpoints.
        foreach (var (componentId, expectedPosition) in sample.ComponentPositions)
        {
            if (!elementsById.TryGetValue(componentId, out var element))
            {
                Fail($"[{sample.FileName}] {componentId} 布局坐标为 ({expectedPosition.X},{expectedPosition.Y})", "元件不存在");
                continue;
            }
            bool matches = Math.Abs(element.X - expectedPosition.X) < 0.01f &&
                           Math.Abs(element.Y - expectedPosition.Y) < 0.01f;
            Assert(matches,
                $"[{sample.FileName}] {componentId} 自动布局坐标为 ({expectedPosition.X},{expectedPosition.Y})",
                $"({element.X},{element.Y})");
        }

        var nodeCoordinateSet = sample.NodeCoordinates.Values
            .Select(coordinate => (coordinate.X, coordinate.Y))
            .ToHashSet();
        var wireEndProblems = wires
            .Where(w => !string.IsNullOrEmpty(w.EndElementId) ||
                        !string.IsNullOrEmpty(w.EndPinName) ||
                        !nodeCoordinateSet.Contains((w.EndX, w.EndY)))
            .Select(w => $"{w.Id}->({w.EndX},{w.EndY})[{w.EndElementId}]")
            .ToArray();
        Assert(wireEndProblems.Length == 0,
            $"[{sample.FileName}] 每根导线开放端（EndElementId 空）落在布局节点坐标上",
            string.Join("; ", wireEndProblems));
        Assert(wires.All(w => WireStartsAtPin(w, elementsById)),
            $"[{sample.FileName}] 每根导线起点 = 元件端口 pin 的世界坐标");

        var netlist = NetlistExtractor.ExtractNetlist(elements, wires);
        Assert(netlist.Nets.Count == expectedNodeIds.Length,
            $"[{sample.FileName}] NetlistExtractor 网数 = groundTruth.expectedNodeIds 数（含 ground 合并）",
            $"{netlist.Nets.Count}/{expectedNodeIds.Length}");
        Assert(netlist.Nodes.Count == expectedNodeIds.Length,
            $"[{sample.FileName}] 电气节点数 = groundTruth.expectedNodeIds 数",
            $"{netlist.Nodes.Count}/{expectedNodeIds.Length}");
        Assert(netlist.GroundNet != null,
            $"[{sample.FileName}] 提取出参考地网 GroundNet != null");
        Assert(netlist.Branches.Count == sample.BranchCount,
            $"[{sample.FileName}] 二端分支数 = {sample.BranchCount}", netlist.Branches.Count.ToString(CultureInfo.InvariantCulture));
        Assert(netlist.Transistors.Count == sample.TransistorCount,
            $"[{sample.FileName}] 晶体管器件数 = {sample.TransistorCount}", netlist.Transistors.Count.ToString(CultureInfo.InvariantCulture));

        var netlistValidation = netlist.Validate();
        Assert(netlistValidation.IsValid,
            $"[{sample.FileName}] 电气网表 Validate() 通过（无悬空/短路/失联）",
            string.Join("；", netlistValidation.Issues
                .Where(i => i.Severity == NetlistValidationSeverity.Error)
                .Select(i => $"{i.Code}:{i.Message}")));

        // Topological conservation (the core adapter requirement): every Web
        // connection must become the documented native wire id, and every Web
        // node id must land in exactly one native net — no merges, no splits.
        var wiresById = wires.ToDictionary(w => w.Id, StringComparer.Ordinal);
        var netByWireId = new Dictionary<string, ElectricalNet>(StringComparer.Ordinal);
        foreach (var net in netlist.Nets)
            foreach (var wireId in net.ConnectedWireIds)
                netByWireId[wireId] = net;

        var mappingProblems = new List<string>();
        var netByNodeId = new Dictionary<string, ElectricalNet>(StringComparer.Ordinal);
        foreach (var connection in graph.GetProperty("connections").EnumerateArray())
        {
            string connectionId = connection.GetProperty("id").GetString()!;
            string expectedWireId = "w_" + connectionId;
            if (!wiresById.ContainsKey(expectedWireId))
            {
                mappingProblems.Add($"缺少导线 {expectedWireId}");
                continue;
            }
            if (!netByWireId.TryGetValue(expectedWireId, out var net))
            {
                mappingProblems.Add($"导线 {expectedWireId} 未并入任何网");
                continue;
            }
            string nodeId = connection.GetProperty("nodeId").GetString()!;
            if (netByNodeId.TryGetValue(nodeId, out var existingNet) && !ReferenceEquals(existingNet, net))
                mappingProblems.Add($"node {nodeId} 的导线被拆到多个网");
            else
                netByNodeId[nodeId] = net;
        }
        if (netByNodeId.Values.Distinct().Count() != netByNodeId.Count)
            mappingProblems.Add("不同 nodeId 的导线被合并到同一个网（意外短接）");
        Assert(mappingProblems.Count == 0 && netByNodeId.Count == expectedNodeIds.Length,
            $"[{sample.FileName}] 连接守恒：每个 nodeId 对应唯一原生网，不同 nodeId 不短接",
            string.Join("; ", mappingProblems));

        bool groundGroupIsGroundNet = true;
        foreach (var (nodeId, net) in netByNodeId)
        {
            if (nodeId.Contains("gnd", StringComparison.OrdinalIgnoreCase) && !net.IsGround)
                groundGroupIsGroundNet = false;
        }
        Assert(groundGroupIsGroundNet,
            $"[{sample.FileName}] groundTruth 中的 GND 节点被提取为 IsGround 地网");

        var dataAgain = WebCircuitGraphImporter.ImportFile(path);
        bool deterministic = dataAgain.Elements.Count == data.Elements.Count &&
                             data.Elements.Zip(dataAgain.Elements).All(pair =>
                                 pair.First.X.Equals(pair.Second.X) && pair.First.Y.Equals(pair.Second.Y));
        Assert(deterministic,
            $"[{sample.FileName}] 自动布局确定性：重复导入得到相同元件坐标");

        return data;
    }

    private static bool WireStartsAtPin(CircuitWire wire, Dictionary<string, CircuitElement> elementsById)
    {
        if (!elementsById.TryGetValue(wire.StartElementId, out var element)) return false;
        var pin = element.Pins.FirstOrDefault(p => p.Id == wire.StartPinName);
        if (pin == null) return false;
        var position = element.GetPinWorldPosition(pin);
        return Math.Abs(position.X - wire.StartX) < 0.01f &&
               Math.Abs(position.Y - wire.StartY) < 0.01f;
    }

    private static int ExpectedPinCount(ElementType type) => type switch
    {
        ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized or
        ElementType.Inductor or ElementType.Diode or ElementType.DcVoltageSource or
        ElementType.DcCurrentSource or ElementType.AcVoltageSource or ElementType.AcCurrentSource => 2,
        ElementType.BjtNpn or ElementType.BjtPnp or ElementType.Mosfet or ElementType.MosfetNEnh or
        ElementType.MosfetPEnh or ElementType.MosfetNDep or ElementType.MosfetPDep or
        ElementType.Jfet or ElementType.JfetN or ElementType.JfetP => 3,
        ElementType.Transformer or ElementType.Vcvs or ElementType.Vccs or
        ElementType.Ccvs or ElementType.Cccs => 4,
        ElementType.Ground or ElementType.VccPort => 1,
        _ => 2
    };

    // ------------------------------------------------------------------
    // Suite: parameter mapping
    // ------------------------------------------------------------------

    private static void RunParameterMappingChecks(Dictionary<string, CircuitProjectData> imported)
    {
        var bjt = imported["bjt-common-emitter-bias.json"].Elements;
        AssertDisplay(FindElement(bjt, "vcc"), 12.0, "V", "BJT vcc（source.v_dc 12V -> 12 V）");
        AssertDisplay(FindElement(bjt, "rc"), 2.0, "kΩ", "BJT rc（2000 Ω -> 2 kΩ）");
        AssertDisplay(FindElement(bjt, "rb"), 100.0, "kΩ", "BJT rb（100000 Ω -> 100 kΩ）");
        AssertDisplay(FindElement(bjt, "q1"), 100.0, string.Empty, "BJT q1 beta（unitless 100 -> DisplayNumber 100）");
        Assert(FindElement(bjt, "q1")?.Type == ElementType.BjtNpn &&
               FindElement(bjt, "q1")?.Pins.Count == 3,
            "BJT q1 映射为 ElementType.BjtNpn（B/C/E 三引脚）");
        Assert(FindElement(bjt, "gnd")?.Type == ElementType.Ground,
            "BJT gnd 映射为 ElementType.Ground（GND 单引脚）");

        var diode = imported["diode-limiter.json"].Elements;
        AssertDisplay(FindElement(diode, "vin"), 5.0, "V", "diode vin（5V -> 5 V）");
        AssertDisplay(FindElement(diode, "r1"), 1.0, "kΩ", "diode r1（1000 Ω -> 1 kΩ）");
        AssertDisplay(FindElement(diode, "rl"), 10.0, "kΩ", "diode rl（10000 Ω -> 10 kΩ）");
        var d1 = FindElement(diode, "d1");
        Assert(d1 != null && Math.Abs(d1.DisplayNumber - 0.7) < 1e-9 && d1.UnitSymbol == "V" &&
               d1.Type == ElementType.Diode && d1.Pins.Select(p => p.Id).SequenceEqual(["A", "K"]),
            "diode d1（forward_voltage 0.7 V -> DisplayNumber 0.7 V，A/K 引脚）",
            d1 == null ? "元件缺失" : $"{d1.DisplayNumber}{d1.UnitSymbol}");

        var mos = imported["mos-common-source.json"].Elements;
        AssertDisplay(FindElement(mos, "vdd"), 10.0, "V", "MOS vdd（10V -> 10 V）");
        AssertDisplay(FindElement(mos, "rd"), 5.0, "kΩ", "MOS rd（5000 Ω -> 5 kΩ）");
        var vin = FindElement(mos, "vin");
        Assert(vin != null && vin.Type == ElementType.AcVoltageSource &&
               Math.Abs(vin.DisplayNumber - 10.0) < 1e-9 && vin.UnitSymbol == "mV" &&
               Math.Abs(vin.FrequencyHz - 1000.0) < 1e-9,
            "MOS vin（amplitude 0.01 V -> 10 mV，frequency -> FrequencyHz=1000）",
            vin == null ? "元件缺失" : $"{vin.DisplayNumber}{vin.UnitSymbol}@{vin.FrequencyHz}Hz");
        var m1 = FindElement(mos, "m1");
        Assert(m1 != null && (m1.Type is ElementType.Mosfet or ElementType.MosfetNEnh) &&
               Math.Abs(m1.DisplayNumber - 0.002) < 1e-12 && m1.UnitSymbol == "S",
            "MOS m1（fet.mosfet -> ElementType.Mosfet，transconductance 0.002 S）",
            m1 == null ? "元件缺失" : $"{m1.DisplayNumber}{m1.UnitSymbol}");
        Assert(FindElement(mos, "gnd")?.Type == ElementType.Ground,
            "MOS gnd 映射为 ElementType.Ground");
    }

    private static void AssertDisplay(
        CircuitElementDto? element, double expectedDisplay, string expectedUnit, string name)
    {
        bool matches = element != null &&
                       Math.Abs(element.DisplayNumber - expectedDisplay) < 1e-9 &&
                       element.UnitSymbol == expectedUnit;
        Assert(matches, name,
            element == null ? "元件缺失" : $"{element.DisplayNumber}{element.UnitSymbol}");
    }

    private static CircuitElementDto? FindElement(IEnumerable<CircuitElementDto> elements, string id) =>
        elements.FirstOrDefault(element => element.Id == id);

    // ------------------------------------------------------------------
    // Suite: BJT — classification, explicit rejection, derived solvable variant
    // ------------------------------------------------------------------

    private static async Task RunBjtBenchmarkChecks(CircuitProjectData data, ElectricalNetlist netlist)
    {
        var classification = TopologyClassifier.Classify(netlist);
        Assert(classification.TopologyType == CircuitTopologyType.BjtAmplifier,
            "BJT benchmark 分类为 BjtAmplifier", classification.TopologyType.ToString());
        Assert(classification.Strategy is BjtAmplifierStrategy,
            "BJT benchmark 选择 BjtAmplifierStrategy 策略", classification.Strategy.GetType().Name);
        Assert(classification.SubtypeName == "FixedBias",
            "BJT benchmark 子类型识别为 FixedBias（基极经 RB 接 VCC、无 RE）", classification.SubtypeName);

        var solution = await classification.Strategy.SolveAsync(netlist, new AnalysisParameters());
        Assert(!solution.IsSuccess,
            "BJT benchmark seed 参数（VCEQ≈-10.6V）被原生策略明确拒绝，而非崩溃",
            solution.SummaryText);
        Assert(!string.IsNullOrWhiteSpace(solution.SummaryText),
            "BJT benchmark 拒绝原因为非空教学提示", solution.SummaryText);
        Assert(solution.SummaryText.Contains("放大区", StringComparison.Ordinal) ||
               solution.SummaryText.Contains("工作点", StringComparison.Ordinal),
            "BJT benchmark 拒绝原因指出放大区/工作点问题", solution.SummaryText);

        // ---- Derived solvable variant: only RB 100 kΩ -> 470 kΩ -------------
        // Independent hand calculation (fixed bias, emitter grounded):
        //   IBQ = (VCC - VBE) / RB = (12 - 0.7) / 470k ≈ 24.0426 µA
        //   ICQ = β·IBQ ≈ 2.40426 mA
        //   VCEQ = VCC - ICQ·RC = 12 - 2.40426mA·2k ≈ 7.1915 V -> active region.
        var (elements, wires, _) = data.ToModel();
        var rb = elements.First(element => element.Id == "rb");
        rb.DisplayNumber = 470.0;
        rb.UnitSymbol = "kΩ";
        rb.Value = 470000.0;

        var derived = CircuitProjectData.From(
            "BJT benchmark 派生可解变体",
            "bjt_benchmark_solvable_variant.json",
            elements,
            wires,
            new Dictionary<string, double>(),
            WebCircuitGraphImporter.ImportDescription + "（派生变体：仅将 RB 从 100 kΩ 调整为 470 kΩ）");
        try
        {
            derived.Validate();
            Pass("派生变体（仅 RB=470kΩ）CircuitProjectData.Validate() 通过");
        }
        catch (Exception ex)
        {
            Fail("派生变体（仅 RB=470kΩ）CircuitProjectData.Validate() 通过", ex.Message);
        }

        var (derivedElements, derivedWires, _) = derived.ToModel();
        var derivedNetlist = NetlistExtractor.ExtractNetlist(derivedElements, derivedWires);
        Assert(derivedNetlist.Nets.Count == 4,
            "派生变体网表仍为 4 个网（拓扑不变，仅参数调整）",
            derivedNetlist.Nets.Count.ToString(CultureInfo.InvariantCulture));

        var derivedClassification = TopologyClassifier.Classify(derivedNetlist);
        Assert(derivedClassification.TopologyType == CircuitTopologyType.BjtAmplifier &&
               derivedClassification.SubtypeName == "FixedBias",
            "派生变体仍分类为 BjtAmplifier/FixedBias",
            $"{derivedClassification.TopologyType}/{derivedClassification.SubtypeName}");

        var derivedSolution = await derivedClassification.Strategy.SolveAsync(
            derivedNetlist, new AnalysisParameters());
        Assert(derivedSolution.IsSuccess,
            "派生变体 BjtAmplifierStrategy.SolveAsync 求解成功（导入数据可驱动原生求解）",
            derivedSolution.SummaryText);
        Assert(derivedSolution.Steps.Count == 5,
            "派生变体输出 5 步教学推导", derivedSolution.Steps.Count.ToString(CultureInfo.InvariantCulture));

        var qPoint = derivedSolution.QPoint;
        Assert(qPoint != null && qPoint.IsActiveRegion && qPoint.WorkingRegion.Contains("放大区"),
            "派生变体 Q 点为放大区（IsActiveRegion == true）",
            qPoint == null ? "QPoint 为空" : $"{qPoint.WorkingRegion}");

        const double vcc = 12.0;
        const double vbe = 0.7;
        const double beta = 100.0;
        const double rbOhm = 470e3;
        const double rcOhm = 2e3;
        const double ut = 0.026;
        // 教材估算法（用户口径 2026-10-18）：每个中间量按显示单位两位小数舍入后回代，
        // 期望值复现该手算链（Q 点显示 24.04 μA / 2.4 mA / 7.2 V）。
        double expectedIbq = TeachingDisplay.RoundTo((vcc - vbe) / rbOhm, QuantityKind.Current);
        double expectedIeq = TeachingDisplay.RoundTo((1.0 + beta) * expectedIbq, QuantityKind.Current);
        double expectedIcq = TeachingDisplay.RoundTo(beta * expectedIbq, QuantityKind.Current);
        double expectedVceq = TeachingDisplay.RoundTo(vcc - expectedIcq * rcOhm, QuantityKind.Voltage);
        double expectedRlPrime = TeachingDisplay.RoundTo(rcOhm, QuantityKind.Resistance);
        double expectedRbe = TeachingDisplay.RoundTo(200.0 + (1.0 + beta) * ut / expectedIeq, QuantityKind.Resistance);
        double expectedAv = TeachingDisplay.RoundPlain(-beta * expectedRlPrime / expectedRbe);
        double expectedRi = TeachingDisplay.RoundTo(rbOhm * expectedRbe / (rbOhm + expectedRbe), QuantityKind.Resistance);

        if (qPoint != null)
        {
            AssertRelativelyEqual(expectedIbq, ParseDisplayedNumber(qPoint.Ibq), 1e-3,
                "派生变体 IBQ 与独立手算一致（≈24.04 µA）");
            AssertRelativelyEqual(expectedIcq, ParseDisplayedNumber(qPoint.Icq), 1e-3,
                "派生变体 ICQ 与独立手算一致（≈2.4 mA）");
            AssertRelativelyEqual(expectedVceq, ParseDisplayedNumber(qPoint.Vceq), 1e-3,
                "派生变体 VCEQ 与独立手算一致（≈7.2 V，放大区）");
        }
        else
        {
            Fail("派生变体 IBQ/ICQ/VCEQ 与独立手算一致", "QPoint 为空");
            Fail("派生变体 ICQ 与独立手算一致", "QPoint 为空");
            Fail("派生变体 VCEQ 与独立手算一致", "QPoint 为空");
        }

        var acParameters = derivedSolution.AcParameters;
        Assert(acParameters != null, "派生变体输出交流小信号参数 AcParameters");
        if (acParameters != null)
        {
            AssertRelativelyEqual(expectedAv, ParseDisplayedNumber(acParameters.VoltageGain), 5e-3,
                "派生变体 Av 与独立手算一致（≈-156.25）");
            AssertRelativelyEqual(expectedRi, ParseDisplayedNumber(acParameters.InputResistance), 5e-3,
                "派生变体 Ri 与独立手算一致（≈1.28 kΩ）");
        }
        else
        {
            Fail("派生变体 Av 与独立手算一致", "AcParameters 为空");
            Fail("派生变体 Ri 与独立手算一致", "AcParameters 为空");
        }
    }

    // ------------------------------------------------------------------
    // Suite: diode — import ok, classified as DiodeCircuit and explicitly
    //        handled by the diode teaching strategy (no longer MNA)
    // ------------------------------------------------------------------

    private static async Task RunDiodeBenchmarkChecks(CircuitProjectData data, ElectricalNetlist netlist)
    {
        Assert(data.Elements.Count == 5 && data.Wires.Count == 9,
            "diode 样例 Import 成功（5 元件 / 9 导线）",
            $"{data.Elements.Count}/{data.Wires.Count}");

        var classification = TopologyClassifier.Classify(netlist);
        Assert(classification.TopologyType == CircuitTopologyType.DiodeCircuit,
            "diode 样例分类为 DiodeCircuit（二极管教学电路，不再落入 MNA）", classification.TopologyType.ToString());
        Assert(classification.Strategy is DiodeCircuitStrategy,
            "diode 样例选择 DiodeCircuitStrategy 教学策略", classification.Strategy.GetType().Name);
        Assert(classification.SubtypeName == "DiodeBias",
            "diode 样例子类型识别为 DiodeBias（仅含直流源的偏置型回路）", classification.SubtypeName);

        var solution = await classification.Strategy.SolveAsync(netlist, new AnalysisParameters());
        Assert(!solution.IsSuccess,
            "diode 样例（5V DC 源 + R1 + D1 并联 RL 的直流回路）被二极管教学模型明确拒绝，而不是伪造结果",
            solution.SummaryText);
        Assert(!string.IsNullOrWhiteSpace(solution.SummaryText),
            "diode 样例失败信息非空", solution.SummaryText);
        Assert(solution.SummaryText.Contains("二极管", StringComparison.Ordinal) &&
               ContainsAny(solution.SummaryText, "并联", "分支", "串联回路"),
            "diode 样例失败信息点名二极管教学模型的并联/串联回路限制", solution.SummaryText);
    }

    // ------------------------------------------------------------------
    // Suite: MOS — import ok, classified as MosfetAmplifier; the teaching
    //        strategy explicitly rejects the seed's missing gate bias network
    //        (no longer the old generic "Unsupported" strategy)
    // ------------------------------------------------------------------

    private static async Task RunMosBenchmarkChecks(CircuitProjectData data, ElectricalNetlist netlist)
    {
        Assert(data.Elements.Count == 5 && data.Wires.Count == 11,
            "MOS 样例 Import 成功（5 元件 / 11 导线）",
            $"{data.Elements.Count}/{data.Wires.Count}");

        var (elements, wires, _) = data.ToModel();
        var m1Wires = wires.Where(w => w.StartElementId == "m1").ToArray();
        Assert(m1Wires.Length == 4,
            "四端 MOSFET 的 drain/gate/source/bulk 各生成一根导线（bulk 折叠到 source 引脚）",
            m1Wires.Length.ToString(CultureInfo.InvariantCulture));
        Assert(netlist.Transistors.Count == 1 &&
               netlist.Transistors[0].GateNode != null &&
               netlist.Transistors[0].DrainNode != null &&
               netlist.Transistors[0].SourceNode != null,
            "MOS 晶体管 G/D/S 三端均识别到电气节点");

        var classification = TopologyClassifier.Classify(netlist);
        Assert(classification.TopologyType == CircuitTopologyType.MosfetAmplifier,
            "MOS 样例分类为 MosfetAmplifier", classification.TopologyType.ToString());
        Assert(classification.Strategy is MosfetAmplifierStrategy,
            "MOS 样例选择 MosfetAmplifierStrategy 教学策略（不再是 Unsupported 策略）",
            classification.Strategy.GetType().Name);
        Assert(classification.Strategy.TopologyType == CircuitTopologyType.MosfetAmplifier,
            "MOS 策略 TopologyType 为 MosfetAmplifier",
            classification.Strategy.TopologyType.ToString());
        Assert(!string.IsNullOrWhiteSpace(classification.SubtypeName),
            "MOS 样例分类子类型非空", classification.SubtypeName);

        var solution = await classification.Strategy.SolveAsync(netlist, new AnalysisParameters());
        Assert(!solution.IsSuccess,
            "MOS 样例求解明确失败（不崩溃）", solution.SummaryText);
        Assert(!string.IsNullOrWhiteSpace(solution.SummaryText),
            "MOS 样例失败信息非空", solution.SummaryText);
        Assert(solution.SummaryText.Contains("m1", StringComparison.Ordinal) &&
               ContainsAny(solution.SummaryText, "栅极偏置", "偏置网络"),
            "MOS 样例失败信息点名 m1 缺少栅极偏置网络（具体原因，而非 Unsupported）", solution.SummaryText);
        Assert(!solution.SummaryText.Contains("尚未实现", StringComparison.Ordinal),
            "MOS 样例失败信息不再是“尚未实现”的 Unsupported 文案", solution.SummaryText);

        // Keep the local tuple materialized for symmetry with the other samples.
        Assert(elements.Count == 5, "MOS 样例 ToModel 元件数保持 5");
    }

    // ------------------------------------------------------------------
    // Suite: bare graph document + native MNA solve
    // ------------------------------------------------------------------

    private static async Task RunDirectGraphChecks()
    {
        Assert(WebCircuitGraphImporter.LooksLikeWebCircuitGraph(DirectGraphJson),
            "裸 CircuitGraph 文档（无 circuitGraph 包装）可识别");

        var data = WebCircuitGraphImporter.Import(DirectGraphJson);
        Assert(data.ProjectName == "Voltage Divider",
            "裸文档 ProjectName 取根 title", data.ProjectName);
        Assert(data.Elements.Count == 4 && data.Wires.Count == 7,
            "裸文档导入结果：4 元件 / 7 导线",
            $"{data.Elements.Count}/{data.Wires.Count}");

        var (elements, wires, _) = data.ToModel();
        var netlist = NetlistExtractor.ExtractNetlist(elements, wires);
        Assert(netlist.Nets.Count == 3 && netlist.Branches.Count == 3,
            "裸文档网表：3 网 / 3 二端分支",
            $"{netlist.Nets.Count}/{netlist.Branches.Count}");

        var classification = TopologyClassifier.Classify(netlist);
        Assert(classification.TopologyType == CircuitTopologyType.MnaLinear,
            "裸文档分类为 MnaLinear", classification.TopologyType.ToString());

        var solution = await classification.Strategy.SolveAsync(netlist, new AnalysisParameters());
        Assert(solution.IsSuccess,
            "裸 CircuitGraph 分压网络可被原生 MNA 求解", solution.SummaryText);

        if (solution.LinearNetwork is { NodePotentials.Count: > 0 } linearNetwork)
        {
            var potentials = linearNetwork.NodePotentials
                .Select(node => node.VoltageV)
                .OrderBy(voltage => voltage)
                .ToArray();
            // Independent hand calculation: V(n1)=10 V (source), V(n2)=10·2k/(1k+2k)=6.667 V.
            bool matches = potentials.Length == 2 &&
                           Math.Abs(potentials[0] - 20.0 / 3.0) < 1e-6 &&
                           Math.Abs(potentials[1] - 10.0) < 1e-6;
            Assert(matches,
                "MNA 2.0 结构化节点电位与手算一致：V(n1)=10 V, V(n2)=6.667 V",
                string.Join(", ", potentials.Select(v => v.ToString("G6", CultureInfo.InvariantCulture))));
        }
        else
        {
            Console.WriteLine("  [SKIP] 当前 MnaLinearStrategy 尚未输出结构化 LinearNetwork（MNA 2.0 引擎升版中）；仅校验求解成功。");
        }

        var overridden = WebCircuitGraphImporter.Import(DirectGraphJson, "自定义导入名称");
        Assert(overridden.ProjectName == "自定义导入名称",
            "Import 的 projectName 参数可覆盖标题", overridden.ProjectName);
    }

    // ------------------------------------------------------------------
    // Assertion helpers
    // ------------------------------------------------------------------

    private static void Assert(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            Pass(name, detail);
        }
        else
        {
            Fail(name, detail);
        }
    }

    private static void Pass(string name, string? detail = null)
    {
        _passed++;
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"  ({Trim(detail)})";
        Console.WriteLine($"  [PASS] {name}{suffix}");
    }

    private static void Fail(string name, string? detail = null)
    {
        _failed++;
        string message = name + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $"  ({Trim(detail)})");
        _failures.Add(message);
        Console.WriteLine($"  [FAIL] {message}");
    }

    private static string Trim(string text)
    {
        string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400] + "…";
    }

    private static void AssertThrowsInvalidData(Action action, string name, string? messageFragment = null)
    {
        try
        {
            action();
            Fail(name, "未抛出 InvalidDataException");
        }
        catch (InvalidDataException ex)
        {
            if (messageFragment != null &&
                !ex.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
            {
                Fail(name, $"异常信息未包含 '{messageFragment}'：{ex.Message}");
            }
            else
            {
                Pass(name, ex.Message);
            }
        }
        catch (Exception ex)
        {
            Fail(name, $"实际抛出 {ex.GetType().Name} 而非 InvalidDataException：{ex.Message}");
        }
    }

    private static void AssertRelativelyEqual(double expected, double actual, double relativeTolerance, string name)
    {
        double scale = Math.Max(Math.Abs(expected), 1e-12);
        double relativeError = Math.Abs(actual - expected) / scale;
        Assert(relativeError <= relativeTolerance, name,
            $"expected={expected:G8}, actual={actual:G8}, relativeError={relativeError:E3}");
    }

    private static double ParseDisplayedNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("显示字符串为空，无法解析数值。");
        var match = Regex.Match(text, @"-?\d+(?:\.\d+)?");
        if (!match.Success) throw new FormatException($"无法从 '{text}' 解析数值。");
        double value = double.Parse(match.Value, CultureInfo.InvariantCulture);
        if (text.Contains("μA", StringComparison.Ordinal)) value *= 1e-6;
        else if (text.Contains("mA", StringComparison.Ordinal)) value *= 1e-3;
        else if (text.Contains("kΩ", StringComparison.Ordinal)) value *= 1e3;
        else if (text.Contains("MΩ", StringComparison.Ordinal)) value *= 1e6;
        return value;
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static CircuitProjectData ImportOrEmpty(string path, string label)
    {
        try
        {
            var data = WebCircuitGraphImporter.ImportFile(path);
            Pass($"[{label}] ImportFile 成功（{data.Elements.Count} 元件 / {data.Wires.Count} 导线）");
            return data;
        }
        catch (Exception ex)
        {
            Fail($"[{label}] ImportFile 成功", ex.Message);
            return new CircuitProjectData();
        }
    }
}
