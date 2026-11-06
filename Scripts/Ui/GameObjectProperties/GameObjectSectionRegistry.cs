namespace ACWorldEdit;

/// <summary>
/// The pages of the GameObject properties window, in picker order. Adding a page is one class
/// plus one line here; the window frame, context and tests need no other change.
/// </summary>
public static class GameObjectSectionRegistry
{
    public static IReadOnlyList<Func<GameObjectContext, GameObjectSection>> Factories { get; } =
    [
        ctx => new GameObjectTemplateSection(ctx),
        ctx => new GameObjectTypeSection(ctx),
        ctx => new GameObjectQuestsSection(ctx),
        ctx => new GameObjectSpawnsSection(ctx)
    ];

    public static List<GameObjectSection> Create(GameObjectContext ctx) => Factories.Select(factory => factory(ctx)).ToList();
}
