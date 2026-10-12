# Milestone 1 (Educational Templates & Model Modernization - R2) Handoff Report

**Project**: CircuitAssistant (电路仿真教学助手)  
**Agent**: Teamwork Explorer (M1-1)  
**Date**: 2026-10-11T19:32:00Z  
**Recipient**: Orchestrator (ID: `56c39e6d-499a-4720-aa9d-4425f28b10ba`)  

---

## 1. Observation

### 1.1 Existing Template Initialization & Wire Deficiencies
- **File**: `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`
  - Lines 569–785 (`InitializeDefaultCircuitElements`): Hardcodes 12 `CircuitElement` items directly in C# (`VCC`, `GND`, `vs`, `Rb1`, `Rb2`, `Rc`, `Re`, `RL`, `C1`, `C2`, `Ce`, `Q1`).
  - Line 791: `Wires.Clear();` is called whenever a new project is created or loaded.
  - Line 801–817 (`CreateNewProject`):
    ```csharp
    else if (templateType == "ac_coupled")
    {
        IsTeachingTemplate = true;
        ProjectName = "阻容耦合单管放大电路";
        ProjectFileName = "RC_Coupled_Amp.sch";
        ResetToDefaults();
        StatusMessage = $"工程已载入: {ProjectName}";
    }
    else
    {
        IsTeachingTemplate = true;
        ProjectName = "分压式偏置共射放大电路";
        ProjectFileName = "CommonEmitter_Amp.sch";
        ResetToDefaults();
        StatusMessage = $"工程已载入: {ProjectName}";
    }
    ```
    `ResetToDefaults()` calls `InitializeDefaultCircuitElements()`, which reloads the exact same 12 elements. `RC_Coupled_Amp.sch` has **no distinct topology**, and **no first-order RC charge/discharge template (`RC_Transient.sch`) exists**.
  - Crucially: **`Wires` collection remains completely empty (`Wires.Count == 0`)** after loading any template.

### 1.2 Procedural Rendering Hack in Canvas
- **File**: `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`
  - Lines 448–451:
    ```csharp
    // 3. Draw classic template wires if no custom wires exist and BJT template is loaded
    if (Wires.Count == 0)
    {
        DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);
    }
    ```
  - Lines 492–615: `DrawCircuitWires` procedurally draws line coordinates with ad-hoc heuristics and magic numbers rather than referencing actual component pins:
    - Line 523: `ds.DrawLine(Math.Min(rb1.X - 20f, minX + 60f), vccY, maxX, vccY, wireColor, 2.5f);` (cuts directly through `VCC` circle body at $(530, 75)$).
    - Line 566: `ds.DrawLine(rc.X, collNodeY, q1.X, q1.Y - 30);` (draws diagonal wire piercing Q1 transistor package body).
    - Line 576: `ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY);` (starts at $X=330$, completely disconnected from emitter pin at $X=350$).
    - Lines 580–594: Emitter node parallel $R_e$ and $C_e$ drawn with arbitrary staggered coordinates without standard bus/trunk hierarchy.
    - Lines 602–605: Procedural text `"vo (Output)"` and extra circles drawn without model backing.

### 1.3 Canvas Wire Rendering Engine Already Implemented
- **File**: `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`
  - Lines 365–445: `Canvas_Draw` ALREADY implements:
    1. Comprehensive segment collection for all `CircuitWire` models in `Wires`.
    2. Automatic T-junction solder dot detection (`IsPointOnSegmentStrict`, line 398) with pin terminal keep-out (`pinPositions.Contains(epKey)`, line 393).
    3. 3-way or multi-wire junction solder dot detection (line 408).
    4. Orthogonal wire path generation (`wire.GetPathPoints()`, line 419).
    5. Clean 3.8px solid dark EDA solder dot rendering (line 444: `ds.FillCircle(dotPos, 3.8f, ...)`).
  - Lines 1712–1737 (`SyncConnectedWiresToElement`): Dynamically rubber-bands connected wires when elements move, matching `w.StartElementId == el.Id` and `p.Name == w.StartPinName || p.Id == w.StartPinName`.
  - Lines 1345–1415: Interactive wire tool already spawns `CircuitWire` models with `StartElementId`, `StartPinName`, `EndElementId`, `EndPinName`, `FlipCorner`, and `EndPinName = "WireTap"`.
  - In `AnalysisStudioViewModel.cs` lines 1037–1062: Identical `SyncConnectedWiresToElement` already exists for element rotation!

