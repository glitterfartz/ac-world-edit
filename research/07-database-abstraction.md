# Typed content model, SQL compilation and reversible changes

## Verified schema constraints
Test world DB version ACDB 335.17-dev matches local revision prefix. Relevant tables are InnoDB. creature stores guid/id, not a fixed list of model or alternate-template columns. Appearance rows use creature_template_model(CreatureID,Idx). gameobject stores orientation plus rotation0..3. Conditions have a ten-column compound primary key; SmartAI PK includes entryorguid/source_type/id/link. Only creature_sparring declares a world foreign key in the query result. SQL types and column key metadata are saved in references. [D1](references/source-references.md#d1-read-only-test-database-evidence)

Do not rely on MySQL to validate most content relationships. Zero creature-template orphans were found, but that single audit does not certify the rest of the database.

## Three models
1. **Persistence RowBag:** exact original column names/types/values/nulls and PK; preserves unsupported columns.
2. **Editor aggregate:** normalized semantic properties and references, e.g. NpcDefinition.AppearanceChoices, Placement.InheritedFlags, Quest.Objectives.
3. **Change plan:** explicit inserts/updates/deletes with row ownership, dependencies, baseline preconditions and deployment requirements.

Separating these prevents lossy reads and avoids turning every table into a user-facing panel.

## Aggregate ownership
| Aggregate | Owned rows, when created by editor | Referenced/shared rows |
|---|---|---|
| Ordinary creature spawn | creature plus its explicit addon/override rows | template, shared patrol, events/pools |
| Creature definition | template and authored model/extra rows | shared loot/services/gossip/spells/faction |
| Quest | base/addon/text/locale/POI rows; selected relation rows | item/NPC/GO definitions, other quests |
| Patrol route | waypoint_data and authored spline/script rows | one or more spawn/addon owners |
| SmartAI behavior | complete owner/source block and authored timed lists/conditions/text | spells/targets, shared lists/text/references |
| Encounter project | Editor ownership manifest only | All mapped aggregates and core instance scripting |
| GO definition | template and authored addon | display/lock/spell/loot records |

Ownership is recorded, not inferred solely from matching IDs. Shared records have reverse dependency queries and clone-on-edit. DeletePlan explicitly enumerates removable owned rows, blocked references and preserved shared rows. MVP rejects deletion of complex event/pool/multispawn/script-owned entities until supported.

## Proposed semantic examples
```json
{
  "kind": "CreaturePlacement",
  "stableId": "uuid",
  "template": { "kind": "CreatureDefinition", "key": "2843" },
  "pose": { "map": 0, "x": -14467.8, "y": 468.374, "z": 15.1064, "heading": 0.139626 },
  "availability": { "spawnMask": 1, "phaseMask": 1 },
  "movement": { "kind": "Stationary" },
  "persistence": { "guid": 1, "baselineRowHash": "sha256" }
}
```
Illustrative imported sample, not a new entity or applied edit. NPC flags are semantic bit sets with unknown bits retained. Null and zero are distinct. Signed sentinel values are mapped to tagged variants, not unsigned IDs. GameObject data is a type-specific object with raw field retention. Conditions model OR groups containing AND terms/reference predicates.

## Adapter metadata
Each profile contains:
- Required tables/columns/types/PK and optional capabilities; charset/collation/SQL-mode requirements.
- Semantic reference resolvers and source-supported enum/flag metadata.
- Default/inheritance rules, deletion policies, ID namespaces/ranges and compound owner keys.
- Read-set queries, normalization mappings, SQL emission policy and deployment effects.
- Pinned core revision/schema fingerprint and supported client build.

Fingerprint columns/types/null/default/keys plus relevant version/update context. A matching table name alone is insufficient. Additive unknown fields can be preserved for existing rows; new writes need explicit policy. Missing/incompatible fields disable writing for that aggregate, with a diagnostic. Do not guess a mapping based on a column's ordinal.

## ID allocation
Use UUIDs in drafts, symbolic IDs in plans. Export policy selects operator-owned numeric ranges by namespace: creature templates, creature spawns, GO templates, GO spawns, paths, gossip, quests, loot references, timed lists. The schema generally does not provide an editor-safe atomic auto-ID allocator. MAX(guid)+1 is not a reservation and collides with concurrent authors.

MVP allocation is deterministic against a captured source and configured range, with collision report. Deployment rechecks every chosen ID. Later deployment can serialize project allocation using a dedicated coordination service or explicit operator ownership; do not change core schema for invisible reservations. Across independently authored bundles, merge symbolic IDs first or rebind on export. Rewrite all owned signed SmartAI and path references consistently.

## Read sets and conflict detection
A change carries:
- Write set: exact PK rows or complete owner blocks.
- Read set: referenced definitions, shared-consumer queries and absence predicates needed for new IDs/relationships.
- Baseline raw hash, query fingerprint and source/schema/profile revision.

At export, compare source rows with baseline; classify unchanged/rebasable/conflicting. Three-way merge independent semantic fields where known. SmartAI owner blocks, shared paths, or changes in ownership need explicit review. Floating-point comparisons use original persisted representations; never format to a short human decimal and claim exact equivalence.

Later apply must recheck read and write sets under a transaction with appropriate locks/isolation; absence and dependency queries require phantom protection or a stopped/serialized writer target. Do not claim row locks protect unseen new rows automatically.

## Validation layers
1. Shape/types: SQL width/signedness, finite floats, string byte lengths/charset, required values.
2. Local semantics: heading/masks, movement/radius pairing, normalized authored quaternion, levels/min-max, positive counts.
3. References: map/template/model/item/spell/path/gossip/quest/loot/event exists in effective baseline+draft.
4. Aggregate semantics: inherit versus override, GO type units, trainer requirements, quest slots/prerequisites, condition source keys, SmartAI source/event/target combinations.
5. Workspace conflicts: ID/range collision, shared edit impact, complete dependency read set.
6. Runtime/artifacts: reload/restart requirements, missing client assets, geometry build mismatch.

Validation distinguishes errors from warnings and unverified runtime behavior. Mirror only well-supported server checks; cite the loader/symbol/version in diagnostics. Unsupported scripts can be referenced without the editor claiming to validate their execution.

## Command transaction versus database transaction
A UI command validates and updates the whole aggregate atomically in memory, produces inverse state, and appends one journal entry. Editing a patrol attaches addon and route as one command. Undo after export edits the draft; it does not revoke deployed SQL. Reverting deployed content requires a new compensating plan against current target, especially where other work has intervened.

## SQL emission policy
**Proposal: two export modes.**
- Upstream pending: obey local .agents/docs/sql-guidelines.md, pending create_sql convention, delete-before-insert and linter requirements. Spawn UPDATE/DELETE predicates include both id and guid and target the table alone. SmartAI rewrites the complete (entryorguid,source_type) block.
- Custom/module: use ordinary supported SQL in configured custom/module paths, still with bounded predicates and stable ordering. Never edit released/base files or updater rows.

[A4](references/source-references.md#a4-updates-and-extension). Generate explicit column lists, invariant round-trip float literals, correctly escaped strings and explicit NULL. Whitelist identifiers from adapter metadata. Runtime queries are parameterized; offline SQL literals require a tested exporter honoring target SQL mode/charset. No REPLACE into shared tables, broad DELETE by template without ownership checks, disabling FK checks, or global key-renumbering.

Illustrative single-spawn move, not executed:
```sql
UPDATE `creature`
SET `position_x` = -14466.8
WHERE `id` = 2843 AND `guid` = 1;
```
This conforms to bounded spawn targeting but does not independently enforce baseline freshness. Bundle preconditions must be checked by the operator/deployment runner. Plain SQL files cannot universally abort when a SELECT reports a mismatch; distinguish a review/export bundle from an atomic guarded apply tool.

For full-block rewrite: capture every original SmartAI row before delete, serialize every new row after delete, and include dependency state. Unknown unchanged fields survive import/export. A single row edit produces a complete block export per repository rules.

## Bundle specification
```json
{
  "formatVersion": 1,
  "bundleId": "uuid",
  "coreProfile": "azerothcore-335-schema-fingerprint",
  "sourceRevision": "5ddc706b7fdc",
  "clientProfileHash": "sha256",
  "operations": [],
  "readSet": [],
  "writeSet": [],
  "allocatedIds": {},
  "requirements": ["world-db", "worldserver-restart"],
  "files": {"forward.sql": "sha256", "reverse.sql": "sha256"}
}
```
Operation entries include exact owned PKs, raw before/after rows, expected absence for inserts, dependency queries and semantic labels. Reverse SQL restores captured data, with post-state preconditions. It is not a safe unconditional rollback over new unrelated edits.

## Future controlled apply
Prepare target identity/version -> inspect backup -> stop/serialize target content writers -> verify preconditions -> begin DML transaction -> apply with affected-row checks -> commit -> record bundle audit -> restart/reload by capability -> verify runtime behavior. On DML failure rollback; on runtime failure stop testing, inspect and optionally compensate. DDL and filesystem/client deployment are separate, not atomically committed by a MySQL transaction. If commit acknowledgement is lost, inspect exact post-state before retrying.

Upstream SQL can be replayed idempotently where policy requires delete+insert, but replay over newer content can still overwrite changes. Idempotency and concurrency safety are separate guarantees. The existing updater owns migration tracking; an editor runner must not execute and then fabricate updater history. Decide one application path and test its replay behavior in a clone.

