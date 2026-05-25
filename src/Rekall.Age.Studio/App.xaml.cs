using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;

namespace Rekall.Age.Studio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string StudioLogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rekall AGE",
        "Studio",
        "Logs");

    public static string StudioLogFilePattern => Path.Combine(StudioLogDirectory, "studio-.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Log.Information("Rekall Studio starting. LogDirectory={LogDirectory}", StudioLogDirectory);
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Rekall Studio failed during startup.");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("Rekall Studio exiting. ExitCode={ExitCode}", e.ApplicationExitCode);
        }
        finally
        {
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        Directory.CreateDirectory(StudioLogDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File(
                StudioLogFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:O} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception.");
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Unhandled application domain exception. IsTerminating={IsTerminating}", e.IsTerminating);
            return;
        }

        Log.Fatal(
            "Unhandled application domain exception object. IsTerminating={IsTerminating} ExceptionObject={ExceptionObject}",
            e.IsTerminating,
            e.ExceptionObject);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
