# Host capability matrix

The scripting compiler targets a *host profile*: the semantic catalog plus one runtime's bindings.
The document names semantic nodes only, so switching the host re-validates it without rewriting the
graph. The full design is in [plans/scripting-plan.md](../plans/scripting-plan.md) and the catalog
format in [scripting-capabilities.md](scripting-capabilities.md).

| | `ale-azerothcore` | `eluna-trinitycore` |
|---|---|---|
| Label | ALE (AzerothCore) | Eluna (TrinityCore) |
| Status | verified against the pinned module and a live server | **proof profile, experimental** |
| Source | mod-ale `84b85cc` | upstream Eluna's Lua API (no live server in this environment) |
| Capabilities | 25 curated actions | same 25 bindings (mirrored) |
| Events | 230 generated, 217 implemented | same 230 definitions |
| Registrars | `RegisterCreatureEvent`, `RegisterMapEvent`, … | same names, Eluna's own hook enum ids |
| Object timers | `RegisterEvent(fn, delay, repeats, obj)` | `RegisterEvent` |
| Global timers | `CreateLuaEvent(fn, delay, repeats)` | `CreateLuaEvent` |
| Async queries | `WorldDBQueryAsync` | `WorldDBQueryAsync` |
| MySQL differences | AzerothCore `world` | TrinityCore `world` + `hotfixes` (no script-visible `hotfixes` yet) |

## What the proof demonstrates

- The IR and emitter are host-neutral: the creature-death graph compiles under both profiles with
  identical handler bodies and registrations.
- The catalog hash includes the host id and label, so exported provenance distinguishes profiles
  even when their bindings currently coincide.
- Switching the host records the choice on the document, re-filters the palette and recompiles; a
  node that a host cannot run produces `SC0104` (not available) or `SC0101` (unknown node).
- The UI lists Eluna with an **experimental** badge; its registrations are mirrored, not live-verified.

## Known gaps

- Eluna's hook enum ids are mirrored from the ALE-generated catalog. If upstream Eluna numbers an
  event differently, the profile needs its own `event-bindings.json` before it can be trusted beyond
  compilation.
- TrinityCore-specific capabilities (hotfix-backed items, `hotfixes` queries) are not exposed.
- The Eluna profile is not regenerated from an Eluna checkout; the catalog tool's `--check` gates
  ALE only. Adding a second source is the Milestone 7 catalog work.
- No live Eluna server was available, so runtime behavior (handler signatures, timer callbacks) is
  source-mirrored rather than verified.
