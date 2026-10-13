using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EveCompanionApp.Services;

namespace EveCompanionApp.ViewModels;

/// <summary>
/// One character's DPS telemetry card. Graph points are normalized (x: 0 = 30 s ago, 1 = now;
/// y: 0 = <see cref="GraphMaxDps"/>, 1 = zero) and scaled to the canvas in the view. UI thread only.
/// </summary>
public sealed partial class CharacterDpsViewModel : ViewModelBase
{
    private const double MinGraphMax = 10;
    private static readonly PointCollection NoPoints = CreateEmpty();

    private readonly CharacterDpsTracker _tracker;

    public CharacterDpsViewModel(CharacterDpsTracker tracker)
    {
        _tracker = tracker;
    }

    public string CharacterName => _tracker.CharacterName;

    [ObservableProperty]
    private double _outgoingDps5s;

    [ObservableProperty]
    private double _outgoingDps30s;

    [ObservableProperty]
    private double _peakOutgoingDps;

    [ObservableProperty]
    private double _incomingDps5s;

    [ObservableProperty]
    private double _incomingDps30s;

    [ObservableProperty]
    private double _peakIncomingDps;

    /// <summary>DPS at the top of the graph; shared by both lines so they compare directly.</summary>
    [ObservableProperty]
    private double _graphMaxDps = MinGraphMax;

    [ObservableProperty]
    private PointCollection _outgoingGraphPoints = NoPoints;

    [ObservableProperty]
    private PointCollection _incomingGraphPoints = NoPoints;

    public void Update(DateTime nowUtc)
    {
        _tracker.Tick(nowUtc);
        Sync(nowUtc);
    }

    /// <summary>Flattens the graph and rates (e.g. when monitoring stops); keeps peaks.</summary>
    public void ClearHistory()
    {
        _tracker.ClearHistory();
        Sync(DateTime.UtcNow);
    }

    public void Reset()
    {
        _tracker.Reset();
        Sync(DateTime.UtcNow);
    }

    private void Sync(DateTime nowUtc)
    {
        OutgoingDps5s = _tracker.OutgoingDps5s;
        OutgoingDps30s = _tracker.OutgoingDps30s;
        PeakOutgoingDps = _tracker.PeakOutgoingDps;
        IncomingDps5s = _tracker.IncomingDps5s;
        IncomingDps30s = _tracker.IncomingDps30s;
        PeakIncomingDps = _tracker.PeakIncomingDps;

        var visibleFrom = nowUtc - CharacterDpsTracker.LongWindow;
        var max = 0.0;
        foreach (var sample in _tracker.History)
        {
            if (sample.Time >= visibleFrom)
            {
                max = Math.Max(max, Math.Max(sample.Outgoing, sample.Incoming));
            }
        }

        GraphMaxDps = NiceCeiling(max);
        OutgoingGraphPoints = Normalize(nowUtc, GraphMaxDps, static s => s.Outgoing);
        IncomingGraphPoints = Normalize(nowUtc, GraphMaxDps, static s => s.Incoming);
    }

    private PointCollection Normalize(DateTime nowUtc, double scale, Func<DpsSample, double> select)
    {
        var window = CharacterDpsTracker.LongWindow.TotalSeconds;
        var points = new PointCollection(_tracker.History.Count);
        foreach (var sample in _tracker.History)
        {
            var age = (nowUtc - sample.Time).TotalSeconds;
            if (age > window)
            {
                continue;
            }

            var x = 1 - Math.Max(age, 0) / window;
            var y = 1 - Math.Clamp(select(sample) / scale, 0, 1);
            points.Add(new Point(x, y));
        }

        if (points.Count < 2)
        {
            return NoPoints;
        }

        points.Freeze();
        return points;
    }

    /// <summary>Headroom above <paramref name="value"/>, rounded up to 1, 2 or 5 × 10ⁿ.</summary>
    private static double NiceCeiling(double value)
    {
        var target = Math.Max(value * 1.1, MinGraphMax);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        foreach (var step in (ReadOnlySpan<double>)[1, 2, 5, 10])
        {
            if (step * magnitude >= target)
            {
                return step * magnitude;
            }
        }

        return 10 * magnitude;
    }

    private static PointCollection CreateEmpty()
    {
        var empty = new PointCollection();
        empty.Freeze();
        return empty;
    }
}
