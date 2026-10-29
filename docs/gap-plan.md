# World-editor gap plan: from overlays to authoring

## Context

ACWorldEdit can already **inspect** most of a zone and **deeply edit** four things: creature properties (template, stats, vendor, gossip, trainer, quest links), quests (the five quest tables), SmartAI and speech (`smart_scripts`, `creature_text`, AreaTrigger rules), and graveyards, with waypoint nodes editable in a limited way. Everything else is read-only or invisible. The most important gap is that **nothing can be created or removed in the world**: creatures and GameObjects can only be *moved* (before Phase 10 the pose drafts recorded poses only), and no spawn-level column (respawn, wander, phase, equipment, overrides) is editable.

TrinityCore's `world` has about 230 tables (TDB 1210.26091; counts below come from the live DB). This plan sorts them by domain, separates what designers touch from server configuration, and plans the missing editors as **viewport-first tools** in the spirit of Unity and Unreal: a content browser, drag-to-place, prefab/instance overrides, gizmos, graphs and timelines. The plan deliberately avoids table grids.

Decisions taken with the user:
- **Every phase ships both dialects** (AzerothCore + TrinityCore). Where a system exists only in TrinityCore (spawn groups, conversations, scenes, player choice, spawned AreaTrigger templates), the AzerothCore half is "not applicable" and says so in the UI.
- **Hotfix-backed editors are in scope as late phases**: a hotfix export path, then text, then items. Spells stay out of scope.

Phases continue the numbering in `docs/tc-status.md` (1–9 are done), so the first new phase is 10. Each phase lands as rows in `tc-status.md` (no separate roadmap doc), with tests after the feature works and a visual check in Godot.

---

## Coverage inventory

| Domain | Main tables (TC; AC differences in brackets) | Today | Gap |
|---|---|---|---|
| Creature spawns | `creature` (260k), `creature_addon`, `spawn_group`, `linked_respawn` | Move only | **No place, duplicate or delete; no spawn fields; no addon (auras, emote, mount, stand state)** |
| Creature templates | `creature_template*`, `_difficulty`, `_model`, `_spell`, `_resistance`, `_movement`, `creature_equip_template`, `creature_onkill_reputation`, `creature_immunities`, `creature_template_addon`, `creature_summon_groups`, `npc_spellclick_spells`, `vehicle_template_accessory` | Template, stats, vendor, gossip, trainer, quests, behavior, speech | Models, equipment, spells, addon, on-kill rep, immunities, movement, spell-click, vehicles, summon groups; **no new template** |
| GameObjects | `gameobject` (103k), `gameobject_template` (83k; Data0–34 [AC: 0–23]), `_template_addon`, `gameobject_addon`, quest starter/ender/questitem | Move only | **No properties editor at all**; per-type Data fields; GO quest givers |
| Loot | 13 `*_loot_template` tables (`creature_loot_template` 311k, `reference_loot_template` 14k) [AC: separate `Reference` column; TC: `ItemType` 0 item / 1 reference / 2 currency / 3 tracking quest] | LootID visible on the difficulty page | **Not read or edited** |
| Conditions | `conditions` (17k: gossip 5.4k, loot 3.2k, spell targets 3.3k, SmartAI 1.3k, phase 1k, quest 0.8k, …) | Counted for quests; graveyard team rows only | **No condition editor**: the largest cross-cutting gap |
| Movement | `waypoint_path(_node)` [AC: `waypoint_data`], `creature_formations`, `script_spline_chain_*` [AC: `script_waypoint`, `waypoints`] | Node move, facing and delay | Add, insert, delete nodes; new paths; path properties; formations |
| Population | `pool_template`, `pool_members` [AC: `pool_creature`, `pool_gameobject`, `pool_pool`], `spawn_group_template`, `instance_spawn_groups`, `quest_pool_*` | Used as a preview filter | No editor |
| Events and phases | `game_event*` (14 tables), `phase_area`, `phase_name`, `terrain_swap_defaults` [AC: phaseMask, no phase tables] | Used as a preview filter | No editor |
| Quests | 5 core tables, plus `quest_poi(_points)` (136k/307k), `*_conditional`, `quest_details`, `quest_greeting`, GO starters/enders, `quest_visual_effect`, locales | Core tables | POI, conditional text, GO givers, greetings, locales |
| Travel | `game_tele`, `areatrigger_teleport`, `world_safe_locs`, `spell_target_position`, `instance_template`, `access_requirement` [AC: `dungeon_access_*`], `transports` | Graveyard edit; triggers read | Teleport and instance authoring |
| Areas and ambience | `spell_area`, `game_weather`, `areatrigger_tavern`, `disables`, `areatrigger_template(_actions)`, `areatrigger_create_properties*` | AreaTriggers drawn; behavior rules | Area auras, weather, rest areas, spawned-trigger authoring, disables |
| Cinematics (TC only) | `conversation_*`, `scene_template`, `playerchoice*` | None | No editor |
| Text and items | `hotfixes.broadcast_text`, `item_sparse`, `item` [AC: `broadcast_text`, `item_template` in world], `page_text`, `points_of_interest`, `npc_text` | Linked and read | Write broadcast text, page text and items |
| **Not worth an editor** | `command`, `trinity_string`, `updates*`, `version`, `player_*` stats, `playercreateinfo*`, `pet_levelstats`, `battle_pet_*`, `garrison_*`, `spell_proc`, `spell_custom_attr`, `spell_group*`, `spell_script_names`, `skill_*`, `exploration_basexp`, `achievement_*`, `guild_rewards*`, `lfg_*`, `battleground_*`, `serverside_spell*`, `warden_checks`, `*_locale` (beyond quest text) | — | Server tuning or class design, not world building. A generic read-only table viewer at most |

