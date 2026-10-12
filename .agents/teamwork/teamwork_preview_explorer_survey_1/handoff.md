# Engine Topology Explorer: Handoff Report for Requirement R1

## 1. Observation

### 1.1 Existing Solvers, Netlists, and Strategy Code
- **C# Client Architecture (`apps/desktop-winui/`)**:
  - `apps/desktop-winui/Services/CircuitApiService.cs` (lines 24–80): Contains `AnalyzeCircuitAsync(double vcc, double rb1, double rb2, double rc, double re, double rl, double c1, double c2, double ce, double beta, double freq)`. It sends a POST request with `circuit_type = "bjt_ce_amplifier"` to `http://127.0.0.1:8000/api/v1/analyze`. When the request fails (e.g. timeout or no server), lines 74–79 catch all exceptions and fall back to `GeneratePedagogicalSolution(...)`.
  - `apps/desktop-winui/Services/CircuitApiService.cs` (lines 148–375): `GeneratePedagogicalSolution(...)` hardcodes a 5-step textbook derivation for a Common-Emitter BJT amplifier:
    - Step 1: 基极戴维南等效与直流静态工作点 (Q-Point) 求解 ($V_{th}, R_{th}, I_{BQ}, I_{CQ}, V_{CEQ}$)
    - Step 2: 晶体管低频小信号微变等效参数 $r_{be}$ 计算 ($r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}}$)
    - Step 3: 中频段电压放大倍数 $A_v$ 推导与求解 ($A_v = -\frac{\beta (R_c \parallel R_L)}{r_{be}}$)
    - Step 4: 放大电路输入电阻 $R_i$ 与输出电阻 $R_o$ 计算 ($R_i = R_{b1} \parallel R_{b2} \parallel r_{be}, R_o = R_c$)
    - Step 5: 频率特性分析（密勒效应、极点时间常数与通频带 $BW$）($f_L, f_H, BW$)
  - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 1071–1105): `SolveCircuitAsync()` triggers analysis. Lines 1083–1100 call `SyncElementValuesToModel()` and extract only 11 specific numbers (`VccNumber`, `Rb1Number`, `Rb2Number`, `RcNumber`, `ReNumber`, `RlNumber`, `C1Number`, `C2Number`, `CeNumber`, `BetaNumber`, `FreqNumber`) to pass into `AnalyzeCircuitAsync`.
  - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 1107–1123): `SyncElementValuesToModel()` literally checks:
    ```csharp
    if (el.Id == "VCC") VccNumber = el.DisplayNumber;
    else if (el.Id == "Rb1") Rb1Number = el.DisplayNumber;
    else if (el.Id == "Rb2") Rb2Number = el.DisplayNumber;
    else if (el.Id == "Rc") RcNumber = el.DisplayNumber;
    else if (el.Id == "Re") ReNumber = el.DisplayNumber;
    else if (el.Id == "RL") RlNumber = el.DisplayNumber;
    else if (el.Id == "C1") C1Number = el.DisplayNumber;
    else if (el.Id == "C2") C2Number = el.DisplayNumber;
    else if (el.Id == "Ce") CeNumber = el.DisplayNumber;
    else if (el.Id == "Q1" || el.Id == "BJT") BetaNumber = el.DisplayNumber;
    ```
    If any component does not match these exact IDs, it is completely ignored, and default BJT parameters are used instead.

- **Python Analysis Engine (`services/engine/src/`)**:
  - `services/engine/src/pipeline.py` (lines 35–46): Contains an `AnalysisPipeline` with `DiodeAnalysisStrategy`, `BJTACSmallSignalStrategy`, `BJTDCBiasStrategy`, `MOSACSmallSignalStrategy`, `MOSDCBiasStrategy`, `LinearAnalysisStrategy`.
  - `services/engine/src/mna.py` (lines 56–287): Implements `MNABuilder` for symbolic MNA solving ($Ax = z$ via SymPy `LUsolve`).
  - `services/engine/src/strategies/linear.py` (lines 407–670): Contains symbolic derivation for RC Low-Pass filter and RC High-Pass filter using phasor impedance and transfer function $A_u(j\omega) = \frac{A_{um}}{1 + j\omega/\omega_H}$.
  - `services/engine/src/strategies/bjt_dc.py` and `bjt_ac.py`: Contains textbook derivations for BJT DC bias and AC small-signal parameters.
  - `services/engine/src/strategies/mos_dc.py` and `mos_ac.py`: Contains MOSFET DC bias and small-signal strategies.
  - Python tests (`services/engine/tests/` and `services/api/tests/`): 33 automated tests passing.
  - However, `services/api/main.py` is configured on port 3000 (see `start-api.bat`: `python -m uvicorn services.api.main:app --port 3000`), whereas `CircuitApiService.cs` points to `http://127.0.0.1:8000` and posts to `/api/v1/analyze`, which does not exist in FastAPI (FastAPI uses `/api/tasks/submit`). Hence, `CircuitApiService` ALWAYS catches an error and runs C# `GeneratePedagogicalSolution`.

