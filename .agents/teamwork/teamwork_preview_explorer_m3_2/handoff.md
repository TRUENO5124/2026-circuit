# Requirement R3 Milestone 3 (M3-2): Interactive Wire Tap System Architecture Report

**Explorer**: Interactive Wire Tap Explorer (`teamwork_preview_explorer_m3_2`)  
**Mission**: Requirement R3 (Industrial EDA Wire Routing & Canvas Architecture - Interactive Wire Tap System)  
**Date**: 2026-10-12  

---

## 1. Observation

### 1.1 Direct Source Code Observations in `CircuitCanvas.xaml.cs`
Analysis of [`apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs) reveals key architectural characteristics, limitations, and defects in the current pointer lifecycle and wire creation pipeline:

1. **Wire Creation State Variables ([`CircuitCanvas.xaml.cs:44-50`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:44-50))**:
   ```csharp
   // Wire connection state
   private bool _isDrawingWire;
   private CircuitElement? _wireStartElement;
   private CircuitPin? _wireStartPin;
   private Vector2 _wireStartPos;
   private Vector2 _currentWireEnd;
   private (CircuitElement Element, CircuitPin Pin)? _hoveredPin;
   ```
   *Observation*: The active wire drawing state assumes that every in-progress wire begins at a component pin (`_wireStartElement` and `_wireStartPin`). There are no state fields for tracking a wire branch initiated from an existing wire (`_wireStartWire`, `_wireStartIsTap`). Furthermore, hover tracking only exists for pins (`_hoveredPin`); no hover tracking exists for wire taps (`_hoveredWireTap`).

2. **Pointer Press Routing Logic ([`CircuitCanvas.xaml.cs:1181-1281`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1181-1281))**:
   ```csharp
   // 1. Check if clicked near a Pin (Wire drawing)
   var hitPin = FindPinAt(worldPos);
   if (hitPin != null || IsWireMode)
   {
       if (hitPin != null)
       {
           if (!_isDrawingWire)
           {
               // Start drawing wire from this pin
               _isDrawingWire = true;
               ...
           }
           else
           {
               // Clicked target pin to finish wire!
               ...
           }
       }
       else if (_isDrawingWire)
       {
           // Clicked while drawing wire, but not on a pin: check if tapped into an existing wire!
           CircuitWire? targetWire = null;
           for (int i = Wires.Count - 1; i >= 0; i--)
           {
               if (Wires[i].HitTest(worldPos, 10f))
               {
                   targetWire = Wires[i];
                   break;
               }
           }
           ...
       }
   }
   ```
   *Observations*:
   - When `!_isDrawingWire` and `IsWireMode == true`, but `hitPin == null`, the code falls through to:
     ```csharp
     // 2. Check if hit testing any Wire
     CircuitWire? hitWire = null;
     for (int i = Wires.Count - 1; i >= 0; i--)
     {
         if (Wires[i].HitTest(worldPos, tolerance: 8f))
         {
             hitWire = Wires[i];
             break;
         }
     }
     if (hitWire != null)
     {
         SelectedWire = hitWire;
         _isDraggingWire = true;
         _draggedWire = hitWire;
         ...
     }
     ```
     Even in Wire Mode (`IsWireMode = true`), clicking an existing wire selects and initiates corner drag on that wire instead of spawning a new branch wire from the wire tap!
   - There is zero provision for starting a wire branch by clicking an existing wire.

3. **Defective Wire Tap Creation Data Contract ([`CircuitCanvas.xaml.cs:1249-1269`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1249-1269))**:
   ```csharp
   if (targetWire != null && _wireStartElement != null && _wireStartPin != null)
   {
       var tapPos = GetNearestPointOnWire(targetWire, worldPos);
       var startP = _wireStartElement.GetPinWorldPosition(_wireStartPin);
       var startDir = _wireStartElement.GetPinWorldDirection(_wireStartPin);
       bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);

       var wire = new CircuitWire
       {
           Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
           StartElementId = _wireStartElement.Id,
           StartPinName = _wireStartPin.Name,
           EndElementId = targetWire.StartElementId, // <-- CRITICAL DEFECT
           EndPinName = "WireTap",
           StartX = startP.X,
           StartY = startP.Y,
           EndX = tapPos.X,
           EndY = tapPos.Y,
           FlipCorner = startIsVertical
       };
       Wires.Add(wire);
       WireCreated?.Invoke(wire);
       ...
   }
   ```
   *Critical Defect*: `EndElementId = targetWire.StartElementId` records the host element ID of the target wire's start pin rather than `targetWire.Id`!
   Meanwhile, [`apps/desktop-winui/Engine/NetlistExtractor.cs:207-210`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Engine/NetlistExtractor.cs:207-210) explicitly expects:
   ```csharp
   if (w.EndPinName == "WireTap" && netlist.Wires.ContainsKey(w.EndElementId))
   {
       ds.Union(wKey, $"WIRE:{w.EndElementId}");
   }
   ```
   Because `EndElementId` is populated with an element ID (e.g., `"Q1"` or `"VCC"`) instead of the wire ID (e.g., `"W_a1b2c3"`), `netlist.Wires.ContainsKey(w.EndElementId)` evaluates to `false`, breaking electrical Netlist union-find connectivity!
   Furthermore, `wire.NetId` is never assigned, leaving the newly tapped branch with `NetId = 0`.

4. **Nearest Point Projection on Wire ([`CircuitCanvas.xaml.cs:1617-1650`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1617-1650))**:
   ```csharp
   private static Vector2 GetNearestPointOnWire(CircuitWire wire, Vector2 p)
   {
       var pts = wire.GetPathPoints();
       float bestDist = float.MaxValue;
       Vector2 bestProj = p;

       for (int i = 0; i < pts.Count - 1; i++)
       {
           var a = pts[i];
           var b = pts[i + 1];
           var ab = b - a;
           float lenSq = ab.LengthSquared();
           if (lenSq < 1e-4f) continue;
           float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
           var proj = a + t * ab;

           if (Math.Abs(ab.X) > Math.Abs(ab.Y))
           {
               proj.X = MathF.Round(proj.X / 10f) * 10f;
           }
           else
           {
               proj.Y = MathF.Round(proj.Y / 10f) * 10f;
           }

           float d = Vector2.Distance(p, proj);
           if (d < bestDist)
           {
               bestDist = d;
               bestProj = proj;
           }
       }
       return bestProj;
   }
   ```
   *Observations*:
   - Snapping simply rounds coordinates independently without clamping to the segment's interior bounds, allowing rounded values to jump past segment corners.
   - It does not perform perpendicular auto-drop alignment relative to the branch's starting coordinate (i.e., aligning with `_wireStartPos.X` for horizontal segments or `_wireStartPos.Y` for vertical segments to produce zero-bend orthogonal branches).
   - It ignores pin terminal keep-out zones: a tap can be created within 1-2px of a component pin terminal, violating EDA standards.

5. **Lack of Visual Tap Indicators in `Canvas_Draw` and `Canvas_PointerMoved` ([`CircuitCanvas.xaml.cs:468-478, 1420-1444`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:468-478))**:
   - In `Canvas_PointerMoved`, hovering near a wire only changes the cursor to `Hand` when not in wire mode, or leaves it as `Arrow`. There is zero visual indication (no snap circle, no solder dot preview) when the pointer is within 8px of an existing wire segment.
   - In `Canvas_Draw`, in-progress wire drawing renders a naive diagonal dashed line (`ds.DrawLine(start, _currentWireEnd, ...)`). It does not provide an orthogonal Manhattan routing preview or live solder dot feedback at the tap junction.

6. **Broken Rubber-Banding for Wire Taps ([`CircuitCanvas.xaml.cs:1577-1602`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:1577-1602))**:
   ```csharp
   private void SyncConnectedWiresToElement(CircuitElement el)
   {
       foreach (var w in Wires)
       {
           if (w.StartElementId == el.Id)
           {
               var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
               if (pin != null) { ... }
           }
           if (w.EndElementId == el.Id)
           {
               var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
               if (pin != null) { ... }
           }
       }
   }
   ```
   *Observation*: When a component attached to a trunk wire is dragged, primary wires update, but child tapped wires are linked to the wire (`EndPinName == "WireTap"`, `EndElementId == wire.Id`). Because `EndElementId` is a wire ID rather than `el.Id`, and `"WireTap"` is not in `el.Pins`, `SyncConnectedWiresToElement` never updates the tap positions of child wires. Child branch wires detach and float in mid-air.

---

## 2. Logic Chain

1. **Step 1: Symmetric Bidirectional Wire Tapping Requirement**
   - *Premise*: Requirement R3 (§2) states: "支持直接点击已有导线实现 T 型节点搭接（Wire Tap）". In real-world EDA usage, engineers branch circuits in both directions:
     - Case A (*Pin to Wire*): Dragging from a component pin and clicking an existing bus/trunk wire to tap into it.
     - Case B (*Wire to Pin*): Clicking an existing rail or trunk wire while in Wire Mode, pulling an orthogonal lead out, and clicking a component pin.
     - Case C (*Wire to Wire*): Tapping a sub-branch between two existing trunk/bus lines (e.g., bridging ground rails or test points).
   - *Inference*: The wire connection state machine must be refactored from a pin-only model to a generic endpoint model supporting both `PinEndpoint` and `WireTapEndpoint`.

2. **Step 2: Topological Model Contract Alignment with `NetlistExtractor`**
   - *Premise*: From Section 1.1 Item 3, `NetlistExtractor.cs` (lines 203–210) explicitly uses:
     `w.StartPinName == "WireTap" && netlist.Wires.ContainsKey(w.StartElementId)`
     and
     `w.EndPinName == "WireTap" && netlist.Wires.ContainsKey(w.EndElementId)`.
   - *Inference*: To establish valid topological connectivity and ensure `NetlistExtractor` correctly identifies the electrical net:
     - When starting at a wire tap: `StartPinName = "WireTap"`, `StartElementId = hitWire.Id`, `NetId = hitWire.NetId`.
     - When terminating at a wire tap: `EndPinName = "WireTap"`, `EndElementId = hitWire.Id`, `NetId = hitWire.NetId`.
     - Setting `StartElementId` or `EndElementId` to `hitWire.StartElementId` is provably erroneous and must be corrected.

3. **Step 3: Closest Projection & Manhattan Orthogonal Snapping Formulation**
   - *Premise*: Every wire in `Wires` consists of orthogonal segments ($S_k = (A_k, B_k)$ where either $A_k.X = B_k.X$ or $A_k.Y = B_k.Y$).
   - *Inference*:
     - For a horizontal segment ($A_k.Y = B_k.Y = Y_0$):
       Any orthogonal tap must be at $Y = Y_0$.
       If the starting anchor coordinate $P_{\text{start}}$ satisfies $\min(A_k.X, B_k.X) + \delta \le P_{\text{start}}.X \le \max(A_k.X, B_k.X) - \delta$, dropping a perpendicular line from $P_{\text{start}}$ to the segment yields $X_{\text{tap}} = P_{\text{start}}.X$, producing a pure single straight vertical branch with zero unnecessary bends.
       Otherwise, $X_{\text{tap}}$ snaps to the nearest 10px grid coordinate within $[\min(X) + \delta, \max(X) - \delta]$.
     - For a vertical segment ($A_k.X = B_k.X = X_0$):
       Any orthogonal tap must be at $X = X_0$.
       If $P_{\text{start}}$ satisfies $\min(A_k.Y, B_k.Y) + \delta \le P_{\text{start}}.Y \le \max(A_k.Y, B_k.Y) - \delta$, dropping a perpendicular yields $Y_{\text{tap}} = P_{\text{start}}.Y$, producing a pure single straight horizontal branch.
       Otherwise, $Y_{\text{tap}}$ snaps to the nearest 10px grid coordinate within $[\min(Y) + \delta, \max(Y) - \delta]$.

4. **Step 4: Strict Terminal Keep-Out Enforcement**
   - *Premise*: Requirement R3 (§1) mandates: "元件引脚端子保持净空，仅在真正的支路交汇点绘制 3.8px 经典 EDA 实心焊点（Solder Dot）". Survey 3 and M3-1 establish a keep-out radius of $10\text{px}$ around all component pins.
   - *Inference*: If the computed projection point $P_{\text{proj}}$ falls within $10.0\text{px}$ of any component pin in `Elements`, the wire tap candidate is rejected or redirected directly to the pin. Solder dots are strictly forbidden within $10\text{px}$ of any pin terminal.

5. **Step 5: Interactive Visual Feedback Pipeline**
   - *Premise*: Requirement R3 (§2) mandates responsive EDA visual feedback.
   - *Inference*:
     - When the pointer moves within $8.0\text{px}$ of an existing wire segment, `Canvas_PointerMoved` identifies the closest segment, computes the snapped tap point, and caches `_hoveredWireTap`.
     - In `Canvas_Draw`, when `_hoveredWireTap` is active:
       - Render a 6.0px radius accent snap circle with 1.8px stroke.
       - Render a 3.8px radius solid solder preview dot.
       - Render a badge: `"T型节点 (Net {netId})"`.
       - When in wire drawing mode, snap `_currentWireEnd` directly to the projected tap coordinate and render the orthogonal dashed branch preview in real time at 60fps.

6. **Step 6: Topological Child-Parent Dynamic Synchronization**
   - *Premise*: When a parent trunk wire changes (due to component dragging or corner adjustment), child wires attached via `"WireTap"` must remain attached.
   - *Inference*: A recursive `SyncTappedWires(CircuitWire parentWire)` method must project attached child endpoints onto the updated geometry of `parentWire`. Deleting a parent wire must cascade to remove child branches, preventing floating orphan wires.

---

## 3. Caveats

1. **Read-Only Explorer Scope**: Explorer M3-2 is strictly read-only and does not modify source code or tests directly. All implementation architectures, data structures, and code snippets in this report are prepared for downstream worker execution.
2. **Division of Responsibilities within Milestone 3**:
   - M3-1 designs Bus/Trunk routing rules and Solder Dot rendering geometry (3.8px solid dark circle, pin keep-out).
   - M3-2 (this report) designs the Interactive Wire Tap interaction, projection algorithm, state machine, and child-parent topological tracking.
   - M3-3 designs corner/segment dragging handles and component rubber-banding.
   - All three modules share the canonical `CircuitWire` data model and coordinate snapping conventions (10px grid modulus).
3. **High-DPI and Zoom Scale Invariance**: Proximity tolerances (8.0px for hover, 10.0px for keep-out) are evaluated in world coordinates (`ScreenToWorld`) so interactive snapping remains consistent across all zoom levels (40% to 300%).

---

## 4. Conclusion & Complete Architectural Specification

### 4.1 Data Structures & State Model Enhancements

#### Extended Pointer & Wire Creation State in `CircuitCanvas.xaml.cs`
Replace the pin-only fields with a unified endpoint architecture:

```csharp
// =========================================================================
// Enhanced Wire Connection State Machine
// =========================================================================
public enum WireEndpointKind
{
    None,
    Pin,
    WireTap
}

