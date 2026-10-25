namespace ACWorldEdit;

/// <summary>
/// The sections of the creature properties window, in picker order (alphabetical by title). Adding a
/// feature to the window is one section class plus one line here; the window, context and tests need
/// no other change. The window lists the sections that support the open database's dialect
/// (<see cref="CreatureSection.Supports"/>): the AzerothCore pages, then the TrinityCore ones.
/// </summary>
public static class CreatureSectionRegistry
{
    public static IReadOnlyList<Func<CreatureContext, CreatureSection>> Factories { get; } =
    [
        ctx => new AcBehaviorSection(ctx),
        ctx => new AcStatsSection(ctx),
        ctx => new AcGossipSection(ctx),
        ctx => new AcQuestsSection(ctx),
        ctx => new AcSpeechSection(ctx),
        ctx => new AcTemplateSection(ctx),
        ctx => new AcTrainerSection(ctx),
        ctx => new AcVendorSection(ctx),
        ctx => new TcGossipSection(ctx),
        ctx => new TcQuestsSection(ctx),
        ctx => new TcTemplateSection(ctx),
        ctx => new TcTrainerSection(ctx),
        ctx => new TcVendorSection(ctx)
    ];

    public static List<CreatureSection> Create(CreatureContext ctx) => Factories.Select(factory => factory(ctx)).ToList();
}
