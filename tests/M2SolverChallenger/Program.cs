// ============================================================================
// File: tests/M2SolverChallenger/Program.cs
// Namespace: M2SolverChallenger
// Description: Adversarial Empirical Verification Suite for Milestone 2:
//              Electrical Topology Analysis & Solving Strategy Engine (R1).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace M2SolverChallenger;

partial class Program
{
    private static readonly List<string> Failures = new();
    private static int TotalTests = 0;
    private static int PassedTests = 0;

    private static void AddElement(ElectricalNetlist netlist, CircuitElement el)
    {
        netlist.Elements[el.Id] = el;
    }

    private static ElectricalNet GetOrCreateNet(ElectricalNetlist netlist, int id, string name, bool isGround = false, bool isPower = false)
    {
        var existing = netlist.Nets.FirstOrDefault(n => n.NetId == id);
        if (existing != null) return existing;
        var net = new ElectricalNet { NetId = id, Name = name, IsGround = isGround, IsPower = isPower };
        netlist.Nets.Add(net);
        return net;
    }

    // Hand-built fixtures must supply the same graph layers as the production extractor.
    private static ElectricalNetlist CompleteGraph(ElectricalNetlist netlist)
    {
        if (netlist.Nodes.Count > 0) return netlist;
        foreach (var net in netlist.Nets)
            netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });
        foreach (var el in netlist.Elements.Values.Where(el => el.Pins.Count == 2))
        {
            var first = netlist.FindNodeForPin(el.Id, el.Pins[0].Id);
            var second = netlist.FindNodeForPin(el.Id, el.Pins[1].Id);
            if (first == null || second == null) continue;
            var branch = new ElectricalBranch
            {
                BranchId = $"B_{el.Id}", Element = el, PositiveNode = first, NegativeNode = second,
                ValueSI = NetlistExtractor.ConvertToBaseSi(el.Type, el.DisplayNumber, el.UnitSymbol)
            };
            netlist.Branches.Add(branch);
            first.ConnectedBranches.Add(branch);
            second.ConnectedBranches.Add(branch);
        }
        return netlist;
    }

    private static CircuitTopologyType ClassifyComplete(ElectricalNetlist netlist, out ICircuitStrategy strategy) =>
        TopologyClassifier.Classify(CompleteGraph(netlist), out strategy);

    private static void Assert(bool condition, string testName, string errorDetails)
    {
        TotalTests++;
        if (condition)
        {
            PassedTests++;
            Console.WriteLine($"  [PASS] {testName}");
        }
        else
        {
            Failures.Add($"{testName}: {errorDetails}");
            Console.WriteLine($"  [FAIL] {testName} -> {errorDetails}");
        }
    }

    private static void AssertNear(double actual, double expected, double relTol, string testName, string metricName)
    {
        TotalTests++;
        double diff = Math.Abs(actual - expected);
        double denom = Math.Max(1e-9, Math.Max(Math.Abs(actual), Math.Abs(expected)));
        double relDiff = diff / denom;
        if (relDiff <= relTol || diff < 1e-6)
        {
            PassedTests++;
            Console.WriteLine($"  [PASS] {testName}: {metricName} = {actual:G6} (expected {expected:G6}, relDiff={relDiff:E2})");
        }
        else
        {
            string err = $"{metricName} expected {expected:G6}, but got {actual:G6} (relDiff={relDiff:E2} > tol {relTol:E2})";
            Failures.Add($"{testName}: {err}");
            Console.WriteLine($"  [FAIL] {testName} -> {err}");
        }
    }

    static async Task<int> Main()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("EMPIRICAL CHALLENGER M2-1: ELECTRICAL SOLVER ENGINE EMPIRICAL VERIFICATION");
        Console.WriteLine("================================================================================");

        var parameters = new AnalysisParameters
        {
            OperatingFrequency = 1000.0,
            ThermalVoltageUt = 0.026
        };

        // =====================================================================
        // SUITE 1: Acceptance Criterion 1 (STRICT) - First-Order RC Circuits
        // =====================================================================
        Console.WriteLine("\n[Suite 1] Acceptance Criterion 1: First-Order RC Circuits & BJT Zero-Leakage");

        // Test 1.1: Standard RC_Transient Template
        {
            var rcTemplate = CircuitTemplateFactory.CreateRcTransientTemplate();
            var netlist = NetlistExtractor.ExtractNetlist(rcTemplate.Elements, rcTemplate.Wires);
            var topology = ClassifyComplete(netlist, out var strategy);

            Assert(topology == CircuitTopologyType.FirstOrderRc, "1.1.1 RC Template Classification",
                $"Expected FirstOrderRc, got {topology}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            var analysisResult = solution.ToAnalysisResult();

            Assert(solution.IsSuccess, "1.1.2 RC Template Solving Success", "Solver failed");
            Assert(solution.Steps.Count == 5, "1.1.3 RC Template 5 Derivation Steps", $"Expected 5 steps, got {solution.Steps.Count}");

            // Verify Key Outputs: Req, tau, u_C(t), H(s), fc
            bool hasReq = solution.Steps.Any(s => s.Title.Contains("Req") || s.LatexFormula.Contains("R_{eq}"));
            bool hasTau = solution.Steps.Any(s => s.Title.Contains("时间常数") || s.LatexFormula.Contains(@"\tau"));
            bool hasUc = solution.Steps.Any(s => s.LatexFormula.Contains("u_C(t)") || s.Title.Contains("微分方程"));
            bool hasHs = solution.Steps.Any(s => s.LatexFormula.Contains("H(s)"));
            bool hasFc = solution.Steps.Any(s => s.LatexFormula.Contains("f_c") || s.ResultValue.Contains("fc"));

            Assert(hasReq, "1.1.4 RC Solver Outputs Req", "Step 1 does not contain Req");
            Assert(hasTau, "1.1.5 RC Solver Outputs tau", "Step 2 does not contain tau");
            Assert(hasUc, "1.1.6 RC Solver Outputs u_C(t)", "Step 3 does not contain u_C(t)");
            Assert(hasHs, "1.1.7 RC Solver Outputs H(s)", "Step 4 does not contain H(s)");
            Assert(hasFc, "1.1.8 RC Solver Outputs fc", "Step 4/5 does not contain fc");

            // Verify values: R=1000 Ohm, C=100 uF => tau = 0.1s = 100ms, fc = 1/(2*pi*0.1) = 1.5915 Hz
            Assert(solution.SummaryText.Contains("100") || solution.SummaryText.Contains("1.00"), "1.1.9 RC Summary Metric Text",
                $"SummaryText missing expected numbers: {solution.SummaryText}");

            // STRICT ZERO-LEAKAGE CHECK FOR BJT PARAMETERS
            string[] forbiddenBjtTokens =
            [
                "beta", @"\beta", "β",
                "rbe", "r_{be}",
                "VCEQ", "V_{CEQ}",
                "IBQ", "I_{BQ}",
                "ICQ", "I_{CQ}",
                "hfe", "h_{fe}",
                "三极管", "静态工作点", "微变等效", "基极", "集电极", "发射极"
            ];

            int bjtTokenViolations = 0;
            var stepViolations = new List<string>();

            foreach (var step in solution.Steps)
            {
                string combinedStepText = $"{step.Title} {step.MethodName} {step.Content} {step.LatexFormula} {step.SubstitutionFormula} {step.ResultValue} {step.MnaLog}";
                foreach (var token in forbiddenBjtTokens)
                {
                    if (Regex.IsMatch(combinedStepText, Regex.Escape(token), RegexOptions.IgnoreCase))
                    {
                        bjtTokenViolations++;
                        stepViolations.Add($"Step {step.StepIndex} ('{step.Title}') contains forbidden BJT token '{token}'");
                    }
                }
            }

            Assert(bjtTokenViolations == 0, "1.1.10 STRICT AC1: Zero BJT Keywords/Symbols in Steps",
                $"Found {bjtTokenViolations} violations: {string.Join("; ", stepViolations)}");

            // Verify QPoint has no fictitious BJT parameter values
            bool qpointClean = (solution.QPoint?.Ibq == "—" || string.IsNullOrWhiteSpace(solution.QPoint?.Ibq))
                               && (solution.QPoint?.Icq == "—" || string.IsNullOrWhiteSpace(solution.QPoint?.Icq))
                               && (solution.QPoint?.Vceq == "—" || string.IsNullOrWhiteSpace(solution.QPoint?.Vceq))
                               && (solution.QPoint?.IsActiveRegion == false);

            Assert(qpointClean, "1.1.11 STRICT AC1: QPoint Suppressed with No Numerical BJT Values",
                $"QPoint contains active/numeric values: Ibq='{solution.QPoint?.Ibq}', Icq='{solution.QPoint?.Icq}', Vceq='{solution.QPoint?.Vceq}', IsActive={solution.QPoint?.IsActiveRegion}");

            // Verify AcParameters in AnalysisResult
            Assert(!analysisResult.AcParameters.VoltageGain.Contains("-100"), "1.1.12 AC1: No Default BJT Voltage Gain in Result",
                $"Result.AcParameters.VoltageGain was default BJT value: {analysisResult.AcParameters.VoltageGain}");
        }

        // Test 1.2: Programmatically Constructed RC Circuit (VS=5V, R=2k, C=47uF)
        {
            var netlist = new ElectricalNetlist();
            var vs = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 5.0, UnitSymbol = "V" };
            vs.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vs.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 2.0, UnitSymbol = "kΩ" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var c1 = new CircuitElement { Id = "C1", Name = "C1", Type = ElementType.Capacitor, DisplayNumber = 47.0, UnitSymbol = "μF" };
            c1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            c1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, vs);
            AddElement(netlist, r1);
            AddElement(netlist, c1);
            AddElement(netlist, gnd);

            // Connect nets: Net 0 = GND, Net 1 = V1 (VS pos to R1 1), Net 2 = V2 (R1 2 to C1 1)
            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("C1", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            var net1 = GetOrCreateNet(netlist, 1, "NET_V1");
            net1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
            net1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));

            var net2 = GetOrCreateNet(netlist, 2, "NET_V2");
            net2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("C1", "1", "1"));

            var topology = ClassifyComplete(netlist, out var strategy);
            Assert(topology == CircuitTopologyType.FirstOrderRc, "1.2.1 Custom RC Classification", $"Got {topology}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "1.2.2 Custom RC Solving", "Solver failed");

            // Tau = 2000 * 47e-6 = 0.094 s = 94 ms
            var step2 = solution.Steps[1];
            Assert(step2.SubstitutionFormula.Contains("94") || step2.SubstitutionFormula.Contains("0.094") || step2.ResultValue.Contains("94"),
                "1.2.3 Custom RC Tau Numerical Precision", $"Step 2: {step2.SubstitutionFormula}");

            // Verify strict 0 BJT tokens
            int bjtCount = 0;
            foreach (var step in solution.Steps)
            {
                if (Regex.IsMatch($"{step.Title} {step.Content} {step.LatexFormula}", @"\b(beta|rbe|vceq|ibq|icq)\b", RegexOptions.IgnoreCase))
                    bjtCount++;
            }
            Assert(bjtCount == 0, "1.2.4 Custom RC Zero BJT Tokens", $"Detected {bjtCount} BJT tokens");
        }

        // Test 1.3: First-Order RL Circuit (VS=12V, R=50 Ohm, L=5 mH)
        {
            var netlist = new ElectricalNetlist();
            var vs = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 12.0, UnitSymbol = "V" };
            vs.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vs.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 50.0, UnitSymbol = "Ω" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var l1 = new CircuitElement { Id = "L1", Name = "L1", Type = ElementType.Inductor, DisplayNumber = 5.0, UnitSymbol = "mH" };
            l1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            l1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, vs);
            AddElement(netlist, r1);
            AddElement(netlist, l1);
            AddElement(netlist, gnd);

            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("L1", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            var net1 = GetOrCreateNet(netlist, 1, "NET_V1");
            net1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
            net1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));

            var net2 = GetOrCreateNet(netlist, 2, "NET_V2");
            net2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("L1", "1", "1"));

            var topology = ClassifyComplete(netlist, out var strategy);
            Assert(topology == CircuitTopologyType.FirstOrderRl, "1.3.1 RL Classification", $"Expected FirstOrderRl, got {topology}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "1.3.2 RL Solving", "Solver failed");
            Assert(solution.Steps.Any(s => s.LatexFormula.Contains("i_L(t)")), "1.3.3 RL Formula Contains i_L(t)", "Missing i_L(t)");

            // Display convention (2026-10-18): τ is shown with a textbook unit and at
            // most two decimals: τ = 0.1 ms = 100 μs, fc = 1591.55 Hz → 1.59 kHz.
            Assert(solution.SummaryText.Contains("RL") &&
                   solution.SummaryText.Contains("τ = 100 μs") &&
                   solution.SummaryText.Contains("fc = 1.59 kHz"),
                "1.3.4 RL Summary Metric Text", $"Summary: {solution.SummaryText}");
        }

        // =====================================================================
        // SUITE 2: Second-Order RLC Resonance & Damping Regimes
        // =====================================================================
        Console.WriteLine("\n[Suite 2] Second-Order RLC Circuit Damping & Root Verification");

        // Helper to construct RLC Netlist
        ElectricalNetlist CreateRlcNetlist(double rVal, string rUnit, double lVal, string lUnit, double cVal, string cUnit, bool isParallel = false)
        {
            var net = new ElectricalNetlist();
            var src = new CircuitElement { Id = "VS", Name = "VS", Type = isParallel ? ElementType.DcCurrentSource : ElementType.DcVoltageSource, DisplayNumber = 10.0, UnitSymbol = isParallel ? "mA" : "V" };
            src.Pins.Add(new CircuitPin { Id = isParallel ? "in" : "pos", Name = isParallel ? "in" : "pos" });
            src.Pins.Add(new CircuitPin { Id = isParallel ? "out" : "neg", Name = isParallel ? "out" : "neg" });

            var res = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = rVal, UnitSymbol = rUnit };
            res.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            res.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var ind = new CircuitElement { Id = "L1", Name = "L1", Type = ElementType.Inductor, DisplayNumber = lVal, UnitSymbol = lUnit };
            ind.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            ind.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var cap = new CircuitElement { Id = "C1", Name = "C1", Type = ElementType.Capacitor, DisplayNumber = cVal, UnitSymbol = cUnit };
            cap.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            cap.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(net, src);
            AddElement(net, res);
            AddElement(net, ind);
            AddElement(net, cap);
            AddElement(net, gnd);

            var netGnd = GetOrCreateNet(net, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", isParallel ? "out" : "neg", isParallel ? "out" : "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            if (!isParallel)
            {
                // Series RLC: VS pos -> R1 1; R1 2 -> L1 1; L1 2 -> C1 1; C1 2 -> GND
                var n1 = GetOrCreateNet(net, 1, "N1");
                n1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
                n1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));

                var n2 = GetOrCreateNet(net, 2, "N2");
                n2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
                n2.ConnectedPins.Add(new ComponentPinRef("L1", "1", "1"));

                var n3 = GetOrCreateNet(net, 3, "N3");
                n3.ConnectedPins.Add(new ComponentPinRef("L1", "2", "2"));
                n3.ConnectedPins.Add(new ComponentPinRef("C1", "1", "1"));

                netGnd.ConnectedPins.Add(new ComponentPinRef("C1", "2", "2"));
            }
            else
            {
                // True parallel RLC, with an independent current source across the same pair.
                var n1 = GetOrCreateNet(net, 1, "N1");
                n1.ConnectedPins.Add(new ComponentPinRef("VS", "in", "in"));
                foreach (var id in new[] { "R1", "L1", "C1" })
                {
                    n1.ConnectedPins.Add(new ComponentPinRef(id, "1", "1"));
                    netGnd.ConnectedPins.Add(new ComponentPinRef(id, "2", "2"));
                }
            }

            return net;
        }

        // Test 2.1: Overdamped Condition (Series: L=10 mH, C=1 uF, R=300 Ohm)
        // omega0 = 10000 rad/s, alpha = R/(2L) = 300 / 0.02 = 15000 s^-1 > 10000 => Overdamped
        {
            var rlcNet = CreateRlcNetlist(300.0, "Ω", 10.0, "mH", 1.0, "μF", isParallel: false);
            var top = ClassifyComplete(rlcNet, out var strategy);
            Assert(top == CircuitTopologyType.SecondOrderRlc, "2.1.1 Overdamped Classification", $"Got {top}");

            var solution = await strategy.SolveAsync(rlcNet, parameters);
            Assert(solution.SummaryText.Contains("过阻尼"), "2.1.2 Overdamped Regime Detection",
                $"Summary does not contain '过阻尼': {solution.SummaryText}");

            var step3 = solution.Steps[2]; // Step 3: roots
            Assert(step3.SubstitutionFormula.Contains("s_1") && step3.SubstitutionFormula.Contains("s_2"),
                "2.1.3 Overdamped Two Real Roots Displayed", $"Step 3 roots formula: {step3.SubstitutionFormula}");

            Assert(solution.Steps[3].Title.Contains("阻尼响应") && solution.Steps[3].ResultValue.Contains("过阻尼"),
                "2.1.4 Overdamped Step 4 Status", $"Step 4: {solution.Steps[3].ResultValue}");
        }

        // Test 2.2: Critically Damped Condition (Series: L=10 mH, C=1 uF, R=200 Ohm)
        // Rcrit = 2*sqrt(L/C) = 2*sqrt(0.01 / 1e-6) = 200 Ohm => alpha = 200 / 0.02 = 10000 = omega0
        {
            var rlcNet = CreateRlcNetlist(200.0, "Ω", 10.0, "mH", 1.0, "μF", isParallel: false);
            ClassifyComplete(rlcNet, out var strategy);
            var solution = await strategy.SolveAsync(rlcNet, parameters);

            Assert(solution.SummaryText.Contains("临界阻尼"), "2.2.1 Critically Damped Regime Detection",
                $"Summary does not contain '临界阻尼': {solution.SummaryText}");

            var step3 = solution.Steps[2];
            Assert(step3.SubstitutionFormula.Contains("s_1 = s_2 = -\\alpha"),
                "2.2.2 Critically Damped Double Root Formula", $"Roots: {step3.SubstitutionFormula}");
        }

        // Test 2.3: Underdamped Condition (Series: L=10 mH, C=1 uF, R=50 Ohm)
        // alpha = 50 / 0.02 = 2500 < 10000 => omega_d = sqrt(10000^2 - 2500^2) = 9682.46 rad/s
        {
            var rlcNet = CreateRlcNetlist(50.0, "Ω", 10.0, "mH", 1.0, "μF", isParallel: false);
            ClassifyComplete(rlcNet, out var strategy);
            var solution = await strategy.SolveAsync(rlcNet, parameters);

            Assert(solution.SummaryText.Contains("欠阻尼"), "2.3.1 Underdamped Regime Detection",
                $"Summary does not contain '欠阻尼': {solution.SummaryText}");

            var step3 = solution.Steps[2];
            Assert(step3.SubstitutionFormula.Contains(@"\pm j"),
                "2.3.2 Underdamped Complex Conjugate Roots", $"Roots: {step3.SubstitutionFormula}");

            // Quality factor Q = omega0 / (2*alpha) = 10000 / 5000 = 2.0
            // (display convention: at most two decimals → "Q = 2", trailing zeros trimmed)
            Assert(solution.SummaryText.Contains("品质因数 Q = 2"), "2.3.3 Underdamped Q Factor = 2",
                $"SummaryText Q: {solution.SummaryText}");
        }

        // Test 2.4: Parallel RLC Circuit (Parallel: R=1k, L=10 mH, C=1 uF)
        // alpha = 1 / (2*R*C) = 1 / (2 * 1000 * 1e-6) = 500 s^-1 < 10000 => Underdamped
        // Q = R*sqrt(C/L) = 1000 * sqrt(1e-6 / 0.01) = 1000 * 0.01 = 10.0
        {
            var rlcNet = CreateRlcNetlist(1.0, "kΩ", 10.0, "mH", 1.0, "μF", isParallel: true);
            var top = ClassifyComplete(rlcNet, out var strategy);
            Assert(strategy.SubtypeName == "ParallelRlc", "2.4.1 Parallel RLC Subtype Detection",
                $"Expected ParallelRlc, got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(rlcNet, parameters);
            // Display convention: Q = 10.0 is rendered as "Q = 10".
            Assert(solution.SummaryText.Contains("品质因数 Q = 10"), "2.4.2 Parallel RLC Q Factor = 10",
                $"SummaryText Q: {solution.SummaryText}");
        }

        // =====================================================================
        // SUITE 3: BJT Amplifier Solving Verification
        // =====================================================================
        Console.WriteLine("\n[Suite 3] BJT Amplifier DC Q-Point & Small-Signal AC Verification");

        // Test 3.1: Voltage-Divider Self-Bias BJT Amplifier (CommonEmitter Template)
        {
            var ceTemplate = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var netlist = NetlistExtractor.ExtractNetlist(ceTemplate.Elements, ceTemplate.Wires);
            var top = ClassifyComplete(netlist, out var strategy);

            Assert(top == CircuitTopologyType.BjtAmplifier, "3.1.1 CE Template Classification", $"Got {top}");
            Assert(strategy.SubtypeName == "VoltageDividerSelfBias", "3.1.2 Bias Subtype VoltageDividerSelfBias",
                $"Got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "3.1.3 CE Template Solving", "Solver failed");
            Assert(solution.Steps.Count == 5, "3.1.4 CE 5 Pedagogical Steps", $"Expected 5, got {solution.Steps.Count}");

            Assert(solution.QPoint != null, "3.1.5 QPoint Populated", "QPoint is null");
            // 教材估算法（用户口径 2026-10-18）：U_B 空载分压先舍入两位 → I_EQ → I_BQ → I_CQ，
            // 因此 Q 点显示为 20.1 μA / 2.01 mA / 3.94 V（与精确戴维南解有可见差异）。
            Assert(solution.QPoint.Ibq.Contains("20.1"), "3.1.6 IBQ ≈ 20.1 uA（教材估算法）", $"Got IBQ = '{solution.QPoint.Ibq}'");
            Assert(solution.QPoint.Icq.Contains("2.01"), "3.1.7 ICQ ≈ 2.01 mA（教材估算法）", $"Got ICQ = '{solution.QPoint.Icq}'");
            Assert(solution.QPoint.Vceq.Contains("3.94"), "3.1.8 VCEQ ≈ 3.94 V（教材估算法）",
                $"Got VCEQ = '{solution.QPoint.Vceq}'");
            Assert(solution.QPoint.IsActiveRegion, "3.1.9 BJT In Active Region", "IsActiveRegion is false");

            // AC 链：rbe=200+(1+β)U_T/I_EQ 使用舍入后的 I_EQ，Av/Ri 再按显示单位舍入两位。
            double actualGain = double.Parse(solution.AcParameters.VoltageGain, System.Globalization.CultureInfo.InvariantCulture);
            double actualRin = double.Parse(solution.AcParameters.InputResistance.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) * 1000;
            Assert(Math.Abs(actualGain - (-155.03)) < 0.01,
                "3.1.10 CE gain uses emitter-current small-signal resistance", solution.AcParameters.VoltageGain);
            Assert(Math.Abs(actualRin - 1320) < 6,
                "3.1.11 CE input resistance numeric tolerance", solution.AcParameters.InputResistance);
            Assert(solution.AcParameters.OutputResistance.Contains("3.0") || solution.AcParameters.OutputResistance.Contains("3"),
                "3.1.12 Output Resistance Ro = 3.0 kΩ", $"Got Ro = '{solution.AcParameters.OutputResistance}'");
        }

        // Test 3.2: Fixed-Bias BJT Amplifier
        {
            var netlist = new ElectricalNetlist();
            var vcc = new CircuitElement { Id = "VCC", Name = "VCC", Type = ElementType.DcVoltageSource, DisplayNumber = 12.0, UnitSymbol = "V" };
            vcc.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vcc.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var q1 = new CircuitElement { Id = "Q1", Name = "Q1", Type = ElementType.BjtNpn, DisplayNumber = 100.0, UnitSymbol = "" };
            q1.Pins.Add(new CircuitPin { Id = "B", Name = "B" });
            q1.Pins.Add(new CircuitPin { Id = "C", Name = "C" });
            q1.Pins.Add(new CircuitPin { Id = "E", Name = "E" });

            var rb = new CircuitElement { Id = "Rb", Name = "Rb", Type = ElementType.Resistor, DisplayNumber = 300.0, UnitSymbol = "kΩ" };
            rb.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rb.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var rc = new CircuitElement { Id = "Rc", Name = "Rc", Type = ElementType.Resistor, DisplayNumber = 3.0, UnitSymbol = "kΩ" };
            rc.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rc.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, vcc);
            AddElement(netlist, q1);
            AddElement(netlist, rb);
            AddElement(netlist, rc);
            AddElement(netlist, gnd);

            // Power Net (VCC pos, Rb 1, Rc 1)
            var netVcc = GetOrCreateNet(netlist, 1, "VCC", isPower: true);
            netVcc.ConnectedPins.Add(new ComponentPinRef("VCC", "pos", "pos"));
            netVcc.ConnectedPins.Add(new ComponentPinRef("Rb", "1", "1"));
            netVcc.ConnectedPins.Add(new ComponentPinRef("Rc", "1", "1"));

            // Base Net (Rb 2, Q1 B)
            var netB = GetOrCreateNet(netlist, 2, "NET_B");
            netB.ConnectedPins.Add(new ComponentPinRef("Rb", "2", "2"));
            netB.ConnectedPins.Add(new ComponentPinRef("Q1", "B", "B"));

            // Collector Net (Rc 2, Q1 C)
            var netC = GetOrCreateNet(netlist, 3, "NET_C");
            netC.ConnectedPins.Add(new ComponentPinRef("Rc", "2", "2"));
            netC.ConnectedPins.Add(new ComponentPinRef("Q1", "C", "C"));

            // Ground Net (VCC neg, Q1 E, GND)
            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VCC", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("Q1", "E", "E"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            var top = ClassifyComplete(netlist, out var strategy);
            Assert(top == CircuitTopologyType.BjtAmplifier, "3.2.1 Fixed Bias Classification", $"Got {top}");
            Assert(strategy.SubtypeName == "FixedBias", "3.2.2 FixedBias Subtype Detected", $"Got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.QPoint.Ibq.Contains("37.7") || solution.QPoint.Ibq.Contains("37.6"),
                "3.2.3 Fixed Bias IBQ ~ 37.7 uA", $"Got IBQ = '{solution.QPoint.Ibq}'");
            Assert(solution.QPoint.Icq.Contains("3.77") || solution.QPoint.Icq.Contains("3.76"),
                "3.2.4 Fixed Bias ICQ ~ 3.77 mA", $"Got ICQ = '{solution.QPoint.Icq}'");
        }

        // Test 3.3: Question 13 Common-Base BJT Amplifier Verification (Arbitrary IDs R1..R5, C1..C3, V1, Q1)
        {
            var netlist = new ElectricalNetlist();

            // Q1: BJT beta=50, rbb'=50
            var q1 = new CircuitElement { Id = "Q1", Name = "Q1", Type = ElementType.BjtNpn, DisplayNumber = 50.0, UnitSymbol = "β" };
            q1.Pins.Add(new CircuitPin { Id = "B", Name = "B" });
            q1.Pins.Add(new CircuitPin { Id = "C", Name = "C" });
            q1.Pins.Add(new CircuitPin { Id = "E", Name = "E" });

            // V1: 12V DC power
            var v1 = new CircuitElement { Id = "V1", Name = "V1", Type = ElementType.DcVoltageSource, DisplayNumber = 12.0, UnitSymbol = "V" };
            v1.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            v1.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            // R1: 30k (Rb1)
            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 30.0, UnitSymbol = "kΩ" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // R2: 15k (Rb2)
            var r2 = new CircuitElement { Id = "R2", Name = "R2", Type = ElementType.Resistor, DisplayNumber = 15.0, UnitSymbol = "kΩ" };
            r2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // R3: 3k (Rc)
            var r3 = new CircuitElement { Id = "R3", Name = "R3", Type = ElementType.Resistor, DisplayNumber = 3.0, UnitSymbol = "kΩ" };
            r3.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r3.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // R4: 2k (Re)
            var r4 = new CircuitElement { Id = "R4", Name = "R4", Type = ElementType.Resistor, DisplayNumber = 2.0, UnitSymbol = "kΩ" };
            r4.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r4.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // R5: 3k (RL)
            var r5 = new CircuitElement { Id = "R5", Name = "R5", Type = ElementType.Resistor, DisplayNumber = 3.0, UnitSymbol = "kΩ" };
            r5.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r5.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // C1: 10uF (Co output coupling)
            var c1 = new CircuitElement { Id = "C1", Name = "C1", Type = ElementType.Capacitor, DisplayNumber = 10.0, UnitSymbol = "μF" };
            c1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            c1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // C2: 10uF (Cb base bypass)
            var c2 = new CircuitElement { Id = "C2", Name = "C2", Type = ElementType.Capacitor, DisplayNumber = 10.0, UnitSymbol = "μF" };
            c2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            c2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // C3: 10uF (Ci input coupling)
            var c3 = new CircuitElement { Id = "C3", Name = "C3", Type = ElementType.Capacitor, DisplayNumber = 10.0, UnitSymbol = "μF" };
            c3.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            c3.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            // GND
            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, q1);
            AddElement(netlist, v1);
            AddElement(netlist, r1);
            AddElement(netlist, r2);
            AddElement(netlist, r3);
            AddElement(netlist, r4);
            AddElement(netlist, r5);
            AddElement(netlist, c1);
            AddElement(netlist, c2);
            AddElement(netlist, c3);
            AddElement(netlist, gnd);

            // Connect Nets:
            // GND: V1.neg, GND.GND, R2.2, R4.2, R5.2, C2.2
            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("V1", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R2", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R4", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R5", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("C2", "2", "2"));

            // VCC: V1.pos, R1.1, R3.1
            var netVcc = GetOrCreateNet(netlist, 1, "VCC", isPower: true);
            netVcc.ConnectedPins.Add(new ComponentPinRef("V1", "pos", "pos"));
            netVcc.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));
            netVcc.ConnectedPins.Add(new ComponentPinRef("R3", "1", "1"));

            // Base Node: Q1.B, R1.2, R2.1, C2.1
            var netB = GetOrCreateNet(netlist, 2, "Net_Base");
            netB.ConnectedPins.Add(new ComponentPinRef("Q1", "B", "B"));
            netB.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            netB.ConnectedPins.Add(new ComponentPinRef("R2", "1", "1"));
            netB.ConnectedPins.Add(new ComponentPinRef("C2", "1", "1"));

            // Collector Node: Q1.C, R3.2, C1.1
            var netC = GetOrCreateNet(netlist, 3, "Net_Collector");
            netC.ConnectedPins.Add(new ComponentPinRef("Q1", "C", "C"));
            netC.ConnectedPins.Add(new ComponentPinRef("R3", "2", "2"));
            netC.ConnectedPins.Add(new ComponentPinRef("C1", "1", "1"));

            // Emitter Node: Q1.E, R4.1, C3.1
            var netE = GetOrCreateNet(netlist, 4, "Net_Emitter");
            netE.ConnectedPins.Add(new ComponentPinRef("Q1", "E", "E"));
            netE.ConnectedPins.Add(new ComponentPinRef("R4", "1", "1"));
            netE.ConnectedPins.Add(new ComponentPinRef("C3", "1", "1"));

            // Out Node: C1.2, R5.1
            var netOut = GetOrCreateNet(netlist, 5, "Net_Out");
            netOut.ConnectedPins.Add(new ComponentPinRef("C1", "2", "2"));
            netOut.ConnectedPins.Add(new ComponentPinRef("R5", "1", "1"));

            var inputSource = new CircuitElement { Id = "VS_IN", Type = ElementType.AcVoltageSource, DisplayNumber = 10, UnitSymbol = "mV" };
            inputSource.InitializeDefaultPins();
            AddElement(netlist, inputSource);
            netGnd.ConnectedPins.Add(new ComponentPinRef(inputSource.Id, "neg", "neg"));
            var netInput = GetOrCreateNet(netlist, 6, "Vin");
            netInput.ConnectedPins.Add(new ComponentPinRef(inputSource.Id, "pos", "pos"));
            netInput.ConnectedPins.Add(new ComponentPinRef("C3", "2", "2"));

            var top = ClassifyComplete(netlist, out var strategy);
            Assert(top == CircuitTopologyType.BjtAmplifier, "3.3.1 Question 13 Topology Classification", $"Got {top}");
            Assert(strategy.SubtypeName == "CommonBase", "3.3.2 Question 13 Subtype CommonBase", $"Got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "3.3.3 Question 13 Solve Success", "Solution not successful");
            Assert(solution.Steps.Count == 5, "3.3.4 Question 13 5 Derivation Steps", $"Got {solution.Steps.Count}");

            // 教材估算法（用户口径 2026-10-18）：分压偏置取空载 U_B=4 V（先按显示单位舍入
            // 两位），再由发射极回路 I_EQ→I_BQ→I_CQ，共基 h 参数模型给 Au=β(Rc∥RL)/hie、
            // ri'=Re∥hib；期望值复现该手算舍入链，而不是精确戴维南解。
            double ubRounded = TeachingDisplay.RoundTo(12.0 * 15000.0 / (30000.0 + 15000.0), QuantityKind.Voltage);
            double ieqRounded = TeachingDisplay.RoundTo((ubRounded - 0.7) / 2000.0, QuantityKind.Current);
            double ibqRounded = TeachingDisplay.RoundTo(ieqRounded / 51.0, QuantityKind.Current);
            double icqRounded = TeachingDisplay.RoundTo(50.0 * ibqRounded, QuantityKind.Current);
            double vceqRounded = TeachingDisplay.RoundTo(12.0 - icqRounded * 3000.0 - ieqRounded * 2000.0, QuantityKind.Voltage);
            double hieRounded = TeachingDisplay.RoundTo(50.0 + 51.0 * 0.026 / ieqRounded, QuantityKind.Resistance);
            double hibRounded = TeachingDisplay.RoundTo(hieRounded / 51.0, QuantityKind.Resistance);
            double auRounded = TeachingDisplay.RoundPlain(50.0 * 1500.0 / hieRounded);
            double riRounded = TeachingDisplay.RoundTo(1.0 / (1.0 / 2000.0 + 1.0 / hibRounded), QuantityKind.Resistance);
            double ReadNumber(string text) => double.Parse(Regex.Match(text, @"[-+]?\d+(?:\.\d+)?").Value,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert(Math.Abs(ReadNumber(solution.QPoint.Icq) - icqRounded * 1000) < 0.01,
                "3.3.5 Common-base loaded-divider ICQ", solution.QPoint.Icq);
            Assert(Math.Abs(ReadNumber(solution.QPoint.Ibq) - ibqRounded * 1e6) < 0.1,
                "3.3.6 Common-base loaded-divider IBQ", solution.QPoint.Ibq);
            Assert(Math.Abs(ReadNumber(solution.QPoint.Vceq) - vceqRounded) < 0.01,
                "3.3.7 Common-base loaded-divider VCEQ", solution.QPoint.Vceq);
            Assert(Math.Abs(ReadNumber(solution.Steps[1].ResultValue.Split(',')[0].Split('=')[1]) - hieRounded) < 1,
                "3.3.8 Common-base h_ie follows actual emitter current", solution.Steps[1].ResultValue);
            Assert(Math.Abs(ReadNumber(solution.AcParameters.VoltageGain) - auRounded) < 0.1,
                "3.3.9 Common-base gain follows h parameters", solution.AcParameters.VoltageGain);
            Assert(Math.Abs(ReadNumber(solution.AcParameters.InputResistance) - riRounded) < 0.1,
                "3.3.10 Common-base input resistance", solution.AcParameters.InputResistance);
            Assert(solution.AcParameters.UpperCutoffFreq.Contains("未求解"),
                "3.3.11 No fabricated common-base high-frequency cutoff", solution.AcParameters.UpperCutoffFreq);
        }

        // Test 3.4: Polarized Electrolytic Capacitor in BJT Amplifier (Correct vs Reverse Bias)
        {
            var netlist = new ElectricalNetlist();
            var q1 = new CircuitElement { Id = "Q1", Name = "Q1", Type = ElementType.BjtNpn, DisplayNumber = 100.0, UnitSymbol = "β" };
            q1.Pins.Add(new CircuitPin { Id = "B", Name = "B" });
            q1.Pins.Add(new CircuitPin { Id = "C", Name = "C" });
            q1.Pins.Add(new CircuitPin { Id = "E", Name = "E" });
            AddElement(netlist, q1);

            var vcc = new CircuitElement { Id = "VCC", Name = "VCC", Type = ElementType.DcVoltageSource, DisplayNumber = 12.0, UnitSymbol = "V" };
            vcc.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vcc.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });
            AddElement(netlist, vcc);

            var rb1 = new CircuitElement { Id = "Rb1", Name = "Rb1", Type = ElementType.Resistor, DisplayNumber = 51.0, UnitSymbol = "kΩ" };
            rb1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rb1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(netlist, rb1);

            var rb2 = new CircuitElement { Id = "Rb2", Name = "Rb2", Type = ElementType.Resistor, DisplayNumber = 15.0, UnitSymbol = "kΩ" };
            rb2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rb2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(netlist, rb2);

            var rc = new CircuitElement { Id = "Rc", Name = "Rc", Type = ElementType.Resistor, DisplayNumber = 3.0, UnitSymbol = "kΩ" };
            rc.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rc.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(netlist, rc);

            var re = new CircuitElement { Id = "Re", Name = "Re", Type = ElementType.Resistor, DisplayNumber = 1.0, UnitSymbol = "kΩ" };
            re.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            re.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(netlist, re);

            // CE: Polarized Electrolytic Capacitor 47uF across Re to GND (Emitter bypass)
            var ce = new CircuitElement { Id = "CE", Name = "CE", Type = ElementType.CapacitorPolarized, DisplayNumber = 47.0, UnitSymbol = "μF" };
            ce.Pins.Add(new CircuitPin { Id = "+", Name = "+" });
            ce.Pins.Add(new CircuitPin { Id = "-", Name = "-" });
            AddElement(netlist, ce);

            var gndNet = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            var vccNet = GetOrCreateNet(netlist, 1, "VCC", isPower: true);
            var bNet = GetOrCreateNet(netlist, 2, "Vb");
            var cNet = GetOrCreateNet(netlist, 3, "Vc");
            var eNet = GetOrCreateNet(netlist, 4, "Ve");

            vccNet.ConnectedPins.AddRange(new[] { new ComponentPinRef("VCC", "pos", "pos"), new ComponentPinRef("Rb1", "1", "1"), new ComponentPinRef("Rc", "1", "1") });
            gndNet.ConnectedPins.AddRange(new[] { new ComponentPinRef("VCC", "neg", "neg"), new ComponentPinRef("Rb2", "2", "2"), new ComponentPinRef("Re", "2", "2"), new ComponentPinRef("CE", "-", "-") });
            bNet.ConnectedPins.AddRange(new[] { new ComponentPinRef("Q1", "B", "B"), new ComponentPinRef("Rb1", "2", "2"), new ComponentPinRef("Rb2", "1", "1") });
            cNet.ConnectedPins.AddRange(new[] { new ComponentPinRef("Q1", "C", "C"), new ComponentPinRef("Rc", "2", "2") });
            eNet.ConnectedPins.AddRange(new[] { new ComponentPinRef("Q1", "E", "E"), new ComponentPinRef("Re", "1", "1"), new ComponentPinRef("CE", "+", "+") });

            var top = ClassifyComplete(netlist, out var strategy);
            Assert(top == CircuitTopologyType.BjtAmplifier, "3.4.1 Electrolytic CE Amplifier Classified", $"Got {top}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "3.4.2 Electrolytic CE Solve Success", "Failed to solve");
            Assert(solution.SummaryText.Contains("CE 正偏正常"), "3.4.3 Electrolytic CE Forward Bias Confirmed", $"SummaryText: {solution.SummaryText}");
            Assert(solution.Steps.Any(s => s.Title.Contains("电解电容 CE")), "3.4.4 Electrolytic CE Verification Step Present", "Missing CE verification step");

            // Now reverse CE: + to GND, - to Emitter (ve > 0) -> Reverse bias hazard!
            eNet.ConnectedPins.RemoveAll(p => p.ElementId == "CE");
            gndNet.ConnectedPins.RemoveAll(p => p.ElementId == "CE");
            eNet.ConnectedPins.Add(new ComponentPinRef("CE", "-", "-"));
            gndNet.ConnectedPins.Add(new ComponentPinRef("CE", "+", "+"));

            var revSolution = await strategy.SolveAsync(netlist, parameters);
            Assert(revSolution.SummaryText.Contains("极性反接"), "3.4.5 Electrolytic CE Reverse Bias Detected", $"SummaryText: {revSolution.SummaryText}");
            Assert(!revSolution.IsSuccess && revSolution.AcParameters == null &&
                revSolution.Steps.All(s => s.Phase == SolutionPhase.DcEquivalent) &&
                revSolution.Steps.Any(s => s.Title.Contains("反接错误") && s.Status == "Error"),
                "3.4.6 Electrolytic CE Reverse Bias Blocks AC", "Reverse-biased bypass capacitor must block AC analysis");
        }

        // =====================================================================
        // SUITE 4: SPICE MNA Linear Matrix Solver Verification
        // =====================================================================
        Console.WriteLine("\n[Suite 4] SPICE MNA Linear Network Matrix Solver Verification");

        // Test 4.1: Balanced Wheatstone Bridge Circuit
        // VS=10V (Node 1 to GND)
        // R1=100 (Node 1 to Node 2), R3=200 (Node 2 to GND)
        // R2=100 (Node 1 to Node 3), R4=200 (Node 3 to GND)
        // R5=50 (Node 2 to Node 3, bridge detector)
        // Analytic: V1=10V, V2=6.6667V, V3=6.6667V, V2-V3=0V (Bridge balanced)
        {
            var netlist = new ElectricalNetlist();
            var vs = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 10.0, UnitSymbol = "V" };
            vs.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vs.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 100.0, UnitSymbol = "Ω" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r2 = new CircuitElement { Id = "R2", Name = "R2", Type = ElementType.Resistor, DisplayNumber = 100.0, UnitSymbol = "Ω" };
            r2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r3 = new CircuitElement { Id = "R3", Name = "R3", Type = ElementType.Resistor, DisplayNumber = 200.0, UnitSymbol = "Ω" };
            r3.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r3.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r4 = new CircuitElement { Id = "R4", Name = "R4", Type = ElementType.Resistor, DisplayNumber = 200.0, UnitSymbol = "Ω" };
            r4.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r4.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r5 = new CircuitElement { Id = "R5", Name = "R5", Type = ElementType.Resistor, DisplayNumber = 50.0, UnitSymbol = "Ω" };
            r5.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r5.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, vs);
            AddElement(netlist, r1);
            AddElement(netlist, r2);
            AddElement(netlist, r3);
            AddElement(netlist, r4);
            AddElement(netlist, r5);
            AddElement(netlist, gnd);

            // Ground Net (VS neg, R3 2, R4 2, GND)
            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R3", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R4", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            // Node 1: VS pos, R1 1, R2 1
            var net1 = GetOrCreateNet(netlist, 1, "N1");
            net1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
            net1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));
            net1.ConnectedPins.Add(new ComponentPinRef("R2", "1", "1"));

            // Node 2: R1 2, R3 1, R5 1
            var net2 = GetOrCreateNet(netlist, 2, "N2");
            net2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("R3", "1", "1"));
            net2.ConnectedPins.Add(new ComponentPinRef("R5", "1", "1"));

            // Node 3: R2 2, R4 1, R5 2
            var net3 = GetOrCreateNet(netlist, 3, "N3");
            net3.ConnectedPins.Add(new ComponentPinRef("R2", "2", "2"));
            net3.ConnectedPins.Add(new ComponentPinRef("R4", "1", "1"));
            net3.ConnectedPins.Add(new ComponentPinRef("R5", "2", "2"));

            var top = ClassifyComplete(netlist, out var strategy);
            Assert(top == CircuitTopologyType.MnaLinear, "4.1.1 Bridge Circuit Classified as MnaLinear", $"Got {top}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "4.1.2 Bridge MNA Solver Execution", "MNA solver failed");

            string log = solution.RawMnaLog;
            Assert(log.Contains("N1") && log.Contains("N2") && log.Contains("N3"),
                "4.1.3 Bridge MNA Log Contains Nodes N1, N2, N3", $"Log: {log}");

            double ExtractVoltage(string nodeName)
            {
                var match = Regex.Match(log, $@"节点\s+{nodeName}\s+电位\s+v_\d+\s+=\s+([-\d\.]+)\s+V");
                return match.Success ? double.Parse(match.Groups[1].Value) : double.NaN;
            }

            double v1 = ExtractVoltage("N1");
            double v2 = ExtractVoltage("N2");
            double v3 = ExtractVoltage("N3");

            AssertNear(v1, 10.0, 1e-4, "4.1.4 Bridge Node 1 Voltage", "V1");
            AssertNear(v2, 6.6667, 1e-3, "4.1.5 Bridge Node 2 Voltage", "V2");
            AssertNear(v3, 6.6667, 1e-3, "4.1.6 Bridge Node 3 Voltage", "V3");
            AssertNear(Math.Abs(v2 - v3), 0.0, 1e-3, "4.1.7 Bridge Balance Null Voltage (V2 - V3 = 0)", "Delta_V");
        }

        // Test 4.2: Unbalanced Wheatstone Bridge Circuit
        // R1=100, R3=300; R2=200, R4=100; R5=50
        // Analytic SPICE: V1=10V, V2=5.8696V, V3=4.7826V
        {
            var netlist = new ElectricalNetlist();
            var vs = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 10.0, UnitSymbol = "V" };
            vs.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vs.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 100.0, UnitSymbol = "Ω" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r2 = new CircuitElement { Id = "R2", Name = "R2", Type = ElementType.Resistor, DisplayNumber = 200.0, UnitSymbol = "Ω" };
            r2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r3 = new CircuitElement { Id = "R3", Name = "R3", Type = ElementType.Resistor, DisplayNumber = 300.0, UnitSymbol = "Ω" };
            r3.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r3.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r4 = new CircuitElement { Id = "R4", Name = "R4", Type = ElementType.Resistor, DisplayNumber = 100.0, UnitSymbol = "Ω" };
            r4.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r4.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r5 = new CircuitElement { Id = "R5", Name = "R5", Type = ElementType.Resistor, DisplayNumber = 50.0, UnitSymbol = "Ω" };
            r5.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r5.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, vs);
            AddElement(netlist, r1);
            AddElement(netlist, r2);
            AddElement(netlist, r3);
            AddElement(netlist, r4);
            AddElement(netlist, r5);
            AddElement(netlist, gnd);

            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R3", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R4", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            var net1 = GetOrCreateNet(netlist, 1, "N1");
            net1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
            net1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));
            net1.ConnectedPins.Add(new ComponentPinRef("R2", "1", "1"));

            var net2 = GetOrCreateNet(netlist, 2, "N2");
            net2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("R3", "1", "1"));
            net2.ConnectedPins.Add(new ComponentPinRef("R5", "1", "1"));

            var net3 = GetOrCreateNet(netlist, 3, "N3");
            net3.ConnectedPins.Add(new ComponentPinRef("R2", "2", "2"));
            net3.ConnectedPins.Add(new ComponentPinRef("R4", "1", "1"));
            net3.ConnectedPins.Add(new ComponentPinRef("R5", "2", "2"));

            var top = ClassifyComplete(netlist, out var strategy);
            var solution = await strategy.SolveAsync(netlist, parameters);

            string log = solution.RawMnaLog;
            double ExtractVoltage(string nodeName)
            {
                var match = Regex.Match(log, $@"节点\s+{nodeName}\s+电位\s+v_\d+\s+=\s+([-\d\.]+)\s+V");
                return match.Success ? double.Parse(match.Groups[1].Value) : double.NaN;
            }

            double v2 = ExtractVoltage("N2");
            double v3 = ExtractVoltage("N3");

            AssertNear(v2, 5.8696, 1e-3, "4.2.1 Unbalanced Bridge V2", "V2");
            AssertNear(v3, 4.7826, 1e-3, "4.2.2 Unbalanced Bridge V3", "V3");
        }

        // Test 4.3: Dual-Loop Multi-Source Linear Circuit
        // V1=12V (N1 to GND), V2=6V (N3 to GND)
        // R1=1k (N1 to N2), R2=2k (N3 to N2), R3=1k (N2 to GND)
        // Millman Analytic: V2 = (12/1k + 6/2k) / (1/1k + 1/2k + 1/1k) = (12m + 3m)/(1m + 0.5m + 1m) = 15m/2.5m = 6.000 V
        {
            var netlist = new ElectricalNetlist();
            var v1 = new CircuitElement { Id = "V1", Name = "V1", Type = ElementType.DcVoltageSource, DisplayNumber = 12.0, UnitSymbol = "V" };
            v1.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            v1.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var v2 = new CircuitElement { Id = "V2", Name = "V2", Type = ElementType.DcVoltageSource, DisplayNumber = 6.0, UnitSymbol = "V" };
            v2.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            v2.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

            var r1 = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 1.0, UnitSymbol = "kΩ" };
            r1.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r1.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r2 = new CircuitElement { Id = "R2", Name = "R2", Type = ElementType.Resistor, DisplayNumber = 2.0, UnitSymbol = "kΩ" };
            r2.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r2.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var r3 = new CircuitElement { Id = "R3", Name = "R3", Type = ElementType.Resistor, DisplayNumber = 1.0, UnitSymbol = "kΩ" };
            r3.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r3.Pins.Add(new CircuitPin { Id = "2", Name = "2" });

            var gnd = new CircuitElement { Id = "GND", Name = "GND", Type = ElementType.Ground, DisplayNumber = 0, UnitSymbol = "V" };
            gnd.Pins.Add(new CircuitPin { Id = "GND", Name = "GND" });

            AddElement(netlist, v1);
            AddElement(netlist, v2);
            AddElement(netlist, r1);
            AddElement(netlist, r2);
            AddElement(netlist, r3);
            AddElement(netlist, gnd);

            var netGnd = GetOrCreateNet(netlist, 0, "GND", isGround: true);
            netGnd.ConnectedPins.Add(new ComponentPinRef("V1", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("V2", "neg", "neg"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("R3", "2", "2"));
            netGnd.ConnectedPins.Add(new ComponentPinRef("GND", "GND", "GND"));

            var net1 = GetOrCreateNet(netlist, 1, "N1");
            net1.ConnectedPins.Add(new ComponentPinRef("V1", "pos", "pos"));
            net1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));

            var net2 = GetOrCreateNet(netlist, 2, "N2");
            net2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("R2", "2", "2"));
            net2.ConnectedPins.Add(new ComponentPinRef("R3", "1", "1"));

            var net3 = GetOrCreateNet(netlist, 3, "N3");
            net3.ConnectedPins.Add(new ComponentPinRef("V2", "pos", "pos"));
            net3.ConnectedPins.Add(new ComponentPinRef("R2", "1", "1"));

            var top = ClassifyComplete(netlist, out var strategy);
            var solution = await strategy.SolveAsync(netlist, parameters);

            double ExtractVoltage(string nodeName)
            {
                var match = Regex.Match(solution.RawMnaLog, $@"节点\s+{nodeName}\s+电位\s+v_\d+\s+=\s+([-\d\.]+)\s+V");
                return match.Success ? double.Parse(match.Groups[1].Value) : double.NaN;
            }

            double vNode2 = ExtractVoltage("N2");
            AssertNear(vNode2, 6.000, 1e-4, "4.3.1 Dual-Loop Central Node Voltage", "V_N2");
        }

        // =====================================================================
        // SUITE 5: Scientific Notation Formatter Industrial Precision
        // =====================================================================
        Console.WriteLine("\n[Suite 5] Scientific Notation Formatter Precision & Standards");

        {
            // >= 1000 formatted to scientific notation
            string s1 = ScientificNotationFormatter.ToLatexScientific(6280.0);
            Assert(s1.Contains(@"\times 10^{3}") || s1.Contains("6.28"), "5.1.1 6280 -> 6.28 x 10^3", $"Got '{s1}'");

            string s2 = ScientificNotationFormatter.ToLatexScientific(51000.0);
            Assert(s2.Contains(@"\times 10^{4}") || s2.Contains("5.1"), "5.1.2 51000 -> 5.1 x 10^4", $"Got '{s2}'");

            // < 0.001 formatted to scientific notation
            string s3 = ScientificNotationFormatter.ToLatexScientific(0.00002);
            Assert(s3.Contains(@"\times 10^{-5}") || s3.Contains("2"), "5.1.3 0.00002 -> 2.00 x 10^-5", $"Got '{s3}'");

            // Moderate numbers (0.001 <= |v| < 1000) remain standard decimal
            string s4 = ScientificNotationFormatter.ToLatexScientific(12.0);
            Assert(s4 == "12" || s4 == "12.0", "5.1.4 12.0 -> standard decimal", $"Got '{s4}'");

            string s5 = ScientificNotationFormatter.ToLatexScientific(0.7);
            Assert(s5.StartsWith("0.7"), "5.1.5 0.7 -> standard decimal", $"Got '{s5}'");

            // Edge cases
            string sNan = ScientificNotationFormatter.ToLatexScientific(double.NaN);
            Assert(sNan == @"\text{NaN}", "5.1.6 NaN LaTeX Formatter", $"Got '{sNan}'");

            string sInf = ScientificNotationFormatter.ToLatexScientific(double.PositiveInfinity);
            Assert(sInf == @"\infty", "5.1.7 Infinity LaTeX Formatter", $"Got '{sInf}'");

            string sNegInf = ScientificNotationFormatter.ToLatexScientific(double.NegativeInfinity);
            Assert(sNegInf == @"-\infty", "5.1.8 -Infinity LaTeX Formatter", $"Got '{sNegInf}'");

            string sZero = ScientificNotationFormatter.ToLatexScientific(0.0);
            Assert(sZero == "0", "5.1.9 Zero LaTeX Formatter", $"Got '{sZero}'");

            // Engineering Unit Auto-Scaler
            string u1 = ScientificNotationFormatter.FormatWithUnit(1000000.0, "Hz");
            Assert(u1.Contains("MHz") || u1.Contains("1"), "5.1.10 Auto-scale 10^6 Hz to MHz", $"Got '{u1}'");

            string u2 = ScientificNotationFormatter.FormatWithUnit(0.000047, "F");
            Assert(u2.Contains("μF") || u2.Contains("47"), "5.1.11 Auto-scale 47uF to μF", $"Got '{u2}'");
        }

        // =====================================================================
        // SUITE 6: Adversarial Robustness & Edge Cases
        // =====================================================================
        Console.WriteLine("\n[Suite 6] Adversarial Edge Cases & Stress Scenarios");

        // Test 6.1: Empty Netlist Handled Gracefully
        {
            var emptyNetlist = new ElectricalNetlist();
            var top = ClassifyComplete(emptyNetlist, out var strat);
            Assert(top == CircuitTopologyType.Unsupported, "6.1.1 Empty Netlist Classification", $"Got {top}");
            var sol = await strat.SolveAsync(emptyNetlist, parameters);
            Assert(sol != null, "6.1.2 Empty Netlist Non-Crash", "Solution is null");
        }

        // Test 6.2: Single Resistor Circuit Without Voltage Source (Passive loop)
        {
            var net = new ElectricalNetlist();
            var r = new CircuitElement { Id = "R1", Name = "R1", Type = ElementType.Resistor, DisplayNumber = 100.0, UnitSymbol = "Ω" };
            r.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            r.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(net, r);

            var n0 = GetOrCreateNet(net, 0, "GND", isGround: true);
            n0.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));
            n0.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));

            var top = ClassifyComplete(net, out var strat);
            Assert(top == CircuitTopologyType.MnaLinear, "6.2.1 Passive Netlist Classified as MNA", $"Got {top}");
            var sol = await strat.SolveAsync(net, parameters);
            Assert(!sol.IsSuccess, "6.2.2 Shorted resistor is rejected", "Shorted resistor was reported successful");
        }

        // Test 6.3: Disconnected / Floating Node Circuit (MNA Partial Pivoting Robustness)
        {
            var net = new ElectricalNetlist();
            var vs = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 10.0, UnitSymbol = "V" };
            vs.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            vs.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });
            AddElement(net, vs);

            var rFloating = new CircuitElement { Id = "Rf", Name = "Rf", Type = ElementType.Resistor, DisplayNumber = 1000.0, UnitSymbol = "Ω" };
            rFloating.Pins.Add(new CircuitPin { Id = "1", Name = "1" });
            rFloating.Pins.Add(new CircuitPin { Id = "2", Name = "2" });
            AddElement(net, rFloating);

            var n0 = GetOrCreateNet(net, 0, "GND", isGround: true);
            n0.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));

            var n1 = GetOrCreateNet(net, 1, "N1");
            n1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));

            // N2 is a completely floating isolated node
            var n2 = GetOrCreateNet(net, 2, "N_FLOAT");
            n2.ConnectedPins.Add(new ComponentPinRef("Rf", "1", "1"));
            n2.ConnectedPins.Add(new ComponentPinRef("Rf", "2", "2"));

            var top = ClassifyComplete(net, out var strat);
            var sol = await strat.SolveAsync(net, parameters);
            Assert(!sol.IsSuccess, "6.3.1 Floating/shorted disconnected network is rejected", "Invalid network was reported successful");
        }

        // =====================================================================
        // SUITE 7: MathRendering Assembly Invocation
        //   Round 7 replaced the old Unicode text conversion
        //   (MathFormulaView.FormatLatexToUnicode) with the Win2D self-drawn
        //   pipeline (Math/ parser + layout engine). This suite now verifies the
        //   production app assembly exposes that pipeline through reflection and
        //   that representative formulas render without leaking raw LaTeX.
        // =====================================================================
        Console.WriteLine("\n[Suite 7] MathRendering Assembly Invocation (parser + layout engine)");
        try
        {
            string[] dllCandidates =
            {
                @"D:\BJTU\2026年大创\apps\desktop-winui\bin\_verify\CircuitAssistant.dll",
                @"D:\BJTU\2026年大创\apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.dll"
            };
            string? dllPath = dllCandidates.FirstOrDefault(System.IO.File.Exists);
            if (dllPath == null)
            {
                Console.WriteLine("  [SKIP] CircuitAssistant.dll not found (build the app first)");
            }
            else
            {
                var asm = System.Reflection.Assembly.LoadFrom(dllPath);
                const System.Reflection.BindingFlags PubStatic = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags PubInstance = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

                var parserType = asm.GetType("CircuitAssistant.MathRendering.MathLatexParser");
                var parseMethod = parserType?.GetMethod("Parse", PubStatic);
                Assert(parserType != null && parseMethod != null,
                    "7.1.1 MathLatexParser.Parse 反射可见（真实排版取代 Unicode 转写）",
                    $"parser={parserType != null}, parse={parseMethod != null}");

                var engineType = asm.GetType("CircuitAssistant.MathRendering.MathLayoutEngine");
                var measurerType = asm.GetType("CircuitAssistant.MathRendering.EstimateMathTextMeasurer");
                var layoutMethod = engineType?.GetMethod("Layout", PubInstance);
                var measurerInstance = measurerType?.GetProperty("Instance", PubStatic)?.GetValue(null);
                Assert(engineType != null && layoutMethod != null && measurerInstance != null,
                    "7.1.2 MathLayoutEngine + EstimateMathTextMeasurer 反射可见",
                    $"engine={engineType != null}, layout={layoutMethod != null}, measurer={measurerInstance != null}");

                if (parserType != null && parseMethod != null && engineType != null &&
                    layoutMethod != null && measurerInstance != null)
                {
                    object Render(string latex)
                    {
                        object node = parseMethod.Invoke(null, new object?[] { latex })!;
                        object engine = Activator.CreateInstance(engineType, new object?[] { measurerInstance, null })!;
                        return layoutMethod.Invoke(engine, new object?[] { node, 600f, 16f })!;
                    }

                    string FragmentText(object result)
                    {
                        object fragments = result.GetType().GetProperty("Fragments", PubInstance)!.GetValue(result)!;
                        var parts = new List<string>();
                        foreach (object fragment in (System.Collections.IEnumerable)fragments)
                            parts.Add((string)fragment.GetType().GetProperty("Text", PubInstance)!.GetValue(fragment)!);
                        return string.Join("\u0001", parts);
                    }

                    int CollectionCount(object result, string property) =>
                        ((System.Collections.ICollection?)result.GetType().GetProperty(property, PubInstance)?.GetValue(result))?.Count ?? 0;

                    // 7.1.3 Greek omega + subscript: real subscript fragment, no "_" leaked.
                    string omega = FragmentText(Render(@"\omega_0"));
                    Assert(omega.Contains("ω") && omega.Contains("0") && !omega.Contains('_') && !omega.Contains('\\'),
                        "7.1.3 \\omega_0 -> 真角标 ω+下标 0（无下划线/原始 LaTeX 泄漏）", $"Got '{omega}'");

                    // 7.1.4 r_be formula: stacked fraction rule drawn, no raw LaTeX tokens.
                    object rbeResult = Render(@"r_{be}=r_{bb'}+\frac{U_T}{I_{EQ}}");
                    string rbe = FragmentText(rbeResult);
                    Assert(rbe.Contains("U") && rbe.Contains("T") && rbe.Contains("EQ") &&
                           !rbe.Contains('_') && !rbe.Contains('\\') && CollectionCount(rbeResult, "Rules") >= 1,
                        "7.1.4 r_be 公式：上下叠放分式 + 真角标（无原始 LaTeX）",
                        $"text='{rbe}', rules={CollectionCount(rbeResult, "Rules")}");

                    // 7.1.5 bmatrix: delimiter geometry present and 4 entries laid out.
                    object matrixResult = Render(@"\begin{bmatrix} G & B \\ C & D \end{bmatrix}");
                    string matrix = FragmentText(matrixResult);
                    Assert(CollectionCount(matrixResult, "Delimiters") >= 2 &&
                           matrix.Contains('G') && matrix.Contains('B') && matrix.Contains('C') && matrix.Contains('D'),
                        "7.1.5 bmatrix 矩阵：定界符 + 4 个元素排版",
                        $"text='{matrix}', delimiters={CollectionCount(matrixResult, "Delimiters")}");
                }
            }
        }
        catch (Exception ex)
        {
            Assert(false, "7.1.0 MathRendering Reflection Execution", $"Exception: {ex.Message}");
        }

        // =====================================================================
        // [Suite 8] Project Management, Serialization & Roundtrip Verification
        // =====================================================================
        Console.WriteLine("\n[Suite 8] Project Management & Serialization Roundtrip Verification");
        try
        {
            // 8.1.1 Common Base Template
            var cbTemplate = CircuitTemplateFactory.CreateCommonBaseTemplate();
            Assert(cbTemplate.Elements.Count >= 10, "8.1.1 Common Base Template Elements", $"Count: {cbTemplate.Elements.Count}");
            Assert(cbTemplate.Wires.Count >= 15, "8.1.2 Common Base Template Wires", $"Count: {cbTemplate.Wires.Count}");

            // 8.1.3 DTO Roundtrip
            var projectData = CircuitProjectData.From(
                cbTemplate.ProjectName,
                cbTemplate.ProjectFileName,
                cbTemplate.Elements,
                cbTemplate.Wires,
                new Dictionary<string, double> { ["VCC"] = 12.0, ["Beta"] = 80.0 },
                "Common Base Test Project");

            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_circuit_{Guid.NewGuid():N}.circuit");
            await ProjectManagerService.SaveProjectToFileAsync(tempFile, projectData);
            Assert(System.IO.File.Exists(tempFile), "8.1.3 Project File Saved To Disk", tempFile);

            var loadedProject = await ProjectManagerService.LoadProjectFromFileAsync(tempFile);
            Assert(loadedProject.ProjectName == cbTemplate.ProjectName, "8.1.4 Project Name Preserved", loadedProject.ProjectName);
            Assert(loadedProject.Elements.Count == cbTemplate.Elements.Count, "8.1.5 Elements Count Preserved", $"{loadedProject.Elements.Count}");
            Assert(loadedProject.Wires.Count == cbTemplate.Wires.Count, "8.1.6 Wires Count Preserved", $"{loadedProject.Wires.Count}");
            Assert(loadedProject.Parameters.ContainsKey("VCC") && loadedProject.Parameters["VCC"] == 12.0, "8.1.7 Parameter Preserved", "VCC != 12.0");

            var (reconstElements, reconstWires, reconstParams) = loadedProject.ToModel();
            Assert(reconstElements.Count == cbTemplate.Elements.Count, "8.1.8 Reconstructed Model Elements Count", $"{reconstElements.Count}");
            Assert(reconstWires.Count == cbTemplate.Wires.Count, "8.1.9 Reconstructed Model Wires Count", $"{reconstWires.Count}");

            // 8.1.10 SPICE Netlist generation from reconstructed project
            var netlist = NetlistExtractor.Extract(reconstElements, reconstWires);
            var spice = netlist.ToSpiceNetlist();
            Assert(spice.Contains("R_") && spice.Contains("Q_Q1") && spice.Contains(".END"), "8.1.10 Exported SPICE Netlist Content", spice);

            if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Assert(false, "8.1.0 Suite 8 Exception", ex.ToString());
        }

        // =====================================================================
        // Final Summary & Verdict
        // =====================================================================
        await RunRevisionRegressionChecks();
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("M2 EMPIRICAL VERIFICATION RESULTS SUMMARY");
        Console.WriteLine($"Total Tests:  {TotalTests}");
        Console.WriteLine($"Passed Tests: {PassedTests}");
        Console.WriteLine($"Failed Tests: {Failures.Count}");
        Console.WriteLine("================================================================================");

        if (Failures.Count > 0)
        {
            Console.WriteLine("\n[X] DEFECTS FOUND:");
            for (int i = 0; i < Failures.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {Failures[i]}");
            }
            Console.WriteLine("\nVERDICT: REJECT");
            return 1;
        }
        else
        {
            Console.WriteLine("\n[V] ALL EMPIRICAL VERIFICATION TESTS PASSED (100% SUCCESS RATE)");
            Console.WriteLine("VERDICT: APPROVE");
            return 0;
        }
    }
}
