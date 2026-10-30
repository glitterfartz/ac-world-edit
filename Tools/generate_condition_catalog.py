"""Generate the condition type and source type arrays from a server's ConditionMgr.h plus curated labels.

Usage:
  python Tools/generate_condition_catalog.py <azerothcore>/src/server/game/Conditions/ConditionMgr.h Foundation/AzerothCore/AcConditionTypes.cs
  python Tools/generate_condition_catalog.py --trinity <trinitycore>/src/server/game/Conditions/ConditionMgr.h Foundation/TrinityCore/TcConditionTypes.cs <azerothcore header>
Reads the server headers only; never touches the database or client files. The curated labels below are
keyed by AzerothCore's ids. TrinityCore reuses one when the type has the same code name and is not listed in
TC_TYPES (the types whose parameters or meaning differ); every other type reads its parameters from the
server comment. Source key labels were checked against ConditionMgr::isSourceTypeValid of each server.
"""
import re, sys

N, B, F = 'Number', 'Bool', 'Flags'
SP, IT, QU, CR, GO = 'Spell', 'Item', 'Quest', 'Creature', 'GameObject'
AC, FA, ZO, AR, MP, EV, TI, SK, CM = 'Achievement', 'Faction', 'Zone', 'Area', 'Map', 'GameEvent', 'Title', 'Skill', 'Comparison'

