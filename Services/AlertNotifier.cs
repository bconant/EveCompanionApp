using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

/// <summary>
/// Plays a built-in two-tone alarm (independent of the Windows sound scheme, which players often
/// mute) and flashes the main window's taskbar button until it is brought to the foreground.
/// </summary>
public sealed class AlertNotifier : IAlertNotifier, IDisposable
{
    // A pack of faction rats lands several alerts within a second; one alarm is enough.
    private static readonly TimeSpan SoundCooldown = TimeSpan.FromSeconds(8);

    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    private readonly Lazy<SoundPlayer> _player = new(CreatePlayer);
    private DateTime _lastSoundUtc = DateTime.MinValue;

    public void Notify(SpawnAlert alert, bool playSound)
    {
        if (playSound && DateTime.UtcNow - _lastSoundUtc >= SoundCooldown)
        {
            _lastSoundUtc = DateTime.UtcNow;
            _player.Value.Play();
        }

        FlashMainWindow();
    }

    public void Dispose()
    {
        if (_player.IsValueCreated)
        {
            _player.Value.Stream?.Dispose();
            _player.Value.Dispose();
        }
    }

    private static void FlashMainWindow()
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            return;
        }

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = new WindowInteropHelper(window).Handle,
            Flags = FlashAll | FlashUntilForeground,
        };
        _ = FlashWindowEx(ref info);
    }

    private static SoundPlayer CreatePlayer()
    {
        var player = new SoundPlayer(CreateAlarmWave());
        player.Load();
        return player;
    }

    /// <summary>Three rising 880/1320 Hz chirps, 16-bit mono PCM, with short fades to avoid clicks.</summary>
    private static MemoryStream CreateAlarmWave()
    {
        const int sampleRate = 22050;
        var samples = new List<short>();

        void Tone(double frequency, int milliseconds)
        {
            var count = sampleRate * milliseconds / 1000;
            var fade = sampleRate * 5 / 1000;
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fade);
                samples.Add((short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * envelope * 0.45 * short.MaxValue));
            }
        }

        void Silence(int milliseconds) => samples.AddRange(Enumerable.Repeat((short)0, sampleRate * milliseconds / 1000));

        for (var chirp = 0; chirp < 3; chirp++)
        {
            Tone(880, 110);
            Silence(30);
            Tone(1320, 110);
            Silence(90);
        }

        var dataBytes = samples.Count * sizeof(short);
        var stream = new MemoryStream(44 + dataBytes);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);                    // fmt chunk size
            writer.Write((short)1);              // PCM
            writer.Write((short)1);              // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));  // block align
            writer.Write((short)16);             // bits per sample
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint TimeoutMilliseconds;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
