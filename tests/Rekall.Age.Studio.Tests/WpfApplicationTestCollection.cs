using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Windows;

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
    private readonly BlockingCollection<Action> _work = new();
    private Exception? _startupFailure;

    public WpfApplicationTestFixture()
    {
        _thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ready.Set();
                foreach (var action in _work.GetConsumingEnumerable()) action();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                _startupFailure = exception;
                _ready.Set();
            }
        });
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
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        _work.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        if (!completed.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("WPF Application fixture action did not complete within thirty seconds.");
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(10));
        _work.Dispose();
        _ready.Dispose();
    }
}