### 1.2 Data Model: Netlist, Nodes, Components, Pins
- **In C# (`apps/desktop-winui/Models/CircuitElement.cs`)**:
  - `CircuitPin` (lines 37–53): Defines `Id`, `Name`, `RelativeX`, `RelativeY`, and `ConnectedNetId` (`-1`). Note: `ConnectedNetId` is never written or read anywhere in the C# solution.
  - `CircuitWire` (lines 55–158): Defines `Id`, `StartElementId`, `StartPinName`, `EndElementId`, `EndPinName`, `StartX`, `StartY`, `EndX`, `EndY`, `NetId` (never used), `FlipCorner`, `CustomMid`. Methods: `GetPathPoints()`, `HitTest()`.
  - `CircuitElement` (lines 160–360): Defines `Id`, `Name`, `DefinitionId`, `Category`, `Type` (`ElementType` enum with 24 variants), `DisplayNumber`, `UnitSymbol`, `X`, `Y`, `RotationDeg`, `Orientation`, `Pins`. Methods: `GetPinWorldPosition(pin)`, `GetPinWorldDirection(pin)`, `InitializeDefaultPins()`, `HitTest()`.
  - **Absence of Netlist**: There is no `Netlist`, `ElectricalNet`, or graph connectivity model in C#. `Wires` only tracks visual line endpoints, and `Elements` are independent blocks.
  - **Template Wire Disconnection**: In `AnalysisStudioViewModel.cs` (line 791 and line 1125), `ResetToDefaults()` clears `Wires` (`Wires.Clear()`). Instead of generating `CircuitWire` objects for the template, `CircuitCanvas.xaml.cs` (lines 448–615) draws the template wires using hardcoded Direct2D coordinates in `DrawCircuitWires()`.

### 1.3 MathFormulaView and LaTeX Formatting
- `apps/desktop-winui/Controls/MathFormulaView.xaml.cs` (lines 73–129):
  - Renders LaTeX by converting it to Unicode for Cambria Math via `FormatLatexToUnicode(string latex)`.
  - Replaces Greek letters: `\beta` $\to$ β, `\alpha` $\to$ α, `\pi` $\to$ π, `\tau` $\to$ τ, `\mu` $\to$ μ, `\Omega` $\to$ Ω.
  - **Missing Replacements Observed**:
    - `\omega` (omega) is NOT replaced! As a result, $\omega_0$ renders as `\omega₀`, $\omega_H$ as `\omega_H`.
    - `\Delta` is NOT replaced.
    - `\sqrt` is NOT replaced (renders raw `\sqrt(...)`).
    - `\pm` is NOT replaced (renders raw `\pm`).
    - `\le`, `\ge`, `\neq`, `\to`, `\infty`, `\angle`, `\circ` are NOT replaced.
- `apps/desktop-winui/Services/ScientificNotationFormatter.cs` (lines 12–55):
  - `ToLatexScientific(double value, int precision = 3)`: Converts numbers $\ge 1000$ or $< 0.001$ to `mantissa \times 10^{exponent}` and moderate numbers ($0.001 \le |v| < 1000$) to clean decimals.
  - `ToUnicodeScientific(double value, int precision = 3)`: Converts to `mantissa × 10^{exp}`.
  - Currently, this formatter is only applied to selected numbers in the hardcoded BJT solution in `CircuitApiService.cs`.

### 1.4 Test Infrastructure and Build Setup
- `apps/desktop-winui/CircuitAssistant.csproj`: Compiles cleanly using .NET 9 SDK (`dotnet build` exited with code 0, 0 warnings, 0 errors).
- No C# unit test project exists yet in `apps/`.
- Python unit tests (`pytest services/engine/tests services/api/tests`): 33 passed in 4.66s.

---

## 2. Logic Chain

