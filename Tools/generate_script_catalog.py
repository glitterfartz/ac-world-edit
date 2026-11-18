#!/usr/bin/env python3
"""Generate the semantic ALE event catalog and its host bindings from a mod-ale checkout.

Source of truth:
  * Hooks.h        event enum names and numeric ids,
  * hooks/*.cpp    the actual handler argument pushes (the doc comments are stale),
  * GlobalMethods.h  registration function order (entry, event, fn, shots).

The semantic core (flags, params, returns) is written once; the host pack only carries the
registration name, scope values, numeric event id and enum name. Run with --check in CI once the
pinned clone is available; the checked-in JSON is the build input.

Usage:
  python3 Tools/generate_script_catalog.py --source /path/to/mod-ale
  python3 Tools/generate_script_catalog.py --source /path/to/mod-ale --check
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

ENUM_SCOPES = {
    "PacketEvents": ("Packet", "RegisterPacketEvent", [("opcode", "int")], "event.packet"),
    "ServerEvents": ("Server", "RegisterServerEvent", [], "event.server"),
    "PlayerEvents": ("Player", "RegisterPlayerEvent", [], "event.player"),
    "GuildEvents": ("Guild", "RegisterGuildEvent", [], "event.guild"),
    "GroupEvents": ("Group", "RegisterGroupEvent", [], "event.group"),
    "VehicleEvents": ("Vehicle", "RegisterVehicleEvent", [], "event.vehicle"),
    "CreatureEvents": ("Creature", "RegisterCreatureEvent", [("entry", "CreatureEntry")], "event.creature"),
    "GameObjectEvents": ("GameObject", "RegisterGameObjectEvent", [("entry", "GameObjectEntry")], "event.gameobject"),
    "ItemEvents": ("Item", "RegisterItemEvent", [("entry", "ItemEntry")], "event.item"),
    "BGEvents": ("BattleGround", "RegisterBGEvent", [], "event.battleground"),
    "TicketEvents": ("Ticket", "RegisterTicketEvent", [], "event.ticket"),
    "SpellEvents": ("Spell", "RegisterSpellEvent", [("entry", "SpellId")], "event.spell"),
    "AllCreatureEvents": ("All Creature", "RegisterAllCreatureEvent", [], "event.allcreature"),
}

GOSSIP_VARIANTS = [
    ("CreatureGossipBindings", "Creature Gossip", "RegisterCreatureGossipEvent", [("entry", "CreatureEntry")], "event.creature_gossip"),
    ("GameObjectGossipBindings", "GameObject Gossip", "RegisterGameObjectGossipEvent", [("entry", "GameObjectEntry")], "event.gameobject_gossip"),
    ("ItemGossipBindings", "Item Gossip", "RegisterItemGossipEvent", [("entry", "ItemEntry")], "event.item_gossip"),
    ("PlayerGossipBindings", "Player Gossip", "RegisterPlayerGossipEvent", [("menuId", "int")], "event.player_gossip"),
]

INSTANCE_VARIANTS = [
    ("Map", "RegisterMapEvent", [("mapId", "MapId")], "event.map"),
    ("Instance", "RegisterInstanceEvent", [("instanceId", "InstanceId")], "event.instance"),
]

OBJECT_TYPES = {"Player", "Creature", "Unit", "WorldObject", "Object", "GameObject", "Item",
                "Spell", "SpellInfo", "Aura", "Quest", "Map", "Group", "Guild",
                "WorldPacket", "ALEQuery", "ChatHandler"}

RETURN_OVERRIDES = {
    "PACKET_EVENT_ON_PACKET_RECEIVE": [("handled", "bool", False), ("newPacket", "WorldPacket", True)],
    "PACKET_EVENT_ON_PACKET_SEND": [("handled", "bool", False), ("newPacket", "WorldPacket", True)],
    "SERVER_EVENT_ON_PACKET_RECEIVE": [("handled", "bool", False), ("newPacket", "WorldPacket", True)],
    "SERVER_EVENT_ON_PACKET_SEND": [("handled", "bool", False), ("newPacket", "WorldPacket", True)],
    "PLAYER_EVENT_ON_CHAT": [("suppress", "bool", False), ("newMessage", "string", False)],
    "PLAYER_EVENT_ON_COMMAND": [("handled", "bool", False)],
    "GOSSIP_EVENT_ON_HELLO": [("handled", "bool", False)],
    "GOSSIP_EVENT_ON_SELECT": [("handled", "bool", False)],
    "CREATURE_EVENT_ON_QUEST_ACCEPT": [("handled", "bool", False)],
    "CREATURE_EVENT_ON_QUEST_REWARD": [("handled", "bool", False)],
    "GAMEOBJECT_EVENT_ON_QUEST_ACCEPT": [("handled", "bool", False)],
    "GAMEOBJECT_EVENT_ON_QUEST_REWARD": [("handled", "bool", False)],
    "ITEM_EVENT_ON_QUEST_ACCEPT": [("handled", "bool", False)],
    "ITEM_EVENT_ON_QUEST_REWARD": [("handled", "bool", False)],
}

CATEGORY_PRIMARY = {
    "Creature": ("creature", "Creature"),
    "Creature Gossip": ("creature", "Creature"),
    "GameObject": ("gameObject", "GameObject"),
    "GameObject Gossip": ("gameObject", "GameObject"),
    "Item": ("item", "Item"),
    "Item Gossip": ("item", "Item"),
    "Player": ("player", "Player"),
    "Player Gossip": ("player", "Player"),
    "Unit": ("unit", "Unit"),
    "Spell": ("spell", "Spell"),
    "Map": ("map", "Map"),
    "Instance": ("map", "Map"),
    "All Creature": ("creature", "Creature"),
}

TYPE_HINTS = [
    (r"^p?player$", "Player"), (r"^p?creature$", "Creature"), (r"^p?gameobject$|^p?go$", "GameObject"),
    (r"^p?item$", "Item"), (r"^p?quest$", "Quest"), (r"^p?unit$", "Unit"), (r"^p?target$", "Unit"),
    (r"^p?killer$|^p?victim$|^p?attacker$", "Unit"), (r"^p?summon(ed)?$", "Creature"),
    (r"^p?sender$|^p?receiver$", "Player"),
    (r"^p?map$", "Map"), (r"^mapid$", "MapId"), (r"^instanceid$", "InstanceId"),
    (r"^p?spellinfo$", "SpellInfo"), (r"^p?spell$", "Spell"), (r"^spellid$|^spell_id$", "SpellId"),
    (r"^p?aura$", "Aura"), (r"^p?group$", "Group"), (r"^p?guild$", "Guild"),
    (r"^p?packet$", "WorldPacket"), (r"^p?instance(data)?$|^data$", "Table"),
    (r"^guid$|^p?guid$", "ObjectGuid"), (r"^loot$|^loottable$|^table$", "Table"),
    (r"^(effindex|eff_idx|effectindex)$", "int"), (r"^(amount|damage|heal|count|entry|id|type|eventid|event_id|opcode|menuid|state|oldstate|newstate|code|mask|mapid)$", "int"),
    (r"^(msg|message|text|chatmessage|chat_message|newmessage)$", "string"),
    (r"^(isbefore|hasnext|success|critical|triggered|open|reload|enabled|disabled|friendly)$", "bool"),
    (r"^(x|y|z|o|orientation|speed|delay|rate|percent|distance)$", "float"),
]


def engine_root(argument: str) -> Path:
    root = Path(argument).expanduser().resolve()
    for candidate in (root / "src" / "LuaEngine", root):
        if (candidate / "Hooks.h").exists():
            return candidate
    raise SystemExit(f"Hooks.h not found under {root}")


def parse_enums(root: Path):
    text = (root / "Hooks.h").read_text(encoding="utf-8", errors="replace")
    enums = {}
    for match in re.finditer(r"enum\s+(\w+)\s*\{(.*?)\}", text, re.S):
        name, body = match.group(1), match.group(2)
        if name == "RegisterTypes":
            continue
        events = []
        for line in body.splitlines():
            event = re.match(r"\s*([A-Z][A-Z0-9_]*)\s*=\s*(\d+)\s*,?\s*(?://\s*(.*))?$", line)
            if event:
                events.append({
                    "name": event.group(1),
                    "id": int(event.group(2)),
                    "comment": (event.group(3) or "").strip(),
                })
        enums[name] = events
    return enums


def parse_hooks(root: Path):
    hooks = []
    for path in sorted((root / "hooks").glob("*.cpp")):
        text = path.read_text(encoding="utf-8", errors="replace")
        starts = list(re.finditer(r"START_HOOK\w*\s*\(([^)]*)\)", text))
        for index, start in enumerate(starts):
            args = start.group(1)
            tokens = re.findall(r"\b[A-Z][A-Z0-9_]*_EVENT_[A-Z0-9_]*\b", args)
            if not tokens:
                continue
            end = starts[index + 1].start() if index + 1 < len(starts) else len(text)
            block = text[start.end():end]
            pushes = re.findall(r"^\s*Push\s*\(\s*(.*?)\s*\)\s*;", block, re.M)
            hooks.append({"event": tokens[0], "file": path.name, "args": args, "pushes": pushes})
    return hooks


def snake(name: str) -> str:
    return name.split("_EVENT_", 1)[-1].lower()


def display_name(raw: str) -> str:
    return " ".join(word.capitalize() for word in snake(raw).split("_"))


def infer_param(expression: str, index: int, category: str | None = None):
    text = re.sub(r"[^A-Za-z0-9_]", "", expression).strip()
    lowered = text.lower()
    if lowered == "me" and category in CATEGORY_PRIMARY:
        return CATEGORY_PRIMARY[category]
    for pattern, value in TYPE_HINTS:
        if re.search(pattern, lowered):
            name = text[1:] if re.match(r"^p[A-Z]|^p_", text) else text
            name = name.lstrip("_") or f"arg{index}"
            return name[0].lower() + name[1:], value
    return f"arg{index}", "Any"


def event_params(hook, category):
    params = []
    used = {}
    for index, push in enumerate(hook["pushes"], start=1):
        name, value_type = infer_param(push, index, category)
        base = name
        if name in used:
            used[name] += 1
            name = f"{base}{used[base]}"
        else:
            used[name] = 1
        entry = {"name": name, "type": value_type}
        if value_type in OBJECT_TYPES:
            entry["nullable"] = True
        params.append(entry)
    return params


def event_returns(raw: str):
    return [
        {"name": name, "type": value_type, **({"nullable": True} if nullable else {})}
        for name, value_type, nullable in RETURN_OVERRIDES.get(raw, [])
    ]


def is_unimplemented(raw: str, event: dict, register: str) -> bool:
    return "Not Implemented" in event["comment"] or register == "RegisterVehicleEvent"


def high_frequency(raw: str) -> str:
    return "high-frequency" if "_UPDATE" in raw or "_AIUPDATE" in raw else "normal"


def make_event(raw, definition, hook, category, register, scopes, prefix, used_ids):
    event_id = f"{prefix}.{snake(raw)}"
    if event_id in used_ids:
        # Reused numeric names (WORLD_EVENT_ON_UPDATE vs SERVER_EVENT_ON_UPDATE) need a stable
        # disambiguator; the scope + numeric id remains the real identity.
        event_id = f"{prefix}.{raw.lower().replace('_event_', '_')}"
    used_ids.add(event_id)
    return {
        "id": event_id,
        "display": f"{category} · {display_name(raw)}",
        "category": category,
        "scopeParams": [{"name": name, "type": value_type} for name, value_type in scopes],
        "params": event_params(hook, category),
        "returns": event_returns(raw),
        "availability": "unimplemented" if is_unimplemented(raw, definition, register) else "implemented",
        "performance": high_frequency(raw),
    }, {
        "register": register,
        "scopeArgs": [name for name, _ in scopes],
        "eventId": definition["id"],
        "eventName": raw,
        "handlerEventParam": "event",
        "returnDefaults": [],
    }


def generate(root: Path):
    enums = parse_enums(root)
    hooks = parse_hooks(root)
    events, bindings = [], {}
    used_ids = set()

    def hook_for(raw, file_name=None, binding=None):
        candidates = [hook for hook in hooks if hook["event"] == raw]
        if file_name:
            candidates = [hook for hook in candidates if hook["file"] == file_name]
        if binding:
            candidates = [hook for hook in candidates if binding in hook["args"]]
        if not candidates:
            return {"event": raw, "pushes": []}
        return candidates[0]

    for enum_name, definitions in enums.items():
        if enum_name == "GossipEvents":
            for binding_name, category, register, scopes, prefix in GOSSIP_VARIANTS:
                for event in definitions:
                    raw = event["name"]
                    if binding_name == "PlayerGossipBindings" and raw == "GOSSIP_EVENT_ON_HELLO":
                        continue
                    hook = hook_for(raw, "GossipHooks.cpp", binding_name)
                    definition, host = make_event(raw, event, hook, category, register, scopes, prefix, used_ids)
                    events.append(definition)
                    bindings[definition["id"]] = host
            continue

        if enum_name == "InstanceEvents":
            for category, register, scopes, prefix in INSTANCE_VARIANTS:
                for event in definitions:
                    raw = event["name"]
                    hook = hook_for(raw, "InstanceHooks.cpp")
                    definition, host = make_event(raw, event, hook, category, register, scopes, prefix, used_ids)
                    events.append(definition)
                    bindings[definition["id"]] = host
            continue

        if enum_name not in ENUM_SCOPES:
            continue
        category, register, scopes, prefix = ENUM_SCOPES[enum_name]
        for event in definitions:
            raw = event["name"]
            hook = hook_for(raw)
            definition, host = make_event(raw, event, hook, category, register, scopes, prefix, used_ids)
            events.append(definition)
            bindings[definition["id"]] = host

    events.sort(key=lambda item: item["id"])
    bindings = dict(sorted(bindings.items()))
    return events, bindings


def write_or_check(path: Path, text: str, check: bool) -> bool:
    if check:
        current = path.read_text(encoding="utf-8") if path.exists() else ""
        if current != text:
            print(f"OUT OF DATE: {path}")
            return False
        print(f"ok: {path}")
        return True
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    print(f"wrote {path}")
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=os.environ.get("ACWORLD_ALE_SOURCE", ""))
    parser.add_argument("--out-dir", default="Foundation/Scripting/Catalog/Profiles")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.source:
        parser.error("--source or ACWORLD_ALE_SOURCE is required")

    root = engine_root(args.source)
    events, bindings = generate(root)
    out = Path(args.out_dir)
    ok = write_or_check(out / "core" / "events.json", json.dumps(events, indent=2, ensure_ascii=False) + "\n", args.check)
    ok = write_or_check(out / "ale-azerothcore" / "event-bindings.json", json.dumps(bindings, indent=2, ensure_ascii=False) + "\n", args.check) and ok

    implemented = sum(1 for event in events if event["availability"] == "implemented")
    with_params = sum(1 for event in events if event["params"])
    with_returns = sum(1 for event in events if event["returns"])
    print(f"events={len(events)} implemented={implemented} withParams={with_params} withReturns={with_returns} bindings={len(bindings)}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
