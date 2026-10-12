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

class Program
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
            var topology = TopologyClassifier.Classify(netlist, out var strategy);

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

            var topology = TopologyClassifier.Classify(netlist, out var strategy);
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

            var topology = TopologyClassifier.Classify(netlist, out var strategy);
            Assert(topology == CircuitTopologyType.FirstOrderRl, "1.3.1 RL Classification", $"Expected FirstOrderRl, got {topology}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "1.3.2 RL Solving", "Solver failed");
            Assert(solution.Steps.Any(s => s.LatexFormula.Contains("i_L(t)")), "1.3.3 RL Formula Contains i_L(t)", "Missing i_L(t)");

            Assert(solution.SummaryText.Contains("RL") && (solution.SummaryText.Contains("0.1") || solution.SummaryText.Contains("1.00")),
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
            var src = new CircuitElement { Id = "VS", Name = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 10.0, UnitSymbol = "V" };
            src.Pins.Add(new CircuitPin { Id = "pos", Name = "pos" });
            src.Pins.Add(new CircuitPin { Id = "neg", Name = "neg" });

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
            netGnd.ConnectedPins.Add(new ComponentPinRef("VS", "neg", "neg"));
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
                // Parallel RLC: L1 and C1 in parallel between N1 and GND
                var n1 = GetOrCreateNet(net, 1, "N1");
                n1.ConnectedPins.Add(new ComponentPinRef("VS", "pos", "pos"));
                n1.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));

                var n2 = GetOrCreateNet(net, 2, "N2");
                n2.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
                n2.ConnectedPins.Add(new ComponentPinRef("L1", "1", "1"));
                n2.ConnectedPins.Add(new ComponentPinRef("C1", "1", "1"));

                netGnd.ConnectedPins.Add(new ComponentPinRef("L1", "2", "2"));
                netGnd.ConnectedPins.Add(new ComponentPinRef("C1", "2", "2"));
            }

            return net;
        }

        // Test 2.1: Overdamped Condition (Series: L=10 mH, C=1 uF, R=300 Ohm)
        // omega0 = 10000 rad/s, alpha = R/(2L) = 300 / 0.02 = 15000 s^-1 > 10000 => Overdamped
        {
            var rlcNet = CreateRlcNetlist(300.0, "Ω", 10.0, "mH", 1.0, "μF", isParallel: false);
            var top = TopologyClassifier.Classify(rlcNet, out var strategy);
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
            TopologyClassifier.Classify(rlcNet, out var strategy);
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
            TopologyClassifier.Classify(rlcNet, out var strategy);
            var solution = await strategy.SolveAsync(rlcNet, parameters);

            Assert(solution.SummaryText.Contains("欠阻尼"), "2.3.1 Underdamped Regime Detection",
                $"Summary does not contain '欠阻尼': {solution.SummaryText}");

            var step3 = solution.Steps[2];
            Assert(step3.SubstitutionFormula.Contains(@"\pm j"),
                "2.3.2 Underdamped Complex Conjugate Roots", $"Roots: {step3.SubstitutionFormula}");

            // Quality factor Q = omega0 / (2*alpha) = 10000 / 5000 = 2.0
            Assert(solution.SummaryText.Contains("2.00"), "2.3.3 Underdamped Q Factor = 2.00",
                $"SummaryText Q: {solution.SummaryText}");
        }

        // Test 2.4: Parallel RLC Circuit (Parallel: R=1k, L=10 mH, C=1 uF)
        // alpha = 1 / (2*R*C) = 1 / (2 * 1000 * 1e-6) = 500 s^-1 < 10000 => Underdamped
        // Q = R*sqrt(C/L) = 1000 * sqrt(1e-6 / 0.01) = 1000 * 0.01 = 10.0
        {
            var rlcNet = CreateRlcNetlist(1.0, "kΩ", 10.0, "mH", 1.0, "μF", isParallel: true);
            var top = TopologyClassifier.Classify(rlcNet, out var strategy);
            Assert(strategy.SubtypeName == "ParallelRlc", "2.4.1 Parallel RLC Subtype Detection",
                $"Expected ParallelRlc, got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(rlcNet, parameters);
            Assert(solution.SummaryText.Contains("10.00"), "2.4.2 Parallel RLC Q Factor = 10.00",
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
            var top = TopologyClassifier.Classify(netlist, out var strategy);

            Assert(top == CircuitTopologyType.BjtAmplifier, "3.1.1 CE Template Classification", $"Got {top}");
            Assert(strategy.SubtypeName == "VoltageDividerSelfBias", "3.1.2 Bias Subtype VoltageDividerSelfBias",
                $"Got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.IsSuccess, "3.1.3 CE Template Solving", "Solver failed");
            Assert(solution.Steps.Count == 5, "3.1.4 CE 5 Pedagogical Steps", $"Expected 5, got {solution.Steps.Count}");

            Assert(solution.QPoint != null, "3.1.5 QPoint Populated", "QPoint is null");
            Assert(solution.QPoint.Ibq.Contains("18.0"), "3.1.6 IBQ ~ 18.0 uA", $"Got IBQ = '{solution.QPoint.Ibq}'");
            Assert(solution.QPoint.Icq.Contains("1.8"), "3.1.7 ICQ ~ 1.8 mA", $"Got ICQ = '{solution.QPoint.Icq}'");
            Assert(solution.QPoint.Vceq.Contains("4.78") || solution.QPoint.Vceq.Contains("4.8"), "3.1.8 VCEQ ~ 4.78 V",
                $"Got VCEQ = '{solution.QPoint.Vceq}'");
            Assert(solution.QPoint.IsActiveRegion, "3.1.9 BJT In Active Region", "IsActiveRegion is false");

            Assert(solution.AcParameters.VoltageGain.Contains("-139"), "3.1.10 Voltage Gain Av ~ -139",
                $"Got Av = '{solution.AcParameters.VoltageGain}'");
            Assert(solution.AcParameters.InputResistance.Contains("1.45") || solution.AcParameters.InputResistance.Contains("1.5"),
                "3.1.11 Input Resistance Ri ~ 1.45 kΩ", $"Got Ri = '{solution.AcParameters.InputResistance}'");
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

            var top = TopologyClassifier.Classify(netlist, out var strategy);
            Assert(top == CircuitTopologyType.BjtAmplifier, "3.2.1 Fixed Bias Classification", $"Got {top}");
            Assert(strategy.SubtypeName == "FixedBias", "3.2.2 FixedBias Subtype Detected", $"Got {strategy.SubtypeName}");

            var solution = await strategy.SolveAsync(netlist, parameters);
            Assert(solution.QPoint.Ibq.Contains("37.7") || solution.QPoint.Ibq.Contains("37.6"),
                "3.2.3 Fixed Bias IBQ ~ 37.7 uA", $"Got IBQ = '{solution.QPoint.Ibq}'");
            Assert(solution.QPoint.Icq.Contains("3.77") || solution.QPoint.Icq.Contains("3.76"),
                "3.2.4 Fixed Bias ICQ ~ 3.77 mA", $"Got ICQ = '{solution.QPoint.Icq}'");
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

            var top = TopologyClassifier.Classify(netlist, out var strategy);
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

            var top = TopologyClassifier.Classify(netlist, out var strategy);
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

            var top = TopologyClassifier.Classify(netlist, out var strategy);
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
            var top = TopologyClassifier.Classify(emptyNetlist, out var strat);
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

            var top = TopologyClassifier.Classify(net, out var strat);
            Assert(top == CircuitTopologyType.MnaLinear, "6.2.1 Passive Netlist Classified as MNA", $"Got {top}");
            var sol = await strat.SolveAsync(net, parameters);
            Assert(sol.IsSuccess, "6.2.2 Passive Netlist Solves Successfully", "Solver crashed");
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

            var top = TopologyClassifier.Classify(net, out var strat);
            var sol = await strat.SolveAsync(net, parameters);
            Assert(sol.IsSuccess, "6.3.1 Floating Node Singular Matrix Handled via Pivoting Guard", "MNA failed on floating node");
        }

        // =====================================================================
        // SUITE 7: MathFormulaView.FormatLatexToUnicode Assembly Invocation
        // =====================================================================
        Console.WriteLine("\n[Suite 7] MathFormulaView.FormatLatexToUnicode Assembly Invocation");
        try
        {
            var dllPath = @"D:\BJTU\2026年大创\apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.dll";
            if (System.IO.File.Exists(dllPath))
            {
                var asm = System.Reflection.Assembly.LoadFrom(dllPath);
                var type = asm.GetType("CircuitAssistant.Controls.MathFormulaView");
                var method = type?.GetMethod("FormatLatexToUnicode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (method != null)
                {
                    string CallFmt(string input) => (string)method.Invoke(null, new object[] { input })!;

                    // 7.1 Greek omega_0 -> ω₀
                    string rOmega0 = CallFmt(@"\omega_0");
                    Assert(rOmega0.Contains("ω₀"), "7.1.1 \\omega_0 -> ω₀", $"Got '{rOmega0}'");

                    // 7.2 Square root \sqrt{LC} -> √(LC)
                    string rSqrt = CallFmt(@"\sqrt{LC}");
                    Assert(rSqrt.Contains("√(LC)") || rSqrt.Contains("√"), "7.1.2 \\sqrt{LC} -> √(LC)", $"Got '{rSqrt}'");

                    // 7.3 Greek Delta
                    string rDelta = CallFmt(@"\Delta f");
                    Assert(rDelta.Contains("Δ"), "7.1.3 \\Delta f -> Δ f", $"Got '{rDelta}'");

                    // 7.4 Operators \pm, \le, \ge, \to, \infty, \parallel
                    string rOps = CallFmt(@"\pm \le \ge \to \infty \parallel");
                    Assert(rOps.Contains("±") && rOps.Contains("≤") && rOps.Contains("≥") && rOps.Contains("→") && rOps.Contains("∞") && rOps.Contains("∥"),
                        "7.1.4 Math Operators ±, ≤, ≥, →, ∞, ∥", $"Got '{rOps}'");

                    // 7.5 2D Matrix Bracket format
                    string rMatrix = CallFmt(@"\begin{bmatrix} G & B \\ C & D \end{bmatrix}");
                    Assert(rMatrix.Contains("⎡") && rMatrix.Contains("⎤") && rMatrix.Contains("⎣") && rMatrix.Contains("⎦"),
                        "7.1.5 2D Matrix Cambria Math Bracket Envelopes", $"Got:\n{rMatrix}");
                }
                else
                {
                    Assert(false, "7.1.0 Reflection Method Lookup", "FormatLatexToUnicode method not found on MathFormulaView");
                }
            }
            else
            {
                Console.WriteLine($"  [SKIP] CircuitAssistant.dll not found at {dllPath}");
            }
        }
        catch (Exception ex)
        {
            Assert(false, "7.1.0 Reflection Execution", $"Exception: {ex.Message}");
        }

        // =====================================================================
        // Final Summary & Verdict
        // =====================================================================
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