1. **Root Cause of Acceptance Criterion 1 Violation**:
   - *Premise 1*: When a user draws an RC circuit (VS, R1, C1) on a blank canvas, `Elements` contains `{VS, R1, C1, GND}` and `Wires` contains 4 wires.
   - *Premise 2*: In `AnalysisStudioViewModel.cs` (lines 1083–1105), `SolveCircuitAsync()` calls `SyncElementValuesToModel()` which looks strictly for hardcoded element IDs `VCC`, `Rb1`, `Rb2`, `Rc`, `Re`, `RL`, `C1`, `C2`, `Ce`, `Q1`.
   - *Premise 3*: Since none of `VCC`, `Rb1`, `Rb2`, etc. are found, the parameters retain their initialized default values (12V, 51k, 15k, etc.).
   - *Premise 4*: `SolveCircuitAsync()` passes these values to `CircuitApiService.AnalyzeCircuitAsync()`, which executes `GeneratePedagogicalSolution()` and outputs 5 BJT steps ($I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}, A_v, R_i, R_o, \text{BW}$).
   - *Deduction*: The solver engine is entirely decoupled from the actual canvas topology. It ignores graph connectivity and forces a BJT solution regardless of what the user drew.

2. **Root Cause of Template Wire Inconsistency (Requirement R2 & R3 interaction)**:
   - *Premise 1*: `ResetToDefaults()` does not add wires to `Wires`; it leaves `Wires` empty.
   - *Premise 2*: `CircuitCanvas.xaml.cs` (line 448) only draws wires if `Wires.Count == 0` via a hardcoded rendering routine `DrawCircuitWires()`.
   - *Deduction*: Because the template has no wire objects, any automated netlist extraction would find zero connections unless the template initializes real `CircuitWire` objects. Therefore, R1 (Netlist extraction) and R2 (template model) must share the same canonical `CircuitWire` data model.

3. **Requirement for an Embedded C# Topology & Solving Engine**:
   - *Premise 1*: `start-winui.bat` launches the WinUI 3 desktop client standalone without starting Python or uvicorn.
   - *Premise 2*: A desktop EDA application must provide zero-latency, reliable, offline analysis without relying on external network ports or background processes.
   - *Premise 3*: The C# project targets .NET 9, which has modern high-performance math and graph capabilities.
   - *Deduction*: The primary Topology Classifier, Netlist Extractor, and Solving Strategy Engine must be implemented natively in C# (`apps/desktop-winui/Engine/`), ensuring that the desktop application can parse, classify, and solve all 4 categories independently and reliably.

---

## 3. Architecture Specification for Requirement R1

### 3.1 Data Model Architecture: `ElectricalNetlist`
Create `apps/desktop-winui/Engine/ElectricalNetlist.cs`:

```csharp
namespace CircuitAssistant.Engine;

public class ComponentPinRef
{
    public string ElementId { get; set; } = string.Empty;
    public string PinId { get; set; } = string.Empty; // e.g. "1", "2", "B", "C", "E", "pos", "neg"
    public string PinName { get; set; } = string.Empty;
}

public class ElectricalNet
{
    public int NetId { get; set; }
    public string Name { get; set; } = string.Empty; // "GND", "VCC", "IN", "OUT", "N1", "N2"
    public bool IsGround { get; set; }
    public bool IsPower { get; set; }
    public List<ComponentPinRef> ConnectedPins { get; set; } = [];
}

public class ElectricalNetlist
{
    public List<ElectricalNet> Nets { get; set; } = [];
    public Dictionary<string, CircuitElement> Elements { get; set; } = [];
    
    public ElectricalNet? GroundNet => Nets.FirstOrDefault(n => n.IsGround);
    public ElectricalNet? FindNetForPin(string elementId, string pinId);
}
```

### 3.2 Netlist Extractor: `NetlistExtractor`
Create `apps/desktop-winui/Engine/NetlistExtractor.cs`:
- **Step 1: Node Identification**: Each pin on each `CircuitElement` is a terminal vertex `(el.Id, pin.Id)`. Its world coordinates are calculated via `el.GetPinWorldPosition(pin)`.
- **Step 2: Wire Endpoint Clustered Snapping**:
  - Connect each `CircuitWire` start point `(StartX, StartY)` and end point `(EndX, EndY)` to the nearest element pin (within snap tolerance 8px) or to explicit `(StartElementId, StartPinName)` / `(EndElementId, EndPinName)`.
  - Handle T-junctions / Wire Taps: If wire B's endpoint has `EndPinName == "WireTap"` or lies on wire A's segment (distance $\le 5$px), merge wire A and wire B into the same electrical net.