# id: (name, sentence, [(label, kind)]). Sentences use {1}..{3} for ConditionValue1..3.
TYPES = {
    0: ("Always", "always", []),
    1: ("Has aura", "has aura {1} (effect {2})", [("Spell", SP), ("Effect index", N), ("Check the target instead of the player", B)]),
    2: ("Has item", "has {2} of item {1}", [("Item", IT), ("Count", N), ("Also search the bank", B)]),
    3: ("Has item equipped", "has item {1} equipped", [("Item", IT)]),
    4: ("In zone", "is in zone {1}", [("Zone", ZO)]),
    5: ("Reputation rank", "has reputation rank mask {2} with faction {1}", [("Faction", FA), ("Rank mask", F)]),
    6: ("Team", "is on team {1} (469 Alliance, 67 Horde)", [("Team", N)]),
    7: ("Skill", "has skill {1} at {2} or more", [("Skill", SK), ("Skill value", N)]),
    8: ("Quest rewarded", "has been rewarded quest {1}", [("Quest", QU)]),
    9: ("Quest taken", "has quest {1} in progress", [("Quest", QU)]),
    10: ("Drunk state", "has drunk state {1}", [("Drunken state", N)]),
    11: ("World state", "has world state {1} equal to {2}", [("World state index", N), ("Value", N)]),
    12: ("Game event active", "has game event {1} active", [("Game event", EV)]),
    13: ("Instance info", "has instance data {1}/{3} equal to {2}", [("Instance map", MP), ("Data", N), ("Type", N)]),
    14: ("Quest not saved", "does not have quest {1} saved", [("Quest", QU)]),
    15: ("Class", "is class mask {1}", [("Class mask", F)]),
    16: ("Race", "is race mask {1}", [("Race mask", F)]),
    17: ("Achievement", "has completed achievement {1}", [("Achievement", AC)]),
    18: ("Title", "has title {1}", [("Title", TI)]),
    19: ("Spawn mask", "is in spawn mask {1}", [("Spawn mask", F)]),
    20: ("Gender", "has gender {1}", [("Gender", N)]),
    21: ("Unit state", "has unit state {1}", [("Unit state", F)]),
    22: ("In map", "is in map {1}", [("Map", MP)]),
    23: ("In area", "is in area {1}", [("Area", AR)]),
    24: ("Creature type", "is of creature type {1}", [("Creature type", N)]),
    25: ("Knows spell", "knows spell {1}", [("Spell", SP)]),
    26: ("Phase mask", "is in phase mask {1}", [("Phase mask", F)]),
    27: ("Level", "has a level comparing {2} to {1}", [("Level", N), ("Comparison", CM)]),
    28: ("Quest complete", "has completed all objectives of quest {1}", [("Quest", QU)]),
    29: ("Creature nearby", "has creature {1} within {2} yd (dead: {3})", [("Creature", CR), ("Distance", N), ("Dead", B)]),
    30: ("GameObject nearby", "has game object {1} within {2} yd", [("GameObject", GO), ("Distance", N), ("Required state (0 ignore, 1 ready, 2 not ready)", N)]),
    31: ("Object entry or GUID", "is object type {1} with entry {2} or GUID {3}", [("Type id", N), ("Entry", N), ("GUID", N)]),
    32: ("Object type mask", "matches object type mask {1}", [("Type mask", F)]),
    33: ("Relation", "has relation {2} to condition target {1}", [("Condition target", N), ("Relation type", N)]),
    34: ("Reaction", "has reaction mask {2} to condition target {1}", [("Condition target", N), ("Rank mask", F)]),
    35: ("Distance", "is within {2} yd of condition target {1}", [("Condition target", N), ("Distance", N), ("Comparison", CM)]),
    36: ("Alive", "is alive", []),
    37: ("Health value", "has health comparing {2} to {1}", [("Health", N), ("Comparison", CM)]),
    38: ("Health percent", "has health percent comparing {2} to {1}", [("Percent", N), ("Comparison", CM)]),
    39: ("Realm achievement", "has realm achievement {1}", [("Achievement", AC)]),
    40: ("In water", "is in water", []),
    41: ("Terrain swap", "is in terrain swap {1}", [("Terrain swap", N)]),
    42: ("Stand state", "has stand state {2} (type {1})", [("State type", N), ("State", N)]),
    43: ("Daily quest done", "has done daily quest {1} today", [("Quest", QU)]),
    44: ("Charmed", "is charmed", []),
    45: ("Pet type", "has a pet of type mask {1}", [("Pet type mask", F)]),
    46: ("On taxi", "is on a taxi", []),
    47: ("Quest state", "has quest {1} in state mask {2}", [("Quest", QU), ("State mask (1 not taken, 2 completed, 8 in progress, 32 failed, 64 rewarded)", F)]),
    48: ("Quest objective progress", "has progress {3} on objective {2} of quest {1}", [("Quest", QU), ("Objective index", N), ("Objective count", N)]),
    49: ("Map difficulty", "is on difficulty {1}", [("Difficulty", N)]),
    101: ("Quest exclusive group satisfied", "satisfies the exclusive group of quest {1}", [("Quest", QU)]),
    102: ("Has aura type", "has aura type {1}", [("Aura type", N)]),
    103: ("World script condition", "has world script condition {1} in state {2}", [("Condition id", N), ("State", N)]),
    104: ("AI data", "has AI data {1} equal to {2}", [("Data id", N), ("Value", N)]),
    105: ("Queued for random dungeon", "is queued for a random dungeon (check difficulty {1}, difficulty {2})", [("Check difficulty", B), ("Difficulty", N)]),
    106: ("In combat", "is in combat", []),
}