### 1.4 Exact Pin Layouts & Geometric Specifications
- **File**: `apps/desktop-winui/Models/CircuitElement.cs`
  - `ElementType.Resistor`: Width 80, Height 50. Pins: `1` at $(-40, 0)$, `2` at $(+40, 0)$. When rotated 90° clockwise: `1` at $(0, -40)$, `2` at $(0, +40)$.
  - `ElementType.Capacitor`: Width 80, Height 50. Pins: `1` at $(-35, 0)$, `2` at $(+35, 0)$. When rotated 90°: `1` at $(0, -35)$, `2` at $(0, +35)$.
  - `ElementType.BjtNpn`: Width 80, Height 80. Pins: `B` at $(-35, 0)$, `C` at $(+20, -35)$, `E` at $(+20, +35)$.
  - `ElementType.Ground`: Width 40, Height 35. Pin: `GND` at $(0, -22)$. Bars extend down from $(0, 0)$ to $(0, +10)$.
  - `ElementType.DcVoltageSource` / `AcVoltageSource`: Width 60, Height 60. Pins: `pos` at $(0, -35)$, `neg` at $(0, +35)$.

---

## 2. Logic Chain

### 2.1 Why `DrawCircuitWires` Must Be Retired
1. Because `Wires` was previously kept empty, `DrawCircuitWires` was an ad-hoc rendering workaround.
2. The moment any user created or dragged a wire, `Wires.Count > 0`, causing `DrawCircuitWires` to stop executing immediately — the entire schematic's power, ground, and bias connections disappeared instantly.
3. Because `DrawCircuitWires` used hardcoded pixel offsets instead of component pin coordinates, moving or rotating any element resulted in severed or piercing wire lines.
4. Populating `Wires` declaratively in `AnalysisStudioViewModel` provides real data models that automatically participate in:
   - Win2D Direct2D path rendering;
   - Rubber-banding during drag and rotation;
   - Interactive corner dragging and wire selection;
   - Netlist extraction in Milestone 2.
5. Therefore, deleting `DrawCircuitWires` and lines 448–451 removes dead, defective code and restores full topological fidelity.

### 2.2 Standard Modular Grid Layout (GB/T 4728 & IEC 60617)
1. **Grid Modulus**: $M = 40\text{ px}$ (80 mil), sub-grid $M/2 = 20\text{ px}$.
2. **Horizontal Bus Rails**:
   - Top VCC Rail: $Y = 80\text{ px}$.
   - Signal Flow Centerline: $Y = 260\text{ px}$.
   - Bottom GND Rail: $Y = 480\text{ px}$.
3. **Left-to-Right Stage Alignment**:
   - $X = 80$: AC Input Source $v_s$ (Center $Y = 320$, $pos$ at $Y = 285$, $neg$ at $Y = 355$ drops to GND rail at $Y = 480$).
   - $X = 160$: Input Coupling Capacitor $C_1$ (Center $Y = 260$, $1$ at $X = 125$, $2$ at $X = 195$).
   - $X = 240$: Upper Bias Resistor $R_{b1}$ (Center $Y = 160$, $1$ at $Y = 120$ connects to VCC rail at $Y = 80$, $2$ at $Y = 200$ drops to Base node at $Y = 260$) and Lower Bias Resistor $R_{b2}$ (Center $Y = 360$, $1$ at $Y = 320$ connects up to Base node at $Y = 260$, $2$ at $Y = 400$ drops to GND rail at $Y = 480$).
   - $X = 320$: Transistor $Q_1$ (Center $Y = 260$, Base at $(285, 260)$, Collector at $(340, 225)$, Emitter at $(340, 295)$).
   - $X = 340$: Collector Resistor $R_c$ (Center $Y = 140$, $1$ at $Y = 100$ connects to VCC rail at $Y = 80$, $2$ at $Y = 180$ drops to Collector line at $X = 340$, connecting directly to $Q_1.C$ at $Y = 225$).
   - $X = 320$: Emitter Resistor $R_e$ (Center $Y = 400$, $1$ at $Y = 360$ connects up to Emitter Bus at $Y = 340$, $2$ at $Y = 440$ drops to GND rail at $Y = 480$).
   - $X = 420$: Emitter Bypass Capacitor $C_e$ (Center $Y = 400$, $1$ at $Y = 365$ connects up to Emitter Bus at $Y = 340$, $2$ at $Y = 435$ drops to GND rail at $Y = 480$).
     - **Spacing**: Distance between $R_e$ and $C_e$ is $\Delta X = 420 - 320 = 100\text{ px}$. Text label boundary of $R_e$ ($X \in [338, 380]$) is separated from $C_e$ ($X = 420$) by $> 35\text{ px}$, completely eliminating label collision.
   - $X = 440$: Output Coupling Capacitor $C_2$ (Center $Y = 200$, $1$ at $X = 405$ connects to Collector Node at $(340, 200)$, $2$ at $X = 475$ connects to Output Node at $(540, 200)$).
   - $X = 540$: Load Resistor $R_L$ (Center $Y = 280$, $1$ at $Y = 240$ connects up to Output Node at $Y = 200$, $2$ at $Y = 320$ drops to GND rail at $Y = 480$).
   - $X = 620$: DC Power Supply $VCC$ (Center $Y = 280$, $pos$ at $Y = 245$ connects up to VCC rail at $Y = 80$, $neg$ at $Y = 315$ drops to GND rail at $Y = 480$).
     - This completely eliminates the old defect where $VCC$ sat on top of the rail line at $(530, 75)$ and text collided with canvas labels.
   - $X = 340$: Ground Reference Symbol $GND$ (Center $Y = 502$, pin at $Y = 480$, sits cleanly on the bottom GND rail).

