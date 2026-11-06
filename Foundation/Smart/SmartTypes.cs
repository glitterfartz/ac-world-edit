namespace ACWorldEdit.Foundation;

/// <summary>
/// How a SmartAI parameter is edited and shown (pickers, units, names).
/// </summary>
public enum SmartParamKind
{
    Number,
    Ms,
    Percent,
    Yards,
    Bool,
    Flags,
    Spell,
    Creature,
    GameObject,
    Quest,
    Item,
    TextGroup,
    ActionList,
    Emote,
    TextEmote,
    FactionTemplate
}

public sealed record SmartParam(string Label, SmartParamKind Kind)
{
    /// <summary>
    /// The hover text that explains this parameter: its unit or reference kind, plus the type's own
    /// server help when there is one. 0 is always called out because it usually means none or any.
    /// </summary>
    public string Tip(string typeName = "", string typeHelp = "")
    {
        string meaning = Kind switch
        {
            SmartParamKind.Ms => "Time in seconds; the database stores it in milliseconds.",
            SmartParamKind.Percent => "A percentage from 0 to 100.",
            SmartParamKind.Yards => "A distance in yards.",
            SmartParamKind.Bool => "Off (0) or on (1).",
            SmartParamKind.Flags => "A bit mask; type decimal or 0x hex.",
            SmartParamKind.Spell => "The spell ID used here; choose it by name.",
            SmartParamKind.Creature => "The creature entry ID used here.",
            SmartParamKind.GameObject => "The game object entry ID used here.",
            SmartParamKind.Quest => "The quest ID used here.",
            SmartParamKind.Item => "The item ID used here.",
            SmartParamKind.TextGroup => "The creature_text group the creature speaks; edit its lines on the Speech page.",
            SmartParamKind.ActionList => "The action list ID to run; an action list runs its steps in order.",
            SmartParamKind.Emote => "The emote ID the creature plays.",
            SmartParamKind.TextEmote => "The text-emote ID a player must perform.",
            SmartParamKind.FactionTemplate => "The FactionTemplate ID used for the check.",
            _ => "A whole number; the type help under the rule explains what it means."
        };
        bool reference = Kind is SmartParamKind.Spell or SmartParamKind.Creature or SmartParamKind.GameObject
            or SmartParamKind.Quest or SmartParamKind.Item or SmartParamKind.TextGroup or SmartParamKind.ActionList
            or SmartParamKind.Emote or SmartParamKind.TextEmote or SmartParamKind.FactionTemplate;
        string help = typeHelp.Length > 0 ? $" {typeName}: {typeHelp}." : "";
        return $"{Label}: {meaning}{help}{(reference ? " 0 often means none." : "")}";
    }
}

/// <summary>
/// One SmartAI event, action or target type. Sentence uses {1}..{7} for parameters ({x}/{y}/{z}
/// for target coordinates). ParamsKnown is false when the server comment is free text: the editor
/// then shows all raw parameters with the comment as help.
/// </summary>
public sealed record SmartType(int Id, string Code, string Name, string Sentence, string Help, SmartParam[] Params, bool ParamsKnown);
