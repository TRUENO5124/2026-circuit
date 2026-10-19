// ============================================================================
// File: tests/NativeBenchmark30/RoundtripChecks.cs
// Description: Supplemental evidence mode (--roundtrip) for the native
//              30-question textbook benchmark. Proves that the 30 benchmark
//              fixtures can be transported to a standard Web/Python CircuitGraph
//              JSON document and imported back into the native project pipeline
//              without changing their electrical topology or numerical results.
//
//              Pipeline per fixture (same fixtures as the 30 checked problems,
//              captured from NetlistBuilder.BuiltNetlists while re-running them):
//                1. CircuitGraphExporter.ExportJson   (CircuitGraph JSON document)
//                2. WebCircuitGraphImporter.Import    (production adapter)
//                3. CircuitProjectData.ToModel()      (native element + wire model)
//                4. NetlistExtractor.ExtractNetlist   (production netlist extraction)
//                   + Netlist.Validate() error-free (implicit-ground warning allowed)
//                5. Structural equivalence against the fixture netlist:
//                   element count/type/parameter (R/C/L/V/I exactly; controlled-source
//                   gain via the same engine gain table; BJT beta), net→pin partition,
//                   branch table, transistor table, unique reference ground.
//                6. Net id/name canonicalization to the fixture (topology-based, never
//                   name-based) so numerical output can be compared field by field.
//                7. Numerical equivalence: classify + solve both netlists through the
//                   production pipeline; compare the derived solution contract
//                   (Q point, AC parameters, all pedagogical step results, MNA node
//                   potentials / branch currents / port equivalents / AC phasors /
//                   frequency response digits). Tolerance 1e-9 relative.
//                   Because the fixture netlist itself is checked against the
//                   textbook hand expectations by the default 30-question mode
//                   (re-run here as a precondition), imported numerical results are
//                   transitively equal to the hand-derived expectations at the same
//                   1% (2% for -3dB sweep interpolation) tolerance.
//
//              Failure reporting distinguishes:
//                - importer-mapping/geometry : Import/ToModel/Extract/Validate fails
//                - topology-mismatch         : pin partition / branch / device tables differ
//                - numeric-mismatch          : solve contract differs
//              Two deliberate negative injections (duplicate port in the JSON,
//              geometry overlap in the imported element model) verify the checker
//              reports failure instead of silently passing.
//
//              Artifacts: CircuitGraph JSON files are written to
//                  tests/NativeBenchmark30/generated/circuitgraph/<id>.json
//              (31 files: M08 consists of two independent sub-circuits M08a/M08b).
//              No application/engine/test source is modified by this mode.
// ============================================================================

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace NativeBenchmark30;

internal static class RoundtripChecks
{
    private const double NumericTolerance = 1e-9;

