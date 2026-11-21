using ACWorldEdit;
using ACWorldEdit.Assets;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The consumer-facing world facade. Opening a map must replace the previous world instead of
/// streaming over it (both continents share the same tile grid, so a stale window would keep the
/// old continent's terrain), and disposal must release the world instead of leaking its caches.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WorldViewTests
{
    static WorldView Build()
    {
        var parent = AutoFree(new Node3D())!;
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(parent);
        var camera = AutoFree(new Camera3D())!;
        parent.AddChild(camera);
        return new WorldView(parent, camera);
    }

    /// <summary>A WDT with no global WMO and the given terrain tiles present.</summary>
    static WorldMap Wdt(string name, params (int X, int Y)[] tiles)
    {
        var main = new byte[64 * 64 * 8];
        foreach (var (x, y) in tiles)
        {
            BitConverter.GetBytes(1u).CopyTo(main, (y * 64 + x) * 8);
        }

        // Chunk tags are stored byte-reversed ("NIAM") in the client files.
        return new WorldMap(name, [78, 73, 65, 77, .. BitConverter.GetBytes(main.Length), .. main]);
    }

    [TestCase]
    public async Task OpeningASecondMapReplacesTheWindow()
    {
        var view = Build();
        view.OpenMap(Wdt("Azeroth", (0, 0)), 0);
        int generation = view.Streamer.Generation;

        view.OpenMap(Wdt("Kalimdor", (1, 1)), 1);

        AssertString(view.Map!.Name).IsEqual("Kalimdor");
        AssertThat(view.MapId == 1).IsTrue();
        AssertThat(view.Sky.ActiveMapId == 1).IsTrue();
        AssertThat(view.Streamer.Generation).IsEqual(generation + 1);
        await view.DisposeAsync();
    }

    [TestCase]
    public async Task ReleasingTheClientReplacesTheWindow()
    {
        var view = Build();
        view.OpenMap(Wdt("Azeroth", (0, 0)), 0);
        int generation = view.Streamer.Generation;

        await view.SetClientAsync(null);

        AssertThat(view.Map).IsNull();
        AssertThat(view.Streamer.Generation).IsEqual(generation + 1);
        await view.DisposeAsync();
    }

    [TestCase]
    public async Task DisposingReleasesTheWorld()
    {
        var view = Build();
        view.OpenMap(Wdt("Azeroth", (0, 0)), 0);

        await view.DisposeAsync();

        AssertThat(view.Map).IsNull();
        AssertThat(view.Files).IsNull();

        // Disposal is idempotent: a second await must not throw or double-free.
        await view.DisposeAsync();
    }
}