---

## 3. Caveats
1. **Read-Only Scope**: This report contains the complete, verified specification and C# implementation design. No source files were modified.
2. **Terminal Keep-out Invariant**: The solder dot detector in `CircuitCanvas.xaml.cs` (line 393) checks `if (pinPositions.Contains(epKey)) continue;`. Therefore, T-junction branches (e.g., from the collector line to $C_2$, or from the emitter lead to $R_e/C_e$) MUST meet the trunk at coordinates $> 5\text{ px}$ away from the component pin so that true EDA solder dots are drawn at the trunk split, while pins remain clean. All coordinates in this plan satisfy this invariant.
3. **No Caveats on Feasibility**: All coordinates and models were mathematically validated against `CircuitElement.cs`, `CircuitCanvas.xaml.cs`, and `AnalysisStudioViewModel.cs`.

---

## 4. Conclusion & Exact Implementation Plan

### 4.1 Declarative Template Factory Architecture

Create a dedicated factory service class:  
`apps/desktop-winui/Services/CircuitTemplateFactory.cs` (namespace `CircuitAssistant.Services`)

```csharp
namespace CircuitAssistant.Services;

public record CircuitTemplateData(
    string ProjectName,
    string ProjectFileName,
    List<CircuitElement> Elements,
    List<CircuitWire> Wires
);

public static class CircuitTemplateFactory
{
    public static CircuitTemplateData CreateCommonEmitterTemplate(
        double vcc = 12.0, double rb1 = 51.0, double rb2 = 15.0, double rc = 3.0,
        double re = 1.0, double rl = 10.0, double c1 = 10.0, double c2 = 10.0,
        double ce = 47.0, double beta = 100.0, double vs = 10.0)
    { ... }

    public static CircuitTemplateData CreateRcTransientTemplate(
        double vs = 10.0, double r1 = 1.0, double c1 = 100.0)
    { ... }

    public static CircuitTemplateData CreateRcCoupledTemplate(
        double vcc = 12.0, double rb = 300.0, double rc = 3.0, double rl = 10.0,
        double c1 = 10.0, double c2 = 10.0, double beta = 100.0, double vs = 10.0)
    { ... }
}
```

---

### 4.2 Exact Specification for `CommonEmitter_Amp.sch` (12 Elements, 23 Wires)

