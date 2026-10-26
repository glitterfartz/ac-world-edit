using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>TC trainer checks from ObjectMgr::LoadTrainers and the trainer / trainer_spell schema.</summary>
public static class TcTrainerRules
{
    /// <summary>Trainer.Type: 0 class, 1 tradeskill, 2 pet.</summary>
    public static readonly string[] TypeNames = ["Class", "Tradeskill", "Pet"];

    public static IEnumerable<InteractionIssue> Check(RowSet rows, Func<uint, bool>? spellExists = null)
    {
        foreach (var row in rows.All(TcTables.Trainer))
        {
            if (row.U("Type") >= TypeNames.Length)
                yield return new(true, $"Trainer {row.U("Id")}: type must be Class, Tradeskill or Pet.");
        }

        foreach (var row in rows.All(TcTables.TrainerSpell))
        {
            string key = TcTables.TrainerSpell.KeyOf(row);
            InteractionIssue Error(string message) => new(true, $"Trainer spell {key}: {message}");
            if (row.U("SpellId") == 0)
                yield return Error("enter a spell ID.");
            else if (spellExists != null && !spellExists(row.U("SpellId")))
                yield return Error("spell does not exist in the client.");
            if (row.U("ReqLevel") > byte.MaxValue)
                yield return Error("required level must fit an unsigned byte.");
            for (int i = 1; i <= 3; i++)
            {
                uint ability = row.U($"ReqAbility{i}");
                if (ability != 0 && spellExists != null && !spellExists(ability))
                    yield return Error(string.Create(CultureInfo.InvariantCulture, $"required ability {i} is not a spell in the client."));
            }
        }
    }
}
