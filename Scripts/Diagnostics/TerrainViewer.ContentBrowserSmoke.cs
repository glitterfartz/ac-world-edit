using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --content-browser-smoke (windowed or headless; needs the world database): Ctrl+B opens the
/// content browser, which lists named creature templates; a search finds Hogger by name, double-clicking
/// him opens the creature properties window on his entry, the GameObject tab filters Mailboxes by type,
/// opening one names it on the status bar, and Ctrl+B closes the browser. Read-only throughout.
/// </summary>
public partial class TerrainViewer
{
    bool contentBrowserSmoke;
    readonly SmokeSteps contentBrowserRun = new("CONTENT_BROWSER", 30, 180, 3);
    List<(string Name, Func<bool> Step)>? contentBrowserSteps;

    const uint SmokeHogger = 448;
    const uint SmokeMailbox = 32349;
    const int MailboxType = 19;
    const int HumanoidType = 7;

    void ContentBrowserSmoke(double delta)
    {
        shotPrefix = "content-browser";
        contentBrowserSteps ??= ContentBrowserSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        string Where() => $"visible={contentBrowser.Visible} creatures={contentBrowser.SmokeResults(SpawnKind.Creature).Count} objects={contentBrowser.SmokeResults(SpawnKind.GameObject).Count} window={creatureProps.Visible}/{creatureProps.SmokeEntry} status='{statusLog.Latest}'";
        contentBrowserRun.Advance(delta, contentBrowserSteps, !busy && world != null, Where, SmokeFail, () => GetTree().Quit(), waiting: Where);
    }

    /// <summary>
    /// Windowed: wait until rendered thumbnails replace the blank tiles and their PNGs are cached, then
    /// screenshot the browser. Headless has no renderer, so there is nothing to wait for.
    /// </summary>
    bool Thumbnailed(SpawnKind kind, string shotName)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return true;
        }

        int wanted = Math.Min(3, contentBrowser.SmokeRows(kind));
        if (contentBrowser.SmokeThumbnails(kind) < wanted)
        {
            return false;
        }

        // The smoke cache starts empty, so each tab's thumbnails must have been rendered by this run.
        Require(thumbnails!.Rendered > renderedThumbnails, $"the {kind} thumbnails were not rendered by this run");
        renderedThumbnails = thumbnails.Rendered;
        Require(System.IO.Directory.GetFiles(thumbnails.Directory, "*.png").Length == renderedThumbnails, $"not every rendered thumbnail was saved in {thumbnails.Directory}");
        Screenshot(shotName);
        return true;
    }

    int renderedThumbnails;

    List<(string, Func<bool>)> ContentBrowserSteps() =>
    [
        ("open", () =>
        {
            Require(databaseEnabled, "the content browser smoke needs a world database connection");
            SmokeKey(Key.B, ctrl: true);
            return true;
        }),
        ("listed", () =>
        {
            Require(contentBrowser.Visible, "Ctrl+B did not open the content browser");
            var listed = contentBrowser.SmokeResults(SpawnKind.Creature);
            if (listed.Count == 0)
            {
                return false;
            }

            Require(listed.All(h => h.Name != ""), "the empty search listed an unnamed template");
            Require(contentBrowser.SmokeRows(SpawnKind.Creature) == listed.Count, "the results list does not show every hit");
            contentBrowser.SmokeSearch(SpawnKind.Creature, "Hogger", null);
            return true;
        }),
        ("hogger", () =>
        {
            var found = contentBrowser.SmokeResults(SpawnKind.Creature);
            if (!found.Any(h => h.Entry == SmokeHogger) || !found.All(h => h.Name.Contains("Hogger", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            Require(found[0].Name == "Hogger", $"the exact name should sort first, got '{found[0].Name}'");
            return true;
        }),
        ("creature-thumbnails", () => Thumbnailed(SpawnKind.Creature, "creatures")),
        ("open-hogger", () =>
        {
            Require(contentBrowser.SmokeActivate(SpawnKind.Creature, SmokeHogger), "Hogger is not listed");
            return true;
        }),
        ("hogger-window", () =>
        {
            if (!creatureProps.Visible || creatureProps.SmokeEntry != SmokeHogger || !creatureProps.SmokeLoaded)
            {
                return false;
            }

            creatureProps.Visible = false;
            contentBrowser.SmokeSearch(SpawnKind.GameObject, "Mailbox", MailboxType);
            return true;
        }),
        ("mailboxes", () =>
        {
            var found = contentBrowser.SmokeResults(SpawnKind.GameObject);
            if (!found.Any(h => h.Entry == SmokeMailbox))
            {
                return false;
            }

            Require(found.All(h => h.Type == MailboxType), "the type filter let another GameObject type through");
            return true;
        }),
        ("object-thumbnails", () => Thumbnailed(SpawnKind.GameObject, "objects")),
        ("open-mailbox", () =>
        {
            Require(contentBrowser.SmokeActivate(SpawnKind.GameObject, SmokeMailbox), "the Mailbox is not listed");
            Require(statusLog.Latest.StartsWith($"Game object {SmokeMailbox}", StringComparison.Ordinal), $"opening a GameObject said '{statusLog.Latest}'");
            // Searches on both tabs in one frame: neither may cancel the other.
            contentBrowser.SmokeSearch(SpawnKind.Creature, "Hogger", HumanoidType);
            contentBrowser.SmokeSearch(SpawnKind.GameObject, "Mailbox", null);
            return true;
        }),
        ("both-tabs", () =>
        {
            if (contentBrowser.SmokeSearching(SpawnKind.Creature) || contentBrowser.SmokeSearching(SpawnKind.GameObject))
            {
                return false;
            }

            Require(contentBrowser.SmokeResults(SpawnKind.Creature).All(h => h.Type == HumanoidType), "the creature tab did not finish its own search");
            Require(contentBrowser.SmokeResults(SpawnKind.GameObject).Any(h => h.Entry == SmokeMailbox), "the GameObject tab did not finish its own search");
            SmokeKey(Key.B, ctrl: true);
            return true;
        }),
        ("closed", () =>
        {
            Require(!contentBrowser.Visible, "Ctrl+B did not close the content browser");
            return true;
        })
    ];
}
