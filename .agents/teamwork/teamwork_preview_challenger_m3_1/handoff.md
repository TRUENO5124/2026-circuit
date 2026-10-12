# Milestone 3 Empirical Challenger Handoff Report: Geometric Consistency, Solder Dot Keep-Out & Manhattan Invariant

**Author**: Challenger M3-1 (`teamwork_preview_challenger_m3_1`)  
**Mission**: Milestone 3 — Adversarially challenge and empirically verify geometric consistency, solder dot keep-out, and template standards (Requirement R3)  
**Date**: 2026-10-12  
**Empirical Verdict**: **REJECT** (Blocking Defect Detected in Dynamic Component Dragging Manhattan Routing)

---

## 1. Observation

### 1.1 Automated Test Suite Verification
Execution of `python tests/verify_templates.py`:
- Command: `python tests/verify_templates.py`
- Result: **775 checks passed, 0 failed** (100% success rate).
- Findings: All static template geometries in `CommonEmitter_Amp`, `RC_Transient`, and `RC_Coupled_Amp` conform to endpoint accuracy, zero wire piercing, terminal keep-out > 5.0px, grid modulus alignment, and single lead per pin.

### 1.2 Empirical Solder Dot Keep-Out Verification
Execution of `tests/test_challenger_m3_1.py` [Suite 1 & 2]:
- Command: `python tests/test_challenger_m3_1.py`
- Solder Dot Count:
  - `CommonEmitter_Amp`: 15 rendered solder dots
  - `RC_Transient`: 2 rendered solder dots
  - `RC_Coupled_Amp`: 10 rendered solder dots
- Keep-Out Clearance:
  - For every rendered solder dot $D$, $\min_{P \in \text{Pins}} \|D - P\| > 5.0\text{px}$.
  - Zero solder dots exist within 5.0px Euclidean distance of any component pin across all three templates.
- True T-Junction Coverage & Pin Terminal Silence:
  - 100% of genuine T-junctions outside 5.0px clearance have rendered 3.8px solder dots.
  - 100% of pin terminals (distance $\le 5.0\text{px}$) have zero solder dots.

### 1.3 Defect Discovery: Wire Slant & Non-Manhattan Angle on Component Dragging
Execution of `tests/test_challenger_m3_1.py` [Suite 3]:
- Total Invariants Tested: 18,473 checks
- Passed Checks: 18,461
- **Failed Checks: 12**
- Exact Failure Log:
  1. `CommonEmitter_Amp [Move vs by (40,0)]: Wire W_VS_C1 segment (120, 280)->(125, 260) is SLANTED / NON-MANHATTAN (dx=5.00, dy=20.00)!`
  2. `CommonEmitter_Amp [Move C1 by (0,20)]: Wire W_VS_C1 segment (80, 285)->(120, 280) is SLANTED / NON-MANHATTAN (dx=40.00, dy=5.00)!`
  3. `CommonEmitter_Amp [Move C1 by (30,20)]: Wire W_VS_C1 segment (80, 285)->(160, 280) is SLANTED / NON-MANHATTAN (dx=80.00, dy=5.00)!`
  4. `CommonEmitter_Amp [Move Rc by (-20,40)]: Wire W_COLL_LINE segment (320, 220)->(340, 225) is SLANTED / NON-MANHATTAN (dx=20.00, dy=5.00)!`
  5. `RC_Transient [Move R1 by (0,40)]: Wire W_VS_R1 segment (120, 245)->(200, 240) is SLANTED / NON-MANHATTAN (dx=80.00, dy=5.00)!`
  6. `RC_Transient [Move R1 by (0,40)]: Wire W_R1_C1 segment (280, 240)->(360, 245) is SLANTED / NON-MANHATTAN (dx=80.00, dy=5.00)!`
  7. `RC_Transient [Move R1 by (-20,40)]: Wire W_VS_R1 segment (120, 245)->(180, 240) is SLANTED / NON-MANHATTAN (dx=60.00, dy=5.00)!`
  8. `RC_Transient [Move R1 by (-20,40)]: Wire W_R1_C1 segment (260, 240)->(360, 245) is SLANTED / NON-MANHATTAN (dx=100.00, dy=5.00)!`
  9. `RC_Coupled_Amp [Move vs by (40,0)]: Wire W_VS_C1 segment (120, 280)->(125, 260) is SLANTED / NON-MANHATTAN (dx=5.00, dy=20.00)!`
  10. `RC_Coupled_Amp [Move C1 by (0,20)]: Wire W_VS_C1 segment (80, 285)->(120, 280) is SLANTED / NON-MANHATTAN (dx=40.00, dy=5.00)!`
  11. `RC_Coupled_Amp [Move C1 by (30,20)]: Wire W_VS_C1 segment (80, 285)->(160, 280) is SLANTED / NON-MANHATTAN (dx=80.00, dy=5.00)!`
  12. `RC_Coupled_Amp [Move Rc by (-20,40)]: Wire W_COLL_LINE segment (320, 220)->(340, 225) is SLANTED / NON-MANHATTAN (dx=20.00, dy=5.00)!`

### 1.4 Direct Code Observations
1. **`apps/desktop-winui/Models/CircuitElement.cs` Line 103**:
   ```csharp
   if (Math.Abs(p1.X - p2.X) > 6 && Math.Abs(p1.Y - p2.Y) > 6)
   ```
   If either $|p_1.X - p_2.X| \le 6$ or $|p_1.Y - p_2.Y| \le 6$, `GetPathPoints()` skips inserting an intermediate 90° corner. It directly appends $p_2$, producing a single segment $[p_1, p_2]$.
