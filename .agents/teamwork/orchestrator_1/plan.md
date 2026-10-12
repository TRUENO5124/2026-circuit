# Execution Plan: CircuitAssistant Comprehensive Refactoring

## Overview
Refactor CircuitAssistant to industrial EDA standards (Altium Designer / KiCad / SPICE) across three key pillars:
1. R1: Industrial Electrical Topology Analysis & Solving Strategy Engine Refactoring (Netlist extraction, topology classification, transient/frequency response, MNA formulation, LaTeX & scientific notation formatting).
2. R2: Industrial Standard Educational Template Circuit Diagram Refactoring (GB/T 4728 & IEC 60617 conventions, rail alignment, signal flow, 80/160 mil grid layout, zero interference/overlap).
3. R3: Industrial EDA Multi-branch Wire Routing & Render Architecture Refactoring (Bus/Trunk routing, T-junction solder dots, wire tap, orthgonal snapping, rubber-banding).

## Execution Steps

### Phase 0: Survey & Scope Mapping (Current)
- Dispatch 3 Explorers (specializing in R1 engine, R2 templates, R3 routing & UI canvas) to investigate the existing codebase structure, project dependencies, data models, canvas interactions, solver implementations, and build/test configuration.
- Synthesize findings into `PROJECT.md` with full architecture, feature inventory, code layout, interface contracts, and milestone breakdown.

### Phase 1: Milestone 1 - Electrical Topology Analysis & Solving Engine (R1)
- Subagent iteration loop:
  - Explorers map detailed netlist extraction & MNA / topology classifier requirements.
  - Worker implements graph connectivity, Netlist model, TopologyClassifier (1st-order RC/RL with Thevenin Req, tau, time-domain solution, H(s), fc; 2nd-order RLC resonance omega0, alpha, Q; BJT/MOSFET bias & small signal Q-point, r_be/gm, Av, Ri, Ro, Bode; MNA matrix equation Gv + Bi = J; LaTeX + scientific notation formatting via MathFormulaView).
  - Reviewers (2) verify electrical correctness, numerical accuracy, LaTeX formula generation, and regression avoidance.
  - Challengers (2) stress test with edge cases (pure RC, RLC, BJT, pure linear resistive, invalid topologies).
  - Auditor checks code integrity, ensuring genuine mathematical solver algorithms without hardcoded shortcuts.
  - Orchestrator gate evaluation.

### Phase 2: Milestone 2 - Educational Circuit Templates (R2)
- Subagent iteration loop:
  - Explorers map schema file format, coordinate system, template loading mechanism, and existing flaws in `CommonEmitter_Amp.sch` and RC circuits.
  - Worker reconstructs template files with strict GB/T 4728 & IEC 60617 standards, 80/160 mil grid alignment, left-to-right flow, top VCC / bottom GND rails, zero component/text/wire overlap.
  - Reviewers (2) verify visual layout, netlist connectivity, and standard adherence.
  - Challengers (2) verify template loading, netlist extraction, and solving compatibility.
  - Auditor checks template data authenticity.
  - Orchestrator gate evaluation.

### Phase 3: Milestone 3 - Multi-branch Wire Routing & Canvas Rendering (R3)
- Subagent iteration loop:
  - Explorers map canvas wire routing data model, junction detection, mouse event handlers, dragging/rubber-banding logic, and solder dot rendering.
  - Worker implements single-trunk out-lead + orthogonal T-tap branching, 3.8px solder dots at genuine branch junctions, clear terminal keep-outs, wire tap interaction, corner dragging, and component movement rubber-banding.
  - Reviewers (2) verify EDA drawing aesthetics, interaction feel, and data model consistency.
  - Challengers (2) verify drag-and-drop robustness, complex multi-branch network integrity, and corner cases.
  - Auditor checks UI/rendering code integrity.
  - Orchestrator gate evaluation.

### Phase 4: Milestone 4 - Dual-Track E2E Testing, Integration & Final Acceptance Verification
- Full solution build verification (`CircuitAssistant.csproj`, 0 errors).
- Automated tests covering T1-T4 test cases across all features.
- Verification against all acceptance criteria:
  1. Pure RC circuit solver generates solely 1st-order RC derivations without any BJT parameters.
  2. Transistor circuit accurately identifies BJT topology, Q-point, and small-signal parameters.
  3. Formula view correctly formats LaTeX and scientific notation.
  4. Template circuit `CommonEmitter_Amp.sch` renders flawlessly without overlaps or piercing wires.
  5. Multi-node wiring renders single-trunk + T-tap solder dots cleanly.
  6. Solution builds with 0 errors and application operates stably.
- Final forensic audit & sentinel reporting.