# TrinityCore types whose parameters or meaning differ from AzerothCore's entry of the same id.
TC_TYPES = {
    1: ("Has aura", "has aura {1} (effect {2})", [("Spell", SP), ("Effect index", N)]),
    19: ("Spawn mask (deprecated)", "is in spawn mask {1}", [("Spawn mask", F)]),
    26: ("Phase", "is in phase {1}", [("Phase", N)]),
    30: ("GameObject nearby", "has game object {1} within {2} yd", [("GameObject", GO), ("Distance", N)]),
    31: ("Object entry or GUID (legacy)", "is object type {1} with entry {2} or GUID {3}", [("Type id", N), ("Entry", N), ("GUID", N)]),
    32: ("Object type mask (legacy)", "matches object type mask {1}", [("Type mask", F)]),
    41: ("Terrain swap", "is in terrain swap {1}", [("Terrain swap", N)]),
    48: ("Quest objective progress", "has progress {3} on quest objective {1}", [("Quest objective", N), None, ("Progress", N)]),
    50: ("Game master", "is a game master (or can be: {1})", [("Can be GM", B)]),
    51: ("Object entry or GUID", "is object type {1} with entry {2} or GUID {3}", [("Type id", N), ("Entry", N), ("GUID", N)]),
    52: ("Object type mask", "matches object type mask {1}", [("Type mask", F)]),
    53: ("Battle pet count", "has {2} of battle pet species {1} (comparison {3})", [("Species", N), ("Count", N), ("Comparison", CM)]),
    54: ("Scenario step", "is at scenario step {1}", [("Scenario step", N)]),
    55: ("Scene in progress", "is playing a scene with script package {1}", [("Script package", N)]),
    56: ("Player condition", "satisfies player condition {1}", [("Player condition", N)]),
    57: ("Private object", "is a private object", []),
    58: ("String id", "has a string id (ConditionStringValue1)", []),
    59: ("Label", "has label {1}", [("Label", N)]),
    60: ("Group status", "has group status {1} (0 none, 1 group, 2 group not raid, 3 raid, 4 not group or not raid)", [("Group status", N)]),
}

# Source key labels: id: (group, entry, id). Empty means the column is not used.
LOOT = ("Loot table entry", "Item", "")
SOURCES = {
    0: ("", "", ""),
    1: LOOT, 2: LOOT, 3: LOOT, 4: LOOT, 5: LOOT, 6: LOOT, 7: LOOT, 8: LOOT, 9: LOOT, 10: ("Reference loot entry", "Item", ""), 11: LOOT, 12: LOOT,
    13: ("Spell effect mask", "Spell", ""),
    14: ("Gossip menu", "Text id", ""),
    15: ("Gossip menu", "Option id", ""),
    16: ("", "Creature", ""),
    17: ("", "Spell", ""),
    18: ("Clicked creature", "Spell", ""),
    19: ("", "Quest", ""),
    21: ("Creature", "Spell", ""),
    22: ("Script owner entry (+1 for a creature, or GUID)", "smart_scripts id", "Script source type"),
    23: ("Creature", "Item", ""),
    24: ("", "Spell", ""),
    25: ("", "Map (terrain swap)", ""),
    26: ("Phase", "Area", ""),
    27: ("Zone", "Graveyard", ""),
}
AC_SOURCES = {
    28: ("", "", ""),
    29: ("Creature", "", ""),
    30: ("Object type", "Entry", ""),
}
TC_SOURCES = {
    28: ("Area trigger", "Custom (0 no, 1 yes)", ""),
    29: ("", "Conversation line", ""),
    30: ("", "Area trigger (client)", ""),
    31: ("Trainer", "Spell", ""),
    32: ("Object type", "Entry", ""),
    33: ("", "Spawn group", ""),
    34: ("", "Player condition", ""),
    35: ("Skill line", "Skill line ability", ""),
    36: ("Player choice", "Response", ""),
}
AC_NOT_FOR_CLIENT_TYPES = {41}
AC_NOT_FOR_CLIENT_SOURCES = {25, 26, 27}
TC_DEPRECATED_TYPES = {19, 31, 32}


def parse(path):
    h = open(path, encoding='utf-8', errors='replace').read()
    types, sources = [], []
    for enum, prefix, out in [('ConditionTypes', 'CONDITION_', types), ('ConditionSourceType', 'CONDITION_SOURCE_TYPE_', sources)]:
        body = re.search(r'enum ' + enum + r'\s*\{(.*?)\};', h, re.S).group(1)
        for line in body.splitlines():
            m = re.match(r'\s*(' + prefix + r'\w+)\s*=\s*(\d+)\s*,?\s*(?://\s*(.*))?$', line)
            if m and not m.group(1).endswith(('_END', '_START', '_MAX')):
                out.append((m.group(1), int(m.group(2)), (m.group(3) or '').strip()))
    # The type enum's prefix also matches the source enum's names; keep each list to its own enum.
    types = [t for t in types if not t[0].startswith('CONDITION_SOURCE_TYPE_')]
    return types, sources


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def friendly(code, prefix):
    words = code[len(prefix):].replace('_', ' ').lower()
    return words[:1].upper() + words[1:]


