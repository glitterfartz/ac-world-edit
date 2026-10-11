# Simple implementation plan

Use the existing standalone Godot viewer and ordinary existing creatures on outdoor maps.
Source database and client files stay unchanged.

## 1. Display and select creatures — current milestone

Read nearby spawns and template names from the world database. Show labeled markers at their
server positions. Select by clicking a marker or choosing an entry in the nearby-creature list.
Show GUID, template ID, map, coordinates and heading in read-only details. Refresh as the camera
changes region/map; show connection failures and result limits clearly.

Done when real spawns display, viewport/list selection agree, and navigation/map changes cannot
attach stale results. Queries use read-only transactions; credentials stay in external config.
Markers are previews; full models and in-game parity are not required for this read-only slice.

## 2. Move creatures in a saved draft — later

Move/yaw an existing creature, undo/redo, leave the area and reopen without losing edits.
Verify coordinates/headings against actual client landmarks before enabling placement editing.
Preserve source rows and unknown fields. No new spawns, duplication or deletion in this slice.

## 3. Export a reviewed SQL change — later

Export bounded forward/reverse SQL with original rows and freshness checks. Prove application
and reversal in an explicitly authorized disposable database before declaring export ready.
The application remains export-only and never applies changes to source.

## Defer

GameObjects, ID allocation, WMO interiors, templates/quests/scripts, terrain writing and live apply.
Use standalone Godot without an EditorPlugin comparison gate. Full performance certification and
advanced snapshot machinery follow usage evidence; retain practical responsiveness and complete
read snapshots for current features. Unverified checks remain recorded, not claimed passed.
