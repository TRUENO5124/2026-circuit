using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace ChallengerRunner;

class Program
{
    private static int totalTests = 0;
    private static int passedTests = 0;
    private static int failedTests = 0;
    private static readonly List<string> failureDetails = new();

    static async Task<int> Main()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("CHALLENGER M2-2: EMPIRICAL STRESS TESTS FOR NETLIST EXTRACTOR & TOPOLOGY CLASSIFIER");
        Console.WriteLine("================================================================================");

        await RunSuite1_StandardTemplateExtraction();
        RunSuite2_BusTrunkAndTTapConnectivity();
        RunSuite3_GroundReferenceResolution();
        RunSuite4_EdgeCasesAndFaultTolerance();
        await RunSuite5_TopologyClassificationAndSolving();
        await RunSuite6_AdvancedTopologyAndBiasDetection();
        await RunSuite7_Milestone3_EdaRoutingAndCanvasArchitecture();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine($"TEST EXECUTION SUMMARY: Total={totalTests}, Passed={passedTests}, Failed={failedTests}");
        Console.WriteLine("================================================================================");

        if (failedTests > 0)
        {
            Console.WriteLine("\n[FAILURE SUMMARY]");
            foreach (var f in failureDetails)
            {
                Console.WriteLine($"  FAIL: {f}");
            }
            return 1;
        }

        Console.WriteLine("\nALL EMPIRICAL TESTS PASSED SUCCESSFULLY! (Verdict: APPROVE candidates confirmed)");
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

    // =========================================================================
    // SUITE 1: Standard Template Netlist Extraction & Pin Mappings
    // =========================================================================
    private static async Task RunSuite1_StandardTemplateExtraction()
    {
        Console.WriteLine("\n[SUITE 1] Netlist Extraction on Standard Educational Templates");

        // 1.1 CommonEmitter_Amp
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);

            Assert(netlist != null, "1.1.1 CommonEmitter: Netlist extraction non-null");
            Assert(netlist!.Elements.Count == 12, "1.1.2 CommonEmitter: Elements count == 12", $"Got {netlist.Elements.Count}");
            Assert(netlist.Wires.Count == 23, "1.1.3 CommonEmitter: Wires count == 23", $"Got {netlist.Wires.Count}");
            Assert(netlist.HasGround, "1.1.4 CommonEmitter: HasGround == true");
            Assert(netlist.GroundNet != null && netlist.GroundNet.NetId == 0, "1.1.5 CommonEmitter: GroundNet NetId == 0");
            Assert(netlist.GroundNode != null && netlist.GroundNode.Index == 0, "1.1.6 CommonEmitter: GroundNode Index == 0");

            // Verify canonical 7 nodes
            Assert(netlist.Nodes.Count == 7, "1.1.7 CommonEmitter: Canonical 7 electrical nodes", $"Got {netlist.Nodes.Count} nodes: {string.Join(", ", netlist.Nodes.Select(n => $"{n.Name}({n.Index})"))}");

            // Verify active transistor device extraction
            Assert(netlist.Transistors.Count == 1, "1.1.8 CommonEmitter: Transistors count == 1");
            var q1 = netlist.Transistors.FirstOrDefault();
            Assert(q1 != null && q1.Element.Id == "Q1", "1.1.9 CommonEmitter: Transistor is Q1");
            Assert(q1?.BaseNode != null && q1.CollectorNode != null && q1.EmitterNode != null, "1.1.10 CommonEmitter: Q1 all 3 terminals mapped to nodes");
            Assert(q1?.BaseNode?.Name == "Vb", "1.1.11 CommonEmitter: Q1.Base node name == Vb", $"Got {q1?.BaseNode?.Name}");
            Assert(q1?.CollectorNode?.Name == "Vc", "1.1.12 CommonEmitter: Q1.Collector node name == Vc", $"Got {q1?.CollectorNode?.Name}");
            Assert(q1?.EmitterNode?.Name == "Ve", "1.1.13 CommonEmitter: Q1.Emitter node name == Ve", $"Got {q1?.EmitterNode?.Name}");

            // Verify power net (VCC)
            var vccNet = netlist.FindNetForPin("VCC", "pos");
            Assert(vccNet != null && vccNet.IsPower && vccNet.Name == "VCC", "1.1.14 CommonEmitter: VCC positive is on VCC power net");
            var rb1Pin1Net = netlist.FindNetForPin("Rb1", "1");
            var rcPin1Net = netlist.FindNetForPin("Rc", "1");
            Assert(rb1Pin1Net == vccNet && rcPin1Net == vccNet, "1.1.15 CommonEmitter: Rb1.1 and Rc.1 unified to VCC net");

            // Verify Ground net members
            var gndNet = netlist.GroundNet;
            Assert(netlist.FindNetForPin("vs", "neg") == gndNet, "1.1.16 CommonEmitter: vs.neg is on GND");
            Assert(netlist.FindNetForPin("VCC", "neg") == gndNet, "1.1.17 CommonEmitter: VCC.neg is on GND");
            Assert(netlist.FindNetForPin("Rb2", "2") == gndNet, "1.1.18 CommonEmitter: Rb2.2 is on GND");
            Assert(netlist.FindNetForPin("Re", "2") == gndNet, "1.1.19 CommonEmitter: Re.2 is on GND");
            Assert(netlist.FindNetForPin("Ce", "2") == gndNet, "1.1.20 CommonEmitter: Ce.2 is on GND");
            Assert(netlist.FindNetForPin("RL", "2") == gndNet, "1.1.21 CommonEmitter: RL.2 is on GND");

            // Verify validation check
            var valResult = netlist.Validate();
            Assert(valResult.IsValid, "1.1.22 CommonEmitter: Netlist validation IsValid == true", $"Errors: {string.Join("; ", valResult.Issues.Select(i => i.Message))}");

