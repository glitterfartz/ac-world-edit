# Editor plan

Everything still to build: the refinements left in the built editors, the design of editor phases 10 onward
and the open gaps in the data, rendering and performance layers. This page is pending work only; what
already landed is in [completed-work.md](../status/completed-work.md), and how the built parts work is in
[architecture/](../architecture/overview.md). AzerothCore vs TrinityCore schema differences are in
[reference/database-differences.md](../reference/database-differences.md).

Target: TrinityCore `master` `world` (TDB 1210.26091) plus `hotfixes`; retail client 12.1.5.70575.

Phases 1–9 are built and are not planned here again. Phases 10 and 11 are mostly built; phases 12 onward
are the design below. States used throughout: **partial**, **not started**, **deviates** (built
differently from the design), **open gap**, **unverified**, **in progress**.

## Pending work at a glance

| Area | State | Open |
| --- | --- | --- |
| Phase 7 quests (refinements) | partial | Quest tables outside the editor, objective fields, reference checks, TC content smoke |
| Phase 8 SmartAI and speech (refinements) | partial | AT rules for TC, other source types, loader rules, type curation, parameter names, difficulties, editing speech, TC smokes |
| Phase 9 waypoints (refinements) | partial | Other path users, unused-paths list, TC waypoint smoke |
| 10 Editor foundation | mostly | Reference index, cascade-delete specs, allocator adoption, a viewer check of spawn poses |
| 11 Spawn authoring | mostly | Model ghost, multi-floor picking, cross-zone paste, group edit and rotation, preview-this-phase, transport rotation, addon defaults, `SpawnFacts`, new template, thumbnails |
| 12 GameObject editor | mostly | Behavior page, dedicated edit/undo smoke |
| 13 Conditions builder | not started | Strip, builder, tester, `ReadConditionsAsync`, wiring |
| 14 Movement authoring | mostly | Unused-paths list, convoys and transports, cascading formation deletes |
| 15 Loot editor | not started | Loot source, model, roller, rules, window |
| 16 Population control | not started | Pool, spawn-group and linked-respawn editors |
| 17 Events and phases | not started | Event timeline, phase authoring |
| 18 Creature completion | partial | Models and equipment, combat, rewards, movement, interactions, summons, new template |
| 19 Quest completion | partial | POI, GO/item givers, conditional text, greetings, pools, objective and reference gaps |
| 20 Travel and instances | mostly not started | Destination editing, instances, access requirements, shared `game_tele` source |
| 21 Areas and ambience | mostly not started | Weather, area auras, taverns, disables, spawned triggers |
| 22 Cinematics | not started | Conversations, scenes, player choice |
| 23 Text pipeline | partial | Hotfix script, speech and page text, editing existing text, AC path |
| 24 Item editor | not started | Item source, editor, creation flow |
| Foundation and overlays | partial | Reference lookups, hotfix overrides, preview filters, graveyard checks |
| Performance | in progress | Forward+ baseline, upload budget, spawn table indexes, connection pooling |
| Tests | partial | `Db2Tables` thread safety, stale corpus filters |
| Retail rendering | partial | Equipment, skeletons, more shaders and layers, underwater medium |
| WoW: Forever | open gap | No world database branch; WoWDBDefs build pin |

## Design principles (every phase)

1. **Template = prefab, spawn = instance.** TrinityCore already models this. Per-spawn `npcflag` and
   `unit_flags*` are nullable, and NULL means inherit from the template. `creature_addon` overrides
   `creature_template_addon`. The inspector shows inherited values faded and overridden ones in bold, with a
   "revert to template" control per field.
2. **Viewport first.** Anything with a position is edited with a gizmo or a click in the world: spawns, path
   nodes, formation offsets, POI polygons, teleport targets, summon positions. Lists are second.
3. **Plain language over columns.** Show "Respawns after 5 min", not `spawntimesecs`. Show "Drops one of
   these (40%)" instead of `GroupId`. Column names appear only in tooltips.
4. **Reference safety.** Every ID field is a search picker with name and icon. Every entity page has a
   "Used by" panel, and every delete shows its impact before it happens.
5. **One draft, one export.** Everything is a `RowEdit` through `InteractionDraft` and leaves only through
   `SqlExport`, with undo, redo, freshness checks and stale preflights.
6. **Descriptors, not per-dialect classes.** Each dialect fills one descriptor; one renderer draws it (the
   `EntityPage`, `ItemListSpec` and `PageRenderer` pattern). New reads go only through `IDBSource` /
   `IWorldSchema`. `Ac`/`Tc` prefixes and folders as in AGENTS.md. Foundation, Data and Assets stay
   Godot-free.
7. **Status text says what happened** ("Placed Stormwind Guard."), and it goes to the `StatusBar`.

