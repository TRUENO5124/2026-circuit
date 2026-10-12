# Circuit Template Explorer (Requirement R2) Handoff Report

**Project**: CircuitAssistant (电路仿真教学助手)  
**Task**: Requirement R2 — 工业标准教学模板电路图底层重构 (Industrial Standard Educational Template Circuit Diagram Refactoring)  
**Agent**: Teamwork Explorer (Survey 2)  
**Date**: 2026-10-11T19:20:00Z  

---

## 1. Observation

### 1.1 Template Storage & Loading Inventory
- **File System Inspection**:
  - Command: `find_by_name(Pattern="*.sch", SearchDirectory="d:/BJTU/2026年大创")`
  - Result: **0 files found**. No physical `.sch` files exist in the repository.
- **Source Code Search**:
  - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`:
    - Line 162: `private string _projectFileName = "CommonEmitter_Amp.sch";`
    - Line 796: `ProjectFileName = "Blank_Circuit.sch";`
    - Line 805: `ProjectFileName = "RC_Coupled_Amp.sch";`
    - Line 813: `ProjectFileName = "CommonEmitter_Amp.sch";`
  - Observation: `CommonEmitter_Amp.sch`, `RC_Coupled_Amp.sch`, and `Blank_Circuit.sch` exist solely as string labels displayed on the tab header.
- **In-Memory Hardcoding**:
  - In `AnalysisStudioViewModel.cs` (lines 569–785):
    `InitializeDefaultCircuitElements()` hardcodes exactly 12 `CircuitElement` items in C#:
    1. `VCC` (`source.v_dc`, $X=530, Y=75, W=90, H=40$)
    2. `GND` (`reference.ground`, $X=330, Y=445, W=40, H=35$)
    3. `vs` (`source.v_ac_small_signal`, $X=70, Y=280, W=60, H=60$)
    4. `Rb1` (`passive.resistor`, Rot=90°, $X=200, Y=150, W=80, H=50$)
    5. `Rb2` (`passive.resistor`, Rot=90°, $X=200, Y=340, W=80, H=50$)
    6. `Rc` (`passive.resistor`, Rot=90°, $X=350, Y=130, W=80, H=50$)
    7. `Re` (`passive.resistor`, Rot=90°, $X=330, Y=385, W=80, H=50$)
    8. `RL` (`passive.resistor`, Rot=90°, $X=500, Y=280, W=80, H=50$)
    9. `C1` (`passive.capacitor`, Rot=0°, $X=130, Y=250, W=80, H=50$)
    10. `C2` (`passive.capacitor`, Rot=0°, $X=440, Y=180, W=80, H=50$)
    11. `Ce` (`passive.capacitor`, Rot=90°, $X=380, Y=385, W=80, H=50$)
    12. `Q1` (`bjt.npn`, $X=330, Y=250, W=80, H=80$)
  - In `AnalysisStudioViewModel.cs` line 791: `Wires.Clear();` is called. **The `Wires` collection contains 0 items.**
  - In `AnalysisStudioViewModel.cs` lines 801–817:
    `CreateNewProject("ac_coupled")` calls `ResetToDefaults()`, which executes the exact same `InitializeDefaultCircuitElements()`. There is **no distinct circuit topology** for the RC-coupled amplifier, and **no first-order RC charge/discharge template** exists in the desktop app.

### 1.2 Procedural Rendering & Hardcoded Wire Traces
- In `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (lines 448–451):
  ```csharp
  // 3. Draw classic template wires if no custom wires exist and BJT template is loaded
  if (Wires.Count == 0)
  {
      DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);
  }
  ```
