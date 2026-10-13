using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveCompanionApp.Models;
using EveCompanionApp.Services;

namespace EveCompanionApp.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int MaxRecentHits = 100;
    private const int MaxAlerts = 50;
    private static readonly TimeSpan ShortWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    // Enough graph samples to span the 30-second window at the refresh rate, plus slack.
    private static readonly int GraphHistoryCapacity = (int)(LongWindow / RefreshInterval) + 4;
    private static readonly TimeSpan SpawnAlertCooldown = TimeSpan.FromMinutes(5);

    // ISK/hour swings wildly over the first few kills, so it isn't shown before this.
    private static readonly TimeSpan MinIskRatePeriod = TimeSpan.FromMinutes(5);

    private readonly ILogTailerService _tailer;
    private readonly ILogParserService _parser;
    private readonly IFolderPickerService _folderPicker;
    private readonly IAlertNotifier _alertNotifier;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;

    // Touched only on the UI thread.
    private readonly DpsCalculator _outgoing = new();
    private readonly DpsCalculator _incoming = new();
    private readonly Dictionary<string, DateTime> _lastAlertByNpc = new(StringComparer.OrdinalIgnoreCase);
    private readonly KillLedger _killLedger = new();
    private DateTime? _firstBountyUtc;

    // Incremented on the tailer thread.
    private long _linesRead;

    // Filled from the tailer thread (hits are enqueued there); ticked on the UI thread.
    private readonly ConcurrentDictionary<string, CharacterDpsTracker> _trackersByName = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel(
        ILogTailerService tailer,
        ILogParserService parser,
        IFolderPickerService folderPicker,
        IAlertNotifier alertNotifier)
    {
        _tailer = tailer;
        _parser = parser;
        _folderPicker = folderPicker;
        _alertNotifier = alertNotifier;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Keeps rolling DPS decaying toward zero when combat stops and no new lines arrive.
        // Also the graph clock: trackers are ticked only here so samples stay evenly spaced.
        _refreshTimer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, (_, _) => OnRefreshTick(), _dispatcher);
        _refreshTimer.Stop();

        _tailer.LineReceived += OnLineReceived;
        _tailer.ActiveLogsChanged += OnActiveLogsChanged;
        _tailer.Faulted += OnTailerFaulted;
    }

    /// <summary>Newest first, capped at <see cref="MaxRecentHits"/>.</summary>
    public ObservableCollection<CombatEvent> RecentHits { get; } = [];

    public ObservableCollection<SpawnAlert> SpawnAlerts { get; } = [];

    /// <summary>One per character found in a log's Listener header, in order of discovery.</summary>
    public ObservableCollection<CharacterDpsViewModel> CharacterTrackers { get; } = [];

    public IEnumerable<NpcFactionInfo> FilteredFactions =>
        string.IsNullOrWhiteSpace(ReferenceFilter)
            ? NpcFactionInfo.Known
            : NpcFactionInfo.Known.Where(f => MatchesFilter(f, ReferenceFilter.Trim()));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMonitoringCommand))]
    private string _logDirectory = DefaultLogDirectory();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMonitoringCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopMonitoringCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectLogDirectoryCommand))]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _statusMessage = "Stopped";

    /// <summary>File name(s) currently being tailed; several when multiboxing.</summary>
    [ObservableProperty]
    private string? _monitoredFilePath;

    [ObservableProperty]
    private string? _activeCharacters;

    [ObservableProperty]
    private string _currentSystem = "Unknown";

    [ObservableProperty]
    private long _linesProcessed;

    /// <summary>Outgoing DPS over the last 5 seconds.</summary>
    [ObservableProperty]
    private double _currentDps;

    [ObservableProperty]
    private double _dps30Seconds;

    /// <summary>Total outgoing damage divided by time in combat.</summary>
    [ObservableProperty]
    private double _combatDps;

    [ObservableProperty]
    private double _peakDps;

    [ObservableProperty]
    private double _incomingDps;

    [ObservableProperty]
    private long _totalDamageDealt;

    [ObservableProperty]
    private long _totalDamageTaken;

    /// <summary>Largest single outgoing hit since the last clear.</summary>
    [ObservableProperty]
    private int _maxHit;

    /// <summary>Who landed <see cref="MaxHit"/>, on what, and how well.</summary>
    [ObservableProperty]
    private string? _maxHitDetail;

    /// <summary>Kills across all pilots, with fleet duplicates merged.</summary>
    [ObservableProperty]
    private int _kills;

    /// <summary>ISK added to every monitored pilot's next bounty payout.</summary>
    [ObservableProperty]
    private long _totalBounty;

    /// <summary>Bounty rate since the first bounty; null until enough time has passed.</summary>
    [ObservableProperty]
    private double? _iskPerHour;

    /// <summary>Per-pilot bounty totals, one per line, for the tooltip.</summary>
    [ObservableProperty]
    private string? _bountyBreakdown;

    /// <summary>Most recent rare spawn, shown in the banner until dismissed.</summary>
    [ObservableProperty]
    private SpawnAlert? _activeAlert;

    [ObservableProperty]
    private bool _isAlertSoundEnabled = true;

    /// <summary>Keeps the window above the game client.</summary>
    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredFactions))]
    private string _referenceFilter = string.Empty;

    private bool CanStartMonitoring() => !IsMonitoring && !string.IsNullOrWhiteSpace(LogDirectory);

    private bool CanStopMonitoring() => IsMonitoring;

    private bool CanSelectLogDirectory() => !IsMonitoring;

    [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
    private void StartMonitoring()
    {
        try
        {
            _tailer.Start(LogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
            return;
        }

        IsMonitoring = true;
        StatusMessage = "Monitoring";
        MonitoredFilePath = "Waiting for log files…";
        _refreshTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
    private Task StopMonitoringAsync() => StopCoreAsync("Stopped");

    [RelayCommand(CanExecute = nameof(CanSelectLogDirectory))]
    private void SelectLogDirectory()
    {
        if (_folderPicker.PickFolder(LogDirectory) is { } folder)
        {
            LogDirectory = folder;
        }
    }

    [RelayCommand]
    private void ClearStats()
    {
        RecentHits.Clear();
        SpawnAlerts.Clear();
        ActiveAlert = null;
        _lastAlertByNpc.Clear();
        _outgoing.Reset();
        _incoming.Reset();
        _killLedger.Reset();
        _firstBountyUtc = null;
        PeakDps = 0;
        TotalDamageDealt = 0;
        TotalDamageTaken = 0;
        MaxHit = 0;
        MaxHitDetail = null;
        Kills = 0;
        TotalBounty = 0;
        BountyBreakdown = null;
        foreach (var tracker in CharacterTrackers)
        {
            tracker.Reset();
        }

        RefreshMetrics();
    }

    [RelayCommand]
    private void DismissAlert() => ActiveAlert = null;

    public void Dispose()
    {
        _refreshTimer.Stop();
        _tailer.LineReceived -= OnLineReceived;
        _tailer.ActiveLogsChanged -= OnActiveLogsChanged;
        _tailer.Faulted -= OnTailerFaulted;
    }

    private async Task StopCoreAsync(string status)
    {
        _refreshTimer.Stop();
        await _tailer.StopAsync();

        IsMonitoring = false;
        MonitoredFilePath = null;
        ActiveCharacters = null;
        CurrentDps = 0;
        Dps30Seconds = 0;
        IncomingDps = 0;
        foreach (var tracker in CharacterTrackers)
        {
            tracker.ClearHistory();
        }

        StatusMessage = status;
    }

    // Tailer thread: parse here (the parser is stateless) and marshal only the results.
    private void OnLineReceived(object? sender, LogLine line)
    {
        Interlocked.Increment(ref _linesRead);

        // Falls back to the file name so fleet deduplication still tells logs apart.
        var pilot = line.Character ?? Path.GetFileNameWithoutExtension(line.FilePath);

        if (_parser.TryParseCombat(line.Text, out var combat))
        {
            var alert = _parser.TryDetectSpawn(combat.Target, combat.Timestamp, out var spawn)
                ? spawn with { Character = pilot }
                : null;
            var hit = combat with { Character = pilot, IsRareSpawn = alert is not null };
            var receivedUtc = DateTime.UtcNow;
            if (line.Character is not null)
            {
                GetOrAddTracker(line.Character).RecordHit(hit.Damage, hit.IsOutgoing, receivedUtc);
            }

            _ = _dispatcher.InvokeAsync(() => ApplyHit(hit, alert, receivedUtc));
        }
        else if (_parser.TryParseBounty(line.Text, out var bounty))
        {
            var payout = bounty with { Character = pilot };
            _ = _dispatcher.InvokeAsync(() => ApplyBounty(payout));
        }
        else if (_parser.TryParseLocation(line.Text, out var system))
        {
            _ = _dispatcher.InvokeAsync(() => CurrentSystem = system);
        }
    }

    private void OnActiveLogsChanged(object? sender, IReadOnlyList<MonitoredLog> logs)
    {
        var files = logs.Count == 0
            ? "Waiting for log files…"
            : string.Join(", ", logs.Select(l => Path.GetFileName(l.FilePath)));
        var pilots = logs.Select(l => l.Character).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var pilot in pilots)
        {
            GetOrAddTracker(pilot);
        }

        _ = _dispatcher.InvokeAsync(() =>
        {
            MonitoredFilePath = files;
            ActiveCharacters = pilots.Count == 0 ? null : string.Join(", ", pilots);
        });
    }

    // Tailer thread. The tracker exists before its card is added, so no hit is lost meanwhile.
    private CharacterDpsTracker GetOrAddTracker(string character)
    {
        if (_trackersByName.TryGetValue(character, out var existing))
        {
            return existing;
        }

        var tracker = new CharacterDpsTracker(character, GraphHistoryCapacity);
        if (!_trackersByName.TryAdd(character, tracker))
        {
            return _trackersByName[character];
        }

        _ = _dispatcher.InvokeAsync(() => CharacterTrackers.Add(new CharacterDpsViewModel(tracker)));
        return tracker;
    }

    private void OnTailerFaulted(object? sender, Exception ex) =>
        _ = _dispatcher.InvokeAsync(() => StopCoreAsync($"Monitoring failed: {ex.Message}"));

    private void ApplyHit(CombatEvent hit, SpawnAlert? alert, DateTime receivedUtc)
    {
        if (hit.IsOutgoing)
        {
            _outgoing.Add(hit.Damage, hit.Timestamp, receivedUtc);
            TotalDamageDealt += hit.Damage;
            if (hit.Damage > MaxHit)
            {
                MaxHit = hit.Damage;
                MaxHitDetail = $"{hit.Character} → {hit.Target} ({hit.HitQuality})";
            }
        }
        else
        {
            _incoming.Add(hit.Damage, hit.Timestamp, receivedUtc);
            TotalDamageTaken += hit.Damage;
        }

        RecentHits.Insert(0, hit);
        TrimTo(RecentHits, MaxRecentHits);

        if (alert is not null)
        {
            RaiseSpawnAlert(alert);
        }

        RefreshMetrics();
    }

    private void ApplyBounty(BountyEvent bounty)
    {
        _killLedger.Record(bounty.Character, bounty.Amount, bounty.Timestamp);
        _firstBountyUtc ??= DateTime.UtcNow;

        Kills = _killLedger.Kills;
        TotalBounty = _killLedger.TotalBounty;
        BountyBreakdown = string.Join(
            Environment.NewLine,
            _killLedger.BountyByPilot
                .OrderByDescending(p => p.Value)
                .Select(p => $"{p.Key}: {p.Value:N0} ISK"));
        RefreshMetrics();
    }

    private void RaiseSpawnAlert(SpawnAlert alert)
    {
        // One alert per NPC name per cooldown, even when several pilots shoot it.
        var now = DateTime.UtcNow;
        if (_lastAlertByNpc.TryGetValue(alert.NpcName, out var last) && now - last < SpawnAlertCooldown)
        {
            return;
        }

        _lastAlertByNpc[alert.NpcName] = now;
        SpawnAlerts.Insert(0, alert);
        TrimTo(SpawnAlerts, MaxAlerts);

        ActiveAlert = alert;
        _alertNotifier.Notify(alert, IsAlertSoundEnabled);
    }

    private void OnRefreshTick()
    {
        RefreshMetrics();

        var now = DateTime.UtcNow;
        foreach (var tracker in CharacterTrackers)
        {
            tracker.Update(now);
        }
    }

    private void RefreshMetrics()
    {
        var now = DateTime.UtcNow;
        CurrentDps = _outgoing.GetDps(ShortWindow, now);
        Dps30Seconds = _outgoing.GetDps(LongWindow, now);
        CombatDps = _outgoing.CombatDps;
        IncomingDps = _incoming.GetDps(ShortWindow, now);
        PeakDps = Math.Max(PeakDps, CurrentDps);
        LinesProcessed = Interlocked.Read(ref _linesRead);

        var earningFor = now - _firstBountyUtc;
        IskPerHour = earningFor >= MinIskRatePeriod ? TotalBounty / earningFor.Value.TotalHours : null;
    }

    private static void TrimTo<T>(ObservableCollection<T> collection, int max)
    {
        while (collection.Count > max)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static bool MatchesFilter(NpcFactionInfo info, string filter) =>
        info.Faction.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || info.DamageDealt.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || info.WeakTo.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || info.Notes.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string DefaultLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Gamelogs");
}
