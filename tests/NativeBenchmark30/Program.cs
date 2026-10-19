// ============================================================================
// File: tests/NativeBenchmark30/Program.cs
// Description: NATIVE 30-QUESTION TEXTBOOK BENCHMARK HARNESS.
//
//              This console project links the production CircuitAssistant engine
//              sources directly (same pattern as tests/EngineV2Checks /
//              tests/M2SolverChallenger; it is NOT a dotnet-test project) and
//              runs 30 independent textbook problems through the real engine
//              pipeline:
//                  TopologyClassifier -> dedicated strategy (RC/RL/RLC/BJT)
//                  LinearNetworkAnalyzer / MnaLinearStrategy (MNA 2.0)
//              Every problem carries its own hand-derived textbook expectation
//              (written as comments + independent formulas in Problems.cs). The
//              engine result is compared against those expectations with a
//              relative tolerance (1% by default; per-check overrides are shown
//              in the report). Engine implementation formulas are NOT reused as
//              expectations: expectations are standard textbook relations
//              (三要素法 / 二阶特征根 / BJT 微变等效模型 / 节点分析与电路定理).
//
//              Report format: one line per problem
//                  [PASS]/[FAIL] <id> <topic> expected=... actual=... err=...%
//              followed by the summary line
//                  Total=30 Passed=y Failed=z Accuracy=(y/30)%
//              Exit code is non-zero when any problem fails.
//
//              Run with:
//                  dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
//              Optional topology audit (prints the extracted net->pin incidence of
//              every fixture circuit for manual schematic cross-checking):
//                  dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj -- --topology
//              Optional round-trip evidence mode (supplemental; default mode unchanged):
//              re-runs the same 30 fixtures, exports them as Web/Python CircuitGraph
//              JSON to tests/NativeBenchmark30/generated/circuitgraph/, re-imports them
//              through Services/WebCircuitGraphImporter + CircuitProjectData.ToModel +
//              NetlistExtractor, then checks structural and numerical equivalence and
//              runs two deliberate negative injections:
//                  dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj -- --roundtrip
//              Every fixture netlist additionally passes a hard structural self-check
//              in NetlistBuilder.VerifyTopology() (floating pins, element shorts,
//              duplicate bindings, branch/net mismatches, ground uniqueness) before
//              it reaches the engine, so a PASS cannot come from a mis-wired circuit.
// ============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Models;