    public static int Run()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console may be locked down */ }

        Console.WriteLine("================================================================================");
        Console.WriteLine("补 充 证 据：Benchmark fixture → Web CircuitGraph JSON → 原生导入 round-trip 校验");
        Console.WriteLine("路径：Export → WebCircuitGraphImporter.Import → ToModel → ExtractNetlist →");
        Console.WriteLine("      拓扑等价（按拓扑非名字）→ 数值等价（分类/求解结果逐字段，容差 1e-9）");
        Console.WriteLine("================================================================================\n");

        string projectDirectory = FindProjectDirectory();
        string outputDirectory = Path.Combine(projectDirectory, "generated", "circuitgraph");
        Directory.CreateDirectory(outputDirectory);

        // ---- 1. Re-run the 30 problems and capture the exact fixture netlists. -----
        var fixtures = new List<FixtureCapture>();
        int defaultPassed = 0;
        int defaultFailed = 0;
        var defaultFailures = new List<string>();
        foreach (BenchmarkCase benchmarkCase in Problems.BuildAll())
        {
            NetlistBuilder.BuiltNetlists.Clear();
            NetlistBuilder.Audit.Clear();
            IReadOnlyList<CheckOutcome> outcomes;
            try
            {
                outcomes = benchmarkCase.Run();
            }
            catch (Exception ex)
            {
                outcomes = new[]
                {
                    new CheckOutcome
                    {
                        Label = "运行异常",
                        ExpectedText = "fixture 可被引擎求解",
                        ActualText = ex.GetType().Name + ": " + ex.Message,
                        Passed = false
                    }
                };
            }
            bool passed = outcomes.All(o => o.Passed);
            if (passed) defaultPassed++;
            else
            {
                defaultFailed++;
                defaultFailures.Add(benchmarkCase.Id);
            }
            var captured = NetlistBuilder.BuiltNetlists.ToList();
            if (captured.Count > 0)
            {
                fixtures.Add(new FixtureCapture(benchmarkCase.Id, benchmarkCase.Topic, captured));
            }
        }
        int totalNetlists = fixtures.Sum(f => f.Netlists.Count);
        Console.WriteLine($"前置条件：30 题默认模式复跑（fixture 输出 vs 手算期望）：{defaultPassed}/30 PASS，" +
                          $"采集 fixture 网表 {totalNetlists} 个（M08 含电容/电感两个独立子电路）。");
        if (defaultFailed > 0)
        {
            Console.WriteLine($"  [FAIL] 默认模式未全绿：{string.Join(", ", defaultFailures)}；round-trip 结论对失败题不成立。");
        }
        Console.WriteLine();

        // ---- 2. Export → import → structural + numeric equivalence. ----------------
        int passedProblems = 0;
        int failedProblems = 0;
        int exportedNetlists = 0;
        var roundtripFailures = new List<string>();
        var failureCategories = new List<string>();
        int comparedFieldCount = 0;

        foreach (FixtureCapture fixture in fixtures)
        {
            var importedNetlists = new List<ElectricalNetlist>();
            var problems = new List<string>();
            var structureParts = new List<string>();
            string stage = "";
            bool problemOk = true;

            for (int i = 0; i < fixture.Netlists.Count; i++)
            {
                ElectricalNetlist fixtureNetlist = fixture.Netlists[i];
                string circuitId = fixture.Netlists.Count == 1
                    ? fixture.ProblemId
                    : fixture.ProblemId + ((char)('a' + i)).ToString();
                string filePath = Path.Combine(outputDirectory, circuitId + ".json");
                ElectricalNetlist? imported = null;

                // 2a. Export (also writes the importable artifact used by the app).
                try
                {
                    string json = CircuitGraphExporter.ExportJson(fixtureNetlist, circuitId, fixture.Topic);
                    File.WriteAllText(filePath, json, new UTF8Encoding(false));
                    exportedNetlists++;
                }
                catch (Exception ex)
                {
                    problemOk = false;
                    stage = "exporter";
                    problems.Add($"{circuitId}: CircuitGraph 导出失败 {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // 2b. Import through the production adapter + project model + extractor.
                try
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    CircuitProjectData data = WebCircuitGraphImporter.Import(json);
                    (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
                    imported = NetlistExtractor.ExtractNetlist(elements, wires);
                    NetlistValidationResult validation = imported.Validate();
                    if (!validation.IsValid)
                    {
                        string errors = string.Join(" | ", validation.Issues
                            .Where(issue => issue.Severity == NetlistValidationSeverity.Error)
                            .Select(issue => issue.Message));
                        throw new InvalidOperationException("导入网表 Validate 存在 Error：" + errors);
                    }
                }
                catch (Exception ex)
                {
                    problemOk = false;
                    stage = "importer/geometry";
                    problems.Add($"{circuitId}: Import→ToModel→ExtractNetlist→Validate 失败 {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // 2c. Structural equivalence before any renaming (topology, not names).
                StructuralResult structural = CompareTopology(fixtureNetlist, imported);
                if (!structural.Ok)
                {
                    problemOk = false;
                    stage = "topology-mismatch";
                    problems.Add($"{circuitId}: 拓扑不等价 — {structural.Detail}");
                    continue;
                }

                // 2d. Canonicalize net identities to the fixture (by pin partition) so the
                //     remaining contract comparison can be a direct field-by-field diff.
                try
                {
                    CanonicalizeNets(imported, fixtureNetlist);
                }
                catch (Exception ex)
                {
                    problemOk = false;
                    stage = "canonicalization";
                    problems.Add($"{circuitId}: 网表规范化失败 {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // 2e. Numerical contract equivalence.
                List<SignatureEntry> expectedSignature = BuildEngineSignature(fixtureNetlist);
                List<SignatureEntry> actualSignature = BuildEngineSignature(imported);
                if (!CompareSignatures(expectedSignature, actualSignature, out string signatureDetail))
                {
                    problemOk = false;
                    stage = "numeric-mismatch";
                    problems.Add($"{circuitId}: 求解结果不等价 — {signatureDetail}");
                    continue;
                }

                comparedFieldCount += expectedSignature.Count;
                structureParts.Add(DescribeStructure(fixtureNetlist));
                importedNetlists.Add(imported);
            }

            if (problemOk)
            {
                passedProblems++;
                Console.WriteLine($"[PASS] RT-{fixture.ProblemId} {fixture.Topic} | netlists={fixture.Netlists.Count} | " +
                                  $"结构=等价（{string.Join("；", structureParts)}） | " +
                                  $"数值=等价（逐字段 ≤{NumericTolerance:G2} rel；传递手算期望 1%/扫频 2%）");
            }
            else
            {
                failedProblems++;
                roundtripFailures.Add(fixture.ProblemId);
                failureCategories.Add($"{fixture.ProblemId}:{stage}");
                Console.WriteLine($"[ROUNDTRIP-FAIL] RT-{fixture.ProblemId} {fixture.Topic} | 分类={stage}");
                foreach (string problem in problems)
                {
                    Console.WriteLine("        -> " + problem);
                }
            }
        }

        // ---- 3. Deliberate negative injections. ------------------------------------
        Console.WriteLine();
        ElectricalNetlist? f01Fixture = fixtures.FirstOrDefault(f => f.ProblemId == "F01")?.Netlists.FirstOrDefault();
        bool negativeDuplicatePort = NegativeDuplicatePort(outputDirectory);
        bool negativeGeometryOverlap = NegativeGeometryOverlap(outputDirectory, f01Fixture, fixtures);
        Console.WriteLine($"[{(negativeDuplicatePort ? "PASS" : "FAIL")}] NEG-1 故意重复端口 JSON → 导入必须被拒绝（不得静默通过）");
        Console.WriteLine($"[{(negativeGeometryOverlap ? "PASS" : "FAIL")}] NEG-2 故意几何重叠 → 拓扑/校验比较必须检出（不得静默通过）");

        // ---- 4. Summary. ------------------------------------------------------------
        int negativePassed = (negativeDuplicatePort ? 1 : 0) + (negativeGeometryOverlap ? 1 : 0);
        Console.WriteLine();
        Console.WriteLine($"RoundTripTotal={fixtures.Count} Passed={passedProblems} Failed={failedProblems} " +
                          $"Accuracy={(fixtures.Count == 0 ? 0.0 : passedProblems * 100.0 / fixtures.Count):0.00}% " +
                          $"Netlists={exportedNetlists}/{totalNetlists} NegativeChecks={negativePassed}/2");
        Console.WriteLine($"DefaultMode={defaultPassed}/30 PASS（手算期望前置条件） JsonDir={outputDirectory}");
        Console.WriteLine($"比较字段={comparedFieldCount} 个（节点电位/支路电流/任意端口 Req+Voc/Q 点/AcParameters/全部推导步骤/MNA AC 相量/频响 61 点等）");
        Console.WriteLine("数值等价链：imported ≡ fixture（≤1e-9 rel）且 fixture ↔ 手算期望（默认模式 30/30 PASS；默认容差 1%，扫频截止 2%）");
        Console.WriteLine("            ⇒ imported ↔ 手算期望（同一 1%/2% 容差）。");
        if (failedProblems > 0)
        {
            Console.WriteLine();
            Console.WriteLine("ROUNDTRIP FAILURES（逐项分类）:");
            foreach (string category in failureCategories) Console.WriteLine("  " + category);
            Console.WriteLine("失败题清单: " + string.Join(", ", roundtripFailures));
        }

        bool allGood = failedProblems == 0 && negativePassed == 2 && defaultFailed == 0;
        if (!allGood)
        {
            return 1;
        }
        Console.WriteLine();
        Console.WriteLine("结论：30 题 fixture 全部可作为 Web CircuitGraph JSON 导入回原生工程，拓扑与数值等价。");
        return 0;
    }

    // =====================================================================
    // Fixture capture type
    // =====================================================================

    private sealed record FixtureCapture(string ProblemId, string Topic, List<ElectricalNetlist> Netlists);

    // =====================================================================
    // Structural equivalence (before renaming)
    // =====================================================================

    private sealed record StructuralResult(bool Ok, string Detail);

    private static string DescribeStructure(ElectricalNetlist netlist)
    {
        int controlledSources = netlist.Elements.Values
            .Count(e => e.Type is ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs);
        return $"nets={netlist.Nets.Count} branches={netlist.Branches.Count} BJT={netlist.Transistors.Count} 受控源={controlledSources}";
    }

    private static StructuralResult CompareTopology(ElectricalNetlist expected, ElectricalNetlist actual)
    {
        // Element tables exclude the synthetic GND symbol (the fixture netlist represents
        // ground by net 0 only); the net partition maps below keep the full element map so
        // the imported GND symbol pin can be filtered by element type before comparing.
        Dictionary<string, CircuitElement> expectedAllElements = expected.Elements.Values
            .ToDictionary(e => e.Id, StringComparer.Ordinal);
        Dictionary<string, CircuitElement> actualAllElements = actual.Elements.Values
            .ToDictionary(e => e.Id, StringComparer.Ordinal);
        Dictionary<string, CircuitElement> expectedElements = expectedAllElements.Values
            .Where(e => e.Type != ElementType.Ground)
            .ToDictionary(e => e.Id, StringComparer.Ordinal);
        Dictionary<string, CircuitElement> actualElements = actualAllElements.Values
            .Where(e => e.Type != ElementType.Ground)
            .ToDictionary(e => e.Id, StringComparer.Ordinal);

        if (expectedElements.Count != actualElements.Count)
        {
            return Fail($"元件数 {expectedElements.Count}（fixture）vs {actualElements.Count}（导入）");
        }
        foreach ((string id, CircuitElement expectedElement) in expectedElements.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!actualElements.TryGetValue(id, out CircuitElement? actualElement))
            {
                return Fail($"导入网表缺少元件 {id}");
            }
            if (expectedElement.Type != actualElement.Type)
            {
                return Fail($"元件 {id} 类型 {expectedElement.Type} vs {actualElement.Type}");
            }
            if (!ValueEqual(ElementSiValue(expectedElement), ElementSiValue(actualElement)))
            {
                return Fail($"元件 {id} 参数 {FormatNumber(ElementSiValue(expectedElement))} vs {FormatNumber(ElementSiValue(actualElement))}（SI）");
            }
        }

        string expectedPartition = NetPinPartition(expected, expectedAllElements);
        string actualPartition = NetPinPartition(actual, actualAllElements);
        if (!string.Equals(expectedPartition, actualPartition, StringComparison.Ordinal))
        {
            return Fail("网→引脚划分不一致：" + DescribePartitionDifference(expectedPartition, actualPartition));
        }

        List<string> expectedBranches = expected.Branches
            .Select(b => b.Element.Id + "=" + FormatNumber(ElementSiValue(b.Element)))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        List<string> actualBranches = actual.Branches
            .Select(b => b.Element.Id + "=" + FormatNumber(ElementSiValue(b.Element)))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (!expectedBranches.SequenceEqual(actualBranches, StringComparer.Ordinal))
        {
            return Fail($"两端支路表不一致：fixture[{string.Join(",", expectedBranches)}] vs 导入[{string.Join(",", actualBranches)}]");
        }

        List<string> expectedTransistors = expected.Transistors
            .Select(t => t.Element.Id + "=" + FormatNumber(ElementSiValue(t.Element)))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        List<string> actualTransistors = actual.Transistors
            .Select(t => t.Element.Id + "=" + FormatNumber(ElementSiValue(t.Element)))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (!expectedTransistors.SequenceEqual(actualTransistors, StringComparer.Ordinal))
        {
            return Fail($"晶体管表不一致：fixture[{string.Join(",", expectedTransistors)}] vs 导入[{string.Join(",", actualTransistors)}]");
        }

        List<ElectricalNet> expectedGrounds = expected.Nets.Where(n => n.IsGround).ToList();
        List<ElectricalNet> actualGrounds = actual.Nets.Where(n => n.IsGround).ToList();
        if (expectedGrounds.Count != 1 || actualGrounds.Count != 1)
        {
            return Fail($"参考地数量不是 1：fixture={expectedGrounds.Count}，导入={actualGrounds.Count}");
        }
        if (expected.Nets.Count != actual.Nets.Count)
        {
            return Fail($"网数 {expected.Nets.Count}（fixture）vs {actual.Nets.Count}（导入）");
        }
        return Ok();
    }

    private static StructuralResult Fail(string detail) => new(false, detail);
    private static StructuralResult Ok() => new(true, "等价");

    /// <summary>Multiset of "sorted pin list" strings for every net (ignores net ids/names).</summary>
    private static string NetPinPartition(ElectricalNetlist netlist, Dictionary<string, CircuitElement> elementsById)
    {
        var netStrings = new List<string>(netlist.Nets.Count);
        foreach (ElectricalNet net in netlist.Nets)
        {
            var pins = net.ConnectedPins
                .Where(pin => !(elementsById.TryGetValue(pin.ElementId, out CircuitElement? element) &&
                                element.Type == ElementType.Ground))
                .Select(pin => pin.ElementId + "." + pin.PinId)
                .OrderBy(s => s, StringComparer.Ordinal);
            netStrings.Add(string.Join(",", pins));
        }
        netStrings.Sort(StringComparer.Ordinal);
        return string.Join("|", netStrings);
    }

    private static string DescribePartitionDifference(string expected, string actual)
    {
        var expectedNets = expected.Split('|', StringSplitOptions.None).ToHashSet(StringComparer.Ordinal);
        var actualNets = actual.Split('|', StringSplitOptions.None).ToHashSet(StringComparer.Ordinal);
        var missing = expectedNets.Except(actualNets, StringComparer.Ordinal).Take(3).ToList();
        var extra = actualNets.Except(expectedNets, StringComparer.Ordinal).Take(3).ToList();
        var parts = new List<string>();
        if (missing.Count > 0) parts.Add("fixture 有而导入缺失: [" + string.Join("] [", missing) + "]");
        if (extra.Count > 0) parts.Add("导入多出: [" + string.Join("] [", extra) + "]");
        return parts.Count == 0 ? "网集合相同但排序键不同" : string.Join("；", parts);
    }

    /// <summary>Parameter in base SI units using the production conversion tables.</summary>
    private static double ElementSiValue(CircuitElement element)
    {
        if (element.Type is ElementType.BjtNpn or ElementType.BjtPnp)
        {
            return element.DisplayNumber; // β
        }
        if (element.Type is ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs)
        {
            return LinearNetworkAnalyzer.ConvertControlledSourceGain(element, new List<string>());
        }
        return NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
    }

    // =====================================================================
    // Canonicalization: topology-based net id/name mapping to the fixture
    // =====================================================================

    private static void CanonicalizeNets(ElectricalNetlist imported, ElectricalNetlist fixture)
    {
        // Full element maps are required so the synthetic GND symbol pin (imported side)
        // can be filtered out of the net pin keys before matching nets by topology.
        Dictionary<string, CircuitElement> fixtureElements = fixture.Elements.Values
            .ToDictionary(e => e.Id, StringComparer.Ordinal);
        Dictionary<string, CircuitElement> importedElements = imported.Elements.Values
            .ToDictionary(e => e.Id, StringComparer.Ordinal);

        var fixtureByPinSet = new Dictionary<string, ElectricalNet>(StringComparer.Ordinal);
        foreach (ElectricalNet net in fixture.Nets)
        {
            string key = NetPinKey(net, fixtureElements);
            if (!fixtureByPinSet.TryAdd(key, net))
            {
                throw new InvalidOperationException("fixture 中存在引脚集合相同的两个网，无法按拓扑映射。");
            }
        }

        var mapping = new List<(ElectricalNet Imported, ElectricalNet Fixture)>(imported.Nets.Count);
        foreach (ElectricalNet net in imported.Nets)
        {
            string key = NetPinKey(net, importedElements);
            if (!fixtureByPinSet.TryGetValue(key, out ElectricalNet? fixtureNet))
            {
                throw new InvalidOperationException($"导入网 '{net.Name}'（引脚 {key}）在 fixture 中不存在。");
            }
            mapping.Add((net, fixtureNet));
        }

        // Two phases: first move every id out of the fixture range, then assign the
        // fixture ids/names unchanged. NetId 0 is the reference ground in both netlists.
        foreach ((ElectricalNet net, _) in mapping)
        {
            net.NetId = -1_000_000 - net.NetId;
        }
        foreach ((ElectricalNet net, ElectricalNet fixtureNet) in mapping)
        {
            net.NetId = fixtureNet.NetId;
            net.Name = fixtureNet.Name;
            net.IsGround = fixtureNet.IsGround;
            net.IsPower = fixtureNet.IsPower;
        }
        foreach (ElectricalNode node in imported.Nodes)
        {
            if (node.Net == null) continue;
            node.Index = node.Net.NetId;
            node.Name = node.Net.Name;
        }
    }

    private static string NetPinKey(ElectricalNet net, Dictionary<string, CircuitElement> elementsById)
    {
        var pins = net.ConnectedPins
            .Where(pin => !(elementsById.TryGetValue(pin.ElementId, out CircuitElement? element) &&
                            element.Type == ElementType.Ground))
            .Select(pin => pin.ElementId + "." + pin.PinId)
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join(",", pins);
    }

    // =====================================================================
    // Numerical equivalence: full engine solution contract, field by field
    // =====================================================================

    private sealed record SignatureEntry(string Key, string? Text, double Number, bool IsNumeric);

    private static SignatureEntry TextEntry(string key, string? text) => new(key, text, 0.0, false);
    private static SignatureEntry NumberEntry(string key, double number) => new(key, null, number, true);

    private static List<SignatureEntry> BuildEngineSignature(ElectricalNetlist netlist)
    {
        var entries = new List<SignatureEntry>();

        // --- Dedicated strategy path (RC/RL, RLC, BJT or MNA strategy). ---
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        entries.Add(TextEntry("classify", $"{classification.TopologyType}/{classification.SubtypeName}"));
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
        entries.Add(TextEntry("solution.success", solution.IsSuccess ? "true" : "false"));
        entries.Add(TextEntry("solution.summary", solution.SummaryText));
        if (solution.QPoint is { } qPoint)
        {
            entries.Add(TextEntry("q.ibq", qPoint.Ibq));
            entries.Add(TextEntry("q.icq", qPoint.Icq));
            entries.Add(TextEntry("q.vceq", qPoint.Vceq));
            entries.Add(TextEntry("q.region", qPoint.WorkingRegion));
        }
        if (solution.AcParameters is { } ac)
        {
            entries.Add(TextEntry("ac.av", ac.VoltageGain));
            entries.Add(TextEntry("ac.ri", ac.InputResistance));
            entries.Add(TextEntry("ac.ro", ac.OutputResistance));
            entries.Add(TextEntry("ac.fl", ac.LowerCutoffFreq));
            entries.Add(TextEntry("ac.fh", ac.UpperCutoffFreq));
            entries.Add(TextEntry("ac.bw", ac.Bandwidth));
        }
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            entries.Add(TextEntry($"step[{step.StepIndex}].title", step.Title));
            entries.Add(TextEntry($"step[{step.StepIndex}].result", step.ResultValue));
        }
        if (solution.LinearNetwork is { } network)
        {
            entries.Add(NumberEntry("lin.dim", network.MatrixDimension));
            entries.Add(TextEntry("lin.mode", network.OperatingMode));
            entries.Add(TextEntry("lin.model", network.ModelSummary));
            foreach (NodePotentialInfo node in network.NodePotentials)
            {
                entries.Add(NumberEntry($"lin.node[{node.NetName}].v", node.VoltageV));
                entries.Add(TextEntry($"lin.node[{node.NetName}].display", node.Display));
            }
            foreach (PortEquivalentInfo port in network.PortEquivalents)
            {
                string key = $"lin.port[{port.PositiveNet}-{port.NegativeNet}]";
                entries.Add(NumberEntry(key + ".req", port.ResistanceOhm));
                entries.Add(TextEntry(key + ".hasVoc", port.HasOpenCircuitVoltage ? "yes" : "no"));
                if (port.HasOpenCircuitVoltage) entries.Add(NumberEntry(key + ".voc", port.OpenCircuitVoltageV));
            }
            if (network.FrequencyResponse is { } response)
            {
                entries.Add(NumberEntry("fr.f", response.OperatingFrequencyHz));
                entries.Add(NumberEntry("fr.gain", response.GainRatio));
                entries.Add(NumberEntry("fr.db", response.GainDb));
                entries.Add(NumberEntry("fr.phase", response.PhaseDeg));
                entries.Add(NumberEntry("fr.points", response.Points.Count));
                entries.Add(TextEntry("fr.hasLower", response.LowerCutoffHz.HasValue ? "yes" : "no"));
                entries.Add(TextEntry("fr.hasUpper", response.UpperCutoffHz.HasValue ? "yes" : "no"));
                entries.Add(TextEntry("fr.hasBw", response.BandwidthHz.HasValue ? "yes" : "no"));
                if (response.LowerCutoffHz is double lower) entries.Add(NumberEntry("fr.lower", lower));
                if (response.UpperCutoffHz is double upper) entries.Add(NumberEntry("fr.upper", upper));
                if (response.BandwidthHz is double bandwidth) entries.Add(NumberEntry("fr.bw", bandwidth));
                foreach (AcSweepPointInfo point in response.Points)
                {
                    entries.Add(NumberEntry($"fr.pt[{point.FrequencyHz}].gain", point.GainRatio));
                    entries.Add(NumberEntry($"fr.pt[{point.FrequencyHz}].db", point.GainDb));
                    entries.Add(NumberEntry($"fr.pt[{point.FrequencyHz}].phase", point.PhaseDeg));
                }
            }
        }

        // --- General MNA analyzer path (independent of the teaching strategy). ---
        if (LinearNetworkAnalyzer.TryCreate(netlist, out LinearNetworkAnalyzer? model, out _) && model != null)
        {
            entries.Add(NumberEntry("mna.dim", model.MatrixDimension));
            if (model.TrySolveDc(out LinearSolution? dc, out _) && dc != null)
            {
                foreach (LinearNetNode node in model.Nodes.OrderBy(n => n.NetId))
                {
                    entries.Add(NumberEntry($"mna.dc.node[{node.NetId}]", dc.NodePhasor(node.NetId).Real));
                }
                foreach (LinearDevice device in model.Devices
                             .Where(d => d.BranchVariableIndex >= 0)
                             .OrderBy(d => d.Element.Id, StringComparer.Ordinal))
                {
                    entries.Add(NumberEntry($"mna.dc.branch[{device.ElementId}]", dc.BranchPhasor(device.ElementId).Real));
                }
                var netIds = model.Nodes.Select(n => n.NetId).Append(model.GroundNetId).OrderBy(id => id).ToList();
                for (int i = 0; i < netIds.Count; i++)
                {
                    for (int j = i + 1; j < netIds.Count; j++)
                    {
                        if (model.TryGetEquivalentResistance(netIds[i], netIds[j], out double resistance, out _))
                        {
                            entries.Add(NumberEntry($"mna.req[{netIds[i]},{netIds[j]}]", resistance));
                        }
                        double openCircuit = dc.NodePhasor(netIds[i]).Real - dc.NodePhasor(netIds[j]).Real;
                        if (double.IsFinite(openCircuit))
                        {
                            entries.Add(NumberEntry($"mna.voc[{netIds[i]},{netIds[j]}]", openCircuit));
                        }
                    }
                }
            }
            if (model.HasAcExcitation && model.PreferredOperatingFrequencyHz is double frequency)
            {
                if (model.TrySolveAc(frequency, out LinearSolution? acSolution, out _) && acSolution != null)
                {
                    foreach (LinearNetNode node in model.Nodes.OrderBy(n => n.NetId))
                    {
                        entries.Add(NumberEntry($"mna.ac[{node.NetId}].mag", acSolution.NodeMagnitude(node.NetId)));
                        entries.Add(NumberEntry($"mna.ac[{node.NetId}].phase", acSolution.NodePhaseDeg(node.NetId)));
                    }
                }
            }
        }

        return entries;
    }

    private static bool CompareSignatures(
        List<SignatureEntry> expected, List<SignatureEntry> actual, out string detail)
    {
        detail = string.Empty;
        if (expected.Count != actual.Count)
        {
            detail = $"字段数不一致：fixture={expected.Count}，导入={actual.Count}";
            // Still try to locate the first key-level difference for context.
        }
        int count = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < count; i++)
        {
            SignatureEntry left = expected[i];
            SignatureEntry right = actual[i];
            if (!string.Equals(left.Key, right.Key, StringComparison.Ordinal))
            {
                detail = $"第 {i} 个字段键不一致：fixture '{left.Key}'，导入 '{right.Key}'";
                return false;
            }
            if (left.IsNumeric != right.IsNumeric)
            {
                detail = $"字段 '{left.Key}' 类型不一致";
                return false;
            }
            if (!left.IsNumeric)
            {
                if (!string.Equals(left.Text, right.Text, StringComparison.Ordinal))
                {
                    detail = $"字段 '{left.Key}' 文本不一致：fixture='{Trim(left.Text)}'，导入='{Trim(right.Text)}'";
                    return false;
                }
                continue;
            }
            if (!NumberEqual(left.Number, right.Number))
            {
                detail = $"字段 '{left.Key}' 数值不一致：fixture={FormatNumber(left.Number)}，导入={FormatNumber(right.Number)}";
                return false;
            }
        }
        return expected.Count == actual.Count;
    }

    private static bool NumberEqual(double left, double right)
    {
        if (double.IsNaN(left) || double.IsNaN(right)) return double.IsNaN(left) && double.IsNaN(right);
        if (double.IsInfinity(left) || double.IsInfinity(right)) return left == right;
        double scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= NumericTolerance * scale;
    }

    private static bool ValueEqual(double left, double right) => NumberEqual(left, right);

    private static string Trim(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= 160 ? single : single[..160] + "…";
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "+∞";
        if (double.IsNegativeInfinity(value)) return "-∞";
        return value.ToString("G10", CultureInfo.InvariantCulture);
    }

    // =====================================================================
    // Negative injections: the checker must report failure, never pass silently
    // =====================================================================

    private static bool NegativeDuplicatePort(string outputDirectory)
    {
        try
        {
            string path = Path.Combine(outputDirectory, "F01.json");
            if (!File.Exists(path))
            {
                Console.WriteLine("        NEG-1 无法执行：缺少 F01.json");
                return false;
            }
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
            JsonArray? connections = root?["circuitGraph"]?["connections"]?.AsArray();
            if (connections == null) return false;

            var r1Indices = new List<int>();
            for (int i = 0; i < connections.Count; i++)
            {
                if (string.Equals((string?)connections[i]?["componentId"], "R1", StringComparison.Ordinal))
                {
                    r1Indices.Add(i);
                }
            }
            if (r1Indices.Count < 2) return false;
            connections[r1Indices[1]]!["portId"] = connections[r1Indices[0]]!["portId"]!.GetValue<string>();
            string corrupted = root!.ToJsonString();

            try
            {
                WebCircuitGraphImporter.Import(corrupted);
                Console.WriteLine("        NEG-1 未被检测：重复端口 JSON 被静默导入（检查器失效）");
                return false;
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("重复连接", StringComparison.Ordinal))
            {
                Console.WriteLine($"        NEG-1 预期拒绝：{Trim(ex.Message)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"        NEG-1 拒绝原因非预期：{ex.GetType().Name}: {Trim(ex.Message)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"        NEG-1 注入异常：{ex.GetType().Name}: {Trim(ex.Message)}");
            return false;
        }
    }

    private static bool NegativeGeometryOverlap(
        string outputDirectory, ElectricalNetlist? f01Fixture, List<FixtureCapture> fixtures)
    {
        try
        {
            string path = Path.Combine(outputDirectory, "F01.json");
            if (!File.Exists(path))
            {
                Console.WriteLine("        NEG-2 无法执行：缺少 F01.json");
                return false;
            }
            ElectricalNetlist? fixture = f01Fixture ?? fixtures.FirstOrDefault(f => f.ProblemId == "F01")?.Netlists.FirstOrDefault();
            if (fixture == null) return false;

            CircuitProjectData data = WebCircuitGraphImporter.Import(File.ReadAllText(path, Encoding.UTF8));
            (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
            CircuitElement? capacitor = elements.FirstOrDefault(e => e.Id == "C1");
            CircuitElement? resistor = elements.FirstOrDefault(e => e.Id == "R1");
            CircuitWire? capacitorWire = wires.FirstOrDefault(w => w.StartElementId == "C1");
            if (capacitor == null || resistor == null || capacitorWire == null) return false;

            // Deliberate geometry accident: move R1 so its pin 1 lands exactly on the
            // open wire end (node coordinate) of C1, producing a spurious connection and
            // orphaning R1's own leads. The extractor must not silently reproduce the
            // fixture topology; the checker must report it.
            CircuitPin pin = resistor.Pins.First(p => p.Id == "1");
            resistor.X = capacitorWire.EndX - pin.RelativeX;
            resistor.Y = capacitorWire.EndY - pin.RelativeY;

            ElectricalNetlist extracted = NetlistExtractor.ExtractNetlist(elements, wires);
            Dictionary<string, CircuitElement> fixtureElements = fixture.Elements.Values
                .ToDictionary(e => e.Id, StringComparer.Ordinal);
            Dictionary<string, CircuitElement> extractedElements = extracted.Elements.Values
                .ToDictionary(e => e.Id, StringComparer.Ordinal);
            string fixturePartition = NetPinPartition(fixture, fixtureElements);
            string extractedPartition = NetPinPartition(extracted, extractedElements);
            bool topologyDetected = !string.Equals(fixturePartition, extractedPartition, StringComparison.Ordinal);
            bool validationDetected = !extracted.Validate().IsValid;
            Console.WriteLine($"        NEG-2 注入几何重叠后：拓扑{(topologyDetected ? "不同（已检出）" : "相同（未检出）")}，" +
                              $"Validate {(validationDetected ? "失败（已检出）" : "通过")}");
            return topologyDetected || validationDetected;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"        NEG-2 注入异常：{ex.GetType().Name}: {Trim(ex.Message)}");
            return false;
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>Locates tests/NativeBenchmark30 (walking up from the build output directory).</summary>
    private static string FindProjectDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory != null; i++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NativeBenchmark30.csproj")))
            {
                return directory.FullName;
            }
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "tests", "NativeBenchmark30");
    }
}
