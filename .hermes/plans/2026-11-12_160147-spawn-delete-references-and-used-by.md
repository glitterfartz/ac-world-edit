# Spawn delete removes its dependents; entity pages show "Used by"

## Goal

Let a spawn delete optionally remove every row that names the spawn—in the same undoable command—and add a read-only "Used by" card to the creature and GameObject pages, both built on one reference lookup.

## Delivery slices (read this before starting)

Two independently shippable slices. Do not put them in one PR.

- **Slice A — Used by card (Stage 7)**: read-only, no catalog change, no risk to export or undo. Ship it first if the user wants a quick win.
- **Slice B — Remove these too (Stages 1–6)**: widens the editable catalog by eight tables, which then appear in Pending Changes and SQL export. Every stage below is ordered so the tree builds and the suite is green after each commit.
- **Stage 6 (orphaned waypoint paths) is deferred by default.** It goes beyond the request and touches a fourth table family. Implement it only if the user asks; until then the dialog keeps its honest warning that a path "stays in the database unused", and Stage 5 leaves `removeToo` gated on `references.CanRemove` alone.

## Current context / assumptions

Read from the working tree at commit `9ca5dfd` (clean). Re-check with `git log --oneline -1` before starting. Root `AGENTS.md` and the nested `Foundation/`, `Scripts/` and `Tests/` files were read; the constraints that bind this plan are called out in Stage 8.

**How a delete works today**

- `Scripts/Overlays/Creature/CreatureOverlay.Group.cs:117` `DeleteAsync` reads what references the selection (`CountSpawnReferencesAsync`, `ReadSpawnPathsAsync`), then either deletes at once or opens `SpawnDeleteDialog.Ask`.
- `Scripts/Overlays/GameObject/GameObjectOverlay.Group.cs:114` is the same without paths.
- `Scripts/Ui/Scene/SpawnDeleteDialog.cs` is a 3-line wrapper over `Dialogs.Confirm` (`Scripts/Ui/Framework/Dialogs.cs:40`), which offers only OK and Cancel.
- `Foundation/Spawn/SpawnImpact.cs:4` defines `SpawnImpactItem(SpawnReference Reference, int Count)`; `:39` builds the warning text.
- `Foundation/Spawn/SpawnEdits.cs:134` `Delete(schema, kind, effective, spawn)` emits **only** the spawn row and its addon row. `:149` `References(dialect, kind)` lists the tables that also name the spawn: `smart_scripts` (per-GUID, negated `entryorguid`), `spawn_group`, `linked_respawn` (both `guid` and `linkedGuid`), `pool_members` (TC) or `pool_creature`/`pool_gameobject` (AC), `game_event_creature`/`game_event_gameobject`, `game_event_model_equip` (AC only), `creature_formations` (`leaderGUID` and `memberGUID`, creatures only).
- `Foundation/Spawn/SpawnPoses.Batch.cs:46` `DeleteMany(sender, schema, source, guids, label)` batches `SpawnEdits.Delete` for the selection into one `ContentDrafts.Commit`, i.e. one undo step.
- `Foundation/Spawn/SpawnPoses.cs:44` `Snapshot(string source)` is public and is the per-source database baseline; `:57` `Load` and `:88` `LoadFormationRows` already write non-spawn tables into it. `InteractionDraft.Tables(source)` is public too. `ContentDrafts.Commit` (`Foundation/Drafts/ContentDrafts.cs:107`) is the single exit for a command.

**Why the dependents are not already removed**

- `Foundation/Drafts/InteractionDraft.cs:166` `Execute` requires every edit's `Before` to equal a row in the effective view, and `ValidateEdit` (`:67`) calls `Tables(source).Find(edit.Table)`, which throws `Table X is not editable.` for a table missing from the catalog.
- `Foundation/AzerothCore/AcTables.cs:15` and `Foundation/TrinityCore/TcTables.cs:17` register the editable tables. They **include** `smart_scripts` and `creature_formations` and **exclude** `pool_creature`, `pool_gameobject`, `pool_members`, `spawn_group`, `game_event_creature`, `game_event_gameobject`, `game_event_model_equip` and `linked_respawn`.
- `Scripts/Data/DbSpawnImpact.cs` counts references with raw SQL and never reads rows, so counting works for tables that cannot be edited.
- `AcInteractionSource.Load` (`Scripts/Data/AzerothCore/AcInteractionSource.cs:171`) is already used for both dialects by the shared `Scripts/Data/DbFormations.cs`, so it is safe to reuse from a shared reader despite the name.

**Why "Used by" is missing**

- The only reverse lookup is spawn-GUID scoped (`IDBSource.CountSpawnReferencesAsync`, `Scripts/Data/IDBSource.cs:40`); `CountTemplateUseAsync` (`:64`) counts only spawns and spawns with their own addon. `grep -rn 'FindReferences' --include=*.cs .` returns nothing: `FindReferencesAsync` does not exist.
- `ReadWaypointPathRowsAsync` **does** exist (`Scripts/Data/IDBSource.cs:49`, implemented in both sources), which is what makes Stage 6 possible at all.
- `Scripts/Ui/CreatureProperties/SpawnsSection.cs` and `Scripts/Ui/GameObjectProperties/GameObjectSpawnsSection.cs` are the templates for a page section: `Id`, `Title`, `IconPath`, `Tip`, `Badge`, `HasContent`, `Refresh`, an `async void Load…` guarded by a `HashSet<uint> requested`, and `Ctx.Redraw()` when data arrives. `GameObjectContext.Entry` is `uint?`.
- `CreatureSectionRegistry.Factories` and `GameObjectSectionRegistry.Factories` are the only registration points; `Scripts/Ui/Properties/PropertiesWindow.cs` needs no change.

**Assumptions**

- Read-then-delete is immediate. A dependent row changed in the database between the dialog opening and the answer is not conflict-detected; the commit refuses with `Not saved: <table> <key>: the row changed; reload and retry.` and the user retries. Acceptable.
- Only staged (not-yet-exported) spawns are excluded from the count, as today.
- Both readings are bounded by `MaxGuids = 500`, the existing cap. A delete of more than 500 spawns removes dependents for the first 500 only; the dialog's numbers match what is removed because both come from the same read.
- Per-spawn SmartAI rows are removed when the user picks the third answer. That is the point of the button: the dialog names them.

## Architecture / proposed approach

Keep the existing count query for callers that only warn (`CountSpawnReferencesAsync`) and add one reader that returns **both** per-table counts and the full rows (`ReadSpawnReferencesAsync`) using the same predicates, so the dialog's numbers equal the rows the command deletes. Register the missing tables as editable `TableSchema`s in both dialects, load their rows into the `SpawnPoses` snapshot, and append their `InteractionEdits.Delete` edits to the existing `DeleteMany` commit, so one Undo restores spawn and dependents. For pages, add a second reference list (template entry, counted with raw SQL, no schema needed) rendered by one shared `ReferenceCountCard` hosted by two thin sections.

No new framework beyond one `VBoxContainer` control, no `EntityKind` enum, no changes to `PropertiesWindow`.

## Scope: what the third button removes

| Table | AC | TC | Removed by "Remove these too"? |
| --- | --- | --- | --- |
| `smart_scripts` (`source_type` 0/1, `entryorguid = -guid`) | yes | yes | yes |
| `creature_formations` (`leaderGUID`, `memberGUID`) | yes | yes | yes — the group dissolves, members stay standing |
| `spawn_group` | yes | yes | yes |
| `game_event_creature` / `game_event_gameobject` | yes | yes | yes |
| `linked_respawn` (`guid`, `linkedGuid`) | yes | yes | yes |
| `pool_creature` / `pool_gameobject` | yes | — | yes |
| `pool_members` | — | yes | yes |
| `game_event_model_equip` | yes | — | yes |
| orphaned waypoint path (nodes + TC header) | yes | yes | only if Stage 6 is taken |

Deliberately left behind (see open questions, not silent breakage): an emptied `pool_template`, and a `linked_respawn` partner that simply stops being linked.

---

## Stage 0 — Baseline

```bash
git switch -c spawn-delete-references
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false
```

Expected: `Build succeeded.` with `0 Error(s)`, and the runner's final line reports `Failed: 0` (record the total it prints; do not assume one). `Skipped` may be non-zero when `ACWORLD_DB_INFO` is unset — that is normal for the default suite.

Commit nothing.

---

## Stage 1 — Row and reference plumbing (Foundation only, no UI)

