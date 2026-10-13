namespace EveCompanionApp.Services;

/// <summary>
/// Rolling and whole-combat DPS for one damage stream. Not thread-safe: use from one thread.
/// </summary>
/// <remarks>
/// Rolling windows use local receipt time so they decay smoothly while idle and are immune to
/// clock drift against EVE time. Combat DPS uses the log's own timestamps: idle gaps longer than
/// <see cref="CombatGap"/> (warping between sites, docking) don't count as time in combat.
/// </remarks>
public sealed class DpsCalculator
{
    /// <summary>Longest rolling window <see cref="GetDps"/> can answer.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromSeconds(60);

    // Log timestamps have one-second resolution, so each engagement counts at least one second.
    private static readonly TimeSpan TimestampResolution = TimeSpan.FromSeconds(1);

    private readonly Queue<(DateTime ReceivedUtc, int Damage)> _recent = new();
    private DateTime? _lastHitTimestamp;

    public DpsCalculator(TimeSpan? combatGap = null)
    {
        CombatGap = combatGap ?? TimeSpan.FromSeconds(15);
    }

    public TimeSpan CombatGap { get; }

    public long TotalDamage { get; private set; }

    public TimeSpan CombatTime { get; private set; }

    /// <summary>Total damage divided by time actually spent in combat.</summary>
    public double CombatDps => TotalDamage == 0 ? 0 : TotalDamage / Math.Max(CombatTime.TotalSeconds, 1);

    public void Add(int damage, DateTime logTimestamp, DateTime receivedUtc)
    {
        TotalDamage += damage;
        _recent.Enqueue((receivedUtc, damage));

        if (_lastHitTimestamp is not { } last || logTimestamp - last > CombatGap)
        {
            CombatTime += TimestampResolution;
        }
        else if (logTimestamp > last)
        {
            CombatTime += logTimestamp - last;
        }

        // Lines from several pilots' logs can arrive slightly out of order.
        if (_lastHitTimestamp is null || logTimestamp > _lastHitTimestamp)
        {
            _lastHitTimestamp = logTimestamp;
        }
    }

    /// <summary>Damage received in the last <paramref name="window"/>, per second.</summary>
    public double GetDps(TimeSpan window, DateTime nowUtc)
    {
        if (window <= TimeSpan.Zero || window > MaxWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, $"Window must be between 0 and {MaxWindow}.");
        }

        while (_recent.TryPeek(out var oldest) && oldest.ReceivedUtc < nowUtc - MaxWindow)
        {
            _recent.Dequeue();
        }

        var cutoff = nowUtc - window;
        long sum = 0;
        foreach (var sample in _recent)
        {
            if (sample.ReceivedUtc > cutoff)
            {
                sum += sample.Damage;
            }
        }

        return sum / window.TotalSeconds;
    }

    public void Reset()
    {
        _recent.Clear();
        _lastHitTimestamp = null;
        TotalDamage = 0;
        CombatTime = TimeSpan.Zero;
    }
}
