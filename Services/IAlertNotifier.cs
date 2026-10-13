using EveCompanionApp.Models;

namespace EveCompanionApp.Services;

/// <summary>Draws the player's attention to a rare spawn without stealing focus from the game.</summary>
public interface IAlertNotifier
{
    /// <summary>Flashes the taskbar button and, if <paramref name="playSound"/>, sounds an alarm. Call on the UI thread.</summary>
    void Notify(SpawnAlert alert, bool playSound);
}