namespace NativeBenchmark30;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may be locked down */ }
        bool topologyMode = args.Any(a => string.Equals(a, "--topology", StringComparison.OrdinalIgnoreCase));
        bool roundtripMode = args.Any(a => string.Equals(a, "--roundtrip", StringComparison.OrdinalIgnoreCase));
        if (roundtripMode)
        {
            // Supplemental evidence mode: exports the 30 benchmark fixtures to Web/Python
            // CircuitGraph JSON, re-imports them through WebCircuitGraphImporter and checks
            // structural + numeric equivalence. The default benchmark mode is unchanged.
            return RoundtripChecks.Run();
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("原 生 30 题 教 材 BENCHMARK（RC / RL / RLC / BJT / MNA 2.0）");
        Console.WriteLine("引擎输出 vs 独立手算期望；相对容差默认 1%，逐题注明例外；失败退出码非 0");
        if (topologyMode) Console.WriteLine("拓扑审计模式(--topology)：逐题打印构建网表的 net→引脚映射与元件参数，供人工核对");
        Console.WriteLine("================================================================================");

        List<BenchmarkCase> cases = Problems.BuildAll();
        if (cases.Count != 30)
        {
            Console.WriteLine($"[FAIL] benchmark 题数不是 30 而是 {cases.Count}");
            return 2;
        }

        int passed = 0;
        int failed = 0;
        int limited = 0;
        var failureDetails = new List<string>();

        foreach (BenchmarkCase benchmarkCase in cases)
        {
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
                        ExpectedText = "引擎完成求解并返回结构化结果",
                        ActualText = ex.GetType().Name + ": " + ex.Message,
                        Passed = false
                    }
                };
            }

            List<CheckOutcome> bad = outcomes.Where(o => !o.Passed).ToList();
            bool ok = bad.Count == 0 && !benchmarkCase.Limited;
            if (benchmarkCase.Limited) limited++;
            else if (ok) passed++;
            else
            {
                failed++;
                failureDetails.Add(benchmarkCase.Id);
            }

            string status = benchmarkCase.Limited ? "LIMITED" : ok ? "PASS" : "FAIL";
            string expectedText = string.Join("；", outcomes.Select(o => o.Label + "=" + o.ExpectedText));
            string actualText = string.Join("；", outcomes.Select(o => o.Label + "=" + o.ActualText));
            string errorText = FormatRowError(outcomes);

            Console.WriteLine($"[{status}] {benchmarkCase.Id} {benchmarkCase.Topic} | expected={expectedText} | actual={actualText} | err={errorText}");

            if (!ok)
            {
                if (benchmarkCase.Limited && bad.Count == 0)
                {
                    Console.WriteLine($"        [LIMITED] {benchmarkCase.LimitReason}");
                }
                foreach (CheckOutcome check in bad)
                {
                    string tol = double.IsNaN(check.Tolerance)
                        ? "文本匹配"
                        : $"tol={check.Tolerance * 100:0.###}%";
                    Console.WriteLine($"        -> FAIL {check.Label}: expected={check.ExpectedText}, actual={check.ActualText} ({tol}) {check.Note}");
                }
            }

            if (topologyMode)
            {
                foreach (string entry in NetlistBuilder.Audit)
                {
                    Console.WriteLine("        [拓扑] " + entry);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Total={cases.Count} Passed={passed} Failed={failed} Accuracy={passed * 100.0 / cases.Count:0.00}%");
        if (limited > 0)
        {
            Console.WriteLine($"Limited={limited}（引擎明确边界，单独统计，不计入 Passed/Failed）");
        }
        if (failed > 0)
        {
            Console.WriteLine("FAILED: " + string.Join(", ", failureDetails));
            return 1;
        }
        return 0;
    }

    private static string FormatRowError(IReadOnlyList<CheckOutcome> outcomes)
    {
        List<CheckOutcome> numeric = outcomes.Where(o => !double.IsNaN(o.RelativeError)).ToList();
        if (numeric.Count == 0) return "—（文本/拓扑断言）";
        double maxError = numeric.Max(o => o.RelativeError);
        double maxTolerance = numeric.Max(o => o.Tolerance);
        return $"{maxError * 100:0.######}%（tol≤{maxTolerance * 100:0.###}%）";
    }
}

// ============================================================================
// Result + assertion primitives
// ============================================================================

internal sealed class CheckOutcome
{
    public string Label { get; init; } = string.Empty;
    public string ExpectedText { get; init; } = string.Empty;
    public string ActualText { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public double RelativeError { get; init; } = double.NaN;
    public double Tolerance { get; init; } = double.NaN;
    public string Note { get; init; } = string.Empty;
}

internal sealed class BenchmarkCase
{
    public string Id { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public Func<IReadOnlyList<CheckOutcome>> Run { get; init; } = () => Array.Empty<CheckOutcome>();
    public bool Limited { get; init; }
    public string LimitReason { get; init; } = string.Empty;
}

internal static class Checks
{
    public const double DefaultTolerance = 0.01; // 1 %

