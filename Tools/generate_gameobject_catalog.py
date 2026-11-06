"""Generate the GameObject type and Data field catalogues from a server's headers.

Usage:
  python Tools/generate_gameobject_catalog.py <GameObjectData.h> <SharedDefines.h> Foundation/AzerothCore/AcGameObjectTypes.cs
  python Tools/generate_gameobject_catalog.py --trinity <trinity GameObjectData.h> <trinity SharedDefines.h> Foundation/TrinityCore/TcGameObjectTypes.cs <azerothcore GameObjectData.h> <azerothcore SharedDefines.h>

Reads the server headers only; never touches the database or client files. The
GameObjectData.h union gives each type's fields and order. TrinityCore annotates
every field with a kind, range, default and reference, so those are parsed. The
AzerothCore header only has identifiers and comments, so a curated overlay below
fills in the important kinds, references, labels and defaults; everything else
falls back to a friendly identifier and the server comment.
"""
import re
import sys


# Fields whose WotLK meaning is not obvious from the identifier alone. The key is
# (type code, field code); values override the inferred field.
# Kinds: b = bool, n = number, m = milliseconds, r = reference, c = choice.
AC_OVERLAY = {
    ("GAMEOBJECT_TYPE_DOOR", "startOpen"): dict(label="Starts open", kind="b", tip="The door is open when it spawns."),
    ("GAMEOBJECT_TYPE_DOOR", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to open it. 0 for none."),
    ("GAMEOBJECT_TYPE_DOOR", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_DOOR", "noDamageImmune"): dict(label="Damage interrupts opening", kind="b", tip="Taking damage cancels the opening."),
    ("GAMEOBJECT_TYPE_DOOR", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it opens. 0 for none."),
    ("GAMEOBJECT_TYPE_DOOR", "closeTextID"): dict(label="Close text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it closes. 0 for none."),
    ("GAMEOBJECT_TYPE_DOOR", "ignoredByPathing"): dict(label="Ignored by pathing", kind="b", tip="Creatures ignore it when pathfinding."),
    ("GAMEOBJECT_TYPE_BUTTON", "startOpen"): dict(label="Starts open", kind="b", tip="The button starts in the open/active state."),
    ("GAMEOBJECT_TYPE_BUTTON", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_BUTTON", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_BUTTON", "linkedTrap"): dict(label="Linked trap", kind="r", reference="GameObject", tip="GameObject entry of the trap it triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_BUTTON", "noDamageImmune"): dict(label="Damage interrupts opening", kind="b", tip="Taking damage cancels the opening."),
    ("GAMEOBJECT_TYPE_BUTTON", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_BUTTON", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it opens. 0 for none."),
    ("GAMEOBJECT_TYPE_BUTTON", "closeTextID"): dict(label="Close text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it closes. 0 for none."),
    ("GAMEOBJECT_TYPE_BUTTON", "losOK"): dict(label="Requires line of sight", kind="b", tip="Must be in line of sight to use."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "questList"): dict(label="Quest list", kind="n", tip="Quest giver list ID."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "pageMaterial"): dict(label="Page material", kind="r", reference="PageTextMaterial", tip="PageTextMaterial.dbc ID."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "gossipID"): dict(label="Gossip menu", kind="r", reference="Gossip", tip="Gossip menu ID. 0 for none."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "customAnim"): dict(label="Custom animation", kind="n", tip="Animation played while interacting."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "noDamageImmune"): dict(label="Damage does not interrupt", kind="b", tip="Taking damage does not cancel the interaction."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "losOK"): dict(label="Requires line of sight", kind="b", tip="Must be in line of sight to use."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "allowMounted"): dict(label="Usable while mounted", kind="b", tip="Players can use it while mounted."),
    ("GAMEOBJECT_TYPE_QUESTGIVER", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_CHEST", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to open it. 0 for none."),
    ("GAMEOBJECT_TYPE_CHEST", "lootId"): dict(label="Loot table", kind="r", reference="Loot", tip="Loot table rolled when opened. 0 drops nothing."),
    ("GAMEOBJECT_TYPE_CHEST", "chestRestockTime"): dict(label="Restock time", kind="n", tip="Seconds before the chest can be looted again."),
    ("GAMEOBJECT_TYPE_CHEST", "consumable"): dict(label="Consumable", kind="b", tip="The chest is destroyed after it is looted."),
    ("GAMEOBJECT_TYPE_CHEST", "minSuccessOpens"): dict(label="Minimum opens (deprecated)", kind="n", tip="Not read by WotLK servers."),
    ("GAMEOBJECT_TYPE_CHEST", "maxSuccessOpens"): dict(label="Maximum opens (deprecated)", kind="n", tip="Not read by WotLK servers."),
    ("GAMEOBJECT_TYPE_CHEST", "eventId"): dict(label="Looted event", kind="r", reference="GameEvent", tip="Game event started when looted. 0 for none."),
    ("GAMEOBJECT_TYPE_CHEST", "linkedTrapId"): dict(label="Linked trap", kind="r", reference="GameObject", tip="GameObject entry of the trap it triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_CHEST", "questId"): dict(label="Required quest", kind="r", reference="Quest", tip="Quest required to open it. 0 for none."),
    ("GAMEOBJECT_TYPE_CHEST", "level"): dict(label="Level", kind="n", tip="Level used by the lock/skill checks."),
    ("GAMEOBJECT_TYPE_CHEST", "losOK"): dict(label="Requires line of sight", kind="b", tip="Must be in line of sight to open."),
    ("GAMEOBJECT_TYPE_CHEST", "leaveLoot"): dict(label="Leave loot", kind="b", tip="Leaves the loot window open after looting."),
    ("GAMEOBJECT_TYPE_CHEST", "notInCombat"): dict(label="Only out of combat", kind="b", tip="Cannot be opened in combat."),
    ("GAMEOBJECT_TYPE_CHEST", "logLoot"): dict(label="Log loot", kind="b", tip="Writes the loot to the server log."),
    ("GAMEOBJECT_TYPE_CHEST", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_CHEST", "groupLootRules"): dict(label="Group loot rules", kind="b", tip="Uses group loot rules instead of personal loot."),
    ("GAMEOBJECT_TYPE_CHEST", "floatingTooltip"): dict(label="Floating tooltip", kind="b", tip="Shows a floating tooltip over the object."),
    ("GAMEOBJECT_TYPE_GENERIC", "floatingTooltip"): dict(label="Floating tooltip", kind="b", tip="Shows a floating tooltip over the object."),
    ("GAMEOBJECT_TYPE_GENERIC", "highlight"): dict(label="Highlight", kind="b", tip="Highlights the object when hovered."),
    ("GAMEOBJECT_TYPE_GENERIC", "serverOnly"): dict(label="Server only", kind="b", tip="The client does not see this object."),
    ("GAMEOBJECT_TYPE_GENERIC", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_GENERIC", "floatOnWater"): dict(label="Floats on water", kind="b", tip="The object floats on water."),
    ("GAMEOBJECT_TYPE_GENERIC", "questID"): dict(label="Quest", kind="r", reference="Quest", tip="Quest associated with the object. 0 for none."),
    ("GAMEOBJECT_TYPE_TRAP", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_TRAP", "level"): dict(label="Level", kind="n", tip="Trap level used by skill checks."),
    ("GAMEOBJECT_TYPE_TRAP", "diameter"): dict(label="Trigger diameter", kind="n", tip="Radius in which the trap activates."),
    ("GAMEOBJECT_TYPE_TRAP", "spellId"): dict(label="Spell", kind="r", reference="Spell", tip="Spell cast when the trap triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_TRAP", "type"): dict(label="Trap type", kind="c", options=[(0, "Does not despawn"), (1, "Despawns after casting"), (2, "Bomb casts on spawn")], tip="What happens after the trap casts."),
    ("GAMEOBJECT_TYPE_TRAP", "cooldown"): dict(label="Cooldown", kind="n", tip="Seconds between activations."),
    ("GAMEOBJECT_TYPE_TRAP", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_TRAP", "startDelay"): dict(label="Start delay", kind="n", tip="Seconds before the trap can trigger."),
    ("GAMEOBJECT_TYPE_TRAP", "serverOnly"): dict(label="Server only", kind="b", tip="The client does not see this trap."),
    ("GAMEOBJECT_TYPE_TRAP", "stealthed"): dict(label="Stealthed", kind="b", tip="The trap is hidden until it triggers."),
    ("GAMEOBJECT_TYPE_TRAP", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_TRAP", "invisible"): dict(label="Invisible", kind="b", tip="The trap is invisible."),
    ("GAMEOBJECT_TYPE_TRAP", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_TRAP", "closeTextID"): dict(label="Close text", kind="r", reference="BroadcastText", tip="Broadcast text shown when it closes. 0 for none."),
    ("GAMEOBJECT_TYPE_TRAP", "ignoreTotems"): dict(label="Ignore totems", kind="b", tip="Totems do not trigger the trap."),
    ("GAMEOBJECT_TYPE_CHAIR", "slots"): dict(label="Seats", kind="n", default=1, tip="How many players can sit at once."),
    ("GAMEOBJECT_TYPE_CHAIR", "height"): dict(label="Seat height", kind="n", default=1, tip="How high the seat is above the floor; 1 is the normal chair height."),
    ("GAMEOBJECT_TYPE_CHAIR", "onlyCreatorUse"): dict(label="Only the creator can use it", kind="b", tip="Only the summoning player can sit."),
    ("GAMEOBJECT_TYPE_CHAIR", "triggeredEvent"): dict(label="Triggered event", kind="r", reference="GameEvent", tip="Game event started when used. 0 for none."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "focusId"): dict(label="Focus", kind="n", tip="SpellFocusObject.dbc ID."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "dist"): dict(label="Distance", kind="n", tip="Maximum interaction distance."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "linkedTrapId"): dict(label="Linked trap", kind="r", reference="GameObject", tip="GameObject entry of the trap it triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "serverOnly"): dict(label="Server only", kind="b", tip="The client does not see this focus."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "questID"): dict(label="Quest", kind="r", reference="Quest", tip="Quest associated with the focus. 0 for none."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "floatingTooltip"): dict(label="Floating tooltip", kind="b", tip="Shows a floating tooltip over the object."),
    ("GAMEOBJECT_TYPE_TEXT", "pageID"): dict(label="Page", kind="r", reference="PageText", tip="PageText.dbc ID shown to the player."),
    ("GAMEOBJECT_TYPE_TEXT", "language"): dict(label="Language", kind="r", reference="Language", tip="Language the text is written in."),
    ("GAMEOBJECT_TYPE_TEXT", "pageMaterial"): dict(label="Page material", kind="r", reference="PageTextMaterial", tip="PageTextMaterial.dbc ID."),
    ("GAMEOBJECT_TYPE_TEXT", "allowMounted"): dict(label="Usable while mounted", kind="b", tip="Players can read it while mounted."),
    ("GAMEOBJECT_TYPE_GOOBER", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "questId"): dict(label="Quest", kind="r", reference="Quest", tip="Quest associated with the object. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "eventId"): dict(label="Event", kind="r", reference="GameEvent", tip="Game event started when used. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_GOOBER", "customAnim"): dict(label="Custom animation", kind="n", tip="Animation played while interacting."),
    ("GAMEOBJECT_TYPE_GOOBER", "consumable"): dict(label="Consumable", kind="b", tip="The object is destroyed after use."),
    ("GAMEOBJECT_TYPE_GOOBER", "cooldown"): dict(label="Cooldown", kind="n", tip="Seconds between uses."),
    ("GAMEOBJECT_TYPE_GOOBER", "pageId"): dict(label="Page", kind="r", reference="PageText", tip="PageText.dbc ID shown to the player."),
    ("GAMEOBJECT_TYPE_GOOBER", "language"): dict(label="Language", kind="r", reference="Language", tip="Language the text is written in."),
    ("GAMEOBJECT_TYPE_GOOBER", "pageMaterial"): dict(label="Page material", kind="r", reference="PageTextMaterial", tip="PageTextMaterial.dbc ID."),
    ("GAMEOBJECT_TYPE_GOOBER", "spellId"): dict(label="Spell", kind="r", reference="Spell", tip="Spell cast when used. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "noDamageImmune"): dict(label="Damage does not interrupt", kind="b", tip="Taking damage does not cancel the interaction."),
    ("GAMEOBJECT_TYPE_GOOBER", "linkedTrapId"): dict(label="Linked trap", kind="r", reference="GameObject", tip="GameObject entry of the trap it triggers. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_GOOBER", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "closeTextID"): dict(label="Close text", kind="r", reference="BroadcastText", tip="Broadcast text shown when closed. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "losOK"): dict(label="Requires line of sight", kind="b", tip="Must be in line of sight to use."),
    ("GAMEOBJECT_TYPE_GOOBER", "allowMounted"): dict(label="Usable while mounted", kind="b", tip="Players can use it while mounted."),
    ("GAMEOBJECT_TYPE_GOOBER", "floatingTooltip"): dict(label="Floating tooltip", kind="b", tip="Shows a floating tooltip over the object."),
    ("GAMEOBJECT_TYPE_GOOBER", "gossipID"): dict(label="Gossip menu", kind="r", reference="Gossip", tip="Gossip menu ID. 0 for none."),
    ("GAMEOBJECT_TYPE_GOOBER", "WorldStateSetsState"): dict(label="World state sets state", kind="b", tip="A world state controls the object's state."),
    ("GAMEOBJECT_TYPE_TRANSPORT", "pauseAtTime"): dict(label="Pause at time", kind="n", tip="Time in the transport path where it pauses."),
    ("GAMEOBJECT_TYPE_TRANSPORT", "startOpen"): dict(label="Starts open", kind="b", tip="The transport starts in the open state."),
    ("GAMEOBJECT_TYPE_TRANSPORT", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_TRANSPORT", "pause1EventID"): dict(label="Pause 1 event", kind="r", reference="GameEvent", tip="Game event started at the first pause. 0 for none."),
    ("GAMEOBJECT_TYPE_TRANSPORT", "pause2EventID"): dict(label="Pause 2 event", kind="r", reference="GameEvent", tip="Game event started at the second pause. 0 for none."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "radius"): dict(label="Radius", kind="n", tip="Radius of the damage area."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "damageMin"): dict(label="Minimum damage", kind="n", tip="Lowest damage dealt."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "damageMax"): dict(label="Maximum damage", kind="n", tip="Highest damage dealt."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "damageSchool"): dict(label="Damage school", kind="c", options=[(0, "Physical"), (1, "Holy"), (2, "Fire"), (3, "Nature"), (4, "Frost"), (5, "Shadow"), (6, "Arcane")], tip="School of the damage."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "autoCloseTime"): dict(label="Auto-close", kind="n", scale=65536, tip="Seconds until it closes. Stored as 1/65536 s."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_AREADAMAGE", "closeTextID"): dict(label="Close text", kind="r", reference="BroadcastText", tip="Broadcast text shown when closed. 0 for none."),
    ("GAMEOBJECT_TYPE_CAMERA", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_CAMERA", "cinematicId"): dict(label="Cinematic", kind="r", reference="Cinematic", tip="Cinematic.dbc ID played when used."),
    ("GAMEOBJECT_TYPE_CAMERA", "eventID"): dict(label="Event", kind="r", reference="GameEvent", tip="Game event started when used. 0 for none."),
    ("GAMEOBJECT_TYPE_CAMERA", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "reqParticipants"): dict(label="Required participants", kind="n", tip="How many players must take part."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "spellId"): dict(label="Spell", kind="r", reference="Spell", tip="Spell cast by the ritual."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "animSpell"): dict(label="Animation spell", kind="r", reference="Spell", tip="Spell whose animation plays during the ritual."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "ritualPersistent"): dict(label="Persistent", kind="b", tip="The ritual stays after it finishes."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "casterTargetSpell"): dict(label="Caster target spell", kind="r", reference="Spell", tip="Spell cast on the caster's target."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "casterTargetSpellTargets"): dict(label="Caster target spell targets", kind="n", tip="How many targets the caster target spell hits."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "castersGrouped"): dict(label="Casters grouped", kind="b", tip="The participants are grouped for the cast."),
    ("GAMEOBJECT_TYPE_SUMMONING_RITUAL", "ritualNoTargetCheck"): dict(label="No target check", kind="b", tip="The ritual does not require a target."),
    ("GAMEOBJECT_TYPE_GUARDPOST", "creatureID"): dict(label="Creature", kind="r", reference="Creature", tip="Creature entry spawned by the guard post."),
    ("GAMEOBJECT_TYPE_GUARDPOST", "charges"): dict(label="Charges", kind="n", tip="How many creatures it can spawn."),
    ("GAMEOBJECT_TYPE_SPELLCASTER", "spellId"): dict(label="Spell", kind="r", reference="Spell", tip="Spell cast when used."),
    ("GAMEOBJECT_TYPE_SPELLCASTER", "charges"): dict(label="Charges", kind="n", tip="How many times it can cast."),
    ("GAMEOBJECT_TYPE_SPELLCASTER", "partyOnly"): dict(label="Party only", kind="b", tip="Only party or raid members can use it."),
    ("GAMEOBJECT_TYPE_SPELLCASTER", "allowMounted"): dict(label="Usable while mounted", kind="b", tip="Players can use it while mounted."),
    ("GAMEOBJECT_TYPE_SPELLCASTER", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_MEETINGSTONE", "minLevel"): dict(label="Minimum level", kind="n", tip="Lowest level that can use the stone."),
    ("GAMEOBJECT_TYPE_MEETINGSTONE", "maxLevel"): dict(label="Maximum level", kind="n", tip="Highest level that can use the stone."),
    ("GAMEOBJECT_TYPE_MEETINGSTONE", "areaID"): dict(label="Area", kind="r", reference="Area", tip="AreaTable.dbc ID of the meeting stone's area."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "pickupSpell"): dict(label="Pickup spell", kind="r", reference="Spell", tip="Spell cast when the flag is picked up."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "radius"): dict(label="Radius", kind="n", tip="How close a player must be to the flag to pick it up."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "returnAura"): dict(label="Return aura", kind="r", reference="Spell", tip="Aura applied while carrying the flag."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "returnSpell"): dict(label="Return spell", kind="r", reference="Spell", tip="Spell cast when the flag is returned."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "noDamageImmune"): dict(label="Damage does not interrupt", kind="b", tip="Taking damage does not cancel the interaction."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_FLAGSTAND", "losOK"): dict(label="Requires line of sight", kind="b", tip="Must be in line of sight to use."),
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "radius"): dict(label="Radius", kind="n", tip="How close the bobber must land to the hole for a catch."),
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "lootId"): dict(label="Loot table", kind="r", reference="Loot", tip="The loot table rolled when a fish is caught. 0 means no loot."),
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "minSuccessOpens"): dict(label="Minimum opens", kind="n", tip="The fewest catches before the hole despawns."),
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "maxSuccessOpens"): dict(label="Maximum opens", kind="n", tip="The most catches before the hole despawns."),
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="The Lock.dbc ID required to fish here. 0 means no lock."),
    ("GAMEOBJECT_TYPE_FLAGDROP", "lockId"): dict(label="Lock", kind="r", reference="Lock", tip="Lock.dbc ID required to use it. 0 for none."),
    ("GAMEOBJECT_TYPE_FLAGDROP", "eventID"): dict(label="Event", kind="r", reference="GameEvent", tip="Game event started when used. 0 for none."),
    ("GAMEOBJECT_TYPE_FLAGDROP", "pickupSpell"): dict(label="Pickup spell", kind="r", reference="Spell", tip="Spell cast when the flag is picked up."),
    ("GAMEOBJECT_TYPE_FLAGDROP", "noDamageImmune"): dict(label="Damage does not interrupt", kind="b", tip="Taking damage does not cancel the interaction."),
    ("GAMEOBJECT_TYPE_FLAGDROP", "openTextID"): dict(label="Open text", kind="r", reference="BroadcastText", tip="Broadcast text shown when opened. 0 for none."),
    ("GAMEOBJECT_TYPE_MINI_GAME", "gameType"): dict(label="Game type", kind="n", tip="The mini-game type ID; it selects which mini-game the object runs."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "radius"): dict(label="Radius", kind="n", tip="The radius in yards in which players can capture the point."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "spell"): dict(label="Spell", kind="r", reference="Spell", tip="Spell cast while capturing."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "worldState1"): dict(label="World state 1", kind="n", tip="The world state set while the point is neutral."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "worldstate2"): dict(label="World state 2", kind="n", tip="The world state set while the point is contested."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "winEventID1"): dict(label="Win event 1", kind="r", reference="GameEvent", tip="Event started when the first team wins."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "winEventID2"): dict(label="Win event 2", kind="r", reference="GameEvent", tip="Event started when the second team wins."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "contestedEventID1"): dict(label="Contested event 1", kind="r", reference="GameEvent", tip="Event started when the point becomes contested."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "contestedEventID2"): dict(label="Contested event 2", kind="r", reference="GameEvent", tip="Event started when the point becomes contested."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "progressEventID1"): dict(label="Progress event 1", kind="r", reference="GameEvent", tip="Event started while the first team captures."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "progressEventID2"): dict(label="Progress event 2", kind="r", reference="GameEvent", tip="Event started while the second team captures."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "neutralEventID1"): dict(label="Neutral event 1", kind="r", reference="GameEvent", tip="Event started when the point becomes neutral."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "neutralEventID2"): dict(label="Neutral event 2", kind="r", reference="GameEvent", tip="Event started when the point becomes neutral."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "neutralPercent"): dict(label="Neutral percent", kind="n", tip="Percentage at which the point becomes neutral."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "worldstate3"): dict(label="World state 3", kind="n", tip="The world state set while the point is controlled."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "minSuperiority"): dict(label="Minimum superiority", kind="n", tip="Minimum player advantage needed to capture."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "maxSuperiority"): dict(label="Maximum superiority", kind="n", tip="Maximum player advantage that still captures."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "minTime"): dict(label="Minimum time", kind="n", tip="Minimum capture time."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "maxTime"): dict(label="Maximum time", kind="n", tip="Maximum capture time."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "large"): dict(label="Large", kind="b", tip="Uses the large interaction radius."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "highlight"): dict(label="Highlight", kind="b", tip="Highlights the point."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "startingValue"): dict(label="Starting value", kind="n", tip="Initial capture progress."),
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "unidirectional"): dict(label="Unidirectional", kind="b", tip="The point can only be captured one way."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "startOpen"): dict(label="Starts active", kind="b", tip="The generator starts active."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "radius"): dict(label="Radius", kind="n", tip="The radius in yards in which the auras are applied."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "auraID1"): dict(label="Aura 1", kind="r", reference="Spell", tip="First aura applied in the radius."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "conditionID1"): dict(label="Condition 1", kind="r", reference="PlayerCondition", tip="PlayerCondition.dbc ID for the first aura."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "auraID2"): dict(label="Aura 2", kind="r", reference="Spell", tip="Second aura applied in the radius."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "conditionID2"): dict(label="Condition 2", kind="r", reference="PlayerCondition", tip="PlayerCondition.dbc ID for the second aura."),
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "serverOnly"): dict(label="Server only", kind="b", tip="The client does not see this generator."),
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "mapID"): dict(label="Map", kind="r", reference="Map", tip="The map ID whose difficulty this object changes."),
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "difficulty"): dict(label="Difficulty", kind="n", tip="Dungeon difficulty (0 normal, 1 heroic)."),
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "chairheight"): dict(label="Seat height", kind="n", tip="How high the seat is above the floor; 1 is the normal height."),
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "heightOffset"): dict(label="Height offset", kind="n", tip="Vertical offset for the seated player."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "intactNumHits"): dict(label="Intact hits", kind="n", tip="Hits needed to damage the building."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "creditProxyCreature"): dict(label="Credit proxy creature", kind="r", reference="Creature", tip="Creature entry that receives kill credit."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "state1Name"): dict(label="State 1 name", kind="n", tip="Name shown in the first state."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "intactEvent"): dict(label="Intact event", kind="r", reference="GameEvent", tip="Event started while intact."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "damagedDisplayId"): dict(label="Damaged display", kind="r", reference="GameObjectDisplay", tip="Display ID used while damaged."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "damagedNumHits"): dict(label="Damaged hits", kind="n", tip="Hits needed to destroy the damaged building."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "damagedEvent"): dict(label="Damaged event", kind="r", reference="GameEvent", tip="Event started when damaged."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "destroyedDisplayId"): dict(label="Destroyed display", kind="r", reference="GameObjectDisplay", tip="Display ID used when destroyed."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "destroyedEvent"): dict(label="Destroyed event", kind="r", reference="GameEvent", tip="Event started when destroyed."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "debuildingTimeSecs"): dict(label="Rebuild time", kind="n", tip="Seconds until the building rebuilds."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "destructibleData"): dict(label="Destructible data", kind="n", tip="Destructible data ID."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "rebuildingEvent"): dict(label="Rebuilding event", kind="r", reference="GameEvent", tip="Event started while rebuilding."),
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "damageEvent"): dict(label="Damage event", kind="r", reference="GameEvent", tip="Event started when damaged."),
    ("GAMEOBJECT_TYPE_TRAPDOOR", "whenToPause"): dict(label="Pause when", kind="n", tip="When the trap door pauses."),
    ("GAMEOBJECT_TYPE_TRAPDOOR", "startOpen"): dict(label="Starts open", kind="b", tip="The trap door starts open."),
    ("GAMEOBJECT_TYPE_TRAPDOOR", "autoClose"): dict(label="Auto-close", kind="n", tip="Seconds until it closes."),
}

# Type names that read better than the raw enum suffix.
TYPE_NAMES = {
    "GAMEOBJECT_TYPE_QUESTGIVER": "Quest giver",
    "GAMEOBJECT_TYPE_SPELL_FOCUS": "Spell focus",
    "GAMEOBJECT_TYPE_MAP_OBJECT": "Map object",
    "GAMEOBJECT_TYPE_MAPOBJECT": "Map object",
    "GAMEOBJECT_TYPE_MO_TRANSPORT": "Map object transport",
    "GAMEOBJECT_TYPE_MAP_OBJ_TRANSPORT": "Map object transport",
    "GAMEOBJECT_TYPE_SUMMONING_RITUAL": "Summoning ritual",
    "GAMEOBJECT_TYPE_RITUAL": "Summoning ritual",
    "GAMEOBJECT_TYPE_GUILD_BANK": "Guild bank",
    "GAMEOBJECT_TYPE_GUILDBANK": "Guild bank",
    "GAMEOBJECT_TYPE_TRAPDOOR": "Trap door",
    "GAMEOBJECT_TYPE_MINI_GAME": "Mini game",
    "GAMEOBJECT_TYPE_AREADAMAGE": "Area damage",
    "GAMEOBJECT_TYPE_FISHINGNODE": "Fishing node",
    "GAMEOBJECT_TYPE_FISHINGHOLE": "Fishing hole",
    "GAMEOBJECT_TYPE_DO_NOT_USE": "Do not use",
    "GAMEOBJECT_TYPE_DO_NOT_USE_2": "Do not use 2",
    "GAMEOBJECT_TYPE_DONOTUSE": "Do not use",
    "GAMEOBJECT_TYPE_PVP_REWARD": "PvP reward",
    "GAMEOBJECT_TYPE_GARR_TALENT_TREE": "Garrison talent tree",
    "GAMEOBJECT_TYPE_GATHERING_NODE": "Gathering node",
    "GAMEOBJECT_TYPE_CHALLENGE_MODE_REWARD": "Challenge mode reward",
    "GAMEOBJECT_TYPE_PLAYER_CHOICE_CHEST": "Player choice chest",
    "GAMEOBJECT_TYPE_CRAFTING_TABLE": "Crafting table",
    "GAMEOBJECT_TYPE_PERKS_PROGRAM_CHEST": "Perks program chest",
    "GAMEOBJECT_TYPE_CLIENT_CREATURE": "Client creature",
    "GAMEOBJECT_TYPE_CLIENT_ITEM": "Client item",
    "GAMEOBJECT_TYPE_CLIENT_MODEL": "Client model",
    "GAMEOBJECT_TYPE_ASSIST_ACTION": "Assist action",
    "GAMEOBJECT_TYPE_RESERVED_13A": "Reserved 13A",
    "GAMEOBJECT_TYPE_RESERVED_13B": "Reserved 13B",
    "GAMEOBJECT_TYPE_DUEL_ARBITER": "Duel arbiter",
    "GAMEOBJECT_TYPE_DUELFLAG": "Duel flag",
    "GAMEOBJECT_TYPE_CONTROL_ZONE": "Control zone",
    "GAMEOBJECT_TYPE_CAPTURE_POINT": "Capture point",
    "GAMEOBJECT_TYPE_FUTURE_PATCH": "Future patch",
    "GAMEOBJECT_TYPE_ITEM_FORGE": "Item forge",
    "GAMEOBJECT_TYPE_LEGENDARY_FORGE": "Legendary forge",
    "GAMEOBJECT_TYPE_MULTI": "Multi",
    "GAMEOBJECT_TYPE_SIEGEABLE_MULTI": "Siegeable multi",
    "GAMEOBJECT_TYPE_SIEGEABLE_MO": "Siegeable map object",
    "GAMEOBJECT_TYPE_UI_LINK": "UI link",
    "GAMEOBJECT_TYPE_KEYSTONE_RECEPTACLE": "Keystone receptacle",
    "GAMEOBJECT_TYPE_GARRISON_BUILDING": "Garrison building",
    "GAMEOBJECT_TYPE_GARRISON_PLOT": "Garrison plot",
    "GAMEOBJECT_TYPE_GARRISON_MONUMENT": "Garrison monument",
    "GAMEOBJECT_TYPE_GARRISON_MONUMENT_PLAQUE": "Garrison monument plaque",
    "GAMEOBJECT_TYPE_GARRISON_SHIPMENT": "Garrison shipment",
    "GAMEOBJECT_TYPE_PHASEABLE_MO": "Phaseable map object",
    "GAMEOBJECT_TYPE_WEEKLY_REWARD_CHEST": "Weekly reward chest",
}

TYPE_HINTS = {
    "GAMEOBJECT_TYPE_DOOR": "Opens and closes; often a dungeon gate or a scripted door.",
    "GAMEOBJECT_TYPE_BUTTON": "A pressable button that can open a linked door or trap.",
    "GAMEOBJECT_TYPE_QUESTGIVER": "A GameObject players talk to for quests.",
    "GAMEOBJECT_TYPE_CHEST": "A lootable chest; the loot table is rolled when it is opened.",
    "GAMEOBJECT_TYPE_GENERIC": "A simple object; most fields are display and interaction switches.",
    "GAMEOBJECT_TYPE_TRAP": "Triggers a spell when a player comes near or uses it.",
    "GAMEOBJECT_TYPE_CHAIR": "A sittable chair.",
    "GAMEOBJECT_TYPE_SPELL_FOCUS": "A focus for a spell, such as a forge or a fishing spot.",
    "GAMEOBJECT_TYPE_TEXT": "Shows a page of text.",
    "GAMEOBJECT_TYPE_GOOBER": "A general interactive object with quest, gossip and spell support.",
    "GAMEOBJECT_TYPE_TRANSPORT": "A moving elevator, boat or other transport object.",
    "GAMEOBJECT_TYPE_CAPTURE_POINT": "A PvP capture point.",
    "GAMEOBJECT_TYPE_CONTROL_ZONE": "A PvP control zone.",
    "GAMEOBJECT_TYPE_MAILBOX": "A mailbox.",
    "GAMEOBJECT_TYPE_FISHINGHOLE": "A fishing hole; the bobber must land inside its radius to catch a fish.",
    "GAMEOBJECT_TYPE_FISHINGNODE": "The bobber object a player casts while fishing.",
    "GAMEOBJECT_TYPE_GATHERING_NODE": "A gatherable node such as an herb or ore vein.",
    "GAMEOBJECT_TYPE_FLAGSTAND": "A battleground flag that players can pick up.",
    "GAMEOBJECT_TYPE_FLAGDROP": "The dropped flag in a battleground.",
    "GAMEOBJECT_TYPE_SPELLCASTER": "Casts a spell when a player uses it.",
    "GAMEOBJECT_TYPE_GUARDPOST": "Spawns a guard creature when triggered.",
    "GAMEOBJECT_TYPE_MEETINGSTONE": "A meeting stone that summons party members.",
    "GAMEOBJECT_TYPE_CAMERA": "Plays a camera cinematic when used.",
    "GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY": "Changes the instance difficulty when used.",
    "GAMEOBJECT_TYPE_BARBER_CHAIR": "A barber-shop chair.",
    "GAMEOBJECT_TYPE_TRAPDOOR": "A trap door that opens and closes.",
    "GAMEOBJECT_TYPE_AURA_GENERATOR": "Applies one or two auras to players in a radius.",
    "GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING": "A building that can be damaged and destroyed.",
}

REFERENCE_NAMES = {
    "Lock_": "Lock",
    "GameObjects": "GameObject",
    "GameObject": "GameObject",
    "Treasure": "Loot",
    "Loot": "Loot",
    "QuestV2": "Quest",
    "Quest": "Quest",
    "Gossip": "Gossip",
    "BroadcastText": "BroadcastText",
    "Spell": "Spell",
    "PlayerCondition": "PlayerCondition",
    "GameEvents": "GameEvent",
    "GameEvent": "GameEvent",
    "PageTextMaterial": "PageTextMaterial",
    "PageText": "PageText",
    "FactionTemplate": "FactionTemplate",
    "Faction": "Faction",
    "Item": "Item",
    "Achievement": "Achievement",
    "SkillLine": "SkillLine",
    "Currency": "Currency",
    "ExtendedCost": "ExtendedCost",
    "AnimKit": "AnimKit",
    "AreaTrigger": "AreaTrigger",
    "Area": "Area",
    "AreaTable": "Area",
    "Map": "Map",
    "Cinematic": "Cinematic",
    "CinematicSequences": "Cinematic",
    "Language": "Language",
    "Languages": "Language",
    "Creature": "Creature",
    "CreatureDisplayInfo": "CreatureDisplay",
    "QuestGiver": "QuestGiver",
    "vignette": "Vignette",
    "animationdata": "AnimationData",
    "GameObjectDisplayInfo": "GameObjectDisplay",
    "gameobjectdisplayinfo": "GameObjectDisplay",
    "DungeonEncounter": "DungeonEncounter",
    "ChestProperties": "ChestProperties",
    "PlayerDataFlagCharacter": "PlayerDataFlag",
    "PlayerDataFlagAccount": "PlayerDataFlag",
    "CameraMode": "CameraMode",
    "CharShipmentContainer": "CharShipmentContainer",
    "CustomItemBonusFilter": "CustomItemBonusFilter",
    "DestructibleHitpoint": "DestructibleHitpoint",
    "DestructibleModelData": "DestructibleModelData",
    "GameObjectDiffAnim": "GameObjectDiffAnim",
    "GarrPlotInstance": "GarrPlotInstance",
    "GarrTalentTree": "GarrTalentTree",
    "MawPowerFilter": "MawPowerFilter",
    "MultiProperties": "MultiProperties",
    "PlayerChoice": "PlayerChoice",
    "SiegeableProperties": "SiegeableProperties",
    "SpellFocusObject": "SpellFocusObject",
    "SpellScript": "SpellScript",
    "SpellVisual": "SpellVisual",
    "TaxiPath": "TaxiPath",
    "TransportPhysics": "TransportPhysics",
    "TrophyInstance": "TrophyInstance",
    "TrophyType": "TrophyType",
    "UiItemInteraction": "UiItemInteraction",
    "UiMap": "UiMap",
    "UiTextureAtlasMember": "UiTextureAtlasMember",
    "WorldState": "WorldState",
    "WorldStateExpression": "WorldStateExpression",
}

BOOL_CODES = {
    "startOpen", "noDamageImmune", "large", "serverOnly", "consumable", "leaveLoot", "notInCombat",
    "logLoot", "floatingTooltip", "highlight", "floatOnWater", "onlyCreatorUse", "losOK", "allowMounted",
    "stealthed", "invisible", "ignoreTotems", "partyOnly", "ritualPersistent", "castersGrouped",
    "ritualNoTargetCheck", "canBeStopped", "unidirectional", "ignoredByPathing", "groupLootRules",
    "WorldStateSetsState", "alwaysSpawn", "hideLabel", "lock", "usable", "triggered", "inUse",
}

# Tips shared by every type with the same column code. These are the fields whose
# names do not explain the effect on their own.
COMMON_TIPS = {
    "open": "The Lock.dbc ID required to open or use it. 0 means no lock.",
    "lockId": "The Lock.dbc ID required to open or use it. 0 means no lock.",
    "startOpen": "Whether it starts in the open or active state.",
    "autoClose": "How long it stays open before closing again, in milliseconds. 0 means it never closes automatically.",
    "autoCloseTime": "How long it stays open before closing again, in seconds. The stored value is in 1/65536-second units.",
    "noDamageImmune": "Whether taking damage interrupts the opening or interaction.",
    "openTextID": "The broadcast text shown when it opens or is used. 0 means no text.",
    "closeTextID": "The broadcast text shown when it closes. 0 means no text.",
    "conditionID1": "The PlayerCondition.dbc ID that must pass before it can be used. 0 means no condition.",
    "conditionID2": "The PlayerCondition.dbc ID that must pass before the second effect can be used. 0 means no condition.",
    "InteractRadiusOverride": "The interaction radius in yards multiplied by 100. 0 uses the model's normal radius.",
    "GiganticAOI": "Whether it uses a much larger area of interest, so it is sent to players farther away.",
    "LargeAOI": "Whether it uses a larger area of interest than normal.",
    "InfiniteAOI": "Whether it is always sent to every player on the map.",
    "TrueInfiniteAOI": "Whether it is always sent to every player on the map, ignoring the normal area limit.",
    "requireLOS": "Whether the player must have line of sight to use it.",
    "losOK": "Whether the player must have line of sight to use it.",
    "allowMounted": "Whether it can be used while mounted.",
    "NeverUsableWhileMounted": "Whether it cannot be used while mounted.",
    "playerCast": "Whether the player casts the spell instead of the GameObject.",
    "consumable": "Whether it is destroyed after it is used.",
    "cooldown": "How many seconds it waits before it can be used again.",
    "startDelay": "How many seconds it waits after spawning before it can trigger.",
    "customAnim": "The custom animation index played when it is used.",
    "floatingTooltip": "Whether a floating tooltip is shown above it.",
    "highlight": "Whether it is highlighted when the player hovers over it.",
    "serverOnly": "Whether it is hidden from the client; server-only objects are not sent to players.",
    "linkedTrap": "The GameObject entry of the trap it triggers. 0 means no trap.",
    "linkedTrapId": "The GameObject entry of the trap it triggers. 0 means no trap.",
    "eventID": "The game event started when it is used. 0 means no event.",
    "eventId": "The game event started when it is used. 0 means no event.",
    "triggeredEvent": "The game event started when it is triggered. 0 means no event.",
    "NotLOSBlocking": "Whether it does not block line of sight when closed.",
    "stealthAffected": "Whether stealth affects the trap's ability to detect players.",
    "SpawnVignette": "The vignette shown in the world when it spawns. 0 means none.",
    "DungeonEncounter": "The dungeon encounter associated with this object. 0 means none.",
    "MultiProperties": "The MultiProperties record that controls the object's multi-use behavior.",
    "SiegeableProperties": "The SiegeableProperties record that controls the object's siege behavior.",
    "PlotInstance": "The garrison plot instance this object belongs to.",
    "onlychargeheightcheck": "The height in yards used by the transport's charge check; 0 disables the check.",
    "onlychargetimecheck": "The time in milliseconds used by the transport's charge check; 0 disables the check.",
    "gossipID": "The gossip menu shown when a player talks to it. 0 means no menu.",
    "questID": "The quest associated with it. 0 means no quest.",
    "questId": "The quest associated with it. 0 means no quest.",
    "spell": "The spell cast when it is used or triggered. 0 means no spell.",
    "spellId": "The spell cast when it is used or triggered. 0 means no spell.",
    "pageID": "The page of text shown. 0 means no page.",
    "pageId": "The page of text shown. 0 means no page.",
    "language": "The language the text is written in.",
    "pageMaterial": "The page material used for the text background.",
    "level": "The level used for its skill or lock checks.",
    "radius": "The effect radius in yards.",
    "diameter": "The trigger diameter in yards.",
    "charges": "How many times it can trigger before it is used up. -1 often means unlimited.",
    "stealthed": "Whether it is hidden from players until it triggers.",
    "invisible": "Whether it is invisible.",
    "ignoreTotems": "Whether totems are ignored when it checks for nearby units.",
    "notInCombat": "Whether it can only be used out of combat.",
    "leaveLoot": "Whether the loot window stays open after looting.",
    "logLoot": "Whether loot is written to the server log.",
    "groupLootRules": "Whether group loot rules are used instead of personal loot.",
    "onlyCreatorUse": "Whether only the player who summoned it can use it.",
    "ritualPersistent": "Whether the ritual stays after it finishes.",
    "castersGrouped": "Whether the ritual participants are grouped for the cast.",
    "ritualNoTargetCheck": "Whether the ritual skips the target check.",
    "canBeStopped": "Whether players can stop the transport.",
    "unidirectional": "Whether the point can only be captured one way.",
    "FrequentHeartbeat": "Whether the control zone updates its state more often.",
    "Legacy": "Whether the object uses the older difficulty behavior.",
    "InteriorVisible": "Whether the building's interior is visible.",
    "InteriorLight": "Whether the building's interior is lit.",
    "PVPEnabling": "Whether the building enables PvP.",
    "Displaymouseoverasanameplate": "Whether the destructible building shows a nameplate when the player hovers over it.",
    "DontPlayOpenAnim": "Whether it skips its opening animation.",
    "IgnoreBoundingBox": "Whether interaction ignores the model's bounding box.",
    "SortFarZ": "Whether the client sorts the object with far-Z objects.",
    "SyncAnimationtoObjectLifetime": "Whether the animation is synchronized with the object's lifetime.",
    "NoFuzzyHit": "Whether the client requires an exact hit instead of a fuzzy one.",
    "AllowMultiInteract": "Whether several players can interact with it at the same time.",
    "floatOnWater": "Whether it floats on water.",
    "BlocksPathsDown": "Whether it blocks pathfinding downward.",
    "AutoLink": "Whether it automatically links to a nearby door or trap.",
    "DoorisOpaque": "Whether the door blocks the view when closed.",
    "IgnoredByPathing": "Whether creatures ignore it when pathfinding.",
    "BlockGhosts": "Whether it blocks ghost players.",
    "Preferonlyifinlineofsight": "Whether the guard post prefers a target in line of sight.",
    "InitStopped": "Whether the transport starts stopped.",
    "allowstopping": "Whether players can stop the transport.",
    "Allowareaexplorationwhileonthistransport": "Whether riding it reveals the area on the map.",
    "ExpireDuration": "How long the flag stays before it expires, in milliseconds.",
    "RespawnTime": "How long the flag waits before it respawns, in milliseconds.",
    "CaptureTime": "How long the capture takes, in milliseconds.",
    "minSuperiority": "The smallest player-count advantage that can capture the point.",
    "maxSuperiority": "The largest player-count advantage that still captures the point.",
    "minTime": "The shortest time a capture can take.",
    "maxTime": "The longest time a capture can take.",
    "startingValue": "The capture progress the point starts with, in percent.",
    "neutralPercent": "The capture progress at which the point becomes neutral, in percent.",
    "killbonustime": "The extra capture time awarded for kills, in percent.",
    "UncontestedTime": "How long the point must be uncontested before it changes state.",
    "SpawnMap": "The map ID where it spawns.",
    "transportPhysics": "The TransportPhysics.dbc ID that controls the transport's movement.",
    "PlayerChoice": "The PlayerChoice.db2 ID used by this object.",
    "MawPowerFilter": "The MawPowerFilter.db2 ID used by this object.",
    "CustomItemBonusFilter": "The custom item bonus filter applied to the reward.",
    "UiMapID": "The UiMap.db2 ID used by this object.",
    "GarrTalentTreeID": "The garrison talent tree shown by this object.",
    "TrophyTypeID": "The garrison trophy type shown by this object.",
    "TrophyInstanceID": "The garrison trophy instance shown by this object.",
    "ShipmentContainer": "The garrison shipment container used by this object.",
    "CreatureDisplayInfo": "The CreatureDisplayInfo.db2 ID of the client creature.",
    "AnimKit": "The AnimKit.db2 ID played by the client object.",
    "Item": "The Item.db2 ID shown by the client object.",
    "WhenAvailable": "The GameObjectDisplay ID used when the object is available.",
    "Script": "The SpellScript.db2 ID used by this object.",
    "taxiPathID": "The TaxiPath.dbc ID the transport follows.",
    "focusId": "The SpellFocusObject.dbc ID that identifies this focus.",
    "dist": "How close a player must be for the focus to count.",
    "spellFocusType": "The SpellFocusObject.dbc ID for this focus type.",
    "spellFocusType2": "The SpellFocusObject.dbc ID for the second focus type.",
    "spellFocusType3": "The SpellFocusObject.dbc ID for the third focus type.",
    "spellFocusType4": "The SpellFocusObject.dbc ID for the fourth focus type.",
    "AreaNameSet": "The area name set index used by the client for this map object.",
    "DoodadSetA": "The first doodad set index used by the client for this map object.",
    "DoodadSetB": "The second doodad set index used by the client for this map object.",
    "DoodadSetC": "The third doodad set index used by the client for this map object.",
    "ExpansionLevel": "The expansion level required to see or use this object.",
    "AssistActionType": "The assist action type; it decides which action the object offers.",
    "InitialDamage": "The damage state the siegeable object starts in.",
}



# Type-specific clarifications for the fields whose meaning depends on the type.
TYPE_FIELD_TIPS = {
    ("GAMEOBJECT_TYPE_CHEST", "chestLoot"): "The loot table rolled when the chest is opened. 0 means it drops nothing.",
    ("GAMEOBJECT_TYPE_CHEST", "chestRestockTime"): "How many seconds before a looted chest can be looted again.",
    ("GAMEOBJECT_TYPE_CHEST", "minRestock"): "The shortest time in seconds before the chest restocks.",
    ("GAMEOBJECT_TYPE_CHEST", "maxRestock"): "The longest time in seconds before the chest restocks.",
    ("GAMEOBJECT_TYPE_CHEST", "xpLevel"): "The level used to calculate the experience the chest awards.",
    ("GAMEOBJECT_TYPE_CHEST", "xpDifficulty"): "The experience tier awarded for looting; it controls how much XP the chest gives.",
    ("GAMEOBJECT_TYPE_CHEST", "ChestFlags"): "A bitmask of chest behaviors such as consumable, group loot and floating tooltip. The raw value keeps every bit.",
    ("GAMEOBJECT_TYPE_CHEST", "OneTimeChestCharacterFlag"): "The player data flag that marks this chest as looted for the character.",
    ("GAMEOBJECT_TYPE_CHEST", "OneTimeChestAccountFlag"): "The player data flag that marks this chest as looted for the account.",
    ("GAMEOBJECT_TYPE_CHEST", "trivialSkillLow"): "The lowest gathering skill at which looting this chest is trivial.",
    ("GAMEOBJECT_TYPE_CHEST", "trivialSkillHigh"): "The highest gathering skill at which looting this chest is trivial.",
    ("GAMEOBJECT_TYPE_CHEST", "DungeonEncounter"): "The dungeon encounter this chest belongs to, used by the encounter journal and loot rules.",
    ("GAMEOBJECT_TYPE_CHEST", "chestPersonalLoot"): "The personal loot table rolled for each player who opens the chest.",
    ("GAMEOBJECT_TYPE_CHEST", "chestPushLoot"): "The push-loot table rolled when the chest is opened.",
    ("GAMEOBJECT_TYPE_CHEST", "spell"): "A spell cast on the player who opens the chest.",
    ("GAMEOBJECT_TYPE_CHEST", "SpawnVignette"): "The vignette shown in the world when the chest spawns.",
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "radius"): "How close the bobber must land to the hole for a catch.",
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "chestLoot"): "The loot table rolled when a fish is caught. 0 means no loot.",
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "minRestock"): "The shortest time in seconds before the fishing hole restocks.",
    ("GAMEOBJECT_TYPE_FISHINGHOLE", "maxRestock"): "The longest time in seconds before the fishing hole restocks.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "chestLoot"): "The loot table rolled when the node is gathered. 0 means no loot.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "trivialSkillLow"): "The lowest gathering skill at which this node is trivial.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "trivialSkillHigh"): "The highest gathering skill at which this node is trivial.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "ObjectDespawnDelay"): "How many seconds after being gathered the node despawns.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "MaxNumberofLoots"): "How many times the node can be gathered before it despawns.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "xpDifficulty"): "The experience tier awarded for gathering the node.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "PlayOpenAnimationonOpening"): "Whether the node plays its opening animation when gathered.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "turnpersonallootsecurityoff"): "Whether personal loot security is disabled for this node.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "ClearObjectVignetteonOpening"): "Whether the node's vignette is cleared when it is gathered.",
    ("GAMEOBJECT_TYPE_GATHERING_NODE", "Overrideminimaptrackingicon"): "The minimap tracking icon override shown for this node.",
    ("GAMEOBJECT_TYPE_TRAP", "charges"): "How many times the trap can trigger before it is used up. -1 often means unlimited.",
    ("GAMEOBJECT_TYPE_TRAP", "cooldown"): "How many seconds the trap waits between activations.",
    ("GAMEOBJECT_TYPE_TRAP", "TriggerCondition"): "The PlayerCondition.dbc ID that must pass before the trap can trigger. 0 means no condition.",
    ("GAMEOBJECT_TYPE_TRAP", "Checkallunits"): "Whether the trap checks all units instead of only players.",
    ("GAMEOBJECT_TYPE_TRAP", "SummonerTriggered"): "Whether only the summoner can trigger the trap.",
    ("GAMEOBJECT_TYPE_GOOBER", "AllowMultiInteract"): "Whether several players can use it at the same time.",
    ("GAMEOBJECT_TYPE_GOOBER", "DontPlayOpenAnim"): "Whether it skips its opening animation.",
    ("GAMEOBJECT_TYPE_GOOBER", "IgnoreBoundingBox"): "Whether interaction ignores the model's bounding box.",
    ("GAMEOBJECT_TYPE_GOOBER", "SortFarZ"): "Whether the client sorts the object with far-Z objects.",
    ("GAMEOBJECT_TYPE_GOOBER", "SyncAnimationtoObjectLifetime"): "Whether the animation is synchronized with the object's lifetime.",
    ("GAMEOBJECT_TYPE_GOOBER", "NoFuzzyHit"): "Whether the client requires an exact hit instead of a fuzzy one.",
    ("GAMEOBJECT_TYPE_TRANSPORT", "SpawnMap"): "The map ID where the transport spawns.",
    ("GAMEOBJECT_TYPE_TRANSPORT", "transportPhysics"): "The TransportPhysics.dbc ID that controls the transport's movement.",
    ("GAMEOBJECT_TYPE_MAP_OBJ_TRANSPORT", "SpawnMap"): "The map ID where the transport spawns.",
    ("GAMEOBJECT_TYPE_MAP_OBJ_TRANSPORT", "transportPhysics"): "The TransportPhysics.dbc ID that controls the transport's movement.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "minSuperiority"): "The smallest player-count advantage that can capture the point.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "maxSuperiority"): "The largest player-count advantage that still captures the point.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "minTime"): "The shortest time a capture can take, in seconds.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "maxTime"): "The longest time a capture can take, in seconds.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "startingValue"): "The capture progress the point starts with, in percent.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "neutralPercent"): "The capture progress at which the point becomes neutral, in percent.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "killbonustime"): "The extra capture time awarded for kills, in percent.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "UncontestedTime"): "How long the point must be uncontested before it changes state, in seconds.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "FrequentHeartbeat"): "Whether the control zone updates its state more often.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "EnablingWorldStateExpression"): "The world state expression that enables the point. 0 means always enabled.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "worldState1"): "The world state set while the zone is neutral.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "worldstate2"): "The world state set while the zone is contested.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "worldstate3"): "The world state set while the zone is controlled.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "CaptureEventHorde"): "The game event started when the Horde captures the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "CaptureEventAlliance"): "The game event started when the Alliance captures the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "ContestedEventHorde"): "The game event started when the Horde contests the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "ContestedEventAlliance"): "The game event started when the Alliance contests the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "ProgressEventHorde"): "The game event started while the Horde is capturing the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "ProgressEventAlliance"): "The game event started while the Alliance is capturing the zone.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "NeutralEventHorde"): "The game event started when the zone becomes neutral for the Horde.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "NeutralEventAlliance"): "The game event started when the zone becomes neutral for the Alliance.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "speedWorldState1"): "The world state that controls the Horde's capture speed.",
    ("GAMEOBJECT_TYPE_CONTROL_ZONE", "speedWorldState2"): "The world state that controls the Alliance's capture speed.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "CaptureTime"): "How long the capture takes, in milliseconds.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "AssaultBroadcastHorde"): "The broadcast text shown when the Horde starts capturing.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "CaptureBroadcastHorde"): "The broadcast text shown when the Horde captures the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "DefendedBroadcastHorde"): "The broadcast text shown when the Horde defends the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "AssaultBroadcastAlliance"): "The broadcast text shown when the Alliance starts capturing.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "CaptureBroadcastAlliance"): "The broadcast text shown when the Alliance captures the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "DefendedBroadcastAlliance"): "The broadcast text shown when the Alliance defends the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "ContestedEventHorde"): "The game event started when the Horde contests the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "CaptureEventHorde"): "The game event started when the Horde captures the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "DefendedEventHorde"): "The game event started when the Horde defends the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "ContestedEventAlliance"): "The game event started when the Alliance contests the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "CaptureEventAlliance"): "The game event started when the Alliance captures the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "DefendedEventAlliance"): "The game event started when the Alliance defends the point.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "SpellVisual1"): "The spell visual played at capture stage 1.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "SpellVisual2"): "The spell visual played at capture stage 2.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "SpellVisual3"): "The spell visual played at capture stage 3.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "SpellVisual4"): "The spell visual played at capture stage 4.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "SpellVisual5"): "The spell visual played at capture stage 5.",
    ("GAMEOBJECT_TYPE_CAPTURE_POINT", "worldState1"): "The world state that tracks the point's owner.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "CreditProxyCreature"): "The creature that receives kill credit for destroying the building.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "HealthRec"): "The destructible hitpoint record that defines the building's health.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "DestructibleModelRec"): "The destructible model record used for the damaged and destroyed states.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "IntactEvent"): "The game event started while the building is intact.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "DamagedEvent"): "The game event started when the building becomes damaged.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "DestroyedEvent"): "The game event started when the building is destroyed.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "RebuildingTime"): "How many seconds the building takes to rebuild.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "RebuildingEvent"): "The game event started while the building rebuilds.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "DamageEvent"): "The game event started when the building takes damage.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "Thexoffsetofthedestructiblenameplateifitisenabled"): "The horizontal offset of the destructible nameplate, in hundredths of a yard.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "Theyoffsetofthedestructiblenameplateifitisenabled"): "The vertical offset of the destructible nameplate, in hundredths of a yard.",
    ("GAMEOBJECT_TYPE_DESTRUCTIBLE_BUILDING", "Thezoffsetofthedestructiblenameplateifitisenabled"): "The depth offset of the destructible nameplate, in hundredths of a yard.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "DifficultyNormal"): "The AnimationData entry played for normal difficulty.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "DifficultyHeroic"): "The AnimationData entry played for heroic difficulty.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "DifficultyEpic"): "The AnimationData entry played for epic difficulty.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "DifficultyLegendary"): "The AnimationData entry played for legendary difficulty.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "HeroicAttachment"): "The GameObjectDisplay attachment shown for heroic difficulty.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "ChallengeAttachment"): "The GameObjectDisplay attachment shown for challenge mode.",
    ("GAMEOBJECT_TYPE_DUNGEON_DIFFICULTY", "DifficultyAnimations"): "The animation set played when the selected difficulty changes.",
    ("GAMEOBJECT_TYPE_RITUAL", "casters"): "How many players must take part in the ritual.",
    ("GAMEOBJECT_TYPE_RITUAL", "casterTargetSpellTargets"): "How many targets the ritual's target spell affects.",
    ("GAMEOBJECT_TYPE_RITUAL", "Allowunfriendlycrossfactionpartymemberstocollaborateonaritual"): "Whether hostile cross-faction party members can help with the ritual.",
    ("GAMEOBJECT_TYPE_CHAIR", "chairslots"): "How many players can sit at the chair at once.",
    ("GAMEOBJECT_TYPE_CHAIR", "chairheight"): "The height of the seat.",
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "CustomSitHeightOffset"): "The vertical seat offset in inches.",
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "CustomSitAnimKit"): "The AnimKit played while a player sits in the chair.",
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "CustomizationFeatureMask"): "A bitmask of the barber-shop customization features available at this chair.",
    ("GAMEOBJECT_TYPE_BARBER_CHAIR", "Preventteleportingtheplayeroutofthebarbershopchair"): "Whether players are prevented from teleporting out of the barber chair.",
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "focusId"): "The SpellFocusObject.dbc ID that identifies this focus.",
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "dist"): "How close a player must be for the focus to count.",
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "spellFocusType2"): "The SpellFocusObject.dbc ID for the second focus type.",
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "spellFocusType3"): "The SpellFocusObject.dbc ID for the third focus type.",
    ("GAMEOBJECT_TYPE_SPELL_FOCUS", "spellFocusType4"): "The SpellFocusObject.dbc ID for the fourth focus type.",
    ("GAMEOBJECT_TYPE_FLAGSTAND", "pickupSpell"): "The spell cast when a player picks up the flag.",
    ("GAMEOBJECT_TYPE_FLAGSTAND", "returnAura"): "The aura applied while a player carries the flag.",
    ("GAMEOBJECT_TYPE_FLAGSTAND", "returnSpell"): "The spell cast when the flag is returned.",
    ("GAMEOBJECT_TYPE_FLAGDROP", "pickupSpell"): "The spell cast when a player picks up the flag.",
    ("GAMEOBJECT_TYPE_FLAGDROP", "ExpireDuration"): "How long the flag stays before it expires, in milliseconds.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "pickupSpell"): "The spell cast when a player picks up the flag.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "ExpireDuration"): "How long the flag stays before it expires, in milliseconds.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "RespawnTime"): "How long the flag waits before it respawns, in milliseconds.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "FlagDrop"): "The flag-drop GameObject created when the flag is dropped.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "ExclusiveCategory"): "The battleground category that stops two flags in the same category being carried together.",
    ("GAMEOBJECT_TYPE_NEW_FLAG", "ReturnonDefenderInteract"): "Whether the defender can return the flag by interacting with it.",
    ("GAMEOBJECT_TYPE_MEETINGSTONE", "areaID"): "The AreaTable.dbc ID of the meeting stone's area.",
    ("GAMEOBJECT_TYPE_MEETINGSTONE", "Preventmeetingstonefromtargetinganunfriendlypartymemberoutsideofinstances"): "Whether the stone refuses to target an unfriendly party member outside an instance.",
    ("GAMEOBJECT_TYPE_GUARDPOST", "charges"): "How many creatures the guard post can spawn.",
    ("GAMEOBJECT_TYPE_SPELLCASTER", "charges"): "How many times the spellcaster can cast before it is used up.",
    ("GAMEOBJECT_TYPE_SPELLCASTER", "partyOnly"): "Whether only party or raid members can use it.",
    ("GAMEOBJECT_TYPE_TRAPDOOR", "PathBlockerBump"): "How far the trap door pushes creatures that bump into it.",
    ("GAMEOBJECT_TYPE_TRAPDOOR", "whenToPause"): "When the trap door pauses during its animation.",
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "auraID1"): "The first aura applied to players in the radius.",
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "auraID2"): "The second aura applied to players in the radius.",
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "conditionID1"): "The PlayerCondition.dbc ID for the first aura. 0 means no condition.",
    ("GAMEOBJECT_TYPE_AURA_GENERATOR", "conditionID2"): "The PlayerCondition.dbc ID for the second aura. 0 means no condition.",
    ("GAMEOBJECT_TYPE_QUESTGIVER", "questGiver"): "The quest-giver list ID used by the object.",
    ("GAMEOBJECT_TYPE_QUESTGIVER", "gossipID"): "The gossip menu shown when a player talks to it. 0 means no menu.",
    ("GAMEOBJECT_TYPE_QUESTGIVER", "customAnim"): "The animation played while a player interacts with it.",
    ("GAMEOBJECT_TYPE_CAMERA", "camera"): "The CinematicSequences.dbc ID played when it is used.",
    ("GAMEOBJECT_TYPE_CAMERA", "cinematicId"): "The Cinematic.dbc ID played when it is used.",
    ("GAMEOBJECT_TYPE_ASSIST_ACTION", "cooldown"): "How long the assist action waits before it can be used again, in milliseconds.",
}

