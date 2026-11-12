using System.Globalization;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Everywhere else in the database this creature template is named: spawns, SmartAI, loot, vendors and quests.</summary>
public sealed partial class UsedBySection : CreatureSection
{
    readonly ReferenceCountCard card = new(SpawnKind.Creature);

    public UsedBySection(CreatureContext ctx) : base(ctx)
    {
        card.Bind(() => DBSource.Open(Ctx.Connection()), Ctx.Redraw);
    }

    public override string Id => "usedby";
    public override string Title => "Used by";
    public override bool Supports(WorldDialect dialect) => true;
    public override string IconPath => "Interface/Icons/INV_Misc_QuestionMark.blp";
    public override string Tip => "Every row elsewhere in the database that names this creature template, so an edit here shows what it reaches.";
    public override string Badge => card.SmokeCounts is { } counts ? counts.Sum(c => c.Count).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Snapshot != null;

    public override void OnCreatureChanged(CreatureSpawn? creature) => card.Reset();

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

        if (Ctx.Snapshot is not { } snapshot)
        {
            AddChild(Placeholder());
            return;
        }

        if (card.GetParent() != this)
        {
            card.GetParent()?.RemoveChild(card);
            AddChild(card);
        }

        card.Show(snapshot.Entry);
    }

    public ReferenceCountCard SmokeCard => card;
}
