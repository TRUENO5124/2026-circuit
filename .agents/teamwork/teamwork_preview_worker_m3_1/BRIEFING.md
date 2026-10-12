# BRIEFING — 2026-10-12T07:20:00Z

## Mission
Implement Milestone 3: Industrial EDA multi-branch wire routing and canvas render architecture (R3).

## 🔒 My Identity
- Archetype: worker
- Roles: [implementer, qa]
- Working directory: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m3_1
- Original parent: 56c39e6d-499a-4720-aa9d-4425f28b10ba
- Milestone: Milestone 3 (Industrial EDA Wire Routing & Canvas Architecture - R3)

## 🔒 Key Constraints
- Exclusive file write ownership:
  - `apps/desktop-winui/Controls/CircuitCanvas.xaml.cs`
  - `apps/desktop-winui/Models/CircuitElement.cs`
  - `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs`
  - `apps/desktop-winui/Services/CircuitTemplateFactory.cs`
- Integrity Mandate: Genuine implementation, no hardcoded results/dummy facades/cheating.
- All verification commands must pass:
  - `dotnet build apps/desktop-winui/CircuitAssistant.csproj /p:NoWarn=MVVMTK0045` (0 errors, 0 warnings)
  - `python tests/verify_templates.py` (100% pass)
  - `python tests/adversarial_template_tests.py` (100% pass)
  - `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj` (100% pass)
  - `pytest services/engine/tests services/api/tests` (33 passed)
  - `npm run check` (100% pass)

## Current Parent
- Conversation ID: 56c39e6d-499a-4720-aa9d-4425f28b10ba
- Updated: not yet

## Task Summary
- **What to build**:
  1. CircuitCanvas.xaml.cs: Solder dot rendering (3.8px solid dot, Euclidean terminal keepout > 5.0px, genuine T/multi-way junctions), interactive wire tap (hover snap marker, WireTap branch creation), orthogonal corner/segment dragging with 10px grid micro-snapping.
  2. CircuitElement.cs: CircuitWire corner hit test, segment hit test, Manhattan path preservation.
  3. AnalysisStudioViewModel.cs: Two-phase wire synchronization (Phase 1: pin-connected wire leads, Phase 2: perpendicular slide projection along parent bus rails without tilting rail).
  4. CircuitTemplateFactory.cs: Clean template wire topology without phantom bindings, strictly 1 lead per pin, GND pin tie, offset emitter ground tap in RC_Coupled_Amp.
- **Success criteria**: All template verification scripts and test suites pass, 0 compile errors.
- **Interface contracts**: PROJECT.md & original requirements.
- **Code layout**: apps/desktop-winui/...

## Key Decisions Made
- [TBD]

## Artifact Index
- DISPATCH.md — Assignment instructions
- BRIEFING.md — Situational awareness
- progress.md — Liveness heartbeat
- handoff.md — 5-component handoff report

## Change Tracker
- **Files modified**: None yet
- **Build status**: Untested
- **Pending issues**: None

## Quality Status
- **Build/test result**: Untested
- **Lint status**: Clean
- **Tests added/modified**: TBD

## Loaded Skills
- None