public readonly record struct WireTapHit(
    CircuitWire Wire,
    int SegmentIndex,
    Vector2 TapPoint,
    bool IsHorizontalSegment,
    int NetId
);

// State fields to add to CircuitCanvas
private bool _isDrawingWire;
private WireEndpointKind _wireStartKind = WireEndpointKind.None;

// When _wireStartKind == WireEndpointKind.Pin:
private CircuitElement? _wireStartElement;
private CircuitPin? _wireStartPin;

// When _wireStartKind == WireEndpointKind.WireTap:
private CircuitWire? _wireStartWire;
private int _wireStartSegmentIndex;

private Vector2 _wireStartPos;
private Vector2 _currentWireEnd;

// Live hover feedback states
private (CircuitElement Element, CircuitPin Pin)? _hoveredPin;
private WireTapHit? _hoveredWireTap;
```

---

### 4.2 Geometric Projection & Manhattan Snapping Engine

Implement the following helper methods in `CircuitCanvas.xaml.cs`:

```csharp
// =========================================================================
// Manhattan Wire Tap Projection & Snapping Engine
// =========================================================================

/// <summary>
/// Searches for an existing wire segment within tolerance distance of worldPos.
/// Enforces terminal keep-out (10px around all component pins).
/// </summary>
private WireTapHit? FindWireTapAt(Vector2 worldPos, float tolerance = 8.0f, Vector2? referenceStart = null)
{
    // 1. Strict Pin Terminal Keep-Out: do NOT tap if within 10px of any pin
    foreach (var el in Elements)
    {
        foreach (var pin in el.Pins)
        {
            var pinPos = el.GetPinWorldPosition(pin);
            if (Vector2.Distance(worldPos, pinPos) <= 10.0f)
            {
                return null;
            }
        }
    }

    WireTapHit? bestHit = null;
    float minDistance = float.MaxValue;

    // Search top-to-bottom (newest wires first)
    for (int i = Wires.Count - 1; i >= 0; i--)
    {
        var wire = Wires[i];
        var pts = wire.GetPathPoints();

        for (int segIdx = 0; segIdx < pts.Count - 1; segIdx++)
        {
            var a = pts[segIdx];
            var b = pts[segIdx + 1];
            var ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-4f) continue;

            // Clamped projection factor t in [0, 1]
            float t = Math.Clamp(Vector2.Dot(worldPos - a, ab) / lenSq, 0f, 1f);
            var rawProj = a + t * ab;
            float dist = Vector2.Distance(worldPos, rawProj);

            if (dist <= tolerance && dist < minDistance)
            {
                bool isHorizontal = Math.Abs(ab.Y) < 1e-3f;
                Vector2 snappedTap;

                if (isHorizontal)
                {
                    float minX = MathF.Min(a.X, b.X);
                    float maxX = MathF.Max(a.X, b.X);

                    // Manhattan Perpendicular Auto-Drop:
                    // If referenceStart falls within segment range, drop pure vertical line!
                    if (referenceStart.HasValue && referenceStart.Value.X >= minX + 5f && referenceStart.Value.X <= maxX - 5f)
                    {
                        snappedTap = new Vector2(referenceStart.Value.X, a.Y);
                    }
                    else
                    {
                        // Micro-snap to 10px grid along segment, clamped 5px away from endpoints
                        float gridX = MathF.Round(rawProj.X / 10f) * 10f;
                        gridX = Math.Clamp(gridX, minX + 5f, maxX - 5f);
                        snappedTap = new Vector2(gridX, a.Y);
                    }
                }
                else
                {
                    float minY = MathF.Min(a.Y, b.Y);
                    float maxY = MathF.Max(a.Y, b.Y);

                    // Manhattan Perpendicular Auto-Drop:
                    // If referenceStart falls within segment range, drop pure horizontal line!
                    if (referenceStart.HasValue && referenceStart.Value.Y >= minY + 5f && referenceStart.Value.Y <= maxY - 5f)
                    {
                        snappedTap = new Vector2(a.X, referenceStart.Value.Y);
                    }
                    else
                    {
                        // Micro-snap to 10px grid along segment, clamped 5px away from endpoints
                        float gridY = MathF.Round(rawProj.Y / 10f) * 10f;
                        gridY = Math.Clamp(gridY, minY + 5f, maxY - 5f);
                        snappedTap = new Vector2(a.X, gridY);
                    }
                }

                // Verify snapped point also satisfies pin keep-out
                bool encroachesPin = Elements.Any(el => el.Pins.Any(p =>
                    Vector2.Distance(snappedTap, el.GetPinWorldPosition(p)) <= 10.0f));

                if (!encroachesPin)
                {
                    minDistance = dist;
                    bestHit = new WireTapHit(wire, segIdx, snappedTap, isHorizontal, wire.NetId);
                }
            }
        }
    }

    return bestHit;
}
```

---

### 4.3 Pointer Event Flow & Branch Creation in `CircuitCanvas.xaml.cs`

#### A. `Canvas_PointerPressed` Implementation
Integrates bidirectional wire tapping (Pin $\to$ Pin, Pin $\to$ WireTap, WireTap $\to$ Pin, WireTap $\to$ WireTap):

```csharp
private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    Focus(FocusState.Programmatic);

    var pt = e.GetCurrentPoint(Canvas);
    var screenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
    var worldPos = ScreenToWorld(screenPos);

    // =========================================================================
    // 1. IN-PROGRESS WIRE CONNECTION COMPLETION (SECOND CLICK)
    // =========================================================================
    if (_isDrawingWire)
    {
        var hitPin = FindPinAt(worldPos);
        var hitTap = FindWireTapAt(worldPos, tolerance: 10.0f, referenceStart: _wireStartPos);

        // Target: Component Pin
        if (hitPin != null)
        {
            var endPin = hitPin.Value.Pin;
            var endEl = hitPin.Value.Element;
            var endPos = endEl.GetPinWorldPosition(endPin);

            // Avoid connecting pin to itself
            if (_wireStartKind == WireEndpointKind.Pin &&
                _wireStartElement == endEl && _wireStartPin == endPin)
            {
                return;
            }

            CircuitWire wire;
            if (_wireStartKind == WireEndpointKind.Pin)
            {
                // Pin -> Pin
                var startDir = _wireStartElement!.GetPinWorldDirection(_wireStartPin!);
                bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);
                wire = new CircuitWire
                {
                    Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
                    StartElementId = _wireStartElement.Id,
                    StartPinName = _wireStartPin!.Name,
                    EndElementId = endEl.Id,
                    EndPinName = endPin.Name,
                    StartX = _wireStartPos.X,
                    StartY = _wireStartPos.Y,
                    EndX = endPos.X,
                    EndY = endPos.Y,
                    FlipCorner = startIsVertical
                };
            }
            else
            {
                // WireTap -> Pin
                var endDir = endEl.GetPinWorldDirection(endPin);
                bool endIsVertical = Math.Abs(endDir.Y) > Math.Abs(endDir.X);
                wire = new CircuitWire
                {
                    Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
                    StartElementId = _wireStartWire!.Id,
                    StartPinName = "WireTap",
                    EndElementId = endEl.Id,
                    EndPinName = endPin.Name,
                    StartX = _wireStartPos.X,
                    StartY = _wireStartPos.Y,
                    EndX = endPos.X,
                    EndY = endPos.Y,
                    NetId = _wireStartWire.NetId,
                    FlipCorner = !endIsVertical
                };
            }

            Wires.Add(wire);
            WireCreated?.Invoke(wire);
            StatusMessageChanged?.Invoke($"已建立导线连接: {wire.StartElementId}.{wire.StartPinName} ⟷ {wire.EndElementId}.{wire.EndPinName}");

            ResetDrawingWireState(e.Pointer);
            return;
        }

        // Target: Existing Wire (T-Junction Tap)
        if (hitTap != null)
        {
            var targetWire = hitTap.Value.Wire;
            var tapPos = hitTap.Value.TapPoint;

            CircuitWire wire;
            if (_wireStartKind == WireEndpointKind.Pin)
            {
                // Pin -> WireTap
                var startDir = _wireStartElement!.GetPinWorldDirection(_wireStartPin!);
                bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);
                wire = new CircuitWire
                {
                    Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
                    StartElementId = _wireStartElement.Id,
                    StartPinName = _wireStartPin!.Name,
                    EndElementId = targetWire.Id, // Correctly records targetWire.Id!
                    EndPinName = "WireTap",
                    StartX = _wireStartPos.X,
                    StartY = _wireStartPos.Y,
                    EndX = tapPos.X,
                    EndY = tapPos.Y,
                    NetId = targetWire.NetId,     // Inherits parent NetId!
                    FlipCorner = startIsVertical
                };
            }
            else
            {
                // WireTap -> WireTap
                wire = new CircuitWire
                {
                    Id = $"W_{Guid.NewGuid().ToString().Substring(0, 6)}",
                    StartElementId = _wireStartWire!.Id,
                    StartPinName = "WireTap",
                    EndElementId = targetWire.Id,
                    EndPinName = "WireTap",
                    StartX = _wireStartPos.X,
                    StartY = _wireStartPos.Y,
                    EndX = tapPos.X,
                    EndY = tapPos.Y,
                    NetId = _wireStartWire.NetId,
                    FlipCorner = false
                };
            }

            Wires.Add(wire);
            WireCreated?.Invoke(wire);
            StatusMessageChanged?.Invoke($"已接入导线支路 (T形节点: Net {wire.NetId})");

            ResetDrawingWireState(e.Pointer);
            return;
        }

        // Clicked empty canvas space while drawing: cancel
        ResetDrawingWireState(e.Pointer);
        return;
    }

    // =========================================================================
    // 2. WIRE CONNECTION INITIATION (FIRST CLICK)
    // =========================================================================
    var startPinHit = FindPinAt(worldPos);
    var startTapHit = FindWireTapAt(worldPos, tolerance: 8.0f);

    // Clicked Pin: start from pin
    if (startPinHit != null)
    {
        _isDrawingWire = true;
        _wireStartKind = WireEndpointKind.Pin;
        _wireStartElement = startPinHit.Value.Element;
        _wireStartPin = startPinHit.Value.Pin;
        _wireStartPos = _wireStartElement.GetPinWorldPosition(_wireStartPin);
        _currentWireEnd = _wireStartPos;

        Canvas.CapturePointer(e.Pointer);
        StatusMessageChanged?.Invoke($"正在连接导线: 从 {_wireStartElement.Id}.{_wireStartPin.Name} 出发，点击目标引脚或导线完成连接，按 Esc 取消");
        Canvas.Invalidate();
        return;
    }

    // Clicked Wire in WireMode: start from Wire Tap!
    if (IsWireMode && startTapHit != null)
    {
        _isDrawingWire = true;
        _wireStartKind = WireEndpointKind.WireTap;
        _wireStartWire = startTapHit.Value.Wire;
        _wireStartSegmentIndex = startTapHit.Value.SegmentIndex;
        _wireStartPos = startTapHit.Value.TapPoint;
        _currentWireEnd = _wireStartPos;

        Canvas.CapturePointer(e.Pointer);
        StatusMessageChanged?.Invoke($"正在从导线支路 (Net {_wireStartWire.NetId}) 引出走线，点击目标引脚或导线完成连接，按 Esc 取消");
        Canvas.Invalidate();
        return;
    }

    // =========================================================================
    // 3. SELECTION & MANIPULATION (WIRES, ELEMENTS, PANNING)
    // =========================================================================
    // Check Wire Selection
    CircuitWire? hitWire = null;
    for (int i = Wires.Count - 1; i >= 0; i--)
    {
        if (Wires[i].HitTest(worldPos, tolerance: 8f))
        {
            hitWire = Wires[i];
            break;
        }
    }

    if (hitWire != null)
    {
        SelectedWire = hitWire;
        SelectedElement = null;
        ElementSelected?.Invoke(null);
        UpdateSelectionBar(null);

        _isDraggingWire = true;
        _draggedWire = hitWire;
        _dragStartPointerPos = worldPos;
        Canvas.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
        StatusMessageChanged?.Invoke($"已选中导线 · 拖拽拐角可直接调整走线 · 按 [Del] 键删除");
        Canvas.Invalidate();
        return;
    }

    // Check Element Selection
    CircuitElement? hitElement = null;
    for (int i = Elements.Count - 1; i >= 0; i--)
    {
        if (Elements[i].HitTest(worldPos))
        {
            hitElement = Elements[i];
            break;
        }
    }

    if (hitElement != null)
    {
        SelectedElement = hitElement;
        SelectedWire = null;
        ElementSelected?.Invoke(hitElement);

        _isDraggingElement = true;
        _draggedElement = hitElement;
        _dragStartPointerPos = worldPos;
        _dragStartElementPos = new Vector2(hitElement.X, hitElement.Y);

        Canvas.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
        Canvas.Invalidate();
    }
    else
    {
        // Panning Canvas
        SelectedElement = null;
        SelectedWire = null;
        ElementSelected?.Invoke(null);

        _isPanningCanvas = true;
        _lastPointerPos = screenPos;
        Canvas.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Canvas.Invalidate();
    }
}

