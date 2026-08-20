using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;
using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Studio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly RekallAgeDesktopFailureReporter FailureReporter = new();

    public static string StudioLogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rekall AGE",
        "Studio",
        "Logs");

    public static string StudioLogFilePattern => Path.Combine(StudioLogDirectory, "studio-.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            ConfigureLogging();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Log.Information("Rekall Studio starting. LogDirectory={LogDirectory}", StudioLogDirectory);
            base.OnStartup(e);
            if (e.Args.Contains(RekallAgeStudioAutomation.AutomationSwitch, StringComparer.Ordinal))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (!RekallAgeStudioAutomation.TryParse(e.Args, out var options, out var error))
                {
                    Log.Error("Studio automation arguments are invalid: {Error}", error);
                    Shutdown(2);
                    return;
                }

                var result = await RekallAgeStudioAutomation.RunAsync(options!, null, CancellationToken.None);
                Log.Information(
                    "Studio automation completed. Succeeded={Succeeded} Evidence={EvidencePath}",
                    result.Succeeded,
                    options!.EvidencePath);
                Shutdown(result.Succeeded ? 0 : 3);
                return;
            }

            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Rekall Studio failed during startup.");
            ReportFailure("studio.startup", "REKALL_STUDIO_STARTUP_FATAL", exception, "fatal");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
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
        Log.Fatal(e.Exception, "Unhandled dispatcher exception; Studio will terminate.");
        ReportFailure("studio.dispatcher", "REKALL_STUDIO_DISPATCHER_FATAL", e.Exception, "fatal");
        e.Handled = false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Unhandled application domain exception. IsTerminating={IsTerminating}", e.IsTerminating);
            ReportFailure("studio.app-domain", "REKALL_STUDIO_APPDOMAIN_FATAL", exception, "fatal");
            return;
        }

        Log.Fatal(
            "Unhandled application domain exception object. IsTerminating={IsTerminating} ExceptionObject={ExceptionObject}",
            e.IsTerminating,
            e.ExceptionObject);
        var objectType = e.ExceptionObject?.GetType().FullName ?? "<null>";
        ReportFailure(
            "studio.app-domain",
            "REKALL_STUDIO_APPDOMAIN_FATAL",
            new InvalidOperationException($"Unhandled non-Exception application-domain object type: {objectType}."),
            "fatal");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        ReportFailure("studio.unobserved-task", "REKALL_STUDIO_TASK_UNOBSERVED", e.Exception, "observed");
        e.SetObserved();
    }

    private static void ReportFailure(string category, string code, Exception exception, string outcome)
    {
        var result = FailureReporter.ReportAsync(
                new RekallAgeDesktopFailureRequest(
                    Component: "studio",
                    Outcome: outcome,
                    Category: category,
                    Code: code,
                    Exception: exception,
                    Limitations: ["Studio does not automatically restart after desktop failures."],
                    NextActions: ["rekall.diagnostics.inspect_failures"]),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (result.Written)
        {
            Log.Error("Structured failure report written. Code={Code} ReportPath={ReportPath}", code, result.Path);
        }
        else if (!result.Duplicate)
        {
            Log.Error("Structured failure report failed. Code={Code} ReportCode={ReportCode} Issue={Issue}", code, result.Code, result.Issue);
        }
    }
}
