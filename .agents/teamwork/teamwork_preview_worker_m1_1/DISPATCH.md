## 2026-10-11T19:34:07Z
You are Worker M1-1 for Milestone 1 (Educational Templates & Model Modernization - R2).
Your working directory is: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m1_1

MANDATORY FIRST STEP:
Read the authoritative user request at: d:/BJTU/2026年大创/.agents/teamwork/ORIGINAL_REQUEST.md
Also read PROJECT.md at: d:/BJTU/2026年大创/PROJECT.md
Also read the 3 Explorer handoff reports:
- d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m1_1/handoff.md
- d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m1_2/handoff.md
- d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m1_3/handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE FILE WRITE OWNERSHIP:
You have exclusive write ownership of:
- `apps/desktop-winui/Services/CircuitTemplateFactory.cs` (New file)
- `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`
- `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`

MISSION & REQUIREMENTS:
1. Create `apps/desktop-winui/Services/CircuitTemplateFactory.cs` (namespace `CircuitAssistant.Services`):
   - Implement `CircuitTemplateData` record with `ProjectName`, `ProjectFileName`, `List<CircuitElement> Elements`, `List<CircuitWire> Wires`.
   - Implement `CreateCommonEmitterTemplate(...)`:
     - 12 elements (vs, C1, Rb1, Rb2, Q1, Rc, Re, Ce, C2, RL, VCC, GND).
     - Standard GB/T 4728 & IEC 60617 layout: top VCC rail at Y=80, bottom GND rail at Y=480, horizontal signal line at Y=260.
     - Re at X=320 and Ce at X=420 (100px separation, zero text label overlap).
     - Zero wire piercing: Q1, C1, vs terminals strictly aligned.
     - VCC source at (620, 280) feeding the rail at right, removing old overlapping source circle on the rail.
     - 23 declarative `CircuitWire` models with accurate coordinates, pins, and NetIds.
   - Implement `CreateRcTransientTemplate(...)`:
     - 4 elements (VS, R1, C1, GND), 5 declarative wires.
     - 1st-order series RC charging circuit feeding Milestone 2 analytical solver.
   - Implement `CreateRcCoupledTemplate(...)`:
     - 9 elements (vs, C1, Rb, Q1, Rc, C2, RL, VCC, GND), 16 declarative wires.
     - Dedicated fixed-bias RC-coupled amplifier, fixing the reload bug.
2. Update `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`:
   - Refactor `InitializeDefaultCircuitElements()` and `CreateNewProject(string templateType)` to use `CircuitTemplateFactory`.
   - Populate BOTH `Elements` and `Wires` (`Wires.Clear()` followed by adding all template wires).
   - Support `"common_emitter"` (CommonEmitter_Amp.sch), `"rc_transient"` (RC_Transient.sch), `"ac_coupled"` (RC_Coupled_Amp.sch), and `"blank"` (Blank_Circuit.sch).
   - Ensure parameter synchronization works cleanly with the new templates.
3. Update `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`:
   - Remove lines 448–451 (`if (Wires.Count == 0) DrawCircuitWires(...)`).
   - Remove or deprecate `DrawCircuitWires` since all templates now populate `Wires` directly with `CircuitWire` models that are automatically rendered by Win2D with EDA solder dots.
4. Verify Compilation and Tests:
   - Run `dotnet build apps/desktop-winui/CircuitAssistant.csproj` (must compile with 0 warnings, 0 errors).
   - Run `pytest services/engine/tests services/api/tests` (all 33 tests must pass).
5. Document changes and test results in `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m1_1/handoff.md`, and report completion back to the orchestrator via send_message.
