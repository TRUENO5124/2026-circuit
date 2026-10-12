# Milestone 3 Empirical Challenger Handoff Report: Topological Connectivity, Rubber-Banding Displacement & NetlistExtractor Invariants

**Author**: Challenger M3-2 (`teamwork_preview_challenger_m3_2`)  
**Mission**: Milestone 3 — Adversarially challenge and empirically verify topological connectivity, rubber-banding, NetlistExtractor robustness, and C# runtime models (Requirement R3)  
**Date**: 2026-10-12  
**Empirical Verdict**: **REJECT** (Vulnerabilities Confirmed in 80px Grid Modulus Pin Collision, Plastic Bus Rail Distortion Hysteresis, and Dragging Wire Slants)

---

## 1. Observation

### 1.1 Automated Existing Test Suite Verification
All baseline and worker test suites were executed independently in clean processes:
1. `python tests/adversarial_template_tests.py`:
   - Command: `python tests/adversarial_template_tests.py`
   - Result: **318/318 invariants passed (0 failed)**.
   - Verified: 100 random Monte Carlo template switching transitions, graph connectivity via Union-Find, bus/trunk single lead per pin, and static rail distortion checks.
2. `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj`:
   - Command: `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj`
   - Result: **131/131 tests passed (0 failed)** across Suites 1 through 7.
   - Verified: Canonical netlist extraction, ground reference resolution, RC transient BJT metric suppression, and static template bus architecture.
3. `python tests/verify_templates.py`:
   - Command: `python tests/verify_templates.py`
   - Result: **775/775 checks passed (0 failed)**.
4. Solution Compilation:
   - Command: `dotnet build apps/desktop-winui/CircuitAssistant.csproj /p:NoWarn=MVVMTK0045`
   - Result: **0 errors, 0 warnings**.
5. Engine & API Pytest:
   - Command: `pytest services/engine/tests services/api/tests`
   - Result: **33/33 passed**.
6. Web Check:
   - Command: `npm run check`
   - Result: **100% passed**.

---

### 1.2 Adversarial Stress Test Suite Design & Execution
A dedicated C# and Python adversarial stress test harness was implemented:
- C# Project: `tests/M3AdversarialStress/M3AdversarialStress.csproj` & `tests/M3AdversarialStress/Program.cs`
- Python Integration Harness: `tests/test_adversarial_m3_stress.py`
- Command: `python tests/test_adversarial_m3_stress.py`

#### A. Invariants Fully Verified & Robust (Passed)
1. **Dynamic Displacement at Non-Colliding Modulus ($\pm 20\text{px}, \pm 40\text{px}$)**:
   - `CommonEmitter_Amp` (12 elements, 23 wires): Tested all non-ground components (`vs, C1, Rb1, Rb2, Q1, Rc, Re, Ce, C2, RL, VCC`) across all horizontal, vertical, and diagonal displacements ($\pm 20\text{px}, \pm 40\text{px}$). 
     - Result: **100% pass (176 displacement steps)**. Canonical 7 nodes preserved, 0 fractured nets, Ground Net 0 invariant.
   - `RC_Transient` (4 elements, 5 wires): Tested all components across all $\pm 20\text{px}, \pm 40\text{px}, \pm 80\text{px}$ displacements.
     - Result: **100% pass**. Canonical 3 nodes preserved, zero fictitious BJT metrics.
   - `RC_Coupled_Amp` (9 elements, 16 wires): Tested all components across $\pm 20\text{px}, \pm 40\text{px}$.
     - Result: **100% pass**. Canonical 6 nodes preserved.
2. **Compound Multi-Component Displacement (Suite 2)**:
   - Parallel emitter branch sliding (`Re` and `Ce` moved cohesively by $+20\text{px}, +40\text{px}$): **PASS**.
   - Counter-directional bias displacement (`Rb1` $+40\text{px}$, `Rb2` $-40\text{px}$): **PASS**. Base node unified.
   - Cascading collector & output displacement (`Rc` $+20\text{px}$, `C2` $+40\text{px}$, `RL` $+40\text{px}$): **PASS**.
   - Monte Carlo 50-round randomized simultaneous displacement at 20px modulus: **PASS (50/50 rounds)**.
3. **Wire Tap NetId Propagation Invariants (Suite 3 - 12/12 Passed)**:
   - *Test 3.1*: Direct Pin-to-Wire tap propagates parent trunk NetId to child branch wire (`branch.NetId == trunk.NetId == 0`) and unifies connected pins into identical `ElectricalNode`.
   - *Test 3.2*: Reversed Wire-to-Pin tap (`StartPinName = "WireTap"`) shares trunk NetId and unifies terminal pins.
   - *Test 3.3*: Deeply nested 4-hop tap chain ($W_0 \to W_1 \to W_2 \to W_3$) propagates exact NetId across all 4 levels ($W_0.\text{NetId} = W_1.\text{NetId} = W_2.\text{NetId} = W_3.\text{NetId} = 0$) and unifies root pin with leaf pin.
   - *Test 3.4*: Rail-to-rail bridging wire taps unify two independent trunk rails and merge all pins into a single node.
   - *Test 3.5*: Continuous dynamic sliding along trunk bus rail from $X=140$ to $X=600$ preserves identical NetId and node unification at every step.
   - *Test 3.6*: Strict cross-net isolation guarantees zero NetId collision or unintended leakage between Net A and Net B.