def param_list(params):
    # A None entry is a skipped middle value: an empty label marks the slot as unused.
    return ', '.join('new("", ConditionParamKind.Number)' if p is None else f'new({cs(p[0])}, ConditionParamKind.{p[1]})' for p in params)


def emit_types(rows, curated, status_of):
    lines = ['    public static readonly ConditionType[] Types =', '    [']
    for code, ident, comment in sorted(rows, key=lambda r: r[1]):
        if ident in curated:
            name, sentence, params = curated[ident]
        else:
            name, sentence, params = friendly(code, 'CONDITION_'), '', []
        lines.append(f'        new({ident}, {cs(code)}, {cs(name)}, {cs(sentence)}, {cs(comment)}, [{param_list(params)}], ConditionStatus.{status_of(ident)}),')
    lines.append('    ];')
    return '\n'.join(lines)


def emit_sources(rows, labels, status_of):
    lines = ['    public static readonly ConditionSource[] Sources =', '    [']
    for code, ident, _ in sorted(rows, key=lambda r: r[1]):
        group, entry, sid = labels.get(ident, ("", "", ""))
        lines.append(f'        new({ident}, {cs(code)}, {cs(friendly(code, "CONDITION_SOURCE_TYPE_"))}, {cs(group)}, {cs(entry)}, {cs(sid)}, ConditionStatus.{status_of(ident)}),')
    lines.append('    ];')
    return '\n'.join(lines)


args = [a for a in sys.argv[1:] if a != '--trinity']
trinity = '--trinity' in sys.argv
types, sources = parse(args[0])
out_path = args[1]

if trinity:
    ac_types, _ = parse(args[2])
    ac_codes = {ident: code for code, ident, _ in ac_types}
    curated = {}
    for code, ident, _ in types:
        if ident in TC_TYPES:
            curated[ident] = TC_TYPES[ident]
        elif ac_codes.get(ident) == code and ident in TYPES:
            curated[ident] = TYPES[ident]
    class_name, dialect, labels = 'TcConditionTypes', 'TrinityCore master', {**SOURCES, **TC_SOURCES}
    type_status = lambda i: 'Deprecated' if i in TC_DEPRECATED_TYPES else 'Usable'
    source_status = lambda i: 'Usable'
else:
    curated = {i: TYPES[i] for _, i, _ in types if i in TYPES}
    class_name, dialect, labels = 'AcConditionTypes', 'AzerothCore', {**SOURCES, **AC_SOURCES}
    type_status = lambda i: 'NotForClient' if i in AC_NOT_FOR_CLIENT_TYPES else 'Usable'
    source_status = lambda i: 'NotForClient' if i in AC_NOT_FOR_CLIENT_SOURCES else 'Usable'

text = f'''namespace ACWorldEdit.Foundation;

/// <summary>
/// Condition types and source types generated from {dialect} ConditionMgr.h (ConditionTypes and
/// ConditionSourceType), with curated names, sentences and parameter kinds. Regenerate with
/// Tools/generate_condition_catalog.py when the server adds types; a type without a curated entry
/// has no sentence and no parameter kinds, so the editor shows its raw values with the server comment.
/// </summary>
public static class {class_name}
{{
{emit_types(types, curated, type_status)}

{emit_sources(sources, labels, source_status)}
}}
'''
open(out_path, 'w', encoding='utf-8', newline='\n').write(text)
print(f'{out_path}: {len(types)} types, {len(sources)} sources')
