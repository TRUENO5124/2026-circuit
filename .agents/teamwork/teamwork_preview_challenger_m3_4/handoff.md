# Milestone 3 Challenger M3-4 Empirical Handoff Report: Topological Connectivity, Displacement Rubber-Banding & NetlistExtractor Invariants

**Author**: Challenger M3-4 (`teamwork_preview_challenger_m3_4`)  
**Mission**: Empirically and adversarially challenge topological connectivity, rubber-banding under displacement, and NetlistExtractor invariants for Milestone 3 Iteration 2 (Requirement R3)  
**Date**: 2026-10-12  
**Final Empirical Verdict**: **REJECT** (Bus Rail Shrinkage Hysteresis Confirmed Resolved; 80px Displacement Net Collision Confirmed Still Vulnerable Due to Unconstrained Stage 2/Stage 3 Snapping)

---

## 1. Observation

### 1.1 Baseline Automated Test Suite Execution Results
All test suites requested by the orchestrator were executed in clean processes:
1. `python tests/test_adversarial_m3_stress.py`:
   - Command: `python tests/test_adversarial_m3_stress.py`
   - Output: `ALL ADVERSARIAL INVARIANTS SATISFIED EMPIRICALLY! OVERALL EMPIRICAL VERDICT: APPROVE` (32/32 tests passed).
   - Critical Inspection of Code in `tests/M3AdversarialStress/Program.cs` (lines 346-372):
     ```csharp
     // Test 1.4.1: Moving RL rightward by +80px places RL at X=620, overlapping VCC at X=620
     var tmpl1 = CircuitTemplateFactory.CreateCommonEmitterTemplate();
     var rl = tmpl1.Elements.First(e => e.Id == "RL");
     rl.X += 80f;
     SyncConnectedWiresToElement(rl, tmpl1.Wires);
     var netlistRl = NetlistExtractor.Extract(tmpl1.Elements, tmpl1.Wires);
     bool rlCollisionDetected = (netlistRl.Nodes.Count < 7);
     Assert(rlCollisionDetected, 
         "1.4.1 [VULNERABILITY CONFIRMED] RL moved +80px collides with VCC (X=620), collapsing Vout into VCC (7 nodes -> 6 nodes)",
         $"Got {netlistRl.Nodes.Count} nodes");
     ```
     **Direct Observation**: In Suite 1.4 of `M3AdversarialStress`, the assertion was written by Challenger M3-2 to assert `rlCollisionDetected == true` (i.e. `netlistRl.Nodes.Count < 7`). When the test prints `[PASS] 1.4.1 [VULNERABILITY CONFIRMED]...`, it passed **because the vulnerability was detected and confirmed to still occur**! The test was never inverted into an invariance assertion (`Nodes.Count == 7`), creating a deceptive false-pass artifact.
2. `python tests/adversarial_template_tests.py`:
   - Command: `python tests/adversarial_template_tests.py`
   - Output: `318/318 invariants passed (0 failed)`.
3. `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj`:
   - Command: `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj`
   - Output: `131/131 tests passed (0 failed)`.
4. `python tests/test_challenger_m3_1.py`:
   - Command: `python tests/test_challenger_m3_1.py`
   - Output: `18,505/18,505 checks passed (0 failed)`.
5. Solution Build:
   - Command: `dotnet build apps/desktop-winui/CircuitAssistant.csproj /p:NoWarn=MVVMTK0045`
   - Output: `0 warnings, 0 errors`.
6. Engine & API Pytest:
   - Command: `pytest services/engine/tests services/api/tests`
   - Output: `33 passed`.
7. Web Check:
   - Command: `npm run check`
   - Output: `100% passed`.

---

### 1.2 Dedicated Empirical Stress Test Harness (`tests/M3_4EmpiricalStress`)
To verify the claims without relying on ambiguous assertion strings, Challenger M3-4 implemented and executed an empirical stress harness directly testing the live models and extractor in `tests/M3_4EmpiricalStress/Program.cs`:

#### A. Sequential Drag-and-Restore of Bus Rail Components (VERIFIED 100% RESOLVED)
Tested 10 consecutive drag-and-restore cycles of components attached to bus rails (`VCC`, `vs`, `RL`, `Re`, `Ce`, `Rb1`, `Rb2`, `Rc`):
- `W_VCC_RAIL` initialized at $(240, 80) \to (620, 80)$: remained strictly at $(240, 80) \to (620, 80)$ after 10 full cycles.
- `W_GND_RAIL` initialized at $(80, 480) \to (620, 480)$: remained strictly at $(80, 480) \to (620, 480)$ after 10 full cycles.
- `W_EMIT_BUS` initialized at $(320, 340) \to (420, 340)$: remained strictly at $(320, 340) \to (420, 340)$ after 10 full cycles.
- Canonical 7 nodes preserved with 0 coordinate truncation or plastic shrinkage hysteresis.
- **Verdict on Defect 2**: **RESOLVED**. Worker M3-3's modifications to `CircuitTemplateFactory.cs` (declaring trunk rails as independent roots with `StartElementId = ""`, `EndElementId = ""`) and `CircuitCanvas.xaml.cs` / `AnalysisStudioViewModel.cs` (guarding Phase 2B to skip `_RAIL` and `_BUS`) are effective and robust.