- **Step 3: Disjoint-Set Union-Find**:
  - Merge all mutually connected pins and wires into equivalence classes (connected components).
- **Step 4: Net Labeling & Metadata**:
  - Net connected to any `ElementType.Ground` or `DefinitionId == "reference.ground"` $\to$ `NetId = 0`, `Name = "GND"`, `IsGround = true`.
  - Net connected to `DcVoltageSource` positive terminal with label `VCC` $\to$ `Name = "VCC"`, `IsPower = true`.
  - Signal input net connected to `AcVoltageSource` $\to$ `Name = "IN"`.
  - Output net $\to$ `Name = "OUT"`.
  - Back-annotate each `CircuitPin.ConnectedNetId` and `CircuitWire.NetId`.

### 3.3 Topology Classifier: `TopologyClassifier`
Create `apps/desktop-winui/Engine/TopologyClassifier.cs`:
Analyzes the `ElectricalNetlist` and classifies into 4 primary categories and sub-topologies:

| Category | Detection Criteria | Target Strategy |
| :--- | :--- | :--- |
| **First-Order RC Circuit** | 0 transistors, exactly 1 Capacitor, 0 Inductors, $\ge 1$ Resistor, $\ge 1$ Source | `FirstOrderRcStrategy` (Low-pass, High-pass, or DC charging) |
| **First-Order RL Circuit** | 0 transistors, 0 Capacitors, exactly 1 Inductor, $\ge 1$ Resistor, $\ge 1$ Source | `FirstOrderRlStrategy` (Low-pass, High-pass) |
| **Second-Order RLC Resonance** | 0 transistors, exactly 1 Capacitor, exactly 1 Inductor, $\ge 1$ Resistor | `SecondOrderRlcStrategy` (Series, Parallel) |
| **Transistor Amplifier (BJT)** | $\ge 1$ BJT (`BjtNpn` or `BjtPnp`) | `BjtAmplifierStrategy` (Voltage-divider CE, Fixed CE, CC/Emitter Follower, CB) |
| **Transistor Amplifier (MOSFET)** | $\ge 1$ MOSFET (`Mosfet*`, `Jfet*`) | `MosfetAmplifierStrategy` (Common-Source, Common-Drain) |
| **General Linear Network** | Multi-resistor/multi-source network without single canonical template | `MnaLinearStrategy` (SPICE MNA Matrix $\mathbf{G}\mathbf{v} + \mathbf{B}\mathbf{i} = \mathbf{J}$) |

### 3.4 Mathematical Formulations for the 4 Required Categories

#### 1. First-Order RC / RL Circuits
- **Thevenin Resistance $R_{eq}$**: Deactivate independent sources (voltage sources shorted, current sources open), compute equivalent resistance across the dynamic element's terminals.
- **Time Constant**:
  $$\tau = R_{eq} C \quad (\text{RC}) \qquad \text{or} \qquad \tau = \frac{L}{R_{eq}} \quad (\text{RL})$$
- **Three-Element Method (三要素法)**:
  - Differential equation: $\tau \frac{d f(t)}{dt} + f(t) = f(\infty)$
  - General solution: $f(t) = f(\infty) + [f(0^+) - f(\infty)] e^{-t/\tau}$
  - Initial value $f(0^+)$ (capacitor voltage continuity $u_C(0^+) = u_C(0^-)$, inductor current continuity $i_L(0^+) = i_L(0^-)$).
  - Steady-state value $f(\infty)$ (C opens, L shorts).
  - Transient term: $[f(0^+) - f(\infty)] e^{-t/\tau}$.
- **Transfer Function & Frequency Response**:
  - Low-pass RC: $H(s) = \frac{1}{1 + s R_{eq} C} = \frac{1}{1 + s\tau}$, $A_u(j\omega) = \frac{1}{1 + j\frac{\omega}{\omega_c}} = \frac{1}{1 + j\frac{f}{f_c}}$, $f_c = \frac{1}{2\pi R_{eq} C}$.
  - High-pass RC: $H(s) = \frac{s\tau}{1 + s\tau}$, $A_u(j\omega) = \frac{j\frac{f}{f_c}}{1 + j\frac{f}{f_c}}$, $f_c = \frac{1}{2\pi R_{eq} C}$.
  - Low-pass RL: $H(s) = \frac{1}{1 + s\tau}$, $f_c = \frac{R_{eq}}{2\pi L}$.
  - High-pass RL: $H(s) = \frac{s\tau}{1 + s\tau}$, $f_c = \frac{R_{eq}}{2\pi L}$.
