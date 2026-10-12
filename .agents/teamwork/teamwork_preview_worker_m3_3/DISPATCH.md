## 2026-10-12T04:08:14Z
You are Worker M3-3 (Remediation Worker) for Milestone 3 (Industrial EDA Wire Routing & Canvas Architecture - R3).
Your working directory is: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m3_3

MANDATORY FIRST STEP:
Read the authoritative user request at: d:/BJTU/2026年大创/.agents/teamwork/ORIGINAL_REQUEST.md
Also read PROJECT.md at: d:/BJTU/2026年大创/PROJECT.md
Also read the two Challenger handoff reports for Milestone 3 detailing the exact defects:
- d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_challenger_m3_1/handoff.md
- d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_challenger_m3_2/handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE FILE WRITE OWNERSHIP:
You have exclusive write ownership of:
- `apps/desktop-winui/Models/CircuitElement.cs`
- `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`
- `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`
- `apps/desktop-winui/Services/CircuitTemplateFactory.cs`
- `apps/desktop-winui/Engine/NetlistExtractor.cs`

MISSION & REQUIREMENTS:
Implement the remediation for all 3 defects uncovered by the Challengers:

1. Defect 1: Wire Slant / Non-Manhattan Angle on Dynamic Dragging
   - In `apps/desktop-winui/Models/CircuitElement.cs` (line 103) and `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (line 511):
     Change:
     `if (Math.Abs(p1.X - p2.X) > 6 && Math.Abs(p1.Y - p2.Y) > 6)`
     To:
     `if (Math.Abs(p1.X - p2.X) > 1.0f && Math.Abs(p1.Y - p2.Y) > 1.0f)`
   - In `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 879-880, 890-891, 910-911, 928-929) and `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs` (lines 1817-1818, 1828-1829, 1848-1849, 1866-1867):
     In `SyncConnectedWiresToElement`, do NOT coarsely round pin coordinates to 10px (`MathF.Round(pPos / 10f) * 10f`). Use exact `pPos.X` and `pPos.Y` (or 5px modulus) because component pins sit on CAD-defined 5px/25px/35px offsets.

2. Defect 2: Mutual Wire Tap Circularity & Trunk Rail Shrinkage Hysteresis
   - In `apps/desktop-winui/Services/CircuitTemplateFactory.cs` (lines 228, 240, 332, 502, etc.):
     Define `W_GND_RAIL` and `W_VCC_RAIL` as independent root trunk wires:
     `StartElementId = ""`, `StartPinName = "None"`, `EndElementId = ""`, `EndPinName = "None"` (or empty strings), NOT `StartElementId = "W_VS_GND"`, `EndElementId = "W_VCC_GND"` with `"WireTap"`.
     The feeder wires (`W_VS_GND`, `W_VCC_GND`, `W_VCC_FEED`) already connect to the rails via wire taps.
   - In `SyncConnectedWiresToElement` (both `CircuitCanvas.xaml.cs` and `AnalysisStudioViewModel.cs`):
     In Phase 2B, ensure trunk bus rails (e.g. wires with `Id.EndsWith("_RAIL")` or pure trunk wires) are NEVER updated as child branches of feeder lines, and when child wires re-project, ensure `newTap` projects the tapped end onto the parent wire without corrupting trunk coordinates.

3. Defect 3: Pin Abutment Net Merging during 80px Component Displacement
   - In `apps/desktop-winui/Engine/NetlistExtractor.cs` (lines 297-308):
     In Stage 4 (Direct Pin-to-Pin Snapping), guard the union: do NOT merge pins if either pin already has connected wires that belong to distinct electrical nets, unless the two pins are directly connected or intended to abut without wires.

4. Run and Verify Test Suites:
   - `dotnet build apps/desktop-winui/CircuitAssistant.csproj /p:NoWarn=MVVMTK0045` (0 warnings, 0 errors)
   - `python tests/verify_templates.py` (775/775 checks pass)
   - `python tests/adversarial_template_tests.py` (318/318 invariants pass)
   - `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj` (131/131 tests pass)
   - `python tests/test_challenger_m3_1.py` (ALL 18,473 checks MUST PASS!)
   - `python tests/test_adversarial_m3_stress.py` (ALL stress tests MUST PASS!)
   - `pytest services/engine/tests services/api/tests` (33 passed)
   - `npm run check` (100% passed)

5. Document all changes, files touched, and test outputs in `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m3_3/handoff.md`.
Report completion back to the orchestrator via send_message.