    /// <summary>Relative-error check: |actual - expected| / max(|expected|, 1e-12) &lt;= relativeTolerance.</summary>
    public static CheckOutcome Number(string label, double expected, double actual, double relativeTolerance = DefaultTolerance, string unit = "", string note = "")
    {
        double denominator = Math.Max(Math.Abs(expected), 1e-12);
        double error = Math.Abs(actual - expected) / denominator;
        bool ok = double.IsFinite(actual) && double.IsFinite(expected) && error <= relativeTolerance;
        return new CheckOutcome
        {
            Label = label,
            ExpectedText = FormatQuantity(expected, unit),
            ActualText = FormatQuantity(actual, unit),
            Passed = ok,
            RelativeError = double.IsFinite(error) ? error : double.NaN,
            Tolerance = relativeTolerance,
            Note = note
        };
    }

    /// <summary>Absolute-tolerance check for quantities whose textbook expectation is exactly zero.</summary>
    public static CheckOutcome NearZero(string label, double actual, double absoluteTolerance, string unit = "", string note = "")
    {
        bool ok = double.IsFinite(actual) && Math.Abs(actual) <= absoluteTolerance;
        return new CheckOutcome
        {
            Label = label,
            ExpectedText = FormatQuantity(0.0, unit),
            ActualText = FormatQuantity(actual, unit),
            Passed = ok,
            RelativeError = ok ? 0.0 : 1.0,
            Tolerance = double.NaN,
            Note = note
        };
    }

    /// <summary>Text/topology assertion: passes when the engine text contains the expected token.</summary>
    public static CheckOutcome TextContains(string label, string expectedToken, string actualText)
    {
        return new CheckOutcome
        {
            Label = label,
            ExpectedText = expectedToken,
            ActualText = string.IsNullOrWhiteSpace(actualText) ? "（空）" : actualText,
            Passed = actualText?.Contains(expectedToken, StringComparison.Ordinal) == true
        };
    }

    /// <summary>Strict boolean assertion (e.g. "no -3 dB upper cutoff must be reported").</summary>
    public static CheckOutcome Flag(string label, bool expected, bool actual, string note = "")
    {
        return new CheckOutcome
        {
            Label = label,
            ExpectedText = expected ? "是" : "否",
            ActualText = actual ? "是" : "否",
            Passed = expected == actual,
            Note = note
        };
    }

    public static string FormatQuantity(double value, string unit)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞" + unit;
        if (double.IsNegativeInfinity(value)) return "-∞" + unit;
        double magnitude = Math.Abs(value);
        string number = magnitude != 0 && (magnitude >= 1e5 || magnitude < 1e-3)
            ? value.ToString("0.######E+0", CultureInfo.InvariantCulture)
            : value.ToString("0.######", CultureInfo.InvariantCulture);
        return unit.Length == 0 ? number : number + " " + unit;
    }
}

// ============================================================================
// Engine pipeline helpers
// ============================================================================

internal static class Pipeline
{
    public static (ClassificationResult Classification, PedagogicalSolution Solution) Solve(ElectricalNetlist netlist)
    {
        ClassificationResult classification = TopologyClassifier.Classify(netlist);
        PedagogicalSolution solution = classification.Strategy
            .SolveAsync(netlist, new AnalysisParameters())
            .GetAwaiter()
            .GetResult();
        return (classification, solution);
    }

    public static (ClassificationResult Classification, PedagogicalSolution Solution) SolveSucceeded(ElectricalNetlist netlist)
    {
        (ClassificationResult classification, PedagogicalSolution solution) = Solve(netlist);
        if (!solution.IsSuccess)
        {
            throw new InvalidOperationException($"引擎求解失败（{classification.TopologyType}/{classification.SubtypeName}）：{solution.SummaryText}");
        }
        return (classification, solution);
    }

    public static LinearNetworkAnalyzer CreateModel(ElectricalNetlist netlist)
    {
        if (!LinearNetworkAnalyzer.TryCreate(netlist, out LinearNetworkAnalyzer? model, out string error) || model == null)
        {
            throw new InvalidOperationException("线性网络 MNA 建模失败：" + error);
        }
        return model;
    }

