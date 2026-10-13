using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

/// <summary>
/// A FileSystemWatcher reports new session files; a background loop polls each open file for
/// appended bytes. Files are opened with <see cref="FileShare.ReadWrite"/> so the game client
/// keeps writing while we read.
/// </summary>
/// <remarks>
/// Appends are polled rather than watched: NTFS defers Changed notifications for files another
/// process holds open, so they arrive late or not at all. A periodic rescan backs up the watcher,
/// which can drop events (e.g. on OneDrive-redirected Documents folders).
/// </remarks>
public sealed partial class LogTailerService : ILogTailerService
{
    private const string LogFilePattern = "*.txt";
    private const int MaxSessionFiles = 16;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _createdFiles = new();
    private CancellationTokenSource? _cts;
    private FileSystemWatcher? _watcher;
    private Task? _loop;

    public event EventHandler<LogLine>? LineReceived;

    public event EventHandler<IReadOnlyList<MonitoredLog>>? ActiveLogsChanged;

    public event EventHandler<Exception>? Faulted;

    [GeneratedRegex(@"^(?<stamp>\d{8}_\d{6})_")]
    private static partial Regex SessionFileNamePattern();

    [GeneratedRegex(@"^\s*Listener:\s*(?<name>.+?)\s*$")]
    private static partial Regex ListenerPattern();

    public void Start(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Log directory not found: {directory}");
        }

        lock (_gate)
        {
            if (_loop is not null)
            {
                throw new InvalidOperationException("Log tailing is already running.");
            }

            _cts = new CancellationTokenSource();
            _watcher = new FileSystemWatcher(directory, LogFilePattern) { NotifyFilter = NotifyFilters.FileName };
            _watcher.Created += OnFileCreated;
            _watcher.EnableRaisingEvents = true;

            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(directory, token), token);
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            (loop, cts, watcher) = (_loop, _cts, _watcher);
            (_loop, _cts, _watcher) = (null, null, null);
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileCreated;
            watcher.Dispose();
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (loop is not null)
            {
                await loop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
            _createdFiles.Clear();
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private void OnFileCreated(object sender, FileSystemEventArgs e) => _createdFiles.Enqueue(Path.GetFullPath(e.FullPath));

    private async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        // Owned exclusively by this loop, so no locking is needed.
        var tails = new Dictionary<string, FileTail>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var path in FindSessionFiles(directory))
            {
                TryAttach(tails, seen, path, startAtEnd: true);
            }

            DropSupersededSessions(tails);
            PublishActiveLogs(tails);

