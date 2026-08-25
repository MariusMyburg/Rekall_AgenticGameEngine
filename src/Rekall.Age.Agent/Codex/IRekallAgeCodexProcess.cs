using System.Diagnostics;

namespace Rekall.Age.Agent.Codex;

public interface IRekallAgeCodexProcess : IAsyncDisposable
{
    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    TextWriter StandardInput { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    ValueTask CloseStandardInputAsync();

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);
}

public interface IRekallAgeCodexProcessFactory
{
    IRekallAgeCodexProcess Start(ProcessStartInfo startInfo);
}
