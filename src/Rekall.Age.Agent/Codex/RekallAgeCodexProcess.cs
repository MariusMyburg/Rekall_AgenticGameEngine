using System.Diagnostics;

namespace Rekall.Age.Agent.Codex;

public sealed class RekallAgeCodexProcessFactory : IRekallAgeCodexProcessFactory
{
    public IRekallAgeCodexProcess Start(ProcessStartInfo startInfo) =>
        RekallAgeCodexProcess.Start(startInfo);
}

public sealed class RekallAgeCodexProcess : IRekallAgeCodexProcess
{
    private readonly Process _process;
    private int _inputClosed;
    private int _disposed;

    private RekallAgeCodexProcess(Process process)
    {
        _process = process;
    }

    public TextReader StandardOutput => _process.StandardOutput;

    public TextReader StandardError => _process.StandardError;

    public TextWriter StandardInput => _process.StandardInput;

    public bool HasExited => _process.HasExited;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public static RekallAgeCodexProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Codex App Server process could not be started.");
        return new RekallAgeCodexProcess(process);
    }

    public async ValueTask CloseStandardInputAsync()
    {
        if (Interlocked.Exchange(ref _inputClosed, 1) == 0)
        {
            await _process.StandardInput.DisposeAsync();
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