## Standing decisions

- The dialect follows the client kind: Wrath335 uses AzerothCore and Retail uses TrinityCore. There is no
  autodetect, so a schema mismatch fails with a dialect-specific error.
- **Every phase ships both dialects.** Where a system exists only in TrinityCore (spawn groups,
  conversations, scenes, player choice, spawned AreaTrigger templates), the AzerothCore half says "not
  supported" in the UI.
- **Drafts are per client profile** (`user://drafts/wrath/`, `user://drafts/retail/`), so both viewers run
  side by side without sharing a draft lock. Switching client in-app reopens the other profile's drafts.
- Hotfixes are read from a second database name on the same connection (default `hotfixes`), falling back to
  client DB2. Hotfix-backed editors (text, then items) are deliberately late phases; spells stay out of
  scope.
- **An export source with no SourceDialects entry is AzerothCore.** The source key is a one-way hash of the
  connection, and every draft saved before TrinityCore support was made against AzerothCore;
  `SourceDialects.DialectOf` defaults to AzerothCore.
- **Dialect-native rows.** Editing stays on the generic path, `RowEdit(Table, Key, Before, After)` →
  `SqlExport`, with each dialect's own table catalog, so structural differences (`quest_objectives` 1:N rows,
  `creature_template_difficulty`, `creature_template_gossip`) round-trip without a lossy neutral model.
  Editors switch descriptors (field groups, enums, catalogs), not data models.
- **`IWorldSchema` grows per phase.** It has `Tables` and `Sql`, plus the entity descriptors. `Smart` is the
  dialect's `SmartCatalog`. AzerothCore's static data is `Ac`-prefixed (`AcTables`, `AcQuestModel`,
  `AcNpcFlags`); the quest vocabulary both pages show is in `QuestVocabulary`.
- **Creature pages are descriptors, not per-dialect classes.** A page is an ordered list of groups; a group
  is fields bound to one row or an item list. Dialect-specific section adapters use `Ac` / `Tc` prefixes and
  live in the layer's `AzerothCore/` / `TrinityCore/` folders.
- **Editors read the dialect only through `IDBSource.Schema`:** `CreatureSectionRegistry` filters sections by
  schema support; `QuestPanel` is shared and `AcQuestPanel` / `TcQuestPanel` draw the pages that differ;
  `BehaviorPanel` is shared and takes `SmartCatalog` from the open connection's dialect.

## Verification per phase

- `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`; Db tests with `ACWORLD_DB_INFO` (AC) and
  `ACWORLD_TC_DB_INFO` (TC); unit tests for each new descriptor, rules and export after the feature works.
- Visual check in Godot in **both** client profiles: open the new editor, run the create/edit/delete/undo
  flow, and screenshot via a stage in an existing smoke or a new `--*-smoke` flag (`--content-smoke`,
  `--interaction-smoke` and `--spawn-authoring-smoke` are the closest patterns). The retail corpus runs with
  `--settings corpus.runsettings` and `ACWORLD_RETAIL_DIR`.
- Export from Pending Changes, run apply then revert against a scratch copy of `world` (and `hotfixes` for
  phases 23–24), and confirm the server loads the rows without `sql.sql` errors.
- Update this page, [completed-work.md](../status/completed-work.md), the matching architecture page and
  `database-differences.md` in the same change.

---

## Refinements to built phases

### Phase 7: quests

| Item | State | Open |
| --- | --- | --- |
| Quest tables outside the editor | not started | `quest_*_conditional`, `quest_objectives_completion_effect`, `quest_reward_choice_items`, `quest_reward_display_spell`, `quest_poi`, locale tables and the `conditions` rows of source types 19 and 20 are neither read nor edited. |
| Objective fields | partial | `Flags2`, `Visible`, `ConditionalAmount`, `ProgressBarWeight` and `ParentObjectiveID` are whitelisted but have no control. Area triggers, criteria trees and pet battles are typed IDs rather than pickers. |
| Reference checks | partial | Emote, area trigger, criteria tree, `ContentTuningID`, `QuestInfoID`, `QuestSortID` for retail and the title reward are not checked. |
| Content smoke for TrinityCore | not started | The TC quest editor is covered by the client smoke and unit tests, not by a copy of `--content-smoke`. |

### Phase 8: SmartAI and speech