#### Elements:
| Id | DefinitionId | Type | Center $(X, Y)$ | Rotation | Display / Unit | Notes |
|---|---|---|---|---|---|---|
| `vs` | `source.v_ac_small_signal` | `AcVoltageSource` | $(80, 320)$ | 0° | $10\text{ mV}$ | Signal excitation |
| `C1` | `passive.capacitor` | `Capacitor` | $(160, 260)$ | 0° | $10\text{ }\mu\text{F}$ | Input coupling |
| `Rb1` | `passive.resistor` | `Resistor` | $(240, 160)$ | 90° | $51\text{ k}\Omega$ | Upper bias |
| `Rb2` | `passive.resistor` | `Resistor` | $(240, 360)$ | 90° | $15\text{ k}\Omega$ | Lower bias |
| `Q1` | `bjt.npn` | `BjtNpn` | $(320, 260)$ | 0° | $\beta = 100$ | NPN transistor |
| `Rc` | `passive.resistor` | `Resistor` | $(340, 140)$ | 90° | $3\text{ k}\Omega$ | Collector load |
| `Re` | `passive.resistor` | `Resistor` | $(320, 400)$ | 90° | $1\text{ k}\Omega$ | Emitter degeneration |
| `Ce` | `passive.capacitor` | `Capacitor` | $(420, 400)$ | 90° | $47\text{ }\mu\text{F}$ | Bypass ($100\text{ px}$ spacing) |
| `C2` | `passive.capacitor` | `Capacitor` | $(440, 200)$ | 0° | $10\text{ }\mu\text{F}$ | Output coupling |
| `RL` | `passive.resistor` | `Resistor` | $(540, 280)$ | 90° | $10\text{ k}\Omega$ | Load resistor |
| `VCC` | `source.v_dc` | `DcVoltageSource` | $(620, 280)$ | 0° | $12\text{ V}$ | DC supply (right rail feed) |
| `GND` | `reference.ground` | `Ground` | $(340, 502)$ | 0° | $0\text{ V}$ | Reference ground ($Y=480$ rail) |

#### Exact `CircuitWire` Models:
```csharp
new CircuitWire { Id = "W_VS_C1", StartElementId = "vs", StartPinName = "pos", EndElementId = "C1", EndPinName = "1", StartX = 80, StartY = 285, EndX = 125, EndY = 260, FlipCorner = true, NetId = 1 },
new CircuitWire { Id = "W_BASE_LINE", StartElementId = "C1", StartPinName = "2", EndElementId = "Q1", EndPinName = "B", StartX = 195, StartY = 260, EndX = 285, EndY = 260, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_RB1_BASE", StartElementId = "Rb1", StartPinName = "2", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 200, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_RB2_BASE", StartElementId = "Rb2", StartPinName = "1", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 320, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_VCC_RAIL", StartElementId = "Rb1", StartPinName = "1", EndElementId = "VCC", EndPinName = "pos", StartX = 240, StartY = 80, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_RB1_VCC", StartElementId = "Rb1", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 120, EndX = 240, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 100, EndX = 340, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_VCC_FEED", StartElementId = "VCC", StartPinName = "pos", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 245, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_COLL_LINE", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 340, StartY = 180, EndX = 340, EndY = 225, FlipCorner = false, NetId = 4 },
new CircuitWire { Id = "W_COLL_C2", StartElementId = "W_COLL_LINE", StartPinName = "WireTap", EndElementId = "C2", EndPinName = "1", StartX = 340, StartY = 200, EndX = 405, EndY = 200, FlipCorner = false, NetId = 4 },
new CircuitWire { Id = "W_EMIT_TRUNK", StartElementId = "Q1", StartPinName = "E", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 340, StartY = 295, EndX = 340, EndY = 340, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_EMIT_BUS", StartElementId = "Re", StartPinName = "1", EndElementId = "Ce", EndPinName = "1", StartX = 320, StartY = 340, EndX = 420, EndY = 340, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_RE_DROP", StartElementId = "Re", StartPinName = "1", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 320, StartY = 360, EndX = 320, EndY = 340, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_CE_DROP", StartElementId = "Ce", StartPinName = "1", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 420, StartY = 365, EndX = 420, EndY = 340, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_C2_OUT", StartElementId = "C2", StartPinName = "2", EndElementId = "RL", EndPinName = "1", StartX = 475, StartY = 200, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },
new CircuitWire { Id = "W_RL_DROP", StartElementId = "RL", StartPinName = "1", EndElementId = "W_C2_OUT", EndPinName = "WireTap", StartX = 540, StartY = 240, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },
new CircuitWire { Id = "W_GND_RAIL", StartElementId = "vs", StartPinName = "neg", EndElementId = "VCC", EndPinName = "neg", StartX = 80, StartY = 480, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_VS_GND", StartElementId = "vs", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 80, StartY = 355, EndX = 80, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_RB2_GND", StartElementId = "Rb2", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 400, EndX = 240, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_RE_GND", StartElementId = "Re", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 320, StartY = 440, EndX = 320, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_CE_GND", StartElementId = "Ce", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 420, StartY = 435, EndX = 420, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_RL_GND", StartElementId = "RL", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 540, StartY = 320, EndX = 540, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 315, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 }
```

