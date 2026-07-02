using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScanApp.App.Scanning;
using ScanApp.App.ViewModels;
using ScanApp.Core.Settings;
using ScanApp.Services.Editing;
using ScanApp.Services.Export;
using ScanApp.Services.Presets;
using ScanApp.Services.Projects;
using ScanApp.Services.Settings;
using Serilog;

namespace ScanApp.App;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>The DI container, for the rare spot that can't be constructor-injected.</summary>
    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Settings (also decides the data directory used by logging and presets).
                var store = new SettingsStore(AppContext.BaseDirectory);
                services.AddSingleton(store);
                services.AddSingleton<SettingsService>();

                // Structured file logging: %DATA%/logs/scanapp-YYYYMMDD.log, 7-day retention.
                var logPath = Path.Combine(store.DataDirectory, "logs", "scanapp-.log");
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .CreateLogger();
                services.AddLogging(b => b.ClearProviders().AddSerilog(Log.Logger, dispose: true));

                // Domain services.
                services.AddSingleton<UndoService>();
                services.AddSingleton<ExportService>();
                services.AddSingleton(sp => new ProjectService(
                    store.DataDirectory, sp.GetRequiredService<ILogger<ProjectService>>()));
                services.AddSingleton(sp => new PresetService(
                    store.DataDirectory, sp.GetRequiredService<ILogger<PresetService>>()));

                // Scanner backends behind the hub.
                services.AddSingleton<ScannerHub>();

                // View-models + shell.
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Log.Information("Actually Good Scanning App starting (portable={Portable})",
            _host.Services.GetRequiredService<SettingsService>().IsPortable);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Exiting");
        Log.CloseAndFlush();
        _host?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