| Item | State | Open |
| --- | --- | --- |
| AreaTrigger rules for TrinityCore | partial | The create properties' `ScriptName` is shown, not edited; triggers created by spells (no spawn) cannot be opened; `areatrigger_template_actions` are not read. |
| Other script source types | not started | TC also uses types 1 (object), 3, 5 and 10; only creature rules (0), AreaTrigger rules (2, 11, 12) and timed action lists (9) are read. |
| Loader rules | partial | TC's per-type validation (`IsEventValid`, `CheckUnusedEventParams` / `ActionParams` / `TargetParams`: unused parameters must be 0, ranges, existing spells and creatures) is not reproduced, so the server may reject a row the editor accepts. |
| Type curation | partial | Of the selectable TC types, 49 of 83 events, 57 of 139 actions and 19 of 31 targets have a written sentence; the rest show their name and server-comment parameter labels, and 5 actions and 1 target show raw numbered fields. |
| Names for parameters | partial | Emotes, text emotes, faction templates, game events, sound kits and scene or conversation IDs show numbers (no TC catalog behind the pickers). |
| `Difficulties` | partial | A free text field of difficulty IDs; values are not checked against `Difficulty.db2`. |
| Speech | partial | The Speech page has no "New…" button (writing a broadcast text exists on the gossip page only), and changing an existing text with stale checks against the client's version is not built. |
| Smokes | partial | `--behavior-smoke` and `--content-smoke` run against AzerothCore only; there is no TC copy of the action-list and Then-chain flows beyond unit tests. |

### Phase 9: waypoints

| Item | State | Open |
| --- | --- | --- |
| Waypoints from other users | partly done | Paths are found through `creature_addon` / `creature_template_addon` plus the SmartAI rules that start them; `script_waypoint`-style users are not followed. |
| Path links and orphans | mostly done | The "Unused paths" list (Phase 14) is not built. |
| Smokes | partial | `--waypoint-smoke` has no TC run of its own; TC path points are covered by the client smoke only. |

---

## Phase 10: Editor foundation (shared infrastructure)

**State:** mostly. The Properties shell, spawn poses as row edits, nullable columns, `IdAllocator`,
`ConditionCatalog` and the content browser are in. Open:

- **Reference index.** `IDBSource.FindReferencesAsync(kind, id)` returns which tables point at an entity.
  Each `IWorldSchema` holds a per-dialect reference map (creature entry ← `creature.id`, `smart_scripts`
  target params, `quest_objectives` ObjectID, `creature_queststarter`, …). Today the per-feature
  `TemplateReferences` / `CountSpawnReferencesAsync` / `CountTemplateReferencesAsync` reads cover what is
  shipped; this generalises them.
- **Cascade-delete specs.** Each entity kind declares its dependents, mirroring the server's own
  `DeleteFromDB`. TrinityCore's `Creature::DeleteFromDB` (`Creature.cpp:2042+`) removes `spawn_group`,
  `creature_addon`, `game_event_creature`, `game_event_model_equip` and `linked_respawn` rows; it does not
  remove pool, formation, SmartAI or `conditions` rows, so those stay listed in the dialog. The spawn delete
  dialog does this per kind today; other kinds still need it.
- **Allocator adoption.** `MaxMenuId`, `MaxTextId`, `MaxTrainerId` and the quest `MAX(id)` still read their
  own maxima (they came before `IdAllocator`); move them onto `IDBSource.MaxIdAsync(tables)`. The optional
  per-kind **user ID range** in Settings (so custom content does not collide with a future TDB import) is
  not built.
- **Viewer check.** Spawn poses as row edits are green in code and tests; moving, undo, discard in Pending
  Changes and the profile switch have still not been checked in the running viewer.

## Phase 11: Spawn authoring (place, duplicate, delete, instance overrides)

**State:** mostly. Placing, duplicating, deleting with an impact warning, multi-select, the spawn inspector
and the template addon are built. Open:

- **Placement.** The ghost is a capsule, not the model. In a multi-floor building the click picks the first
  floor the ray reaches from above.
- **Multi-select.** Copy and paste across zones (keeping relative layout), rotating a group, and editing one
  field for the whole group.
- **Spawn inspector.** A "preview this phase" button, the GameObject parent rotation for transport-attached
  objects, removing an addon row that is back to all defaults, and checks that a mount, emote or aura ID
  exists.
- **Spawn rules.** The client/database lookups ride on optional `SpawnFacts` (template, map, equipment set,
  difficulty per map, phase, spawn mask per map) and nothing fills them yet; the equipment set check waits
  for a read of `creature_equip_template`, the rest for client map data.
- **New template from selection** ("Save as new creature…"): copies the template and its child rows through
  `IdAllocator`.
- **Content browser.** Thumbnails for world-model (`.wmo`) GameObjects, a thumbnail for a creature's
  equipment (weapons are not attached), the smoke on the AzerothCore profile, filters beyond `type` (rank,
  family, level), and tabs for later kinds (loot, pools, events).

Spacing rules that a later change must not break: AC spawn flags are NOT NULL and 0 means "use the
template" (`ObjectMgr::ChooseCreatureFlags`), so an AC spawn cannot override a flag to 0 and the inspector
writes 0 when the value equals the template, as `Creature::SaveToDB` does.

