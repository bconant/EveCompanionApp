using System.Globalization;
using System.Windows.Data;
using EveCompanionApp.Helpers;

namespace EveCompanionApp.Converters;

/// <summary>
/// Formats an ISK amount with <see cref="IskFormatter"/>. ConverterParameter: none for compact
/// ("12.50M ISK"), <c>Exact</c> for the full amount, <c>PerHour</c> for a compact rate.
/// </summary>
public sealed class IskConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long? amount = value switch
        {
            long l => l,
            int i => i,
            double d when double.IsFinite(d) => (long)Math.Round(d),
            decimal m => (long)Math.Round(m),
            _ => null,
        };

        if (amount is not { } isk)
        {
            return (parameter as string) == "PerHour" ? "— ISK/h" : "—";
        }

        return (parameter as string) switch
        {
            "Exact" => IskFormatter.FormatExact(isk),
            "PerHour" => IskFormatter.Format(isk) + "/h",
            _ => IskFormatter.Format(isk),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
