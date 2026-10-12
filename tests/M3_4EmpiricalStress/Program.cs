using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace M3_4EmpiricalStress;

public class Program
{
    private static int totalTests = 0;
    private static int passedTests = 0;
    private static int failedTests = 0;

    public static void Main()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("CHALLENGER M3-4 EMPIRICAL ADVERSARIAL STRESS HARNESS");
        Console.WriteLine("Topological Connectivity, Bus Rail Invariance & NetlistExtractor Invariants");
        Console.WriteLine("================================================================================");

        Test1_SequentialDragAndRestoreOfBusRailComponents();
        Test2_NetlistExtractor80pxDisplacementInvariants();
        Test3_CanvasRubberBandingExactMirrorTest();

        Console.WriteLine("================================================================================");
        Console.WriteLine($"CHALLENGER M3-4 SUMMARY: Total={totalTests}, Passed={passedTests}, Failed={failedTests}");
        Console.WriteLine("================================================================================");
    }

    private static void Assert(bool condition, string testName, string failDetail = "")
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
            Console.WriteLine($"  [FAIL] {testName}");
            if (!string.IsNullOrEmpty(failDetail))
            {
                Console.WriteLine($"         Detail: {failDetail}");
            }
        }
    }

    /// <summary>
    /// Exact implementation from CircuitCanvas.xaml.cs & AnalysisStudioViewModel.cs
    /// including Worker M3-3's modifications.
    /// </summary>
    public static void CanvasSyncConnectedWiresToElement(CircuitElement el, IList<CircuitWire> wires)
    {
        var movedWireIds = new HashSet<string>();

        // Phase 1: Update direct pin-connected wires
        foreach (var w in wires)
        {
            if (w.StartElementId == el.Id && w.StartPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.StartX = pPos.X;
                    w.StartY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
            if (w.EndElementId == el.Id && w.EndPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.EndX = pPos.X;
                    w.EndY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
        }

        // Phase 2A: Branch wires starting or ending at el and tapping into a parent rail
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
                        w.StartX = pPos.X;
                        w.StartY = pPos.Y;
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
                        w.EndX = pPos.X;
                        w.EndY = pPos.Y;
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
                // Worker M3-3 guard:
                if (w.Id.EndsWith("_RAIL", StringComparison.OrdinalIgnoreCase) ||
                    w.Id.EndsWith("_BUS", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(w.StartElementId) && string.IsNullOrEmpty(w.EndElementId)))
                {
                    continue;
                }

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

    private static void Test1_SequentialDragAndRestoreOfBusRailComponents()
    {
        Console.WriteLine("\n[SECTION 1] Sequential Drag-and-Restore of Rail-Attached Components");

        var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
        var vccRail = tmpl.Wires.First(w => w.Id == "W_VCC_RAIL");
        var gndRail = tmpl.Wires.First(w => w.Id == "W_GND_RAIL");
        var emitBus = tmpl.Wires.First(w => w.Id == "W_EMIT_BUS");

        var vccInit = (vccRail.StartX, vccRail.StartY, vccRail.EndX, vccRail.EndY);
        var gndInit = (gndRail.StartX, gndRail.StartY, gndRail.EndX, gndRail.EndY);
        var emitInit = (emitBus.StartX, emitBus.StartY, emitBus.EndX, emitBus.EndY);

        string[] railAttachedComponents = { "VCC", "vs", "RL", "Re", "Ce", "Rb1", "Rb2", "Rc" };
        bool allRailsIntact = true;
        string detail = "";

        for (int cycle = 1; cycle <= 10; cycle++)
        {
            foreach (var compId in railAttachedComponents)
            {
                var el = tmpl.Elements.First(e => e.Id == compId);
                float origX = el.X;
                float origY = el.Y;

                // Drag +40px X, -30px Y
                el.X += 40f;
                el.Y -= 30f;
                CanvasSyncConnectedWiresToElement(el, tmpl.Wires);

                // Restore
                el.X = origX;
                el.Y = origY;
                CanvasSyncConnectedWiresToElement(el, tmpl.Wires);

                // Check rails
                if (vccRail.StartX != vccInit.StartX || vccRail.EndX != vccInit.EndX ||
                    vccRail.StartY != vccInit.StartY || vccRail.EndY != vccInit.EndY)
                {
                    allRailsIntact = false;
                    detail = $"VCC_RAIL shrunk/moved during cycle {cycle} on {compId}: ({vccRail.StartX},{vccRail.StartY})->({vccRail.EndX},{vccRail.EndY}) vs init ({vccInit.StartX},{vccInit.StartY})->({vccInit.EndX},{vccInit.EndY})";
                    break;
                }
                if (gndRail.StartX != gndInit.StartX || gndRail.EndX != gndInit.EndX ||
                    gndRail.StartY != gndInit.StartY || gndRail.EndY != gndInit.EndY)
                {
                    allRailsIntact = false;
                    detail = $"GND_RAIL shrunk/moved during cycle {cycle} on {compId}: ({gndRail.StartX},{gndRail.StartY})->({gndRail.EndX},{gndRail.EndY})";
                    break;
                }
                if (emitBus.StartX != emitInit.StartX || emitBus.EndX != emitInit.EndX ||
                    emitBus.StartY != emitInit.StartY || emitBus.EndY != emitInit.EndY)
                {
                    allRailsIntact = false;
                    detail = $"EMIT_BUS shrunk/moved during cycle {cycle} on {compId}: ({emitBus.StartX},{emitBus.StartY})->({emitBus.EndX},{emitBus.EndY})";
                    break;
                }
            }
            if (!allRailsIntact) break;
        }

        Assert(allRailsIntact, "1.1 Trunk rails (VCC_RAIL, GND_RAIL, EMIT_BUS) remain 100% invariant under 10 sequential drag-and-restore cycles", detail);

        var netlistPostCycles = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
        Assert(netlistPostCycles.Nodes.Count == 7, "1.2 Canonical 7 nodes preserved after 10 drag-and-restore cycles on all rail components", $"Got {netlistPostCycles.Nodes.Count} nodes");
    }

    private static void Test2_NetlistExtractor80pxDisplacementInvariants()
    {
        Console.WriteLine("\n[SECTION 2] NetlistExtractor Invariants Under 80px Component Displacement");

        // 2.1 RL moved +80px (from X=540 to X=620)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var rl = tmpl.Elements.First(e => e.Id == "RL");
            rl.X += 80f;
            CanvasSyncConnectedWiresToElement(rl, tmpl.Wires);

            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            var rl1Net = netlist.FindNetForPin("RL", "1");
            var vccPosNet = netlist.FindNetForPin("VCC", "pos");
            var outNet = netlist.FindNetForPin("C2", "2");

            bool nodesPreserved = (netlist.Nodes.Count == 7);
            bool noUnwantedMerger = (rl1Net != null && vccPosNet != null && rl1Net != vccPosNet);

            Assert(nodesPreserved, 
                "2.1 RL moved +80px: Preserves canonical 7 electrical nodes", 
                $"Nodes count: {netlist.Nodes.Count} (expected 7). rl1Net={rl1Net?.NetId}, vccPosNet={vccPosNet?.NetId}");
            Assert(noUnwantedMerger, 
                "2.2 RL moved +80px: Zero unwanted merger between Vout (RL.1) and VCC (VCC.pos)",
                $"rl1Net ({rl1Net?.Name}, Id={rl1Net?.NetId}) == vccPosNet ({vccPosNet?.Name}, Id={vccPosNet?.NetId})");
        }

        // 2.3 VCC moved -80px (from X=620 to X=540)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var vcc = tmpl.Elements.First(e => e.Id == "VCC");
            vcc.X -= 80f;
            CanvasSyncConnectedWiresToElement(vcc, tmpl.Wires);

            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            var rl1Net = netlist.FindNetForPin("RL", "1");
            var vccPosNet = netlist.FindNetForPin("VCC", "pos");

            bool nodesPreserved = (netlist.Nodes.Count == 7);
            bool noUnwantedMerger = (rl1Net != null && vccPosNet != null && rl1Net != vccPosNet);

            Assert(nodesPreserved,
                "2.3 VCC moved -80px: Preserves canonical 7 electrical nodes",
                $"Nodes count: {netlist.Nodes.Count} (expected 7)");
            Assert(noUnwantedMerger,
                "2.4 VCC moved -80px: Zero unwanted merger between VCC and Vout",
                $"rl1Net={rl1Net?.NetId}, vccPosNet={vccPosNet?.NetId}");
        }

        // 2.5 Re moved (-80px, +40px) (lands on Ground drop)
        {
            var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
            var re = tmpl.Elements.First(e => e.Id == "Re");
            re.X -= 80f;
            re.Y += 40f;
            CanvasSyncConnectedWiresToElement(re, tmpl.Wires);

            var netlist = NetlistExtractor.Extract(tmpl.Elements, tmpl.Wires);
            var emitNet = netlist.FindNetForPin("Q1", "E");
            var re1Net = netlist.FindNetForPin("Re", "1");
            var gndNet = netlist.GroundNet;

            bool nodesPreserved = (netlist.Nodes.Count == 7);
            bool emitterNotGrounded = (re1Net != null && gndNet != null && re1Net != gndNet);

            Assert(nodesPreserved,
                "2.5 Re moved (-80,+40): Preserves canonical 7 electrical nodes",
                $"Nodes count: {netlist.Nodes.Count} (expected 7)");
            Assert(emitterNotGrounded,
                "2.6 Re moved (-80,+40): Emitter net does not short-circuit to Ground",
                $"re1Net={re1Net?.NetId}, gndNet={gndNet?.NetId}");
        }
    }

    private static void Test3_CanvasRubberBandingExactMirrorTest()
    {
        Console.WriteLine("\n[SECTION 3] Comprehensive Displacement Invariants Across All Non-Ground Components");

        float[] testDeltas = { -80f, -40f, -20f, 20f, 40f, 80f };
        var tmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
        var nonGndElements = tmpl.Elements.Where(e => e.Type != ElementType.Ground).ToList();

        int testedCombinations = 0;
        int preservedCombinations = 0;
        var failureExamples = new List<string>();

        foreach (var el in nonGndElements)
        {
            foreach (var dx in testDeltas)
            {
                foreach (var dy in new float[] { 0f, dx })
                {
                    testedCombinations++;
                    var testTmpl = CircuitTemplateFactory.CreateCommonEmitterTemplate();
                    var testEl = testTmpl.Elements.First(e => e.Id == el.Id);
                    testEl.X += dx;
                    testEl.Y += dy;
                    CanvasSyncConnectedWiresToElement(testEl, testTmpl.Wires);

                    var nlist = NetlistExtractor.Extract(testTmpl.Elements, testTmpl.Wires);
                    if (nlist.Nodes.Count == 7)
                    {
                        preservedCombinations++;
                    }
                    else
                    {
                        if (failureExamples.Count < 5)
                        {
                            failureExamples.Add($"{el.Id} at (dx={dx}, dy={dy}): nodes={nlist.Nodes.Count} (expected 7)");
                        }
                    }
                }
            }
        }

        Console.WriteLine($"Displacement Combinations Tested: {testedCombinations}, Preserved: {preservedCombinations}, Fractured/Collapsed: {testedCombinations - preservedCombinations}");
        Assert(preservedCombinations == testedCombinations,
            $"3.1 100% of displacement vectors preserve canonical 7 nodes ({preservedCombinations}/{testedCombinations})",
            string.Join("; ", failureExamples));
    }
}
