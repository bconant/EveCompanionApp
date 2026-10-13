namespace EveCompanionApp.Models;

/// <summary>
/// One complete line read from a tailed log file.
/// </summary>
/// <param name="FilePath">Full path of the file the line came from.</param>
/// <param name="Character">Pilot from the log's "Listener:" header, or null if not yet known.</param>
/// <param name="Text">The raw line, without its line terminator.</param>
public sealed record LogLine(string FilePath, string? Character, string Text);
