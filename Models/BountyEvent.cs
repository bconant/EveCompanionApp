namespace EveCompanionApp.Models;

/// <summary>
/// A <c>(bounty)</c> line: ISK credited to one pilot for one kill.
/// </summary>
/// <param name="Timestamp">EVE time (UTC) as written in the log.</param>
/// <param name="Amount">ISK added to the pilot's next bounty payout.</param>
/// <param name="Character">Pilot whose log the line came from.</param>
public sealed record BountyEvent(DateTime Timestamp, long Amount, string Character = "");
