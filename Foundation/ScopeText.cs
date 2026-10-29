namespace ACWorldEdit.Foundation;

/// <summary>How far an edit reaches: every spawn of a creature template, one spawn, or both at once.</summary>
public enum PropertyScope
{
    /// <summary>The creature template: shared by every spawn of it.</summary>
    Template,
    /// <summary>One spawn: only that spawn changes.</summary>
    Spawn,
    /// <summary>Rules that mix the two (a template's behavior plus rules written for one spawn).</summary>
    Both
}

/// <summary>
/// The wording that says what an edit reaches, in one place so the properties window's banner and the page
/// headers agree. The properties window edits the template; a spawn's own settings are in the inspector beside the world.
/// </summary>
public static class ScopeText
{
    static string Spawns(int count) => count == 1 ? "1 spawn" : $"{count} spawns";

    /// <summary>The short label above a group of pages.</summary>
    public static string Header(PropertyScope scope, uint entry, ulong guid, int spawns) => scope switch
    {
        PropertyScope.Template => $"TEMPLATE · Entry {entry} · {Spawns(spawns)}",
        PropertyScope.Spawn => guid == 0 ? "THIS SPAWN · none selected" : $"THIS SPAWN · GUID {guid}",
        _ => $"TEMPLATE AND THIS SPAWN · Entry {entry}"
    };

    /// <summary>One or two sentences saying who is affected by what is edited on a page.</summary>
    public static string Banner(PropertyScope scope, string name, uint entry, ulong guid, int spawns) => scope switch
    {
        PropertyScope.Template =>
            $"Editing the creature template. Changes apply to every spawn of {name} ({entry}, {Spawns(spawns)}); " +
            "a spawn that has its own appearance keeps it. A single spawn's settings are in the inspector on the right.",
        PropertyScope.Spawn when guid == 0 =>
            "Select a spawn in the world to edit its own settings. The inspector on the right edits one spawn at a time.",
        PropertyScope.Spawn =>
            $"This spawn only: GUID {guid}. Other spawns of this template do not change.",
        _ when guid == 0 =>
            $"Both scopes: the rules of the template ({name}, {entry}) and rules written for one spawn. Select a spawn to see its own rules too.",
        _ =>
            $"Both scopes: the rules of the template ({name}, {entry}) and the rules written for spawn {guid} only."
    };
}