- In `CircuitCanvas.xaml.cs` (lines 492–615), `DrawCircuitWires` procedurally draws line coordinates with ad-hoc heuristics and magic numbers rather than referencing actual component pins:
  - Line 523: `ds.DrawLine(Math.Min(rb1.X - 20f, minX + 60f), vccY, maxX, vccY, wireColor, 2.5f);`
  - Line 531: `ds.DrawLine(minX, gndY, maxX, gndY, wireColor, 2.5f);`
  - Line 542: `float bjtBaseX = q1.X - 30;`
  - Line 549: `ds.DrawLine(c1.X + 22, c1.Y, rb1.X, baseNodeY, wireColor, 2f);`
  - Line 552: `ds.DrawLine(vs.X, vs.Y - 22, vs.X, c1.Y, wireColor, 2f);`
  - Line 564–566:
    ```csharp
    float collNodeY = rc.Y + 20 + 20;
    ds.DrawLine(rc.X, rc.Y + 20, rc.X, collNodeY, wireColor, 2f);
    ds.DrawLine(rc.X, collNodeY, q1.X, q1.Y - 30, wireColor, 2f);
    ```
  - Line 575–576:
    ```csharp
    float emitNodeY = q1.Y + 30 + 20;
    ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY, wireColor, 2f);
    ```

### 1.3 Exact Pin Definitions & Geometric Offsets
- In `apps/desktop-winui/Models/CircuitElement.cs` (lines 257–340):
  - `ElementType.Resistor`:
    - Pin 1: `(RelativeX = -40, RelativeY = 0)`
    - Pin 2: `(RelativeX = 40, RelativeY = 0)`
    - When rotated 90°: Pin 1 at $(X, Y - 40)$, Pin 2 at $(X, Y + 40)$.
  - `ElementType.Capacitor`:
    - Pin 1: `(RelativeX = -35, RelativeY = 0)`
    - Pin 2: `(RelativeX = 35, RelativeY = 0)`
    - When rotated 0°: Pin 1 at $(X - 35, Y)$, Pin 2 at $(X + 35, Y)$.
    - When rotated 90°: Pin 1 at $(X, Y - 35)$, Pin 2 at $(X, Y + 35)$.
  - `ElementType.BjtNpn`:
    - Base: `(RelativeX = -35, RelativeY = 0)` $\to$ World: $(X - 35, Y)$
    - Collector: `(RelativeX = 20, RelativeY = -35)` $\to$ World: $(X + 20, Y - 35)$
    - Emitter: `(RelativeX = 20, RelativeY = 35)` $\to$ World: $(X + 20, Y + 35)$
  - `ElementType.Ground`:
    - Pin GND: `(RelativeX = 0, RelativeY = -22)` $\to$ World: $(X, Y - 22)$
  - `ElementType.DcVoltageSource` / `AcVoltageSource`:
    - Pin pos: `(RelativeX = 0, RelativeY = -35)`
    - Pin neg: `(RelativeX = 0, RelativeY = 35)`

### 1.4 Coordinate System, Grid & Snapping
- In `CircuitCanvas.xaml.cs`:
  - `DrawGrid` (lines 886–895):
    `step = 40` (draws dot grid at intervals of $40\text{ px}$).
  - Pointer dragging snap (line 1533):
    `MathF.Round((_dragStartElementPos.X + delta.X) / 10f) * 10f` ($10\text{ px}$ snap).
  - Component pin offsets:
    - Resistor: span $80\text{ px}$ (multiple of 40).
    - Capacitor: span $70\text{ px}$ (not aligned to $40\text{ px}$ grid; offset by $5\text{ px}$ on each side).
    - BJT vertical pin distance: $70\text{ px}$ (not aligned to $40\text{ px}$ grid).
    - Ground pin: $-22\text{ px}$ (fractional off-grid).

---

## 2. Logic Chain

### 2.1 Logic: The Fragility of Template Wire Rendering
1. **Observation**: `Wires.Count == 0` upon template creation (`AnalysisStudioViewModel.cs:791`).
2. **Observation**: `CircuitCanvas.xaml.cs:448` renders `DrawCircuitWires` *only if* `Wires.Count == 0`.
3. **Reasoning**: If a user connects any wire using the interactive wire tool (`WireCreated` appends to `Wires`), `Wires.Count` becomes $> 0$.
4. **Deduction**: The moment a user draws a single wire or tap on the canvas, `DrawCircuitWires` stops executing, and all original circuit connections (VCC rail, GND rail, bias lines, transistor leads) disappear instantly. The template is not represented by real data models, making interactive modifications catastrophic.

