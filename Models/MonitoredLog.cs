namespace EveCompanionApp.Models;

/// <summary>
/// A log file the tailer currently has open.
/// </summary>
/// <param name="FilePath">Full path of the file.</param>
/// <param name="Character">Pilot from the log's "Listener:" header, or null if not yet known.</param>
/// <param name="SessionStartUtc">Session start, from the EVE file name (yyyyMMdd_HHmmss_id.txt) or file creation time.</param>
public sealed record MonitoredLog(string FilePath, string? Character, DateTime SessionStartUtc);