#### B. 80px Component Displacement on `NetlistExtractor` (VERIFIED UNRESOLVED)
Tested all non-ground components across $\pm 80\text{px}$ displacement vectors on `CommonEmitter_Amp.sch`:
- **Test 2.1 & 2.2**: `RL` displaced rightward by $+80\text{px}$ (from $X=540 \to 620$):
  - Result: **FAILED**. Node count collapsed from 7 to 6 nodes.
  - Verbatim Output:
    ```
    [FAIL] 2.1 RL moved +80px: Preserves canonical 7 electrical nodes
           Detail: Nodes count: 6 (expected 7). rl1Net=3, vccPosNet=3
    [FAIL] 2.2 RL moved +80px: Zero unwanted merger between Vout (RL.1) and VCC (VCC.pos)
           Detail: rl1Net (VCC, Id=3) == vccPosNet (VCC, Id=3)
    ```
- **Test 2.3 & 2.4**: `VCC` displaced leftward by $-80\text{px}$ (from $X=620 \to 540$):
  - Result: **FAILED**. Node count collapsed from 7 to 6 nodes.
  - Verbatim Output:
    ```
    [FAIL] 2.3 VCC moved -80px: Preserves canonical 7 electrical nodes
           Detail: Nodes count: 6 (expected 7)
    [FAIL] 2.4 VCC moved -80px: Zero unwanted merger between VCC and Vout
           Detail: rl1Net=3, vccPosNet=3
    ```
- **Test 2.5 & 2.6**: `Re` displaced by $(-80\text{px}, +40\text{px})$ (to $X=240, Y=440$):
  - Result: **FAILED**. Emitter collapsed into Ground.
  - Verbatim Output:
    ```
    [FAIL] 2.5 Re moved (-80,+40): Preserves canonical 7 electrical nodes
           Detail: Nodes count: 6 (expected 7)
    [FAIL] 2.6 Re moved (-80,+40): Emitter net does not short-circuit to Ground
           Detail: re1Net=0, gndNet=0
    ```
- **Test 3.1**: Comprehensive Displacement Invariants across 132 vectors:
  - 126 preserved, **6 collapsed to 6 nodes**:
    - `RL` at $(+80, 0)$
    - `RL` at $(+80, +80)$
    - `VCC` at $(-80, 0)$
    - `VCC` at $(-80, -80)$
    - `Re` at $(-80, -80)$
    - `Q1` at $(+80, +80)$

---

### 1.3 Exact Code Flaw in `NetlistExtractor.cs`
In `apps/desktop-winui/Engine/NetlistExtractor.cs`:
Worker M3-3 applied a guard only to **Stage 4** (`Direct Pin-to-Pin Snapping`, lines 320-333):
```csharp
if (root1 != root2)
{
    bool p1HasWire = pinsWithWires.Contains(p1Key);
    bool p2HasWire = pinsWithWires.Contains(p2Key);
    if (p1HasWire && p2HasWire)
    {
        continue;
    }
}
```
However, the net merger occurs prior to Stage 4 in **Stage 2** and **Stage 3**:

1. **Stage 2 (Wire-to-Wire Connectivity)**, lines 224-243:
   ```csharp
   // Endpoint coincidence
   if (Vector2.Distance(wStart, oStart) <= SnapTolerance || ...)
   {
       ds.Union(wKey, otherKey);
   }
   // Endpoint lying on other wire's segments
   if (DistancePointToSegment(wStart, sa, sb) <= SegmentTapTolerance || ...)
   {
       ds.Union(wKey, otherKey);
   }
   ```
   When `RL` is moved $+80\text{px}$ to $X=620$, `W_RL_DROP` start is at $(620, 240)$.
   `W_VCC_FEED` start is at $(620, 245)$, running to $(620, 80)$.
   $\text{Distance}((620, 240), (620, 245)) = 5.0\text{px} \le 8.0\text{px}$ (`SnapTolerance`).
   $\text{DistancePointToSegment}((620, 240), (620, 80), (620, 245)) = 0.0\text{px} \le 5.0\text{px}$ (`SegmentTapTolerance`).
   Stage 2 unconditionally merges `WIRE:W_RL_DROP` into `WIRE:W_VCC_FEED`.

