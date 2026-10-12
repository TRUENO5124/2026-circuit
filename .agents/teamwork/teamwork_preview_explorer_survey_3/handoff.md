# Requirement R3 Architecture Investigation & Survey Report

**Explorer**: Canvas Routing Explorer (`teamwork_preview_explorer_survey_3`)  
**Mission**: Requirement R3 (Industrial EDA multi-branch wire routing & render architecture refactoring) and solution compilation/stability survey  
**Date**: 2026-10-12  

---

## 1. Observation

### 1.1 Project Structure and Build System
- **Desktop Application Project**: [`apps/desktop-winui/CircuitAssistant.csproj`](file:///d:/BJTU/2026年大创/apps/desktop-winui/CircuitAssistant.csproj)
  - `TargetFramework`: `net9.0-windows10.0.26100.0`
  - `TargetPlatformMinVersion`: `10.0.17763.0`
  - Dependencies:
    - `Microsoft.WindowsAppSDK` (v2.3.9+)
    - `Microsoft.Graphics.Win2D` (v1.4.0) — Direct2D hardware accelerated rendering
    - `CommunityToolkit.Mvvm` (v8.4.0) — Source-generated MVVM observable models
    - `Microsoft.Windows.SDK.BuildTools` (WinApp CLI support)
- **Compilation Tool Command & Result**:
  - `dotnet build apps/desktop-winui/CircuitAssistant.csproj` executed via terminal.
  - Result:
    ```text
    已成功生成。
        0 个警告
        0 个错误
    已用时间 00:00:27.23
    CircuitAssistant -> D:\BJTU\2026年大创\apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.dll
    ```
- **Test Suite Command & Result**:
  - `pytest services/engine/tests services/api/tests` executed via terminal.
  - Result:
    ```text
    ======================== 33 passed, 1 warning in 6.42s ========================
    services\engine\tests\test_mna.py .......                                [ 21%]
    services\engine\tests\test_pipeline.py ..........                        [ 51%]
    services\api\tests\test_api.py ................                          [100%]
    ```

---

### 1.2 Current Wire, Pin, and Routing Models
- **`CircuitPin` & `CircuitWire` in [`apps/desktop-winui/Models/CircuitElement.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Models/CircuitElement.cs:37-158)**:
  - Lines 37-53:
    ```csharp
    public partial class CircuitPin : ObservableObject
    {
        [ObservableProperty] private string _id = string.Empty;
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private float _relativeX;
        [ObservableProperty] private float _relativeY;
        [ObservableProperty] private int _connectedNetId = -1;
    }
    ```
    Observations: Pin positions are computed dynamically via `CircuitElement.GetPinWorldPosition(pin)` using element rotation. `_connectedNetId` is unassigned (-1) everywhere in the codebase.
  - Lines 55-134:
    ```csharp
    public partial class CircuitWire : ObservableObject
    {
        [ObservableProperty] private string _id = string.Empty;
        [ObservableProperty] private string _startElementId = string.Empty;
        [ObservableProperty] private string _startPinName = string.Empty;
        [ObservableProperty] private string _endElementId = string.Empty;
        [ObservableProperty] private string _endPinName = string.Empty;
        [ObservableProperty] private float _startX;
        [ObservableProperty] private float _startY;
        [ObservableProperty] private float _endX;
        [ObservableProperty] private float _endY;
        [ObservableProperty] private int _netId;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _flipCorner;
        [ObservableProperty] private float _customMid;

        public List<Vector2> GetPathPoints()
        {
            var pts = new List<Vector2>();
            var p1 = new Vector2(StartX, StartY);
            var p2 = new Vector2(EndX, EndY);
            pts.Add(p1);
            if (Math.Abs(p1.X - p2.X) > 6 && Math.Abs(p1.Y - p2.Y) > 6)
            {
                if (CustomMid != 0)
                {
                    if (FlipCorner) { pts.Add(new Vector2(p1.X, CustomMid)); pts.Add(new Vector2(p2.X, CustomMid)); }
                    else { pts.Add(new Vector2(CustomMid, p1.Y)); pts.Add(new Vector2(CustomMid, p2.Y)); }
                }
                else
                {
                    if (FlipCorner) pts.Add(new Vector2(p1.X, p2.Y));
                    else pts.Add(new Vector2(p2.X, p1.Y));
                }
            }
            pts.Add(p2);
            return pts;
        }
    }
    ```
    Observations:
    1. A wire is strictly a single 2-point point-to-point connection with at most one L-bend (or two bends if `CustomMid != 0`).
    2. `CustomMid` is never set or modified anywhere in `CircuitCanvas` (confirmed via codebase search `Select-String -Pattern 'CustomMid'`).
    3. There is no concept of a multi-segment waypoint list, Bus/Trunk line, or first-class Junction entity.

---

### 1.3 Canvas Rendering, Solder Dots, and Hardcoded Template Wires
- **Direct2D Rendering Loop in [`apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:330-452)**:
  - Lines 354-445:
    ```csharp
    // Collect all pin positions so we never draw solder dots on component pins
    var pinPositions = new HashSet<(int X, int Y)>();
    foreach (var el in Elements)
    {
        foreach (var pin in el.Pins)
        {
            var p = el.GetPinWorldPosition(pin);
            pinPositions.Add(((int)MathF.Round(p.X / 5f) * 5, (int)MathF.Round(p.Y / 5f) * 5));
        }
    }
    ...
    // Detect T-junctions: where a wire endpoint touches another wire's segment
    foreach (var wire in Wires)
    {
        var endpoints = new[] { new Vector2(wire.StartX, wire.StartY), new Vector2(wire.EndX, wire.EndY) };
        foreach (var ep in endpoints)
        {
            var epKey = ((int)MathF.Round(ep.X / 5f) * 5, (int)MathF.Round(ep.Y / 5f) * 5);
            if (pinPositions.Contains(epKey)) continue;

            foreach (var (sa, sb, sw) in allSegments)
            {
                if (sw == wire) continue;
                if (IsPointOnSegmentStrict(ep, sa, sb, 5f))
                {
                    junctionDots.Add(epKey);
                    break;
                }
            }
        }
    }
    ...
    // Draw true EDA junction solder dots (neat, dark, 3.8px radius)
    foreach (var (jx, jy) in junctionDots)
    {
        var dotPos = new Vector2(jx, jy);
        ds.FillCircle(dotPos, 3.8f, isDark ? dotColor : Color.FromArgb(255, 15, 25, 35));
    }
    ```
  - Lines 448-451:
    ```csharp
    // 3. Draw classic template wires if no custom wires exist and BJT template is loaded
    if (Wires.Count == 0)
    {
        DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);
    }
    ```
- **The `DrawCircuitWires` Hardcoded Hack in [`CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:492-615)**:
  - Lines 523-535:
    ```csharp
    // Supply Rail
    ds.DrawLine(Math.Min(rb1.X - 20f, minX + 60f), vccY, maxX, vccY, wireColor, 2.5f);
    ...
    // Ground Rail
    ds.DrawLine(minX, gndY, maxX, gndY, wireColor, 2.5f);
    // Rb1 branch
    ds.DrawLine(rb1.X, vccY, rb1.X, rb1.Y - 20, wireColor, 2f);
    ds.DrawLine(rb1.X, rb1.Y + 20, rb1.X, baseNodeY, wireColor, 2f);
    ```
  - Lines 575-594 (Emitter Re/Ce branch):
    ```csharp
    float emitNodeY = q1.Y + 30 + 20;
    ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY, wireColor, 2f);
    DrawJunctionDot(ds, new Vector2(q1.X, emitNodeY), dotColor);

    if (re != null)
    {
        ds.DrawLine(q1.X, emitNodeY, re.X, emitNodeY, wireColor, 2f);
        ds.DrawLine(re.X, emitNodeY, re.X, re.Y - 20, wireColor, 2f);
        ds.DrawLine(re.X, re.Y + 20, re.X, gndY, wireColor, 2f);
        DrawJunctionDot(ds, new Vector2(re.X, gndY), dotColor);
    }

    if (ce != null)
    {
        ds.DrawLine(q1.X, emitNodeY, ce.X, emitNodeY, wireColor, 2f);
        ds.DrawLine(ce.X, emitNodeY, ce.X, ce.Y - 20, wireColor, 2f);
        ds.DrawLine(ce.X, ce.Y + 20, ce.X, gndY, wireColor, 2f);
        DrawJunctionDot(ds, new Vector2(ce.X, gndY), dotColor);
    }
    ```
  - Lines 1283-1286:
    ```csharp
    private void DrawJunctionDot(CanvasDrawingSession ds, Vector2 pos, Color color)
    {
        ds.FillCircle(pos, 4.5f, color);
    }
    ```
  - Critical Defects Directly Observed in `DrawCircuitWires`:
    1. Resistors `Rb1`, `Rb2`, `Rc`, `Re` rotated 90° have pin terminals at `Y - 40` and `Y + 40` (and body edges at `Y - 20` and `Y + 20`). `DrawCircuitWires` draws lines down to `Y - 20` and `Y + 20`, piercing right through the pin terminal and drawing over the resistor body leads.
    2. Transistor `Q1` emitter pin is at `(q1.X + 20, q1.Y + 35)`. `DrawCircuitWires` connects to `(q1.X, q1.Y + 30)`, missing the pin by 20px horizontally and 5px vertically.
    3. `Re` is at `X = 330, Y = 385`; `q1` is at `X = 330, Y = 250`. `ds.DrawLine(q1.X, emitNodeY, re.X, emitNodeY)` connects `(330, 300)` to `(330, 300)` (a zero-length line).
    4. Solder dots in `DrawCircuitWires` are drawn at `4.5f` with accent color instead of 3.8px solid dark EDA dots.
    5. In `AnalysisStudioViewModel.cs` line 791: `CreateNewProject` executes `Wires.Clear()`. When the template is active, `Wires` is completely empty. If the user connects even one wire, `Wires.Count == 1`, `Wires.Count == 0` becomes false, and all template wires vanish from the canvas.

---

### 1.4 Wire Interaction: Creation, Tapping, Corner Dragging, Rubber-Banding
- **Wire Creation & Wire Tap in [`CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1320-1415)**:
  - If a user connects multiple elements to the same pin (e.g. `Re.Pin1 -> Q1.E` and `Ce.Pin1 -> Q1.E`), both wires are added to `Wires` with `EndElementId = "Q1", EndPinName = "E"`.
    - Both wire lines terminate at the exact same pin coordinate `(q1.X + 20, q1.Y + 35)`.
    - Multiple wires crowd directly onto a single terminal pin.
  - Wire Tap:
    ```csharp
    var tapPos = GetNearestPointOnWire(targetWire, worldPos);
    ...
    var wire = new CircuitWire
    {
        Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
        StartElementId = _wireStartElement.Id,
        StartPinName = _wireStartPin.Name,
        EndElementId = targetWire.StartElementId,
        EndPinName = "WireTap",
        StartX = startP.X,
        StartY = startP.Y,
        EndX = tapPos.X,
        EndY = tapPos.Y,
        FlipCorner = startIsVertical
    };
    ```
    Observations:
    - User cannot start a wire by clicking an existing wire (can only end on a wire).
    - `targetWire` is not split, nor is a persistent junction model created.
