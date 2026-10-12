## 2026-10-11T23:11:04Z
You are Explorer M3-3 for Milestone 3 (Industrial EDA Wire Routing & Canvas Architecture - R3).
Your working directory is: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m3_3

MANDATORY FIRST STEP:
Read the authoritative user request at: d:/BJTU/2026年大创/.agents/teamwork/ORIGINAL_REQUEST.md
Also read PROJECT.md at: d:/BJTU/2026年大创/PROJECT.md
Also read Survey 3 handoff report at: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_survey_3/handoff.md
Also inspect `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`, `apps/desktop-winui/Models/CircuitElement.cs`, and `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`.

MISSION:
Design Orthogonal Corner/Segment Dragging & Rubber-Banding Wire Follow:
1. Wire Corner & Segment Dragging:
   - Analyze how `CircuitWire.GetPathPoints()` and `CustomMid` / `FlipCorner` operate.
   - Design interactive manipulation: when a wire is selected, detect dragging on its corner or middle segment.
   - Enforce Manhattan orthogonality and 10px grid micro-snapping (`MathF.Round(coord / 10f) * 10f`).
2. Component Dragging & Rubber-Banding:
   - Analyze `SyncConnectedWiresToElement(CircuitElement el)` in `AnalysisStudioViewModel.cs` and `CircuitCanvas.xaml.cs`.
   - When an element is dragged, dynamically stretch attached wires to follow pin world positions without tearing, diagonal distortion, or leaving orphan taps.
   - Protect trunk bus rails from distortion when components tap into them.

CONSTRAINTS:
- You are read-only. Do NOT modify source code or tests.
- Maintain progress.md in your working directory.
- Deliver your detailed design to `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m3_3/handoff.md`.
- When done, call send_message to report completion to the orchestrator.