ACRONYMS = {
    "id": "ID", "ids": "IDs", "los": "LOS", "ok": "OK", "npc": "NPC", "ai": "AI", "xp": "XP",
    "pvp": "PvP", "wmo": "WMO", "m2": "M2", "dbc": "DBC", "db": "DB", "gm": "GM",
    "dmg": "Damage", "min": "Minimum", "max": "Maximum", "anim": "Animation",
}

FIELD_LABELS = {
    "autoCloseTime": "Auto-close time",
    "autoClose": "Auto-close",
    "startOpen": "Starts open",
    "lockId": "Lock",
    "open": "Lock",
    "chestLoot": "Loot table",
    "minRestock": "Minimum restock time",
    "maxRestock": "Maximum restock time",
    "xpLevel": "Experience level",
    "xpDifficulty": "Experience difficulty",
    "ObjectDespawnDelay": "Despawn delay",
    "MaxNumberofLoots": "Maximum loots",
    "trivialSkillLow": "Trivial skill low",
    "trivialSkillHigh": "Trivial skill high",
    "UILinkType": "UI link type",
    "PlayerInteractionType": "Player interaction type",
    "lootId": "Loot table",
    "questID": "Quest",
    "questId": "Quest",
    "spellId": "Spell",
    "spellID": "Spell",
    "eventId": "Event",
    "eventID": "Event",
    "gossipID": "Gossip menu",
    "openTextID": "Open text",
    "closeTextID": "Close text",
    "linkedTrap": "Linked trap",
    "linkedTrapId": "Linked trap",
    "pageID": "Page",
    "pageId": "Page",
    "pageMaterial": "Page material",
    "language": "Language",
    "level": "Level",
    "radius": "Radius",
    "cooldown": "Cooldown",
    "startDelay": "Start delay",
    "customAnim": "Custom animation",
    "allowMounted": "Usable while mounted",
    "noDamageImmune": "Damage does not interrupt",
    "losOK": "Requires line of sight",
    "serverOnly": "Server only",
    "large": "Large",
    "floatingTooltip": "Floating tooltip",
    "highlight": "Highlight",
    "consumable": "Consumable",
    "leaveLoot": "Leave loot",
    "notInCombat": "Only out of combat",
    "logLoot": "Log loot",
    "groupLootRules": "Group loot rules",
    "minSuccessOpens": "Minimum opens",
    "maxSuccessOpens": "Maximum opens",
    "chestRestockTime": "Restock time",
    "diameter": "Trigger diameter",
    "focusId": "Focus",
    "dist": "Distance",
    "reqParticipants": "Required participants",
    "animSpell": "Animation spell",
    "ritualPersistent": "Persistent",
    "casterTargetSpell": "Caster target spell",
    "casterTargetSpellTargets": "Caster target spell targets",
    "castersGrouped": "Casters grouped",
    "ritualNoTargetCheck": "No target check",
    "creatureID": "Creature",
    "charges": "Charges",
    "partyOnly": "Party only",
    "minLevel": "Minimum level",
    "maxLevel": "Maximum level",
    "areaID": "Area",
    "pickupSpell": "Pickup spell",
    "returnAura": "Return aura",
    "returnSpell": "Return spell",
    "gameType": "Game type",
    "worldState1": "World state 1",
    "worldstate2": "World state 2",
    "worldstate3": "World state 3",
    "winEventID1": "Win event 1",
    "winEventID2": "Win event 2",
    "contestedEventID1": "Contested event 1",
    "contestedEventID2": "Contested event 2",
    "progressEventID1": "Progress event 1",
    "progressEventID2": "Progress event 2",
    "neutralEventID1": "Neutral event 1",
    "neutralEventID2": "Neutral event 2",
    "neutralPercent": "Neutral percent",
    "minSuperiority": "Minimum superiority",
    "maxSuperiority": "Maximum superiority",
    "minTime": "Minimum time",
    "maxTime": "Maximum time",
    "startingValue": "Starting value",
    "unidirectional": "Unidirectional",
    "auraID1": "Aura 1",
    "auraID2": "Aura 2",
    "conditionID1": "Condition 1",
    "conditionID2": "Condition 2",
    "difficulty": "Difficulty",
    "chairheight": "Seat height",
    "heightOffset": "Height offset",
    "intactNumHits": "Intact hits",
    "creditProxyCreature": "Credit proxy creature",
    "state1Name": "State 1 name",
    "intactEvent": "Intact event",
    "damagedDisplayId": "Damaged display",
    "damagedNumHits": "Damaged hits",
    "damagedEvent": "Damaged event",
    "destroyedDisplayId": "Destroyed display",
    "destroyedEvent": "Destroyed event",
    "debuildingTimeSecs": "Rebuild time",
    "destructibleData": "Destructible data",
    "rebuildingEvent": "Rebuilding event",
    "damageEvent": "Damage event",
    "whenToPause": "Pause when",
}