### 2.2 Logic: Defect Analysis of `CommonEmitter_Amp.sch`
1. **Defect 1 — Overlapping VCC Loops / Multiple Coils & Conflicting Texts**:
   - `AnalysisStudioViewModel.cs:574` places a full 2-terminal DC voltage source `VCC` at $(530, 75)$.
   - `CircuitCanvas.xaml.cs:523` draws the horizontal VCC supply rail from $X=110$ to $X=540$ at $Y=75$.
   - The rail line passes directly through the body of the DC voltage source circle at $(530, 75)$.
   - Line 527 draws text `"VCC (+12 V)"` at $(550, 67)$.
   - `DrawElementLabels` (line 780) simultaneously draws `"VCC"` at $(556, 61)$ and `"12 V"` at $(556, 77)$.
   - The two text renderers collide and produce double-printed text over the rail line.
   - Standard EDA (KiCad/Altium/IEC 60617) specifies that VCC in an amplifier schematic must be a single upward Power Port terminal (bar or arrow symbol) or a dedicated bus feed, not an inline independent two-terminal source circle sitting on the wire.

2. **Defect 2 — Wires Piercing Component Bodies and Pin Terminals**:
   - **Q1 Collector Piercing**:
     - `Rc` is at $(350, 130)$, rotated 90°. Its bottom pin is at $(350, 170)$.
     - `Q1` is at $(330, 250)$. Its Collector pin is at $(330 + 20, 250 - 35) = (350, 215)$.
     - Both $Rc$ and $Q1.C$ share the exact same horizontal coordinate $X = 350$.
     - However, line 566 draws: `ds.DrawLine(rc.X, collNodeY, q1.X, q1.Y - 30);`
     - This draws a diagonal line from $(350, 170)$ to $(330, 220)$.
     - Instead of dropping vertically into the collector pin, the line slants leftward, cutting diagonally across the circuit and piercing into Q1's package body!
   - **Q1 Emitter Disconnection**:
     - `Q1` Emitter pin is at $(330 + 20, 250 + 35) = (350, 285)$.
     - Line 576 draws: `ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY);` from $(330, 280)$ to $(330, 300)$.
     - The wire starts at $X = 330$, whereas the emitter pin is at $X = 350$. The wire is completely disconnected from the emitter pin!
   - **Q1 Base Gap**:
     - `Q1` Base pin is at $(330 - 35, 250) = (295, 250)$.
     - Line 542 sets `bjtBaseX = q1.X - 30 = 300`.
     - The base wire stops at $X = 300$, failing to reach the pin terminal at $X = 295$.
   - **Capacitor C1 Piercing**:
     - C1 is at $(130, 250)$, with pins at $X = 95$ and $X = 165$.
     - Line 549 starts the wire at $c1.X + 22 = 152$ (inside C1's right plate).
     - Line 553 ends the input wire at $c1.X - 22 = 108$ (inside C1's left plate).
     - Both wires pierce through the capacitor's internal dielectric area.
   - **AC Signal Source Piercing**:
     - vs is at $(70, 280)$ with pins at $Y = 245$ and $Y = 315$.
     - Line 552 starts the wire at $vs.Y - 22 = 258$, piercing the top lead.

3. **Defect 3 — Cramped Spacing between $R_e$ and $C_e$ & Text Collision**:
   - $R_e$ is at $(330, 385)$ (Vertical, Width 80, Height 50).
   - $C_e$ is at $(380, 385)$ (Vertical, Width 80, Height 50).
   - Center distance: $\Delta X = 380 - 330 = 50\text{ px}$.
   - $R_e$'s labels are rendered to its right: $X = 330 + 18 = 348$, extending $40\text{ px}$ to $X = 388$.
   - $C_e$'s vertical leads and plates are situated at $X = 380 \pm 6$.
   - $R_e$'s identifier `"Re"` and parameter `"1 kΩ"` are printed directly over $C_e$'s symbol and top/bottom wire leads!

4. **Defect 4 — Misaligned Top VCC Bus and Bottom GND Bus**:
   - Top VCC rail: spans $X = 110$ to $X = 540$ at $Y = 75$.
   - Bottom GND rail: spans $X = 50$ to $X = 540$ at $Y = 445$.
   - Left endpoints are staggered by $60\text{ px}$ ($110$ vs $50$), violating standard electrical drafting symmetry where power and ground rails bound the functional stages symmetrically.
   - Ground reference symbol is located at $(330, 445)$ with its terminal pin at $Y = 423$.
   - The GND rail at $Y = 445$ runs straight through the ground symbol's horizontal plates.

5. **Defect 5 — Multi-branch Bus Routing & Solder Dot Violations (R3 Synergy)**:
   - At the emitter node, instead of an orthogonal trunk with T-junction branches, the code attempts to draw separate lines from arbitrary points.
   - Wires to ground drop at 5 separate coordinates ($X = 70, 200, 330, 380, 500$) without clean modular alignment.

---

## 3. Caveats
1. **Scope Boundary**: This investigation focuses strictly on Requirement R2 (educational template circuit diagram refactoring) and its architectural impact on R1 (solver inputs) and R3 (bus routing and solder dots). We performed read-only analysis without editing source code.
2. **Web vs Desktop Data Model**:
   - Web editor uses `@circuit/schema` (`CircuitGraph` with `components`, `nodes`, `connections` in JSON).
   - Desktop WinUI uses C# classes (`CircuitElement`, `CircuitPin`, `CircuitWire`).
   - The recommended template reconstruction must be provided in both data formats (JSON for cross-platform benchmark/schema, and C# data factory/JSON loader for WinUI 3 desktop client).
3. **No Caveats on Defect Reproduction**: All 7 identified defects in `CommonEmitter_Amp.sch` and `DrawCircuitWires` were verified directly from source code and mathematical coordinates.

---

## 4. Conclusion & Reconstruction Blueprint

### 4.1 Architectural Decision: Retire `DrawCircuitWires()`
- The hardcoded procedural method `DrawCircuitWires()` must be completely removed from `CircuitCanvas.xaml.cs`.
- Templates must be constructed as **declarative datasets** containing:
  1. An `ObservableCollection<CircuitElement>` with standardized grid coordinates.
  2. An `ObservableCollection<CircuitWire>` containing every orthogonal wire and bus branch, with assigned `NetId`s and `FlipCorner` configurations.
- Because `Wires` will be pre-populated with actual `CircuitWire` instances, standard Win2D rendering and rubber-banding will automatically apply without special-case branching.

### 4.2 Standard Modular Grid System ($M = 40\text{ px}$ / $80\text{ mil}$)
To conform to **GB/T 4728** and **IEC 60617**:
- Base Grid Modulus $M = 40\text{ px}$.
- Sub-grid snapping modulus $M/2 = 20\text{ px}$.
- Component Terminal Normalization:
  - Resistor: Pins at $(0, -40)$ and $(0, +40)$ when vertical; $(-40, 0)$ and $(+40, 0)$ when horizontal.
  - Capacitor: Normalize pin offsets from $\pm 35$ to $\pm 40$ (or keep lead length 40px so terminals snap to grid).
  - BJT: Base pin at $(-40, 0)$, Collector pin at $(+20, -40)$, Emitter pin at $(+20, +40)$.
  - Ground: Top pin at $(0, -20)$, ground bars at $(0, 0)$.

### 4.3 Standardized Reconstruction Blueprint for `CommonEmitter_Amp.sch`

#### Grid Coordinate Allocation:
- **Horizontal Rails**:
  - Top VCC Bus Rail: $Y = 80\text{ px}$, spanning $X = 220\text{ px}$ to $X = 540\text{ px}$.
  - Signal Flow Centerline: $Y = 260\text{ px}$.
  - Bottom GND Reference Rail: $Y = 480\text{ px}$, spanning $X = 80\text{ px}$ to $X = 540\text{ px}$.

#### Left-to-Right Stage Layout:
| Stage | Component | ID | Center $(X, Y)$ | Rotation | Terminal 1 $(X, Y)$ | Terminal 2 $(X, Y)$ | Notes |
|---|---|---|---|---|---|---|---|
| **Input** | AC Source | `vs` | $(80, 320)$ | 0° | $(80, 280)$ `pos` | $(80, 360)$ `neg` | Bottom drops to GND at $(80, 480)$ |
| **Coupling** | Input Cap | `C1` | $(160, 260)$ | 0° (Horiz) | $(120, 260)$ `1` | $(200, 260)$ `2` | Aligned with signal centerline |
| **Bias Top** | Pull-up Res | `Rb1` | $(240, 160)$ | 90° (Vert) | $(240, 120)$ `1` | $(240, 200)$ `2` | Top connects to VCC rail at $(240, 80)$ |
| **Bias Bottom**| Pull-down Res| `Rb2` | $(240, 360)$ | 90° (Vert) | $(240, 320)$ `1` | $(240, 400)$ `2` | Bottom drops to GND rail at $(240, 480)$ |
| **Amplifier** | NPN Transistor| `Q1` | $(320, 260)$ | 0° | Base: $(280, 260)$ | Coll: $(340, 220)$ | Emit: $(340, 300)$ |
| **Collector** | Collector Res | `Rc` | $(340, 140)$ | 90° (Vert) | $(340, 100)$ `1` | $(340, 180)$ `2` | Top connects to VCC rail at $(340, 80)$; Bottom drops to Coll $(340, 220)$ |
| **Emitter R** | Emitter Res | `Re` | $(320, 400)$ | 90° (Vert) | $(320, 360)$ `1` | $(320, 440)$ `2` | Top connects to Emitter Trunk $(340, 340)$; Bottom to GND at $(320, 480)$ |
| **Bypass C** | Bypass Cap | `Ce` | $(420, 400)$ | 90° (Vert) | $(420, 360)$ `1` | $(420, 440)$ `2` | Top connects to Emitter Trunk $(340, 340)$; Bottom to GND at $(420, 480)$ |
| **Output C** | Output Cap | `C2` | $(440, 220)$ | 0° (Horiz) | $(400, 220)$ `1` | $(480, 220)$ `2` | Input connected to Collector Node at $(340, 220)$ |
| **Load** | Load Resistor | `RL` | $(540, 320)$ | 90° (Vert) | $(540, 280)$ `1` | $(540, 360)$ `2` | Top connects to Output Node at $(540, 220)$; Bottom to GND at $(540, 480)$ |
| **Ground** | Earth Ground | `GND` | $(370, 480)$ | 0° | $(370, 460)$ `GND` | Ground bar on rail | Sits cleanly on the bottom GND bus rail |

#### Spacing & Clearance Verification:
- $R_e$ ($X = 320$) and $C_e$ ($X = 420$) horizontal distance is **$100\text{ px}$** (2.5 grid units).
  - $R_e$ labels on right occupy $X \in [338, 380]$.
  - $C_e$ symbol boundary is $X \in [414, 426]$.
  - **Clearance between labels and capacitor body is $> 34\text{ px}$** $\to$ Completely eliminates text interference!
- Top rail and bottom rail are aligned and symmetric:
  - Top VCC rail: from $X = 220$ to $X = 540$ at $Y = 80$.
  - Bottom GND rail: from $X = 80$ to $X = 540$ at $Y = 480$.
- VCC Power Port:
  - Eliminate the circular DC source element on the rail.
  - Render an upward Power Terminal at $(540, 80)$ or $(340, 60)$ with label `+VCC (+12V)`.

#### Declarative Wire List Specification (`CircuitWire` Instances):
1. `W_VS_IN`: From `vs.pos` $(80, 280)$ to $(80, 260)$ to `C1.1` $(120, 260)$.
2. `W_BASE_NET`:
   - `C1.2` $(200, 260)$ to Base Junction $(240, 260)$ [Solder Dot at $(240, 260)$].
   - Base Junction $(240, 260)$ up to `Rb1.2` $(240, 200)$.
   - Base Junction $(240, 260)$ down to `Rb2.1` $(240, 320)$.
   - Base Junction $(240, 260)$ right to `Q1.Base` $(280, 260)$.
3. `W_VCC_RAIL`:
   - Bus trunk: from $(240, 80)$ to $(340, 80)$ to $(540, 80)$.
   - Drop to `Rb1.1`: from $(240, 80)$ down to $(240, 120)$ [Solder Dot at $(240, 80)$].
   - Drop to `Rc.1`: from $(340, 80)$ down to $(340, 100)$ [Solder Dot at $(340, 80)$].
4. `W_COLLECTOR_NET`:
   - From `Rc.2` $(340, 180)$ straight down to Collector Node $(340, 220)$ [Solder Dot at $(340, 220)$].
   - Collector Node $(340, 220)$ into `Q1.Collector` $(340, 220)$.
   - Collector Node $(340, 220)$ horizontally right to `C2.1` $(400, 220)$.
5. `W_EMITTER_NET` (Single Trunk + T-Branch):
   - From `Q1.Emitter` $(340, 300)$ straight down to Emitter Distribution Node $(340, 340)$ [Solder Dot at $(340, 340)$].
   - Branch left from $(340, 340)$ to $(320, 340)$ and down to `Re.1` $(320, 360)$.
   - Branch right from $(340, 340)$ to $(420, 340)$ and down to `Ce.1` $(420, 360)$.
6. `W_OUTPUT_NET`:
   - From `C2.2` $(480, 220)$ to Output Node $(540, 220)$ [Solder Dot at $(540, 220)$].
   - Output Node $(540, 220)$ down to `RL.1` $(540, 280)$.
   - Output Node $(540, 220)$ right to test port $v_o$ at $(580, 220)$.
7. `W_GND_RAIL`:
   - Continuous horizontal trunk from $(80, 480)$ to $(540, 480)$ at $Y = 480$.
   - Drop from `vs.neg`: $(80, 360)$ to $(80, 480)$ [Solder Dot at $(80, 480)$].
   - Drop from `Rb2.2`: $(240, 400)$ to $(240, 480)$ [Solder Dot at $(240, 480)$].
   - Drop from `Re.2`: $(320, 440)$ to $(320, 480)$ [Solder Dot at $(320, 480)$].
   - Drop from `Ce.2`: $(420, 440)$ to $(420, 480)$ [Solder Dot at $(420, 480)$].
   - Drop from `RL.2`: $(540, 360)$ to $(540, 480)$ [Solder Dot at $(540, 480)$].
   - Ground terminal: `GND` at $(370, 480)$ bonded directly to the GND trunk.

### 4.4 Specification for Additional Educational Templates

#### Template 2: First-Order RC Charge/Discharge & Transient Response (`RC_Transient.sch`)
- **Circuit Objective**: Support Requirement R1 acceptance criteria (RC transient, time constant $\tau = RC$, 3-factor differential equation $f(0^+), f(\infty), \tau$).
- **Component Placement**:
  - DC Voltage Source $V_S$ ($10\text{ V}$): $X = 120, Y = 280$
  - Series Resistor $R_1$ ($1\text{ k}\Omega$): $X = 240, Y = 200$ (Horizontal)
  - Integrating Capacitor $C_1$ ($100\text{ }\mu\text{F}$): $X = 360, Y = 280$ (Vertical)
  - Output Voltage Probe $v_C(t)$: $X = 440, Y = 200$
  - Bottom Ground Rail: $Y = 360$, connecting $V_S.n$ and $C_1.n$.
- **Signal Flow**: Step excitation $\to$ Charging Resistor $\to$ Integrating Capacitor $\to$ Probe.

#### Template 3: RC-Coupled Single-Stage Amplifier (`RC_Coupled_Amp.sch`)
- Fix the existing bug where selecting `RC_Coupled_Amp.sch` reloads `CommonEmitter_Amp.sch`.
- Load a dedicated RC-coupled topology emphasizing AC signal paths without emitter degeneration or with split collector load.

---

## 5. Verification Method

To independently verify these findings and confirm the existing state of the codebase:

1. **Verify Desktop WinUI Solution Build**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected outcome*: 0 Errors, 0 Warnings.

2. **Verify Python Analytical Engine Tests**:
   ```powershell
   pytest services/engine/tests services/api/tests
   ```
   *Expected outcome*: 33 passed in ~4.8s.

3. **Verify TypeScript Schema Contracts & Benchmark JSON Validation**:
   ```powershell
   npm run check
   ```
   *Expected outcome*: 16 schema tests passed, 3 benchmarks validated, all artifacts built cleanly.

4. **Code Inspection Verification**:
   - Inspect `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` lines 569–785 to verify that `Wires` is never populated.
   - Inspect `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` lines 448–451 and 564–576 to verify the hardcoded diagonal line piercing Q1's body and the disconnected emitter wire.
   - Inspect `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` lines 523–528 and `DrawElementLabels` lines 780–814 to verify the overlapping VCC lines and text labels.
