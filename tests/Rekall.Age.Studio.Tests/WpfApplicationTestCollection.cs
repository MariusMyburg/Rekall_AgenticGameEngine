using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Rekall.Age.Studio.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationTestCollection : ICollectionFixture<WpfApplicationTestFixture>
{
    public const string Name = "WPF Application";
}

public sealed class WpfApplicationTestFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private App? _application;
    private Dispatcher? _dispatcher;
    private Exception? _startupFailure;
    private bool _disposed;

    public WpfApplicationTestFixture()
    {
        _thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _application = app;
                _dispatcher = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                _startupFailure = exception;
                _ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Rekall AGE WPF test application"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("WPF Application fixture did not start within ten seconds.");
        }
        if (_startupFailure is not null) ExceptionDispatchInfo.Capture(_startupFailure).Throw();
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeAsync(() =>
        {
            var synchronizationContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                action();
                return Task.CompletedTask;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            }
        }).GetAwaiter().GetResult();
    }

    public T Invoke<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        T result = default!;
        Invoke(() => { result = action(); });
        return result;
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dispatcher = _dispatcher
            ?? throw new InvalidOperationException("The WPF Application fixture has not finished starting.");
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await action();
                completed.TrySetResult();
            }
            catch (Exception exception)
            {
                completed.TrySetException(exception);
            }
        }), DispatcherPriority.Send);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                _application?.Shutdown();
                if (!dispatcher.HasShutdownStarted) dispatcher.InvokeShutdown();
            }), DispatcherPriority.Send);
        }

        var stopped = _thread.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
        if (!stopped)
        {
            throw new TimeoutException(
                "The shared WPF Application thread did not stop within ten seconds after shutdown was requested.");
        }
    }
}