def cs(text):
    return '"' + str(text).replace('\\', '\\\\').replace('"', '\\"') + '"'


def friendly(code):
    code = code.lstrip('_')
    if code in ACRONYMS:
        return ACRONYMS[code]
    words = []
    for part in re.split(r'[_]+', code):
        # split camel case, keeping runs of capitals together
        pieces = re.findall(r'[A-Z]+(?=[A-Z][a-z]|\d|\b)|[A-Z]?[a-z]+|[A-Z]+|\d+', part)
        for piece in pieces or [part]:
            low = piece.lower()
            words.append(ACRONYMS.get(low, low))
    return ' '.join(words[:1] + [w if w.isupper() or any(c.isupper() for c in w[1:]) else w for w in words[1:]]) if words else code


def title(text):
    if not text:
        return text
    return text[0].upper() + text[1:]


def clean_comment(comment):
    c = comment.strip()
    c = re.sub(r'^\d+\s*', '', c)
    c = c.lstrip('->- ').strip()
    if not c:
        return ""
    c = c.replace('recieve', 'receive')
    c = c[0].upper() + c[1:]
    if not c.endswith('.'):
        c += '.'
    return c


def parse_shared_types(path):
    h = open(path, encoding='utf-8', errors='replace').read()
    m = re.search(r'enum\s+(?:class\s+)?GameobjectTypes[^{]*\{(.*?)\};', h, re.S)
    if not m:
        raise SystemExit(f'GameobjectTypes enum not found in {path}')
    types = []
    for line in m.group(1).splitlines():
        fm = re.match(r'\s*(GAMEOBJECT_TYPE_\w+)\s*=\s*(\d+)', line)
        if fm:
            types.append((int(fm.group(2)), fm.group(1)))
    return sorted(types)