- **Corner Manipulation in [`CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1506-1527)**:
  ```csharp
  var cornerA = new Vector2(p1.X, p2.Y);
  var cornerB = new Vector2(p2.X, p1.Y);

  float distA = Vector2.Distance(worldPos, cornerA);
  float distB = Vector2.Distance(worldPos, cornerB);
  bool newFlip = distA < distB;

  if (_draggedWire.FlipCorner != newFlip)
  {
      _draggedWire.FlipCorner = newFlip;
      Canvas.Invalidate();
  }
  ```
  Observations:
  - Dragging a wire does not move any corner or segment to a user-specified coordinate. It simply toggles `FlipCorner` between `true` and `false` based on proximity to corner A vs B.
  - There is no orthogonal snapping to grid, nor custom corner offset positioning.
- **Rubber-Banding in [`CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1713-1737)**:
  ```csharp
  private void SyncConnectedWiresToElement(CircuitElement el)
  {
      foreach (var w in Wires)
      {
          if (w.StartElementId == el.Id) { ... }
          if (w.EndElementId == el.Id)
          {
              var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
              if (pin != null) { w.EndX = pPos.X; w.EndY = pPos.Y; }
          }
      }
  }
  ```
  Observations:
  - For a tapped wire, `EndPinName = "WireTap"`. Because `"WireTap"` is not in `el.Pins`, `pin` is null.
  - When the host element of the tapped wire is moved, the tapped branch's `(EndX, EndY)` is never updated, causing the branch to detach and float in mid-air.
  - In the default template, `Wires.Count == 0`, so `SyncConnectedWiresToElement` does nothing.

