# Scripting capabilities and host profile

Reference for the semantic node catalog in `Foundation/Scripting/Catalog` and the ALE
(`ale-azerothcore`) profile that binds it to mod-ale. The design and milestones are in
[plans/scripting-plan.md](../plans/scripting-plan.md); the pipeline is described in
[architecture/scripting.md](../architecture/scripting.md).

## How catalogs fit together

A document names semantic node kinds only (for example `unit.cast_spell`,
`event.creature.on_died`). Two catalog layers resolve them:

- `Profiles/core/` defines the semantics: capabilities (display name, category, receiver types,
  typed parameters, returns, `effect`, `tier`, `availability`, `performance`), events (scope
  parameters, handler parameters, returns, availability), and shared enums.
- `Profiles/<host>/` defines one runtime: `host.json` (label, conventions, caveats, host enums),
  `capability-bindings.json` (call name, receiver style, ALE argument order and defaults, return
  shape) and `event-bindings.json` (registrar, event number, scope arguments, handler parameter
  name).

Validation and compilation use the merged profile. An implemented capability without a binding,
an event without a registration, or a binding argument without a parameter is a loader error, so
the editor cannot offer a node the host cannot run. The current host is
`AcScriptHosts.Id = "ale-azerothcore"` (`ALE (AzerothCore)`).

The event and binding files are generated from a mod-ale checkout:

```bash
python Tools/generate_script_catalog.py --source <mod-ale> --core <core-dir> --host <host-dir>
```

Capabilities are curated by hand because names and parameter shapes are editor semantics, not a
direct reflection of the Lua API.

## Capabilities (29)

`effect` is `read`, `write` or `control`; writes are ordered relative to reads in a handler. The
argument column is the ALE call as generated; `?` marks an optional semantic parameter, and a
binding default is shown as `=value`.

| Id | Display | Parameters | ALE call | Receiver |
|---|---|---|---|---|
| `object.to_player` | To Player | — | `ToPlayer()` | colon |
| `object.get_guid` | Get GUID | — | `GetGUID()` | colon |
| `object.get_guid_low` | Get GUID Low | — | `GetGUIDLow()` | colon |
| `object.get_entry` | Get Entry | — | `GetEntry()` | colon |
| `unit.get_level` | Get Level | — | `GetLevel()` | colon |
| `unit.get_health` | Get Health | — | `GetHealth()` | colon |
| `unit.has_aura` | Has Aura | `spell:SpellId` | `HasAura(spell)` | colon |
| `unit.get_threat` | Get Threat | `target:Unit` | `GetThreat(target)` | colon |
| `unit.send_unit_say` | Say | `message:string, language:int?` | `SendUnitSay(message, language=0)` | colon |
| `unit.send_unit_yell` | Yell | `message:string, language:int?` | `SendUnitYell(message, language=0)` | colon |
| `unit.cast_spell` | Cast Spell | `target:Unit, spell:SpellId, triggered:bool?` | `CastSpell(target, spell, triggered=false)` | colon |
| `unit.add_aura` | Add Aura | `spell:SpellId, target:Unit` | `AddAura(spell, target)` | colon |
| `unit.remove_aura` | Remove Aura | `spell:SpellId` | `RemoveAura(spell)` | colon |
| `unit.set_health` | Set Health | `health:int` | `SetHealth(health)` | colon |
| `unit.deal_damage` | Deal Damage | `target:Unit, damage:int, durabilityLoss:bool?, school:int?, spell:SpellId?` | `DealDamage(target, damage, durabilityLoss=true, school=7, spell=0)` | colon |
| `unit.deal_heal` | Deal Heal | `target:Unit, spell:SpellId, amount:int, critical:bool?` | `DealHeal(target, spell, amount, critical=false)` | colon |
| `player.send_broadcast_message` | Send Message | `message:string` | `SendBroadcastMessage(message)` | colon |
| `player.add_item` | Add Item | `item:ItemEntry, count:int?` | `AddItem(item, count=1)` | colon |
| `player.get_item_count` | Get Item Count | `item:ItemEntry, checkBank:bool?` | `GetItemCount(item, checkBank=false)` | colon |
| `player.teleport` | Teleport | `map:MapId, x:float, y:float, z:float, orientation:float` | `Teleport(map, x, y, z, orientation)` | colon |
| `creature.respawn` | Respawn | — | `Respawn()` | colon |
| `gameobject.respawn` | Respawn | — | `Respawn()` | colon |
| `gameobject.despawn` | Despawn | — | `Despawn()` | colon |
| `server.send_world_message` | Send World Message | `message:string` | `SendWorldMessage(message)` | global |
| `player.get_by_guid` | Get Player by GUID | `guid:ObjectGuid` | `GetPlayerByGUID(guid)` | global |
| `map.get_by_id` | Get Map by ID | `map:MapId` | `GetMapById(map)` | global |
| `instance.get_data` | Get Instance Data | — | `GetInstanceData()` | colon |
| `instance.save` | Save Instance Data | — | `SaveInstanceData()` | colon |
| `debug.print` | Print | `message:string` | `print(message)` | global |

