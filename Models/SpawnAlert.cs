namespace EveCompanionApp.Models;

public enum SpawnType
{
    /// <summary>Faction rats: Dread Guristas, Shadow Serpentis, Domination, True Sansha, Dark Blood, Sentient.</summary>
    Faction,
    Officer,
    Drifter,
    Sleeper,
}

/// <summary>
/// A notable NPC spotted in the combat log.
/// </summary>
/// <param name="NpcName">The NPC's name as it appears in the log.</param>
/// <param name="Type">Which class of notable spawn it is.</param>
/// <param name="Timestamp">EVE time (UTC) of the combat line that revealed it.</param>
/// <param name="Character">Pilot whose log reported it.</param>
public sealed record SpawnAlert(string NpcName, SpawnType Type, DateTime Timestamp, string Character = "");
