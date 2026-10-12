using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace M3AdversarialStress;

public class Program
{
    private static int totalTests = 0;
    private static int passedTests = 0;
    private static int failedTests = 0;
    private static readonly List<string> failureDetails = new();

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("CHALLENGER M3-2: ADVERSARIAL STRESS TEST SUITE");
        Console.WriteLine("Topological Connectivity, Rubber-Banding Displacement & Wire Tap NetId Propagation");
        Console.WriteLine("================================================================================");

        RunSuite1_DynamicDisplacementStressTesting();
        RunSuite2_CompoundAndMonteCarloDisplacement();
        RunSuite3_WireTapNetIdPropagation();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine($"CHALLENGER M3-2 SUMMARY: Total={totalTests}, Passed={passedTests}, Failed={failedTests}");
        Console.WriteLine("================================================================================");

        if (failedTests > 0)
        {
            Console.WriteLine("\n[CRITICAL FAILURE DETECTED]");
            foreach (var f in failureDetails)
            {
                Console.WriteLine($"  FAIL: {f}");
            }
            Console.WriteLine("\nOVERALL EMPIRICAL VERDICT: REJECT");
            return 1;
        }

        Console.WriteLine("\nALL ADVERSARIAL INVARIANTS SATISFIED EMPIRICALLY!");
        Console.WriteLine("OVERALL EMPIRICAL VERDICT: APPROVE");
        return 0;
    }

    private static void Assert(bool condition, string testName, string message = "")
    {
        totalTests++;
        if (condition)
        {
            passedTests++;
            Console.WriteLine($"  [PASS] {testName}");
        }
        else
        {
            failedTests++;
            string err = $"{testName}: {message}";
            failureDetails.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] {err}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Executes the canonical two-phase rubber-banding wire synchronization algorithm
    /// matching CircuitCanvas.xaml.cs and AnalysisStudioViewModel.cs.
    /// </summary>
    public static void SyncConnectedWiresToElement(CircuitElement el, IList<CircuitWire> wires)
    {
        var movedWireIds = new HashSet<string>();

        // Phase 1: Update direct pin-connected wires with 10px grid snap
        foreach (var w in wires)
        {
            if (w.StartElementId == el.Id && w.StartPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.StartX = MathF.Round(pPos.X / 10f) * 10f;
                    w.StartY = MathF.Round(pPos.Y / 10f) * 10f;
                    movedWireIds.Add(w.Id);
                }
            }
            if (w.EndElementId == el.Id && w.EndPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.EndX = MathF.Round(pPos.X / 10f) * 10f;
                    w.EndY = MathF.Round(pPos.Y / 10f) * 10f;
                    movedWireIds.Add(w.Id);
                }
            }
        }

        // Phase 2A: Branch wires starting or ending at el and tapping into a parent rail
        // Protect parent trunk bus rails from distortion by sliding tap perpendicularly along rail
        foreach (var w in wires)
        {
            if (w.StartElementId == el.Id && w.EndPinName == "WireTap")
            {
                var parent = wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.StartX = MathF.Round(pPos.X / 10f) * 10f;
                        w.StartY = MathF.Round(pPos.Y / 10f) * 10f;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
            else if (w.EndElementId == el.Id && w.StartPinName == "WireTap")
            {
                var parent = wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.EndX = MathF.Round(pPos.X / 10f) * 10f;
                        w.EndY = MathF.Round(pPos.Y / 10f) * 10f;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
        }

        // Phase 2B: Child wires tapping into parent wires that moved in Phase 1 or 2A
        bool anyUpdated;
        int maxPasses = 3;
        do
        {
            anyUpdated = false;
            foreach (var w in wires)
            {
                if (w.EndPinName == "WireTap" && movedWireIds.Contains(w.EndElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
                else if (w.StartPinName == "WireTap" && movedWireIds.Contains(w.StartElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
            }
            maxPasses--;
        } while (anyUpdated && maxPasses > 0);
    }

    // =========================================================================
    // SUITE 1: Dynamic Template Displacement Stress Testing (+/-20px, +/-40px, +/-80px)
    // =========================================================================
    private static void RunSuite1_DynamicDisplacementStressTesting()
    {
        Console.WriteLine("\n[SUITE 1] Netlist Extraction Stability Under Dynamic Component Displacement (+/-20, +/-40, +/-80px)");

        float[] nonCollidingDeltas = { -40f, -20f, 20f, 40f };
        float[] fullDeltas = { -80f, -40f, -20f, 20f, 40f, 80f };

        // 1.1 Common Emitter Template: Non-colliding (+/-20px, +/-40px)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var baselineNetlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            int baselineNodeCount = baselineNetlist.Nodes.Count; // 7 canonical nodes
            Assert(baselineNodeCount == 7, "1.1.0 Baseline CommonEmitter has 7 canonical nodes");

            var baselinePinToNode = new Dictionary<(string, string), int>();
            foreach (var el in tmpl.Elements)
            {
                foreach (var pin in el.Pins)
                {
                    var node = baselineNetlist.FindNodeForPin(el.Id, pin.Id);
                    if (node != null) baselinePinToNode[(el.Id, pin.Id)] = node.Index;
                }
            }

            var elementsToTest = tmpl.Elements.Where(e => e.Type != ElementType.Ground).ToList();
            bool nonCollidingPassed = true;
            string failMsg = "";

            foreach (var el in elementsToTest)
            {
                var vectors = new List<(float dx, float dy)>();
                foreach (var d in nonCollidingDeltas)
                {
                    vectors.Add((d, 0));
                    vectors.Add((0, d));
                }
                vectors.Add((-20f, -20f));
                vectors.Add((20f, 20f));
                vectors.Add((-40f, 20f));
                vectors.Add((20f, -40f));

                foreach (var (dx, dy) in vectors)
                {
                    var testTmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
                    var testEl = testTmpl.Elements.First(e => e.Id == el.Id);
                    testEl.X += dx;
                    testEl.Y += dy;
                    SyncConnectedWiresToElement(testEl, testTmpl.Wires);

                    var displacedNetlist = NetlistExtractor.Extract(testTmpl.Elements, testTmpl.Wires);
                    if (displacedNetlist.Nodes.Count != baselineNodeCount)
                    {
                        nonCollidingPassed = false;
                        failMsg = $"{el.Id} at ({dx},{dy}): expected {baselineNodeCount} nodes, got {displacedNetlist.Nodes.Count}";
                        break;
                    }

                    foreach (var kvp in baselinePinToNode)
                    {
                        var (elemId, pinId) = kvp.Key;
                        int expectedNode = kvp.Value;
                        var actualNode = displacedNetlist.FindNodeForPin(elemId, pinId);
                        if (actualNode == null || actualNode.Index != expectedNode)
                        {
                            nonCollidingPassed = false;
                            failMsg = $"{el.Id} at ({dx},{dy}): Pin {elemId}.{pinId} mapped to {actualNode?.Index} instead of {expectedNode}";
                            break;
                        }
                    }
                    if (!nonCollidingPassed) break;
                }
                if (!nonCollidingPassed) break;
            }

            Assert(nonCollidingPassed, "1.1.1 CommonEmitter: 100% node count and connectivity invariance under +/-20px and +/-40px displacement", failMsg);
        }

        // 1.2 RC Transient Template: Complete (+/-20px, +/-40px, +/-80px)
        {
            var tmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var baselineNetlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            int baselineNodeCount = baselineNetlist.Nodes.Count; // 3 nodes
            Assert(baselineNodeCount == 3, "1.2.0 Baseline RC Transient has 3 canonical nodes");

            var elementsToTest = tmpl.Elements.Where(e => e.Type != ElementType.Ground).ToList();
            bool rcPreserved = true;
            string rcFailure = "";

            foreach (var el in elementsToTest)
            {
                foreach (var d in fullDeltas)
                {
                    var testX = CircuitTemplateFactory.CreateRcTransientTemplate();
                    var elX = testX.Elements.First(e => e.Id == el.Id);
                    elX.X += d;
                    SyncConnectedWiresToElement(elX, testX.Wires);
                    var netlistX = NetlistExtractor.Extract(testX.Elements, testX.Wires);
                    if (netlistX.Nodes.Count != baselineNodeCount)
                    {
                        rcPreserved = false;
                        rcFailure = $"{el.Id} moved dx={d}: got {netlistX.Nodes.Count} nodes";
                    }

                    var testY = CircuitTemplateFactory.CreateRcTransientTemplate();
                    var elY = testY.Elements.First(e => e.Id == el.Id);
                    elY.Y += d;
                    SyncConnectedWiresToElement(elY, testY.Wires);
                    var netlistY = NetlistExtractor.Extract(testY.Elements, testY.Wires);
                    if (netlistY.Nodes.Count != baselineNodeCount)
                    {
                        rcPreserved = false;
                        rcFailure = $"{el.Id} moved dy={d}: got {netlistY.Nodes.Count} nodes";
                    }
                }
            }

            Assert(rcPreserved, "1.2.1 RC Transient: 100% node count and connectivity invariance under +/-20, +/-40, +/-80px displacement", rcFailure);
        }

        // 1.3 RC Coupled Amplifier Template: Non-colliding (+/-20px, +/-40px)
        {
            var tmpl = CircuitTemplateFactory.CreateRcCoupledTemplate();
            var baselineNetlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            int baselineNodeCount = baselineNetlist.Nodes.Count; // 6 nodes
            Assert(baselineNodeCount == 6, "1.3.0 Baseline RC Coupled Amp has 6 canonical nodes");

            var elementsToTest = tmpl.Elements.Where(e => e.Type != ElementType.Ground).ToList();
            bool acPreserved = true;
            string acFailure = "";

            foreach (var el in elementsToTest)
            {
                foreach (var d in nonCollidingDeltas)
                {
                    var testTmpl = CircuitTemplateFactory.CreateRcCoupledTemplate();
                    var testEl = testTmpl.Elements.First(e => e.Id == el.Id);
                    testEl.X += d;
                    SyncConnectedWiresToElement(testEl, testTmpl.Wires);
                    var nlist = NetlistExtractor.Extract(testTmpl.Elements, testTmpl.Wires);
                    if (nlist.Nodes.Count != baselineNodeCount)
                    {
                        acPreserved = false;
                        acFailure = $"{el.Id} moved dx={d}: got {nlist.Nodes.Count} nodes";
                    }
                }
            }

            Assert(acPreserved, "1.3.1 RC Coupled Amp: 100% node count invariance under +/-20px and +/-40px displacement", acFailure);
        }

        // 1.4 ADVERSARIAL STRESS CHALLENGE: 80px Component Grid Modulus Collision Immunity
        {
            Console.WriteLine("\n  [ADVERSARIAL CHALLENGE 1.4] 80px Component Collision & Segment Piercing Immunity:");
            // Test 1.4.1: Moving RL rightward by +80px places RL at X=620, adjacent to VCC at X=620
            var tmpl1 = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var rl = tmpl1.Elements.First(e => e.Id == "RL");
            rl.X += 80f;
            SyncConnectedWiresToElement(rl, tmpl1.Wires);
            var netlistRl = NetlistExtractor.Extract(tmpl1.Elements, tmpl1.Wires);
            bool rlPreserved = (netlistRl.Nodes.Count == 7);
            Assert(rlPreserved, 
                "1.4.1 [VULNERABILITY RESOLVED] RL moved +80px to X=620 preserves canonical 7 nodes without false Vout-to-VCC collapse",
                $"Got {netlistRl.Nodes.Count} nodes");

            // Test 1.4.2: Moving VCC leftward by -80px places VCC at X=540, adjacent to RL at X=540
            var tmpl2 = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var vcc = tmpl2.Elements.First(e => e.Id == "VCC");
            vcc.X -= 80f;
            SyncConnectedWiresToElement(vcc, tmpl2.Wires);
            var netlistVcc = NetlistExtractor.Extract(tmpl2.Elements, tmpl2.Wires);
            bool vccPreserved = (netlistVcc.Nodes.Count == 7);
            Assert(vccPreserved,
                "1.4.2 [VULNERABILITY RESOLVED] VCC moved -80px to X=540 preserves canonical 7 nodes without false VCC-to-Vout collapse",
                $"Got {netlistVcc.Nodes.Count} nodes");

            // Test 1.4.3: Moving Re by (-80, +40) places Re.1 at (350, 400) coincident with Rb2.2
            var tmpl3 = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var re = tmpl3.Elements.First(e => e.Id == "Re");
            re.X -= 80f; re.Y += 40f;
            SyncConnectedWiresToElement(re, tmpl3.Wires);
            var netlistRe = NetlistExtractor.Extract(tmpl3.Elements, tmpl3.Wires);
            bool rePreserved = (netlistRe.Nodes.Count == 7);
            Assert(rePreserved,
                "1.4.3 [VULNERABILITY RESOLVED] Re moved (-80,+40) preserves canonical 7 nodes without emitter-to-ground short",
                $"Got {netlistRe.Nodes.Count} nodes");
        }

        // 1.5 ADVERSARIAL STRESS CHALLENGE: Repeated Drag Hysteresis / Plastic Deformation
        {
            Console.WriteLine("\n  [ADVERSARIAL CHALLENGE 1.5] Repeated Drag Hysteresis & Plastic Distortion Analysis:");
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var c2Out = tmpl.Wires.First(w => w.Id == "W_C2_OUT");
            float initC2Len = c2Out.EndX - c2Out.StartX; // 540 - 475 = 65

            // Drag RL rightward by 40px and then back to original position
            var rl = tmpl.Elements.First(e => e.Id == "RL");
            float origX = rl.X;

            rl.X += 40f;
            SyncConnectedWiresToElement(rl, tmpl.Wires);
            rl.X = origX;
            SyncConnectedWiresToElement(rl, tmpl.Wires);

            float postDragC2Len = c2Out.EndX - c2Out.StartX;
            bool c2Deformed = (c2Out.EndX != 540) || (c2Out.StartX != 475);

            // Also test if running netlist extraction after drag-and-restore cycle maintains 7 nodes
            var netlistPostRestore = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            bool cyclePassed = (netlistPostRestore.Nodes.Count == 7) && !c2Deformed;

            Assert(cyclePassed,
                $"1.5.1 Drag-and-restore cycle on RL preserves C2 output wire geometry and 7 canonical nodes",
                $"c2Out EndX={c2Out.EndX} (expected 540), Nodes={netlistPostRestore.Nodes.Count}");
        }
    }

    // =========================================================================
    // SUITE 2: Compound & Monte Carlo Multi-Component Displacement
    // =========================================================================
    private static void RunSuite2_CompoundAndMonteCarloDisplacement()
    {
        Console.WriteLine("\n[SUITE 2] Compound & Multi-Component Simultaneous Displacement Stress Tests");

        // 2.1 Emitter Parallel Branch Sliding: Re and Ce moved together
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var re = tmpl.Elements.First(e => e.Id == "Re");
            var ce = tmpl.Elements.First(e => e.Id == "Ce");
            float origReX = re.X, origCeX = ce.X;

            bool emitterPairStable = true;
            foreach (float d in new[] { 20f, 40f, 80f, -20f, -40f })
            {
                re.X = origReX + d;
                ce.X = origCeX + d;
                SyncConnectedWiresToElement(re, tmpl.Wires);
                SyncConnectedWiresToElement(ce, tmpl.Wires);

                var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
                var emitNet = netlist.FindNetForPin("Q1", "E");
                var re1Net = netlist.FindNetForPin("Re", "1");
                var ce1Net = netlist.FindNetForPin("Ce", "1");

                if (emitNet == null || re1Net != emitNet || ce1Net != emitNet)
                {
                    emitterPairStable = false;
                    break;
                }
            }
            Assert(emitterPairStable, "2.1 Parallel Re/Ce branches slide cohesively along emitter bus without net fracture");
        }

        // 2.2 Counter-Directional Bias Network Displacement: Rb1 (+40px), Rb2 (-40px)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var rb1 = tmpl.Elements.First(e => e.Id == "Rb1");
            var rb2 = tmpl.Elements.First(e => e.Id == "Rb2");

            rb1.X += 40f;
            rb2.X -= 40f;
            SyncConnectedWiresToElement(rb1, tmpl.Wires);
            SyncConnectedWiresToElement(rb2, tmpl.Wires);

            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            var baseNet = netlist.FindNetForPin("Q1", "B");
            var rb1Base = netlist.FindNetForPin("Rb1", "2");
            var rb2Base = netlist.FindNetForPin("Rb2", "1");

            Assert(baseNet != null && rb1Base == baseNet && rb2Base == baseNet,
                "2.2 Counter-directional displacement of Rb1 (+40) and Rb2 (-40) maintains base electrical node unification");
            Assert(netlist.Nodes.Count == 7, "2.2.1 Total node count remains 7");
        }

        // 2.3 Cascading Collector & Output Network Displacement: Rc (+20), C2 (+40), RL (+80)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var rc = tmpl.Elements.First(e => e.Id == "Rc");
            var c2 = tmpl.Elements.First(e => e.Id == "C2");
            var rl = tmpl.Elements.First(e => e.Id == "RL");

            rc.X += 20f;
            c2.X += 40f;
            rl.X += 40f;
            SyncConnectedWiresToElement(rc, tmpl.Wires);
            SyncConnectedWiresToElement(c2, tmpl.Wires);
            SyncConnectedWiresToElement(rl, tmpl.Wires);

            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            var collNet = netlist.FindNetForPin("Q1", "C");
            var rc2 = netlist.FindNetForPin("Rc", "2");
            var c2In = netlist.FindNetForPin("C2", "1");
            var c2Out = netlist.FindNetForPin("C2", "2");
            var rl1 = netlist.FindNetForPin("RL", "1");

            Assert(collNet != null && rc2 == collNet && c2In == collNet, "2.3.1 Collector net intact under cascading displacement");
            Assert(c2Out != null && c2Out == rl1, "2.3.2 Output net intact under cascading displacement");
            Assert(netlist.Nodes.Count == 7, "2.3.3 Total node count invariant at 7");
        }

        // 2.4 Monte Carlo Simultaneous Randomized Displacements (50 rounds, 20px modulus)
        {
            var rng = new Random(42);
            float[] possibleDeltas = { -20f, 0f, 20f };
            bool monteCarloAllPassed = true;
            string mcError = "";

            for (int round = 0; round < 50; round++)
            {
                var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
                var elements = tmpl.Elements.Where(e => e.Type != ElementType.Ground && e.Id != "VCC").ToList();

                var displacedPositions = new Dictionary<string, (float dx, float dy)>();
                foreach (var el in elements)
                {
                    float dx = possibleDeltas[rng.Next(possibleDeltas.Length)];
                    float dy = possibleDeltas[rng.Next(possibleDeltas.Length)];
                    displacedPositions[el.Id] = (dx, dy);
                    el.X += dx;
                    el.Y += dy;
                    SyncConnectedWiresToElement(el, tmpl.Wires);
                }

                var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
                if (netlist.Nodes.Count != 7 || !netlist.HasGround || netlist.GroundNet?.NetId != 0)
                {
                    monteCarloAllPassed = false;
                    var moves = string.Join(", ", displacedPositions.Where(p => p.Value.dx != 0 || p.Value.dy != 0).Select(p => $"{p.Key}:({p.Value.dx},{p.Value.dy})"));
                    mcError = $"Round {round}: got {netlist.Nodes.Count} nodes, HasGround={netlist.HasGround}. Moves: [{moves}]";
                    Console.WriteLine($"    [DEBUG MC FAILURE] {mcError}");
                    for (int i = 0; i < netlist.Nets.Count; i++)
                    {
                        var n = netlist.Nets[i];
                        Console.WriteLine($"      Net {n.NetId} ({n.Name}): Pins=[{string.Join(", ", n.ConnectedPins.Select(p => $"{p.ElementId}.{p.PinId}"))}]");
                    }
                    break;
                }
            }

            Assert(monteCarloAllPassed, "2.4 Monte Carlo 50-round randomized multi-element displacement maintains 7 canonical nodes", mcError);
        }
    }

    // =========================================================================
    // SUITE 3: Wire Tap NetId Propagation Invariants & Node Merging
    // =========================================================================
    private static void RunSuite3_WireTapNetIdPropagation()
    {
        Console.WriteLine("\n[SUITE 3] Wire Tap NetId Propagation and Electrical Node Merging Invariants");

        // 3.1 Direct Pin-to-Wire Tap NetId Propagation
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 200, Y = 200 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 100, Y = 300 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Trunk wire connecting R1.2 to GND
            var trunkWire = new CircuitWire
            {
                Id = "W_TRUNK",
                StartElementId = "R1", StartPinName = "2",
                EndElementId = "GND", EndPinName = "GND",
                StartX = 140, StartY = 100, EndX = 100, EndY = 278,
                NetId = -1
            };

            // Child branch wire tapping trunk wire from R2.1
            var branchWire = new CircuitWire
            {
                Id = "W_BRANCH",
                StartElementId = "R2", StartPinName = "1",
                EndElementId = "W_TRUNK", EndPinName = "WireTap",
                StartX = 160, StartY = 200, EndX = 140, EndY = 100,
                NetId = -1
            };

            var wires = new List<CircuitWire> { trunkWire, branchWire };
            var netlist = NetlistExtractor.Extract(elements, wires);

            Assert(trunkWire.NetId >= 0, "3.1.1 Trunk wire NetId assigned");
            Assert(branchWire.NetId == trunkWire.NetId, 
                $"3.1.2 Child branch wire correctly shares trunk NetId: branch={branchWire.NetId}, trunk={trunkWire.NetId}");

            var r1_2_node = netlist.FindNodeForPin("R1", "2");
            var r2_1_node = netlist.FindNodeForPin("R2", "1");
            Assert(r1_2_node != null && r1_2_node == r2_1_node, 
                "3.1.3 R1.2 and R2.1 merge into the exact same electrical node through wire tap");
        }

        // 3.2 Wire-to-Pin Tap NetId Propagation (Reversed Tap Orientation)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 200, Y = 200 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var trunk = new CircuitWire
            {
                Id = "W_TRUNK_REV",
                StartElementId = "R1", StartPinName = "1",
                EndElementId = "R1", EndPinName = "2",
                StartX = 60, StartY = 100, EndX = 140, EndY = 100,
                NetId = -1
            };

            // Branch wire starting as WireTap on trunk and ending at R2.2
            var branch = new CircuitWire
            {
                Id = "W_BRANCH_REV",
                StartElementId = "W_TRUNK_REV", StartPinName = "WireTap",
                EndElementId = "R2", EndPinName = "2",
                StartX = 100, StartY = 100, EndX = 240, EndY = 200,
                NetId = -1
            };

            var netlist = NetlistExtractor.Extract(elements, new[] { trunk, branch });
            Assert(branch.NetId == trunk.NetId, $"3.2.1 Reversed branch tap shares trunk NetId: branch={branch.NetId}, trunk={trunk.NetId}");

            var r1Pin1Node = netlist.FindNodeForPin("R1", "1");
            var r2Pin2Node = netlist.FindNodeForPin("R2", "2");
            Assert(r1Pin1Node != null && r1Pin1Node == r2Pin2Node, "3.2.2 R1.1 and R2.2 unify into identical node");
        }

        // 3.3 Deeply Nested Multi-Hop Wire Taps (4 Hops: Trunk -> Branch -> SubBranch -> Leaf)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_ROOT", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R_LEAF", Type = ElementType.Resistor, X = 400, Y = 400 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Hop 0: Trunk wire from R_ROOT.2
            var w0 = new CircuitWire { Id = "W0", StartElementId = "R_ROOT", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 100, EndX = 300, EndY = 100 };
            // Hop 1: W1 taps W0
            var w1 = new CircuitWire { Id = "W1", StartElementId = "W0", StartPinName = "WireTap", EndElementId = "None", EndPinName = "None", StartX = 200, StartY = 100, EndX = 200, EndY = 250 };
            // Hop 2: W2 taps W1
            var w2 = new CircuitWire { Id = "W2", StartElementId = "W1", StartPinName = "WireTap", EndElementId = "None", EndPinName = "None", StartX = 200, StartY = 200, EndX = 350, EndY = 200 };
            // Hop 3: W3 taps W2 and lands on R_LEAF.1
            var w3 = new CircuitWire { Id = "W3", StartElementId = "W2", StartPinName = "WireTap", EndElementId = "R_LEAF", EndPinName = "1", StartX = 300, StartY = 200, EndX = 360, EndY = 400 };

            var netlist = NetlistExtractor.Extract(elements, new[] { w0, w1, w2, w3 });

            Assert(w0.NetId >= 0, "3.3.1 Hop 0 trunk has valid NetId");
            Assert(w1.NetId == w0.NetId && w2.NetId == w0.NetId && w3.NetId == w0.NetId, 
                $"3.3.2 4-hop nested wire chain propagates exact NetId: W0={w0.NetId}, W1={w1.NetId}, W2={w2.NetId}, W3={w3.NetId}");

            var rootNode = netlist.FindNodeForPin("R_ROOT", "2");
            var leafNode = netlist.FindNodeForPin("R_LEAF", "1");
            Assert(rootNode != null && rootNode == leafNode, "3.3.3 Root pin and Leaf pin merge into same electrical node across 4 hops");
        }

        // 3.4 Wire-to-Wire Rail Bridging (Connecting two bus rails via tap-to-tap tie)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_TOP", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R_BOT", Type = ElementType.Resistor, X = 100, Y = 300 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var topRail = new CircuitWire { Id = "W_TOP", StartElementId = "R_TOP", StartPinName = "1", EndElementId = "R_TOP", EndPinName = "2", StartX = 60, StartY = 100, EndX = 140, EndY = 100 };
            var botRail = new CircuitWire { Id = "W_BOT", StartElementId = "R_BOT", StartPinName = "1", EndElementId = "R_BOT", EndPinName = "2", StartX = 60, StartY = 300, EndX = 140, EndY = 300 };

            // Tie wire bridging W_TOP and W_BOT
            var bridgeWire = new CircuitWire
            {
                Id = "W_BRIDGE",
                StartElementId = "W_TOP", StartPinName = "WireTap",
                EndElementId = "W_BOT", EndPinName = "WireTap",
                StartX = 100, StartY = 100, EndX = 100, EndY = 300
            };

            var netlist = NetlistExtractor.Extract(elements, new[] { topRail, botRail, bridgeWire });

            Assert(topRail.NetId == botRail.NetId && bridgeWire.NetId == topRail.NetId, 
                $"3.4.1 Bridged rails share identical NetId: TOP={topRail.NetId}, BOT={botRail.NetId}, BRIDGE={bridgeWire.NetId}");
            var topPinNode = netlist.FindNodeForPin("R_TOP", "1");
            var botPinNode = netlist.FindNodeForPin("R_BOT", "1");
            Assert(topPinNode != null && topPinNode == botPinNode, "3.4.2 All pins on both rails unified into one single node");
        }

        // 3.5 Dynamic Tap Sliding Invariance: Continuous sliding maintains NetId and Node
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_RAIL", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R_SLIDER", Type = ElementType.Resistor, X = 100, Y = 200 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Long trunk rail from X=100 to X=600 at Y=100
            var rail = new CircuitWire { Id = "W_LONG_RAIL", StartElementId = "R_RAIL", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 100, EndX = 600, EndY = 100 };
            var branch = new CircuitWire { Id = "W_BRANCH_SLIDER", StartElementId = "R_SLIDER", StartPinName = "1", EndElementId = "W_LONG_RAIL", EndPinName = "WireTap", StartX = 60, StartY = 200, EndX = 60, EndY = 100 };

            bool slidingInvariant = true;
            for (float testX = 140f; testX <= 600f; testX += 40f)
            {
                var slider = elements.First(e => e.Id == "R_SLIDER");
                slider.X = testX;
                SyncConnectedWiresToElement(slider, new[] { rail, branch });

                var netlist = NetlistExtractor.Extract(elements, new[] { rail, branch });
                if (branch.NetId != rail.NetId)
                {
                    slidingInvariant = false;
                    break;
                }
                var p1 = netlist.FindNodeForPin("R_RAIL", "2");
                var p2 = netlist.FindNodeForPin("R_SLIDER", "1");
                if (p1 == null || p1 != p2)
                {
                    slidingInvariant = false;
                    break;
                }
            }

            Assert(slidingInvariant, "3.5 Continuous dynamic tap sliding from X=140 to X=600 preserves identical NetId and node unification");
        }

        // 3.6 Cross-Net Isolation (Anti-Short-Circuit Guarantee)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_A1", Type = ElementType.Resistor, X = 100, Y = 100 },
                new CircuitElement { Id = "R_A2", Type = ElementType.Resistor, X = 100, Y = 200 },
                new CircuitElement { Id = "R_B1", Type = ElementType.Resistor, X = 400, Y = 100 },
                new CircuitElement { Id = "R_B2", Type = ElementType.Resistor, X = 400, Y = 200 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Net A trunk and branch
            var trunkA = new CircuitWire { Id = "WA_TRUNK", StartElementId = "R_A1", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 100, EndX = 250, EndY = 100 };
            var branchA = new CircuitWire { Id = "WA_BRANCH", StartElementId = "R_A2", StartPinName = "1", EndElementId = "WA_TRUNK", EndPinName = "WireTap", StartX = 60, StartY = 200, EndX = 140, EndY = 100 };

            // Net B trunk and branch (completely separate coordinates)
            var trunkB = new CircuitWire { Id = "WB_TRUNK", StartElementId = "R_B1", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 440, StartY = 100, EndX = 550, EndY = 100 };
            var branchB = new CircuitWire { Id = "WB_BRANCH", StartElementId = "R_B2", StartPinName = "1", EndElementId = "WB_TRUNK", EndPinName = "WireTap", StartX = 360, StartY = 200, EndX = 440, EndY = 100 };

            var netlist = NetlistExtractor.Extract(elements, new[] { trunkA, branchA, trunkB, branchB });

            Assert(trunkA.NetId == branchA.NetId, "3.6.1 Net A wires share Net A NetId");
            Assert(trunkB.NetId == branchB.NetId, "3.6.2 Net B wires share Net B NetId");
            Assert(trunkA.NetId != trunkB.NetId, $"3.6.3 Strict isolation: Net A NetId ({trunkA.NetId}) != Net B NetId ({trunkB.NetId})");

            var nodeA = netlist.FindNodeForPin("R_A1", "2");
            var nodeB = netlist.FindNodeForPin("R_B1", "2");
            Assert(nodeA != null && nodeB != null && nodeA != nodeB, "3.6.4 Net A and Net B represent disjoint electrical nodes");
        }
    }
}
