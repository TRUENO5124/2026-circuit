# Milestone 1 Mathematical Validation & Layout Specification Handoff Report

**Project**: CircuitAssistant (电路仿真教学助手)  
**Milestone**: Milestone 1 (Educational Templates & Model Modernization - Requirement R2)  
**Agent**: Teamwork Explorer M1-2  
**Date**: 2026-10-11T19:28:00Z  

---

## 1. Observation

### 1.1 Existing File Locations and Template Mechanism
1. **Repository Template Storage**:
   - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 162, 569–785, 788–817):
     - Line 162: `private string _projectFileName = "CommonEmitter_Amp.sch";`
     - Line 791: `Wires.Clear();`
     - Lines 569–780: `InitializeDefaultCircuitElements()` hardcodes exactly 12 `CircuitElement` items in memory (`VCC`, `GND`, `vs`, `Rb1`, `Rb2`, `Rc`, `Re`, `RL`, `C1`, `C2`, `Ce`, `Q1`).
     - Line 791 clears `Wires` upon creation, leaving `Wires.Count == 0`.
2. **Procedural Wire Rendering Hack**:
   - `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (lines 448–451):
     ```csharp
     if (Wires.Count == 0)
     {
         DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);
     }
     ```
     `DrawCircuitWires` draws ad-hoc line segments directly onto the canvas buffer when `Wires.Count == 0`.

### 1.2 Verbatim Observations of Flaws in Current Layout & Drafting
1. **Q1 Transistor Wire Piercing & Disconnection**:
   - In `CircuitElement.cs` (lines 294–298):
     `BjtNpn` pins: Base `RelativeX = -35, RelativeY = 0`; Collector `RelativeX = 20, RelativeY = -35`; Emitter `RelativeX = 20, RelativeY = 35`.
   - In `AnalysisStudioViewModel.cs` (lines 669, 775): `Rc` at $(350, 130)$, rotated 90° (bottom pin at $(350, 170)$); `Q1` at $(330, 250)$ (Collector pin at $(350, 215)$, Emitter pin at $(350, 285)$).
   - In `CircuitCanvas.xaml.cs` (lines 564–566):
     ```csharp
     float collNodeY = rc.Y + 20 + 20; // 170
     ds.DrawLine(rc.X, rc.Y + 20, rc.X, collNodeY, wireColor, 2f);
     ds.DrawLine(rc.X, collNodeY, q1.X, q1.Y - 30, wireColor, 2f); // Draws from (350, 170) to (330, 220)!
     ```
     This slants leftward and diagonally cuts straight through Q1's package body circle (radius 30, centered at $(330, 250)$).
   - Lines 575–576:
     ```csharp
     float emitNodeY = q1.Y + 30 + 20; // 300
     ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY, wireColor, 2f); // Draws from (330, 280) to (330, 300)!
     ```
     The wire starts at $X = 330$, whereas Q1's emitter pin is at $X = 350$. The wire is displaced by $20\text{ px}$, leaving the emitter pin completely disconnected and cutting through the lower arc of the transistor symbol.
   - Line 542: `float bjtBaseX = q1.X - 30; // 300`
     Base pin is at $330 - 35 = 295\text{ px}$. The wire terminates at $300\text{ px}$, leaving a $5\text{ px}$ gap.

2. **Capacitor C1 Dielectric Piercing**:
   - `C1` is at $(130, 250)$, horizontal. Plates at $130 \pm 6 = [124, 136]$, pins at $130 \pm 35 = [95, 165]$.
   - In `CircuitCanvas.xaml.cs` line 549: `ds.DrawLine(c1.X + 22, c1.Y, rb1.X, baseNodeY, wireColor, 2f);` starts at $X = 152$.
   - Line 553: `ds.DrawLine(vs.X, c1.Y, c1.X - 22, c1.Y, wireColor, 2f);` ends at $X = 108$.
   - In `DrawCapacitor` (lines 910–921), capacitor leads are drawn from $\pm 35$ to $\pm 6$. The wires penetrate past the pin nodes into the dielectric space between the plates.