---

### 1.3 Vulnerability Discoveries

#### Defect 1: False Electrical Net Merger on 80px Component Displacement (Grid Modulus Collision)
- **File**: `apps/desktop-winui/Engine/NetlistExtractor.cs` (lines 297-308)
- **Mechanism**:
  ```csharp
  // 4. Direct Pin-to-Pin Snapping (abutting components without intermediate wires)
  for (int i = 0; i < allPins.Count; i++) {
      var p1 = allPins[i];
      for (int j = i + 1; j < allPins.Count; j++) {
          var p2 = allPins[j];
          if (p1.ElementId != p2.ElementId && Vector2.Distance(p1.Pos, p2.Pos) <= SnapTolerance) {
              ds.Union($"PIN:{p1.ElementId}.{p1.PinId}", $"PIN:{p2.ElementId}.{p2.PinId}");
          }
      }
  }
  ```
- **Empirical Failure Evidence**:
  In educational templates (`CommonEmitter_Amp.sch`), components sit on an 80px grid modulus:
  - `RL` baseline is at $X=540$, `VCC` baseline is at $X=620$ ($\Delta X = 80\text{px}$).
  - Moving `RL` rightward by $+80\text{px}$ places `RL` at $X=620$, directly overlapping `VCC`. `RL.1` lands at $(620, 240)$, distance to `VCC.pos` $(620, 245)$ is $5\text{px} \le 8.0\text{px}$ (`SnapTolerance`). `NetlistExtractor` fuses `PIN:RL.1` into Net 3 (VCC), collapsing node count from 7 to 6.
  - Moving `VCC` leftward by $-80\text{px}$ places `VCC` at $X=540$, directly overlapping `RL`, collapsing VCC into Vout (7 nodes -> 6 nodes).
  - Moving `Re` by $(-80, +40)$ places `Re` at $(240, 440)$, where `Re.1` lands at $(240, 400)$, which is the exact coordinate of `Rb2.2` (Ground). `Re.1` fuses with Ground, collapsing Emitter into Ground (7 nodes -> 6 nodes).
  - In Monte Carlo multi-element testing with $\pm 40\text{px}$ deltas, relative displacement of $80\text{px}$ between `Q1` $(20, -40)$ and `Rc` $(-40, 40)$ brings `Q1.B` $(305, 220)$ and `Rc.2` $(300, 220)$ within $5.0\text{px} \le 8.0\text{px}$, causing direct Base-to-Collector short-circuit.

