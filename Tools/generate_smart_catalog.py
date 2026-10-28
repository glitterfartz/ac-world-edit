"""Generate the SmartAI type arrays from a server's SmartScriptMgr.h enum comments plus curated labels.

Usage:
  python Tools/generate_smart_catalog.py <azerothcore>/src/server/game/AI/SmartScripts/SmartScriptMgr.h Foundation/AzerothCore/AcSmartTypes.cs
  python Tools/generate_smart_catalog.py --trinity <trinitycore>/src/server/game/AI/SmartScripts/SmartScriptMgr.h Foundation/TrinityCore/TcSmartTypes.cs <azerothcore header>
Reads the server headers only; never touches the database or client files. The curated labels below
are keyed by AzerothCore's IDs. TrinityCore reuses one only when the type has the same code name and
the same parameter comment in both headers; the rest read their parameters from the comment.
"""
import re, sys


def parse_enums(header_path):
    h = open(header_path, encoding='utf-8', errors='replace').read()
    result = {}
    for enum, prefix in [('SMART_EVENT', 'SMART_EVENT_'), ('SMART_ACTION', 'SMART_ACTION_'), ('SMARTAI_TARGETS', 'SMART_TARGET_')]:
        body = re.search(r'enum ' + enum + r'\s*\{(.*?)\};', h, re.S).group(1)
        items = []
        for line in body.splitlines():
            m = re.match(r'\s*(' + prefix + r'\w+)\s*=\s*(\d+)\s*,?\s*(?://\s*(.*))?$', line)
            if m:
                items.append((m.group(1), int(m.group(2)), (m.group(3) or '').strip()))
        result[enum] = items
    return result


args = [a for a in sys.argv[1:] if a != '--trinity']
trinity = '--trinity' in sys.argv
enums = parse_enums(args[0])
out_path = args[1]