3. **Signal Source vs Lead Piercing**:
   - $v_s$ is at $(70, 280)$. Pin `pos` at $(70, 245)$, pin `neg` at $(70, 315)$.
   - In `CircuitCanvas.xaml.cs` line 552: `ds.DrawLine(vs.X, vs.Y - 22, vs.X, c1.Y, wireColor, 2f);` starts at $Y = 258$, penetrating $13\text{ px}$ into the source symbol.
   - Line 554: `ds.DrawLine(vs.X, vs.Y + 22, vs.X, gndY, wireColor, 2f);` starts at $Y = 302$, penetrating $13\text{ px}$ into the bottom lead.

4. **Inline 2-Terminal VCC Source & Multiple Overlapping Labels**:
   - `AnalysisStudioViewModel.cs` line 574: places an independent 2-terminal `DcVoltageSource` at $(530, 75)$ with $W = 90, H = 40$.
   - `CircuitCanvas.xaml.cs` line 523: `ds.DrawLine(Math.Min(rb1.X - 20f, minX + 60f), vccY, maxX, vccY, wireColor, 2.5f);` draws the supply rail straight through the center of the circular source symbol at $Y = 75$.
   - Line 526: draws text `"VCC (+12 V)"` at $(550, 67)$.
   - `DrawElementLabels` (lines 804–805): simultaneously draws `"VCC"` at $(556, 61)$ and `"12 V"` at $(556, 77)$.
   - The negative pin of VCC hangs unconnected at $Y = 110$.

5. **Re / Ce Cramped Spacing and Text Collision**:
   - $R_e$ is at $(330, 385)$ (vertical), $C_e$ is at $(380, 385)$ (vertical). $\Delta X = 50\text{ px}$.
   - $R_e$'s labels (`"Re"`, `"1 kΩ"`) are drawn at $X = 330 + 18 = 348$, extending to $X \approx 394$.
   - $C_e$'s physical symbol plates span $X \in [362, 398]$ with vertical lead at $X = 380$.
   - $R_e$'s text labels overlap $14\text{ px}$ past $C_e$'s lead wire and directly over $C_e$'s left plate.

6. **Power and Ground Rail Asymmetry**:
   - Top VCC rail: $X \in [110, 540]$ at $Y = 75$.
   - Bottom GND rail: $X \in [50, 540]$ at $Y = 445$.
   - The left boundary is staggered by $60\text{ px}$, and the ground symbol at $(330, 445)$ has its plates pierced by the rail line.

---

## 2. Logic Chain

### 2.1 Modulus Grid Standardization ($M = 40\text{ px} = 80\text{ mil}$)
1. **Observation**: `CircuitCanvas.xaml.cs:886` draws a background grid with `step = 40`.
2. **Observation**: Existing component pins in `CircuitElement.cs` use arbitrary offsets (e.g., Capacitor $\pm 35$, BJT $-35/+35$, Ground $-22$, Source $\pm 35$).
3. **Reasoning**: Any coordinate ending in $\pm 35$ or $\pm 22$ cannot snap to a grid with step 40 (or sub-grid step 20).
4. **Deduction**: Normalizing component terminal leads to 40px modulus ($M = 40\text{ px}$, sub-modulus $M/2 = 20\text{ px}$) aligns all component pins to the grid:
   - Resistor ($80\text{ px}$ span): Pins at $\pm 40$.
   - Capacitor ($80\text{ px}$ span): Normalize pin offsets from $\pm 35$ to $\pm 40$ (extending leads by 5px from 29px to 34px).
   - BJT NPN: Normalize Base to $(-40, 0)$, Collector to $(+20, -40)$, Emitter to $(+20, +40)$.
   - Voltage/Current Sources ($80\text{ px}$ span): Normalize pin offsets from $\pm 35$ to $\pm 40$.
   - Ground: Top pin at $(0, -20)$, ground bars at $(0, 0)$.

### 2.2 Mathematical Alignment of VCC ($Y = 80$) and GND ($Y = 480$) Rails
1. **Vertical Rail Spacing**:
   $$\Delta Y_{rails} = Y_{GND} - Y_{VCC} = 480 - 80 = 400\text{ px} = 10 \times 40\text{ px} = 10 M$$
