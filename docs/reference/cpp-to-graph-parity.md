# C++ ALE to graph parity

How the semantic graph maps onto mod-ale (`84b85cc`) and what is deliberately not exposed yet.
Part 1 covers the event and function surface; the Eluna/TrinityCore half arrives with the
multi-host milestone. The machine-readable catalog is
[`Foundation/Scripting/Catalog`](../../Foundation/Scripting/Catalog) and the user-facing tables are in
[scripting-capabilities.md](scripting-capabilities.md).

## Source of truth

The event catalog is generated from the module, not from its docs:

| Input | Used for |
|---|---|
| `src/LuaEngine/hooks/Hooks.h` | event enum names and numeric ids |
| `src/LuaEngine/hooks/*.cpp` | the actual argument push order |
| `src/LuaEngine/GlobalMethods.h` | registration function name and argument order (`entry`, `event`, `fn`, `shots`) |
| `src/LuaEngine/methods/*.h` | function-table breadth (the capability gap) |

```bash
python Tools/generate_script_catalog.py --source <mod-ale> --check
```

`--check` compares the generated files byte-for-byte with the checked-in profiles; it is clean
against the pinned clone. Curated display/performance/availability changes go in
`Catalog/script_catalog_overrides.json`, so regeneration stays deterministic.

## Event parity

All 230 generated events are in the catalog; 217 are marked `implemented`. The 13 not implemented
have no C++ hook body in the pinned module:

| Category | Unimplemented events |
|---|---|
| Packet | `on_packet_receive_unknown` |
| Server | `on_grid_load`, `on_grid_unload`, `on_network_start`, `on_network_stop`, `on_packet_receive_unknown`, `on_socket_close`, `on_socket_open` |
| Vehicle | `on_add_passenger`, `on_install`, `on_install_accessory`, `on_remove_passenger`, `on_uninstall` |

The graph hides them from the palette and the validator reports `SC0104` if one is present in a
document. Each implemented event carries its scope arguments, typed handler parameters (object
userdata is nullable), returns, performance mark and registration binding.

Event families and their registrars:

| Family | Registrar | Scope |
|---|---|---|
| Creature, GameObject, Item, Spell | `Register*Event` | entry / gameobject entry / item entry / spell id |
| Creature/GameObject/Item/Player gossip | `Register*GossipEvent` | entry, or menu id for the player menu |
| Map, Instance | `RegisterMapEvent`, `RegisterInstanceEvent` | `mapId`, `instanceId` |
| Server, Player, Guild, Group, Vehicle, Battleground, Ticket, All Creature | their registrar | none (packet opcode for packet events) |

Gossip `on_hello`/`on_select` handlers must return `false` when the default gossip action should
run; the catalog marks the `handled` return slot on those events.

## Capability parity (first slice)

The curated capability set is 29 host actions: object casts and GUID/entry reads, unit level,
health, aura, threat and speech, unit cast/aura/health/damage/heal, player message/item/teleport,
creature and GameObject respawn/despawn, world message and player-by-GUID. Bindings carry the ALE
argument order and host defaults (for example `CastSpell(target, spell, triggered = false)`).

The pinned module exposes far more through `LuaFunctions.cpp` (37 method tables and 1,713
entries). These areas are intentionally not exposed yet:

| Area | Why not yet | Where it lands |
|---|---|---|
| Instance data read/write/save | needs typed tables and the instance lifecycle | Milestone 6 |
| Timers, async queries, http | latent IR and object-lifetime rules | Milestone 6 |
| Lists/tables and iteration | no value source in the graph yet | Milestone 6 |
| Playerbots, arena, custom server modules | outside the curated tiers | later packs |
| TrinityCore Eluna methods | different host shape | Milestone 5 |

## What "parity" means here

A graph node is exposed only when the catalog has a semantic definition **and** the active host
binds it. The loader refuses an implemented capability without a binding and an event without a
registration, so the palette can never offer a node the runtime cannot execute. "Parity" therefore
means parity of the *offered* surface, not every C++ function: the tiers grow by adding curated
capabilities and bindings, and the generator keeps the event half honest.