def parse_structs(path):
    h = open(path, encoding='utf-8', errors='replace').read()
    type_re = re.compile(r'//\s*(\d+)\s+(GAMEOBJECT_TYPE_\w+)(?:\s*-\s*empty)?')
    field_re = re.compile(r'\s*(u?int(?:8|16|32|64)?|float|double|std::string)\s+(\w+)\s*;\s*//\s*(\d+)\s*(.*)$')
    types = {}
    cur = None
    fields = []
    for line in h.splitlines():
        m = type_re.search(line)
        if m:
            if cur is not None:
                types[cur[1]] = (cur[0], fields)
            cur = (int(m.group(1)), m.group(2))
            fields = []
            continue
        if cur is not None:
            fm = field_re.match(line)
            if fm:
                fields.append((int(fm.group(3)), fm.group(2), fm.group(4).strip(), fm.group(1)))
    if cur is not None:
        types[cur[1]] = (cur[0], fields)
    return types


def type_name(type_code):
    return (TYPE_NAMES.get(type_code) or title(friendly(type_code.replace('GAMEOBJECT_TYPE_', '')))).lower()


def add_meta(tip, minimum, maximum, default, scale):
    if maximum is not None and 'Range ' not in tip:
        low = 0 if minimum is None else minimum
        tip += f" Range {low}–{maximum}."
    if default and 'Default ' not in tip:
        tip += f" Default {default}."
    if scale != 1 and 'stored value' not in tip:
        tip += f" The stored value is {scale:g} times the displayed value."
    return tip


