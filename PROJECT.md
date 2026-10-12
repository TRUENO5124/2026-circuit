# Project: CircuitAssistant Refactoring (Industrial-Grade EDA Standard)

## Architecture

```
+---------------------------------------------------------------------------------------+
|                               WinUI 3 Desktop App                                     |
|                                                                                       |
|  +---------------------------+       +---------------------------------------------+  |
|  |       CircuitCanvas       |       |           AnalysisStudioViewModel           |  |
|  |                           |       |                                             |  |
|  | - Win2D Hardware Render   | <---> | - Element & Wire Collections (Observable)   |  |
|  | - Bus/Trunk Routing       |       | - Project & Template State                  |  |
|  | - T-Tap Solder Dots (3.8px)|      | - Deductions & Step ViewModels              |  |
|  | - Interactive Wire Tap    |       +---------------------------------------------+  |
|  | - Rubber-Banding Drag     |                              |                         |
|  +---------------------------+                              v                         |
|                                              +-----------------------------+          |
|                                              |      NetlistExtractor       |          |
|                                              +-----------------------------+          |
|                                                             |                         |
|                                                             v                         |
|                                              +-----------------------------+          |
|                                              |      ElectricalNetlist      |          |
|                                              +-----------------------------+          |
|                                                             |                         |
|                                                             v                         |
|                                              +-----------------------------+          |
|                                              |     TopologyClassifier      |          |
|                                              +-----------------------------+          |
|                                                             |                         |
|                               +-----------------------------+--------------------+    |
|                               |                             |                    |    |
|                               v                             v                    v    |
|                     +--------------------+        +-------------------+    +--------+ |
|                     | FirstOrderRc/Rl    |        | SecondOrderRlc    |    | BJT/   | |
|                     | Strategy           |        | Strategy          |    | MOSFET | |
|                     +--------------------+        +-------------------+    +--------+ |
|                               |                             |                    |    |
|                               +-----------------------------+--------------------+    |
|                                                             |                         |
|                                                             v                         |
|                                              +-----------------------------+          |
|                                              |   ScientificNotation +      |          |
|                                              |     MathFormulaView         |          |
|                                              +-----------------------------+          |
+---------------------------------------------------------------------------------------+
```

### Module Boundaries
1. **Desktop UI & Canvas Control** (`apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`):
   - Win2D Direct2D drawing loop for elements, wires, terminals, and EDA solder dots.
   - Interactive pointer event handlers for component dragging, rubber-banding, wire routing, corner adjustment, and wire tapping.
2. **Data Models & State** (`apps/desktop-winui/Models/CircuitElement.cs`, `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`):
   - Canonical `CircuitElement`, `CircuitPin`, and `CircuitWire` models.
   - Project lifecycle: declarative template generation for `CommonEmitter_Amp.sch`, `RC_Transient.sch`, `RC_Coupled_Amp.sch`.
3. **Electrical Engine & Topology Classifier** (`apps/desktop-winui/Engine/`):
   - `ElectricalNetlist.cs`: Net representation, connected pin references, ground/power net identification.
   - `NetlistExtractor.cs`: Union-find graph connectivity extraction from canvas elements and wires.
   - `TopologyClassifier.cs`: Circuit identification (1st-order RC/RL, 2nd-order RLC, BJT/MOSFET amplifier, General MNA).
   - `Strategies/`: Dedicated analytical solvers emitting rigorous LaTeX derivations with zero fictitious parameters.
4. **Formula & Scientific Rendering** (`apps/desktop-winui/Controls/MathFormulaView.xaml.cs`, `apps/desktop-winui/Services/ScientificNotationFormatter.cs`):
   - LaTeX-to-Unicode conversion supporting full Greek (`\omega`, `\Delta`, `\tau`, `\beta`, `\alpha`), math operators (`\sqrt`, `\pm`, `\le`, `\ge`, `\infty`, `\to`, `\angle`), and matrix formats.
   - Standardized scientific notation for all values $\ge 1000$ or $< 0.001$.

---