---

## 2. Logic Chain

1. **Premise 1 (From Section 1.3 & 1.4)**: Current wire connections connect pins point-to-point directly. When 3 or more pins belong to the same net (e.g. `Q1.E`, `Re`, `Ce`), multiple individual wires converge on the pin terminal `Q1.E`.
   - **Inference**: This causes wire congestion on pin terminals and violates Requirement R3 ("引脚仅引出一条正交主干导线，后续分支均通过 T型焊点垂直搭接在主干上；严禁多条独立导线平行挤占并叠连在同一个端子上；元件引脚端子保持净空").

2. **Premise 2 (From Section 1.3)**: Solder dots are computed dynamically via 5px grid quantization `((int)MathF.Round(pt.X / 5f) * 5)` and distance thresholds. In `DrawCircuitWires`, solder dots are hardcoded at 4.5px radius with accent color.
   - **Inference**: Quantizing floating-point wire coordinates to 5px grids causes visual jitter and misalignment between solder dots and wire lines. Moreover, 4.5px radius and cyan accent color violate the EDA standard of 3.8px solid dark dots at true T-junctions.

3. **Premise 3 (From Section 1.3 & 1.4)**: The default template `CommonEmitter_Amp.sch` does not populate `ViewModel.Wires`, relying instead on the `DrawCircuitWires` function in `CircuitCanvas`.
   - **Inference**: Because template wires are not entities in `ViewModel.Wires`, user interactions (selection, deletion, wire tapping, corner manipulation, rubber-banding) fail on all default template wires. Adding any single new wire hides all template wires because `Wires.Count > 0`.