`receiver: colon` means the call is emitted on the wired `self` object; `global` means it is a
free function. Optional arguments use the binding default when the pin is left empty; a trailing
argument with no default at all is omitted.

Control flow, comparisons, numeric/string operators, variables and user functions are
**language-level built-ins**, not host capabilities: `flow.branch`, `flow.sequence`,
`flow.for_range`, `flow.foreach`, `list.create`, `flow.equal`/`not_equal`/`less`/`less_equal`/`greater`/`greater_equal`/`and`/`or`/`not`,
`math.add`/`subtract`/`multiply`/`divide`/`modulo`/`min`/`max`, `string.concat`/`length`,
`flow.variable_get`/`variable_set`, `flow.parameter_get`, `flow.call_function` and `flow.return`.
`timer.once`/`timer.repeat` are builtins with a continuation (`timer.event_id`, `timer.object`,
`timer.repeats` read the callback arguments). They live in `ScriptBuiltinCatalog` and never appear in a host pack.

## Events

`Profiles/core/events.json` contains **230 events**, all `implemented` in the pinned mod-ale
(`84b85cc`), across these categories:

| Category | Events | Category | Events |
|---|---:|---|---:|
| Player | 71 | Instance | 7 |
| Creature | 37 | Map | 7 |
| Server | 34 | Group | 6 |
| All Creature | 13 | Item | 5 |
| GameObject | 13 | Vehicle | 5 |
| Guild | 11 | BattleGround | 4 |
| Ticket | 4 | Packet | 3 |
| Spell | 3 | Creature Gossip | 2 |
| GameObject Gossip | 2 | Item Gossip | 2 |
| Player Gossip | 1 | | |

An event definition carries its scope parameters (`entry` for creature events, `map`/`instance`
where the registrar requires them), typed handler parameters (nullable for object userdata) and
its returns. Example (`event.creature.on_died`):

```json
{
  "id": "event.creature.on_died",
  "display": "Creature · On Died",
  "category": "Creature",
  "scopeParams": [{ "name": "entry", "type": "CreatureEntry" }],
  "params": [
    { "name": "creature", "type": "Creature", "nullable": true },
    { "name": "killer", "type": "Unit", "nullable": true }
  ],
  "returns": [],
  "availability": "implemented",
  "performance": "normal"
}
```

Its ALE binding:

```json
{
  "register": "RegisterCreatureEvent",
  "scopeArgs": ["entry"],
  "eventId": 4,
  "eventName": "CREATURE_EVENT_ON_DIED",
  "handlerEventParam": "event",
  "returnDefaults": []
}
```

Compiling the template graph therefore emits
`RegisterCreatureEvent(1234, 4, __acw_n1) -- CREATURE_EVENT_ON_DIED`.

Event groups with a C++ handler signature that has no object (server, arena, ticket, packet) have
an empty or packet scope; their bindings name the registrar and event number. High-frequency
events (`ON_UPDATE`, AI update) are marked `performance: high` and produce an informational
diagnostic so handlers stay small.

## Enums

`Profiles/core/enums.json` currently defines:

| Enum | Values |
|---|---|
| `PowerType` | Mana 0, Rage 1, Focus 2, Energy 3, Happiness 4, Runes 5, RunicPower 6 |
| `QuestStatus` | None 0, Complete 1, Incomplete 3, Failed 5, Rewarded 6 |

Host-specific enums live in `host.json` (`enums` is empty for ALE today). Enum values are offered
as pickers and validated as integers.

## Runtime conventions and caveats

From `Profiles/ale-azerothcore/host.json`:

- Generated code targets Lua 5.1 syntax even though the local build runs Lua 5.2.
- Object timers use `RegisterEvent` with callback `(eventId, delay, repeats, object)`; global
  timers use `CreateLuaEvent` with `(eventId, delay, repeats)`.
- Async queries use `WorldDBQueryAsync` and receive the `query` callback argument.
- Object-scoped timers tick only while the object is in world and die with it.
- `Map:GetInstanceData()` returns `nil` for C++-scripted instances; instance data restores from
  the last save on reload, so save at checkpoints.
- Core object userdata is invalid after the handler returns; store GUIDs instead.
- `RemoveEventById` is a global function (there is no `Global` table) with a known argument bug;
  prefer object-scoped timers.

## Diagnostics

Validation uses stable codes; the full table is in
[architecture/scripting.md](../architecture/scripting.md#validation). Catalog/loader errors are
`SC0601`–`SC0602`; export refusals are `SC0501`–`SC0504`.

## Adding a capability

1. Add the semantic entry to `Profiles/core/capabilities.json` (unique `id`, display, category,
   receiver types, typed params with defaults, returns, effect/tier/availability/performance).
2. Add the matching binding to `Profiles/ale-azerothcore/capability-bindings.json` with the ALE
   call name, receiver style, argument order and host defaults.
3. Add or update tests in `Tests/ACWorldEdit.Tests/Scripting/ScriptCatalogTests.cs` and a golden
   graph if the node participates in one.
4. Update this table and `docs/status/completed-work.md` when coverage lands.
