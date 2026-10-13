using System.Globalization;

namespace EveCompanionApp.Helpers;

/// <summary>Compact ISK amounts for tight layouts, e.g. "12.50M ISK".</summary>
public static class IskFormatter
{
    /// <summary>
    /// 1.23B ISK, 12.50M ISK, 450.0K ISK, or the plain amount below one thousand. A value that
    /// would round up to 1000 of a unit is promoted (999,999,999 shows as 1.00B, not 1000.00M).
    /// </summary>
    public static string Format(long amount)
    {
        var value = (decimal)amount;
        var magnitude = Math.Abs(value);

        if (magnitude >= 1_000_000_000m || Round(magnitude / 1_000_000m, 2) >= 1_000m)
        {
            return Scaled(value / 1_000_000_000m, 2, "B");
        }

        if (magnitude >= 1_000_000m || Round(magnitude / 1_000m, 1) >= 1_000m)
        {
            return Scaled(value / 1_000_000m, 2, "M");
        }

        if (magnitude >= 1_000m)
        {
            return Scaled(value / 1_000m, 1, "K");
        }

        return FormatExact(amount);
    }

    /// <summary>Full comma-separated amount, e.g. "12,504,375 ISK".</summary>
    public static string FormatExact(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " ISK";

    private static decimal Round(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static string Scaled(decimal value, int decimals, string suffix) =>
        Round(value, decimals).ToString(decimals == 2 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + suffix + " ISK";
}