private void ResetDrawingWireState(Pointer pointer)
{
    _isDrawingWire = false;
    _wireStartKind = WireEndpointKind.None;
    _wireStartElement = null;
    _wireStartPin = null;
    _wireStartWire = null;
    _hoveredPin = null;
    _hoveredWireTap = null;
    Canvas.ReleasePointerCapture(pointer);
    Canvas.Invalidate();
}
```

---

#### B. `Canvas_PointerMoved` Implementation
Maintains real-time hover detection for wire taps (within 8px) and generates Manhattan routing preview endpoints:

```csharp
private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    var pt = e.GetCurrentPoint(Canvas);
    var currentScreenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
    var worldPos = ScreenToWorld(currentScreenPos);
    CursorCoordinatesChanged?.Invoke(worldPos.X, worldPos.Y);

    if (_isDraggingWire && _draggedWire != null)
    {
        // Handled by M3-3 corner dragging...
    }
    else if (_isDraggingElement && _draggedElement != null)
    {
        // Drag element and update wires
        var delta = worldPos - _dragStartPointerPos;
        float newX = MathF.Round((_dragStartElementPos.X + delta.X) / 10f) * 10f;
        float newY = MathF.Round((_dragStartElementPos.Y + delta.Y) / 10f) * 10f;

        _draggedElement.X = newX;
        _draggedElement.Y = newY;
        ElementMoved?.Invoke(_draggedElement);

        SyncConnectedWiresToElement(_draggedElement);
        Canvas.Invalidate();
    }
    else if (_isPanningCanvas)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        var delta = currentScreenPos - _lastPointerPos;
        _lastPointerPos = currentScreenPos;
        _translation += delta;
        Canvas.Invalidate();
    }
    else
    {
        // =====================================================================
        // Hover Detection: Pins and Wire Taps
        // =====================================================================
        _hoveredPin = FindPinAt(worldPos);
        _hoveredWireTap = (_hoveredPin == null)
            ? FindWireTapAt(worldPos, tolerance: 8.0f, referenceStart: _isDrawingWire ? _wireStartPos : null)
            : null;

        if (_isDrawingWire)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);

            if (_hoveredPin.HasValue)
            {
                _currentWireEnd = _hoveredPin.Value.Element.GetPinWorldPosition(_hoveredPin.Value.Pin);
            }
            else if (_hoveredWireTap.HasValue)
            {
                _currentWireEnd = _hoveredWireTap.Value.TapPoint;
            }
            else
            {
                // Micro-snap floating pointer to 10px grid
                _currentWireEnd = new Vector2(
                    MathF.Round(worldPos.X / 10f) * 10f,
                    MathF.Round(worldPos.Y / 10f) * 10f
                );
            }
            Canvas.Invalidate();
        }
        else if (_hoveredPin.HasValue)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            Canvas.Invalidate();
        }
        else if (_hoveredWireTap.HasValue)
        {
            ProtectedCursor = InputSystemCursor.Create(
                IsWireMode ? InputSystemCursorShape.Cross : InputSystemCursorShape.Hand);
            Canvas.Invalidate();
        }
        else
        {
            bool isOverElement = Elements.Any(el => el.HitTest(worldPos));
            bool isOverWire = Wires.Any(w => w.HitTest(worldPos, 6f));
            ProtectedCursor = (isOverElement || isOverWire)
                ? InputSystemCursor.Create(InputSystemCursorShape.Hand)
                : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            Canvas.Invalidate();
        }
    }
}
```

---

### 4.4 Interactive Visual Feedback & Win2D Rendering (`Canvas_Draw`)

Add the following drawing code to [`apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:Canvas_Draw`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitCanvas.xaml.cs:330):

```csharp
// =========================================================================
// In-Progress Wire Drawing with Manhattan Polyline Preview
// =========================================================================
if (_isDrawingWire)
{
    var p1 = _wireStartPos;
    var p2 = _currentWireEnd;

    // Determine Manhattan bend based on exit orientation
    bool startIsVertical;
    if (_wireStartKind == WireEndpointKind.Pin && _wireStartElement != null && _wireStartPin != null)
    {
        var dir = _wireStartElement.GetPinWorldDirection(_wireStartPin);
        startIsVertical = Math.Abs(dir.Y) > Math.Abs(dir.X);
    }
    else if (_wireStartKind == WireEndpointKind.WireTap && _wireStartWire != null)
    {
        // Perpendicular exit from tapped segment
        startIsVertical = _hoveredWireTap?.IsHorizontalSegment ?? true;
    }
    else
    {
        startIsVertical = Math.Abs(p2.Y - p1.Y) > Math.Abs(p2.X - p1.X);
    }

    var previewPts = new List<Vector2> { p1 };
    if (Math.Abs(p1.X - p2.X) > 6 && Math.Abs(p1.Y - p2.Y) > 6)
    {
        if (startIsVertical)
            previewPts.Add(new Vector2(p1.X, p2.Y));
        else
            previewPts.Add(new Vector2(p2.X, p1.Y));
    }
    previewPts.Add(p2);

    // Draw dashed preview line
    var strokeStyle = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
    for (int k = 0; k < previewPts.Count - 1; k++)
    {
        ds.DrawLine(previewPts[k], previewPts[k + 1], accentColor, 2f, strokeStyle);
    }

    // Start anchor dot
    ds.FillCircle(p1, 4.0f, accentColor);

    // Floating/Snapped target indicator
    if (_hoveredWireTap.HasValue)
    {
        // 3.8px Solder Preview Dot at tap junction
        ds.FillCircle(p2, 3.8f, accentColor);
        ds.DrawCircle(p2, 6.0f, accentColor, 1.8f);
    }
    else
    {
        ds.FillCircle(p2, 4.0f, accentColor);
    }
}