- **Zero Fictitious Parameters Rule**: The LaTeX output must reference ONLY the actual components present in the netlist (e.g., $R_1, C_1$, $V_S$). All BJT parameters ($I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}$) are strictly suppressed.

#### 2. Second-Order RLC Resonance Circuits
- **Undamped Natural Angular Frequency**: $\omega_0 = \frac{1}{\sqrt{LC}}$, $f_0 = \frac{\omega_0}{2\pi}$.
- **Attenuation Constant $\alpha$**:
  - Series RLC: $\alpha = \frac{R}{2L}$
  - Parallel RLC: $\alpha = \frac{1}{2RC}$
- **Quality Factor $Q$**:
  - Series RLC: $Q = \frac{\omega_0 L}{R} = \frac{1}{\omega_0 CR} = \frac{1}{R}\sqrt{\frac{L}{C}} = \frac{\omega_0}{2\alpha}$
  - Parallel RLC: $Q = \frac{R}{\omega_0 L} = \omega_0 CR = R\sqrt{\frac{C}{L}} = \frac{\omega_0}{2\alpha}$
- **Characteristic Equation & Roots**:
  $$s^2 + 2\alpha s + \omega_0^2 = 0 \implies s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}$$
- **Damping Regimes**:
  - Overdamped ($\alpha > \omega_0$, $Q < 0.5$): Two distinct negative real roots $s_{1, 2} < 0$.
  - Critically damped ($\alpha = \omega_0$, $Q = 0.5$): Two equal real roots $s_1 = s_2 = -\alpha$.
  - Underdamped ($\alpha < \omega_0$, $Q > 0.5$): Complex conjugate roots $s_{1, 2} = -\alpha \pm j\omega_d$, where $\omega_d = \sqrt{\omega_0^2 - \alpha^2}$.

#### 3. Transistor Amplifier Circuits (BJT / MOSFET)
- **DC Operating Point (Q-Point)**:
  - BJT Voltage-Divider CE: $V_{th} = V_{CC} \frac{R_{b2}}{R_{b1} + R_{b2}}$, $R_{th} = R_{b1} \parallel R_{b2}$.
  - $I_{BQ} = \frac{V_{th} - V_{BE}}{R_{th} + (1+\beta)R_e}$, $I_{CQ} = \beta I_{BQ}$, $V_{CEQ} = V_{CC} - I_{CQ}R_c - I_{EQ}R_e$.
  - MOSFET CS: $V_{GSQ} = V_{DD} \frac{R_{g2}}{R_{g1} + R_{g2}} - I_{DQ}R_s$, $I_{DQ} = \frac{1}{2}\mu_n C_{ox}\frac{W}{L}(V_{GSQ} - V_{th})^2$.
- **Small-Signal Parameters**:
  - BJT: $r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} \approx 200\,\Omega + (1+\beta)\frac{26\,\text{mV}}{I_{CQ}}$.
  - MOSFET: $g_m = \frac{2I_{DQ}}{V_{GSQ} - V_{th}}$.
- **AC Dynamic Parameters**:
  - Midband voltage gain: $A_v = -\frac{\beta (R_c \parallel R_L)}{r_{be}}$ (or unbypassed: $-\frac{\beta (R_c \parallel R_L)}{r_{be} + (1+\beta)R_e}$).
  - Input resistance: $R_i = R_{b1} \parallel R_{b2} \parallel r_{be}$.
  - Output resistance: $R_o = R_c$.
  - Frequency response: SCTC for $f_L$, Miller effect ($C_M = C_\mu (1+|A_v|)$) for $f_H$, bandwidth $BW = f_H - f_L$.

#### 4. General Linear Networks (MNA Formulation)
- **SPICE Standard Formulation**:
  $$\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix} \begin{bmatrix} \mathbf{v} \\ \mathbf{j} \end{bmatrix} = \begin{bmatrix} \mathbf{i} \\ \mathbf{e} \end{bmatrix}$$
- Format the matrix system into clean LaTeX `\begin{bmatrix} ... \end{bmatrix}` for both symbolic formulation and numerical evaluation.
- Solve via LU decomposition and display node voltages and branch currents.