## Phase 12: GameObject editor (template + type-aware properties)

**State:** mostly. The generated per-dialect type catalogues, the template/addon/quest tables, the
Template, Type properties, Quests and Spawns pages and the content-browser and right-click entry points are
built. Open:

- **Behavior page** for SmartAI `source_type = 1`, reusing `BehaviorPanel`.
- A dedicated edit/undo smoke; today the flows are covered by unit tests and the browser smoke only.

## Phase 13: Conditions builder (one visual editor for "when does this apply")

**State:** not started. The catalog, sentences and source labels are built; only menu copy (sources 14/15),
graveyard team rows (27) and quest counts (19) read conditions, so nothing can be added, changed or removed.

**Why:** `conditions` gates gossip options, quest availability, loot, vendor items, phases, spell targets,
SmartAI events and graveyards: 17k rows in ~30 source types. Every editor shows "has conditions" counts but
cannot edit them.

**Benefit:** "Show this option only to Alliance players on quest X" becomes three clicks in a readable
sentence instead of decoding `SourceTypeOrReferenceId` / `ElseGroup` / `NegativeCondition`.

UX:

- An inline **condition strip** under any conditionable thing (gossip option, quest link, loot row, vendor
  item, SmartAI rule, phase-area row, graveyard link) shows "Only if: player is Alliance AND quest 'The
  Defias Brotherhood' is complete".
- Clicking it opens a **logic builder**: OR-groups (ElseGroup) as stacked cards, AND rows inside each, a NOT
  toggle per row, and a typed parameter picker per condition type.
- **Reference conditions** (negative source types) are shown as named, reusable condition sets.
- A **live tester** pane: pick class, race, level and completed quests and see whether it passes. It uses
  only the `ConditionCatalog` semantics; no server is needed.

