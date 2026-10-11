# Simple implementation plan

Use the existing standalone Godot viewer and ordinary existing creatures on outdoor maps.
Source database and client files stay unchanged.

Current addition requested by the user: improve client visual fidelity with native creature models,
animation previews, terrain doodads/buildings and WMO interiors. Selecting a creature exposes a
movement gizmo with saved local position drafts and undo/redo. Keep these previews separate from
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

## 2. Move creatures in a saved draft — positions implemented; yaw and verification later

Move/yaw an existing creature, undo/redo, leave the area and reopen without losing edits.
Position gizmos now edit local previews. Verify coordinates/headings against actual client landmarks
before declaring placement/export ready; yaw editing and terrain snapping still need implementation.
Preserve source rows and unknown fields. No new spawns, duplication or deletion in this slice.

## 3. Export a reviewed SQL change — later

Export bounded forward/reverse SQL with original rows and freshness checks. Prove application
and reversal in an explicitly authorized disposable database before declaring export ready.
The application remains export-only and never applies changes to source.

## Defer

GameObjects, ID allocation, templates/quests/scripts, terrain writing and live apply.
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
