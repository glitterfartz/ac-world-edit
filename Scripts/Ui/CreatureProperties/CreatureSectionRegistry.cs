namespace ACWorldEdit;

/// <summary>
/// The sections of the creature properties window, in picker order. Adding a feature to the window
/// is one section class plus one line here; the window, context and tests need no other change.
/// </summary>
public static class CreatureSectionRegistry
{
    public static IReadOnlyList<Func<CreatureContext, CreatureSection>> Factories { get; } =
    [
        ctx => new GossipSection(ctx),
        ctx => new QuestsSection(ctx),
        ctx => new VendorSection(ctx),
        ctx => new TrainerSection(ctx),
        ctx => new BehaviorSection(ctx),
        ctx => new SpeechSection(ctx),
        ctx => new StatsSection(ctx),
        ctx => new InfoSection(ctx)
    ];

    public static List<CreatureSection> Create(CreatureContext ctx) => Factories.Select(factory => factory(ctx)).ToList();
}