## Feature Inventory

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Netlist Extraction | Extract `ElectricalNetlist` from `CircuitElement` and `CircuitWire` using Union-Find and pin world coordinates | M2 | R1 §1 |
| 2 | Topology Classifier | Classify circuits into 1st-order RC/RL, 2nd-order RLC, Transistor (BJT/MOSFET), and MNA Linear | M2 | R1 §1 |
| 3 | 1st-Order RC/RL Solver | Compute Thevenin $R_{eq}$, $\tau$, three-element differential solution, $H(s)$, $f_c$ with zero BJT parameters | M2 | R1 §1 |
| 4 | 2nd-Order RLC Solver | Compute $\omega_0$, $\alpha$, $Q$, characteristic roots $s_{1,2}$, and damping regime (over/crit/under) | M2 | R1 §1 |
| 5 | Transistor Amplifier Solver | DC Q-point ($I_{BQ}, I_{CQ}, V_{CEQ}$), $r_{be}/g_m$, midband $A_v$, $R_i, R_o$, Bode response | M2 | R1 §1 |
| 6 | MNA Matrix Solver | Standard SPICE MNA matrix equation $\mathbf{G}\mathbf{v} + \mathbf{B}\mathbf{i} = \mathbf{J}$ with formatted LaTeX | M2 | R1 §1 |
| 7 | MathFormulaView Enhancements | Extended Unicode converter for $\omega, \Delta, \sqrt{}, \pm, \le, \ge, \neq, \to, \infty, \angle$ and matrices | M2 | R1 §2 |
| 8 | Scientific Notation Formatter | Strict scientific notation formatting for $|v| \ge 1000$ or $|v| < 0.001$ across all derivations | M2 | R1 §2 |
| 9 | Declarative Template Model | Pre-populate `ViewModel.Wires` and `Elements` with declarative data models, retiring `DrawCircuitWires` hack | M1 | R2 §1 |
| 10 | GB/T 4728 & IEC 60617 Layout | Top VCC rail ($Y=80$) and bottom GND rail ($Y=480$) aligned; signal flow left-to-right | M1 | R2 §2 |
| 11 | Grid Modulus & Spacing | 40px (80 mil) grid modulus; $R_e$ and $C_e$ spacing widened to 100px with zero text/component collision | M1 | R2 §2 |
| 12 | Eliminate Template Flaws | Remove overlapping VCC circular source; fix wire piercing Q1, C1, vs; align collector & emitter leads | M1 | R2 §2 |
| 13 | Additional Templates | Implement `RC_Transient.sch` and fix `RC_Coupled_Amp.sch` | M1 | R2 §1 |
| 14 | Bus/Trunk Routing | Single orthogonal lead from pins; perpendicular T-junction branches for parallel components & grounds | M3 | R3 §1 |
| 15 | EDA Solder Dot Rendering | 3.8px solid dark EDA solder dots at true T-junctions; terminal keep-out (0 dots on pin terminals) | M3 | R3 §1 |
| 16 | Interactive Wire Tap | Click existing wire to spawn perpendicular branch; child wires track parent topologically | M3 | R3 §2 |
| 17 | Corner & Segment Dragging | Orthogonal segment dragging with 10px grid snap, replacing binary FlipCorner | M3 | R3 §2 |
| 18 | Rubber-Banding Wire Follow | Moving components updates attached wires dynamically without tearing or leaving orphan taps | M3 | R3 §2 |
| 19 | E2E Solution Verification | `CircuitAssistant.csproj` compiles with 0 errors; all acceptance criteria T1-T5 verified | M4 | AC §1-5 |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Educational Templates & Model Modernization (R2) | Declarative templates (`CommonEmitter_Amp.sch`, `RC_Transient.sch`, `RC_Coupled_Amp.sch`), retire `DrawCircuitWires`, GB/T 4728 & IEC 60617 grid layout (80/160 mil), eliminate wire piercing, VCC overlap, and Re/Ce spacing collisions | none | PLANNED |
| M2 | Electrical Topology Analysis & Solving Engine (R1) | Native C# `ElectricalNetlist`, `NetlistExtractor`, `TopologyClassifier`, 4 strategy classes (RC/RL, RLC, BJT/MOSFET, MNA), MathFormulaView symbol extensions, ScientificNotationFormatter integration, remove hardcoded BJT fallback | M1 | PLANNED |
| M3 | Industrial EDA Wire Routing & Canvas Architecture (R3) | Bus/Trunk routing, 3.8px solder dots at genuine T-junctions with terminal keep-out, interactive Wire Tap, orthogonal corner/segment dragging, rubber-banding wire updates | M1 | PLANNED |
| M4 | Final Integration, E2E Test Suite & Acceptance Verification | Pass 100% of E2E verification test suite (Tiers 1-4), verify all 5 acceptance criteria, solution compilation check (0 errors, 0 warnings), final forensic audit | M2, M3 | PLANNED |