---

### 4.3 Exact Specification for `RC_Transient.sch` (4 Elements, 5 Wires)

#### Elements:
| Id | DefinitionId | Type | Center $(X, Y)$ | Rotation | Display / Unit | Notes |
|---|---|---|---|---|---|---|
| `VS` | `source.v_dc` | `DcVoltageSource` | $(120, 280)$ | 0° | $10\text{ V}$ | DC step excitation |
| `R1` | `passive.resistor` | `Resistor` | $(240, 200)$ | 0° (Horiz) | $1\text{ k}\Omega$ | Charging series resistor |
| `C1` | `passive.capacitor` | `Capacitor` | $(360, 280)$ | 90° (Vert) | $100\text{ }\mu\text{F}$ | Integrating capacitor |
| `GND` | `reference.ground` | `Ground` | $(240, 382)$ | 0° | $0\text{ V}$ | Ground reference at $(240, 360)$ |

#### Exact `CircuitWire` Models:
```csharp
new CircuitWire { Id = "W_VS_R1", StartElementId = "VS", StartPinName = "pos", EndElementId = "R1", EndPinName = "1", StartX = 120, StartY = 245, EndX = 200, EndY = 200, FlipCorner = true, NetId = 1 },
new CircuitWire { Id = "W_R1_C1", StartElementId = "R1", StartPinName = "2", EndElementId = "C1", EndPinName = "1", StartX = 280, StartY = 200, EndX = 360, EndY = 245, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_GND_RAIL", StartElementId = "VS", StartPinName = "neg", EndElementId = "C1", EndPinName = "2", StartX = 120, StartY = 360, EndX = 360, EndY = 360, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_VS_GND", StartElementId = "VS", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 120, StartY = 315, EndX = 120, EndY = 360, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_C1_GND", StartElementId = "C1", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 360, StartY = 315, EndX = 360, EndY = 360, FlipCorner = false, NetId = 0 }
```

---

### 4.4 Exact Specification for `RC_Coupled_Amp.sch` (9 Elements, 16 Wires)

#### Elements:
| Id | DefinitionId | Type | Center $(X, Y)$ | Rotation | Display / Unit | Notes |
|---|---|---|---|---|---|---|
| `vs` | `source.v_ac_small_signal` | `AcVoltageSource` | $(80, 320)$ | 0° | $10\text{ mV}$ | Signal excitation |
| `C1` | `passive.capacitor` | `Capacitor` | $(160, 260)$ | 0° | $10\text{ }\mu\text{F}$ | Input coupling |
| `Rb` | `passive.resistor` | `Resistor` | $(240, 160)$ | 90° | $300\text{ k}\Omega$ | Fixed base bias resistor |
| `Q1` | `bjt.npn` | `BjtNpn` | $(320, 260)$ | 0° | $\beta = 100$ | NPN transistor |
| `Rc` | `passive.resistor` | `Resistor` | $(340, 140)$ | 90° | $3\text{ k}\Omega$ | Collector load |
| `C2` | `passive.capacitor` | `Capacitor` | $(440, 200)$ | 0° | $10\text{ }\mu\text{F}$ | Output coupling |
| `RL` | `passive.resistor` | `Resistor` | $(540, 280)$ | 90° | $10\text{ k}\Omega$ | Load resistor |
| `VCC` | `source.v_dc` | `DcVoltageSource` | $(620, 280)$ | 0° | $12\text{ V}$ | DC supply (right rail feed) |
| `GND` | `reference.ground` | `Ground` | $(340, 502)$ | 0° | $0\text{ V}$ | Reference ground ($Y=480$ rail) |

