using System.Diagnostics.CodeAnalysis;
using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

public interface ILogParserService
{
    /// <summary>Parses a <c>(combat)</c> damage line, outgoing ("to") or incoming ("from").</summary>
    bool TryParseCombat(string line, [NotNullWhen(true)] out CombatEvent? combatEvent);

    /// <summary>Parses a <c>(bounty)</c> line, e.g. "18,750 ISK added to next bounty payout".</summary>
    bool TryParseBounty(string line, [NotNullWhen(true)] out BountyEvent? bounty);

    /// <summary>Extracts the destination solar system from a jump or undock line.</summary>
    bool TryParseLocation(string line, [NotNullWhen(true)] out string? solarSystem);

    /// <summary>Classifies an entity name as a notable spawn (officer, dread, sleeper).</summary>
    bool TryDetectSpawn(string entityName, DateTime timestamp, [NotNullWhen(true)] out SpawnAlert? alert);
}
