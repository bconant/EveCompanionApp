namespace EveCompanionApp.Services;

/// <summary>
/// Bounty totals and a kill count that doesn't double-count fleet kills. Not thread-safe: use
/// from one thread.
/// </summary>
/// <remarks>
/// Every fleet member's log gets its own bounty line for the same kill, with the same amount and
/// (give or take a second) the same timestamp. A bounty line is therefore folded into an existing
/// kill when a <em>different</em> pilot already reported the same amount within
/// <see cref="MatchWindow"/>; otherwise it is a new kill. ISK is summed across all lines, since
/// each line is a payout to that pilot.
/// </remarks>
public sealed class KillLedger
{
    // Generous so a client whose log writes lag the others still gets matched.
    private static readonly TimeSpan RetainFor = TimeSpan.FromMinutes(2);

    private readonly List<KillRecord> _recent = [];
    private readonly Dictionary<string, long> _bountyByPilot = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _newestTimestamp = DateTime.MinValue;

    public KillLedger(TimeSpan? matchWindow = null)
    {
        MatchWindow = matchWindow ?? TimeSpan.FromSeconds(2);
    }

    public TimeSpan MatchWindow { get; }

    public int Kills { get; private set; }

    public long TotalBounty { get; private set; }

    public IReadOnlyDictionary<string, long> BountyByPilot => _bountyByPilot;

    /// <returns>True if the line was counted as a new kill.</returns>
    public bool Record(string pilot, long amount, DateTime timestamp)
    {
        TotalBounty += amount;
        _bountyByPilot[pilot] = _bountyByPilot.GetValueOrDefault(pilot) + amount;

        if (timestamp > _newestTimestamp)
        {
            _newestTimestamp = timestamp;
        }

        _recent.RemoveAll(r => r.Timestamp < _newestTimestamp - RetainFor);

        foreach (var kill in _recent)
        {
            if (kill.Amount == amount
                && (kill.Timestamp - timestamp).Duration() <= MatchWindow
                && !kill.Pilots.Contains(pilot))
            {
                kill.Pilots.Add(pilot);
                return false;
            }
        }

        _recent.Add(new KillRecord(timestamp, amount, pilot));
        Kills++;
        return true;
    }

    public void Reset()
    {
        _recent.Clear();
        _bountyByPilot.Clear();
        _newestTimestamp = DateTime.MinValue;
        Kills = 0;
        TotalBounty = 0;
    }

    private sealed class KillRecord(DateTime timestamp, long amount, string pilot)
    {
        public DateTime Timestamp { get; } = timestamp;

        public long Amount { get; } = amount;

        public HashSet<string> Pilots { get; } = new(StringComparer.OrdinalIgnoreCase) { pilot };
    }
}
