# Simple implementation plan

Use the existing standalone Godot viewer and existing creatures/GameObjects on outdoor and interior maps.
Source database and client files stay unchanged.

Current addition requested by the user: complete step 3 placement editing (yaw, ground snapping,
landmark verification) with an editor menu bar and toolbar, alongside client visual fidelity for
native creature models, animation previews, terrain doodads/buildings and WMO interiors. Selecting a
creature exposes move/rotate gizmos with saved local drafts and undo/redo. Keep these previews separate from
deployment readiness; source data stays read-only. Character equipment/appearance, client effects
and exact client visual parity still need focused follow-up; do not claim them complete.

## 1. Display and select creatures — implemented

Read nearby spawns and template names from the world database. Show native models with textures
and animations, with labeled markers as a fallback. Select a model or a nearby-creature list entry.
Show GUID, template ID, map, coordinates and heading in read-only details. Refresh as the camera
changes region/map; show connection failures and result limits clearly.

Done when real spawns display, viewport/list selection agree, and navigation/map changes cannot
attach stale results. Queries use read-only transactions; credentials stay in external config.
Models and clips preview native client data; complete in-game visual parity remains follow-up work.

## 2. Control scene visibility and load GameObjects — implemented

Use the Scene explorer to toggle scenery props, buildings/interiors, creatures and GameObjects.
Preview SQL event membership, phases/difficulties, pools, hidden helpers, conditions and script bindings.
Defaults hide special-case spawns conservatively; manual event and All stored modes support inspection.
Read GameObject display paths from the client, share native M2/WMO resources and allow model/list selection.
Keep GameObjects read-only; interaction scripts and open/close behavior are future work.

Done when filters run before the result limit, inactive GUID 96098 is hidden by default, layer hiding
stops work, and broad inspection cannot bypass row/model budgets. Use 200 yards, 384 rows and 128 models
per spawn layer by default. SQL previews do not guarantee live client visibility.

## 3. Place creatures in a saved draft — implemented; client landmark verification pending

Move/yaw an existing creature, undo/redo, leave the area and reopen without losing edits. Move and
rotate gizmos, typed pose entry, drop to ground (terrain + WMO floors, fail-closed, optional snap on
drop) and reset to source all record local draft commands. A menu bar (File/Edit/View/Help) and
toolbar (tool modes, history, ground snap, rotation step) carry the commands.

Done when coordinates/headings match the client at reference landmarks: compare several creatures
in-game via the copied `.go xyz` command, including one inside a building and one rotated draft.
Until then placement is not export-ready. Preserve source rows and unknown fields. No new spawns,
duplication or deletion in this slice. M2 doodads are not snap targets.

## 3b. Graveyards in a saved draft — implemented

Show `game_graveyard` rows near the camera as selectable tombstone icons with faction-colored rings
and labels, each with its `graveyard_zone` links and nearby spirit healers. Edit position, name and
zone links (area/zone + faction) in a local graveyard draft with undo/redo, validated against the
server's loading rules (AreaTable zone, faction 0/469/67, no duplicate link). Export joins step 4.
Battleground/default graveyard IDs are referenced from core C++; spirit healers are not moved.

## 3c. Creature interactions — preview and draft editing implemented; quest editor skeleton

A short right-click (or Interact…) opens a client-style window: gossip text and options (submenus,
NpcFlag gating), quests, vendor items with client icons, trainer spells, spell click, flight
master and script/condition notes. Edit mode records gossip/npc_text/option, quest-link, vendor,
trainer, spell-click and npcflag/subname changes in a local interaction draft, validated against
server loader rules, with shared-record forks and reserved new IDs. Quest content opens in a
read-only Quest Editor skeleton (toolbar Quests / View → Quest editor). Conditions, SmartAI, C++
scripts and player state are listed, never evaluated; export joins step 4.

## 4. Export a reviewed SQL change — later

Export bounded forward/reverse SQL with original rows and freshness checks. Prove application
and reversal in an explicitly authorized disposable database before declaring export ready.
The application remains export-only and never applies changes to source.

## Defer

GameObject editing/interactions, ID allocation, templates/quests/scripts, terrain writing and live apply.
Use standalone Godot without an EditorPlugin comparison gate. Full performance certification and
advanced snapshot machinery follow usage evidence; retain practical responsiveness and complete
read snapshots for current features. Unverified checks remain recorded, not claimed passed.

## Potential Future Workflows

- Opening an existing AzerothCore world and placing a new NPC.
- Creating a new creature template and configuring its behavior.
- Designing a quest and connecting it to NPCs, items, and objectives.
- Creating an NPC patrol route directly within the 3D world.
- Constructing an interactive encounter using SmartAI or Lua.
- Editing an existing GameObject and its interactions.
- Modifying terrain or placing client-side world assets.
- Testing a change and safely deploying it to an AzerothCore server.
- Identifying and resolving a broken database relationship.
- Extending the editor with support for an additional emulator core.
- Pacing and moving existing GameObjects
- Support for different phase editing
- Scripted behavior simulation and editing

Water previews load native outdoor and WMO liquid grids and animated client textures. Full client
water lighting, reflections/refraction and underwater effects remain future fidelity work.