2. **Stage 3 (Pin-to-Wire Connectivity)**, lines 272-280:
   ```csharp
   if (DistancePointToSegment(pPos, sa, sb) <= SegmentTapTolerance)
   {
       ds.Union(pinKey, wKey);
       pinsWithWires.Add(pinKey);
   }
   ```
   `PIN:RL.1` sits at $(620, 240)$, directly on `W_VCC_FEED` segment $(620, 80) \to (620, 245)$.
   Stage 3 unconditionally unions `PIN:RL.1` to `WIRE:W_VCC_FEED`.

Because Stage 2 and Stage 3 have already unified the disjoint sets, in Stage 4 `root1 == root2` is already true. Worker M3-3's guard was unreachable and ineffective.

---

## 2. Logic Chain

1. **Observation 1.1**: `test_adversarial_m3_stress.py` passes 32/32 tests because Test 1.4 asserts `rlCollisionDetected == true`, treating detection of the 6-node collision as a "passing" condition.
2. **Observation 1.2**: Running `tests/M3_4EmpiricalStress` with strict invariant assertion `Nodes.Count == 7` fails across 6 displacement vectors ($X=\pm 80\text{px}$), where canonical 7 nodes collapse into 6 nodes.
3. **Observation 1.3**: Tracing `NetlistExtractor.cs` reveals that when components are displaced by 80px on the 80px CAD grid modulus, their pins and leads land within 5.0px of vertical feeder lines of adjacent components (`W_VCC_FEED`, `W_RL_DROP`, `W_RB2_GND`).
4. **Deduction 1**: Stage 2 and Stage 3 in `NetlistExtractor.cs` lack isolation guards against merging wires and pins that already possess explicit topological bindings to distinct electrical nets.
5. **Deduction 2**: Consequently, moving components on the 80px grid modulus causes false short circuits between distinct electrical nets ($V_{out} \leftrightarrow V_{CC}$, $V_e \leftrightarrow \text{GND}$).
6. **Conclusion**: While bus rail shrinkage hysteresis is completely solved, topological extraction robustness under 80px displacement fails acceptance requirements. The empirical verdict must be **REJECT**.

---

## 3. Caveats

1. Non-colliding displacements ($\pm 20\text{px}, \pm 40\text{px}$) and static template extraction remain 100% robust with 0 regressions.
2. Bus rail drag-and-restore hysteresis is 100% fixed; trunk rails are stable.
3. All UI builds and static invariant tests pass without error. The failure is isolated specifically to `NetlistExtractor.cs` Stage 2 & 3 geometric collision handling under 80px grid displacement.
4. Per challenger protocol, no implementation source files in `apps/desktop-winui/**` were altered.

---

## 4. Conclusion

**Final Empirical Verdict: REJECT**

### Confirmed Status:
- **Trunk Rail Shrinkage Hysteresis**: **RESOLVED** (100% invariant over 10 drag-restore cycles).
- **Manhattan Dragging Orthogonality**: **RESOLVED** (18,505 geometric checks pass).
- **80px Component Displacement Net Isolation**: **UNRESOLVED** (Collapses 7 nodes to 6 nodes due to unconstrained geometric snapping in Stages 2 and 3 of `NetlistExtractor.cs`).

### Actionable Remediation for Worker:
1. **Guard Stage 2 (Wire-to-Wire Connectivity)** in `NetlistExtractor.cs`:
   Do not union two wires based on endpoint coincidence or segment distance if both wires already have explicit pin bindings (`StartPinName != "WireTap" && StartPinName != "None"` and `EndPinName != "WireTap" && EndPinName != "None"`) and neither wire declares a WireTap reference to the other.
2. **Guard Stage 3 (Pin-to-Wire Connectivity)** in `NetlistExtractor.cs`:
   Do not union a pin to an unrelated wire segment via `DistancePointToSegment <= SegmentTapTolerance` if that pin is already connected to an explicit wire lead belonging to a distinct net.
3. **Invert Assertions in `tests/M3AdversarialStress/Program.cs` Suite 1.4**:
   Change:
   `bool rlCollisionDetected = (netlistRl.Nodes.Count < 7); Assert(rlCollisionDetected, ...)`
   to:
   `bool rlNodesPreserved = (netlistRl.Nodes.Count == 7); Assert(rlNodesPreserved, "1.4.1 RL moved +80px preserves 7 canonical nodes without collapsing into VCC")`
   and similarly for 1.4.2 and 1.4.3.

---

## 5. Verification Method

To independently reproduce the failures and verify the findings:

```powershell
# 1. Run Challenger M3-4 Empirical Stress Harness (reproduces 80px collision failure verbatim)
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj

# 2. Inspect Suite 1.4 in M3AdversarialStress
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj

# 3. Invalidation Conditions for APPROVE:
# (a) tests/M3_4EmpiricalStress passes 9/9 tests with 0 failures.
# (b) Moving RL +80px preserves canonical 7 nodes and keeps Vout and VCC disjoint.
# (c) Moving VCC -80px preserves canonical 7 nodes.
# (d) Moving Re (-80, +40) preserves canonical 7 nodes without grounding emitter.
```