---

## Interface Contracts

### M1 (Templates) ↔ M2 (Engine) & M3 (Canvas)
- **Data Model**: `AnalysisStudioViewModel.Elements` (`ObservableCollection<CircuitElement>`) and `AnalysisStudioViewModel.Wires` (`ObservableCollection<CircuitWire>`).
- **Invariants**:
  - `Wires` must NOT be empty after loading any template.
  - Every `CircuitWire` must have valid `StartX, StartY, EndX, EndY`, and references `StartElementId, StartPinName, EndElementId, EndPinName`.
  - For T-taps, `EndPinName = "WireTap"` and `EndElementId = parentWire.Id` (or topological child reference).

### M2 (Netlist & Topology) ↔ M2 (Strategies & UI)
- **Class**: `CircuitAssistant.Engine.NetlistExtractor`
  - Signature: `public static ElectricalNetlist ExtractNetlist(IEnumerable<CircuitElement> elements, IEnumerable<CircuitWire> wires)`
  - Output: `ElectricalNetlist` with populated `Nets`, `FindNetForPin(elementId, pinId)`.
- **Class**: `CircuitAssistant.Engine.TopologyClassifier`
  - Signature: `public static CircuitTopologyType Classify(ElectricalNetlist netlist, out ICircuitStrategy strategy)`
- **Interface**: `CircuitAssistant.Engine.ICircuitStrategy`
  - Signature: `Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)`
  - Emits: `PedagogicalSolution` with step-by-step LaTeX formulas formatted with scientific notation.

### M3 (Canvas Wire Routing) ↔ Canvas View
- **Solder Dot Detection**:
  - `junctionDots`: Exact floating point coordinates of intersections between wire endpoints and orthogonal wire segments.
  - Keep-out check: If a junction coordinate is within 5px of any component pin world coordinate, it is excluded from solder dot rendering.
  - Dot geometry: `ds.FillCircle(pos, 3.8f, dotColor)`.

---

## Code Layout

- `apps/desktop-winui/`:
  - `Models/`:
    - `CircuitElement.cs`: Canonical `CircuitElement`, `CircuitPin`, `CircuitWire`.
  - `Engine/`:
    - `ElectricalNetlist.cs`: Netlist and Net data structures.
    - `NetlistExtractor.cs`: Graph connectivity extraction.
    - `TopologyClassifier.cs`: Circuit category classifier.
    - `Strategies/`:
      - `FirstOrderRcStrategy.cs`: 1st-order RC/RL solver.
      - `SecondOrderRlcStrategy.cs`: 2nd-order RLC resonance solver.
      - `BjtAmplifierStrategy.cs`: BJT/MOSFET amplifier solver.
      - `MnaLinearStrategy.cs`: SPICE MNA linear network solver.
  - `ViewModels/`:
    - `AnalysisStudioViewModel.cs`: Circuit templates, project state, solver invocation.
  - `Controls/`:
    - `CircuitCanvas.xaml.cs`: Win2D Direct2D canvas rendering and pointer interaction.
    - `MathFormulaView.xaml.cs`: LaTeX-to-Unicode formula rendering.
  - `Services/`:
    - `CircuitApiService.cs`: API client and pedagogical solution definitions.
    - `ScientificNotationFormatter.cs`: LaTeX/Unicode scientific notation formatting.
- `apps/desktop-winui/CircuitAssistant.csproj`: Project compilation file.
- `tests/`:
  - Unit and integration tests for engine, netlist, and template verification.
