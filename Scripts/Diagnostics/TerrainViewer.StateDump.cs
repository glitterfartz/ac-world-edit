using System.Globalization;
using System.Text.Json;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Deterministic state and UI dumps used as refactor goldens. Both are opt-in flags:
/// <c>--state-dump</c> emits terrain, lighting, ground, pick and SQL-export state after a fixed
/// teleport path; <c>--ui-dump</c> emits the built control tree. Neither touches drafts or a
/// database, and both suppress interactive startup prompts.
/// </summary>
public partial class TerrainViewer
{
    bool stateDump, uiDump;
    int dumpStage, dumpSettle, dumpHop, dumpHourIndex;
    double dumpClock;
    double? dumpPendingHour;
    readonly List<double> dumpHours = [];
    readonly List<object> dumpTerrain = [];
    readonly List<object> dumpLighting = [];

    static readonly TileKey[] DumpHops =
    [
        new(30, 49),
        new(30, 45),
        new(34, 49)
    ];

    static readonly (float X, float Y)[] DumpScreenPoints =
    [
        (0.5f, 0.5f),
        (0.25f, 0.5f),
        (0.75f, 0.5f),
        (0.5f, 0.25f),
        (0.5f, 0.75f),
        (0.35f, 0.65f),
        (0.65f, 0.35f),
        (0.5f, 0.1f)
    ];

