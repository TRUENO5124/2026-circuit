## 2026-10-11T19:20:44Z
[Message] timestamp=2026-10-11T19:20:44Z sender=56c39e6d-499a-4720-aa9d-4425f28b10ba priority=MESSAGE_PRIORITY_HIGH content=You are Explorer M1-1 for Milestone 1 (Educational Templates & Model Modernization - R2).
Your working directory is: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m1_1

MANDATORY FIRST STEP:
Read the authoritative user request at: d:/BJTU/2026年大创/.agents/teamwork/ORIGINAL_REQUEST.md
Also read PROJECT.md at: d:/BJTU/2026年大创/PROJECT.md
Also read the Survey 2 handoff report at: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_survey_2/handoff.md

MISSION:
Investigate and design the exact implementation plan for the Declarative Template Factory in `AnalysisStudioViewModel.cs` and the removal of the procedural `DrawCircuitWires` hack in `CircuitCanvas.xaml.cs`:
1. Design the declarative factory structure to populate both `ObservableCollection<CircuitElement> Elements` and `ObservableCollection<CircuitWire> Wires` for:
   - `CommonEmitter_Amp.sch`
   - `RC_Transient.sch` (brand new 1st-order RC charging/discharging template for R1/R2)
   - `RC_Coupled_Amp.sch` (fix bug where it reloads the common emitter circuit)
2. Detail the exact `CircuitWire` models required (StartX, StartY, EndX, EndY, StartElementId, StartPinName, EndElementId, EndPinName, FlipCorner, NetId) so the template has full topological fidelity.
3. Recommend how to safely remove `DrawCircuitWires` from `CircuitCanvas.xaml.cs` without breaking rendering.

CONSTRAINTS:
- You are read-only. Do NOT modify source code or tests.
- Deliver your detailed implementation plan to `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m1_1/handoff.md`.
- When done, call send_message to report completion to the orchestrator.