4. **Premise 4 (From Section 1.4)**: Wire corner dragging only toggles `FlipCorner` (binary L-shape flip). `_customMid` is unused. Wire tapping sets `EndPinName = "WireTap"` without bidirectional relationship tracking.
   - **Inference**: True corner dragging with orthogonal grid snapping is missing. Moving components breaks tapped branch connections because `SyncConnectedWiresToElement` ignores `"WireTap"`.

5. **Synthesis**: To fulfill R3 and align with industrial EDA standards (Altium / KiCad):
   - The data model must support orthogonal Trunk/Bus lines with branch taps.
   - Tapped wires must record the parent wire ID and maintain attachment upon movement.
   - Interactive corner dragging must update orthogonal bend coordinates with grid snapping.
   - Solder dots must be strictly rendered at 3.8px solid radius at true T-junctions, while pin terminals have a strict keep-out zone.
   - The default template must populate `Wires` with clean, orthogonal, bus/trunk-routed `CircuitWire` models.

---

## 3. Caveats

1. **Read-Only Constraint**: This investigation did not modify any source code or project files. Proposed architectural designs are presented as specifications and design patterns.
2. **Coordinate Grid Convention**: The project currently uses floating-point coordinates with occasional 10px / 5px snap rounding. Industrial EDA grid模数 (80 mil / 160 mil, roughly 20px / 40px) should be standardized across both component placement and wire routing.
3. **Multi-Agent Alignment**:
   - Survey 1 (`teamwork_preview_explorer_survey_1`) investigated R1 (Netlist extraction and solving engine).
   - Survey 2 (`teamwork_preview_explorer_survey_2`) investigated R2 (Educational template layout).
   - The wire routing architecture in R3 must provide the clean Netlist topology needed by R1 and the visual spacing needed by R2.

---

## 4. Conclusion & Proposed Architecture for R3

### 4.1 Bus / Trunk Routing Architecture (网络干线拓扑)
```text
          [Pin Terminal: Q1.E] (Keep-out: hollow pin ring, NO solder dot)
                  │
                  │ (Single orthogonal trunk lead)
                  ▼
         ━━━━━━━━━●━━━━━━━━━ Trunk Line
                  │         │
           (3.8px Solder) (3.8px Solder)
                  │         │
                  ▼         ▼
               [Re.1]    [Ce.1]
```
- **Rule 1 (Single Stem Lead)**: Every pin terminal emits at most ONE orthogonal wire segment extending along the pin's normal orientation (at least 20-30px lead) before joining a trunk or bending.
- **Rule 2 (Orthogonal Trunk Routing)**: When two or more branches connect to the same net, a central orthogonal Trunk wire is established (e.g. horizontal Emitter rail, VCC rail, GND rail).
- **Rule 3 (T-Junction Tap)**: Subsequent parallel branches connect perpendicularly to the trunk via T-junction taps.
- **Rule 4 (No Terminal Crowding)**: If user drags a wire to a pin that already has a connected wire, the interaction automatically routes a T-junction tap onto the existing wire lead rather than piling a second wire onto the pin.

### 4.2 Solder Dot & Terminal Keep-Out Architecture
- **Solder Dot Specification**:
  - Radius: Exactly `3.8px` (Diameter: `7.6px`).
  - Style: Solid fill (Light theme: `#0F1923` / Dark theme: `#00A4EF`).
  - Exact Location: Rendered at the exact mathematical intersection coordinate $(X_{tap}, Y_{tap})$ without 5px grid quantization drift.
  - Triggers: Rendered only when:
    1. A wire endpoint lands on the interior of another wire segment (T-junction), OR
    2. Three or more wire endpoints meet at the same coordinate.
- **Pin Terminal Keep-Out**:
  - Distance check: If $|(X, Y) - (Pin.X, Pin.Y)| < 10px$, NEVER render a solder dot.
  - Pin terminals always display the clean hollow pin ring (`DrawPinNode`, 3.8px radius hollow circle).

