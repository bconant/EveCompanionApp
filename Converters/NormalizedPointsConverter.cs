using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EveCompanionApp.Converters;

/// <summary>
/// Scales normalized graph points (0..1 on both axes, y = 0 at the top) to a canvas's actual size.
/// Bindings: [0] PointCollection, [1] canvas width, [2] canvas height.
/// ConverterParameter <c>Area</c> closes the shape down to the baseline for a filled polygon.
/// </summary>
public sealed class NormalizedPointsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not PointCollection normalized
            || values[1] is not double width
            || values[2] is not double height
            || normalized.Count == 0
            || width <= 0
            || height <= 0)
        {
            return new PointCollection();
        }

        var closeArea = string.Equals(parameter as string, "Area", StringComparison.OrdinalIgnoreCase);
        var scaled = new PointCollection(normalized.Count + (closeArea ? 2 : 0));
        foreach (var point in normalized)
        {
            scaled.Add(new Point(point.X * width, point.Y * height));
        }

        if (closeArea)
        {
            scaled.Add(new Point(normalized[^1].X * width, height));
            scaled.Add(new Point(normalized[0].X * width, height));
        }

        scaled.Freeze();
        return scaled;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