def tip_for(type_code, code, label, kind, ref, minimum, maximum, default, scale, unused, options=None):
    name = type_name(type_code)
    if unused:
        return f"This column is not read by the {name} type; the stored value is preserved."
    if (type_code, code) in TYPE_FIELD_TIPS:
        return add_meta(TYPE_FIELD_TIPS[(type_code, code)], minimum, maximum, default, scale)
    if code in COMMON_TIPS:
        return add_meta(COMMON_TIPS[code], minimum, maximum, default, scale)
    if kind == 'b':
        return add_meta(f"Turns the \"{label}\" behavior on or off for this {name}.", minimum, maximum, default, scale)
    if kind == 'r':
        noun = label if label.lower().endswith("id") else label + " ID"
        return add_meta(f"The {noun} for this {name}. It references {ref}; 0 means none.", minimum, maximum, default, scale)
    if kind == 'c':
        values = [value for _, value in (options or [])]
        if len(values) > 8:
            values = values[:8] + ['…']
        return add_meta(f"The {label} for this {name}. Allowed values: {', '.join(values)}.", minimum, maximum, default, scale)
    tip = f"The {label} for this {name}"
    if kind == 'm':
        tip += ", in milliseconds."
    else:
        tip += "."
    return add_meta(tip, minimum, maximum, default, scale)