2. **Circuit Horizontal Midplane & Transistor Center**:
   - Transistor center is placed at $Y = 260\text{ px}$ ($6.5 M$, on the $20\text{ px}$ subgrid).
   - Collector pin: $Y_{coll} = 260 - 40 = 220\text{ px}$ ($5.5 M$).
   - Distance from VCC rail ($Y = 80$) to Collector pin ($Y = 220$):
     $$\Delta Y_{top} = 220 - 80 = 140\text{ px}$$
     - Drop from VCC to $R_c.1$ ($Y = 100$): $20\text{ px}$.
     - $R_c$ body length ($Y \in [100, 180]$): $80\text{ px}$.
     - Drop from $R_c.2$ ($Y = 180$) to Collector pin ($Y = 220$): $40\text{ px}$.
     - Total: $20 + 80 + 40 = 140\text{ px}$ (Collinear along $X = 340$, zero horizontal offset).
   - Emitter pin: $Y_{emit} = 260 + 40 = 300\text{ px}$ ($7.5 M$).
   - Distance from Emitter pin ($Y = 300$) to GND rail ($Y = 480$):
     $$\Delta Y_{bottom} = 480 - 300 = 180\text{ px}$$
     - Drop from Emitter pin to Emitter Distribution Trunk ($Y = 340$): $40\text{ px}$.
     - Drop from Trunk to $R_e.1$ ($Y = 360$): $20\text{ px}$.
     - $R_e$ body length ($Y \in [360, 440]$): $80\text{ px}$.
     - Drop from $R_e.2$ ($Y = 440$) to GND rail ($Y = 480$): $40\text{ px}$.
     - Total: $40 + 20 + 80 + 40 = 180\text{ px}$.
   - Sum of vertical segments:
     $$\Delta Y_{top} + \text{Span}(Q_1) + \Delta Y_{bottom} = 140 + 80 + 180 = 400\text{ px} \equiv \Delta Y_{rails}$$
3. **Horizontal Extent & Symmetry**:
   - Bottom GND Rail: spans $X = 80\text{ px}$ (input stage $v_s$) to $X = 540\text{ px}$ (load $R_L$). Length = $460\text{ px}$.
   - Top VCC Rail: spans $X = 240\text{ px}$ ($R_{b1}$ bias stage) to $X = 540\text{ px}$ (Power Port / load align).
   - Both rails have slope $0$, terminating together at $X = 540\text{ px}$.

### 2.3 Mathematical Clearance of $R_e$ ($X = 320$) and $C_e$ ($X = 420$)
1. **Horizontal Distance**:
   $$\Delta X = X_{Ce} - X_{Re} = 420 - 320 = 100\text{ px} = 2.5 M$$
2. **Physical Boundary Analysis**:
   - $R_e$ (vertical resistor at $(320, 400)$):
     - Body rectangle: $X \in [310, 330]$, $Y \in [380, 420]$.
     - Labels (`"Re"`, `"1.5 kΩ"`) rendered at $X_{start} = 320 + 18 = 338\text{ px}$.
     - Text length: `"Re"` ($\approx 18\text{ px}$), `"1.5 kΩ"` ($\approx 46\text{ px}$).
     - Text rightmost edge: $X_{label\_max} = 338 + 46 = 384\text{ px}$.
   - $C_e$ (vertical capacitor at $(420, 400)$):
     - Plates span horizontally: $X \in [420 - 18, 420 + 18] = [402, 438]$ at $Y = 400 \pm 6$.
     - Vertical leads run along $X = 420\text{ px}$.
3. **Clearance Verification**:
   - Clearance to $C_e$'s left plate edge:
     $$\text{Clearance}_{plate} = X_{Ce\_plate\_left} - X_{label\_max} = 402 - 384 = 18\text{ px} > 0$$
   - Clearance to $C_e$'s vertical lead:
     $$\text{Clearance}_{lead} = 420 - 384 = 36\text{ px} > 0$$
   - Zero text label collision, zero symbol overlap!

