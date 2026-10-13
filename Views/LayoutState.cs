using CommunityToolkit.Mvvm.ComponentModel;

namespace EveCompanionApp.Views;

/// <summary>
/// Width-driven layout switches for the main window (view-only state, not part of the view
/// model). Declared as a resource so bindings can reach it from data templates and column
/// definitions, which aren't in the visual tree.
/// </summary>
public sealed partial class LayoutState : ObservableObject
{
    /// <summary>Below this window width panels stack vertically (portrait monitors, docked windows).</summary>
    public const double CompactBreakpoint = 1100;

    private const double MinMetricCardWidth = 160;

    // Below this the combat feed drops its weapon column so targets stay readable.
    private const double MinFeedWidthForWeapon = 720;

    // Column counts that split the 8 metric cards into full rows.
    private static readonly int[] MetricColumnOptions = [8, 4, 2, 1];

    [ObservableProperty]
    private bool _isCompact;

    /// <summary>WrapPanel slot width so the metric cards fill each row evenly.</summary>
    [ObservableProperty]
    private double _metricItemWidth = MinMetricCardWidth;

    [ObservableProperty]
    private bool _showWeaponColumn = true;

    public void UpdateWindowWidth(double width) => IsCompact = width < CompactBreakpoint;

    public void UpdateFeedWidth(double width) => ShowWeaponColumn = width >= MinFeedWidthForWeapon;

    public void UpdateMetricsWidth(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var columns = MetricColumnOptions.FirstOrDefault(c => width / c >= MinMetricCardWidth, 1);

        // Floor so rounding never pushes the last card onto a new row.
        MetricItemWidth = Math.Floor(width / columns);
    }
}
