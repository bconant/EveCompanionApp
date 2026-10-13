namespace EveCompanionApp.Models;

/// <summary>
/// A single damage exchange parsed from a <c>(combat)</c> game log line.
/// </summary>
/// <param name="Timestamp">EVE time (UTC) as written in the log.</param>
/// <param name="Damage">Damage applied.</param>
/// <param name="Target">The other party: the entity you hit when outgoing, or the attacker when incoming.</param>
/// <param name="Weapon">Weapon or ammo name; empty when the log omits it (common for incoming NPC fire).</param>
/// <param name="HitQuality">EVE hit quality text, e.g. "Grazes", "Hits", "Smashes", "Wrecks".</param>
/// <param name="IsCritical">True for "Wrecks", EVE's top-quality (critical) hit.</param>
/// <param name="IsOutgoing">True when you dealt the damage; false when you received it.</param>
/// <param name="Character">Pilot whose log the line came from.</param>
/// <param name="IsRareSpawn">True when the other party is a faction, officer, Drifter or Sleeper NPC.</param>
public sealed record CombatEvent(
    DateTime Timestamp,
    int Damage,
    string Target,
    string Weapon,
    string HitQuality,
    bool IsCritical,
    bool IsOutgoing,
    string Character = "",
    bool IsRareSpawn = false);