            var nextRescan = DateTime.UtcNow + RescanInterval;
            using var timer = new PeriodicTimer(PollInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var changed = false;

                while (_createdFiles.TryDequeue(out var created))
                {
                    changed |= TryAttach(tails, seen, created, startAtEnd: false);
                }

                if (DateTime.UtcNow >= nextRescan)
                {
                    nextRescan = DateTime.UtcNow + RescanInterval;
                    foreach (var path in FindSessionFiles(directory))
                    {
                        changed |= TryAttach(tails, seen, path, startAtEnd: false);
                    }
                }

                foreach (var tail in tails.Values.ToList())
                {
                    var hadCharacter = tail.Character is not null;
                    IReadOnlyList<string> lines;
                    try
                    {
                        lines = tail.ReadNewLines();
                    }
                    catch (IOException)
                    {
                        tails.Remove(tail.Path);
                        tail.Dispose();
                        changed = true;
                        continue;
                    }

                    foreach (var line in lines)
                    {
                        LineReceived?.Invoke(this, new LogLine(tail.Path, tail.Character, line));
                    }

                    changed |= !hadCharacter && tail.Character is not null;
                }

                if (changed)
                {
                    DropSupersededSessions(tails);
                    PublishActiveLogs(tails);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, ex);
        }
        finally
        {
            foreach (var tail in tails.Values)
            {
                tail.Dispose();
            }
        }
    }

    private static bool TryAttach(Dictionary<string, FileTail> tails, HashSet<string> seen, string path, bool startAtEnd)
    {
        if (seen.Contains(path))
        {
            return false;
        }

        var tail = FileTail.TryOpen(path, GetSessionStart(new FileInfo(path)), startAtEnd);
        if (tail is null)
        {
            // Not marked as seen, so the next rescan retries it.
            return false;
        }

        seen.Add(path);
        tails[path] = tail;
        return true;
    }

    /// <summary>Keeps only the newest session per pilot (a relog starts a new file).</summary>
    private static void DropSupersededSessions(Dictionary<string, FileTail> tails)
    {
        var superseded = tails.Values
            .Where(t => t.Character is not null)
            .GroupBy(t => t.Character!, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.OrderByDescending(t => t.SessionStartUtc).Skip(1))
            .ToList();

        foreach (var tail in superseded)
        {
            tails.Remove(tail.Path);
            tail.Dispose();
        }
    }

    private void PublishActiveLogs(Dictionary<string, FileTail> tails)
    {
        var snapshot = tails.Values
            .Select(t => new MonitoredLog(t.Path, t.Character, t.SessionStartUtc))
            .OrderBy(l => l.Character ?? l.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ActiveLogsChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Session logs started within the last day (one EVE downtime cycle), newest first.
    /// Falls back to the most recently written file for folders without EVE-style names.
    /// </summary>
    private static List<string> FindSessionFiles(string directory)
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(directory).GetFiles(LogFilePattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var cutoff = DateTime.UtcNow - MaxSessionAge;
        var recent = files
            .Select(f => (f.FullName, Start: GetSessionStart(f)))
            .Where(f => f.Start >= cutoff)
            .OrderByDescending(f => f.Start)
            .Take(MaxSessionFiles)
            .Select(f => f.FullName)
            .ToList();

        if (recent.Count == 0 && files.Length > 0)
        {
            recent.Add(files.MaxBy(f => f.LastWriteTimeUtc)!.FullName);
        }

        return recent;
    }

    /// <summary>EVE names game logs <c>yyyyMMdd_HHmmss_characterId.txt</c> using EVE time (UTC).</summary>
    private static DateTime GetSessionStart(FileInfo file)
    {
        var match = SessionFileNamePattern().Match(file.Name);
        if (match.Success
            && DateTime.TryParseExact(
                match.Groups["stamp"].Value,
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var start))
        {
            return start;
        }

        return file.Exists ? file.CreationTimeUtc : DateTime.UtcNow;
    }

    /// <summary>
    /// Incremental reader for one growing file. Buffers partial lines until their newline
    /// arrives, picks up the pilot from the "Listener:" header, and restarts if truncated.
    /// </summary>
    private sealed class FileTail : IDisposable
    {
        private const int HeaderProbeBytes = 2048;

        private readonly FileStream _stream;
        private readonly Decoder _decoder;
        private readonly int _preambleLength;
        private readonly byte[] _byteBuffer = new byte[16 * 1024];
        private readonly char[] _charBuffer;
        private readonly StringBuilder _pending = new();

        private FileTail(string path, DateTime sessionStartUtc, FileStream stream, Encoding encoding, int preambleLength)
        {
            Path = path;
            SessionStartUtc = sessionStartUtc;
            _stream = stream;
            _decoder = encoding.GetDecoder();
            _preambleLength = preambleLength;
            _charBuffer = new char[encoding.GetMaxCharCount(_byteBuffer.Length)];
        }

        public string Path { get; }

        public DateTime SessionStartUtc { get; }

        public string? Character { get; private set; }

        public static FileTail? TryOpen(string path, DateTime sessionStartUtc, bool startAtEnd)
        {
            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);

                var (encoding, preambleLength) = DetectEncoding(stream);
                var tail = new FileTail(path, sessionStartUtc, stream, encoding, preambleLength);

                if (startAtEnd)
                {
                    // Skipping history also skips the header, so read the pilot name up front.
                    tail.Character = ReadListener(stream, encoding, preambleLength);
                    stream.Position = Math.Max(stream.Length, preambleLength);
                }
                else
                {
                    stream.Position = Math.Min(preambleLength, stream.Length);
                }

                return tail;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stream?.Dispose();
                return null;
            }
        }

        public IReadOnlyList<string> ReadNewLines()
        {
            if (_stream.Length < _stream.Position)
            {
                // Truncated or replaced in place: start over.
                _stream.Position = Math.Min(_preambleLength, _stream.Length);
                _decoder.Reset();
                _pending.Clear();
            }

            var gotData = false;
            int read;
            while ((read = _stream.Read(_byteBuffer, 0, _byteBuffer.Length)) > 0)
            {
                var charCount = _decoder.GetChars(_byteBuffer, 0, read, _charBuffer, 0);
                _pending.Append(_charBuffer, 0, charCount);
                gotData = true;
            }

            return gotData ? ExtractCompleteLines() : Array.Empty<string>();
        }

        public void Dispose() => _stream.Dispose();

        private List<string> ExtractCompleteLines()
        {
            var lines = new List<string>();
            var text = _pending.ToString();
            var start = 0;
            int newline;

            while ((newline = text.IndexOf('\n', start)) >= 0)
            {
                var line = text.AsSpan(start, newline - start).TrimEnd('\r').TrimStart('﻿');
                if (!line.IsWhiteSpace())
                {
                    var value = line.ToString();
                    if (Character is null && ListenerPattern().Match(value) is { Success: true } listener)
                    {
                        Character = listener.Groups["name"].Value;
                    }

                    lines.Add(value);
                }

                start = newline + 1;
            }

            _pending.Remove(0, start);
            return lines;
        }

        private static string? ReadListener(FileStream stream, Encoding encoding, int preambleLength)
        {
            var buffer = new byte[HeaderProbeBytes];
            stream.Position = Math.Min(preambleLength, stream.Length);
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            foreach (var line in encoding.GetString(buffer, 0, count).Split('\n'))
            {
                if (ListenerPattern().Match(line.TrimEnd('\r')) is { Success: true } match)
                {
                    return match.Groups["name"].Value;
                }
            }

            return null;
        }

        private static (Encoding Encoding, int PreambleLength) DetectEncoding(FileStream stream)
        {
            Span<byte> bom = stackalloc byte[3];
            stream.Position = 0;
            var count = stream.ReadAtLeast(bom, bom.Length, throwOnEndOfStream: false);

            if (count >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            {
                return (Encoding.Unicode, 2);
            }

            if (count >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            {
                return (Encoding.BigEndianUnicode, 2);
            }

            if (count >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            {
                return (Encoding.UTF8, 3);
            }

            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 0);
        }
    }
}
