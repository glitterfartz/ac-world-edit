# Sniff import proposal

> **Research only. Status: not started.** This page is a design proposal, not a plan of record. Agents must not implement any part of it unless explicitly asked to. Don't use it to justify changes elsewhere, and don't add it to `../status/open-work.md` without being asked.

How the editor could take packet sniffs, parsed by WowPacketParser (WPP, `X:\PrivateServer\WowPacketParser`), and turn them into spawns, creature data, paths, quests and scripting help. Scope is TrinityCore only; AzerothCore won't use sniffs this way.

## Why not import WPP's SQL

WPP's SQL is its last, lossy stage:

- Spawn rows use placeholder GUIDs (`@CGUID+n`, `@OGUID+n`), and paths start with `SET @MOVID := SET_VALUE_MANUALLY_HERE;`.
- Its guesses become commented-out rows ("might be temporary spawn", "already present in database"). Existing spawns are matched by entry, map and a fixed 0.02 position tolerance (`SQL/Builders/Spawns.cs`).
- Path output (`SQL/Builders/Movement.cs`) collects every spline for a mover without cleanup.

WPP's **UniversalProto** output (`DumpFormat` 14 or 15, `.dat`) is a typed packet stream defined in `WowPacketParser.Proto/PacketStructures/structures.proto`. WPP's newer builders consume it themselves (`Movement.cs` is a `PacketProcessor<PacketMonsterMove>`). The base parsers and the V7, V11 and V12 modules fill it (`packet.Holder.*`), and WPP supports 12.1.5.70575, the retail build this project targets. Sniff and client builds must match, or DB2 and hotfix IDs won't resolve.

## Pipeline

```
.pkt ──WPP (DumpFormat 14)──► .dat ──SniffReader──► Observations ──Reconcile(DB, client)──► Proposals
                                                                                  │ author accepts or edits
                                                                                  ▼
                                                              InteractionDraft RowEdits ──► SqlExport (unchanged)
```

Sniffs are evidence, not edits. Drafts stay the only source of SQL, so the read-only guarantee in `../architecture/editing-and-export.md` holds.

1. **Reader** (Godot-free, e.g. `Scripts/Data/Sniff/`). C# generated from `structures.proto` (Google.Protobuf). It groups packets into per-object observations:
   - from `CreateObject`: position, CreateObject1 vs 2, entry, display, faction, flags, auras, emote, stand state, mount, visible items
   - `MonsterMove` splines, chat, emotes and spell casts
   - gossip, vendor, trainer and quest-giver traffic
   - destroy and out-of-range events
   - the player's position over time
2. **Reconciliation** (pure Foundation code, unit-testable). Match observed objects to database spawns by entry, map and distance. Don't match by sniff GUID, because retail GUID counters aren't stable between sessions. Use a radius that allows for wandering and a nearest-neighbour assignment, not a fixed tolerance.
3. **Proposals to drafts.** An accepted proposal becomes ordinary `RowEdit`s through `IWorldSchema` / `TcTables`; export, guards and undo are unchanged.

## Tracking what isn't known

Every proposed field carries a provenance state, stored in a sidecar sniff-project JSON (not in drafts or SQL):

| State | Meaning | Examples |
|---|---|---|
| Observed | Read from packets, linked to sniff file and packet number | position, display, equipment, auras, gossip menu and text, vendor list, chat text and BroadcastTextID |
| Inferred | Heuristic with a confidence | temporary spawn (CreateObject2, short lifetime, summoner), wander distance (point spread), respawn time (destroy then re-create), waypoint delays (arrival to departure) |
| Conflicting | Sniffs disagree | different equipment or phase between captures |
| Unknowable | Not visible to the client | loot, pools, conditions, server-only spells, AI logic, phase triggers |

**Coverage map.** Draw the player's route with visibility radius on the terrain. A database spawn only counts as missing if it wasn't seen inside covered ground; elsewhere it is "not observed". Without coverage, absence of evidence reads as evidence of absence.

**Spawn statuses** in a ghost overlay: Matched, Displaced, New, Missing (inside coverage only), Temporary?, Unknown entry. Authors can bulk-accept by status inside a selected rectangle.

Several sniffs merge their evidence: confidence rises when observations agree, and disagreements become Conflicting.

## Per-area automation

**Spawns.** `creature` and `creature_addon` (auras, emote, stand state, mount), `creature_equip_template` from visible items, and per-field template differences the author accepts one at a time. Spawn inserts now exist (Phase 11): use a reserved GUID range per project and give drafts concrete GUIDs rather than `@CGUID` variables, since addon rows, paths and formations refer to the GUID. The export preflight (`COUNT(*)=0`) can confirm the range is still free.

**Paths** (the waypoint layer is built; node authoring is Phase 14 in [`editor-gap-plan.md`](editor-gap-plan.md)). The editor does the cleanup WPP leaves to the author:
- drop combat and chase splines (in-combat flag, splines aimed at the player, look-target splines)
- find the loop and keep one lap
- derive node `Delay` from timestamps and stop facing from `LookOrientation`
- map the Cyclic and Flying spline flags to `waypoint_path.MoveType` / `Flags`
- simplify packed points (e.g. Douglas–Peucker)
- check Z against terrain heights to catch points under the ground
- detect group movement (synchronised splines at fixed offsets) and propose `creature_formations` rows

**Services and quests.** Gossip menus and options, vendor inventory and trainer lists map onto the existing TrinityCore creature pages (Phase 6). Quest-giver and quest-query traffic feeds the Phase 7 quest editor and starter/ender links.

**Scripting** (after Phase 8). Sniffs record outcomes, not logic, so the editor helps the author write scripts rather than generating them:
- a per-creature timeline: spawn, combat, chat, casts, aura changes, movement stops, despawn, player actions
- "Make SmartAI from selection" proposes rows: repeating out-of-combat events with min/max timing from observed intervals, `creature_text` via BroadcastTextID, accepted-quest and gossip-select triggers
- action→reaction pairs (a player action followed by an NPC reaction within a short window) across several sniffs, with confidence rising as more sniffs agree

WoWDatabaseEditor's packet viewer does similar action→reaction analysis on the same `.dat` format and is worth studying as prior art (unverified).

## Limits

- Temporary vs permanent spawns is always a heuristic; keep it a reviewable flag.
- Flying Z values and personal or quest phases need the author to decide. The sniff records phase IDs, not the condition that sets them.
- Stats, loot, pools and conditions don't appear in sniffs; list them as Unknowable rather than guessing.

## Possible order

1. Reader, observation model, reconciliation, ghost overlay and coverage map. Read-only and useful on its own.
2. Spawn inserts with a reserved GUID range.
3. Accepting `creature`, `creature_addon` and equipment proposals, plus gossip, vendor and trainer data through the Phase 6 pages.
4. Paths with Phase 9, quests with Phase 7, timeline and SmartAI with Phase 8.