#### Defect 2: Mutual Wire Tap Circularity and Plastic Bus Rail Deformation Hysteresis
- **Files**:
  - `apps/desktop-winui/Services/CircuitTemplateFactory.cs` (lines 228, 240)
  - `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (lines 1884-1894)
  - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 947-957)
- **Mechanism**:
  1. In `CircuitTemplateFactory.cs`, `W_GND_RAIL` specifies:
     `StartElementId = "W_VS_GND", StartPinName = "WireTap", EndElementId = "W_VCC_GND", EndPinName = "WireTap"`.
     Simultaneously, `W_VCC_GND` specifies:
     `EndElementId = "W_GND_RAIL", EndPinName = "WireTap"`.
     This establishes a circular mutual wire tap relationship.
  2. In `SyncConnectedWiresToElement`:
     ```csharp
     if (w.EndPinName == "WireTap" && movedWireIds.Contains(w.EndElementId) && !movedWireIds.Contains(w.Id)) {
         var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
         if (parent != null) {
             var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
             w.EndX = newTap.X;
             w.EndY = newTap.Y;
             movedWireIds.Add(w.Id);
         }
     }
     ```
     When `w` is `W_GND_RAIL` (spanning from $X=80$ to $X=620$), Phase 2B treats it as a child wire of `W_VCC_GND`. It projects `new Vector2(w.StartX, w.StartY) = (80, 480)` onto `W_VCC_GND` (at $X=620$).
  3. If components attached to rails are dragged sequentially without re-initializing the template, `W_GND_RAIL.EndX` and `W_VCC_RAIL.EndX` permanently truncate inward (shrinking from $[80, 620]$ down to $[160, 540]$), which fails to restore upon moving components back. This causes output wire `W_C2_OUT` to collapse into a zero-length point at $(480, 200)$.

#### Defect 3: Wire Slant / Non-Manhattan Diagonal Distortion on Dragging (Corroborated with Challenger M3-1)
- **File**: `apps/desktop-winui/Models/CircuitElement.cs` (line 103)
- **Observation**:
  `GetPathPoints()` skips Manhattan corner generation when `Math.Abs(p1.X - p2.X) <= 6` or `Math.Abs(p1.Y - p2.Y) <= 6`. When dragging components with 35px pin offsets rounded to 10px grid, 5px deltas are produced, yielding 12 reproducible diagonal slanted wire violations across templates.

---

## 2. Logic Chain

1. **Premise 1 (Static Quality)**: `verify_templates.py` (775 checks) and `adversarial_template_tests.py` (318 invariants) confirm that the static templates conform to industrial EDA standards in their initial un-dragged state.
2. **Premise 2 (Displacement at $\pm 20\text{px}, \pm 40\text{px}$)**: For non-colliding single-component displacements, `NetlistExtractor` extracts the exact canonical netlist (7 nodes for CE, 3 for RC, 6 for RC Coupled) with 0 fractured nets. Wire tap NetId propagation is mathematically robust across all 12 topological tests.
3. **Premise 3 (Pin Collision at 80px Modulus)**: Because templates use an 80px modulus and `NetlistExtractor` enforces an 8.0px pin-to-pin abutment snapping radius without verifying intent, moving components by $\pm 80\text{px}$ along neighbor axes places pins within 5.0px of each other, triggering automatic union and collapsing distinct electrical nets into short circuits.
4. **Premise 4 (Hysteresis Rail Shrinkage)**: Circular wire tap definitions in `CircuitTemplateFactory` cause trunk rails to be treated as child wires in Phase 2B of `SyncConnectedWiresToElement`. Projecting `w.StartX` instead of `w.EndX` permanently shrinks trunk bus rails during sequential dragging, corrupting circuit topology.
5. **Deduction**: Therefore, while static templates and wire tap NetId propagation are sound, dynamic dragging of components on the canvas contains critical failure modes (unintended net mergers, plastic rail shrinkage, and non-Manhattan wire slants).
6. **Conclusion**: Milestone 3 fails the adversarial challenge for dynamic dragging stability and must be marked **REJECT**.

---

## 3. Caveats

1. **Scope of Fault**: The bugs do not affect static template loading or standard non-colliding displacements ($\pm 20\text{px}, \pm 40\text{px}$). They manifest specifically during large-displacement dragging ($\pm 80\text{px}$ grid modulus collision) and repeated multi-component dragging hysteresis.
2. **Review-Only Constraint**: In adherence to the Challenger protocol, no implementation source code in `apps/desktop-winui/**` was modified. All tests and reproductions were conducted in `tests/M3AdversarialStress/` and `tests/test_adversarial_m3_stress.py`.

---

## 4. Conclusion

**Final Empirical Verdict: REJECT**

Milestone 3 fails the adversarial challenge due to:
1. **80px Component Modulus Collision**: Displacing components by $\pm 80\text{px}$ merges distinct electrical nets via `NetlistExtractor`'s 8.0px pin abutment snapping ($V_{out} \to V_{CC}$, $V_{CC} \to V_{out}$, $V_e \to \text{GND}$).
2. **Mutual Wire Tap Rail Shrinkage**: Circular wire tap declarations in `CircuitTemplateFactory.cs` coupled with Phase 2B's `w.StartX` projection in `SyncConnectedWiresToElement` permanently deform trunk bus rails over sequential dragging.
3. **Non-Manhattan Wire Slanting**: Confirmed by Challenger M3-1 across 12 dragging scenarios due to the `> 6` threshold in `GetPathPoints()`.

### Actionable Remediation for Worker:
1. **Remove Circular Wire Taps**: In `CircuitTemplateFactory.cs`, define `W_GND_RAIL` and `W_VCC_RAIL` as root trunk wires with independent coordinates (`StartPinName = "None"`, `EndPinName = "None"`), rather than tapping into their own feeder branches.
2. **Fix Phase 2B Projection**: In `CircuitCanvas.xaml.cs` and `AnalysisStudioViewModel.cs`, never allow bus trunk rails to be updated as child branches of feeder lines, and ensure `newTap` projects from the tapped endpoint, not the far end.
3. **Guard Pin Abutment Snapping**: In `NetlistExtractor.cs`, restrict direct pin-to-pin snapping so it only applies to components that do not already have attached wires, preventing accidental shorts during dragging.
4. **Fix Manhattan Threshold**: Change `Math.Abs(...) > 6` to `Math.Abs(...) > 1.0f` in `CircuitElement.cs` line 103 to prevent slanted wires.

---

## 5. Verification Method

To independently verify all findings and reproduce the adversarial challenge:

```powershell
# 1. Run integrated adversarial challenge suite (reproduces all 3 suites)
python tests/test_adversarial_m3_stress.py

# 2. Run C# adversarial stress runner directly
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj

# 3. Verify baseline invariants
python tests/adversarial_template_tests.py
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
python tests/verify_templates.py

# 4. Invalidation condition:
# The verdict will be updated from REJECT to APPROVE when:
# (a) 80px displacement collision does not accidentally merge distinct wired nets.
# (b) Sequential drag-and-restore of VCC or vs leaves W_GND_RAIL and W_VCC_RAIL completely undistorted.
# (c) All wire segments remain strictly orthogonal with zero diagonal slants during dragging.
```