### 2.4 Elimination of Piercing Wires & Industrial Power Port Replacement
1. **Elimination of Q1 Piercing**:
   - $R_c$ bottom pin is at $(340, 180)$.
   - $Q_1$ Collector pin is at $(340, 220)$.
   - Collector drop is a straight vertical line along $X = 340\text{ px}$. Zero diagonal slant, zero package piercing.
   - $Q_1$ Emitter pin is at $(340, 300)$.
   - Emitter drop is a straight vertical line along $X = 340\text{ px}$ down to $(340, 340)$. 100% connected to pin, zero package piercing.
   - Base pin is at $(280, 260)$.
   - Base lead connects from Base Node $(240, 260)$ horizontally along $Y = 260\text{ px}$ to $(280, 260)$. Zero gap.
2. **Elimination of C1 & vs Piercing**:
   - $C_1$ pins at $(120, 260)$ and $(200, 260)$. Wires terminate exactly at the pin coordinates. Zero dielectric piercing.
   - $v_s$ pins at $(80, 280)$ and $(80, 360)$. Input wire starts at $(80, 280)$; ground wire starts at $(80, 360)$. Zero lead piercing.
3. **Industrial VCC Power Port Architecture**:
   - The inline 2-terminal source at $(530, 75)$ is removed from the wire path.
   - Replace with an upward Power Port (GB/T 4728 & IEC 60617 standard):
     - Single pin terminal bonded to the VCC rail at $Y = 80\text{ px}$.
     - Upward stem from $Y = 80$ to $Y = 55\text{ px}$.
     - Horizontal bar or arrowhead at $Y = 55\text{ px}$ ($X \pm 12\text{ px}$).
     - Text label `+VCC (+12V)` positioned in the clear top margin at $Y = 40\text{ px}$.
     - Zero circular body on the rail line, zero duplicate text rendering.

---

## 3. Caveats
1. **Scope Boundary**: As Explorer M1-2, this investigation is strictly read-only. No source files were modified during this task.
2. **Data Model Synchronization**:
   - The desktop WinUI application operates with `CircuitElement` and `CircuitWire` collections in `AnalysisStudioViewModel`.
   - The solver engine in `services/engine/` expects component definitions with `definition_id == "source.v_dc"` and `ports = ["p", "n"]`.
   - The Worker must ensure that `VCC` retains `DefinitionId = "source.v_dc"` and proper port mappings (`p` to VCC net, `n` to GND net) for solver interoperability, while presenting visually as an upward Power Port on canvas.
3. **No Caveats on Geometric Proof**: All mathematical calculations, clearances, and pin alignments were verified with exact coordinates.

---

## 4. Conclusion & Precise Specification for Worker

### 4.1 Master Element Coordinate Table (`CommonEmitter_Amp.sch`)

