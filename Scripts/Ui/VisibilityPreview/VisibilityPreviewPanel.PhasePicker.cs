using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The phase dropdown: one checkable item per phase the loaded area uses, each with how many stored spawns sit in it.
/// AzerothCore phases are mask bits, so any number can be ticked; TrinityCore previews one phase at a time and names
/// it from `phase_name`. The typed mask or phase ID underneath stays the exact value, and the two follow each other.
/// </summary>
public sealed partial class VisibilityPreviewPanel
{
    MenuButton phasePicker = null!;
    PhaseCatalog phaseCatalog = PhaseCatalog.Empty;

    void BuildPhasePicker()
    {
        phasePicker = new MenuButton
        {
            Name = "PhasePicker",
            Text = "Phases…",
            CustomMinimumSize = new(0, 32),
            TooltipText = "The phases used by spawns in the loaded area, with how many spawns each holds."
        };
        AddChild(phasePicker);
        var menu = phasePicker.GetPopup();
        menu.MaxSize = new(380, 500);
        menu.IdPressed += id =>
        {
            if (Trinity)
            {
                SetPhaseId((uint)id);
            }
            else
            {
                int number = (int)id;
                SetMask(AcPhaseMasks.With(CurrentMask(), number, !AcPhaseMasks.Has(CurrentMask(), number)));
            }
        };
    }

    /// <summary>The loaded area's phase counts changed: list them (the list is left alone while the dropdown is open).</summary>
    public void SetPhaseCatalog(PhaseCatalog catalog)
    {
        if (catalog == phaseCatalog || (catalog.Phases.SequenceEqual(phaseCatalog.Phases) && catalog.EveryPhase == phaseCatalog.EveryPhase))
        {
            return;
        }

        phaseCatalog = catalog;
        RefreshPhasePicker();
    }

    /// <summary>List the area's phases plus any the typed value names, tick the chosen ones and say them on the button.</summary>
    void RefreshPhasePicker()
    {
        var menu = phasePicker.GetPopup();
        if (!menu.Visible)
        {
            menu.Clear();
            menu.HideOnCheckableItemSelection = Trinity;
            foreach (var (id, text) in Trinity ? TrinityEntries() : AzerothEntries())
            {
                if (Trinity)
                {
                    menu.AddRadioCheckItem(text, (int)id);
                }
                else
                {
                    menu.AddCheckItem(text, (int)id);
                }
            }

            if (!Trinity && phaseCatalog.EveryPhase > 0)
            {
                menu.AddSeparator();
                menu.AddItem($"{phaseCatalog.EveryPhase:N0} spawns are visible in every phase");
                menu.SetItemDisabled(menu.ItemCount - 1, true);
            }
        }

        uint chosen = Trinity ? CurrentPhaseId() : CurrentMask();
        for (int i = 0; i < menu.ItemCount; i++)
        {
            if (menu.IsItemCheckable(i))
            {
                uint id = (uint)menu.GetItemId(i);
                menu.SetItemChecked(i, Trinity ? id == chosen : AcPhaseMasks.Has(chosen, (int)id));
            }
        }

        phasePicker.Text = Trinity ? TrinityPhaseLabel(chosen) : chosen == 0 ? "No phase selected" : Capitalized(AcPhaseMasks.Describe(chosen));
    }

    uint CurrentPhaseId() => uint.TryParse(phase.Text.Trim(), out uint id) ? id : 0;

    static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Phase 1 first, then every phase the area uses or the typed mask names, each with its spawn count.</summary>
    IEnumerable<(uint Id, string Text)> AzerothEntries()
    {
        uint mask = CurrentMask();
        var ids = phaseCatalog.Phases.Select(p => p.Id).Concat(AcPhaseMasks.Phases(mask).Select(n => (uint)n)).Append(1u).Distinct().Order();
        foreach (uint id in ids)
        {
            var info = phaseCatalog.Phases.FirstOrDefault(p => p.Id == id);
            yield return (id, $"Phase {id}" + (id == 1 ? " (normal world)" : "") + CountSuffix(info?.Spawns ?? 0));
        }
    }

    /// <summary>"No phase" first, then each phase the area uses (or the typed ID) by name, with its spawn count.</summary>
    IEnumerable<(uint Id, string Text)> TrinityEntries()
    {
        yield return (0, "No phase (0)  ·  spawns outside any phase");
        uint chosen = CurrentPhaseId();
        var infos = phaseCatalog.Phases.Where(p => p.Id != 0).ToList();
        if (chosen != 0 && infos.All(p => p.Id != chosen))
        {
            infos.Add(new(chosen, "", 0));
        }

        foreach (var info in infos.OrderBy(p => p.Id))
        {
            yield return (info.Id, $"{(info.Name.Length > 0 ? info.Name : "Phase " + info.Id)} ({info.Id}){CountSuffix(info.Spawns)}");
        }
    }

    string TrinityPhaseLabel(uint id) => id == 0 ? "No phase (0)"
        : phaseCatalog.Phases.FirstOrDefault(p => p.Id == id) is { Name.Length: > 0 } named ? $"{named.Name} ({id})" : $"Phase {id}";

    /// <summary>Counts are left off until the first area has been read, so "no spawns here" never means "not counted yet".</summary>
    string CountSuffix(int spawns) => phaseCatalog.Phases.Length > 0 || phaseCatalog.EveryPhase > 0 ? "  ·  " + SpawnCount(spawns) : "";

    static string SpawnCount(int spawns) => spawns == 0 ? "no spawns here" : spawns == 1 ? "1 spawn" : $"{spawns:N0} spawns";
}
