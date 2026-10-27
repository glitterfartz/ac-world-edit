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

public sealed record SmartParam(string Label, SmartParamKind Kind);

/// <summary>
/// One SmartAI event, action or target type. Sentence uses {1}..{7} for parameters ({x}/{y}/{z}
/// for target coordinates). ParamsKnown is false when the server comment is free text: the editor
/// then shows all raw parameters with the comment as help.
/// </summary>
public sealed record SmartType(int Id, string Code, string Name, string Sentence, string Help, SmartParam[] Params, bool ParamsKnown);