    /// <summary>Read the dump flags. A dump run is non-interactive exactly like a smoke run.</summary>
    void ConfigureDumpChecks(HashSet<string> flags)
    {
        stateDump = flags.Contains("--state-dump");
        uiDump = flags.Contains("--ui-dump");
        if (stateDump || uiDump)
        {
            anySmoke = true;
        }

        dumpHours.Clear();
        string? hours = flags.FirstOrDefault(f => f.StartsWith("--state-hours=", StringComparison.Ordinal));
        if (hours is null)
        {
            dumpHours.AddRange([0, 6, 12, 18, 23.999]);
        }
        else
        {
            foreach (var part in hours["--state-hours=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                dumpHours.Add(double.Parse(part, CultureInfo.InvariantCulture));
            }
        }

        // The profile is normally chosen by ACWORLD_CLIENT_PROFILE; this flag exists so a single
        // invocation can name it explicitly.
        if (flags.FirstOrDefault(f => f.StartsWith("--state-profile=", StringComparison.Ordinal)) is { } profileFlag)
        {
            var kind = profileFlag.EndsWith("retail", StringComparison.OrdinalIgnoreCase) ? ClientKind.Retail : ClientKind.Wrath335;
            if (kind != profile.Kind)
            {
                profile = ClientProfile.For(kind);
                databaseEnabled = profile.SupportsDatabase;
                connection.Dialect = profile.Dialect;
                dataDirectory = kind == ClientKind.Retail ? UserSettings.LoadRetailDirectory() : UserSettings.LoadDataDirectory();
                clientLabel.Text = "WORLD VIEWER  /  " + profile.Title;
            }
        }
    }

    /// <summary>True when the terrain has settled: every wanted tile is loaded and nothing is in flight.</summary>
    bool DumpSettled => store != null && world != null && !busy && pending == null && building == null && loaded.Count == wanted.Count && loaded.Count > 0;

    void ExecuteDumpTick(double delta)
    {
        if (!stateDump && !uiDump)
        {
            return;
        }

        dumpClock += delta;
        if (dumpClock > 240)
        {
            SmokeFail("STATE_DUMP_TIMEOUT " + message);
            return;
        }

        if (!DumpSettled)
        {
            dumpSettle = 0;
            return;
        }

        if (++dumpSettle < 3)
        {
            return;
        }

        if (uiDump && !stateDump)
        {
            EmitUiDump();
            return;
        }

        switch (dumpStage)
        {
            case 0:
                dumpTerrain.Add(TerrainSnapshot("initial"));
                dumpStage = 1;
                dumpHop = 0;
                break;

            case 1:
                if (dumpHop < DumpHops.Length)
                {
                    Teleport(DumpHops[dumpHop++]);
                    dumpSettle = 0;
                    dumpStage = 2;
                    return;
                }

                dumpStage = 3;
                break;

            case 2:
                dumpTerrain.Add(TerrainSnapshot($"hop{dumpHop}"));
                dumpStage = dumpHop < DumpHops.Length ? 1 : 3;
                break;

            case 3:
                sky.Playing = false;
                if (dumpPendingHour is { } sampled)
                {
                    dumpLighting.Add(LightingSnapshot(sampled));
                    dumpPendingHour = null;
                }

                if (dumpHourIndex < dumpHours.Count)
                {
                    double hour = dumpHours[dumpHourIndex++];
                    sky.SetTimeOfDay(hour);
                    dumpPendingHour = hour;
                    dumpSettle = 0;
                    return;
                }

                dumpStage = 4;
                break;

            case 4:
                EmitStateDump();
                break;
        }

        dumpSettle = 0;
    }

    object TerrainSnapshot(string label)
    {
        var (zone, continent) = CurrentLocation();
        return new
        {
            label,
            map = currentMapId,
            camera = JsonRound(camera.Position),
            tile = new { x = CameraTile().X, y = CameraTile().Y },
            wanted = Keys(wanted),
            loaded = Keys(loaded.Keys),
            failed = Keys(failed),
            textures = renderer.TextureCount,
            zone,
            continent
        };
    }

    object LightingSnapshot(double hour)
    {
        var fog = sky.ClientFogValues;
        var height = sky.ClientFogHeightValues;
        return new
        {
            hour = JsonRound(hour),
            active = sky.ActiveLights,
            fog = new { start = JsonRound(fog.X), end = JsonRound(fog.Y), density = JsonRound(fog.Z), mode = JsonRound(fog.W) },
            heightFog = new { plane = JsonRound(height.X), scaler = JsonRound(height.Y), density = JsonRound(height.Z), enabled = JsonRound(height.W) },
            state = sky.Describe()
        };
    }

    void EmitStateDump()
    {
        var document = new
        {
            profile = new { kind = profile.Kind.ToString(), title = profile.Title },
            client = new
            {
                databaseEnabled,
                maps = mapList.OrderBy(m => m.Id).Select(m => new { m.Id, m.Name, m.Directory, m.Continent })
            },
            terrain = dumpTerrain,
            lighting = dumpLighting,
            ground = GroundSamples(),
            picks = PickSamples(),
            exports = ExportSamples(),
            ui = uiDump ? UiTree() : null
        };
        string json = JsonSerializer.Serialize(document, JsonOptions);
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://artifacts/state-{profile.Kind}.json"), json + "\n");
        GD.Print("STATE_DUMP_BEGIN");
        GD.Print(json);
        GD.Print("STATE_DUMP_END");
        dumpStage = 4;
        GetTree().Quit();
    }

    void EmitUiDump()
    {
        string json = JsonSerializer.Serialize(UiTree(), JsonOptions);
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://artifacts/ui-{profile.Kind}.json"), json + "\n");
        GD.Print("UI_DUMP_BEGIN");
        GD.Print(json);
        GD.Print("UI_DUMP_END");
        dumpStage = 4;
        GetTree().Quit();
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    static double JsonRound(double value) => Math.Round(value, 4);
    static float JsonRound(float value) => MathF.Round(value, 4);
    static object JsonRound(Vector3 value) => new { x = JsonRound(value.X), y = JsonRound(value.Y), z = JsonRound(value.Z) };

    static object[] Keys(IEnumerable<TileKey> keys) => keys
        .OrderBy(k => k.X)
        .ThenBy(k => k.Y)
        .Select(k => (object)new { x = k.X, y = k.Y })
        .ToArray();

    /// <summary>Eight fixed world points around the settled camera; height is null when nothing is hit.</summary>
    object[] GroundSamples()
    {
        var origin = camera.Position;
        (string Name, Vector3 Offset)[] points =
        [
            ("center", Vector3.Zero),
            ("north", new Vector3(0, 0, -80)),
            ("south", new Vector3(0, 0, 80)),
            ("east", new Vector3(80, 0, 0)),
            ("west", new Vector3(-80, 0, 0)),
            ("above", new Vector3(0, 40, 0)),
            ("below", new Vector3(0, -40, 0)),
            ("corner", new Vector3(60, 0, 60))
        ];
        return points.Select(p =>
        {
            var hit = GroundAt(origin + p.Offset);
            return (object)new
            {
                p.Name,
                point = JsonRound(origin + p.Offset),
                height = hit.Height is { } h ? (double?)JsonRound(h) : null,
                hit.Message
            };
        }).ToArray();
    }

    /// <summary>Ray and terrain hit at eight fixed screen fractions. Object picks need a database, so this pins projection and terrain only.</summary>
    object[] PickSamples()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        return DumpScreenPoints.Select(f =>
        {
            var ray = PickRay.From(camera, new Vector2(viewport.X * f.X, viewport.Y * f.Y));
            var hit = TerrainRayHit(ray);
            return (object)new
            {
                screen = new { x = JsonRound(f.X), y = JsonRound(f.Y) },
                origin = JsonRound(ray.Origin),
                direction = JsonRound(ray.Direction),
                terrain = hit is { } value ? JsonRound(value) : null
            };
        }).ToArray();
    }

    /// <summary>A fixed draft set so the exported SQL text and file grouping are pinned without a database.</summary>
    object[] ExportSamples()
    {
        static GraveyardRecord Site(double x, string comment) => new(4, 0, x, 2, 3, comment, []);
        PendingChange[] changes =
        [
            new("dump-gy-a", PendingKind.Graveyard, "dump_a", "Graveyard 4",
                new GraveyardEdit("dump_a", 4, Site(1, "Dump"), Site(9, "Dump moved")), null),
            new("dump-gy-b", PendingKind.Graveyard, "dump_b", "Graveyard 7",
                new GraveyardEdit("dump_b", 7, Site(4, "Other"), Site(6, "Other moved")), null),
            new("dump-wp-a", PendingKind.Waypoint, "dump_a", "Path 7 point 1", null, null,
                new WaypointEdit("dump_a", 7, 1, new(1, 2, 3), new(4, 5, 6))),
            new("dump-wp-b", PendingKind.Waypoint, "dump_b", "Path 9 point 2", null, null,
                new WaypointEdit("dump_b", 9, 2, new(-1, 0, 1), new(-2, 0, 2)))
        ];
        return BuildSqlExports(changes, SourceDialects.SchemaFor)
            .Select(e => (object)new
            {
                source = e.Source,
                changes = e.Scripts.Changes,
                apply = e.Scripts.Apply,
                revert = e.Scripts.Revert
            })
            .ToArray();
    }

    /// <summary>The built control tree, in creation order. Label text is deliberately omitted because it is live.</summary>
    object UiTree() => new { nodes = Walk(this, "").ToArray() };

    static IEnumerable<object> Walk(Node node, string parent)
    {
        string path = parent.Length == 0 ? node.Name : $"{parent}/{node.Name}";
        if (node is Control control)
        {
            yield return new
            {
                path,
                type = control.GetType().Name,
                control.Name,
                text = ControlText(control),
                visible = control.Visible,
                disabled = control is BaseButton button ? button.Disabled : (bool?)null,
                tooltip = control.TooltipText
            };
        }

        if (node is PopupMenu popup)
        {
            for (int i = 0; i < popup.ItemCount; i++)
            {
                yield return new
                {
                    path = $"{path}/item[{i}]",
                    type = "PopupItem",
                    name = popup.GetItemText(i),
                    text = popup.GetItemText(i),
                    visible = true,
                    disabled = (bool?)popup.IsItemDisabled(i),
                    tooltip = popup.GetItemAccelerator(i).ToString()
                };
            }
        }

        foreach (var child in node.GetChildren())
        {
            foreach (var item in Walk(child, path))
            {
                yield return item;
            }
        }
    }

    static string ControlText(Control control) => control switch
    {
        Label => "",
        OptionButton option => string.Join("|", Enumerable.Range(0, option.ItemCount).Select(option.GetItemText)),
        SpinBox spin => spin.Value.ToString("0.###", CultureInfo.InvariantCulture),
        LineEdit line => line.Text,
        Button button => button.Text,
        _ => ""
    };
}
