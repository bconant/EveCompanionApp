namespace EveCompanionApp.Models;

/// <summary>
/// Quick-reference damage profile for an NPC faction.
/// </summary>
public sealed record NpcFactionInfo(string Faction, string DamageDealt, string WeakTo, string Notes)
{
    public static IReadOnlyList<NpcFactionInfo> Known { get; } =
    [
        new("Angel Cartel", "Explosive / Kinetic", "Explosive / Kinetic", "Target painting, stasis webs"),
        new("Blood Raiders", "EM / Thermal", "EM / Thermal", "Energy neutralizers"),
        new("Guristas", "Kinetic / Thermal", "Kinetic / Thermal", "ECM jamming"),
        new("Sansha's Nation", "EM / Thermal", "EM / Thermal", "Tracking disruption, warp scramblers"),
        new("Serpentis", "Kinetic / Thermal", "Kinetic / Thermal", "Sensor dampeners"),
        new("Mordu's Legion", "Kinetic / Thermal", "Kinetic / Thermal", "Long-range warp disruption"),
        new("Rogue Drones", "Explosive / Kinetic", "EM / Thermal", "Swarms; minimal EWAR"),
        new("Sleepers", "Omni (all four)", "Omni (slightly EM / Thermal)", "Neuts, webs, scrams, remote reps"),
        new("Triglavian", "Thermal / Explosive", "Thermal / Explosive", "Spooling disintegrators, neuts, remote reps"),
    ];
}