### Task 1.1 `RowSet` can be walked and merged

**Files:** `Foundation/Interaction/InteractionRows.cs`, new `Tests/ACWorldEdit.Tests/Drafts/RowSetTests.cs`.

Use tables that exist **today** (`creature_formations`, `smart_scripts`), so the only red is the missing API:

```csharp
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class RowSetTests
{
    static Row Formation(uint member) => Row.Of(("leaderGUID", 5u), ("memberGUID", member), ("dist", 0f), ("angle", 0f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u));

    [Fact]
    public void RowsCanBeWalkedByNameAndMerged()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureFormations, Formation(5));
        Assert.Equal(["creature_formations"], rows.Names);
        Assert.Single(rows.All("creature_formations"));

        var other = new RowSet();
        other.Set(AcTables.CreatureFormations, Formation(6));
        other.Set(AcTables.SmartScripts, Row.Of(("entryorguid", -5), ("source_type", 0u), ("id", 0u), ("link", 0u)));
        Assert.Empty(rows.All("smart_scripts"));

        rows.Merge(other);
        Assert.Equal(2, rows.All("creature_formations").Count);
        Assert.Single(rows.All("smart_scripts"));
    }
}
```

`AcTables.SmartScripts` has more columns than the four keys, so `Row.Of` with four values would fail `Validate` later. This test never validates—it only stores rows—so the short row is fine; if you prefer, build it with `SpawnEdits`-style full columns. Do **not** call `Validate` here.

Run:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~RowSetTests"
```

Expected: **fails to compile** with `'RowSet' does not contain a definition for 'Names'` / `'Merge'`. A compile failure is the intended red step.

Implement inside `RowSet` (`Foundation/Interaction/InteractionRows.cs`, after `Remove`):

```csharp
    /// <summary>Every table name the set holds rows of, so a caller can walk it without knowing them.</summary>
    public IReadOnlyCollection<string> Names => tables.Keys;

    /// <summary>The rows of a logical table name (empty when it holds none).</summary>
    public IReadOnlyCollection<Row> All(string table) => tables.TryGetValue(table, out var rows) ? rows.Values : [];

    /// <summary>Add another set's rows: a row of the same table and key is replaced.</summary>
    public void Merge(RowSet other)
    {
        foreach (var (name, rows) in other.tables)
        {
            if (!tables.TryGetValue(name, out var mine))
            {
                tables[name] = mine = [];
            }

            foreach (var (key, row) in rows)
            {
                mine[key] = row;
            }
        }
    }
```

Re-run: `Passed!  - Failed: 0, Passed: 1`.

Commit: `feat(drafts): walk and merge row sets`

### Task 1.2 One record for "what still names these spawns"

**File:** `Foundation/Spawn/SpawnImpact.cs`. No test of its own; Task 3.2 exercises it.

```csharp
/// <summary>
/// What still names some spawns: the per-table counts the dialog shows, and the rows a cleanup would
/// delete. Both come from one read, so the numbers and the deletion cannot disagree.
/// </summary>
public sealed record SpawnReferences(IReadOnlyList<SpawnImpactItem> Counts, RowSet Rows)
{
    public static SpawnReferences Empty { get; } = new([], new RowSet());

    public int Total => SpawnImpact.Total(Counts);

    /// <summary>True when there is at least one row the editor can delete with the spawns.</summary>
    public bool CanRemove => Rows.Names.Count > 0;
}
```

Build. Expected: `Build succeeded.`

Commit: `feat(spawn): bundle reference counts with the rows they name`

---

## Stage 2 — Read the reference rows (data layer)

### Task 2.1a Confirm the live shapes, then make the AzerothCore tables editable

**Prerequisite, not a nicety:** `InteractionDraft.ValidateEdit` throws for any table absent from the catalog, and `SqlExport` cannot emit a `DELETE` for it.

**Files:** new `Foundation/AzerothCore/AcTables.References.cs`, `Foundation/AzerothCore/AcTables.cs`.

First run this against `acore_world` (MySQL client or the MySQL MCP server, read-only):

```sql
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, COLUMN_KEY, IS_NULLABLE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('pool_creature','pool_gameobject','spawn_group',
                     'game_event_creature','game_event_gameobject','game_event_model_equip','linked_respawn')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
```

The snippet below is the **expected** shape, not gospel. Correct every column name, `ColumnKind` and key order from the query output before saving: `AcInteractionSource.Load` selects every declared column, so a column that does not exist makes the read throw, and a wrong `ColumnKind` makes `Row.Validate` refuse. Only list columns that exist. If a shape differs from `docs/reference/database-differences.md`, record it there (Task 8.2).

`Foundation/AzerothCore/AcTables.References.cs`:

```csharp
namespace ACWorldEdit.Foundation;

// The tables outside the spawn editors that name a spawn by GUID: what a spawn delete can remove with it.
public sealed partial class AcTables
{
    /// <summary>Pool membership of one spawn (the pool itself lives in pool_template).</summary>
    public static readonly TableSchema PoolCreature = new("pool_creature", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("pool_entry", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ]);

    public static readonly TableSchema PoolGameObject = new("pool_gameobject", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("pool_entry", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ]);

