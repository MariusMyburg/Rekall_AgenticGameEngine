using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Rekall.Age.Agent.LanguageModels;

public interface IRekallAgeGgufImporter
{
    ValueTask<RekallAgeGgufImportResult> ImportAsync(
        string ggufPath,
        CancellationToken cancellationToken);
}

public interface IRekallAgeOllamaProcessRunner
{
    ValueTask<RekallAgeOllamaProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed record RekallAgeOllamaProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record RekallAgeGgufImportResult(string ModelName);

public sealed class RekallAgeOllamaGgufImporter : IRekallAgeGgufImporter
{
    private readonly IRekallAgeOllamaProcessRunner _processRunner;

    public RekallAgeOllamaGgufImporter(IRekallAgeOllamaProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new RekallAgeOllamaProcessRunner();
    }

    public async ValueTask<RekallAgeGgufImportResult> ImportAsync(
        string ggufPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ggufPath)
            || !Path.GetExtension(ggufPath).Equals(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_FILE_INVALID",
                "gguf",
                "Select a local .gguf model file.");
        }
        var fullPath = Path.GetFullPath(ggufPath);
        if (!File.Exists(fullPath))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_FILE_NOT_FOUND",
                "gguf",
                "The selected GGUF model file no longer exists.");
        }
        if (!await HasGgufMagicAsync(fullPath, cancellationToken))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_FILE_INVALID",
                "gguf",
                "The selected file does not contain a valid GGUF header.");
        }

        var modelName = BuildModelName(fullPath);
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "RekallAge",
            "OllamaImports",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var modelfilePath = Path.Combine(tempDirectory, "Modelfile");
        try
        {
            var normalizedPath = fullPath.Replace('\\', '/');
            await File.WriteAllTextAsync(
                modelfilePath,
                $"FROM \"{normalizedPath}\"{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            RekallAgeOllamaProcessResult result;
            try
            {
                result = await _processRunner.RunAsync(
                    "ollama",
                    ["create", modelName, "-f", modelfilePath],
                    cancellationToken);
            }
            catch (Win32Exception)
            {
                throw new RekallAgeLanguageModelProviderException(
                    "REKALL_OLLAMA_RUNTIME_MISSING",
                    "gguf",
                    "Ollama is required to import and run GGUF models. Install Ollama and retry.");
            }
            catch (FileNotFoundException)
            {
                throw new RekallAgeLanguageModelProviderException(
                    "REKALL_OLLAMA_RUNTIME_MISSING",
                    "gguf",
                    "Ollama is required to import and run GGUF models. Install Ollama and retry.");
            }
            if (result.ExitCode != 0)
            {
                throw new RekallAgeLanguageModelProviderException(
                    "REKALL_GGUF_IMPORT_FAILED",
                    "gguf",
                    "Ollama could not import the selected GGUF model.",
                    providerDetail: $"Exit code {result.ExitCode}.");
            }
            return new RekallAgeGgufImportResult(modelName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The import result is authoritative; an OS-held temporary file can be reclaimed later.
            }
            catch (UnauthorizedAccessException)
            {
                // The import result is authoritative; an OS-held temporary file can be reclaimed later.
            }
        }
    }

    private static string BuildModelName(string fullPath)
    {
        var stem = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
        var safe = new StringBuilder();
        foreach (var character in stem)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                safe.Append(character);
            }
            else if (safe.Length > 0 && safe[^1] != '-')
            {
                safe.Append('-');
            }
            if (safe.Length == 40) break;
        }
        var normalizedStem = safe.ToString().Trim('-');
        if (normalizedStem.Length == 0) normalizedStem = "model";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath))).ToLowerInvariant()[..12];
        return $"rekall-{normalizedStem}-{hash}";
    }

    private static async ValueTask<bool> HasGgufMagicAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var magic = new byte[4];
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await stream.ReadExactlyAsync(magic, cancellationToken);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        return magic[0] == (byte)'G'
            && magic[1] == (byte)'G'
            && magic[2] == (byte)'U'
            && magic[3] == (byte)'F';
    }
}

public sealed class RekallAgeOllamaProcessRunner : IRekallAgeOllamaProcessRunner
{
    public async ValueTask<RekallAgeOllamaProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (exception is
                TimeoutException
                or InvalidOperationException
                or IOException
                or Win32Exception
                or ObjectDisposedException)
            {
                // Cancellation remains authoritative even if the OS cannot fully drain a terminating child.
            }
            throw;
        }
        return new RekallAgeOllamaProcessResult(
            process.ExitCode,
            Bound(await stdout),
            Bound(await stderr));
    }

    private static string Bound(string value) => value.Length <= 4_096 ? value : value[..4_096];
}