// =========================================================================
// Visual Tap Indicator (Pointer within 8px of an existing wire)
// =========================================================================
if (_hoveredWireTap.HasValue)
{
    var tapPos = _hoveredWireTap.Value.TapPoint;
    int netId = _hoveredWireTap.Value.NetId;

    // Outer snap ring
    ds.DrawCircle(tapPos, 6.0f, accentColor, 1.8f);

    // Solid EDA solder preview dot
    ds.FillCircle(tapPos, 3.8f, accentColor);

    // Tooltip / Net badge
    string badgeText = netId > 0 ? $"T型节点 (Net {netId})" : "T型节点 (未定义)";
    ds.DrawText(
        badgeText,
        tapPos.X + 8,
        tapPos.Y - 14,
        accentColor,
        new CanvasTextFormat
        {
            FontSize = 10,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }
        }
    );
}
```

---

### 4.5 Topological Child-Parent Dynamic Synchronization

To ensure tapped branch wires dynamically track their parent trunk wire during component dragging or corner adjustments, integrate `SyncTappedWires`:

```csharp
/// <summary>
/// Dynamically updates all branch wires tapped into the specified parent wire.
/// Recurses down child trees to maintain multi-tier branch hierarchies.
/// </summary>
public void SyncTappedWires(CircuitWire parentWire)
{
    foreach (var childWire in Wires)
    {
        if (childWire.Id == parentWire.Id) continue;

        // Child tapped at start
        if (childWire.StartPinName == "WireTap" && childWire.StartElementId == parentWire.Id)
        {
            var oldStart = new Vector2(childWire.StartX, childWire.StartY);
            var updatedTap = GetNearestPointOnWire(parentWire, oldStart);
            childWire.StartX = updatedTap.X;
            childWire.StartY = updatedTap.Y;
            SyncTappedWires(childWire);
        }

        // Child tapped at end
        if (childWire.EndPinName == "WireTap" && childWire.EndElementId == parentWire.Id)
        {
            var oldEnd = new Vector2(childWire.EndX, childWire.EndY);
            var updatedTap = GetNearestPointOnWire(parentWire, oldEnd);
            childWire.EndX = updatedTap.X;
            childWire.EndY = updatedTap.Y;
            SyncTappedWires(childWire);
        }
    }
}
```

In `SyncConnectedWiresToElement(CircuitElement el)`, invoke `SyncTappedWires` after updating primary wires:

```csharp
private void SyncConnectedWiresToElement(CircuitElement el)
{
    var touchedWires = new List<CircuitWire>();
    foreach (var w in Wires)
    {
        if (w.StartElementId == el.Id)
        {
            var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
            if (pin != null)
            {
                var pPos = el.GetPinWorldPosition(pin);
                w.StartX = pPos.X;
                w.StartY = pPos.Y;
                touchedWires.Add(w);
            }
        }
        if (w.EndElementId == el.Id)
        {
            var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
            if (pin != null)
            {
                var pPos = el.GetPinWorldPosition(pin);
                w.EndX = pPos.X;
                w.EndY = pPos.Y;
                touchedWires.Add(w);
            }
        }
    }

    // Propagate position updates to all child branches tapped into touched wires
    foreach (var tw in touchedWires)
    {
        SyncTappedWires(tw);
    }
}
```

In `DeleteSelected()`, ensure cascade deletion of dependent taps:

```csharp
else if (SelectedWire != null)
{
    var w = SelectedWire;

    // Remove child branches tapped into this wire to prevent orphan floating wires
    var childTaps = Wires
        .Where(child => (child.StartPinName == "WireTap" && child.StartElementId == w.Id) ||
                        (child.EndPinName == "WireTap" && child.EndElementId == w.Id))
        .ToList();

    foreach (var tap in childTaps)
    {
        Wires.Remove(tap);
    }

    Wires.Remove(w);
    SelectedWire = null;
    StatusMessageChanged?.Invoke($"已删除导线及其关联的 {childTaps.Count} 条 T 型支路");
    Canvas.Invalidate();
}
```

---

## 5. Verification Method

### 5.1 Compilation & Test Verification
Run PowerShell commands from the repository root:

1. **WinUI C# Desktop Solution Build**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected Output*: Build succeeds with `0 个警告, 0 个错误`.

2. **Algorithm & Solver Test Suite**:
   ```powershell
   pytest services/engine/tests services/api/tests
   ```
   *Expected Output*: `33 passed, 1 warning`.

### 5.2 Manual Interactive Verification Checklist

| Test Case | Interaction Steps | Expected Result | Pass Criteria |
|:---|:---|:---|:---|
| **TC-1: Wire Mode Toggle & Tap Start** | Press `W` key or click "连线模式" toolbar button. Move cursor near an existing wire (e.g. VCC rail or GND rail). | Cursor changes to crosshair. Snap ring (6.0px) and solid solder preview dot (3.8px) appear at the snapped projection point. Tooltip displays `T型节点 (Net 0)`. | Visual snap marker renders cleanly within 8px of wire. |
| **TC-2: Wire-to-Pin Branch Creation** | While in Wire Mode, click on GND rail at $X=350, Y=480$. Drag cursor up towards $R_e$ Pin 2. Click Pin 2. | A new `CircuitWire` is created with `StartPinName = "WireTap"`, `StartElementId = railWire.Id`, `EndElementId = "Re"`, `EndPinName = "引脚 2"`. Solder dot appears at $X=350, Y=480$. | Branch wire connects orthogonally; solder dot rendered at tap; zero solder dots on $R_e$ pin. |
| **TC-3: Pin-to-Wire T-Tap Creation** | Click Pin 1 of $C_e$. Drag cursor down towards the emitter lead of $Q_1$. Hover within 8px of the wire. Click to connect. | End point snaps cleanly to the perpendicular drop point on the emitter wire. Wire created with `EndPinName = "WireTap"`, `EndElementId = emitterWire.Id`, `NetId = emitterWire.NetId`. | T-junction solder dot (3.8px) appears at the intersection. |
| **TC-4: Terminal Keep-Out Verification** | Attempt to click a wire tap within 5px of a transistor or resistor pin terminal. | System detects proximity $< 10\text{px}$ to pin, rejects wire tap, and treats interaction as a direct pin connection. | Zero solder dots are rendered over the pin terminal ring. |
| **TC-5: Rubber-Banding Wire Follow** | Drag resistor $R_e$ horizontally by +40px. | Primary wire attached to $R_e$ moves. The branch wire tapped into the Emitter trunk smoothly adjusts its tap coordinate via `SyncTappedWires`. | Solder dot moves in real time; zero diagonal line distortion or orphan detached lines. |
| **TC-6: Netlist Extraction Verification** | Run `NetlistExtractor.ExtractNetlist(Elements, Wires)`. | All components attached to the trunk and tapped branches share identical `NetId`. | `ElectricalNetlist` contains exact single unified net with all connected pins grouped together. |