    public static LinearSolution SolveDc(LinearNetworkAnalyzer model)
    {
        if (!model.TrySolveDc(out LinearSolution? solution, out string error) || solution == null)
        {
            throw new InvalidOperationException("MNA 直流求解失败：" + error);
        }
        return solution;
    }

    public static LinearSolution SolveAc(LinearNetworkAnalyzer model, double frequencyHz)
    {
        if (!model.TrySolveAc(frequencyHz, out LinearSolution? solution, out string error) || solution == null)
        {
            throw new InvalidOperationException($"MNA 交流相量求解失败（f = {frequencyHz:G6} Hz）：" + error);
        }
        return solution;
    }

    public static CheckOutcome Topology(ClassificationResult classification, CircuitTopologyType expectedType, string expectedSubtype)
    {
        return Checks.TextContains("拓扑", $"{expectedType}/{expectedSubtype}", $"{classification.TopologyType}/{classification.SubtypeName}");
    }

    public static NodePotentialInfo Node(LinearNetworkInfo? network, string netName)
    {
        NodePotentialInfo? node = network?.NodePotentials.FirstOrDefault(n => n.NetName == netName);
        if (node == null)
        {
            throw new InvalidOperationException($"结构化结果中没有节点 '{netName}' 的电位。");
        }
        return node;
    }

    public static PortEquivalentInfo Port(LinearNetworkInfo? network, string positiveNet, string negativeNet)
    {
        PortEquivalentInfo? port = network?.PortEquivalents
            .FirstOrDefault(p => p.PositiveNet == positiveNet && p.NegativeNet == negativeNet);
        if (port == null)
        {
            throw new InvalidOperationException($"结构化结果中没有端口 {positiveNet}–{negativeNet} 的戴维南等效。");
        }
        return port;
    }
}

// ============================================================================
// Display-string parsing (engine emits educational display strings such as
// "τ = 2 ms", "2 × 10³ Ω", "28.25 μA", "1.117 kΩ", "10^{4}" LaTeX).
// These parsers only read engine output; they never re-implement engine maths.
// ============================================================================

internal static class Parsing
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Anchored at the start of the text: a display quantity always begins with its
    // number ("2 × 10³ Ω", "10³ Ω", "0.1 ms", "28.25 μA"). Anchoring prevents a later
    // scientific-notation value in the same string (e.g. "Q = 2.00, α = 2.5 × 10³ s⁻¹")
    // from being parsed instead of the leading number.
    private static readonly Regex UnicodeScientific = new(
        @"^\s*(?<mant>[-+]?\d+(?:\.\d+)?)?\s*×\s*10\s*(?<sup>[⁻⁰¹²³⁴⁵⁶⁷⁸⁹⁺]+)",
        RegexOptions.Compiled);

    private static readonly Regex UnicodeBareTenPower = new(
        @"^\s*10(?<sup>[⁻⁰¹²³⁴⁵⁶⁷⁸⁹⁺]+)",
        RegexOptions.Compiled);

    private static readonly Regex PlainNumber = new(
        @"^\s*(?<num>[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)",
        RegexOptions.Compiled);

    private static readonly Regex UnitLetters = new(
        @"^\s*(?<unit>[A-Za-zμΩ]+)",
        RegexOptions.Compiled);

    private static readonly Regex LatexTimes = new(
        @"(?<mant>[-+]?\d+(?:\.\d+)?)?\s*\\times\s*10\^\{?(?<exp>[-+]?\d+)\}?",
        RegexOptions.Compiled);

    private static readonly Regex LatexBareTen = new(
        @"(?<![\d.])10\^\{?(?<exp>[-+]?\d+)\}?",
        RegexOptions.Compiled);