2. **`apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` Line 1817-1818, 1828-1829 & `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` Line 879-880, 890-891**:
   ```csharp
   w.StartX = MathF.Round(pPos.X / 10f) * 10f;
   w.StartY = MathF.Round(pPos.Y / 10f) * 10f;
   ```
   Component pins in canonical templates have 35px offsets (e.g. `C1.1` at $160 - 35 = 125$, `vs.pos` at $320 - 35 = 285$). Rounding to 10px forces coordinates to round to $120$ or $280$, introducing a 5px offset against the original pin position or an unmoved connecting pin.

---

## 2. Logic Chain

1. **Static Robustness**: `verify_templates.py` confirms that in their initial resting state, all educational templates adhere strictly to GB/T 4728 and IEC 60617 rules: 0 wire pierces, 0 pin keep-out violations, and bus rails aligned.
2. **Solder Dot Precision**: Direct2D solder dot extraction in `CircuitCanvas.xaml.cs` correctly checks continuous Euclidean distance $> 5.0\text{px}$ against all pin coordinates. Across all 3 templates, all 27 solder dots sit strictly outside the keep-out perimeter, and all 0-distance pin terminals are completely silent (0 dots).
3. **The Dynamic Dragging Bug Mechanism**:
   - Step A: When a user drags an element (e.g., `vs` moved rightward by 40px from $X=80$ to $X=120$), `SyncConnectedWiresToElement` updates `w.StartX` to $\text{Round}(120 / 10) \times 10 = 120$, and `w.StartY` to $\text{Round}(285 / 10) \times 10 = 280$.
   - Step B: The other end of wire `W_VS_C1` connects to `C1.1`, which remains stationary at $(125, 260)$.
   - Step C: Wire endpoints become $p_1 = (120, 280)$ and $p_2 = (125, 260)$.
   - Step D: `CircuitWire.GetPathPoints()` computes $|p_1.X - p_2.X| = |120 - 125| = 5$.
   - Step E: The test `Math.Abs(p1.X - p2.X) > 6` evaluates to `false` ($5 \not> 6$).
   - Step F: `GetPathPoints()` does NOT generate a Manhattan corner. It returns $[(120, 280), (125, 260)]$.
   - Step G: Direct2D draws a diagonal line with $\Delta X = 5, \Delta Y = -20$ (slope $=-4.0$).
   - Step H: This directly violates Requirement R3 ("拐角支持鼠标直接拖拽微调走线形态，平滑正交吸附；元件拖拽时，关联导线保持拓扑自适应跟随更新") and Acceptance Criteria ("无非正交走线 / 严格保持 Manhattan 走线").
4. **Remediation Feasibility**:
   Simulating the fix by lowering the corner insertion threshold from `> 6` to `> 1.0f` and aligning pin update modulus to 5px (or using exact `pPos`) completely eliminates all 12 wire slant failures (0 failures, 100% pass across all 18,473 checks).

---

## 3. Caveats

1. **Static Templates Untouched**: The bug is NOT present when loading templates in their initial default layout; it only manifests dynamically during pointer/mouse drag gestures when components are repositioned.
2. **Review-Only Constraint**: As an adversarial challenger with review-only permissions, the defect was reproduced and proven via empirical harness (`tests/test_challenger_m3_1.py`) but not modified in production source code (`apps/desktop-winui/**`). Remediation must be executed by a worker agent.

---

## 4. Conclusion

**Verdict: REJECT**

While Worker M3-2 successfully implemented static EDA solder dot rendering, continuous Euclidean keep-out (> 5.0px), and bus trunk rails, Milestone 3 **CANNOT BE APPROVED** in its current state due to a confirmed, reproducible regression:
- Moving components dynamically on the canvas breaks Manhattan orthogonality and generates slanted wires in 12 verifiable scenarios.

### Recommended Action Plan for Worker:
1. In `apps/desktop-winui/Models/CircuitElement.cs` (line 103) and `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (line 511):
   Change:
   ```csharp
   if (Math.Abs(p1.X - p2.X) > 6 && Math.Abs(p1.Y - p2.Y) > 6)
   ```
   To:
   ```csharp
   if (Math.Abs(p1.X - p2.X) > 1.0f && Math.Abs(p1.Y - p2.Y) > 1.0f)
   ```
2. In `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 879-880, 890-891, 910-911, 928-929) and `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (lines 1817-1818, 1828-1829, 1848-1849, 1866-1867):
   Change 10px rounding on component pins:
   ```csharp
   w.StartX = MathF.Round(pPos.X / 5f) * 5f;
   w.StartY = MathF.Round(pPos.Y / 5f) * 5f;
   ```
   or directly assign `pPos.X` and `pPos.Y` (as element position is already 10px/20px grid-snapped and pin offsets are CAD-fixed multiples of 5).
3. Re-run `python tests/test_challenger_m3_1.py` and verify all 18,473 checks pass.

---

## 5. Verification Method

To independently reproduce the empirical findings and verify this rejection report:

```powershell
# 1. Verify that static template verification passes
python tests/verify_templates.py

# 2. Execute Challenger M3-1 empirical stress test harness to reproduce the 12 wire slant failures
python tests/test_challenger_m3_1.py

# Expected Output:
# Total Checks Executed: 18473
# Passed Checks:        18461
# Failed Checks:        12
# VERDICT: REJECT
```