---

## Editor design principles (every phase)

1. **Template = prefab, spawn = instance.** TrinityCore already models this. Per-spawn `npcflag` and `unit_flags*` are nullable, and NULL means inherit from the template. `creature_addon` overrides `creature_template_addon`. The inspector shows inherited values faded and overridden ones in bold, with a "revert to template" control per field and an "Apply to template" action.
2. **Viewport first.** Anything with a position is edited with a gizmo or a click in the world: spawns, path nodes, formation offsets, POI polygons, teleport targets, summon positions. Lists are second.
3. **Plain language over columns.** Show "Respawns after 5 min", not `spawntimesecs`. Show "Drops one of these (40%)" instead of `GroupId`. Show "Only when the player has quest X complete" instead of `ConditionTypeOrReference=8`. Column names appear only in tooltips.
4. **Reference safety.** Every ID field is a search picker with name and icon. Every entity page has a "Used by" panel (reverse references), and every delete shows its impact before it happens.
5. **One draft, one export.** Everything is a `RowEdit` through `InteractionDraft` and leaves only through `SqlExport`, with undo, redo, freshness checks and stale preflights.
6. **Descriptors, not per-dialect classes.** Each dialect fills one descriptor; one renderer draws it (the existing `CreaturePage`, `ItemListSpec` and `PageRenderer` pattern). New reads go only through `IDBSource` / `IWorldSchema`. `Ac`/`Tc` prefixes and folders as in CLAUDE.md. Foundation, Data and Assets stay Godot-free.
7. **Status text says what happened** ("Placed Stormwind Guard.", "Deleted 3 spawns."), and it goes to the `StatusBar`.

---

## Phase 10: Editor foundation (shared infrastructure)

**Why:** Every later phase creates rows, deletes linked rows, edits NULL-able columns or needs conditions. Today each of those is solved ad hoc: `MAX(id)` per source in `TcInteractionSource.cs:90-92` and `AcContentSource.cs:39`, and the creature page renderer is creature-only. Building these once means each later phase is mostly a descriptor plus a source, not a new subsystem.

**Benefit to the user:** Indirect, but it is what makes every later editor behave the same way: same pickers, same undo, same safe IDs and same delete warnings.

Implementation:
- **Entity page descriptors.** Generalize `CreaturePage` / `FieldSpec` / `ItemListSpec` (`Foundation/CreaturePage.cs`) into `EntityPage` keyed by an entity kind (creature, gameobject, loot table, pool, event, …). Generalize `PageRenderer` (`Scripts/Ui/CreatureProperties/PageRenderer.cs`) and the section registry into a reusable **Properties window shell** with a left picker and pages. This lands the tc-status item "Schema-driven section registry and field groups". The creature window becomes the first client of the shell.
- **Spawn poses become row edits.** Move creature and GameObject poses off `CreatureDraft` / `GameObjectDraft` and onto `RowEdit` against `creature` / `gameobject` schemas in `InteractionDraft`.
  - Migrate existing `creature-positions.json` and `gameobject-positions.json` entries into `interactions.json` on first open, the way `SettingsResolver.LegacyDatabaseMigration` moves old settings, and then retire the pose drafts.
  - The goal is one history, so move → delete → undo has a defined order. A delete's every-column guard then sees the moved pose instead of going STALE against the old one.
  - The move export keeps its float-tolerant guard (`ABS(col - v) < 0.0001`), so `SqlExport` needs float-tolerant guards for content-row updates and deletes too.
  - `GraveyardDraft` and `WaypointDraft` stay as they are; waypoints move to row edits in Phase 14.
