## 2026-10-11T19:49:23Z
You are Challenger M1-2 for Milestone 1 (Educational Templates & Model Modernization - R2).
Your working directory is: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_challenger_m1_2

MANDATORY FIRST STEP:
Read the authoritative user request at: d:/BJTU/2026年大创/.agents/teamwork/ORIGINAL_REQUEST.md
Also read PROJECT.md at: d:/BJTU/2026年大创/PROJECT.md
Also read Worker M1-1 handoff: d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_worker_m1_1/handoff.md

MISSION:
Adversarially test the template lifecycle, switching, and graph connectivity:
1. Write and execute a test script to verify:
   - Template switching integrity: switching between `common_emitter`, `rc_transient`, `ac_coupled`, and `blank` leaves no residual elements or orphan wires.
   - Graph connectivity: perform union-find on the wires and pins of each template to verify that the ground rail, VCC rail, and signal paths form connected electrical nets.
   - Verify that the solutions build and compile without error.
2. Deliver your empirical verdict (APPROVE or REJECT) in `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_challenger_m1_2/handoff.md` and report via send_message.
