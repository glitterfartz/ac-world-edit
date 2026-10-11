# SmartAI, scripting and IDE integration

## Verified starting point
The core has SmartAI loader/validator/executor code, C++ script/module registration, per-subsystem reload commands and optional remote command interfaces. No installed Lua module was observed. The former mod-eluna URL redirects to ALE; its README explicitly describes incompatibility with original Eluna and points to a separate compatibility fork. Select scripting backend by detected installed module/revision, not by a universal Eluna checkbox. [A2/A4](references/source-references.md#a2-behavior-and-relationships), [ALE](https://github.com/azerothcore/mod-ale), [Eluna API](https://elunaluaengine.github.io/)

## SmartAI authoring model
Use a lossless SmartScriptDocument of raw rows plus typed views. Owner key is signed entryorguid plus source_type; template owner positive, spawn-specific owner negative where supported. Preserve event id, link, all six event/action parameters, four target parameters and explicit target position. Local schema PK includes link; graph node identity must preserve full row keys and runtime event identifiers.

Source confirms owner validation by creature/GO/trigger/timed-list type and checks in IsTargetValid/IsEventValid. Compile metadata against pinned enums/validation branches. Core extensions may have extra event flags/action codes; unknown rows appear as expert-only immutable cards, not dropped rows. [SmartScriptMgr](references/source-references.md#a2-behavior-and-relationships)

Recommended cards:
```text
When: On entering combat
Availability: phase and chance, source-compatible flags
Do: Cast [spell reference], with source-supported cast options
Target: Current victim
Then: Linked event or timed action list (explicit)
```
This is a conceptual example, not a guaranteed event/action numeric encoding. Palette labels and parameter units must come from profile metadata.

Each card has warnings, raw-row view and source provenance. Reordering visual cards changes only layout unless the operation explicitly changes runtime ID/link/timed order. Layout metadata stays in project files. Export rewrites the complete owner/source block and associated owned dependencies per AC policy.

## Conditions and event semantics
Represent ConditionExpression as OR groups of AND predicates with typed context targets and optional negation/reference groups. SourceGroup/SourceEntry/SourceId interpretation depends on SourceType. For SmartAI conditions, validate against that profile's event/source lookup rules. Shared references are clone-on-edit or explicitly shared edits; detect recursive references. Do not compile a generic boolean graph without checking it can express the source evaluator.

SmartAI phase masks are behavior state, not the spawn phaseMask field. Timers, event chance/flags, invocation state and action list delays must appear explicitly. Runtime traces need server instrumentation; drawing a graph animation locally is not proof the AI executed.

## Visual scripting boundary
SmartAI fits bounded reactive behaviors: speech, casting, movement, summons and source-supported sequencing. Complex encounter logic, novel server mechanics, custom state algorithms and unsupported hooks remain Lua/C++. A visual editor can help author recipes and references without replacing the scripting language.

First release supports a curated subset and lossless import of broader scripts. Expand support only with fixture round-trips and isolated runtime tests. Graph compiler tests must compare source rows/semantics, not just screenshots. Never automatically translate arbitrary C++/Lua into SmartAI.

## Lua/Eluna/ALE
Create separate ScriptingProfiles: engine name/version, Lua dialect, installed APIs/hooks, reload method, script paths and diagnostics. ALE versus Eluna requires different completion stubs/examples. Compatibility fork existence is verified, but its build parity with this checkout was not tested. Capability detection can inspect configured module manifests/source, then user chooses explicit profile where ambiguous.

Use LuaLS with generated/curated API annotations from the installed backend and configured Lua version. Server engine objects/functions are absent in a plain local Lua interpreter; mocks can test pure logic but cannot certify server hooks. Reload availability and state retention are module-version dependent. Restart test server by default until reload behavior is proved.

## C++ workflow
Open symbol/ScriptName in existing IDE and configured source checkout. Modules are preferred for custom content to reduce core fork maintenance. Maintain registration and module build files as ordinary reviewed source changes. Provide a build task using the existing CMake build preset/directory/toolchain, never an assumed one-size Windows command. Source introspection can map ScriptName to registration but must handle aliases/dynamic registration and unresolved names.

Compile/restart is required for ordinary C++ changes; no hot-reload promise. clangd uses compile_commands.json where available; multi-config MSVC compilation database generation may need an additional configured toolchain or IDE-native symbol service. Do not require switching server compiler merely for code completion. [clangd](https://clangd.llvm.org/)

## Integrated IDE stages — proposed
1. MVP/Phase 2: Open Script/File/Symbol in external VS Code or Visual Studio; source path and build log hyperlinks.
2. Phase 3: editor text view, syntax highlighting and LSP diagnostics/completion/navigation through supervised LuaLS/clangd subprocesses.
3. Later: source patch review, module scaffolding and test/build orchestration, only in explicit project-owned source workspace.
4. Optional debugging: DAP client talks to an appropriate debugger; LSP alone supplies no breakpoints/stack frames. Lua debugging needs backend hook/instrumentation or compatible debug module. C++ debugging requires symbols and supported attach permissions. Suspending a world update thread can invalidate timing or disconnect clients.

[LSP specification](https://microsoft.github.io/language-server-protocol/), [DAP specification](https://microsoft.github.io/debug-adapter-protocol/). Those protocols make integration possible; they do not supply an AzerothCore debugger.

## Build/test diagnostics
JobSpec contains trusted executable, argument list, working directory, env allowlist, cancellation policy, log parser and output artifact expectations. UI shows queued/running/completed/failed, command details and changes requiring rebuild/restart. Parse compiler path/line/column and SQL loader object IDs into links. Distinguish process exit success, server startup success and gameplay acceptance.

No default deployment to a running server. Test on named clone: export -> apply via established updater policy -> restart -> inspect SQL errors -> run in-game checklist. Existing reload smart_scripts calls the manager loader but does not establish that all existing actor AI state is reset. Support matrix must be experimentally verified for each subsystem.

## SmartAI proof corpus
Capture representative templates, negative-GUID owners, timed action lists, links, phase/chance/timing, condition references and a core-specific extended action. Expected gates: import/export unchanged data preserved; one changed card compiles a complete owner block; all links remain valid; unsupported values survive; deleted actor blocks do not orphan owned conditions; test server behavior and loader diagnostics agree.

## IDE decision
Embedding a full IDE is low priority. An external IDE plus entity-to-source navigation delivers most early value, avoids duplicating mature editors and preserves compiler/debugger workflows. Add embedded features only when their maintenance cost is justified by real content-authoring tasks.

