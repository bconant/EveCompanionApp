using System.Collections.Concurrent;
using EveCompanionApp.Helpers;

namespace EveCompanionApp.Services;

/// <summary>One graph sample: 5-second DPS in each direction at <see cref="Time"/>.</summary>
public readonly record struct DpsSample(DateTime Time, double Outgoing, double Incoming);

/// <summary>
/// Rolling outgoing and incoming DPS for one character over 5- and 30-second windows, plus a
/// short sample history for graphs.
/// </summary>
/// <remarks>
/// <see cref="RecordHit(int, bool)"/> is safe from any thread (the log tailer's).
/// <see cref="Tick"/>, <see cref="ClearHistory"/> and <see cref="Reset"/> must stay on one
/// thread (the UI's): Tick is the queues' only consumer, so its peek-then-dequeue purge can't race.
/// Hits are stamped on receipt, so the queues are normally in time order.
/// </remarks>
public sealed class CharacterDpsTracker
{
    public static readonly TimeSpan ShortWindow = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LongWindow = TimeSpan.FromSeconds(30);

    private readonly RingBuffer<DpsSample> _history;

    /// <param name="historyCapacity">Samples kept; size it to cover <see cref="LongWindow"/> at the tick rate.</param>
    public CharacterDpsTracker(string characterName, int historyCapacity = 64)
    {
        CharacterName = characterName;
        _history = new RingBuffer<DpsSample>(historyCapacity);
    }

    public string CharacterName { get; }

    public ConcurrentQueue<(DateTime Time, int Damage)> OutgoingHits { get; } = new();

    public ConcurrentQueue<(DateTime Time, int Damage)> IncomingHits { get; } = new();

    public double OutgoingDps5s { get; private set; }

    public double OutgoingDps30s { get; private set; }

    public double IncomingDps5s { get; private set; }

    public double IncomingDps30s { get; private set; }

    /// <summary>Best 5-second outgoing DPS since the last reset.</summary>
    public double PeakOutgoingDps { get; private set; }

    /// <summary>Worst 5-second incoming DPS since the last reset.</summary>
    public double PeakIncomingDps { get; private set; }

    /// <summary>One sample per <see cref="Tick"/>, oldest first.</summary>
    public IReadOnlyList<DpsSample> History => _history;

    public void RecordHit(int damage, bool isOutgoing) => RecordHit(damage, isOutgoing, DateTime.UtcNow);

    public void RecordHit(int damage, bool isOutgoing, DateTime receivedUtc) =>
        (isOutgoing ? OutgoingHits : IncomingHits).Enqueue((receivedUtc, damage));

    /// <summary>Drops hits older than <see cref="LongWindow"/>, recomputes the rates and records a sample.</summary>
    public void Tick(DateTime nowUtc)
    {
        (OutgoingDps5s, OutgoingDps30s) = Measure(OutgoingHits, nowUtc);
        (IncomingDps5s, IncomingDps30s) = Measure(IncomingHits, nowUtc);
        PeakOutgoingDps = Math.Max(PeakOutgoingDps, OutgoingDps5s);
        PeakIncomingDps = Math.Max(PeakIncomingDps, IncomingDps5s);
        _history.Add(new DpsSample(nowUtc, OutgoingDps5s, IncomingDps5s));
    }

    /// <summary>Forgets pending hits and the graph history; keeps the peaks.</summary>
    public void ClearHistory()
    {
        OutgoingHits.Clear();
        IncomingHits.Clear();
        _history.Clear();
        OutgoingDps5s = OutgoingDps30s = IncomingDps5s = IncomingDps30s = 0;
    }

    public void Reset()
    {
        ClearHistory();
        PeakOutgoingDps = PeakIncomingDps = 0;
    }

    private static (double Dps5s, double Dps30s) Measure(ConcurrentQueue<(DateTime Time, int Damage)> hits, DateTime nowUtc)
    {
        var longCutoff = nowUtc - LongWindow;
        while (hits.TryPeek(out var oldest) && oldest.Time < longCutoff)
        {
            hits.TryDequeue(out _);
        }

        var shortCutoff = nowUtc - ShortWindow;
        long shortSum = 0;
        long longSum = 0;

        // Enumerating a ConcurrentQueue is a moment-in-time snapshot, safe alongside enqueues.
        // Times are re-checked because a clock adjustment can leave an old hit behind newer ones.
        foreach (var (time, damage) in hits)
        {
            if (time < longCutoff)
            {
                continue;
            }

            longSum += damage;
            if (time >= shortCutoff)
            {
                shortSum += damage;
            }
        }

        return (shortSum / ShortWindow.TotalSeconds, longSum / LongWindow.TotalSeconds);
    }
}