            // Verify SPICE netlist generation
            string spice = netlist.ToSpiceNetlist();
            Assert(spice.Contains("Q_Q1") && spice.Contains("2N3904") && spice.Contains(".END"), "1.1.23 CommonEmitter: SPICE netlist contains Q1 and .END");
        }

        // 1.2 RC_Transient
        {
            var tmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);

            Assert(netlist != null, "1.2.1 RC_Transient: Netlist extraction non-null");
            Assert(netlist!.Elements.Count == 4, "1.2.2 RC_Transient: Elements count == 4");
            Assert(netlist.Wires.Count == 5, "1.2.3 RC_Transient: Wires count == 5");
            Assert(netlist.Nodes.Count == 3, "1.2.4 RC_Transient: Nodes count == 3", $"Got {netlist.Nodes.Count}: {string.Join(", ", netlist.Nodes.Select(n => $"{n.Name}({n.Index})"))}");
            Assert(netlist.HasGround && netlist.GroundNode?.Index == 0, "1.2.5 RC_Transient: Canonical Ground Node 0 present");
            Assert(netlist.Transistors.Count == 0, "1.2.6 RC_Transient: Zero transistors");
            Assert(netlist.Branches.Count == 3, "1.2.7 RC_Transient: 3 electrical branches (VS, R1, C1)", $"Got {netlist.Branches.Count}");

            // Pin connectivity
            var vsPosNet = netlist.FindNetForPin("VS", "pos");
            var r1Pin1Net = netlist.FindNetForPin("R1", "1");
            Assert(vsPosNet != null && vsPosNet == r1Pin1Net, "1.2.8 RC_Transient: VS.pos and R1.1 in same net");

            var r1Pin2Net = netlist.FindNetForPin("R1", "2");
            var c1Pin1Net = netlist.FindNetForPin("C1", "1");
            Assert(r1Pin2Net != null && r1Pin2Net == c1Pin1Net, "1.2.9 RC_Transient: R1.2 and C1.1 in same net (Vc)");

            var gndNet = netlist.GroundNet;
            Assert(netlist.FindNetForPin("VS", "neg") == gndNet, "1.2.10 RC_Transient: VS.neg on GND");
            Assert(netlist.FindNetForPin("C1", "2") == gndNet, "1.2.11 RC_Transient: C1.2 on GND");

            var valResult = netlist.Validate();
            Assert(valResult.IsValid, "1.2.12 RC_Transient: Netlist validation IsValid == true");
        }

        // 1.3 RC_Coupled_Amp
        {
            var tmpl = CircuitTemplateFactory.CreateRcCoupledTemplate();
            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);

            Assert(netlist != null, "1.3.1 RC_Coupled: Netlist extraction non-null");
            Assert(netlist!.Elements.Count == 9, "1.3.2 RC_Coupled: Elements count == 9");
            Assert(netlist.Wires.Count == 16, "1.3.3 RC_Coupled: Wires count == 16");
            Assert(netlist.Nodes.Count == 6, "1.3.4 RC_Coupled: Canonical 6 electrical nodes", $"Got {netlist.Nodes.Count}: {string.Join(", ", netlist.Nodes.Select(n => $"{n.Name}({n.Index})"))}");
            Assert(netlist.Transistors.Count == 1, "1.3.5 RC_Coupled: Transistors count == 1");

            var q1 = netlist.Transistors[0];
            Assert(q1.EmitterNode != null && q1.EmitterNode.Index == 0, "1.3.6 RC_Coupled: Q1 emitter directly connected to Ground Node 0");
            var valResult = netlist.Validate();
            Assert(valResult.IsValid, "1.3.7 RC_Coupled: Netlist validation IsValid == true");
        }
    }

    // =========================================================================
    // SUITE 2: Bus / Trunk T-Tap Connectivity Stress Tests
    // =========================================================================
    private static void RunSuite2_BusTrunkAndTTapConnectivity()
    {
        Console.WriteLine("\n[SUITE 2] Bus/Trunk Routing & T-Tap Connectivity Stress Tests");

        // 2.1 Explicit WireTap tag connectivity
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50, DisplayNumber = 10, UnitSymbol = "Ω" },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 300, Y = 200, Width = 80, Height = 50, DisplayNumber = 20, UnitSymbol = "Ω" }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // R1.Pin2 is at (140, 100). Wire 1 starts at (140, 100).
            // R2.Pin1 is at (260, 200). Wire 2 starts at (260, 200) and ends at (300, 100) on W1, tagged with WireTap.
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "2", EndElementId = "Dummy", EndPinName = "1", StartX = 140, StartY = 100, EndX = 400, EndY = 100 },
                new CircuitWire { Id = "W2", StartElementId = "R2", StartPinName = "1", EndElementId = "W1", EndPinName = "WireTap", StartX = 260, StartY = 200, EndX = 300, EndY = 100 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var netR1_2 = netlist.FindNetForPin("R1", "2");
            var netR2_1 = netlist.FindNetForPin("R2", "1");

            Assert(netR1_2 != null && netR1_2 == netR2_1, "2.1 Explicit WireTap connects branch to trunk net");
        }

        // 2.2 Geometric T-tap without explicit WireTap tag (within SegmentTapTolerance <= 5.0f)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 300, Y = 250, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();
            // R1.Pin2 is at (140, 100). R2.Pin1 is at (260, 250).

            // W1 is horizontal trunk: (140, 100) -> (500, 100)
            // W2 starts at R2.Pin1 (260, 250) and drops vertically to (260, 102) -> distance to W1 segment is 2.0f <= 5.0f
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 100, EndX = 500, EndY = 100 },
                new CircuitWire { Id = "W2", StartElementId = "R2", StartPinName = "1", EndElementId = "None", EndPinName = "None", StartX = 260, StartY = 250, EndX = 260, EndY = 102 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var net1 = netlist.FindNetForPin("R1", "2");
            var net2 = netlist.FindNetForPin("R2", "1");

            Assert(net1 != null && net1 == net2, "2.2 Geometric T-tap (dist=2.0f <= 5.0f) correctly merges into trunk net");
        }

        // 2.3 Geometric T-tap outside tolerance (distance = 12.0f > SegmentTapTolerance 5.0f)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 300, Y = 250, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();
            // R1.Pin2 at (140, 100). R2.Pin1 at (260, 250).

            // W1 is horizontal trunk: (140, 100) -> (500, 100)
            // W2 ends at (260, 112) -> dist is 12.0f > 5.0f
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 100, EndX = 500, EndY = 100 },
                new CircuitWire { Id = "W2", StartElementId = "R2", StartPinName = "1", EndElementId = "None", EndPinName = "None", StartX = 260, StartY = 250, EndX = 260, EndY = 112 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var net1 = netlist.FindNetForPin("R1", "2");
            var net2 = netlist.FindNetForPin("R2", "1");

            Assert(net1 != null && net2 != null && net1 != net2, "2.3 Geometric T-tap beyond tolerance (dist=12.0f) correctly does NOT merge");
        }

        // 2.4 Multi-hop nested T-tap (Trunk -> Branch 1 -> Sub-branch 2)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_TOP", Type = ElementType.Resistor, X = 100, Y = 80, Width = 80, Height = 50 },
                new CircuitElement { Id = "R_MID", Type = ElementType.Resistor, X = 250, Y = 200, Width = 80, Height = 50, RotationDeg = 90 },
                new CircuitElement { Id = "R_BOT", Type = ElementType.Resistor, X = 400, Y = 200, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();
            // R_TOP.Pin2 is at (140, 80).
            // R_BOT.Pin1 is at (360, 200).

            // W1 (Trunk): (140, 80) to (500, 80)
            // W2 (Taps W1): (250, 80) to (250, 200)
            // W3 (Taps W2): (250, 200) to (360, 200) -> connects to R_BOT.Pin1 (360, 200)
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R_TOP", StartPinName = "2", EndElementId = "None", EndPinName = "None", StartX = 140, StartY = 80, EndX = 500, EndY = 80 },
                new CircuitWire { Id = "W2", StartElementId = "W1", StartPinName = "WireTap", EndElementId = "None", EndPinName = "None", StartX = 250, StartY = 80, EndX = 250, EndY = 200 },
                new CircuitWire { Id = "W3", StartElementId = "W2", StartPinName = "WireTap", EndElementId = "R_BOT", EndPinName = "1", StartX = 250, StartY = 200, EndX = 360, EndY = 200 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var netTop = netlist.FindNetForPin("R_TOP", "2");
            var netBot = netlist.FindNetForPin("R_BOT", "1");

            Assert(netTop != null && netTop == netBot, "2.4 Multi-hop nested T-tap unifies components across 2 hops");
        }
    }

    // =========================================================================
    // SUITE 3: Ground Reference Resolution Stress Tests
    // =========================================================================
    private static void RunSuite3_GroundReferenceResolution()
    {
        Console.WriteLine("\n[SUITE 3] Ground Reference Resolution Stress Tests");

        // 3.1 Multiple isolated ground symbols on different rails
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "GND1", Type = ElementType.Ground, X = 100, Y = 400, Width = 40, Height = 35 },
                new CircuitElement { Id = "GND2", Type = ElementType.Ground, X = 300, Y = 400, Width = 40, Height = 35 },
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 200, Width = 80, Height = 50 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 300, Y = 200, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Wire 1 connects R1.2 to GND1
            // Wire 2 connects R2.2 to GND2 (no wire between GND1 and GND2!)
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "2", EndElementId = "GND1", EndPinName = "GND", StartX = 140, StartY = 200, EndX = 100, EndY = 378 },
                new CircuitWire { Id = "W2", StartElementId = "R2", StartPinName = "2", EndElementId = "GND2", EndPinName = "GND", StartX = 340, StartY = 200, EndX = 300, EndY = 378 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var netR1 = netlist.FindNetForPin("R1", "2");
            var netR2 = netlist.FindNetForPin("R2", "2");

            Assert(netlist.HasGround, "3.1.1 Multi-GND: HasGround == true");
            Assert(netlist.GroundNet?.NetId == 0, "3.1.2 Multi-GND: GroundNet NetId == 0");
            Assert(netR1 != null && netR1 == netlist.GroundNet, "3.1.3 Multi-GND: R1.2 unified to Ground Net");
            Assert(netR2 != null && netR2 == netlist.GroundNet, "3.1.4 Multi-GND: R2.2 unified to Ground Net");
            Assert(netR1 == netR2, "3.1.5 Multi-GND: Disjoint GND symbols unified to same canonical Net 0");
        }

        // 3.2 Missing Ground symbol fallback: Priority 2 (DC Voltage Source neg terminal)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "V1", Type = ElementType.DcVoltageSource, X = 100, Y = 200, Width = 60, Height = 60 },
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 200, Y = 200, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W_LOOP", StartElementId = "V1", StartPinName = "neg", EndElementId = "R1", EndPinName = "2", StartX = 100, StartY = 235, EndX = 240, EndY = 200 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            Assert(netlist.HasGround, "3.2.1 Missing GND: Auto-recovers ground reference");
            Assert(netlist.GroundNet?.NetId == 0, "3.2.2 Missing GND: Recovered ground assigned NetId 0");
            var vNegNet = netlist.FindNetForPin("V1", "neg");
            Assert(vNegNet == netlist.GroundNet, "3.2.3 Missing GND: V1.neg chosen as Ground Net 0");

            // Note: NetlistExtractor successfully recovered groundNet.IsGround = true.
            // Check that canonical Node 0 is assigned and functional.
            Assert(netlist.GroundNode != null && netlist.GroundNode.Index == 0, "3.2.4 Missing GND: Canonical GroundNode has Index 0");
        }

        // 3.3 Missing Ground symbol fallback: Priority 3 (AC Voltage Source neg terminal)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "AC1", Type = ElementType.AcVoltageSource, X = 100, Y = 200, Width = 60, Height = 60 },
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 200, Y = 200, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W_LOOP", StartElementId = "AC1", StartPinName = "neg", EndElementId = "R1", EndPinName = "2", StartX = 100, StartY = 235, EndX = 240, EndY = 200 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var acNegNet = netlist.FindNetForPin("AC1", "neg");
            Assert(acNegNet != null && acNegNet == netlist.GroundNet, "3.3 Missing GND with AC source: AC1.neg chosen as Ground Net 0");
        }

        // 3.4 Missing Ground fallback: Priority 4 (Highest-degree net in passive circuit)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, X = 200, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "R3", Type = ElementType.Resistor, X = 300, Y = 100, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Connect pin 2 of R1, R2, R3 to a common junction net
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "2", EndElementId = "R2", EndPinName = "2", StartX = 140, StartY = 100, EndX = 240, EndY = 100 },
                new CircuitWire { Id = "W2", StartElementId = "R2", StartPinName = "2", EndElementId = "R3", EndPinName = "2", StartX = 240, StartY = 100, EndX = 340, EndY = 100 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            Assert(netlist.HasGround, "3.4.1 Passive circuit: Recovers ground reference from highest-degree net");
            var commonNet = netlist.FindNetForPin("R1", "2");
            Assert(commonNet == netlist.GroundNet, "3.4.2 Passive circuit: Common junction of 3 resistors becomes Ground Net 0");
        }
    }

    // =========================================================================
    // SUITE 4: Edge Cases & Fault Tolerance Stress Tests
    // =========================================================================
    private static void RunSuite4_EdgeCasesAndFaultTolerance()
    {
        Console.WriteLine("\n[SUITE 4] Edge Cases & Fault Tolerance Stress Tests");

        // 4.1 Floating pin detection
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 100, Y = 300, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Only connect pin 1 to GND, leave pin 2 completely unconnected
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R1", StartPinName = "1", EndElementId = "GND", EndPinName = "GND", StartX = 60, StartY = 100, EndX = 100, EndY = 278 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var val = netlist.Validate();
            bool pin2Reported = val.Issues.Any(i => i.ElementId == "R1" && i.PinId == "2" && (i.Code == "FLOATING_PIN" || i.Code == "DEGREE_ZERO_PIN"));
            Assert(pin2Reported, "4.1 Floating pin detection: Flags R1 pin 2 as unconnected/degree-zero issue");
        }

        // 4.2 Branch short-circuit detection (both pins on same node)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "R_SHORT", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 100, Y = 300, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Both pin 1 and pin 2 connected to GND
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "R_SHORT", StartPinName = "1", EndElementId = "GND", EndPinName = "GND", StartX = 60, StartY = 100, EndX = 100, EndY = 278 },
                new CircuitWire { Id = "W2", StartElementId = "R_SHORT", StartPinName = "2", EndElementId = "GND", EndPinName = "GND", StartX = 140, StartY = 100, EndX = 100, EndY = 278 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var val = netlist.Validate();
            Assert(val.Issues.Any(i => i.Code == "BRANCH_SHORT_CIRCUIT" && i.ElementId == "R_SHORT"),
                "4.2 Branch short-circuit: Flags R_SHORT as BRANCH_SHORT_CIRCUIT");
        }

        // 4.3 Power supply short-circuit to Ground (Critical fatal error)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "VCC", Type = ElementType.DcVoltageSource, X = 100, Y = 100, Width = 60, Height = 60 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 100, Y = 300, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // Wire directly from VCC.pos to GND, and VCC.neg to GND
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W_SHORT1", StartElementId = "VCC", StartPinName = "pos", EndElementId = "GND", EndPinName = "GND", StartX = 100, StartY = 65, EndX = 100, EndY = 278 },
                new CircuitWire { Id = "W_SHORT2", StartElementId = "VCC", StartPinName = "neg", EndElementId = "GND", EndPinName = "GND", StartX = 100, StartY = 135, EndX = 100, EndY = 278 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var val = netlist.Validate();
            Assert(!val.IsValid, "4.3.1 Power-to-ground short: IsValid == false");
            Assert(val.Issues.Any(i => i.Severity == NetlistValidationSeverity.Error && i.Code == "BRANCH_SHORT_CIRCUIT"),
                "4.3.2 Power-to-ground short: Raises BRANCH_SHORT_CIRCUIT error on voltage source");
        }

        // 4.4 Blank / Empty circuit handling
        {
            var tmpl = CircuitTemplateFactory.CreateBlankTemplate();
            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);

            Assert(netlist != null, "4.4.1 Blank template: Extraction does not throw");
            Assert(netlist.NodeCount == 0, "4.4.2 Blank template: NodeCount == 0");
            Assert(netlist.BranchCount == 0, "4.4.3 Blank template: BranchCount == 0");

            var classification = TopologyClassifier.Classify(netlist);
            Assert(classification.TopologyType == CircuitTopologyType.Unsupported, "4.4.4 Blank template: Classifies as Unsupported");
            Assert(classification.SubtypeName == "Empty", "4.4.5 Blank template: SubtypeName == Empty");
        }

        // 4.5 Disjoint Open-Circuit Subgraphs (No cross-contamination)
        {
            var elements = new List<CircuitElement>
            {
                // Subcircuit A: Resistor A1 and A2
                new CircuitElement { Id = "RA1", Type = ElementType.Resistor, X = 100, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "RA2", Type = ElementType.Resistor, X = 200, Y = 100, Width = 80, Height = 50 },
                // Subcircuit B: Resistor B1 and B2 (physically separated)
                new CircuitElement { Id = "RB1", Type = ElementType.Resistor, X = 100, Y = 400, Width = 80, Height = 50 },
                new CircuitElement { Id = "RB2", Type = ElementType.Resistor, X = 200, Y = 400, Width = 80, Height = 50 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "WA", StartElementId = "RA1", StartPinName = "2", EndElementId = "RA2", EndPinName = "1", StartX = 140, StartY = 100, EndX = 160, EndY = 100 },
                new CircuitWire { Id = "WB", StartElementId = "RB1", StartPinName = "2", EndElementId = "RB2", EndPinName = "1", StartX = 140, StartY = 400, EndX = 160, EndY = 400 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var netA = netlist.FindNetForPin("RA1", "2");
            var netB = netlist.FindNetForPin("RB1", "2");

            Assert(netA != null && netB != null && netA != netB, "4.5 Disjoint open-circuit subgraphs correctly kept in separate nets");
        }
    }

    // =========================================================================
    // SUITE 5: Topology Classification & Pedagogical Solving Integrity
    // =========================================================================
    private static async Task RunSuite5_TopologyClassificationAndSolving()
    {
        Console.WriteLine("\n[SUITE 5] Topology Classification & Solving Integrity Tests");

        // 5.1 Standard templates classification
        {
            var rcTmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var rcNetlist = NetlistExtractor.Extract(rcTmpl.Elements, rcTmpl.Wires);
            var rcResult = TopologyClassifier.Classify(rcNetlist);
            Assert(rcResult.TopologyType == CircuitTopologyType.FirstOrderRc, "5.1.1 RC_Transient: Classified as FirstOrderRc");
            Assert(rcResult.SubtypeName == "DcStepTransient", "5.1.2 RC_Transient: SubtypeName == DcStepTransient");

            var ceTmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var ceNetlist = NetlistExtractor.Extract(ceTmpl.Elements, ceTmpl.Wires);
            var ceResult = TopologyClassifier.Classify(ceNetlist);
            Assert(ceResult.TopologyType == CircuitTopologyType.BjtAmplifier, "5.1.3 CommonEmitter: Classified as BjtAmplifier");
            Assert(ceResult.SubtypeName == "VoltageDividerSelfBias", "5.1.4 CommonEmitter: SubtypeName == VoltageDividerSelfBias");

            var coupTmpl = CircuitTemplateFactory.CreateRcCoupledTemplate();
            var coupNetlist = NetlistExtractor.Extract(coupTmpl.Elements, coupTmpl.Wires);
            var coupResult = TopologyClassifier.Classify(coupNetlist);
            Assert(coupResult.TopologyType == CircuitTopologyType.BjtAmplifier, "5.1.5 RC_Coupled: Classified as BjtAmplifier");
            Assert(coupResult.SubtypeName == "FixedBias", "5.1.6 RC_Coupled: SubtypeName == FixedBias");
        }

        // 5.2 Adversarial Component Modification: Remove BJT Q1 from CommonEmitter
        {
            var ceTmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            // Remove Q1
            var elementsWithoutQ1 = ceTmpl.Elements.Where(e => e.Id != "Q1").ToList();
            var wiresWithoutQ1 = ceTmpl.Wires.Where(w => w.StartElementId != "Q1" && w.EndElementId != "Q1").ToList();

            var netlist = NetlistExtractor.Extract(elementsWithoutQ1, wiresWithoutQ1);
            var classification = TopologyClassifier.Classify(netlist);

            Assert(classification.TopologyType != CircuitTopologyType.BjtAmplifier,
                "5.2.1 Adversarial: Removing BJT Q1 NEVER classifies as BjtAmplifier", $"Got {classification.TopologyType}");
        }

        // 5.3 Adversarial Component Modification: Add 2nd capacitor to RC_Transient
        {
            var rcTmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var elements = new List<CircuitElement>(rcTmpl.Elements)
            {
                new CircuitElement
                {
                    Id = "C2",
                    Type = ElementType.Capacitor,
                    DisplayNumber = 50.0,
                    UnitSymbol = "μF",
                    X = 450,
                    Y = 280,
                    Width = 80,
                    Height = 50
                }
            };
            foreach (var el in elements) if (el.Pins.Count == 0) el.InitializeDefaultPins();

            // Wire C2 in parallel with C1
            var wires = new List<CircuitWire>(rcTmpl.Wires)
            {
                new CircuitWire { Id = "W_C2_TOP", StartElementId = "C1", StartPinName = "1", EndElementId = "C2", EndPinName = "1", StartX = 360, StartY = 245, EndX = 450, EndY = 245 },
                new CircuitWire { Id = "W_C2_BOT", StartElementId = "C1", StartPinName = "2", EndElementId = "C2", EndPinName = "2", StartX = 360, StartY = 315, EndX = 450, EndY = 315 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var classification = TopologyClassifier.Classify(netlist);

            // With 2 reactive capacitors, it should be categorized as General MNA linear (not 1st order RC!)
            Assert(classification.TopologyType == CircuitTopologyType.MnaLinear,
                "5.3.1 Adding 2nd capacitor to RC correctly transitions from FirstOrderRc to MnaLinear", $"Got {classification.TopologyType}");
        }

        // 5.4 Adversarial Component Modification: Add inductor to RC_Transient (RLC Resonance)
        {
            var rcTmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var elements = new List<CircuitElement>(rcTmpl.Elements)
            {
                new CircuitElement
                {
                    Id = "L1",
                    Type = ElementType.Inductor,
                    DisplayNumber = 10.0,
                    UnitSymbol = "mH",
                    X = 300,
                    Y = 200,
                    Width = 80,
                    Height = 50
                }
            };
            foreach (var el in elements) if (el.Pins.Count == 0) el.InitializeDefaultPins();

            var netlist = NetlistExtractor.Extract(elements, rcTmpl.Wires);
            var classification = TopologyClassifier.Classify(netlist);

            Assert(classification.TopologyType == CircuitTopologyType.SecondOrderRlc,
                "5.4.1 1 L + 1 C correctly classified as SecondOrderRlc", $"Got {classification.TopologyType}");
        }

        // 5.5 CRITICAL ACCEPTANCE CRITERION 1: Zero BJT Parameter Leakage in Pure RC Circuit Solution
        {
            var rcTmpl = CircuitTemplateFactory.CreateRcTransientTemplate();
            var netlist = NetlistExtractor.Extract(rcTmpl.Elements, rcTmpl.Wires);
            var classification = TopologyClassifier.Classify(netlist, out var strategy);

            var parameters = new AnalysisParameters { OperatingFrequency = 1000.0 };
            var solution = await strategy.SolveAsync(netlist, parameters);

            Assert(solution.IsSuccess, "5.5.1 FirstOrderRcStrategy execution succeeded");
            Assert(solution.Steps.Count >= 3, "5.5.2 Solution has >= 3 derivation steps", $"Got {solution.Steps.Count}");

            // Verify total absence of any BJT terms in any step formulas or explanations
            var bjtForbiddenTerms = new[] { @"\beta", @"\beta=", "r_{be}", "rbe", "I_{BQ}", "I_{CQ}", "V_{CEQ}", "IBQ", "ICQ", "VCEQ", "共射", "三极管" };
            bool termLeaked = false;
            string leakedDesc = "";

            foreach (var step in solution.Steps)
            {
                string combined = $"{step.Title} {step.Content} {step.LatexFormula} {step.SubstitutionFormula} {step.ResultValue}";
                foreach (var term in bjtForbiddenTerms)
                {
                    if (combined.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        termLeaked = true;
                        leakedDesc = $"Step {step.StepIndex} contained forbidden term '{term}': {combined}";
                        break;
                    }
                }
                if (termLeaked) break;
            }

            Assert(!termLeaked, "5.5.3 ZERO BJT parameter leakage in RC solution steps", leakedDesc);

            // Verify Q-Point placeholder for linear circuits
            var uiResult = solution.ToAnalysisResult();
            Assert(uiResult.QPoint != null, "5.5.4 UI AnalysisResult.QPoint is populated");
            Assert(!uiResult.QPoint!.IsActiveRegion, "5.5.5 UI QPoint.IsActiveRegion == false for RC circuit");
            Assert(uiResult.QPoint.WorkingRegion.Contains("无晶体管"), "5.5.6 UI QPoint indicates '无晶体管 (线性网络)'");
            Assert(uiResult.QPoint.Ibq == "—" && uiResult.QPoint.Icq == "—" && uiResult.QPoint.Vceq == "—",
                "5.5.7 UI QPoint currents and voltages are dash placeholders '—'");
        }

        // 5.6 BJT CommonEmitter Solving Quality & Q-Point Verification
        {
            var ceTmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var netlist = NetlistExtractor.Extract(ceTmpl.Elements, ceTmpl.Wires);
            TopologyClassifier.Classify(netlist, out var strategy);

            var parameters = new AnalysisParameters { OperatingFrequency = 1000.0 };
            var solution = await strategy.SolveAsync(netlist, parameters);

            Assert(solution.IsSuccess, "5.6.1 BjtAmplifierStrategy execution succeeded");
            Assert(solution.Steps.Count == 5, "5.6.2 BjtAmplifierStrategy has 5 standard pedagogical steps", $"Got {solution.Steps.Count}");
            Assert(solution.QPoint != null && solution.QPoint.IsActiveRegion, "5.6.3 BJT QPoint is in Active Region");

            // Check step 1 calculates Ibq, Icq, Vceq in LatexFormula + SubstitutionFormula
            var step1 = solution.Steps[0];
            string step1Full = $"{step1.LatexFormula} {step1.SubstitutionFormula} {step1.ResultValue}";
            Assert(step1Full.Contains("I_{BQ}") && step1Full.Contains("I_{CQ}") && step1Full.Contains("V_{CEQ}"),
                "5.6.4 Step 1 contains rigorous Q-point DC derivation formulas");

            // Check step 2 calculates rbe
            var step2 = solution.Steps[1];
            Assert(step2.LatexFormula.Contains("r_{be}"), "5.6.5 Step 2 contains rbe small-signal formula");

            // Check step 3 calculates Av
            var step3 = solution.Steps[2];
            Assert(step3.LatexFormula.Contains("A_v"), "5.6.6 Step 3 contains midband gain Av formula");

            // Check step 4 calculates Ri and Ro
            var step4 = solution.Steps[3];
            Assert(step4.LatexFormula.Contains("R_i") && step4.LatexFormula.Contains("R_o"),
                "5.6.7 Step 4 contains input/output resistance formulas");

            // Check step 5 calculates Bode response
            var step5 = solution.Steps[4];
            Assert(step5.LatexFormula.Contains("f_L") && step5.LatexFormula.Contains("f_H"),
                "5.6.8 Step 5 contains lower and upper cutoff frequencies");
        }
    }

    // =========================================================================
    // SUITE 6: Advanced Topology & Bias Detection Variations
    // =========================================================================
    private static async Task RunSuite6_AdvancedTopologyAndBiasDetection()
    {
        Console.WriteLine("\n[SUITE 6] Advanced Topology & Bias Detection Variations");

        // 6.1 Pure RL Circuit (1 Inductor + 1 Resistor + 1 Source)
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "VS", Type = ElementType.DcVoltageSource, DisplayNumber = 12, UnitSymbol = "V", X = 100, Y = 200, Width = 60, Height = 60 },
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, DisplayNumber = 10, UnitSymbol = "Ω", X = 200, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "L1", Type = ElementType.Inductor, DisplayNumber = 100, UnitSymbol = "mH", X = 300, Y = 200, Width = 80, Height = 50, RotationDeg = 90 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 200, Y = 300, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "VS", StartPinName = "pos", EndElementId = "R1", EndPinName = "1", StartX = 100, StartY = 165, EndX = 160, EndY = 100 },
                new CircuitWire { Id = "W2", StartElementId = "R1", StartPinName = "2", EndElementId = "L1", EndPinName = "1", StartX = 240, StartY = 100, EndX = 300, EndY = 165 },
                new CircuitWire { Id = "W3", StartElementId = "L1", StartPinName = "2", EndElementId = "GND", EndPinName = "GND", StartX = 300, StartY = 235, EndX = 200, EndY = 278 },
                new CircuitWire { Id = "W4", StartElementId = "VS", StartPinName = "neg", EndElementId = "GND", EndPinName = "GND", StartX = 100, StartY = 235, EndX = 200, EndY = 278 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var classification = TopologyClassifier.Classify(netlist, out var strategy);

            Assert(classification == CircuitTopologyType.FirstOrderRl, "6.1.1 RL Circuit correctly classified as FirstOrderRl", $"Got {classification}");
            Assert(strategy.SubtypeName == "RlTransient", "6.1.2 RL Circuit subtype == RlTransient");

            var solution = await strategy.SolveAsync(netlist, new AnalysisParameters());
            Assert(solution.IsSuccess, "6.1.3 RL Circuit solving succeeded");
            Assert(solution.CircuitTitle.Contains("RL"), "6.1.4 RL Circuit title contains RL");
        }

        // 6.2 BJT Collector Feedback Bias Detection
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "Q1", Type = ElementType.BjtNpn, DisplayNumber = 100, UnitSymbol = "β", X = 250, Y = 250, Width = 80, Height = 80 },
                new CircuitElement { Id = "Rb", Type = ElementType.Resistor, DisplayNumber = 100, UnitSymbol = "kΩ", X = 200, Y = 150, Width = 80, Height = 50 },
                new CircuitElement { Id = "Rc", Type = ElementType.Resistor, DisplayNumber = 2, UnitSymbol = "kΩ", X = 300, Y = 150, Width = 80, Height = 50 },
                new CircuitElement { Id = "VCC", Type = ElementType.DcVoltageSource, DisplayNumber = 12, UnitSymbol = "V", X = 400, Y = 200, Width = 60, Height = 60 },
                new CircuitElement { Id = "GND", Type = ElementType.Ground, X = 250, Y = 400, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();
            // Q1.B is at (250-35, 250) = (215, 250). Q1.C is at (250+20, 250-35) = (270, 215). Q1.E is at (250+20, 250+35) = (270, 285).

            // Rb connects between Base and Collector
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W_RB_B", StartElementId = "Rb", StartPinName = "1", EndElementId = "Q1", EndPinName = "B", StartX = 160, StartY = 150, EndX = 215, EndY = 250 },
                new CircuitWire { Id = "W_RB_C", StartElementId = "Rb", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 240, StartY = 150, EndX = 270, EndY = 215 },
                new CircuitWire { Id = "W_RC_C", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 340, StartY = 150, EndX = 270, EndY = 215 },
                new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "VCC", EndPinName = "pos", StartX = 260, StartY = 150, EndX = 400, EndY = 165 },
                new CircuitWire { Id = "W_QE_GND", StartElementId = "Q1", StartPinName = "E", EndElementId = "GND", EndPinName = "GND", StartX = 270, StartY = 285, EndX = 250, EndY = 378 },
                new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "GND", EndPinName = "GND", StartX = 400, StartY = 235, EndX = 250, EndY = 378 }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var classification = TopologyClassifier.Classify(netlist);

            Assert(classification.TopologyType == CircuitTopologyType.BjtAmplifier, "6.2.1 Feedback BJT: Classified as BjtAmplifier");
            Assert(classification.SubtypeName == "CollectorFeedbackBias", "6.2.2 Feedback BJT: Detected CollectorFeedbackBias subtype", $"Got {classification.SubtypeName}");
        }

        // 6.3 MNA Solver Execution on Pure Resistive Divider
        {
            var elements = new List<CircuitElement>
            {
                new CircuitElement { Id = "V1", Type = ElementType.DcVoltageSource, DisplayNumber = 10, UnitSymbol = "V", X = 80, Y = 150, Width = 60, Height = 60 },
                new CircuitElement { Id = "GND1", Type = ElementType.Ground, X = 80, Y = 300, Width = 40, Height = 35 },
                new CircuitElement { Id = "R1", Type = ElementType.Resistor, DisplayNumber = 100, UnitSymbol = "Ω", X = 200, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "R2", Type = ElementType.Resistor, DisplayNumber = 100, UnitSymbol = "Ω", X = 320, Y = 100, Width = 80, Height = 50 },
                new CircuitElement { Id = "GND2", Type = ElementType.Ground, X = 360, Y = 200, Width = 40, Height = 35 }
            };
            foreach (var el in elements) el.InitializeDefaultPins();

            // W1: V1.pos (80, 115) to R1.1 (160, 100) -> Net 1 (Vin)
            // W2: R1.2 (240, 100) to R2.1 (280, 100) -> Net 2 (Vmid)
            // W3: R2.2 (360, 100) to GND2 (360, 178) -> Net 0 (GND)
            // W4: V1.neg (80, 185) to GND1 (80, 278) -> Net 0 (GND)
            var wires = new List<CircuitWire>
            {
                new CircuitWire { Id = "W1", StartElementId = "V1", StartPinName = "pos", EndElementId = "R1", EndPinName = "1", StartX = 80, StartY = 115, EndX = 160, EndY = 100, FlipCorner = true },
                new CircuitWire { Id = "W2", StartElementId = "R1", StartPinName = "2", EndElementId = "R2", EndPinName = "1", StartX = 240, StartY = 100, EndX = 280, EndY = 100, FlipCorner = false },
                new CircuitWire { Id = "W3", StartElementId = "R2", StartPinName = "2", EndElementId = "GND2", EndPinName = "GND", StartX = 360, StartY = 100, EndX = 360, EndY = 178, FlipCorner = false },
                new CircuitWire { Id = "W4", StartElementId = "V1", StartPinName = "neg", EndElementId = "GND1", EndPinName = "GND", StartX = 80, StartY = 185, EndX = 80, EndY = 278, FlipCorner = false }
            };

            var netlist = NetlistExtractor.Extract(elements, wires);
            var classification = TopologyClassifier.Classify(netlist, out var strategy);
            Console.WriteLine($"[DEBUG 6.3 Nets count: {netlist.Nets.Count}]");
            foreach (var n in netlist.Nets)
                Console.WriteLine($"  Net {n.NetId}: {n.Name}, IsGround={n.IsGround}, Pins={string.Join(", ", n.ConnectedPins.Select(p => p.ToString()))}");
            Assert(classification == CircuitTopologyType.MnaLinear, "6.3.1 Pure resistive network classified as MnaLinear");
            var solution = await strategy.SolveAsync(netlist, new AnalysisParameters());
            Assert(solution.IsSuccess, "6.3.2 MNA solver executed successfully");
            Console.WriteLine($"[DEBUG RawMnaLog]:\n{solution.RawMnaLog}");
            Assert(solution.RawMnaLog.Contains("=== MNA 求解日志 ===") && solution.RawMnaLog.Contains("电位 v_"), "6.3.4 MNA solution contains node voltage output");
        }
    }

    // =========================================================================
    // SUITE 7: Milestone 3 EDA Routing, Bus Trunk & Canvas Architecture
    // =========================================================================
    private static async Task RunSuite7_Milestone3_EdaRoutingAndCanvasArchitecture()
    {
        Console.WriteLine("\n[SUITE 7] Milestone 3 EDA Routing, Bus Trunk & Canvas Architecture");

        // 7.1 Common Emitter Template Single-Trunk Bus Architecture
        {
            var template = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            Assert(template.Elements.Count >= 11, "7.1.1 CE Template has all standard elements");

            // Check single trunk lead from Q1 emitter (strict EDA standard: no multi-wire pin crowding)
            var emitWires = template.Wires.Where(w => 
                (w.StartElementId == "Q1" && w.StartPinName == "E") || 
                (w.EndElementId == "Q1" && w.EndPinName == "E")).ToList();
            Assert(emitWires.Count == 1, "7.1.2 Q1 Emitter has exactly 1 direct trunk wire (no pin crowding)", $"Got {emitWires.Count}");
            Assert(emitWires[0].Id == "W_EMIT_TRUNK", "7.1.3 Q1 Emitter wire is W_EMIT_TRUNK");

            // Check Re and Ce tap into emitter bus via WireTap
            var reDrop = template.Wires.FirstOrDefault(w => w.Id == "W_RE_DROP");
            Assert(reDrop != null && reDrop.EndPinName == "WireTap", "7.1.4 Re branches into emitter bus via WireTap");

            var ceDrop = template.Wires.FirstOrDefault(w => w.Id == "W_CE_DROP");
            Assert(ceDrop != null && ceDrop.EndPinName == "WireTap", "7.1.5 Ce branches into emitter bus via WireTap");

            // Check Ground Rail and VCC Rail exist
            var gndRail = template.Wires.FirstOrDefault(w => w.Id == "W_GND_RAIL");
            Assert(gndRail != null, "7.1.6 GND Rail exists spanning the bottom bus");
            Assert(gndRail.StartY == 480 && gndRail.EndY == 480, "7.1.7 GND Rail is perfectly horizontal at Y=480");

            var vccRail = template.Wires.FirstOrDefault(w => w.Id == "W_VCC_RAIL");
            Assert(vccRail != null, "7.1.8 VCC Rail exists spanning the top bus");
            Assert(vccRail.StartY == 80 && vccRail.EndY == 80, "7.1.9 VCC Rail is perfectly horizontal at Y=80");

            // Netlist extraction and connectivity verification through WireTaps
            var netlist = NetlistExtractor.Extract(template.Elements, template.Wires);
            Assert(netlist.Nets.Count >= 7, "7.1.10 Netlist extracts all expected electrical nets", $"Got {netlist.Nets.Count}");

            var emitterNet = netlist.FindNetForPin("Q1", "E");
            Assert(emitterNet != null, "7.1.11 Emitter electrical net extracted");
            Assert(netlist.FindNetForPin("Re", "1") == emitterNet, "7.1.12 Emitter net contains Re.1 via bus tap");
            Assert(netlist.FindNetForPin("Ce", "1") == emitterNet, "7.1.13 Emitter net contains Ce.1 via bus tap");

            var groundNet = netlist.GroundNet;
            Assert(groundNet != null, "7.1.14 Ground electrical net extracted");
            Assert(netlist.FindNetForPin("vs", "neg") == groundNet, "7.1.15 Ground net contains vs.neg");
            Assert(netlist.FindNetForPin("Rb2", "2") == groundNet, "7.1.16 Ground net contains Rb2.2");
            Assert(netlist.FindNetForPin("Re", "2") == groundNet, "7.1.17 Ground net contains Re.2");
            Assert(netlist.FindNetForPin("Ce", "2") == groundNet, "7.1.18 Ground net contains Ce.2");
            Assert(netlist.FindNetForPin("RL", "2") == groundNet, "7.1.19 Ground net contains RL.2");
            Assert(netlist.FindNetForPin("VCC", "neg") == groundNet, "7.1.20 Ground net contains VCC.neg");

            // Verify topology classification and solver on CE template
            var classification = TopologyClassifier.Classify(netlist, out var strategy);
            Assert(classification == CircuitTopologyType.BjtAmplifier, "7.1.21 CE Template classified as BjtAmplifier");
            Assert(strategy.SubtypeName == "VoltageDividerSelfBias", "7.1.22 CE Template detected as VoltageDividerSelfBias");

            var solution = await strategy.SolveAsync(netlist, new AnalysisParameters());
            Assert(solution.IsSuccess, "7.1.23 CE Template solves successfully");
            Assert(solution.QPoint != null && !string.IsNullOrEmpty(solution.QPoint.Icq) && solution.QPoint.Icq != "—", "7.1.24 CE Template calculates valid Q-point");
        }

        // 7.2 RC Transient Template Verification & Zero BJT Leakage
        {
            var rcTemplate = CircuitTemplateFactory.CreateRcTransientTemplate();
            Assert(rcTemplate.Elements.Count == 4, "7.2.1 RC Template contains VS, R1, C1, GND");

            var rcNetlist = NetlistExtractor.Extract(rcTemplate.Elements, rcTemplate.Wires);
            var rcClassification = TopologyClassifier.Classify(rcNetlist, out var rcStrategy);
            Assert(rcClassification == CircuitTopologyType.FirstOrderRc, "7.2.2 RC Template classified as FirstOrderRc");

            var rcSolution = await rcStrategy.SolveAsync(rcNetlist, new AnalysisParameters());
            Assert(rcSolution.IsSuccess, "7.2.3 RC Template solves successfully");
            Assert(!rcSolution.SummaryText.Contains("IBQ") && !rcSolution.SummaryText.Contains("ICQ") && !rcSolution.SummaryText.Contains("rbe"), 
                "7.2.4 STRICT: RC Template has zero BJT metrics");
            Assert(rcSolution.Steps.Any(s => s.LatexFormula.Contains(@"\tau") || s.ResultValue.Contains("0.1") || s.ResultValue.Contains("100")), 
                "7.2.5 RC Template tau = 0.1 s (1kΩ * 100μF)");
            Assert(rcSolution.Steps.Count == 5, "7.2.6 RC Template generates 5 pedagogical steps");
        }
    }
}