| # | ID | DefinitionId | Type | Center $(X, Y)$ | Rot | Size $(W, H)$ | Pin 1 $(X, Y)$ | Pin 2 $(X, Y)$ | Pin 3 $(X, Y)$ | Parameter |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `vs` | `source.v_ac_small_signal` | `AcVoltageSource` | $(80, 320)$ | 0° | $60 \times 60$ | `pos`: $(80, 280)$ | `neg`: $(80, 360)$ | — | $1\text{ kHz}, 10\text{ mV}$ |
| 2 | `C1` | `passive.capacitor` | `Capacitor` | $(160, 260)$ | 0° | $80 \times 50$ | `1`: $(120, 260)$ | `2`: $(200, 260)$ | — | $10\text{ }\mu\text{F}$ |
| 3 | `Rb1` | `passive.resistor` | `Resistor` | $(240, 160)$ | 90° | $80 \times 50$ | `1`: $(240, 120)$ | `2`: $(240, 200)$ | — | $68\text{ k}\Omega$ |
| 4 | `Rb2` | `passive.resistor` | `Resistor` | $(240, 360)$ | 90° | $80 \times 50$ | `1`: $(240, 320)$ | `2`: $(240, 400)$ | — | $20\text{ k}\Omega$ |
| 5 | `Q1` | `bjt.npn` | `BjtNpn` | $(320, 260)$ | 0° | $80 \times 80$ | `B`: $(280, 260)$ | `C`: $(340, 220)$ | `E`: $(340, 300)$ | $\beta = 80$ |
| 6 | `Rc` | `passive.resistor` | `Resistor` | $(340, 140)$ | 90° | $80 \times 50$ | `1`: $(340, 100)$ | `2`: $(340, 180)$ | — | $3.3\text{ k}\Omega$ |
| 7 | `Re` | `passive.resistor` | `Resistor` | $(320, 400)$ | 90° | $80 \times 50$ | `1`: $(320, 360)$ | `2`: $(320, 440)$ | — | $1.5\text{ k}\Omega$ |
| 8 | `Ce` | `passive.capacitor` | `Capacitor` | $(420, 400)$ | 90° | $80 \times 50$ | `1`: $(420, 360)$ | `2`: $(420, 440)$ | — | $47\text{ }\mu\text{F}$ |
| 9 | `C2` | `passive.capacitor` | `Capacitor` | $(440, 220)$ | 0° | $80 \times 50$ | `1`: $(400, 220)$ | `2`: $(480, 220)$ | — | $10\text{ }\mu\text{F}$ |
| 10 | `RL` | `passive.resistor` | `Resistor` | $(540, 320)$ | 90° | $80 \times 50$ | `1`: $(540, 280)$ | `2`: $(540, 360)$ | — | $5.1\text{ k}\Omega$ |
| 11 | `VCC` | `source.v_dc` | `DcVoltageSource` | $(540, 80)$ | 0° | $50 \times 40$ | `pos`: $(540, 80)$ | `neg`: GND ref | — | $12\text{ V}$ |
| 12 | `GND` | `reference.ground` | `Ground` | $(360, 500)$ | 0° | $40 \times 35$ | `GND`: $(360, 480)$ | — | — | $0\text{ V}$ |

---

### 4.2 Declarative Wire List Specification (`CircuitWire` Instances)