# Curated (id: (name, sentence, [(label, kind)])). Sentences use {1}..{6} for params; kinds drive
# pickers and formatting. Labels follow the server comments in SmartScriptMgr.h.
T = 'Ms'
EVENTS = {
    0: ("In combat (timer)", "in combat, every {3}–{4} (first after {1}–{2})", [("First after (min)", T), ("First after (max)", T), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    1: ("Out of combat (timer)", "out of combat, every {3}–{4} (first after {1}–{2})", [("First after (min)", T), ("First after (max)", T), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    2: ("Health between", "when its health is {1}–{2}%", [("Health min %", "Percent"), ("Health max %", "Percent"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    3: ("Mana between", "when its mana is {1}–{2}%", [("Mana min %", "Percent"), ("Mana max %", "Percent"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    4: ("Enters combat", "when it enters combat", []),
    5: ("Kills a unit", "when it kills a unit", [("Cooldown (min)", T), ("Cooldown (max)", T), ("Players only", "Bool"), ("Only this creature", "Creature")]),
    6: ("Dies", "when it dies", []),
    7: ("Evades", "when it evades (leaves combat)", []),
    8: ("Hit by spell", "when hit by {1}", [("Spell", "Spell"), ("School mask", "Number"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    9: ("Target in range", "when its target is {1}–{2} yd away", [("Min distance", "Yards"), ("Max distance", "Yards"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    10: ("Unit comes near (out of combat)", "when a unit comes within {2} yd (out of combat)", [("Hostility (0 hostile, 1 not hostile, 2 any)", "Number"), ("Max distance", "Yards"), ("Cooldown (min)", T), ("Cooldown (max)", T), ("Players only", "Bool")]),
    11: ("Respawns", "when it respawns", [("Type (0 any, 1 map, 2 area)", "Number"), ("Map", "Number"), ("Zone", "Number")]),
    14: ("Friend is hurt", "when a friend within {2} yd is missing {1} health", [("Health missing", "Number"), ("Radius", "Yards"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    17: ("Summons a unit", "when it summons {1}", [("Creature (0 any)", "Creature"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    19: ("Quest accepted", "when a player accepts {1}", [("Quest (0 any)", "Quest"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    20: ("Quest completed", "when a player completes {1}", [("Quest (0 any)", "Quest"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    21: ("Reaches home", "when it reaches its home position", []),
    22: ("Receives emote", "when a player does /{1} to it", [("Text emote", "TextEmote"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    23: ("Has aura", "while it has {1}", [("Spell", "Spell"), ("Stacks", "Number"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    25: ("Reset", "when it resets (spawn, respawn or after combat)", []),
    26: ("Unit comes near (in combat)", "when a unit comes within {2} yd (in combat)", [("Hostility (0 hostile, 1 not hostile, 2 any)", "Number"), ("Max distance", "Yards"), ("Cooldown (min)", T), ("Cooldown (max)", T), ("Players only", "Bool")]),
    34: ("Reaches movement point", "when it reaches movement point {2}", [("Movement type (0 any)", "Number"), ("Point", "Number"), ("Path (0 any)", "Number")]),
    38: ("Data set", "when data {1} is set to {2}", [("Field", "Number"), ("Value", "Number"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    40: ("Reaches waypoint", "when it reaches waypoint {1}", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    52: ("Text finished", "after text group {1} has been said", [("Text group", "TextGroup"), ("Speaker (0 any)", "Creature")]),
    54: ("Just summoned", "when it is summoned", []),
    57: ("Path paused", "when its path is paused", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    58: ("Path ended", "when its path ends", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    59: ("Timed event fires", "when timed event {1} fires", [("Timed event", "Number")]),
    46: ("On trigger (AreaTrigger)", "when a player enters the trigger", [("Only trigger (0 = any)", "Number")]),
    60: ("Timer (always)", "every {3}–{4} (first after {1}–{2})", [("First after (min)", T), ("First after (max)", T), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    61: ("Then (linked)", "then", []),
    62: ("Gossip option chosen", "when a player chooses gossip menu {1} option {2}", [("Gossip menu", "Number"), ("Option", "Number")]),
    63: ("Created", "when it is created", []),
    64: ("Gossip opened", "when a player opens its gossip", [("Filter (0 always, 1 gossip only, 2 use only)", "Number")]),
}
ACTIONS = {
    0: ("Nothing", "do nothing", []),
    1: ("Say text", "say text group {1}", [("Text group", "TextGroup"), ("Wait before 'text finished' (ms)", T), ("Use first target as invoker", "Bool")]),
    2: ("Change faction", "change faction to {1}", [("Faction template (0 default)", "FactionTemplate")]),
    5: ("Play emote", "play emote {1}", [("Emote", "Emote")]),
    8: ("Set react state", "set react state to {1} (0 passive, 1 defensive, 2 aggressive)", [("State", "Number")]),
    10: ("Play random emote", "play a random emote of {1}, {2}, {3}", [("Emote 1", "Emote"), ("Emote 2", "Emote"), ("Emote 3", "Emote"), ("Emote 4", "Emote"), ("Emote 5", "Emote"), ("Emote 6", "Emote")]),
    20: ("Auto attack", "auto attack: {1}", [("Attack", "Bool")]),
    30: ("Random phase", "switch to a random phase of {1}, {2}, {3}", [("Phase 1", "Number"), ("Phase 2", "Number"), ("Phase 3", "Number"), ("Phase 4", "Number"), ("Phase 5", "Number"), ("Phase 6", "Number")]),
    34: ("Set instance data", "set instance data {1} to {2}", [("Field", "Number"), ("Value", "Number")]),
    38: ("Engage whole zone", "put everyone in the zone in combat", [("Range outside dungeons", "Yards")]),
    40: ("Sheathe weapons", "set weapons to {1} (0 sheathed, 1 melee, 2 ranged)", [("Sheath", "Number")]),
    51: ("Kill target", "kill the target", []),
    59: ("Run or walk", "run: {1}", [("Run", "Bool")]),
    84: ("Simple talk", "say text group {1} (no 'text finished' event)", [("Text group", "TextGroup")]),
    87: ("Run random action list", "run a random action list of {1}, {2}, {3}", [("Action list 1", "ActionList"), ("Action list 2", "ActionList"), ("Action list 3", "ActionList"), ("Action list 4", "ActionList"), ("Action list 5", "ActionList"), ("Action list 6", "ActionList")]),
    90: ("Set stand state", "set stand state {1}", [("Bytes 1 value", "Number"), ("Field", "Number")]),
    91: ("Clear stand state", "clear stand state {1}", [("Bytes 1 value", "Number"), ("Field", "Number")]),
    107: ("Summon creature group", "summon creature group {1}", [("Group", "Number"), ("Attack invoker", "Bool"), ("Attack owner's target", "Bool")]),
    134: ("Make invoker cast", "make the invoker cast {1}", [("Spell", "Spell"), ("Cast flags", "Flags"), ("Trigger flags", "Flags"), ("Max targets", "Number")]),
    11: ("Cast spell", "cast {1}", [("Spell", "Spell"), ("Cast flags", "Flags"), ("Trigger flags", "Flags"), ("Max targets", "Number")]),
    12: ("Summon creature", "summon {1}", [("Creature", "Creature"), ("Summon type", "Number"), ("Duration", T), ("Attack invoker", "Bool"), ("Attack owner's target", "Bool"), ("Flags", "Flags")]),
    15: ("Complete quest (explore/event)", "complete {1} for the target", [("Quest", "Quest")]),
    17: ("Loop emote", "loop emote {1}", [("Emote", "Emote")]),
    18: ("Add unit flags", "add unit flags {1}", [("Unit flags", "Flags"), ("Flag field", "Number")]),
    19: ("Remove unit flags", "remove unit flags {1}", [("Unit flags", "Flags"), ("Flag field", "Number")]),
    21: ("Allow combat movement", "allow combat movement: {1}", [("Allowed", "Bool")]),
    22: ("Set phase", "switch to phase {1}", [("Phase", "Number")]),
    23: ("Change phase by", "change phase by {1}", [("Change", "Number")]),
    24: ("Evade", "evade (reset)", []),
    25: ("Flee for help", "flee for help", [("With emote", "Bool")]),
    26: ("Complete group quest event", "complete {1} for the group", [("Quest", "Quest")]),
    28: ("Remove aura", "remove {1}", [("Spell (0 all)", "Spell"), ("Charges", "Number")]),
    29: ("Follow", "follow the target", [("Distance", "Yards"), ("Angle", "Number"), ("End at creature", "Creature"), ("Credit", "Number"), ("Credit type", "Number")]),
    33: ("Give kill credit", "give kill credit for {1}", [("Creature", "Creature")]),
    37: ("Die", "die", [("After (ms)", T)]),
    39: ("Call for help", "call for help within {1} yd", [("Radius", "Yards"), ("With emote", "Bool")]),
    41: ("Despawn", "despawn after {1}", [("After", T), ("Respawn after (s)", "Number")]),
    45: ("Set data", "set data {1} to {2} on the target", [("Field", "Number"), ("Value", "Number")]),
    47: ("Set visibility", "set visible: {1}", [("Visible", "Bool")]),
    48: ("Set active", "set active (keeps grid loaded): {1}", [("Active", "Bool")]),
    49: ("Attack", "attack the target", []),
    50: ("Summon object", "summon object {1} for {2} s", [("GameObject", "GameObject"), ("Despawn after (s)", "Number"), ("At target", "Bool"), ("Summon type", "Number")]),
    53: ("Start path", "start path {2}", [("Run", "Bool"), ("Path", "Number"), ("Repeat", "Bool"), ("Quest", "Quest"), ("Despawn after", T), ("React state", "Number")]),
    54: ("Pause path", "pause its path for {1}", [("Pause", T)]),
    56: ("Give item", "give {2}× {1}", [("Item", "Item"), ("Count", "Number")]),
    62: ("Teleport", "teleport to map {1}", [("Map", "Number")]),
    64: ("Remember targets", "remember the target as {1}", [("Store id", "Number")]),
    66: ("Turn", "turn to face the target", [("Quick change", "Bool"), ("Random", "Bool"), ("Turn angle", "Number")]),
    67: ("Schedule timed event", "schedule timed event {1} in {2}–{3}", [("Timed event", "Number"), ("First after (min)", T), ("First after (max)", T), ("Repeat (min)", T), ("Repeat (max)", T), ("Chance %", "Percent")]),
    69: ("Move to position", "move to point {1}", [("Point id", "Number"), ("Transport", "Bool"), ("Controlled", "Bool"), ("Contact distance", "Yards")]),
    72: ("Close gossip", "close the gossip window", []),
    73: ("Trigger timed event", "trigger timed event {1}", [("Timed event", "Number")]),
    75: ("Add aura", "apply {1}", [("Spell", "Spell")]),
    80: ("Run action list", "run action list {1}", [("Action list", "ActionList"), ("Stop after combat", "Bool"), ("Timer (0 OOC, 1 IC, 2 always)", "Number")]),
    81: ("Set NPC flags", "set NPC flags to {1}", [("NPC flags", "Flags")]),
    82: ("Add NPC flags", "add NPC flags {1}", [("NPC flags", "Flags")]),
    83: ("Remove NPC flags", "remove NPC flags {1}", [("NPC flags", "Flags")]),
    85: ("Cast on self", "cast {1} on itself", [("Spell", "Spell"), ("Cast flags", "Flags"), ("Trigger flags", "Flags"), ("Max targets", "Number")]),
    86: ("Make target cast", "make another unit cast {1}", [("Spell", "Spell"), ("Cast flags", "Flags"), ("Caster target type", "Number"), ("Caster param 1", "Number"), ("Caster param 2", "Number"), ("Caster param 3", "Number")]),
    98: ("Show gossip menu", "show gossip menu {1}", [("Gossip menu", "Number"), ("npc_text", "Number")]),
    101: ("Set home position", "set its home position", [("Use spawn position", "Bool")]),
    103: ("Root", "root: {1}", [("Rooted", "Bool")]),
}
TARGETS = {
    0: ("None", "", []),
    1: ("Itself", "itself", []),
    2: ("Its victim", "its victim", []),
    5: ("Random enemy", "a random enemy", [("Max distance", "Yards"), ("Players only", "Bool")]),
    6: ("Random enemy (not tank)", "a random enemy other than the tank", [("Max distance", "Yards"), ("Players only", "Bool")]),
    7: ("Invoker", "the unit that triggered this", []),
    8: ("Position", "position ({x}, {y}, {z})", []),
    9: ("Creatures in range", "{1} within {2}–{3} yd", [("Creature (0 any)", "Creature"), ("Min distance", "Yards"), ("Max distance", "Yards"), ("Alive (0 both, 1 alive, 2 dead)", "Number")]),
    10: ("Creature by GUID", "creature #{1}", [("Spawn GUID", "Number"), ("Creature", "Creature")]),
    11: ("Creatures within", "{1} within {2} yd", [("Creature (0 any)", "Creature"), ("Max distance", "Yards"), ("Alive (0 both, 1 alive, 2 dead)", "Number")]),
    12: ("Remembered targets", "remembered targets {1}", [("Store id", "Number")]),
    13: ("Objects in range", "{1} within {2}–{3} yd", [("GameObject (0 any)", "GameObject"), ("Min distance", "Yards"), ("Max distance", "Yards")]),
    14: ("Object by GUID", "object #{1}", [("Spawn GUID", "Number"), ("GameObject", "GameObject")]),
    15: ("Objects within", "{1} within {2} yd", [("GameObject (0 any)", "GameObject"), ("Max distance", "Yards")]),
    17: ("Players in range", "players within {1}–{2} yd", [("Min distance", "Yards"), ("Max distance", "Yards"), ("Max count", "Number")]),
    18: ("Players within", "players within {1} yd", [("Max distance", "Yards")]),
    19: ("Closest creature", "the closest {1} within {2} yd", [("Creature (0 any)", "Creature"), ("Max distance", "Yards"), ("Dead", "Bool")]),
    20: ("Closest object", "the closest {1} within {2} yd", [("GameObject (0 any)", "GameObject"), ("Max distance", "Yards")]),
    21: ("Closest player", "the closest player within {1} yd", [("Max distance", "Yards")]),
    23: ("Owner or summoner", "its owner or summoner", [("Use charmer", "Bool")]),
    24: ("Threat list", "everyone on its threat list", [("Max distance", "Yards"), ("Players only", "Bool")]),
    25: ("Closest enemy", "the closest enemy within {1} yd", [("Max distance", "Yards"), ("Players only", "Bool")]),
    26: ("Closest friend", "the closest friend within {1} yd", [("Max distance", "Yards"), ("Players only", "Bool")]),
}


# TrinityCore types whose meaning or parameters differ from the AzerothCore entry of the same code.
TC_EVENTS = {
    9: ("Target in range", "when its target is {1}–{2} yd away", [("Min distance", "Yards"), ("Max distance", "Yards"), ("Repeat every (min)", T), ("Repeat every (max)", T)]),
    10: ("Unit comes near (out of combat)", "when a unit comes within {2} yd (out of combat)", [("Hostility (0 hostile, 1 not hostile, 2 any)", "Number"), ("Max distance", "Yards"), ("Cooldown (min)", T), ("Cooldown (max)", T), ("Players only", "Bool")]),
    26: ("Unit comes near (in combat)", "when a unit comes within {2} yd (in combat)", [("Hostility (0 hostile, 1 not hostile, 2 any)", "Number"), ("Max distance", "Yards"), ("Cooldown (min)", T), ("Cooldown (max)", T), ("Players only", "Bool")]),
    13: ("Victim casting", "when its victim casts {3}", [("Repeat every (min)", T), ("Repeat every (max)", T), ("Spell", "Spell")]),
    32: ("Damaged", "when it takes {1}–{2} damage", [("Min damage", "Number"), ("Max damage", "Number"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    34: ("Reaches movement point", "when it reaches movement point {2}", [("Movement type (0 any)", "Number"), ("Point", "Number")]),
    40: ("Reaches waypoint", "when it reaches waypoint {1}", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    55: ("Path paused", "when its path is paused", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    56: ("Path resumed", "when its path resumes", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    57: ("Path stopped", "when its path stops", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    58: ("Path ended", "when its path ends", [("Waypoint (0 any)", "Number"), ("Path (0 any)", "Number")]),
    46: ("Area trigger entered", "when a player enters the area trigger", []),
    88: ("Area trigger left", "when a player leaves the area trigger", []),
    64: ("Gossip opened", "when a player opens its gossip", [("Skip when only used (objects)", "Bool")]),
    73: ("Spellclick", "when a player clicks it (spellclick)", []),
    83: ("Casts a spell", "when it casts {1}", [("Spell (0 any)", "Spell"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    84: ("Spell cast fails", "when its cast of {1} fails", [("Spell (0 any)", "Spell"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    85: ("Starts a spell cast", "when it starts casting {1}", [("Spell (0 any)", "Spell"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    86: ("Despawns", "when it despawns", []),
    89: ("Aura applied", "when {1} is applied to it", [("Spell (0 any)", "Spell"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    90: ("Aura removed", "when {1} is removed from it", [("Spell (0 any)", "Spell"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    82: ("Summoned unit dies", "when a summoned {1} dies", [("Creature (0 any)", "Creature"), ("Cooldown (min)", T), ("Cooldown (max)", T)]),
    36: ("Corpse removed", "when its corpse is removed", []),
    37: ("AI initialised", "when its AI initialises", []),
}
TC_ACTIONS = {
    1: ("Say text", "say text group {1}", [("Text group", "TextGroup"), ("Wait before 'text finished' (ms)", T), ("Speak to the target", "Bool")]),
    4: ("Play sound", "play sound {1}", [("Sound", "Number"), ("Only the target hears it", "Bool")]),
    6: ("Fail quest", "fail {1}", [("Quest", "Quest")]),
    7: ("Offer quest", "offer {1}", [("Quest", "Quest"), ("Add directly", "Bool")]),
    11: ("Cast spell", "cast {1}", [("Spell", "Spell"), ("Cast flags", "Flags"), ("Triggered flags", "Flags")]),
    12: ("Summon creature", "summon {1}", [("Creature", "Creature"), ("Summon type", "Number"), ("Duration", T), ("Stored target id", "Number"), ("Flags", "Flags"), ("Count", "Number"), ("Created by spell", "Spell")]),
    24: ("Evade", "evade (reset)", [("Last stored home position (0 respawn point)", "Bool")]),
    27: ("Stop combat", "stop combat", []),
    34: ("Set instance data", "set instance data {1} to {2}", [("Field", "Number"), ("Value", "Number"), ("Type (0 data, 1 boss state)", "Number")]),
    37: ("Die", "die", []),
    38: ("Engage whole zone", "put everyone in the zone in combat", []),
    50: ("Summon object", "summon {1} for {2} s", [("GameObject", "GameObject"), ("Despawn after (s)", "Number"), ("Summon type", "Number"), ("Stored target id", "Number")]),
    69: ("Move to position", "move to point {1}", [("Point id", "Number"), ("Transport", "Bool"), ("Disable pathfinding", "Bool"), ("Contact distance", "Yards")]),
    85: ("Cast on self", "cast {1} on itself", [("Spell", "Spell"), ("Cast flags", "Flags")]),
    134: ("Make invoker cast", "make the invoker cast {1}", [("Spell", "Spell"), ("Cast flags", "Flags")]),
    111: ("Stop game event", "stop game event {1}", [("Game event", "Number")]),
    112: ("Start game event", "start game event {1}", [("Game event", "Number")]),
    142: ("Set health", "set its health to {1}", [("Percent", "Percent")]),
}
TC_TARGETS = {
    9: ("Creatures in range", "{1} within {2}–{3} yd", [("Creature (0 any)", "Creature"), ("Min distance", "Yards"), ("Max distance", "Yards"), ("Max count", "Number")]),
    11: ("Creatures within", "{1} within {2} yd", [("Creature (0 any)", "Creature"), ("Max distance", "Yards"), ("Max count", "Number")]),
    13: ("Objects in range", "{1} within {2}–{3} yd", [("GameObject (0 any)", "GameObject"), ("Min distance", "Yards"), ("Max distance", "Yards")]),
    15: ("Objects within", "{1} within {2} yd", [("GameObject (0 any)", "GameObject"), ("Max distance", "Yards")]),
    19: ("Closest creature", "the closest {1} within {2} yd", [("Creature (0 any)", "Creature"), ("Max distance", "Yards"), ("Alive (0 alive, 1 dead, 2 both)", "Number")]),
    20: ("Closest object", "the closest {1} within {2} yd", [("GameObject (0 any)", "GameObject"), ("Max distance", "Yards")]),
    28: ("Farthest target", "the farthest target within {1} yd", [("Max distance", "Yards"), ("Players only", "Bool"), ("In line of sight", "Bool")]),
}


def friendly(code, prefix):
    words = code[len(prefix):].replace('_', ' ').lower()
    return words[:1].upper() + words[1:]


def infer(label):
    l = label.lower()
    if 'emote' in l: return 'Emote'
    if 'faction' in l: return 'FactionTemplate'
    if 'spell' in l: return 'Spell'
    if 'quest' in l: return 'Quest'
    if 'item' in l: return 'Item'
    if 'gameobject' in l or l.startswith('go ') or 'go entry' in l: return 'GameObject'
    if 'creature' in l or 'entry' in l: return 'Creature'
    if '%' in l or 'pct' in l or 'percent' in l: return 'Percent'
    if any(w in l for w in ('cooldown', 'repeat', 'initial', 'timer', 'duration', 'delay', 'ms')): return 'Ms'
    if any(w in l for w in ('dist', 'radius', 'range')): return 'Yards'
    if '0/1' in l or 'on/off' in l or 'off/on' in l: return 'Bool'
    if 'flag' in l: return 'Flags'
    return 'Number'


def clean(label):
    # "0/1" or "on/off" alone says nothing; call it "Enabled".
    if label.strip().lower() in ('0/1', 'on/off', 'off/on'):
        return 'Enabled'
    return label[:1].upper() + label[1:]


def parse_params(comment, max_params):
    c = comment.strip()
    if not c or c.lower().rstrip('.') in ('none', 'no params', 'no action', 'internal usage'):
        return [], ''
    parts = [p.strip() for p in c.split(',') if p.strip()]
    if 1 <= len(parts) <= max_params and all(len(p) <= 48 for p in parts):
        return [(clean(p), infer(p)) for p in parts], ''
    return None, c  # free text: params unknown, show the comment as help


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def emit(name, items, prefix, curated, max_params):
    lines = [f'    public static readonly SmartType[] {name} =', '    [']
    for code, ident, comment in sorted(items, key=lambda i: i[1]):
        if ident in curated:
            title, sentence, params = curated[ident]
            known = True
            help_text = comment
        else:
            title, sentence = friendly(code, prefix), ''
            params, free = parse_params(comment, max_params)
            known = params is not None
            params = params or []
            help_text = comment
        ps = ', '.join(f'new({cs(l)}, SmartParamKind.{k})' for l, k in params)
        lines.append(f'        new({ident}, {cs(code)}, {cs(title)}, {cs(sentence)}, {cs(help_text)}, [{ps}], {str(known).lower()}),')
    lines.append('    ];')
    return '\n'.join(lines)


def reuse_for_trinity(curated_by_ac_id, kind):
    """AzerothCore's curated entries that still describe the same type in TrinityCore."""
    ac = {code: (ident, comment) for code, ident, comment in parse_enums(args[2])[kind]}
    reused = {}
    for code, ident, comment in enums[kind]:
        found = ac.get(code)
        if found and found[0] in curated_by_ac_id and norm(found[1]) == norm(comment):
            reused[ident] = curated_by_ac_id[found[0]]
    return reused


def norm(comment):
    return re.sub(r'[^a-z0-9]', '', comment.lower())


def generate(class_name, dialect_name):
    return f'''namespace ACWorldEdit.Foundation;

/// <summary>
/// SmartAI type arrays generated from {dialect_name} SmartScriptMgr.h enum comments (SMART_EVENT,
/// SMART_ACTION, SMARTAI_TARGETS), with curated names, sentences and parameter kinds for the types
/// most used in the world database. Regenerate with Tools/generate_smart_catalog.py when the server
/// adds types; types without a curated entry show their parameters from the server comment.
/// </summary>
public static class {class_name}
{{
'''


if trinity:
    event_curated = reuse_for_trinity(EVENTS, 'SMART_EVENT')
    action_curated = reuse_for_trinity(ACTIONS, 'SMART_ACTION')
    target_curated = reuse_for_trinity(TARGETS, 'SMARTAI_TARGETS')
    event_curated.update(TC_EVENTS)
    action_curated.update(TC_ACTIONS)
    target_curated.update(TC_TARGETS)
    head = generate('TcSmartTypes', 'TrinityCore master')
    max_event, max_action = 5, 7
else:
    event_curated, action_curated, target_curated = EVENTS, ACTIONS, TARGETS
    head = generate('AcSmartTypes', 'AzerothCore')
    max_event, max_action = 6, 6

body = '\n\n'.join([
    emit('Events', enums['SMART_EVENT'], 'SMART_EVENT_', event_curated, max_event),
    emit('Actions', enums['SMART_ACTION'], 'SMART_ACTION_', action_curated, max_action),
    emit('Targets', enums['SMARTAI_TARGETS'], 'SMART_TARGET_', target_curated, 4),
])
open(out_path, 'w', encoding='utf-8', newline='\n').write(head + body + '\n}\n')
print('events', len(enums['SMART_EVENT']), 'actions', len(enums['SMART_ACTION']), 'targets', len(enums['SMARTAI_TARGETS']))
