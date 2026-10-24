---
name: check-layering
description: Verify the Godot-free folders (Foundation/, Scripts/Assets/, Scripts/Data/) contain no Godot references, then run the default xUnit suite. Use after editing those folders or before committing.
---

# Check layering

`Foundation/`, `Scripts/Assets/**` and `Scripts/Data/*.cs` are compiled directly by `Tests/ACWorldEdit.Tests`, which does not reference Godot. A Godot reference there breaks the test build.

1. Search those paths for Godot references (use the Grep tool, `*.cs` only):
   - pattern `using Godot|\bGodot\.|\bGD\.|\bNode3D\b|\bVector3\b|ProjectSettings|res://|user://`
   - paths `Foundation`, `Scripts/Assets`, `Scripts/Data`
   - Ignore matches in comments only if clearly prose; anything else is a violation. Report each as `file:line`.
2. Run the fast suite: `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`.
3. Report violations and the pass/fail/skip counts. Do not fix violations unless asked.