| # | Wire ID | NetId | Start Pin / Node | End Pin / Node | Start $(X, Y)$ | End $(X, Y)$ | Routing & Bend Configuration | Solder Dot |
|---|---|---|---|---|---|---|---|---|
| 1 | `W_VS_IN` | 1 | `vs.pos` | `C1.1` | $(80, 280)$ | $(120, 260)$ | Orthogonal: $(80, 280) \to (80, 260) \to (120, 260)$, `FlipCorner = true` | None (Pin terminals) |
| 2 | `W_C1_BASE` | 2 | `C1.2` | Base Junction | $(200, 260)$ | $(240, 260)$ | Straight horizontal | $(240, 260)$ |
| 3 | `W_RB1_BASE`| 2 | `Rb1.2` | Base Junction | $(240, 200)$ | $(240, 260)$ | Straight vertical | $(240, 260)$ |
| 4 | `W_BASE_RB2`| 2 | Base Junction | `Rb2.1` | $(240, 260)$ | $(240, 320)$ | Straight vertical | $(240, 260)$ |
| 5 | `W_BASE_Q1` | 2 | Base Junction | `Q1.B` | $(240, 260)$ | $(280, 260)$ | Straight horizontal | $(240, 260)$ |
| 6 | `W_VCC_TRUNK`| 3 | Rail Left $(240, 80)$ | Rail Right $(540, 80)$ | $(240, 80)$ | $(540, 80)$ | Straight horizontal trunk along $Y = 80$ | $(240, 80), (340, 80)$ |
| 7 | `W_VCC_RB1` | 3 | Rail $(240, 80)$ | `Rb1.1` | $(240, 80)$ | $(240, 120)$ | Straight vertical drop | $(240, 80)$ |
| 8 | `W_VCC_RC`  | 3 | Rail $(340, 80)$ | `Rc.1` | $(340, 80)$ | $(340, 100)$ | Straight vertical drop | $(340, 80)$ |
| 9 | `W_RC_COLL` | 4 | `Rc.2` | Coll Node | $(340, 180)$ | $(340, 220)$ | Straight vertical drop | $(340, 220)$ |
| 10 | `W_Q1C_COLL`| 4 | `Q1.C` | Coll Node | $(340, 220)$ | $(340, 220)$ | Collinear junction node | $(340, 220)$ |
| 11 | `W_COLL_C2` | 4 | Coll Node | `C2.1` | $(340, 220)$ | $(400, 220)$ | Straight horizontal along $Y = 220$ | $(340, 220)$ |
| 12 | `W_Q1E_TRUNK`| 5 | `Q1.E` | Emit Trunk Node | $(340, 300)$ | $(340, 340)$ | Straight vertical trunk | $(340, 340)$ |
| 13 | `W_EMIT_RE` | 5 | Emit Trunk Node | `Re.1` | $(340, 340)$ | $(320, 360)$ | Orthogonal: $(340, 340) \to (320, 340) \to (320, 360)$, `FlipCorner = false` | $(340, 340)$ |
| 14 | `W_EMIT_CE` | 5 | Emit Trunk Node | `Ce.1` | $(340, 340)$ | $(420, 360)$ | Orthogonal: $(340, 340) \to (420, 340) \to (420, 360)$, `FlipCorner = false` | $(340, 340)$ |
| 15 | `W_C2_OUT`  | 6 | `C2.2` | Output Node | $(480, 220)$ | $(540, 220)$ | Straight horizontal along $Y = 220$ | $(540, 220)$ |
| 16 | `W_OUT_RL`  | 6 | Output Node | `RL.1` | $(540, 220)$ | $(540, 280)$ | Straight vertical drop | $(540, 220)$ |
| 17 | `W_OUT_TEST`| 6 | Output Node | Test Port $v_o$ | $(540, 220)$ | $(580, 220)$ | Straight horizontal probe lead | $(540, 220)$ |
| 18 | `W_GND_TRUNK`| 0 | Rail Left $(80, 480)$ | Rail Right $(540, 480)$ | $(80, 480)$ | $(540, 480)$ | Straight horizontal trunk along $Y = 480$ | At all 5 T-drops |
| 19 | `W_VS_GND`  | 0 | `vs.neg` | GND Rail | $(80, 360)$ | $(80, 480)$ | Straight vertical drop | $(80, 480)$ |
| 20 | `W_RB2_GND` | 0 | `Rb2.2` | GND Rail | $(240, 400)$ | $(240, 480)$ | Straight vertical drop | $(240, 480)$ |
| 21 | `W_RE_GND`  | 0 | `Re.2` | GND Rail | $(320, 440)$ | $(320, 480)$ | Straight vertical drop | $(320, 480)$ |
| 22 | `W_CE_GND`  | 0 | `Ce.2` | GND Rail | $(420, 440)$ | $(420, 480)$ | Straight vertical drop | $(420, 480)$ |
| 23 | `W_RL_GND`  | 0 | `RL.2` | GND Rail | $(540, 360)$ | $(540, 480)$ | Straight vertical drop | $(540, 480)$ |
| 24 | `W_GND_PIN` | 0 | `GND.GND` | GND Rail | $(360, 480)$ | $(360, 480)$ | Direct bond to rail at $(360, 480)$ | None |

---

## 5. Verification Method

To independently verify this specification:

1. **Verify Desktop WinUI Solution Build**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected outcome*: 0 Errors.

2. **Verify Python Analytical Engine Tests**:
   ```powershell
   pytest services/engine/tests services/api/tests
   ```
   *Expected outcome*: 33 passed in ~4.8s.

3. **Verify Geometric Invariants**:
   - Check collinearity of Collector path: $R_c.X = 340$, $Q_1.Collector.X = 340$, drop wire $X = 340$. $\Delta X = 0$.
   - Check collinearity of Base path: $C_1.Y = 260$, Base Node $Y = 260$, $Q_1.Base.Y = 260$. $\Delta Y = 0$.
   - Check collinearity of Output path: Collector Node $Y = 220$, $C_2.Y = 220$, Output Node $Y = 220$, Test Port $Y = 220$. $\Delta Y = 0$.
   - Check $R_e$ to $C_e$ clearance: $420 - 320 = 100\text{ px}$. Text label margin to plate $\ge 18\text{ px}$, to lead $\ge 36\text{ px}$.
   - Check rail parallelism: $Y_{VCC} = 80$, $Y_{GND} = 480$, both horizontal with $dY/dX = 0$.