Implementation: a `ConditionStrip` control in `Scripts/Ui` and `IDBSource.ReadConditionsAsync(sourceType,
group, entry)`. Wire it into gossip (14, 15), quest availability (19 and the quest editor), SmartAI (22),
vendor (23), graveyard (27, replacing today's team-only handling) and spawn groups / phases (26, 33). Rules
mirror `ConditionMgr::isConditionTypeValid` / `isSourceTypeValid` per dialect.

## Phase 14: Movement authoring (paths, formations)

**State:** mostly. Node move/facing/delay, add/insert/delete/renumber, new paths, path-level properties,
spawn and template link/unlink, SmartAI path users, formations and the preview animation are in. Open:

- **Unused paths list.** Lists the paths no creature, template or SmartAI `WAYPOINT_START` uses, so an
  orphan can be re-linked or deleted, and warns before deleting a path other creatures still use.
- **Convoys and transports.** `script_spline_chain_*` / `script_waypoint`-style movement and transport
  paths are not authored.
- **Cascading formation deletes.** Deleting a creature leaves its formation rows; the delete command does
  not remove them with it.

## Phase 15: Loot editor

**State:** not started. Only `LootID`, `PickPocketLootID` and `SkinLootID` are shown (creature Stats page).

**Why:** loot is what players experience most directly and it is huge: 311k creature rows plus 12 other
loot tables and 14k shared references.

**Benefit:** "what drops and how likely" in one picture, tune a drop with a slider, simulate 1,000 kills
before exporting.

UX:

- A **Loot window** opened from creature (loot, pickpocket, skin), GameObject (chest), item (container) or
  the Content Browser.
- **Group cards:** ungrouped rows are independent rolls; a group shows "exactly one of these" with stacked
  chance bars and equal-chance rows splitting the rest automatically.
- **References** expand inline as nested tables marked "shared by N tables"; editing one prompts "Edit
  shared table / Make a private copy".
- Items get icon, quality colour, tooltip, quest-only flag, loot-mode checkboxes and the condition strip.
- A **Simulate** button runs N rolls with the server algorithm and shows what a player gets.
- **Who drops this?** search: item → every creature, GameObject or table that can drop it.

Implementation: `Tc/AcLootSource` with one `TableSchema` per loot table. The dialect difference is TC
`ItemType` (0 item / 1 reference / 2 currency / 3 tracking quest; `LootStoreItem::Type`, `LootMgr.cpp:156`)
vs AC's separate `Reference` column. A Foundation `LootModel` normalizes the two into a view while edits stay
dialect-native. `LootRoller` mirrors `LootTemplate::Process` / `LootGroup` for the simulator; `LootRules`
mirrors the `LoadLootTable` checks (`LootMgr.cpp:289-382`: MinCount, chance > 0, equal-chance group, MaxCount
≥ MinCount, item/currency/quest exist).

## Phase 16: Population control (pools, spawn groups, linked respawns)

**State:** not started. Pools filter the preview and count in the delete warning; no pool, spawn-group or
linked-respawn editor exists.

**Why:** rare spawns, rotating mining nodes and "one of these five chests" are pools (4k pools, 13k
members); TC spawn groups toggle entire camps for bosses and events.

**Benefit:** select five ore nodes and choose "Make pool: spawn 2 at a time"; the viewport colour-codes
members and a hover card says "Rare: 10% of pool 'Elwynn rares'".

UX: a **Pools** tab in the Content Browser, and a "Population" card on any spawn (pool, chance, max active).
Pools draw as hulls around their members; nested pools [TC `pool_members.type=2`; AC `pool_pool`] form a
tree. Spawn groups list members, flags and conditions, with linked-respawn arrows (boss → adds).

Implementation: `TcPoolSource` (`pool_template`, `pool_members`) and `AcPoolSource` (`pool_template`,
`pool_creature`, `pool_gameobject`, `pool_pool`, `pool_quest`) behind one `PoolPageSpec`. Spawn groups are
TC only (`spawn_group_template`, `spawn_group`, `instance_spawn_groups`). Linked respawn shares one schema.
Rules mirror `PoolMgr` loading (chance sums ≤ 100, a spawn in only one pool, `max_limit`).

## Phase 17: Events and phases (world-state timeline)

**State:** not started. `VisibilityPreviewPanel` filters by event and phase; there is no event or phase
authoring.

**Why:** holidays, Darkmoon Faire and seasonal decorations are `game_event` (89 events, 42k linked spawns),
and phasing (`phase_area` 660, `conditions` source 26) is how modern zones change after quests.

**Benefit:** a calendar timeline shows when each event runs; selecting spawns and choosing "Belongs to
Hallow's End" makes them appear only then; a phase layer toggle lets you author a zone before and after a
quest.

UX:

- An **Events timeline** panel shows events as bars on a year calendar (start, occurrence, length), with an
  inspector for holiday, world state and announce.
- Event membership on spawns, quests, vendor items, model/equip swaps and npcflag changes appears as an
  "Event" card in each relevant editor.
- **Phases (TC):** a phase picker reads `phase_name` and client phase names; "Edit in phase N" stamps new
  spawns with that phase; `phase_area` areas show on the minimap with a condition strip. AC: `phaseMask`
  bits with names.

Implementation: `game_event` and its children into both catalogs (AC lacks `WorldStateId`, has
`game_event_battleground_holiday`). Phase tables are TC only. The `SceneSidebar` visibility-preview
controls become the entry point ("Edit" next to the event picker).

## Phase 18: Creature completion (the missing creature pages)

**State:** partial. Template, Stats, Gossip, Vendor, Trainer, Quests, Behavior and Speech pages and the
template appearance addon are built. Open:

- **Appearance:** model list with weights and per-model scale (`creature_template_model`), live preview, and
  equipment sets (`creature_equip_template`; TC adds appearance mod and visual). Retail equipment rendering
  is still open (see [Retail rendering](#retail-rendering)), so show item icons until it lands.
- **Combat:** spells (`creature_template_spell`, spell picker), resistances, immunities (TC), and the
  template addon's default auras, emote and mount.
- **Rewards:** on-kill reputation as faction pickers with standing caps.
- **Movement:** ground, swim, flight, rooted (TC `creature_template_movement` and its overrides).
- **Interactions:** spell-click editing (read today, not editable) with the condition strip (type 18), and
  vehicle accessories with seats.
- **Summons:** `creature_summon_groups` positions as ghost markers, draggable in the viewport.
- **New template** wizard, from blank or from copy, through `IdAllocator`.

**Why:** equipment (42k rows), spells (9.6k), on-kill reputation (2k), resistances, immunities, movement
capabilities, spell-click (841), vehicle accessories (528) and summon groups (1.1k) all need hand-written SQL
today.

## Phase 19: Quest completion (world-placed quest data)

**State:** partial. The five quest tables, objectives (TC), rewards, story/chain/givers/preview pages and
creature links are built. Open:

- **POI:** `quest_poi` / `quest_poi_points` (136k / 307k) are neither read nor edited; there is no "POI
  edit" viewport mode. A **Map** page would draw each objective's polygon on the terrain with vertex handles.
- **Givers:** GameObject starters/enders and item starters.
- **Text:** `quest_*_conditional` texts, `quest_greeting` (shown on the creature/GO pages) and quest pools
  (Phase 16).
- **Tables:** `quest_objectives_completion_effect`, `quest_reward_choice_items`, `quest_reward_display_spell`,
  locale tables and the `conditions` rows of source types 19 and 20.
- **Field and check gaps:** the Phase 7 refinements above (objective fields, reference checks).

Implementation: add the tables to the catalogs; AC POI uses the same idea with fewer columns.

## Phase 20: Travel and instances

**State:** mostly not started. `game_tele` bookmarks, `areatrigger_teleport` reads and graveyard /
`world_safe_locs` editing exist. Open:

- **Destination editing:** pick a trigger and set its destination by clicking a spot in any map, with a
  jump-to-location preview (`areatrigger_teleport`; AC holds coordinates directly).
- **Spell destinations:** `spell_target_position` (2.5k) as draggable landing markers.
- **Instances:** `instance_template` (parent map, script) and `access_requirement` (level, item, quest,
  achievement) as pages [AC: `dungeon_access_template` / `_requirements`].
- **Shared teleport source:** drop the `AcTeleportSource` reuse deviation in favour of one editable shared
  source; the `.tele` list manager doubles as the editor's bookmark list.

## Phase 21: Areas and ambience

**State:** mostly not started. Client and spawned AreaTriggers draw read-only, their SmartAI rules edit and
their create properties are read. Open:

- **Weather** (`game_weather`) per season, **area auras** (`spell_area`, 968), **rest areas**
  (`areatrigger_tavern`), and **disables** (5.3k) as a filtered list with plain-language source types.
- **Spawned trigger authoring (TC):** place, shape (sphere radius, box extents, polygon vertices) and give
  properties to server-spawned AreaTriggers by reusing `AreaTriggerShapes` and `TransformGizmo`; their
  `areatrigger_template_actions` as action cards. AC: "not supported".
- A zone page opened from the minimap or area name is the natural home for weather, auras and rest triggers.

## Phase 22: Cinematics: conversations, scenes, player choice (TrinityCore only)

**State:** not started.

**Why:** retail storytelling uses conversations (248 templates, 702 lines), scenes and player choices; AC has
no such systems.

**Benefit:** a sequencer-style editor — actors on the left, lines on a timeline with broadcast text and
timing, and a play button that previews order. Player choices are edited as cards that look like the in-game
choice UI.

Implementation: `conversation_template`, `_actors`, `_line_template` (with condition strip, source 29);
`scene_template`; `playerchoice*` (responses, rewards, condition source 36). SmartAI actions that start a
conversation or play a scene open these editors. AC shows the section as not applicable.

## Phase 23: Text pipeline (hotfix export + broadcast and page text)

**State:** partial. TrinityCore can create a gossip broadcast text and its inline `hotfix_data` row. Open:

- **Hotfix export path:** a second `ITableCatalog` for `hotfixes`, an `SqlExport` target writing a separate
  `hotfixes-apply/revert.sql`, and an automatic `hotfix_data` row per changed record (TableHash from
  WoWDBDefs; `RecordId`; `Status`; a new `Id` from `IdAllocator`). Stale checks run against both databases.
  Pending changes show both scripts together.
- **Writing text** from the speech page and GameObject page-text pickers, with a male/female variant and an
  emote/sound strip; **editing** an existing text with a stale check against the client's version.
- **`page_text`:** a book editor with next-page chaining shown as pages.
- **AzerothCore:** `broadcast_text` lives in `acore_world`, so it is a world write with no hotfix rows.

**Why:** every retail NPC line, gossip and emote is a `broadcast_text` row in the `hotfixes` DB (238k), and
the editor can only link existing text. Books and plaques use `page_text` (world).

## Phase 24: Item editor

**State:** not started. Retail item tooltips are computed for the vendor page; there is no item source,
editor or creation flow.

**Why:** custom rewards, vendor goods and quest items need new items; today vendors and loot can only
reference existing ones. TC items are `hotfixes.item` plus `item_sparse`; AC items are
`world.item_template`.

**Benefit:** "Create item" from a loot table, vendor or quest reward opens a tooltip-first editor — edit the
in-game tooltip directly (name, quality colour, stats, armor, damage, icon picker) and it updates live.

Implementation: `Tc/AcItemSource` and catalogs, a tooltip preview (closing the retail item tooltip gap), the
Phase 23 hotfix path for TC, and rules for stat and slot validity. Spells stay out of scope.

**AC parity caveat (verify first):** the app never writes game files, and a 3.3.5 client may need a matching
`Item.dbc` row (display and inventory type) for a new `item_template` ID. Start the phase by testing a new ID
on a 3.3.5 client. If the item doesn't display, AC creation is limited to IDs or display info the client
already has (clone an existing ID range, or tell the user about a client patch); that is not full parity. TC
does not have this problem because `hotfix_data` sends the new rows to the client.

---

## Foundation, data and overlays

| Item | State | Open |
| --- | --- | --- |
| Guids widened to `ulong` | partial | Spawn search still binds the guid as `uint` (harmless, max ≈ 11.8M). |
| `ColumnKind` 64-bit kinds | partial | Validated and exported; no table uses them yet. |
| Hotfix rows with several `VerifiedBuild` versions | partial | Broadcast text takes the highest build; display and area-trigger reads take whichever row comes last. |
| WoWDBDefs FileDataID corpus check | not started | Needs client data. |
| Other hotfix overrides (`gameobject_display_info`, `spell_name`, `content_tuning`) | not started | `RetailGameObjectCatalog.Override` exists but is never called; vendor item names and quality already read `item_sparse`, gossip text `broadcast_text`. |
| Retail item tooltips | partial | Bonus lists, scaling stat distributions, socket stat cost, sockets and race restrictions, and resistances are not computed; numbers are not verified against in-game tooltips. |
| `creature_display_info_extra` | not started | Not read (empty in TDB); one of four texture columns of `creature_display_info` unread. |
| Retail area names (`AreaLookup`) | not started | Null for retail. |
| Retail graveyard zone links | open gap | `ClientServices.OpenRetail` passes no `AreaLookup`, so the retail graveyard editor accepts any ghost-zone ID (duplicate links are still refused) and `--graveyard-smoke` skips its unknown-area check. Fix: build `AreaLookup` from the retail AreaTable DB2 (`Db2Tables` already maps it to FileDataID 1353545). |
| `AreaTriggerActionSet`, `ContentTuning` tables | not started | Registered, not read. |
| Difficulty / phase preview filter | partial | Phase groups ignored (a spawn with only a `PhaseGroup` is hidden); `phaseUseFlags` read, not used; difficulty list hardcoded. |
| Area triggers from `AreaTrigger.db2` + `hotfixes.area_trigger` | partial | Trigger phases are not filtered by the previewed phase, and the `hotfixes` path tables are not read. |
| Server-spawned `areatrigger` overlay | partial | The create properties' roll, pitch and yaw, the spawn's phases and `SpawnDifficulties` are not applied; spawns are read-only (Phase 21). |
| Graveyard: safe location is a teleport target | partial | Counts `areatrigger_teleport` only; battleground start points are not checked. |
| Graveyard: locked-link controls and teleport line | unverified | Not inspected visually (e.g. Alterac Valley graveyard 169). |

### Known deviations

Built differently from the design; each is a candidate for unification when its area is next touched.

| Item | Deviation |
| --- | --- |
| `Spatial` / `Restrictions` per dialect | TC has its own copies in `TcSpawnSource`; AC stays on the shared `WorldReadSession`. |
| Teleports from `game_tele` | Reuses `AcTeleportSource` (same schema); no `TcTeleportSource`. Phase 20 drops this in favour of one editable shared source. |
| Graveyard healer lookup | TC reuses `AcGraveyardSource.ReadHealers`. |
| Unchosen geoset groups | Variant 1, except ears (7) and head (32) use variant 2; the rule comes from data, not a documented client rule. |
| Retail swamp liquid (material 14) | Screen reflections replace the client reflection render, interaction buffers are absent and constant mapping is inferred. |

## Performance

Measured with `--perf-smoke` on retail Stormwind; designs and numbers in
[performance.md](../status/performance.md).

| Item | State | Open |
| --- | --- | --- |
| Renderer | in progress | The project runs Forward+ (Godot's desktop default). The settled Stormwind frame is 8.5–13 ms and CPU-bound, mostly WMO shells, embedded doodads, ADT doodads and shadows (`--perf-breakdown`). Re-baseline `--perf-smoke` on Forward+; reducing building and shadow cost needs the user's decision. |
| Upload budget | not started | Model uploads are capped at 8 surfaces and 1.5 ms per frame. With three decode workers about 40 % idle during arrival, the cap or the decoded-queue backpressure (three entries, 256 MiB) is the likely limit, unmeasured. First candidate: move the numerics-to-Godot array conversion onto the decode worker. |
| Spawn table indexes | not started | `creature` (map index only) and `gameobject` (no map index) are scanned for every region query (150 ms of about 200 ms). Suggested `(map, position_x, position_y)` indexes are in [performance.md](../status/performance.md); the app cannot add them. |
| Connection per query | not started | Pooling is off by design; each overlay query opens a connection (about 20 ms). |

## Tests

| Item | State | Open |
| --- | --- | --- |
| CI coverage | not started | Not covered: tests that need client data, a smoke against a real retail client in an exported build, and a full smoke suite in CI. |
| `Db2Tables` thread safety | partial | Retail test classes still run one at a time (`[Collection(RetailData.Collection)]`); drop that once the retail corpus tests pass in parallel. |
| `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes` accepted failures | not started | Stale "until the retail WMO milestone" comments; tighten or remove the filter. |

Deferred tests are listed in [needed-tests.md](../status/needed-tests.md).

## Retail rendering

| Item | State | Open |
| --- | --- | --- |
| Equipment (`NPCModelItemSlotDisplayInfo` → `ItemDisplayInfo`) | not started | Creatures render without their items. |
| Customization beyond geosets and hair/eye | not started | Bone sets, skinned models, particle colours. |
| Humanoid whose extra has no bake material | partial | Drawn without customization (every geoset shows). |
| Parent skeletons (`.skel` `SKPD`) | not started | Parent skeletons are not followed. |
| Animation names (AnimationData DB2) | not started | The preview menu shows IDs only. |
| M2 multitexture batches and spline tracks | partial | Texture weights and per-layer UV animation are not applied (weighted layers draw at full weight, crossfades show their base texture); guild tabard shaders (26 to 28) and more than four units use the base texture with a warning; splines interpolate linearly. |
| WMO material shaders | partial | The third UV set (third layers read the second) is not read; an unmapped shader still warns. A retail interior group (MOHD flag 0x2) lights itself as mix(interior, world, vertex alpha), taken from wowdev.wiki and checked by eye in one Khaz Algar cave (`fdid:5305801`) only: the `* 2` on the interior term and the lack of portal attenuation are unverified. |
| Terrain layers 5–8 | not started | Only the first four are drawn. |
| Retail azerite liquid (material 18) | partial | Not ported: the per-layer UV matrices, the per-light medium loop, and the refraction/reflection/glow beyond the body approximation; the depth coefficient is an estimate. See [rendering.md](../architecture/rendering.md#material-18-azerite). |
| Underwater lighting and fog | partial | Under a liquid the viewer keeps the region's ordinary light bands and fog (retail switches to the LightParams water colours and shorter fog), and there is no underwater post pass (tint, distortion, the water-line window). Only the default weather slot is read. |
| Uncompressed alpha size | partial | Follows the WDT big-alpha flag; decide per layer if a map disagrees. |
| Path-named placements | partial | Resolve only through the community listfile. |

## WoW: Forever

| Item | State | Open |
| --- | --- | --- |
| World database | not started | There is no private-server branch for this client, so it opens with overlays, editors, teleports and the connection prompt off. Adding one means a schema and a dialect, not a client change. |
| Pinned definitions | open gap | `Data/WoWDBDefs` lists 1.60.1 up to 70450 while the install is 70790. DBCD resolves tables by layout hash so they load; bump the submodule when upstream lists the newer builds so layouts stay validated. |

---

## Out of scope

Tables that need no editor: `command`, `trinity_string`, `updates*`, `version`, `player_*` stats,
`playercreateinfo*`, `pet_levelstats`, `battle_pet_*`, `garrison_*`, `spell_proc`, `spell_custom_attr`,
`spell_group*`, `spell_script_names`, `skill_*`, `exploration_basexp`, `achievement_*`, `guild_rewards*`,
`lfg_*`, `battleground_*`, `serverside_spell*`, `warden_checks` and `*_locale` beyond quest text: server
tuning or class design, not world building. A generic read-only table viewer at most. Spells are their own
product, not a phase here.

## Cross-phase reuse map

| Need | Reuse |
| --- | --- |
| Page layout and fields | `EntityPage` / `FieldSpec` / `ItemListSpec`, `PageRenderer` |
| Edits, undo, export | `InteractionDraft`, `RowEdit`, `DraftHistory`, `SqlExport`, `PendingChanges` |
| Pickers | `RecordPicker`, `CreatureSection.SearchState<T>`, `IClientLookup.Search*` / `Exists(ClientRecord, id)` |
| Positions and gizmos | `TransformGizmo`, `ScenePicking`, `ViewerShell.Ground`, `RegionOverlay` / `RegionQuery` |
| Generated catalogs | `Tools/generate_smart_catalog.py` pattern → GO types, conditions, loot |
| Behavior | `BehaviorPanel` with new SmartAI source types (1 for GameObjects, …) |
| Conditions | `ConditionCatalog` + the Phase 13 strip, used by every later editor |

## Recommended order and sizing

12 (GameObjects, M) → 13 (conditions, M) → 15 (loot, L) → 14 remainder (movement, M) → 16 (pools, M) →
17 (events/phases, M) → 18 (creature pages, M) → 19 (quest completion, M) → 20 (travel, S) → 21 (ambience,
M) → 23 (text, M) → 24 (items, L) → 22 (cinematics, M). Phases 10 and 11 continue in parallel as their
remaining items come up.

Sizes (S/M/L) include full AC+TC parity (about 1.5× a single-dialect phase). Phases on TC-only systems
(spawn groups, 21's spawned triggers, 22) cost less on the AC side because it says "not supported". Loot
comes before movement because it touches more players' experience; cinematics come last because they are TC
only and need the Phase 23 text path for authoring new lines.
