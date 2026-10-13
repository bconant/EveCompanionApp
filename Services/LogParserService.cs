using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

/// <summary>
/// Parses EVE Online game log lines, e.g.
/// <c>[ 2026.10.04 18:32:11 ] (combat) &lt;color=0xff00ffff&gt;&lt;b&gt;523&lt;/b&gt; ... to ... Guristas Pithi ... - Heavy Missile - Hits</c>.
/// Markup is stripped before matching, so only the plain-text shape matters.
/// </summary>
public sealed partial class LogParserService : ILogParserService
{
    private const string TimestampFormat = "yyyy.MM.dd HH:mm:ss";

    // "Wrecks" is EVE's best hit quality, the closest thing it has to a critical hit.
    private static readonly HashSet<string> CriticalQualities = new(StringComparer.OrdinalIgnoreCase) { "Wrecks" };

    private static readonly string[] OfficerNames =
    [
        // Angel Cartel
        "Gotan Kreiss", "Hakim Stormare", "Mizuro Cybon", "Tobias Kruzhor",
        // Blood Raiders
        "Ahremen Arkah", "Draclira Merlonne", "Raysere Giant", "Tairei Namazoth",
        // Guristas
        "Estamel Tharchon", "Kaikka Peunato", "Thon Eney", "Vepas Minimala",
        // Sansha's Nation
        "Brokara Ryver", "Chelm Soran", "Selynne Mardakar", "Vizan Ankonin",
        // Serpentis
        "Brynn Jerdola", "Cormack Vaaja", "Setele Schellan", "Tuvan Orth",
        // Rogue Drones
        "Unit D-34343", "Unit F-435454", "Unit P-343554", "Unit W-634",
    ];

    // Faction spawns lead with one of these, e.g. "Dread Guristas Irregular".
    private static readonly string[] FactionPrefixes =
        ["Dread Guristas", "Shadow Serpentis", "Domination", "True Sansha", "Dark Blood", "Sentient"];

    private static readonly string[] SleeperPrefixes = ["Sleepless", "Awakened", "Emergent"];

    [GeneratedRegex(@"^\[\s*(?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})\s*\]\s*\((?<kind>\w+)\)\s*(?<msg>.*)$")]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^(?<dmg>\d+)\s+(?<dir>to|from)\s+(?<who>.+?)(?:\s+-\s+(?<weapon>.+?))?\s+-\s+(?<quality>[^-]+?)$")]
    private static partial Regex DamagePattern();

    [GeneratedRegex(@"^(?<amount>\d[\d,]*(?:\.\d+)?)\s*ISK\b")]
    private static partial Regex BountyPattern();

    [GeneratedRegex(@"^Jumping from .+? to (?<to>.+?)\.?$")]
    private static partial Regex JumpPattern();

    [GeneratedRegex(@"^Undocking from .+? to (?<to>.+?) solar system\.?$")]
    private static partial Regex UndockPattern();

    public bool TryParseCombat(string line, [NotNullWhen(true)] out CombatEvent? combatEvent)
    {
        combatEvent = null;

        if (!TrySplitLine(line, out var timestamp, out var kind, out var message)
            || !kind.Equals("combat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = DamagePattern().Match(message);
        if (!match.Success || !int.TryParse(match.Groups["dmg"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var damage))
        {
            return false;
        }

        var quality = match.Groups["quality"].Value;
        combatEvent = new CombatEvent(
            timestamp,
            damage,
            Target: match.Groups["who"].Value,
            Weapon: match.Groups["weapon"].Value,
            HitQuality: quality,
            IsCritical: CriticalQualities.Contains(quality),
            IsOutgoing: match.Groups["dir"].Value == "to");
        return true;
    }

    public bool TryParseBounty(string line, [NotNullWhen(true)] out BountyEvent? bounty)
    {
        bounty = null;

        if (!TrySplitLine(line, out var timestamp, out var kind, out var message)
            || !kind.Equals("bounty", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = BountyPattern().Match(message);
        if (!match.Success
            || !decimal.TryParse(
                match.Groups["amount"].ValueSpan,
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return false;
        }

        bounty = new BountyEvent(timestamp, (long)Math.Round(amount));
        return true;
    }

    public bool TryParseLocation(string line, [NotNullWhen(true)] out string? solarSystem)
    {
        solarSystem = null;

        if (!TrySplitLine(line, out _, out _, out var message))
        {
            return false;
        }

        var match = JumpPattern().Match(message);
        if (!match.Success)
        {
            match = UndockPattern().Match(message);
        }

        if (!match.Success)
        {
            return false;
        }

        solarSystem = match.Groups["to"].Value;
        return true;
    }

    public bool TryDetectSpawn(string entityName, DateTime timestamp, [NotNullWhen(true)] out SpawnAlert? alert)
    {
        SpawnType? type =
            OfficerNames.Any(n => entityName.Contains(n, StringComparison.OrdinalIgnoreCase)) ? SpawnType.Officer
            : entityName.Contains("Drifter", StringComparison.OrdinalIgnoreCase) ? SpawnType.Drifter
            : SleeperPrefixes.Any(p => StartsWithWord(entityName, p))
                || entityName.Contains("Sleeper", StringComparison.OrdinalIgnoreCase) ? SpawnType.Sleeper
            : FactionPrefixes.Any(p => StartsWithWord(entityName, p)) ? SpawnType.Faction
            : null;

        alert = type is { } t ? new SpawnAlert(entityName, t, timestamp) : null;
        return alert is not null;
    }

    private static bool StartsWithWord(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (text.Length == prefix.Length || !char.IsLetterOrDigit(text[prefix.Length]));

    private static bool TrySplitLine(string line, out DateTime timestamp, out string kind, out string message)
    {
        timestamp = default;
        kind = string.Empty;
        message = string.Empty;

        var match = LinePattern().Match(line);
        if (!match.Success
            || !DateTime.TryParseExact(
                match.Groups["ts"].Value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp))
        {
            return false;
        }

        kind = match.Groups["kind"].Value;
        message = WhitespacePattern().Replace(MarkupPattern().Replace(match.Groups["msg"].Value, string.Empty), " ").Trim();
        return true;
    }
}
