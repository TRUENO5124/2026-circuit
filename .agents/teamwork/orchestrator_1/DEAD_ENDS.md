# Dead Ends Log

| Iteration | Approach Tried | Why It Failed | Files Touched |
|---|---|---|---|
| M3 Iteration 2 | Adding a wired-pin guard only to Stage 4 (Direct Pin-to-Pin Snapping) in `NetlistExtractor.cs` to prevent 80px displacement net mergers | Ineffective because when an element like `RL` is moved +80px to X=620, its drop wire and pin land within 5px of `W_VCC_FEED` (X=620). Stage 2 (Wire-to-Wire) and Stage 3 (Pin-to-Wire) unconditionally merge them into the same disjoint-set net prior to Stage 4, making the Stage 4 guard unreachable (`root1 == root2` already true). | `apps/desktop-winui/Engine/NetlistExtractor.cs` |
