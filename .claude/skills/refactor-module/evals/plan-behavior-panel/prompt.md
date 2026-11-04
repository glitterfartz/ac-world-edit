---
tags: [skill-creator, refactor]
max_turns: 20
timeout_seconds: 600
allowed_tools: [Read, Glob, Grep, Write]
---

`Scripts/Ui/BehaviorPanel.cs` is around 590 lines and `BehaviorPanel.Rules.cs` around 669. It is getting hard to review, and the behavior editor is the part people are most afraid to touch. Plan a behaviour-preserving split that keeps the built UI identical and does not break the behavior smokes. Don't write code yet.

Write the plan to the output file requested by the run. Do not modify the repository.