def parse_tc_field(index, code, comment, type_code):
    raw_label = comment.split(',')[0].strip()
    # A raw label that is just the identifier reads better through FIELD_LABELS; a
    # descriptive label with spaces or parentheses is kept as the server wrote it.
    if code in FIELD_LABELS:
        label = FIELD_LABELS[code]
    elif raw_label == code or (not any(c.isupper() for c in raw_label[1:]) and ' ' not in raw_label and '(' not in raw_label):
        label = title(friendly(code))
    else:
        label = title(raw_label)
    kind = 'n'
    default = 0
    ref = None
    options = None
    minimum = None
    maximum = None
    scale = 1
    unused = raw_label.lower().startswith('unused') or code.lower().startswith('unused')

    em = re.search(r'enum\s*\{([^}]*)\}', comment)
    if em:
        values = [v.strip() for v in em.group(1).split(',') if v.strip()]
        if values and all(v in ('false', 'true') for v in values):
            kind = 'b'
            default = 1 if re.search(r'Default:\s*true\b', comment) else 0
        elif values:
            kind = 'c'
            options = list(enumerate(values))
            dm = re.search(r'Default:\s*([^,]+)', comment)
            if dm and dm.group(1).strip() in values:
                default = values.index(dm.group(1).strip())
    rm = re.search(r'References:\s*([\w_]+)', comment)
    if rm:
        kind = 'r'
        ref = REFERENCE_NAMES.get(rm.group(1), rm.group(1))
        # A reference's zero is normally "none"; keep the parsed default if present.
    mm = re.search(r'Min value:\s*(-?\d+)', comment)
    if mm:
        minimum = int(mm.group(1))
    elif kind == 'n':
        minimum = 0
    mx = re.search(r'Max value:\s*(-?\d+)', comment)
    if mx:
        v = int(mx.group(1))
        maximum = None if v >= 2147483647 else v
    dm = re.search(r'Default value:\s*(-?\d+)', comment)
    if dm:
        default = int(dm.group(1))
    if '(ms)' in raw_label and kind == 'n':
        kind = 'm'
    if raw_label.lower().startswith('unused'):
        unused = True
    tip = tip_for(type_code, code, label, kind, ref, minimum, maximum, default, scale, unused, options)
    return dict(index=index, code=code, label=label, tip=tip, kind=kind, default=default,
                ref=ref, options=options, minimum=minimum, maximum=maximum, scale=scale, unused=unused)


