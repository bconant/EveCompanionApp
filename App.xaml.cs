using System.Windows;
using EveCompanionApp.Services;
using EveCompanionApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EveCompanionApp;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        MainWindow = _services.GetRequiredService<Views.MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Disposes singletons, which cancels any active tailing session.
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILogTailerService, LogTailerService>();
        services.AddSingleton<ILogParserService, LogParserService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IAlertNotifier, AlertNotifier>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Views.MainWindow>();

        return services;
    }
}