### 3.5 MathFormulaView Unicode Converter Enhancements
Modify `apps/desktop-winui/Controls/MathFormulaView.xaml.cs` to add:
- Greek symbols: `\omega` $\to$ ω, `\Omega` $\to$ Ω, `\Delta` $\to$ Δ.
- Math symbols: `\sqrt{...}` $\to$ √( ... ), `\pm` $\to$ ±, `\le` $\to$ ≤, `\ge` $\to$ ≥, `\neq` $\to$ ≠, `\to` $\to$ →, `\infty` $\to$ ∞, `\angle` $\to$ ∠, `\circ` $\to$ °.
- Extended subscripts: Add `ₕ`, `ₖ`, `ₗ`, `ₙ`, `ᵣ`, `ᵤ`, `ᵥ`, `ₓ` to `ToSubscript`.
- Support multi-row matrices `\begin{bmatrix} ... \end{bmatrix}` with formatted column alignments.

---

## 4. Caveats
1. **Component Orientation / Pin World Coordinates**: In `CircuitCanvas.xaml.cs`, `HitTest` and `GetPinWorldPosition` compute coordinates based on element center and rotation. Snapping during netlist extraction must account for rotation angles ($0^\circ, 90^\circ, 180^\circ, 270^\circ$).
2. **Template Wires**: The existing `CommonEmitter_Amp.sch` relies on `CircuitCanvas.DrawCircuitWires` when `Wires.Count == 0`. When implementing R1, `ResetToDefaults()` must be updated to populate canonical `CircuitWire` objects so that `NetlistExtractor` has actual wire data to parse.
3. **Multi-Stage Circuits**: Multi-stage cascaded BJT amplifiers or complex feedback loops will fall back to `MnaLinearStrategy` if not recognized by the single-stage classifier. This is by design and provides a safe fallback.

---

## 5. Conclusion
1. The requirement to "thoroughly abolish hardcoded transistor logic" is directly addressed by replacing the fixed parameter forwarding in `AnalysisStudioViewModel.cs` (lines 1083–1105) and `CircuitApiService.cs` with an end-to-end `NetlistExtractor` $\to$ `TopologyClassifier` $\to$ `StrategyEngine` pipeline.
2. The 4 required circuit classes (First-order RC/RL, Second-order RLC resonance, Transistor amplifiers, and General Linear MNA networks) have distinct topological signatures and unambiguous mathematical expressions.
3. Implementing this engine natively in C# ensures instant, zero-dependency, crash-free execution in the WinUI 3 desktop application, directly satisfying all acceptance criteria.

---

## 6. Verification Method

### Independent Verification Steps
1. **Build Verification**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected Result*: Build succeeded, 0 warnings, 0 errors.

2. **Acceptance Test 1 — RC Circuit (No Transistors)**:
   - Create a blank project in the WinUI 3 app.
   - Place `vs` (AcVoltageSource), `R1` (Resistor), `C1` (Capacitor), `GND` (Ground).
   - Connect `vs` $\to$ `R1` $\to$ `C1` $\to$ `GND`.
   - Click "求解与理论分析".
   - *Pass Condition*: Derivation outputs RC differential equation, $\tau = R_1 C_1$, $f_c = \frac{1}{2\pi R_1 C_1}$, and Bode response. Inspection of steps confirms ZERO presence of $I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}$ or any transistor parameter.
   - *Invalidation Condition*: Any mention of BJT parameters or Q-point in the generated steps.

3. **Acceptance Test 2 — Transistor Amplifier**:
   - Load `CommonEmitter_Amp.sch`.
   - Click "求解与理论分析".
   - *Pass Condition*: Correctly identifies BJT Voltage-Divider Common-Emitter Amplifier, outputs Q-point ($I_{BQ}, I_{CQ}, V_{CEQ}$), $r_{be}$, $A_v$, $R_i$, $R_o$, and bandwidth $BW$.

4. **Acceptance Test 3 — Scientific Notation & Formula Rendering**:
   - Set resistor to $51\text{ k}\Omega$ ($51000\,\Omega$) and capacitor to $10\,\mu\text{F}$ ($10^{-5}\text{ F}$).
   - Inspect MathFormulaView rendering in the deduction drawer.
   - *Pass Condition*: Numerical values appear as $5.10 \times 10^{4}$ and $1.00 \times 10^{-5}$, $\omega$ renders as ω (not `\omega`), and subscripts/superscripts are properly formatted.

5. **Python Regression Verification**:
   ```powershell
   pytest services/engine/tests services/api/tests
   ```
   *Expected Result*: All 33 existing tests pass.