def infer_ac_kind(code, comment):
    if code in BOOL_CODES:
        return 'b', None
    if code.startswith('empty') or code.lower().startswith('unused'):
        return 'n', None
    lower = code.lower()
    if 'lock' in lower or '-> Lock.dbc' in comment:
        return 'r', 'Lock'
    if code in ('openTextID', 'closeTextID') or 'castBarCaption' in comment:
        return 'r', 'BroadcastText'
    if code in ('linkedTrap', 'linkedTrapId'):
        return 'r', 'GameObject'
    if code in ('eventId', 'eventID'):
        return 'r', 'GameEvent'
    if code in ('pageID', 'pageId'):
        return 'r', 'PageText'
    if code == 'pageMaterial':
        return 'r', 'PageTextMaterial'
    if code == 'language':
        return 'r', 'Language'
    if code in ('areaID', 'areaId'):
        return 'r', 'Area'
    if code in ('mapID', 'mapId'):
        return 'r', 'Map'
    if code in ('cinematicId', 'cinematicID'):
        return 'r', 'Cinematic'
    if code in ('creatureID', 'creatureId'):
        return 'r', 'Creature'
    if code in ('questID', 'questId'):
        return 'r', 'Quest'
    if 'spell' in lower:
        return 'r', 'Spell'
    if 'loot' in lower:
        return 'r', 'Loot'
    if 'gossip' in lower:
        return 'r', 'Gossip'
    return 'n', None


