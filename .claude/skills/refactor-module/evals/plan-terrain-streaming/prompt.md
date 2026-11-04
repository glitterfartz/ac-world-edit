---
tags: [skill-creator, refactor]
max_turns: 20
timeout_seconds: 600
allowed_tools: [Read, Glob, Grep, Write]
---

I want to shrink `Scripts/Viewer/Streaming` before adding tile LOD. `TerrainMinimap.cs` and the streamer feel tangled with the shell and I keep getting lost in the partial files. Give me a refactor plan I can hand to another agent: what to capture first, how to split the commits, what to extract and what to name it. Don't change any code yet.

Write the plan to the output file requested by the run. Do not modify the repository.
