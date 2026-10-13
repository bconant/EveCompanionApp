using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

/// <summary>
/// Follows every active EVE game log in a folder and reports appended lines.
/// </summary>
/// <remarks>
/// All events are raised on a background thread; subscribers must marshal to the UI thread.
/// Files already present when monitoring starts are read from their current end (history is
/// skipped); session files created afterwards are read from the start. Only the newest session
/// per pilot is followed, so relogging replaces that pilot's old file.
/// </remarks>
public interface ILogTailerService : IDisposable
{
    /// <summary>A complete line was appended to a monitored log.</summary>
    event EventHandler<LogLine>? LineReceived;

    /// <summary>The set of monitored logs (or their known pilots) changed.</summary>
    event EventHandler<IReadOnlyList<MonitoredLog>>? ActiveLogsChanged;

    /// <summary>The tailing loop stopped because of an unexpected error.</summary>
    event EventHandler<Exception>? Faulted;

    void Start(string directory);

    Task StopAsync();
}