def parse_ac_field(index, code, comment, ctype, type_code):
    if (type_code, code) in AC_OVERLAY:
        return None  # handled by the overlay
    kind, ref = infer_ac_kind(code, comment)
    minimum = 0 if ctype.startswith('uint') else None
    label = FIELD_LABELS.get(code, title(friendly(code)))
    unused = code.startswith('empty') or code.lower().startswith('unused')
    tip = clean_comment(comment) or tip_for(type_code, code, label, kind, ref, minimum, None, 0, 1, unused)
    return dict(index=index, code=code, label=label, tip=tip, kind=kind, default=0,
                ref=ref, options=None, minimum=minimum, maximum=None, scale=1, unused=unused)


def build_fields(parsed_fields, trinity, type_code):
    fields = []
    for index, code, comment, ctype in parsed_fields:
        if trinity:
            f = parse_tc_field(index, code, comment, type_code)
        else:
            f = parse_ac_field(index, code, comment, ctype, type_code)
            if f is None:
                ov = AC_OVERLAY[(type_code, code)]
                label = ov.get('label', title(friendly(code)))
                kind = ov.get('kind', 'n')
                ref = ov.get('reference')
                options = ov.get('options')
                minimum = ov.get('minimum', 0 if ctype.startswith('uint') else None)
                maximum = ov.get('maximum')
                default = ov.get('default', 0)
                scale = ov.get('scale', 1)
                unused = code.startswith('empty')
                tip = ov.get('tip') or tip_for(type_code, code, label, kind, ref, minimum, maximum, default, scale, unused, options)
                f = dict(index=index, code=code, label=label, tip=tip, kind=kind,
                         default=default, ref=ref, options=options,
                         minimum=minimum, maximum=maximum, scale=scale, unused=unused)
        fields.append(f)
    fields.sort(key=lambda f: f['index'])
    # Merge duplicate indexes (should not happen) by keeping the first.
    seen = set()
    unique = []
    for f in fields:
        if f['index'] not in seen:
            seen.add(f['index'])
            unique.append(f)
    return unique


def emit_type(type_id, code, fields, trinity):
    name = TYPE_NAMES.get(code) or title(friendly(code.replace('GAMEOBJECT_TYPE_', '')))
    hint = TYPE_HINTS.get(code, '')
    lines = [f'    new({type_id}, {cs(code)}, {cs(name)}, {cs(hint)},', '    [']
    for f in fields:
        args = [str(f['index']), cs(f['code']), cs(f['label']), cs(f['tip']), 'GameObjectFieldKind.' + {
            'b': 'Bool', 'c': 'Choice', 'm': 'Milliseconds', 'r': 'Reference', 'n': 'Number',
        }[f['kind']]]
        if f['minimum'] is not None:
            args.append("Min: int.MinValue" if f['minimum'] == -2147483648 else f"Min: {f['minimum']}")
        if f['maximum'] is not None:
            args.append(f"Max: {f['maximum']}")
        if f['default']:
            args.append(f"Default: {f['default']}")
        if f['options']:
            args.append('Options: [' + ', '.join(f'({v}, {cs(n)})' for v, n in f['options']) + ']')
        if f['ref']:
            args.append(f'Reference: {cs(f["ref"])}')
        if f['scale'] != 1:
            args.append(f'Scale: {f["scale"]}')
        if f['unused']:
            args.append('Unused: true')
        lines.append('        new(' + ', '.join(args) + '),')
    lines.append('    ]),')
    return '\n'.join(lines)


def generate(names, structs, dialect, class_name, trinity):
    types = []
    for type_id, code in names:
        parsed = structs.get(code, (type_id, []))[1]
        fields = build_fields(parsed, trinity, code)
        types.append(emit_type(type_id, code, fields, trinity))
    head = f'''namespace ACWorldEdit.Foundation;

/// <summary>
/// GameObject type and Data field catalogue generated from {dialect} `GameObjectData.h`
/// and `SharedDefines.h`. Regenerate with Tools/generate_gameobject_catalog.py when the
/// server headers change. The physical column signedness comes from the table schema;
/// this catalogue supplies the label, kind, range, default and reference.
/// </summary>
public static class {class_name}
{{
    public static readonly GameObjectType[] Types =
    [
'''
    return head + '\n'.join(types) + '\n    ];\n}\n'


def main():
    args = [a for a in sys.argv[1:] if a != '--trinity']
    trinity = '--trinity' in sys.argv
    if trinity:
        if len(args) != 5:
            raise SystemExit('usage: --trinity <tc GameObjectData.h> <tc SharedDefines.h> <out.cs> <ac GameObjectData.h> <ac SharedDefines.h>')
        data, shared, out, _ac_data, _ac_shared = args
        names = parse_shared_types(shared)
        structs = parse_structs(data)
        text = generate(names, structs, 'TrinityCore master', 'TcGameObjectTypes', True)
    else:
        if len(args) != 3:
            raise SystemExit('usage: <GameObjectData.h> <SharedDefines.h> <out.cs>')
        data, shared, out = args
        names = parse_shared_types(shared)
        structs = parse_structs(data)
        text = generate(names, structs, 'AzerothCore', 'AcGameObjectTypes', False)
    open(out, 'w', encoding='utf-8', newline='\n').write(text)
    print(f'{out}: {len(names)} types, {sum(len(structs.get(c, (0, []))[1]) for _, c in names)} fields')


if __name__ == '__main__':
    main()
