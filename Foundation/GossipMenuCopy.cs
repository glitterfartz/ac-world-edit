namespace ACWorldEdit.Foundation;

/// <summary>
/// What copying a gossip menu for one creature has to carry besides the menu's own rows: the <c>conditions</c> that
/// gate the menu's texts and options, and the creature's own SmartAI events that listen to the menu. Both dialects
/// use the same condition source types and the same <c>GOSSIP_SELECT</c> event, so one set of rules serves both.
/// </summary>
public static class GossipMenuCopy
{
    /// <summary>conditions.SourceTypeOrReferenceId of a gossip menu's text (SourceGroup MenuID, SourceEntry TextID).</summary>
    public const int MenuText = 14;

    /// <summary>conditions.SourceTypeOrReferenceId of a gossip menu option (SourceGroup MenuID, SourceEntry OptionID).</summary>
    public const int MenuOption = 15;

    /// <summary>SMART_EVENT_GOSSIP_SELECT: event_param1 is the menu ID and event_param2 the option ID.</summary>
    public const uint GossipSelectEvent = 62;

    /// <summary>The condition source types the menu copy carries.</summary>
    public static bool IsMenuCondition(Row condition) => condition.I("SourceTypeOrReferenceId") is MenuText or MenuOption;

    /// <summary>
    /// Inserts that give <paramref name="copy"/> the conditions of <paramref name="menu"/>. A text condition
    /// follows its text to the new text ID when the copy made new texts (<paramref name="textIds"/>: old to new).
    /// </summary>
    public static RowEdit[] Conditions(TableSchema table, RowSet rows, uint menu, uint copy, IReadOnlyDictionary<uint, uint>? textIds = null) =>
        rows.All(table).Where(c => IsMenuCondition(c) && c.U("SourceGroup") == menu)
            .OrderBy(c => c.I("SourceTypeOrReferenceId")).ThenBy(c => c.L("SourceEntry")).ThenBy(c => c.U("ElseGroup")).ThenBy(c => c.I("ConditionTypeOrReference"))
            .Select(c =>
            {
                var moved = c.With("SourceGroup", copy);
                if (c.I("SourceTypeOrReferenceId") == MenuText && textIds != null && textIds.TryGetValue((uint)c.I("SourceEntry"), out uint text))
                {
                    moved = moved.With("SourceEntry", (int)text);
                }

                return InteractionEdits.Insert(table, moved);
            }).ToArray();

    /// <summary>
    /// Updates that point the creature's own (source type 0) <c>GOSSIP_SELECT</c> events at the copy instead of
    /// <paramref name="menu"/>. The copy keeps the original's option IDs, so the events keep working; rules of
    /// other creatures that share the original menu are not touched.
    /// </summary>
    public static RowEdit[] RepointSelectEvents(TableSchema smart, RowSet rows, uint entry, uint menu, uint copy) =>
        rows.All(smart).Where(r => r.U("source_type") == 0 && r.L("entryorguid") == entry && r.U("event_type") == GossipSelectEvent && r.U("event_param1") == menu)
            .OrderBy(r => r.U("id")).ThenBy(r => r.U("link"))
            .Select(r => InteractionEdits.Update(smart, r, r.With("event_param1", copy))).ToArray();
}