    /// <summary>Parses the first quantity (number + optional SI-prefixed unit) in an engine display string.</summary>
    public static double ParseQuantity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("数量文本为空，无法解析。");
        }

        double value;
        string rest;
        Match scientific = UnicodeScientific.Match(text);
        if (scientific.Success)
        {
            double mantissa = scientific.Groups["mant"].Success
                ? double.Parse(scientific.Groups["mant"].Value, Inv)
                : 1.0;
            value = mantissa * Math.Pow(10.0, SuperscriptToInt(scientific.Groups["sup"].Value));
            rest = text[scientific.Length..];
        }
        else
        {
            Match barePower = UnicodeBareTenPower.Match(text);
            if (barePower.Success)
            {
                value = Math.Pow(10.0, SuperscriptToInt(barePower.Groups["sup"].Value));
                rest = text[barePower.Length..];
            }
            else
            {
                Match plain = PlainNumber.Match(text);
                if (!plain.Success)
                {
                    throw new FormatException($"文本中没有可解析的数值：'{text}'");
                }
                value = double.Parse(plain.Groups["num"].Value, Inv);
                rest = text[plain.Length..];
            }
        }

        return value * UnitMultiplier(rest);
    }

    /// <summary>Finds a marker (e.g. "τ = ") and parses the quantity that follows it.</summary>
    public static double ExtractQuantity(string text, string marker)
    {
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new FormatException($"文本中找不到标记 '{marker}'：'{text}'");
        }
        return ParseQuantity(text[(index + marker.Length)..]);
    }

    /// <summary>Extracts the segment between two markers and parses its LaTeX/plain scientific value (e.g. "10^{4}").</summary>
    public static double ExtractLatexBetween(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new FormatException($"文本中找不到起始标记 '{startMarker}'：'{text}'");
        }
        start += startMarker.Length;
        int end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new FormatException($"文本中找不到结束标记 '{endMarker}'：'{text}'");
        }
        return ParseLatexScientific(text[start..end]);
    }

    public static double ParseLatexScientific(string text)
    {
        Match times = LatexTimes.Match(text);
        if (times.Success)
        {
            double mantissa = times.Groups["mant"].Success
                ? double.Parse(times.Groups["mant"].Value, Inv)
                : 1.0;
            return mantissa * Math.Pow(10.0, int.Parse(times.Groups["exp"].Value, Inv));
        }
        Match bare = LatexBareTen.Match(text);
        if (bare.Success)
        {
            return Math.Pow(10.0, int.Parse(bare.Groups["exp"].Value, Inv));
        }
        return double.Parse(text.Trim(), Inv);
    }

    private static double UnitMultiplier(string rest)
    {
        Match match = UnitLetters.Match(rest);
        if (!match.Success) return 1.0;
        string unit = match.Groups["unit"].Value;
        if (unit.StartsWith("μ", StringComparison.Ordinal) || unit.StartsWith("u", StringComparison.Ordinal)) return 1e-6;
        if (unit.StartsWith("m", StringComparison.Ordinal)) return 1e-3;
        if (unit.StartsWith("k", StringComparison.Ordinal)) return 1e3;
        if (unit.StartsWith("M", StringComparison.Ordinal)) return 1e6;
        if (unit.StartsWith("n", StringComparison.Ordinal)) return 1e-9;
        if (unit.StartsWith("p", StringComparison.Ordinal)) return 1e-12;
        if (unit.StartsWith("G", StringComparison.Ordinal)) return 1e9;
        return 1.0;
    }

    private static int SuperscriptToInt(string superscript)
    {
        var builder = new System.Text.StringBuilder(superscript.Length);
        foreach (char c in superscript)
        {
            builder.Append(c switch
            {
                '⁻' => '-',
                '⁺' => '+',
                '⁰' => '0',
                '¹' => '1',
                '²' => '2',
                '³' => '3',
                '⁴' => '4',
                '⁵' => '5',
                '⁶' => '6',
                '⁷' => '7',
                '⁸' => '8',
                '⁹' => '9',
                _ => throw new FormatException($"无法识别的上标字符 '{c}'（来自 '{superscript}'）。")
            });
        }
        return int.Parse(builder.ToString(), Inv);
    }
}
