using System.Globalization;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Everywhere else in the database this GameObject template is named: spawns, SmartAI, loot and quests.</summary>
public sealed partial class GameObjectUsedBySection : GameObjectSection
{
    readonly ReferenceCountCard card = new(SpawnKind.GameObject);

    public GameObjectUsedBySection(GameObjectContext ctx) : base(ctx)
    {
        card.Bind(() => DBSource.Open(Ctx.Connection()), Ctx.Redraw);
    }

    public override string Id => "usedby";
    public override string Title => "Used by";
    public override string IconPath => "Interface/Icons/INV_Misc_QuestionMark.blp";
    public override string Tip => "Every row elsewhere in the database that names this GameObject template, so an edit here shows what it reaches.";
    public override string Badge => card.SmokeCounts is { } counts ? counts.Sum(c => c.Count).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Entry != null;

    public override void Refresh()
    {
        // The card is kept across redraws (its read is not repeated for the same entry), so only
        // the placeholder and other children go.
        foreach (var child in GetChildren())
        {
            if (child != card)
            {
                child.QueueFree();
            }
        }

        if (Ctx.Entry is not { } entry)
        {
            AddChild(Placeholder());
            return;
        }

        if (card.GetParent() != this)
        {
            card.GetParent()?.RemoveChild(card);
            AddChild(card);
        }

        card.Show(entry);
    }

    public ReferenceCountCard SmokeCard => card;
}
