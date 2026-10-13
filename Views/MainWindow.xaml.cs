using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EveCompanionApp.ViewModels;

namespace EveCompanionApp.Views;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private readonly LayoutState _layout;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _layout = (LayoutState)Resources["Layout"];
        SizeChanged += (_, e) => _layout.UpdateWindowWidth(e.NewSize.Width);
        MetricsPanel.SizeChanged += (_, e) => _layout.UpdateMetricsWidth(e.NewSize.Width);
        FeedCard.SizeChanged += (_, e) => _layout.UpdateFeedWidth(e.NewSize.Width);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar on Windows 10 20H1+ / 11; silently ignored elsewhere.
        var hwnd = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
