using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>TC trainer checks from ObjectMgr::LoadTrainers and the trainer / trainer_spell schema.</summary>
public static class TcTrainerRules
{
    /// <summary>trainer.Type per trinitycore.info: 0 none, 1 talent, 2 tradeskill (the default), 3 pet.</summary>
    public static readonly string[] TypeNames = ["None", "Talent", "Tradeskill", "Pet"];

    public static IEnumerable<InteractionIssue> Check(RowSet rows, Func<ClientRecord, uint, bool>? exists = null, Func<uint, bool>? trainerExists = null)
    {
        foreach (var link in rows.All(TcTables.CreatureTrainer))
        {
            string where = $"Trainer binding {TcTables.CreatureTrainer.KeyOf(link)}";
            uint trainer = link.U("TrainerID");
            if (trainer == 0)
                yield return new(true, $"{where}: enter a trainer ID.");
            else if (trainerExists != null && rows.Get(TcTables.Trainer, trainer.ToString(CultureInfo.InvariantCulture)) == null && !trainerExists(trainer))
                yield return new(true, $"{where}: trainer {trainer} does not exist.");
            uint menu = link.U("MenuID"), option = link.U("OptionID");
            if ((menu != 0 || option != 0) && !rows.All(TcTables.GossipOption).Any(o => o.U("MenuID") == menu && o.U("OptionID") == option))
                yield return new(true, $"{where}: gossip menu {menu} has no option {option}.");
        }

        foreach (var row in rows.All(TcTables.Trainer))
        {
            if (row.U("Type") >= TypeNames.Length)
                yield return new(true, $"Trainer {row.U("Id")}: type must be None, Talent, Tradeskill or Pet.");
        }

        foreach (var row in rows.All(TcTables.TrainerSpell))
        {
            string key = TcTables.TrainerSpell.KeyOf(row);
            InteractionIssue Error(string message) => new(true, $"Trainer spell {key}: {message}");
            if (row.U("SpellId") == 0)
                yield return Error("enter a spell ID.");
            else if (exists != null && !exists(ClientRecord.Spell, row.U("SpellId")))
                yield return Error("spell does not exist in the client.");
            if (row.U("ReqSkillLine") != 0 && exists != null && !exists(ClientRecord.SkillLine, row.U("ReqSkillLine")))
                yield return Error("required skill line does not exist in the client.");
            if (row.U("ReqLevel") > byte.MaxValue)
                yield return Error("required level must fit an unsigned byte.");
            for (int i = 1; i <= 3; i++)
            {
                uint ability = row.U($"ReqAbility{i}");
                if (ability != 0 && exists != null && !exists(ClientRecord.Spell, ability))
                    yield return Error(string.Create(CultureInfo.InvariantCulture, $"required ability {i} is not a spell in the client."));
            }
        }
    }
}