- **Nullable columns.** Add NULL to `TableSchema` / `Row` (`Foundation/InteractionRows.cs`) and to `SqlExport` (`col IS NULL` guards, `NULL` literals), plus an "inherit" state in `FieldSpec`.
- **`IdAllocator`** (Foundation). Next free ID per (table, key column) = max(database max, draft-staged inserts) + 1, with an optional **user ID range** per entity kind in Settings so custom content doesn't collide with future TDB imports. The database max comes from one new `IDBSource.MaxIdsAsync(tables)`. Inserts keep their existing `COUNT(*)=0` preflight. Replace the existing `MaxMenuId` / `MaxTextId` / `MaxTrainerId` and the quest `max` with it.
- **Reference index.** `IDBSource.FindReferencesAsync(kind, id)` returns which tables point at an entity. Each `IWorldSchema` holds a per-dialect reference map (e.g. creature entry ← `creature.id`, `smart_scripts` target params, `quest_objectives` ObjectID, `creature_queststarter`, …). It feeds the "Used by" panel and delete-impact dialogs.
- **Cascade delete specs.** Each entity kind declares its dependents, mirroring the server's own `DeleteFromDB`. TrinityCore's `Creature::DeleteFromDB` (`Creature.cpp:2042+`) removes `spawn_group`, `creature_addon`, `game_event_creature`, `game_event_model_equip` and `linked_respawn` rows. It does **not** remove pool, formation, SmartAI or `conditions` rows, so the dialog lists those and offers to remove them too.
- **Condition engine (model only).** Add `ConditionCatalog` per dialect: source types from `ConditionMgr.h` (TC; AC's equivalent), condition types with parameter kinds, and sentences such as "Player has aura {spell}". Generate it with a script modelled on `Tools/generate_smart_catalog.py`. The visual builder is Phase 12.
- **Content browser shell.** A dockable bottom panel (Unreal "Content Browser" style) with tabs per entity kind (creature templates, GO templates, later loot tables, pools, events). It offers search, filters and a model thumbnail rendered off-screen through `ModelLibrary` and cached under `user://thumbs/<profile>/`. Phase 11 makes it drag-to-place.

Docs: `editing-and-export.md` (allocator, nullable, references), `viewer-runtime.md` (shell, browser), `tc-status.md` rows. Correct the tc-status note on condition source "type 20": the TC enum goes from 19 to 21.

---

## Phase 11: Spawn authoring (place, duplicate, delete, instance overrides)

**Why:** This is the core of a world editor. Today a designer can move Hogger but cannot add a second Hogger, remove a misplaced crate, give a guard a longer respawn, make a creature wander, or put it in a phase. Every content task ("populate this camp", "add a quest object") starts with placement.

**Benefit to the user:** You can build a camp in minutes by dragging templates from the browser onto the terrain. Ctrl+D duplicates a selection, Delete removes it, and the inspector sets per-spawn behavior. This is the Unity/Unreal "drop prefab, tweak instance" loop.

UX:
- **Drag from the Content Browser** shows a ghost model that follows the ground (`TerrainViewer.Ground.cs`, `ScenePicking`) and snaps to WMO floors. Click places it. Shift keeps placing. Facing comes from a rotate-drag or the existing gizmo.
- **Multi-select** (box select, Ctrl-click) for move, duplicate and delete together. Copy/paste across zones keeps relative layout.
- **Spawn inspector** (in `CreatureOverlay.Ui` / `GameObjectOverlay.Ui`):
  - *Spawn*: respawn timer (min/sec), wander radius drawn as a ring in the viewport, movement type (Idle, Random, Path → Phase 14), current health %.
  - *Visibility*: spawn difficulties as checkboxes [AC: spawnMask], phase ID / phase group / terrain swap [AC: phaseMask bits] with a "preview this phase" button.
  - *Overrides* (prefab model). Each override is shown faded with the template value until it is overridden. The two dialects differ:
    - **TC:** `modelid` (0 = template), `equipment_id`, nullable `npcflag` / `unit_flags` / `unit_flags2` / `unit_flags3` (NULL = template), `curHealthPct`, `ScriptName`, `StringId`.
    - **AC:** `equipment_id`, `npcflag` / `unit_flags` / `dynamicflags`, `curhealth` / `curmana`, `ScriptName`. There is no `modelid`.
    - AC flags are NOT NULL, and 0 means "use the template" (`ObjectMgr::ChooseCreatureFlags`, `ObjectMgr.cpp:1659`), so **an AC spawn cannot override a flag to 0**. The AC inspector disables "clear all flags" with that explanation and writes 0 when the value equals the template, as `Creature::SaveToDB` does (`Creature.cpp:1433`).
  - *Appearance (addon)*: mount, stand state, sheath, emote, auras (spell picker list), anim kits, visibility distance.
  - GameObject: state (ready / active), anim progress, spawn time, invisibility, and a parent-rotation gizmo for transport-attached objects.
- **Delete** shows the impact list (events, pools, spawn groups, linked respawns, formations, SmartAI that references this GUID) before it runs (built: Keep or Delete anyway). **Still to build:** an option to remove those rows in the same command. Until then an exported delete leaves dangling rows in `pool_members` / `pool_creature`, `spawn_group`, `game_event_creature` / `game_event_gameobject`, `linked_respawn`, `creature_formations` and per-GUID `smart_scripts`. The server's own `DeleteFromDB` cleans only some of these (see Phase 10, cascade delete specs).
- **New template from selection** ("Save as new creature…") copies the template and its child rows, using `IdAllocator`.

Implementation:
- Add `creature`, `creature_addon`, `gameobject`, `gameobject_addon`, `creature_template_addon` and `gameobject_template_addon` to `AcTables` / `TcTables` with dialect columns, nullable on TC.
- **One history:** poses are row edits since Phase 10, so place, move, edit and delete share one undo stack. A move of a spawn placed in this draft collapses into its staged insert (net change). Overlays render staged inserts from the effective `RowSet`, so new spawns appear immediately. A delete cascades in one command.
- **New-row defaults** follow what the server writes itself (`cs_npc.cpp:133-151` → `Creature::SaveToDB`, `cs_gobject.cpp:141` → `GameObject::SaveToDB`; AC's equivalents). `zoneId`/`areaId` are filled from the client area lookup when one exists, otherwise 0. TC treats them as a cache (`Calculate.Creature.Zone.Area.Data`), so a missing retail `AreaLookup` does not block this phase.
- Rules: `TcSpawnRules` / `AcSpawnRules` cover template exists, map exists, difficulty valid for the map (`ClientRecord.Difficulty`), phase exists (TC `phase_name` / client), movement type vs wander radius, and equipment ID exists.
- Reuse: `TransformGizmo`, `RegionOverlay`, `SpawnPreview` (staged spawns must pass the preview filter or show a "hidden by preview" badge).

Verify: `--client-shot-overlays` place/duplicate/delete flow in both dialects. Export, then run against a scratch `world`. Confirm with `.npc info` in-game that the placed spawn loads.

---

## Phase 12: Conditions builder (one visual editor for "when does this apply")

**Why:** `conditions` gates gossip options, quest availability, loot, vendor items, phases, spell targets, SmartAI events and graveyards: 17k rows in ~30 source types. Every existing editor shows "has conditions" counts (quest links, gossip) but can't edit them. Copying a gossip menu doesn't copy its conditions (open tc-status gap).

**Benefit to the user:** "Show this option only to Alliance players on quest X" becomes three clicks in a readable sentence instead of decoding `SourceTypeOrReferenceId`/`ElseGroup`/`NegativeCondition`. One editor works everywhere.

UX:
- An inline **condition strip** under any conditionable thing (a gossip option, quest link, loot row, vendor item, SmartAI rule, phase-area row, graveyard link) shows a sentence such as "Only if: player is Alliance AND quest 'The Defias Brotherhood' is complete".
- Clicking it opens a **logic builder**: OR-groups (ElseGroup) as stacked cards, AND rows inside each, a NOT toggle per row, and a typed parameter picker per condition type (quest, spell, aura, faction/rank, item count, zone, phase, level range, class/race checkboxes, …).
- **Reference conditions** (negative source types) are shown as named, reusable "condition sets" that can be shared.
- A **live tester** pane: pick class, race, level and completed quests, and see whether it passes. This uses only the `ConditionCatalog` semantics; no server is needed.

Implementation: a `ConditionCatalog` (Phase 10) plus a `ConditionStrip` control in `Scripts/Ui`, and an `IDBSource.ReadConditionsAsync(sourceType, group, entry)`. `conditions` goes into both table catalogs. Wire it into gossip (types 14, 15), quest availability (19, and the quest editor), SmartAI (22), vendor (23), graveyard (27, which replaces today's team-only handling), and spawn groups / phases (26, 33). Copy-menu then copies its conditions. Rules mirror `ConditionMgr::isConditionTypeValid` / `isSourceTypeValid` for each dialect.

---

## Phase 13: GameObject editor (template + type-aware properties)

**Why:** GameObjects are half of world content: chests (type 3, 7k), doors (0, 5k), quest objects (2, 10), traps (6), spell focuses (8, 10k), chairs (7, 17.6k). Today they are move-only, and nothing about them can be seen or changed. The template's 35 `Data` columns mean different things per type, which is why raw editing is hopeless.

**Benefit to the user:** Pick a chest and the page reads "Lock: Rusty Lock · Loot: (table) · Despawn after loot · Respawn 5 min". Pick a door and it reads "Starts closed · Auto-close after 3 s". It works like a Unity component inspector that changes with the object type.

UX: a Properties window (Phase 10 shell) with pages Template (name, display with live model preview, size, type selector, faction, flags), **Type properties** (labelled, typed fields for the chosen type), Loot (Phase 15 page), Quests (GO starters/enders/questitems), Behavior (SmartAI source type 1, reusing `BehaviorPanel`), Used by.

Implementation:
- **Generated type catalog:** `TrinityCore/GameObjectData.h` comments are machine-readable per field (`// 1 open, References: Lock_, NoValue = 0`, `enum { false, true, }; Default: false`, `Min value / Max value`). Write `Tools/generate_gameobject_catalog.py` (pattern from `generate_smart_catalog.py`) to emit `TcGameObjectTypes.cs` and `AcGameObjectTypes.cs` (AC: Data0–23 from its `GameObjectData.h`). Each field carries a kind (bool, int range, ms, reference → picker kind: Lock, BroadcastText, GameObject, Spell, Loot, QuestGiver, Gossip, PlayerCondition…).
- `EntityPage` for GO: the type-properties group is built at runtime from the catalog for the row's `type`. Changing the type asks before it resets the Data fields to their defaults.
- Tables: `gameobject_template`, `gameobject_template_addon`, `gameobject_queststarter`/`questender`/`questitem`, SmartAI type 1 (closes a tc-status Phase 8 gap). The reads go through a new `Tc/AcGameObjectPropertiesSource`.
- Right-click a GO in the world → Properties… (same entry point as creatures).

---

## Phase 14: Movement authoring (paths, formations)

**Why:** Paths can be nudged but not built: no new path, no inserting nodes, no deleting nodes (tc-status Phase 9 gap). Patrols and escorts are among the most common content tasks. Formations (3.1k rows) decide how groups move together and are invisible today.

**Benefit to the user:** Click "New path", click points on the ground, and the creature walks it. Drag a follower around its leader to set the formation offset. It works like a spline tool in Unreal.

UX:
- **Path tool:** with a creature selected, "Edit path" enters a mode where clicking the ground appends nodes, Alt-click inserts between nodes, and Delete removes a node. Each node card holds delay, facing (rotation handle) and run/walk. Path-level controls (move type, velocity, flags) are shown with a warning "shared by N creatures".
- A preview button animates a ghost along the path at the path's speed.
- **Formations:** select a leader to see its members linked by lines. Dragging a member around the leader edits `dist`/`angle` (polar). Group AI flags appear as checkboxes, and patrol turn-around points can be picked from the path.

Implementation: extend `WaypointDraft` or move waypoints onto `RowEdit` for node insert/delete/renumber (renumbering = delete + insert in one command). Add path creation through `IdAllocator` and the creature's `creature_addon.PathId` plus `MovementType=2` together. AC: `waypoint_data` / `creature_addon.path_id`. Follow SmartAI `WAYPOINT_START` users (tc-status gap). Formations go in both catalogs (same schema).

---

## Phase 15: Loot editor

**Why:** Loot is what players experience most directly, and it's huge (311k creature rows plus 12 other loot tables, with 14k shared references). The difficulty page already shows `LootID`, `PickPocketLootID` and `SkinLootID`, but they lead nowhere.

**Benefit to the user:** You see "what drops and how likely" in one picture, can tune a drop with a slider, and can simulate 1,000 kills before exporting.

UX:
- A **Loot window** opened from creature (loot, pickpocket, skin), GO (chest), item (container) or the Content Browser.
- **Group cards:** ungrouped rows are independent rolls ("each kill: 35% Linen Cloth ×1–2"). A group shows "exactly one of these" with stacked chance bars, and equal-chance rows split the rest automatically.
- **References** expand inline as nested, collapsible tables marked "shared by N tables". Editing one prompts "Edit shared table / Make a private copy".
- Items get icon, quality colour and a tooltip. Flags: quest-only (with the quest), loot mode as named checkboxes, and the condition strip (Phase 12).
- A **Simulate** button runs N rolls with the server algorithm and shows a histogram of what a player gets.
- **Who drops this?** search: item → every creature/GO/table that can drop it.

Implementation: `Tc/AcLootSource` with one `TableSchema` per loot table. The dialect difference is TC `ItemType` (0 item / 1 reference / 2 currency / 3 tracking quest; read as `LootStoreItem::Type`, `LootMgr.cpp:156`) vs AC's separate `Reference` column. A `LootModel` in Foundation normalizes the two into a view while edits stay dialect-native. Add a `LootRoller` that mirrors `LootTemplate::Process` / `LootGroup` for the simulator, and `LootRules` that mirror the `LoadLootTable` checks (`LootMgr.cpp:289-382`: MinCount, chance > 0, equal-chance group, MaxCount ≥ MinCount, item/currency/quest exist). Phase 10's `IdAllocator` supplies new loot entry IDs.

---

## Phase 16: Population control (pools, spawn groups, linked respawns)

**Why:** Rare spawns, rotating mining nodes and "one of these five chests" are all pools (4k pools, 13k members). Spawn groups (TC) toggle entire camps for bosses and events. Today they only filter the preview; you can't see or build them.

**Benefit to the user:** Select five ore nodes and choose "Make pool: spawn 2 at a time". The viewport colour-codes pool members, and a hover card shows "Rare: 10% of pool 'Elwynn rares'".

UX: a **Pools** tab in the Content Browser, and an inspector "Population" card on any spawn (member of pool X, chance, max active). Pools are drawn as hulls in the viewport around their members. Nested pools [TC: `pool_members.type=2`; AC: `pool_pool`] form a tree. Spawn groups list their members, flags and conditions, with linked respawn arrows (boss → adds).

Implementation: `TcPoolSource` (`pool_template`, `pool_members`) and `AcPoolSource` (`pool_template`, `pool_creature`, `pool_gameobject`, `pool_pool`, `pool_quest`) behind one `PoolPageSpec`. Spawn groups are TC only (`spawn_group_template`, `spawn_group`, `instance_spawn_groups`), and AC shows "not supported". Linked respawn uses the same schema in both. Rules mirror `PoolMgr` loading (chance sums ≤ 100, a spawn in only one pool, max_limit).

---

## Phase 17: Events and phases (world-state timeline)

**Why:** Holidays, Darkmoon Faire and seasonal decorations are `game_event` (89 events, 42k linked spawns), and phasing (`phase_area` 660, `conditions` source 26) is how modern zones change after quests. The preview already understands both but can't edit either.

**Benefit to the user:** A calendar timeline shows when each event runs. Selecting spawns and choosing "Belongs to Hallow's End" means they only appear during it. A phase layer toggle lets you author what a zone looks like before and after a quest.

UX:
- An **Events timeline** panel shows events as bars on a year calendar (start, occurrence, length), with an inspector for holiday, world state and announce.
- Event membership on spawns, quests, vendor items, model/equip swaps and npcflag changes [the `game_event_*` tables] appears as an "Event" card in each relevant editor.
- **Phases (TC):** a phase picker reads `phase_name` and the client Phase names. Choosing "Edit in phase N" stamps new spawns with that phase. `phase_area` areas show on the minimap with the condition strip for when the phase applies. AC: phaseMask bits with names.

Implementation: `game_event` and its children go into both catalogs (AC lacks `WorldStateId`, has `game_event_battleground_holiday`). Phase tables are TC only. The `SceneSidebar` visibility-preview controls become the entry point ("Edit" next to the event picker).

---

## Phase 18: Creature completion (the missing creature pages)

**Why:** The creature window covers services but not how the creature looks or fights. Equipment (42k rows), spells (9.6k), on-kill reputation (2k), resistances, immunities, movement capabilities, spell-click (841), vehicle accessories (528) and summon groups (1.1k) all need hand-written SQL today.

**Benefit to the user:** One window holds the whole NPC: what it holds in its hands, what it casts, what rep it gives, what it summons and where. Summon positions can be placed in the world.

Pages (each an `EntityPage` / `ItemListSpec`):
- **Appearance:** model list with weights and per-model scale (`creature_template_model`), live preview, and equipment sets (`creature_equip_template`; TC adds appearance mod and visual). Retail equipment rendering is a known tc-status gap, so show the item icons until it lands.
- **Combat:** spells (`creature_template_spell`, spell picker), resistances, immunities (TC), and the template addon (default auras, emote, mount).
- **Rewards:** on-kill reputation as faction pickers with standing caps.
- **Movement:** ground, swim, flight, rooted (TC `creature_template_movement` and its overrides).
- **Interactions:** spell-click (already read, not editable) with the condition strip (type 18), and vehicle accessories with seats.
- **Summons:** `creature_summon_groups` positions as ghost markers in the viewport, draggable.
- **New template** wizard: from blank or from copy, through `IdAllocator`.

---

## Phase 19: Quest completion (world-placed quest data)

**Why:** The quest editor edits the quest but not where it shows on the map (`quest_poi`, 136k plus 307k points), who gives it when it isn't a creature (GO starters, item starters), conditional texts, greetings, or the condition-based availability (the tc-status Phase 7 gap list).

**Benefit to the user:** Quest map blobs are drawn by clicking polygon corners on the terrain, right where the objective is. A quest can be given by a wanted poster.

UX: a **Map** page in the quest editor that switches the viewport to "POI edit": each objective's POI polygon is drawn on the terrain and edited with vertex handles. Givers gain a GameObject tab. Conditional description, progress, completion and log texts use the condition strip. Quest greeting goes on the creature/GO pages. Quest pools use Phase 16.

Implementation: add `quest_poi`, `quest_poi_points`, `quest_*_conditional`, `quest_details`, `quest_greeting`, `quest_reward_display_spell`, `quest_reward_choice_items`, GO starters/enders and `quest_pool_*` to the catalogs. AC POI uses the same idea with fewer columns. Also close the open TC objective-field gaps (Flags2, ParentObjectiveID, …).

---

## Phase 20: Travel and instances

**Why:** Teleports, portals, instance entrances and spell destinations make up the world's connectivity: `areatrigger_teleport` 344, `spell_target_position` 2.5k, `access_requirement`, `game_tele` 2.3k. Today they're read-only lines in the viewport at best.

**Benefit to the user:** Select a trigger and choose "Set destination", then pick a spot in any map with a jump-to-location preview. Teleport spells show their landing points in the world as markers you can drag.

UX: destination pickers that open the target map and allow click-to-place. Instance pages hold entry requirements (level, item, quest, achievement), the parent map and script. A `.tele` list manager doubles as the editor's bookmark list.

Implementation: `world_safe_locs` creation (TC; reuse the graveyard draft model), `areatrigger_teleport` [AC: `areatrigger_teleport` holds coordinates directly], `spell_target_position`, `instance_template`, `access_requirement` [AC: `dungeon_access_template` / `_requirements`], and `game_tele` (drop the `AcTeleportSource` reuse deviation in favour of an editable shared source).

---

## Phase 21: Areas and ambience

**Why:** These are small tables with big gameplay effects: area auras on enter (`spell_area` 968), weather (`game_weather`), rest areas (`areatrigger_tavern`), disabled content (`disables` 5.3k), and server-spawned AreaTrigger authoring (`areatrigger_template`, `_actions`, `_create_properties*`; spawns are read-only today).

**Benefit to the user:** You can author the feel of a zone: weather sliders per season, "players in this area get this aura while on quest X", tavern zones painted as triggers. Server-spawned AreaTriggers can be placed and shaped with gizmos (sphere radius, box extents, polygon vertices).

Implementation: a zone page (opened from the minimap or area name) for weather, area auras and rest triggers, and a disables manager as a filtered list with plain-language source types. Server AreaTrigger spawns (TC only) are placed, shaped and given properties by reusing `AreaTriggerShapes` drawing plus `TransformGizmo` handles. Their `areatrigger_template_actions` show as action cards. AC: no spawned AreaTriggers ("not supported").

---

## Phase 22: Cinematics: conversations, scenes, player choice (TrinityCore only)

**Why:** Retail storytelling uses conversations (248 templates, 702 lines; talking-head dialogue between actors), scenes and player choices. Without an editor, modern quest content can't be authored. AC has no such systems.

**Benefit to the user:** A **sequencer**-style editor: actors on the left, lines on a timeline with broadcast text and timing, and a play button that previews text order. Player choices are edited as cards that look like the in-game choice UI.

Implementation: `conversation_template`, `_actors`, `_line_template` (with condition strip, source 29); `scene_template`; `playerchoice*` (responses, rewards, condition source 36). Linking from SmartAI actions (start conversation, play scene) opens these editors. AC shows the section as not applicable.

---

## Phase 23: Text pipeline (hotfix export + broadcast and page text)

**Why:** On retail, every line of NPC dialogue, gossip and emote is a `broadcast_text` row in the **hotfixes** DB (238k), and the editor can only *link* existing text (tc-status Phase 8 "Speech" gap). New content needs new words. Books and plaques use `page_text` (world).

**Benefit to the user:** You type the new line where it's used (gossip option, speech line, GO page), and it's created, linked and shipped to clients. There is no ID hunting.

Implementation:
- **Hotfix export path:** a second `ITableCatalog` for `hotfixes`, an `SqlExport` target that writes a separate `hotfixes-apply/revert.sql`, and an automatic `hotfix_data` row per changed record (TableHash from WoWDBDefs; `RecordId`; `Status`; a new `Id` from `IdAllocator`). Stale checks run against both databases (the plan the tc-status Speech row already describes). Pending changes show both scripts together.
- AC side: `broadcast_text` lives in `acore_world`, so it's a world write with no hotfix rows.
- Inline "New text…" / "Edit text" in the speech, gossip and GO page-text pickers, with a male/female variant and an emote/sound strip. `page_text` gets a book editor with next-page chaining shown as pages.

---

## Phase 24: Item editor

**Why:** Custom rewards, vendor goods and quest items need new items. Today vendors and loot can only reference existing ones. TC items are `hotfixes.item` plus `item_sparse` (plus extras); AC items are `world.item_template`.

**Benefit to the user:** "Create item" from a loot table, vendor or quest reward opens a tooltip-first editor. You edit the in-game tooltip directly (name, quality colour, stats, armor, damage, icon picker over the client's icon list), and the generated tooltip updates live.

Implementation: `Tc/AcItemSource` and catalogs, a tooltip preview (the retail item tooltip gap in tc-status), the Phase 23 hotfix path for TC, and rules for stat and slot validity. Spells stay out of scope; a spell editor is a separate product.

**AC parity caveat (verify first):** the app never writes game files, and a 3.3.5 client may need a matching `Item.dbc` row (display and inventory type) for a new `item_template` ID. Start the phase by testing a new ID on a 3.3.5 client.
- If the item doesn't display correctly, AC item creation is limited to IDs or display info the client already has. That means cloning an existing item's ID range, or an external client patch the UI tells the user about. It is not full parity.
- TC does not have this problem, because `hotfix_data` sends the new rows to the client.

---

## Cross-phase reuse map

| Need | Reuse |
|---|---|
| Page layout and fields | `CreaturePage` / `FieldSpec` / `ItemListSpec` → `EntityPage` (Phase 10), `PageRenderer` |
| Edits, undo, export | `InteractionDraft`, `RowEdit`, `DraftHistory`, `SqlExport`, `PendingChanges` |
| Pickers | `PickerPopup` (`Scripts/Ui/EditorFields.cs:311`), `CreatureSection.SearchState<T>`, `IClientLookup.Search*` / `Exists(ClientRecord, id)` |
| Positions and gizmos | `TransformGizmo`, `ScenePicking`, `TerrainViewer.Ground`, `RegionOverlay` / `RegionQuery` |
| Generated catalogs | `Tools/generate_smart_catalog.py` pattern → GO types, conditions |
| Behavior | `BehaviorPanel` with new SmartAI source types (1 for GOs, etc.) |

## Verification (per phase)

- `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`; Db tests with `ACWORLD_DB_INFO` (AC) and `ACWORLD_TC_DB_INFO` (TC); unit tests for each new descriptor, rules and export after the feature works.
- Visual check in Godot in **both** client profiles: open the new editor, run the create/edit/delete/undo flow, and screenshot via a smoke stage (extend the TC client smoke and `--content-smoke`).
- Export from Pending Changes, run apply then revert against a scratch copy of `world` (and `hotfixes` for 23–24), and confirm the server loads the rows without `sql.sql` errors (the rules phases mirror those loader checks).
- Update `tc-status.md`, the matching architecture page and `DB-DIFFERENCES.md` in the same change.

## Recommended order and sizing

10 (foundation, M) → 11 (spawns, L) → 12 (conditions, M) → 13 (GameObjects, M) → 15 (loot, L) → 14 (movement, M) → 16 (pools, M) → 17 (events/phases, M) → 18 (creature pages, M) → 19 (quest completion, M) → 20 (travel, S) → 21 (ambience, M) → 23 (text, M) → 24 (items, L) → 22 (cinematics, M).

Sizes (S/M/L) already include full AC+TC parity (about 1.5× a single-dialect phase). Phases on TC-only systems (spawn groups, 21's spawned triggers, 22) cost less on the AC side because it only shows "not supported".

Loot comes before movement because it touches more players' experience. Cinematics come last because they are TC only and need the Phase 23 text path for authoring new lines.