    /// <summary>A spawn's membership of a spawn group.</summary>
    public static readonly TableSchema SpawnGroup = new("spawn_group", ["groupId", "spawnId", "spawnType"], [
        ("groupId", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.UInt, 0),
        ("spawnType", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    public static readonly TableSchema GameEventCreature = new("game_event_creature", ["guid", "eventEntry"], [
        ("guid", ColumnKind.UInt, 0),
        ("eventEntry", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameEventGameObject = new("game_event_gameobject", ["guid", "eventEntry"], [
        ("guid", ColumnKind.UInt, 0),
        ("eventEntry", ColumnKind.UInt, 0)
    ]);

    /// <summary>The model and equipment a creature wears during one event.</summary>
    public static readonly TableSchema GameEventModelEquip = new("game_event_model_equip", ["eventEntry", "guid"], [
        ("eventEntry", ColumnKind.UInt, 0),
        ("guid", ColumnKind.UInt, 0),
        ("modelid", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema LinkedRespawn = new("linked_respawn", ["guid", "linkedGuid"], [
        ("guid", ColumnKind.UInt, 0),
        ("linkedGuid", ColumnKind.UInt, 0),
        ("linkType", ColumnKind.UInt, 0)
    ]);
}
```

In `Foundation/AzerothCore/AcTables.cs:15`, append to the initializer before `];`:

```csharp
, PoolCreature, PoolGameObject, SpawnGroup, GameEventCreature, GameEventGameObject, GameEventModelEquip, LinkedRespawn
```

Build. Expected: `Build succeeded.`

Commit: `feat(database): make the azerothcore spawn reference tables editable`

### Task 2.1b Same for TrinityCore, plus the invariant test

**Files:** new `Foundation/TrinityCore/TcTables.References.cs`, `Foundation/TrinityCore/TcTables.cs`, new `Tests/ACWorldEdit.Tests/Drafts/ReferenceCatalogTests.cs`.

Run the same `information_schema` query against `world` (TC has `pool_members`, no `game_event_model_equip`), then correct this expected shape the same way:

```csharp
namespace ACWorldEdit.Foundation;

// The tables outside the spawn editors that name a spawn by GUID: what a spawn delete can remove with it.
public sealed partial class TcTables
{
    /// <summary>Pool membership (type 0 is a creature, 1 a GameObject); guids are bigint here.</summary>
    public static readonly TableSchema PoolMembers = new("pool_members", ["type", "spawnId"], [
        ("type", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.ULong, 0),
        ("poolSpawnId", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ]);

    public static readonly TableSchema SpawnGroup = new("spawn_group", ["groupId", "spawnId", "spawnType"], [
        ("groupId", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.ULong, 0),
        ("spawnType", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    public static readonly TableSchema GameEventCreature = new("game_event_creature", ["guid", "eventEntry"], [
        ("guid", ColumnKind.ULong, 0),
        ("eventEntry", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameEventGameObject = new("game_event_gameobject", ["guid", "eventEntry"], [
        ("guid", ColumnKind.ULong, 0),
        ("eventEntry", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema LinkedRespawn = new("linked_respawn", ["guid", "linkedGuid"], [
        ("guid", ColumnKind.ULong, 0),
        ("linkedGuid", ColumnKind.ULong, 0),
        ("linkType", ColumnKind.UInt, 0)
    ]);
}
```

Append to `Foundation/TrinityCore/TcTables.cs:17`:

```csharp
, PoolMembers, SpawnGroup, GameEventCreature, GameEventGameObject, LinkedRespawn
```

Write the failing invariant test:

```csharp
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>Every table a spawn delete can name must be editable, or the cleanup silently loses rows.</summary>
public class ReferenceCatalogTests
{
    [Theory]
    [InlineData(WorldDialect.AzerothCore)]
    [InlineData(WorldDialect.TrinityCore)]
    public void EveryReferencedTableIsInTheCatalog(WorldDialect dialect)
    {
        var catalog = dialect == WorldDialect.TrinityCore ? (ITableCatalog)TcTables.Instance : AcTables.Instance;
        foreach (var kind in new[] { SpawnKind.Creature, SpawnKind.GameObject })
        {
            foreach (var reference in SpawnEdits.References(kind, dialect))
            {
                Assert.Contains(catalog.All, table => table.Name == reference.Table);
            }
        }
    }
}
```

(Check the real parameter order of `SpawnEdits.References` before pasting: it is `(WorldDialect dialect, SpawnKind kind)` at `Foundation/Spawn/SpawnEdits.cs:149`, so write `SpawnEdits.References(dialect, kind)`.)

Run before the TC schemas exist and after only the AC ones: the AC case passes, the TC case fails naming `pool_members`, `spawn_group`, `game_event_creature`, `linked_respawn`. Then add the TC file and re-run:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~ReferenceCatalogTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2`.

Then the whole default suite, because catalog changes affect export/coverage fixtures:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false
```

Expected: `Failed: 0`. If a coverage fixture now lists more tables, extend the fixture (it is asserting draft coverage) rather than reverting the schema.

Commit: `feat(database): make the trinitycore spawn reference tables editable`

### Task 2.2 Keep the count query, add the row reader

**Files:** new `Scripts/Data/DbReferences.cs`; delete `Scripts/Data/DbSpawnImpact.cs` and its `.uid`; `Scripts/Data/IDBSource.cs`; `Scripts/Data/AzerothCore/AcDBSource.cs:43`; `Scripts/Data/TrinityCore/TcDBSource.cs:52`; new `Tests/ACWorldEdit.Tests/Db/ReferenceCleanupDbTests.cs`.

Two methods with different jobs, sharing one predicate builder:

- `CountAsync` — unchanged behaviour: one `COUNT(*)` per reference, counting **matches per reference** (a `creature_formations` self-row matched by both `leaderGUID` and `memberGUID` counts twice). Every existing caller and `SpawnImpactDbTests` keep working.
- `ReadAsync` — loads the rows and counts **distinct rows to be removed**, which is what the dialog's total and the third button must agree with. A row matched by two references is listed once.

This difference is intentional and must be stated in the XML doc on both methods; do not try to make one number serve both meanings.

Write the failing live test first (new file — repo rule: one new file per database feature):

```csharp
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only checks that the rows offered for removal are exactly the rows the counts name.
/// Each test finds a real referenced GUID first, so it does not depend on particular data.
/// </summary>
[Trait("Category", "Db")]
public class ReferenceCleanupDbTests
{
    [DbFact]
    public async Task AzerothCoreReturnsTheRowsItCounts() => await Check(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreReturnsTheRowsItCounts() => await Check(Environments.TrinityConnection()!);

    static async Task Check(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly("SELECT guid FROM game_event_creature LIMIT 1");
        if (await command.ExecuteScalarAsync() is not { } picked || picked is DBNull)
        {
            return;
        }

        ulong guid = Convert.ToUInt64(picked);
        var references = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, [guid], default);
        Assert.True(references.Total > 0);
        Assert.True(references.CanRemove);

        int eventRows = references.Rows.All("game_event_creature").Count(row => row["guid"] == guid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(references.Counts.Where(c => c.Reference.Table == "game_event_creature").Sum(c => c.Count), eventRows);

        var none = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, [4_000_000_000UL], default);
        Assert.Empty(none.Counts);
        Assert.False(none.CanRemove);
    }
}
```

Run with `all.runsettings` (the default filter would hide nothing here, but this suite is opt-in):

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~ReferenceCleanupDbTests" --settings Tests/ACWorldEdit.Tests/all.runsettings
```

Expected without `ACWORLD_DB_INFO`: the tests are **Skipped** with the attribute's reason. Report that as "not proven". With the variables set, expect a compile failure on `ReadSpawnReferencesAsync`.

Add to `Scripts/Data/IDBSource.cs` after `CountSpawnReferencesAsync` (`:40`):

```csharp
    /// <summary>
    /// What still names these spawn GUIDs, as rows: the per-table counts of distinct rows and the rows
    /// themselves, so a delete can remove them in the same command. Bounded; a table the database lacks
    /// is skipped. Unlike <see cref="CountSpawnReferencesAsync"/> this counts each row once.
    /// </summary>
    Task<SpawnReferences> ReadSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token);
```

Create `Scripts/Data/DbReferences.cs`:

```csharp
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The rows outside the spawn tables that name some spawn GUIDs (<see cref="SpawnEdits.References"/>).
/// <see cref="CountAsync"/> counts matches per reference, as the warning always has; <see cref="ReadAsync"/>
/// loads the rows and counts distinct rows, which is what a cleanup command will delete. Both share one
/// predicate builder, so they cannot drift apart.
/// </summary>
internal static class DbReferences
{
    const int MaxGuids = 500;

    /// <summary>Matches per reference: the number the delete warning reports.</summary>
    public static async Task<IReadOnlyList<SpawnImpactItem>> CountAsync(WorldDatabase database, WorldDialect dialect, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        var counts = new List<SpawnImpactItem>();
        if (wanted.Length == 0)
        {
            return counts;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in SpawnEdits.References(dialect, kind))
        {
            if (!session.Has($"{reference.Table}.{reference.Column}"))
            {
                continue;
            }

            var (where, values) = Match(reference, wanted);
            int count = await CountOneAsync(session, reference.Table, where, values, token).ConfigureAwait(false);
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return counts;
    }

    /// <summary>The rows themselves, counted once each: what a delete can remove with the spawns.</summary>
    public static async Task<SpawnReferences> ReadAsync(WorldDatabase database, ITableCatalog tables, WorldDialect dialect, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        var rows = new RowSet();
        if (wanted.Length == 0)
        {
            return new([], rows);
        }

        var counts = new List<SpawnImpactItem>();
        bool capped = false;
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in SpawnEdits.References(dialect, kind))
        {
            // ReferenceCatalogTests keeps every reference table in the catalog; a table the source
            // cannot edit is skipped rather than throwing, as any uneditable table is.
            if (!session.Has($"{reference.Table}.{reference.Column}")
                || tables.All.FirstOrDefault(t => t.Name == reference.Table) is not { } table)
            {
                continue;
            }

            var (where, values) = Match(reference, wanted);
            var before = rows.All(table).Select(table.KeyOf).ToHashSet();
            await AcInteractionSource.Load(session, rows, table, where, values.Select(v => (object)v.Value).ToArray(), token).ConfigureAwait(false);
            int count = rows.All(table).Count(row => !before.Contains(table.KeyOf(row)));
            // AcInteractionSource.Load stops at DbLimits.MaxRows. A reference that fills that window is
            // not removable in one command, so no cleanup is offered at all rather than a partial one.
            capped |= count >= DbLimits.MaxRows;
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return capped
            ? new(await CountAsync(database, dialect, kind, wanted, token).ConfigureAwait(false), new RowSet())
            : new(counts, rows);
    }

    /// <summary>One reference's match count; the one query shape both readers use.</summary>
    internal static async Task<int> CountOneAsync(WorldReadSession session, string table, string where, (string Name, object Value)[] values, CancellationToken token)
    {
        await using var command = session.Command("");
        command.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE {where}";
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    /// <summary>The WHERE clause and its parameters; names come from code, the GUIDs are parameters (negated where the table stores them so).</summary>
    static (string Where, (string Name, object Value)[] Values) Match(SpawnReference reference, ulong[] wanted)
    {
        string names = string.Join(',', wanted.Select((_, i) => "@k" + i));
        var values = wanted.Select((guid, i) => ("@k" + i, reference.Negated ? (object)-(long)guid : guid)).ToArray();
        string where = $"`{reference.Column}` IN ({names})" + (reference.Where == null ? "" : $" AND {reference.Where}");
        return (where, values);
    }
}
```

Replace the two `CountSpawnReferencesAsync` bodies:

`Scripts/Data/AzerothCore/AcDBSource.cs:43`:

```csharp
    public Task<IReadOnlyList<SpawnImpactItem>> CountSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbReferences.CountAsync(database, WorldDialect.AzerothCore, kind, guids, token);
    public Task<SpawnReferences> ReadSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbReferences.ReadAsync(database, AcTables.Instance, WorldDialect.AzerothCore, kind, guids, token);
```

`Scripts/Data/TrinityCore/TcDBSource.cs:52`:

```csharp
    public Task<IReadOnlyList<SpawnImpactItem>> CountSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbReferences.CountAsync(database, WorldDialect.TrinityCore, kind, guids, token);
    public Task<SpawnReferences> ReadSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbReferences.ReadAsync(database, TcTables.Instance, WorldDialect.TrinityCore, kind, guids, token);
```

Delete the old reader:

```bash
git rm Scripts/Data/DbSpawnImpact.cs Scripts/Data/DbSpawnImpact.cs.uid
```

Build and run both reference suites:

```bash
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~SpawnImpactDbTests|FullyQualifiedName~ReferenceCleanupDbTests" --settings Tests/ACWorldEdit.Tests/all.runsettings
```

Expected: `Build succeeded.`; the existing `SpawnImpactDbTests` still green (its counts are unchanged by design), and `ReferenceCleanupDbTests` green with both databases reachable. Zero skips is the acceptance bar for the live pair.

Commit: `refactor(data): add a row reader beside the spawn reference count`

---

## Stage 3 — Delete the dependents in the same command

### Task 3.1 `SpawnEdits.DeleteReferences`

**Files:** `Foundation/Spawn/SpawnEdits.cs`, `Tests/ACWorldEdit.Tests/Drafts/SpawnEditsTests.cs`.

Failing test first (append to the existing class — it is the suite for this code):

```csharp
    [Fact]
    public void ReferenceRowsBecomeOneDeleteEachInTableOrder()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureFormations, Row.Of(("leaderGUID", 5u), ("memberGUID", 9u), ("dist", 1f), ("angle", 0f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u)));
        rows.Set(AcTables.LinkedRespawn, Row.Of(("guid", 5u), ("linkedGuid", 6u), ("linkType", 0u)));

        var edits = SpawnEdits.DeleteReferences(AcSchema.Instance, rows);

        Assert.Equal(["creature_formations", "linked_respawn"], edits.Select(e => e.Table));
        Assert.All(edits, e => Assert.Null(e.After));
        Assert.Equal("9", edits[0].Key);
        Assert.Equal("5|6", edits[1].Key);
    }
```

Run:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~SpawnEditsTests"
```

Expected: compile failure, `'SpawnEdits' does not contain a definition for 'DeleteReferences'`.

Add to `Foundation/Spawn/SpawnEdits.cs`, after `Delete` (`:143`):

```csharp
    /// <summary>
    /// Deletes for the rows that name the spawns (<see cref="References"/>), so one command can remove the
    /// spawn and everything that pointed at it. Every row must be one the caller read for this delete.
    /// </summary>
    public static RowEdit[] DeleteReferences(IWorldSchema schema, RowSet rows) => rows.Names
        .OrderBy(name => name, StringComparer.Ordinal)
        .SelectMany(name =>
        {
            var table = schema.Tables.Find(name);
            return rows.All(name)
                .OrderBy(row => table.KeyOf(row), StringComparer.Ordinal)
                .Select(row => InteractionEdits.Delete(table, row));
        })
        .ToArray();
```

Re-run: `Passed!  - Failed: 0, Passed: N`.

Commit: `feat(spawn): turn reference rows into delete edits`

### Task 3.2 `SpawnPoses` loads and deletes the dependents

**Files:** `Foundation/Spawn/SpawnPoses.cs`, `Foundation/Spawn/SpawnPoses.Batch.cs`, `Tests/ACWorldEdit.Tests/Drafts/SpawnPosesTests.cs`.

Failing test (append to `SpawnPosesTests`; use `creature_formations`, already in the TC catalog, and no placeholder variables):

```csharp
    [Fact]
    public void RemovingDependentsIsOneCommandAndOneUndo()
    {
        var (drafts, poses, _) = Creature();
        var formation = TcSchema.Instance.Tables.Find("creature_formations");
        var related = new RowSet();
        related.Set(formation, Row.Of(("leaderGUID", 5u), ("memberGUID", 5u), ("dist", 0f), ("angle", 0f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u)));

        poses.LoadRelated(Source, related);
        var (ok, message) = poses.DeleteMany(null, Tc, Source, [5], "Delete", related);
        Assert.True(ok, message);
        Assert.Equal(2, drafts.Draft!.NetEdits().Count);

        poses.Undo(null);
        Assert.Empty(drafts.Draft.NetEdits());
        Assert.NotNull(poses.Snapshot(Source).Get(formation, "5"));
    }
```

Run:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~SpawnPosesTests"
```

Expected: compile failure on `LoadRelated` and the six-argument `DeleteMany`.

Add to `Foundation/Spawn/SpawnPoses.cs` after `LoadFormationRows` (`:101`). It writes into the per-source baseline, exactly as `LoadFormationRows` and `LoadPathRows` already do, and that baseline persists until the source changes:

```csharp
    /// <summary>
    /// Remember the rows a delete also removes (pools, groups, events, links, formations, per-spawn SmartAI)
    /// as its baseline, so they can be deleted in the same command.
    /// </summary>
    public void LoadRelated(string source, RowSet rows)
    {
        if (!Available)
        {
            return;
        }

        var snapshot = Snapshot(source);
        var catalog = drafts.Draft!.Tables(source);
        foreach (string name in rows.Names)
        {
            if (catalog.All.FirstOrDefault(t => t.Name == name) is not { } table)
            {
                continue;
            }

            foreach (var row in rows.All(name))
            {
                snapshot.Set(table, row);
            }
        }
    }
```

Change `DeleteMany` in `Foundation/Spawn/SpawnPoses.Batch.cs:46`:

```csharp
    /// <summary>Delete several spawns, each with its addon row, and the rows that name them, as one undoable command.</summary>
    public (bool Ok, string Message) DeleteMany(object? sender, IWorldSchema schema, string source, IReadOnlyList<ulong> guids, string label = "Delete", RowSet? related = null)
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var effective = drafts.Draft.Effective(source, Snapshot(source));
        var edits = new List<RowEdit>();
        foreach (ulong guid in guids.Distinct())
        {
            if (effective.Get(Table(source), Key(guid)) is not { } row)
            {
                return (false, "A selected spawn is still loading; try again.");
            }

            edits.AddRange(SpawnEdits.Delete(schema, kind, effective, row));
        }

        if (related != null)
        {
            edits.AddRange(SpawnEdits.DeleteReferences(schema, related));
        }

        return edits.Count == 0 ? (true, "") : Commit(sender, source, label, edits);
    }
```

Re-run the filter, then the whole suite:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false
```

Expected: `Failed: 0`. If `SpawnRules` objects to the extra rows, pass it only the spawn/addon rows (do **not** weaken the rule) and note it in the commit body.

Commit: `feat(spawn): delete reference rows with the spawn in one command`

---

## Stage 4 — The three-answer dialog

### Task 4.1 `Dialogs.Confirm` gains an optional third answer

**File:** `Scripts/Ui/Framework/Dialogs.cs`.

New GdTests file `Tests/ACWorldEdit.GdTests/DeleteImpactDialogTests.cs` — copy the `using`/`[RequireGodotRuntime]` header from `Tests/ACWorldEdit.GdTests/MenuSheetTests.cs`. It asserts: (1) with `extra: ("Remove these too", () => ran = true)` a button with that text exists; (2) emitting its `Pressed` runs the action once and frees the dialog; (3) without `extra`, no such button exists.

Run (needs Godot; note `-c Debug`):

```bash
GODOT_BIN=$(command -v godot) dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~DeleteImpactDialogTests"
```

Expected before the change: compile failure (`Confirm` has no `extra` parameter).

Replace `Dialogs.Confirm` (`Scripts/Ui/Framework/Dialogs.cs:40`) with:

```csharp
    /// <summary>
    /// A confirm/cancel question with an optional third answer. <paramref name="extra"/> is a button label
    /// and the action it runs; the dialog frees itself on any answer.
    /// </summary>
    public static ConfirmationDialog Confirm(Node owner, string title, string text, Action confirmed,
        string ok = "OK", string cancel = "Cancel", Vector2I? size = null, (string Label, Action Acted)? extra = null)
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            DialogText = text,
            OkButtonText = ok,
            CancelButtonText = cancel
        };
        if (extra is { } third)
        {
            dialog.AddButton(third.Label, false, "extra");
            dialog.CustomAction += action =>
            {
                if (action.ToString() != "extra")
                {
                    return;
                }

                third.Acted();
                dialog.QueueFree();
            };
        }

        dialog.Confirmed += () =>
        {
            confirmed();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        owner.AddChild(dialog);
        if (size is { } rect)
        {
            dialog.PopupCentered(rect);
        }
        else
        {
            dialog.PopupCentered();
        }

        return dialog;
    }
```

`AcceptDialog.CustomAction` does not hide the dialog by itself — the `QueueFree()` above is required, which is why the test asserts the node is freed.

Re-run the filter: `Passed!`.

Commit: `feat(ui): offer a third answer in confirm dialogs`

### Task 4.2 The delete dialog words the choice

**Files:** `Foundation/Spawn/SpawnImpact.cs`, `Scripts/Ui/Scene/SpawnDeleteDialog.cs`, new `Tests/ACWorldEdit.Tests/Spawn/SpawnImpactTests.cs`.

Failing test:

```csharp
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Spawn;

public class SpawnImpactTests
{
    static readonly SpawnImpactItem[] Items = [new(new SpawnReference("pool_creature", "guid", "pool membership"), 2)];

    [Fact]
    public void OffersCleanupOnlyWhenThereIsSomethingToRemove()
    {
        Assert.Contains("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items));
        Assert.DoesNotContain("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("remove the rows that name it too", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("• 2 pool membership (pool_creature)", SpawnImpact.Prompt("Hogger (5)", Items));
    }
}
```

Run:

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~SpawnImpactTests"
```

Expected: compile failure on `offersCleanup`.

Change `SpawnImpact.Prompt` (`Foundation/Spawn/SpawnImpact.cs:39`):

```csharp
    /// <summary>The question a delete asks when something still references the spawns.</summary>
    public static string Prompt(string subject, IEnumerable<SpawnImpactItem> items, IEnumerable<SpawnPathUse>? paths = null, bool offersCleanup = false) =>
        $"{subject} is still referenced:\n\n{string.Join('\n', Lines(items, paths).Select(l => "• " + l))}\n\n" +
        (offersCleanup
            ? "Remove the spawn alone, or remove the rows that name it too?"
            : "Deleting it leaves these rows behind. Delete anyway?");
```

Replace `Scripts/Ui/Scene/SpawnDeleteDialog.cs`:

```csharp
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The question a spawn delete asks when other tables still name the spawn: it lists them, because the
/// spawn and its addon row are all a plain delete removes. With <c>removeToo</c> it also offers to remove
/// those rows in the same undoable command.
/// </summary>
public static class SpawnDeleteDialog
{
    /// <summary>
    /// Open the dialog on <paramref name="host"/>. <paramref name="confirmed"/> runs on "Delete only this";
    /// <paramref name="removeToo"/> runs on "Remove these too" and is offered only when it is not null.
    /// </summary>
    public static ConfirmationDialog Ask(Node host, string subject, IReadOnlyList<SpawnImpactItem> items, Action confirmed, IReadOnlyList<SpawnPathUse>? paths = null, Action? removeToo = null) =>
        Dialogs.Confirm(host, "Delete " + subject, SpawnImpact.Prompt(subject, items, paths, removeToo != null), confirmed,
            ok: removeToo == null ? "Delete anyway" : "Delete only this",
            cancel: "Keep",
            extra: removeToo == null ? null : ("Remove these too", removeToo));
}
```

Re-run both filters: `Passed!`.

Commit: `feat(ui): offer to remove a spawn's reference rows too`

---

## Stage 5 — Wire both overlays

The final `DeleteNow` below is the only version; Stage 6 (if taken) changes only the `removeToo` condition and adds one separate method, never rewrites this one.

### Task 5.1 Creature overlay

**File:** `Scripts/Overlays/Creature/CreatureOverlay.Group.cs` — replace `DeleteAsync` (`:117`) and `DeleteNow` (`:154`):

```csharp
    public async Task DeleteAsync()
    {
        if (poses == null || Group.Count == 0 || (deleteDialog != null && GodotObject.IsInstanceValid(deleteDialog)))
        {
            return;
        }

        gizmo.Finish(false);
        var guids = Group.ToArray();
        string subject = SubjectOf(guids);
        var existing = guids.Where(g => !IsStaged(g)).ToArray();
        var references = SpawnReferences.Empty;
        IReadOnlyList<SpawnPathUse> paths = [];
        if (existing.Length > 0)
        {
            try
            {
                var source = DBSource.Open(Connection());
                references = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, existing, default);
                paths = await source.ReadSpawnPathsAsync(existing, default);
            }
            catch (Exception)
            {
                Say($"Could not check what references {subject}; not deleted.");
                return;
            }
        }

        if (!SpawnImpact.Any(references.Counts, paths))
        {
            DeleteNow(guids, subject);
            return;
        }

        deleteDialog = SpawnDeleteDialog.Ask(this, subject, references.Counts, () => DeleteNow(guids, subject), paths,
            references.CanRemove ? () => DeleteNow(guids, subject, references) : null);
    }

    void DeleteNow(IReadOnlyList<ulong> guids, string subject, SpawnReferences? references = null)
    {
        if (references != null)
        {
            poses!.LoadRelated(sourceKey, references.Rows);
        }

        var (ok, message) = poses!.DeleteMany(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, guids,
            guids.Count == 1 ? "Delete" : $"Delete {guids.Count} creatures", references?.Rows);
        Say(ok ? references == null ? $"Deleted {subject}." : $"Deleted {subject} and the rows that named it." : message);
    }
```

Build:

```bash
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
```

Expected: `Build succeeded.`

Commit: `feat(creature): remove the rows that name a deleted spawn too`

### Task 5.2 GameObject overlay

**File:** `Scripts/Overlays/GameObject/GameObjectOverlay.Group.cs` — replace `DeleteAsync` (`:114`) and `DeleteNow` (`:148`):

```csharp
    public async Task DeleteAsync()
    {
        if (poses == null || Group.Count == 0 || (deleteDialog != null && GodotObject.IsInstanceValid(deleteDialog)))
        {
            return;
        }

        gizmo.Finish(false);
        var guids = Group.ToArray();
        string subject = SubjectOf(guids);
        var existing = guids.Where(g => !IsStaged(g)).ToArray();
        var references = SpawnReferences.Empty;
        if (existing.Length > 0)
        {
            try
            {
                references = await DBSource.Open(Connection()).ReadSpawnReferencesAsync(SpawnKind.GameObject, existing, default);
            }
            catch (Exception)
            {
                Say($"Could not check what references {subject}; not deleted.");
                return;
            }
        }

        if (!SpawnImpact.Any(references.Counts))
        {
            DeleteNow(guids, subject);
            return;
        }

        deleteDialog = SpawnDeleteDialog.Ask(this, subject, references.Counts, () => DeleteNow(guids, subject), null,
            references.CanRemove ? () => DeleteNow(guids, subject, references) : null);
    }

    void DeleteNow(IReadOnlyList<ulong> guids, string subject, SpawnReferences? references = null)
    {
        if (references != null)
        {
            poses!.LoadRelated(sourceKey, references.Rows);
        }

        var (ok, message) = poses!.DeleteMany(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, guids,
            guids.Count == 1 ? "Delete" : $"Delete {guids.Count} GameObjects", references?.Rows);
        Say(ok ? references == null ? $"Deleted {subject}." : $"Deleted {subject} and the rows that named it." : message);
    }
```

Build. Expected: `Build succeeded.`

Commit: `feat(gameobject): remove the rows that name a deleted spawn too`

### Task 5.3 Smoke can press the third button

**Files:** `Scripts/Diagnostics/SpawnSmoke.cs`, `Scripts/Diagnostics/CreatureOverlay.Smoke.cs:377`, `Scripts/Diagnostics/GameObjectOverlay.Smoke.cs:175`, `Scripts/Diagnostics/ViewerShell.SpawnAuthoringSmoke.cs` (after `referenced-creature-gone-and-undone`, around `:903`).

Add to `SpawnSmoke`, beside `Answer`:

```csharp
    /// <summary>Press the delete dialog's "Remove these too" button; false when it is not offered.</summary>
    public static bool AnswerRemoveToo(Godot.ConfirmationDialog? dialog)
    {
        if (dialog == null || !Godot.GodotObject.IsInstanceValid(dialog) || dialog.IsQueuedForDeletion())
        {
            return false;
        }

        foreach (var node in dialog.FindChildren("*", "Button", true, false))
        {
            if (node is Godot.Button { Text: "Remove these too" } button)
            {
                button.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                return true;
            }
        }

        return false;
    }
```

Add wrappers beside the existing `SmokeAnswerDelete` in both overlay smoke partials:

```csharp
    public bool SmokeAnswerRemoveToo() => SpawnSmoke.AnswerRemoveToo(deleteDialog);
```

Add three steps in `ViewerShell.SpawnAuthoringSmoke.cs`, reusing `authoringReferenced`, `AuthoringScript`, `drafts`, `creatures`:

```csharp
        ("referenced-creature-cleanup-offered", () =>
        {
            creatures.Select(authoringReferenced);
            creatures.SmokePressDelete();
            return creatures.SmokeDeleteDialog != null;
        }),
        ("referenced-creature-cleanup-taken", () =>
        {
            Require(creatures.SmokeAnswerRemoveToo(), "the dialog did not offer \"Remove these too\"");
            Require(drafts.Draft!.NetEdits().Count > 1, "the cleanup staged only the spawn");
            Require(AuthoringScript(creatures.SmokeSourceKey).Contains("DELETE FROM `creature` WHERE", StringComparison.Ordinal), "the export lacks the spawn DELETE");
            return true;
        }),
        ("referenced-creature-cleanup-undone", () =>
        {
            creatures.SmokeHistory(false);
            Require(drafts.Draft!.NetEdits().Count == 0, "undo left a net change");
            return true;
        }),
```

Run the smoke through the golden harness (the launcher's `--smoke` only runs the terrain smoke, so do not use it here):

```bash
python Tools/smoke_golden.py --smoke spawn-authoring-smoke
```

Expected: the run reports every step ok and the golden diff is empty (`Tools/smoke_golden.py` exits `0`). If it needs the database and none is configured, it fails loudly — that is the correct signal; do not record a skip as a pass. When the smoke legitimately prints new steps, refresh the golden **only if the change is intended**: `python Tools/smoke_golden.py --smoke spawn-authoring-smoke --update`.

Commit: `test(smoke): press remove-these-too and undo it`

---

## Stage 6 — Orphaned waypoint paths (deferred; only if asked)

`IDBSource.ReadWaypointPathRowsAsync(pathId)` already exists (`Scripts/Data/IDBSource.cs:49`, both sources), and `SpawnPoses.LoadPathRows` already writes path rows into the baseline, so this is a small addition — but it widens the scope to a fourth table family.

**File:** `Scripts/Overlays/Creature/CreatureOverlay.Group.cs`.

Change only the third-button condition in `DeleteAsync`:

```csharp
        bool removePath = SpawnImpact.Orphaned(paths).Count > 0;
        deleteDialog = SpawnDeleteDialog.Ask(this, subject, references.Counts, () => DeleteNow(guids, subject), paths,
            references.CanRemove || removePath ? () => DeleteWithPathsAsync(guids, subject, references, paths) : null);
```

Add the method; `DeleteNow` stays as written in Task 5.1:

```csharp
    /// <summary>Delete the spawns, the rows that name them and any path nothing else uses, as one command.</summary>
    async Task DeleteWithPathsAsync(IReadOnlyList<ulong> guids, string subject, SpawnReferences references, IReadOnlyList<SpawnPathUse>? paths)
    {
        var related = references.Rows.Clone();
        var source = DBSource.Open(Connection());
        foreach (var path in SpawnImpact.Orphaned(paths))
        {
            related.Merge(await source.ReadWaypointPathRowsAsync(path.PathId, default));
        }

        poses!.LoadRelated(sourceKey, related);
        var (ok, message) = poses.DeleteMany(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, guids,
            guids.Count == 1 ? "Delete" : $"Delete {guids.Count} creatures", related);
        Say(ok ? $"Deleted {subject} and the rows that named it." : message);
    }
```

Extend `SpawnPosesTests` with a merged path-row case (single undo restores spawn, formation and path nodes); run the same filtered commands as Task 3.2. Confirm the path tables are in both catalogs (`WaypointData` on AC; `WaypointPath` + `WaypointPathNode` on TC) before relying on `LoadRelated` — `ReferenceCatalogTests` does not cover them.

Commit: `feat(creature): remove an unused waypoint path with its last creature`

---

## Stage 7 — The "Used by" card (Slice A; safe to ship alone)

### Task 7.1 The template reference list

**Files:** new `Foundation/Spawn/TemplateReferences.cs`, new `Tests/ACWorldEdit.Tests/Spawn/TemplateReferencesTests.cs`.

Failing test:

```csharp
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Spawn;

public class TemplateReferencesTests
{
    [Fact]
    public void TemplatesNameTheirSpawnsAndContent()
    {
        var creature = TemplateReferences.For(SpawnKind.Creature);
        Assert.Contains(creature, r => r.Table == "creature" && r.Column == "id");
        Assert.Contains(creature, r => r.Table == "smart_scripts" && r.Where == "source_type = 0");

        var gameObject = TemplateReferences.For(SpawnKind.GameObject);
        Assert.Contains(gameObject, r => r.Table == "gameobject" && r.Column == "id");
        Assert.Contains(gameObject, r => r.Table == "smart_scripts" && r.Where == "source_type = 1");
    }
}
```

Run: `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~TemplateReferencesTests"` → compile failure.

```csharp
namespace ACWorldEdit.Foundation;

/// <summary>
/// The tables outside a template's own rows that name a creature or GameObject template by entry, so a page
/// can say what an edit reaches. Counted only, so no table schema is needed and a missing table is skipped.
/// </summary>
public static class TemplateReferences
{
    public static IReadOnlyList<SpawnReference> For(SpawnKind kind) => kind == SpawnKind.Creature
        ?
        [
            new("creature", "id", "spawns"),
            new("smart_scripts", "entryorguid", "SmartAI rules for this template", "source_type = 0"),
            new("creature_loot_template", "Entry", "loot entries"),
            new("pickpocketing_loot_template", "Entry", "pickpocketing loot entries"),
            new("skinning_loot_template", "Entry", "skinning loot entries"),
            new("npc_vendor", "entry", "vendor lists"),
            new("creature_queststarter", "id", "quests it starts"),
            new("creature_questender", "id", "quests it ends")
        ]
        :
        [
            new("gameobject", "id", "spawns"),
            new("smart_scripts", "entryorguid", "SmartAI rules for this template", "source_type = 1"),
            new("gameobject_loot_template", "Entry", "loot entries"),
            new("gameobject_queststarter", "id", "quests it starts"),
            new("gameobject_questender", "id", "quests it ends")
        ];
}
```

Re-run: `Passed!`.

Commit: `feat(templates): name the tables a template is used by`

### Task 7.2 Count template references

**Files:** new `Scripts/Data/DbTemplateReferences.cs`; `Scripts/Data/IDBSource.cs`; `Scripts/Data/AzerothCore/AcDBSource.cs`; `Scripts/Data/TrinityCore/TcDBSource.cs`; new `Tests/ACWorldEdit.Tests/Db/TemplateReferencesDbTests.cs`.

Failing live test:

```csharp
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>Live check that template reference counts match the database, on both dialects.</summary>
[Trait("Category", "Db")]
public class TemplateReferencesDbTests
{
    [DbFact]
    public async Task AzerothCoreCountsTheSpawnsOfATemplate() => await Check(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreCountsTheSpawnsOfATemplate() => await Check(Environments.TrinityConnection()!);

    static async Task Check(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var pick = session.ReadOnly("SELECT id FROM creature ORDER BY guid LIMIT 1");
        uint entry = Convert.ToUInt32(await pick.ExecuteScalarAsync());

        var counts = await source.CountTemplateReferencesAsync(SpawnKind.Creature, entry, default);
        var spawns = Assert.Single(counts, c => c.Reference.Table == "creature");

        await using var exact = session.ReadOnly($"SELECT COUNT(*) FROM creature WHERE id={entry}");
        Assert.Equal(Convert.ToInt32(await exact.ExecuteScalarAsync()), spawns.Count);
        Assert.All(counts, c => Assert.True(c.Count > 0));
        Assert.Empty(await source.CountTemplateReferencesAsync(SpawnKind.Creature, uint.MaxValue, default));
    }
}
```

Run with `all.runsettings` → compile failure on `CountTemplateReferencesAsync`.

Add to `Scripts/Data/IDBSource.cs` after `CountSpawnReferencesAsync`:

```csharp
    /// <summary>How many rows of each other table name a creature or GameObject template, for the page's "Used by" card.</summary>
    Task<IReadOnlyList<SpawnImpactItem>> CountTemplateReferencesAsync(SpawnKind kind, uint entry, CancellationToken token);
```

Create `Scripts/Data/DbTemplateReferences.cs`:

```csharp
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Counts the rows elsewhere in the database that name a creature or GameObject template by entry. It shares
/// the one predicate shape with <see cref="DbReferences"/> (a table, a column and an optional extra filter);
/// only the reference list differs, so there is no second query style to maintain.
/// </summary>
internal static class DbTemplateReferences
{
    public static async Task<IReadOnlyList<SpawnImpactItem>> CountAsync(WorldDatabase database, SpawnKind kind, uint entry, CancellationToken token)
    {
        var counts = new List<SpawnImpactItem>();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in TemplateReferences.For(kind))
        {
            if (!session.Has($"{reference.Table}.{reference.Column}"))
            {
                continue;
            }

            await using var command = session.Command("");
            command.CommandText = $"SELECT COUNT(*) FROM `{reference.Table}` WHERE `{reference.Column}` = @entry"
                + (reference.Where == null ? "" : $" AND {reference.Where}");
            command.Parameters.AddWithValue("@entry", entry);
            int count = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return counts;
    }
}
```

Keep it to these lines: the entry form binds one named parameter rather than a GUID list, so it does not go through `DbReferences.Match`; the counting itself is the same three lines and stays local on purpose (a shared helper would need to abstract the parameter shape for one caller). `DbReferences.CountOneAsync` exists for the GUID form, which is the one with two callers.

Add one line to each source:

```csharp
    public Task<IReadOnlyList<SpawnImpactItem>> CountTemplateReferencesAsync(SpawnKind kind, uint entry, CancellationToken token) => DbTemplateReferences.CountAsync(database, kind, entry, token);
```

Build, then run the live filter. Expected with databases present: `Passed!  - Failed: 0, Passed: 2`; without them, both are Skipped and the feature is **not** proven.

Commit: `feat(data): count what a creature or gameobject template is used by`

### Task 7.3 One card, two pages

**Files:** new `Scripts/Ui/Framework/ReferenceCountCard.cs`, new `Scripts/Ui/CreatureProperties/UsedBySection.cs`, new `Scripts/Ui/GameObjectProperties/GameObjectUsedBySection.cs`, both registries.

Card — items live in their own container, so no child-index guessing; a failed read says so instead of claiming zero:

```csharp
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The "Used by" list of an entity page: how many rows of each other table name this template. Read-only,
/// read once per entry, and refreshed by the page's redraw when the counts arrive.
/// </summary>
public sealed partial class ReferenceCountCard(SpawnKind kind) : VBoxContainer
{
    readonly Label summary = ClientStyle.Text("", 14, ClientStyle.Body);
    readonly VBoxContainer items = new();
    readonly HashSet<uint> requested = [];
    Func<IDBSource>? source;
    Action? changed;
    IReadOnlyList<SpawnImpactItem>? counts;
    string? error;
    uint forEntry;
    bool built;

    /// <summary>Where the counts come from, and who redraws the page when they arrive.</summary>
    public void Bind(Func<IDBSource> open, Action redraw)
    {
        source = open;
        changed = redraw;
    }

    public void Reset()
    {
        counts = null;
        error = null;
        forEntry = 0;
        requested.Clear();
    }

    public void Show(uint entry)
    {
        if (!built)
        {
            built = true;
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            AddChild(summary);
            AddChild(items);
        }

        if (forEntry != entry)
        {
            forEntry = entry;
            counts = null;
            error = null;
        }

        Load(entry);
        summary.Text = error != null ? "Could not read what uses this template: " + error
            : counts == null ? "Counting what else names this template…"
            : counts.Count == 0 ? "Nothing else in the database names this template."
            : "These rows name this template. An edit here reaches them:";
        foreach (var child in items.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var item in counts ?? [])
        {
            items.AddChild(ClientStyle.Text($"• {item.Count} {item.Reference.Meaning} ({item.Reference.Table})", 13, ClientStyle.Body));
        }
    }

    async void Load(uint entry)
    {
        if (source == null || counts != null || error != null || !requested.Add(entry))
        {
            return;
        }

        try
        {
            var found = await source().CountTemplateReferencesAsync(kind, entry, default);
            if (forEntry == entry)
            {
                counts = found;
            }
        }
        catch (Exception failure)
        {
            if (forEntry == entry)
            {
                error = DbErrors.Safe(failure);
            }
        }

        requested.Remove(entry);
        if (forEntry == entry)
        {
            changed?.Invoke();
        }
    }

    /// <summary>The note and the counts the user sees, for tests and the smoke.</summary>
    public string SmokeText => summary.Text;
    public IReadOnlyList<SpawnImpactItem>? SmokeCounts => counts;
}
```

Sections — do **not** call `Clear()` (it queues the persistent card for deletion) and do not queue the card; prune only non-card children:

`Scripts/Ui/CreatureProperties/UsedBySection.cs`:

```csharp
using System.Globalization;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Everywhere else in the database this creature template is named: spawns, SmartAI, loot, vendors and quests.</summary>
public sealed partial class UsedBySection : CreatureSection
{
    readonly ReferenceCountCard card = new(SpawnKind.Creature);

    public UsedBySection(CreatureContext ctx) : base(ctx)
    {
        card.Bind(() => DBSource.Open(Ctx.Connection()), Ctx.Redraw);
    }

    public override string Id => "usedby";
    public override string Title => "Used by";
    public override bool Supports(WorldDialect dialect) => true;
    public override string IconPath => "Interface/Icons/INV_Misc_QuestionMark.blp";
    public override string Tip => "Every row elsewhere in the database that names this creature template, so an edit here shows what it reaches.";
    public override string Badge => card.SmokeCounts is { } counts ? counts.Sum(c => c.Count).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Snapshot != null;

    public override void OnCreatureChanged(CreatureSpawn? creature) => card.Reset();

    public override void Refresh()
    {
        foreach (var child in GetChildren())
        {
            if (child != card)
            {
                child.QueueFree();
            }
        }

        if (Ctx.Snapshot is not { } snapshot)
        {
            AddChild(Placeholder());
            return;
        }

        if (card.GetParent() != this)
        {
            card.GetParent()?.RemoveChild(card);
            AddChild(card);
        }

        card.Show(snapshot.Entry);
    }

    public ReferenceCountCard SmokeCard => card;
}
```

`Scripts/Ui/GameObjectProperties/GameObjectUsedBySection.cs` — same shape, `GameObjectContext`, `Ctx.Entry is uint entry`, `DBSource.Open(ctx.Connection())`, `ctx.Redraw`:

```csharp
using System.Globalization;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Everywhere else in the database this GameObject template is named: spawns, SmartAI, loot and quests.</summary>
public sealed partial class GameObjectUsedBySection : GameObjectSection
{
    readonly ReferenceCountCard card = new(SpawnKind.GameObject);

    public GameObjectUsedBySection(GameObjectContext ctx) : base(ctx)
    {
        card.Bind(() => DBSource.Open(ctx.Connection()), ctx.Redraw);
    }

    public override string Id => "usedby";
    public override string Title => "Used by";
    public override string IconPath => "Interface/Icons/INV_Misc_QuestionMark.blp";
    public override string Tip => "Every row elsewhere in the database that names this GameObject template, so an edit here shows what it reaches.";
    public override string Badge => card.SmokeCounts is { } counts ? counts.Sum(c => c.Count).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Entry != null;

    public override void Refresh()
    {
        foreach (var child in GetChildren())
        {
            if (child != card)
            {
                child.QueueFree();
            }
        }

        if (Ctx.Entry is not { } entry)
        {
            AddChild(Placeholder());
            return;
        }

        if (card.GetParent() != this)
        {
            card.GetParent()?.RemoveChild(card);
            AddChild(card);
        }

        card.Show(entry);
    }

    public ReferenceCountCard SmokeCard => card;
}
```

There is no `OnGameObjectChanged` hook: `EntitySection` (`Scripts/Ui/Properties/EntitySection.cs`) only declares `OnShown`, so do **not** add an override. The card's own "entry changed" check in `Show` resets it; `Reset()` stays public for the smoke and tests.

Register: append `ctx => new UsedBySection(ctx),` to `CreatureSectionRegistry.Factories`, and `ctx => new GameObjectUsedBySection(ctx)` to `GameObjectSectionRegistry.Factories`.

Build:

```bash
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
```

Expected: `Build succeeded.` (the base classes report any member a section missed).

Commit: `feat(ui): show what a creature or gameobject template is used by`

### Task 7.4 Prove the page

`Tests/AGENTS.md`: new UI flows get an interaction test. Add to the **existing** creature/GD window suite only if the file already covers the section picker; otherwise create `Tests/ACWorldEdit.GdTests/UsedBySectionTests.cs` with `[RequireGodotRuntime]`, open a creature page with a fake context, click the `usedby` entry, and assert `SmokeCard.SmokeText` becomes either the loading line or the counted line after one frame (never asserts on live data).

```bash
GODOT_BIN=$(command -v godot) dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~UsedBySectionTests"
```

Expected: `Passed!` (or Skipped when `GODOT_BIN` is unset — report that honestly).

Visual check, which is mandatory per `AGENTS.md` for UI work: run the windowed viewer, open a creature template with spawns, click **Used by**, and confirm the counts read like the Spawns page. Capture the UI dump and diff it:

```bash
python Tools/ui_golden.py --target creature:<entry>,usedby
```

Expected: prints the diff against `Tests/golden`; a brand-new section legitimately differs, so refresh **only that target** with `--update` and commit the golden with the code (the manifest `Tests/golden/.sha256` is rewritten by the tool).

Commit: `test(ui): cover the used-by page`

---

## Stage 8 — Docs (part of done, not a follow-up)

Constraints from the nested `AGENTS.md` files that this change must respect:

- `Foundation/` stays Godot-free (all Stage 1–4 and Stage 7.1 code is; `ReferenceCountCard` lives in `Scripts/Ui`).
- Dialect-specific classes are `Ac*`/`Tc*` in the dialect folders; the new table partials are `AcTables`/`TcTables` in theirs, and the shared readers stay unprefixed in `Scripts/Data`.
- `Tests/golden/` is pinned; only `Tools/ui_golden.py --update` (or `smoke_golden.py --update`) rewrites it, and only for an intended change.
- Docs move in the same commit as the code.

### Task 8.1 Fix the statements this change makes false

- `docs/status/open-work.md:109` — status cell becomes `done`; replace the `**Still open:**` sentence with: the dialog offers **Remove these too**, which deletes the pool, group, event, link, formation and per-spawn SmartAI rows in the same undoable command; `DbReferences.ReadAsync` returns counts and rows from one read; the eight tables are in both catalogs.
- `docs/architecture/viewer-runtime.md:201` — after "a delete removes the spawn and its addon row in one command", add: "or, with **Remove these too**, the rows in other tables that name the spawns, still as one undo step."
- `docs/architecture/editing-and-export.md` — in the `SpawnPoses` row, add `LoadRelated` and note that `DeleteMany` takes the reference rows.

### Task 8.2 Record schema findings

If Task 2.1 found a column or kind differing from the snippets, add the table shape to `docs/reference/database-differences.md` near the pools line (`:98`). If nothing differed, no edit.

Commit: `docs: describe the delete cleanup and the used-by page`

---

## Stage 9 — Full verification

```bash
dotnet format whitespace ACWorldEdit.csproj
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false
ACWORLD_DB_INFO='host;port;user;password;acore_world' ACWORLD_TC_DB_INFO='host;port;user;password;world' \
  dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --settings Tests/ACWorldEdit.Tests/all.runsettings
GODOT_BIN=$(command -v godot) dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false
python Tools/smoke_golden.py --smoke spawn-authoring-smoke
```

Expected: build `0 Error(s)`; default suite `Failed: 0`; the `all` run has **zero skipped** among `ReferenceCleanupDbTests`, `TemplateReferencesDbTests` and `SpawnImpactDbTests`; GdTests `Failed: 0`; smoke exits `0`. A skipped live test is not a pass — say so explicitly if the databases are unavailable.

Manual check in the viewer, on AzerothCore: delete a creature that has an event row, choose **Remove these too**, then Undo (both statements visible in Change History); open a creature template → **Used by** and compare its spawn count with the Spawns page; open a GameObject with a loot row → **Used by** lists loot.

---

## Tests / validation summary

| Layer | File | What it proves |
| --- | --- | --- |
| Unit | `Tests/ACWorldEdit.Tests/Drafts/RowSetTests.cs` | rows can be walked by name and merged |
| Unit | `Tests/ACWorldEdit.Tests/Drafts/ReferenceCatalogTests.cs` | every reference table is editable in both dialects |
| Unit | `Tests/ACWorldEdit.Tests/Drafts/SpawnEditsTests.cs` | reference rows become ordered deletes |
| Unit | `Tests/ACWorldEdit.Tests/Drafts/SpawnPosesTests.cs` | one command, one undo restores spawn + dependents |
| Unit | `Tests/ACWorldEdit.Tests/Spawn/SpawnImpactTests.cs` | dialog wording with and without cleanup |
| Unit | `Tests/ACWorldEdit.Tests/Spawn/TemplateReferencesTests.cs` | template reference list per kind |
| Unit | `Tests/ACWorldEdit.Tests/Sql/…` (extend the delete-export test) | a reference row exports a guarded `DELETE` |
| Live | `Tests/ACWorldEdit.Tests/Db/ReferenceCleanupDbTests.cs` | counts equal the rows offered, both dialects |
| Live | `Tests/ACWorldEdit.Tests/Db/TemplateReferencesDbTests.cs` | template counts equal the database, both dialects |
| UI | `Tests/ACWorldEdit.GdTests/DeleteImpactDialogTests.cs` | the third button runs its action and frees the dialog |
| UI | `Tests/ACWorldEdit.GdTests/UsedBySectionTests.cs` | the Used by page is listed and loads |
| Smoke | `Scripts/Diagnostics/ViewerShell.SpawnAuthoringSmoke.cs` | real key/button flow, export, undo |

## Risks, tradeoffs, open questions

- **Catalog growth is the real cost.** Eight tables across two dialects become editable, so they appear in Pending Changes and the SQL export (intended — the export is the fix). A wrong column kind makes a read throw. Mitigated by the `information_schema` gate and the live tests; do not skip either.
- **Two count meanings.** `CountSpawnReferencesAsync` counts matches per reference (unchanged, keeps `SpawnImpactDbTests` honest); `ReadSpawnReferencesAsync` counts distinct rows, which is what is deleted. A formation self-row matched by both `leaderGUID` and `memberGUID` is 2 in the first and 1 in the second. The dialog uses the second. Both XML docs say so.
- **Staleness.** Rows are read once, when the dialog opens. A concurrent database change makes the commit refuse with `Not saved: … the row changed; reload and retry.` No partial deletes: the command is all-or-nothing.
- **500-GUID cap.** Dependents are removed for the first 500 GUIDs, matching the existing count cap. Numbers shown equal rows removed. Not surfaced in the dialog (out of reach through multi-select in practice).
- **Pool/event parents left empty.** Removing the last `pool_members` row leaves an empty `pool_template`; removing a `linked_respawn` row can leave a partner unlinked. Both are deliberate and listed here so nobody reports them as regressions.
- **Used-by is counts, not links.** No navigation to the referencing rows; that needs per-table readers and a filters UI. YAGNI for this change.
- **`SpawnRules` on the widened row set.** If the rule check iterates every table in the effective set, it may object to the deleted reference rows. Fix by passing only spawn/addon rows to the check; never relax a rule.
- **Open question 1 (needs a decision before Stage 5):** a deleted formation leader dissolves the group and leaves the members standing. The alternative is to refuse the delete while `creature_formations` rows exist. This plan implements dissolution because the user explicitly asked for "Remove these too" in the same undoable command; say so in the dialog wording, and revisit only if the user prefers a refusal.
- **Open question 2:** whether to show the 500-spawn cap in the dialog. Plan: no (unreachable in practice), cheap to add later as one clause in `SpawnImpact.Prompt`.