### 4.3 Interactive Wire Tap (Wire-to-Wire Branching)
- **Two-Way Tapping**:
  1. *Pin to Wire*: User drags from Pin and hovers over an existing wire $\to$ cursor shows crosshair and snaps to nearest orthogonal point on wire segment $\to$ click establishes T-junction tap.
  2. *Wire to Pin/Wire*: In Wire Mode, user clicks an existing wire $\to$ starts new wire from that tap point $\to$ drags to pin or wire to complete branch.
- **Topological Association in Model**:
  - Add to `CircuitWire`:
    - `TargetWireId`: ID of host wire being tapped (empty if pin-to-pin).
    - `TapSegmentIndex`: Index of the segment being tapped.
    - `TapParamRatio` or `TapCoordinate`: Position of the tap on the host segment.
  - When the host wire moves or the host component moves, all child tapped wires automatically recalculate their tap endpoints to maintain topological contact.

### 4.4 Corner Dragging with Orthogonal Snapping
- **Interactive Corner & Segment Handles**:
  - When `wire.IsSelected`:
    - Render circular handle (5.5px) at each 90° bend corner.
    - Render subtle bar handle at the midpoint of each segment.
  - Pointer Interactions:
    - Dragging a corner handle moves both adjacent segments, maintaining 90° angles.
    - Dragging a segment handle moves the segment perpendicularly (horizontal segment moves vertically along Y; vertical segment moves horizontally along X).
    - Coordinates snap smoothly to a 10px or 20px grid.
  - Store bend coordinate in `CircuitWire` (e.g. `CustomMid` or `List<Vector2> PathPoints`).

### 4.5 Component Dragging with Rubber-Banding
- When `CircuitElement` is dragged:
  1. Primary attached wires update `(StartX, StartY)` or `(EndX, EndY)` matching the moved pin's world position.
  2. Orthogonal path generator preserves lead exit orientation from pin before turning.
  3. Any child wires tapped into the moved wire update their tap junction positions proportionally.
  4. Solder dots update in real time at 60fps.

### 4.6 Template Modernization
- In `AnalysisStudioViewModel`:
  - Replace `Wires.Clear()` in `CreateNewProject` with `InitializeDefaultCircuitWires()`.
  - Populate `Wires` with standard EDA orthogonal trunk and branch wires:
    - VCC top bus rail.
    - GND bottom reference rail.
    - Base net trunk with taps from `Rb1`, `Rb2`, `C1` to `Q1.B`.
    - Emitter net trunk with single lead from `Q1.E` and T-junction taps to `Re` and `Ce` (100px clear separation between `Re` and `Ce`).
    - Collector net trunk connecting `Rc` to `Q1.C` with tap to `C2`.
  - Retire the hardcoded `DrawCircuitWires` function in `CircuitCanvas.xaml.cs`.

---

## 5. Verification Method

### 5.1 Compilation Verification
Execute from project root via PowerShell:
```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj
```
**Expected**: Build succeeds with 0 errors, 0 warnings.

### 5.2 Test Suite Verification
Execute from project root via PowerShell:
```powershell
pytest services/engine/tests services/api/tests
```
**Expected**: All 33 tests pass.

### 5.3 Interactive Verification Checklist
1. **Startup / Template Load**:
   - Launch app via `.\start-winui.bat`.
   - Verify `CommonEmitter_Amp.sch` displays clean orthogonal wires with:
     - Parallel VCC top rail and GND bottom rail.
     - Single trunk lead from `Q1.E` branching into `Re` and `Ce` via T-junctions.
     - Exactly 3.8px solid dark solder dots at branch junctions (`Q1.E` trunk taps, GND taps, VCC taps).
     - Hollow pin rings on all component terminals with zero solder dots on pins.
     - No piercing wires through `Rb1`, `Rb2`, `Rc`, `Re`, `Ce`, or `Q1`.
2. **Wire Tap Interaction**:
   - Toggle Wire Mode (`W` key).
   - Click an existing wire (e.g. VCC rail or GND rail) $\to$ verify wire drawing begins from tap point.
   - Click a resistor pin $\to$ verify 3.8px solder dot appears at tap point and wire connects orthogonally.
3. **Corner Dragging**:
   - Click to select any wire with a 90° bend.
   - Click and drag the bend handle $\to$ verify the corner moves smoothly with orthogonal grid snapping.
4. **Rubber-Banding Component Drag**:
   - Drag `Re` or `Ce` horizontally or vertically $\to$ verify attached wires and tap points adjust dynamically without detaching or leaving orphan lines.