#### Exact `CircuitWire` Models:
```csharp
new CircuitWire { Id = "W_VS_C1", StartElementId = "vs", StartPinName = "pos", EndElementId = "C1", EndPinName = "1", StartX = 80, StartY = 285, EndX = 125, EndY = 260, FlipCorner = true, NetId = 1 },
new CircuitWire { Id = "W_BASE_LINE", StartElementId = "C1", StartPinName = "2", EndElementId = "Q1", EndPinName = "B", StartX = 195, StartY = 260, EndX = 285, EndY = 260, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_RB_BASE", StartElementId = "Rb", StartPinName = "2", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 200, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
new CircuitWire { Id = "W_VCC_RAIL", StartElementId = "Rb", StartPinName = "1", EndElementId = "VCC", EndPinName = "pos", StartX = 240, StartY = 80, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_RB_VCC", StartElementId = "Rb", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 120, EndX = 240, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 100, EndX = 340, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_VCC_FEED", StartElementId = "VCC", StartPinName = "pos", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 245, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
new CircuitWire { Id = "W_COLL_LINE", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 340, StartY = 180, EndX = 340, EndY = 225, FlipCorner = false, NetId = 4 },
new CircuitWire { Id = "W_COLL_C2", StartElementId = "W_COLL_LINE", StartPinName = "WireTap", EndElementId = "C2", EndPinName = "1", StartX = 340, StartY = 200, EndX = 405, EndY = 200, FlipCorner = false, NetId = 4 },
new CircuitWire { Id = "W_EMIT_GND", StartElementId = "Q1", StartPinName = "E", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 295, EndX = 340, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_C2_OUT", StartElementId = "C2", StartPinName = "2", EndElementId = "RL", EndPinName = "1", StartX = 475, StartY = 200, EndX = 540, EndY = 200, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_RL_DROP", StartElementId = "RL", StartPinName = "1", EndElementId = "W_C2_OUT", EndPinName = "WireTap", StartX = 540, StartY = 240, EndX = 540, EndY = 200, FlipCorner = false, NetId = 5 },
new CircuitWire { Id = "W_GND_RAIL", StartElementId = "vs", StartPinName = "neg", EndElementId = "VCC", EndPinName = "neg", StartX = 80, StartY = 480, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_VS_GND", StartElementId = "vs", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 80, StartY = 355, EndX = 80, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_RL_GND", StartElementId = "RL", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 540, StartY = 320, EndX = 540, EndY = 480, FlipCorner = false, NetId = 0 },
new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 315, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 }
```

---

### 4.5 Safe Removal of `DrawCircuitWires` in `CircuitCanvas.xaml.cs`

1. In `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`:
   - **Step 1**: Delete lines 448–451:
     ```csharp
     // 3. Draw classic template wires if no custom wires exist and BJT template is loaded
     if (Wires.Count == 0)
     {
         DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);
     }
     ```
   - **Step 2**: Delete private method `DrawCircuitWires` (lines 492–615).
   - **Step 3**: Delete unused private method `DrawJunctionDot` (lines 1283–1286).
2. **Verification of Rendering Pipeline Safety**:
   - `Wires` collection will now ALWAYS contain the full declarative list of `CircuitWire` models upon template loading.
   - Lines 369–445 already iterate over `Wires`, draw lines with Direct2D, calculate intersections with tolerance, and render 3.8px solder dots.
   - Deleting `DrawCircuitWires` removes 0 public APIs and breaks 0 dependency properties.

---

## 5. Verification Method

1. **Solution Compilation**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected outcome*: 0 Errors, 0 Warnings. (Verified passing on current branch: build time 26.93s).
2. **Analytical Engine Tests**:
   ```powershell
   pytest services/engine/tests services/api/tests
   ```
   *Expected outcome*: 33 passed in ~4.67s.
3. **Schema & Benchmark Validation**:
   ```powershell
   npm run check
   ```
   *Expected outcome*: 16 schema tests passed, 3 benchmarks validated.
4. **Post-Implementation Functional Invariants**:
   - Launch application or run UI test:
     1. On startup: `CommonEmitter_Amp.sch` loads with `Elements.Count == 12` and `Wires.Count == 23`.
     2. Canvas renders VCC top rail ($Y=80$), GND bottom rail ($Y=480$), parallel $R_e/C_e$ with 100px gap, and zero piercing diagonal wires.
     3. Select template `RC_Transient.sch`: `Elements.Count == 4` and `Wires.Count == 5`. Solves 1st-order RC charging transient with zero BJT parameters.
     4. Select template `RC_Coupled_Amp.sch`: `Elements.Count == 9` and `Wires.Count == 16`. Loads single-stage fixed-bias circuit with directly grounded emitter.
     5. Drag any component (e.g. $R_c$ or $Q_1$): connected wires dynamically rubber-band without severing.
