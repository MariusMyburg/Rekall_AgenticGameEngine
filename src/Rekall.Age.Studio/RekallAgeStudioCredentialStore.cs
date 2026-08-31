using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioCredentialStore
{
    ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken);

    ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken);

    ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioCredentialStoreException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed class RekallAgeStudioDpapiCredentialStore : IRekallAgeStudioCredentialStore
{
    private const string OpenAiProviderId = "openai";
    private const string KimiProviderId = "kimi";
    private const string EntropyTargetPrefix = "Rekall AGE Studio/";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _credentialDirectory;

    public RekallAgeStudioDpapiCredentialStore()
        : this(System.IO.Path.Combine(RekallAgeStudioLanguageModelSetupStore.ResolveSetupRoot(), "Credentials"))
    {
    }

    internal RekallAgeStudioDpapiCredentialStore(string credentialDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialDirectory);
        _credentialDirectory = System.IO.Path.GetFullPath(credentialDirectory);
    }

    internal string CredentialDirectory => _credentialDirectory;

    public async ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = CredentialPathFor(providerId);
        if (!File.Exists(path)) return null;

        byte[] protectedCredential;
        try
        {
            protectedCredential = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_READ_FAILED",
                "The stored credential could not be read.",
                exception);
        }

        byte[] plainCredential;
        try
        {
            plainCredential = ProtectedData.Unprotect(
                protectedCredential,
                EntropyFor(providerId),
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_CORRUPT",
                "The stored credential is corrupt.",
                exception);
        }

        try
        {
            var credential = Utf8.GetString(plainCredential);
            if (string.IsNullOrWhiteSpace(credential))
            {
                throw new RekallAgeStudioCredentialStoreException(
                    "REKALL_CREDENTIAL_STORE_CORRUPT",
                    "The stored credential is corrupt.");
            }

            return credential;
        }
        catch (DecoderFallbackException exception)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_CORRUPT",
                "The stored credential is corrupt.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainCredential);
        }
    }

    public async ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = CredentialPathFor(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        var plainCredential = Utf8.GetBytes(credential);
        byte[] protectedCredential;
        try
        {
            protectedCredential = ProtectedData.Protect(
                plainCredential,
                EntropyFor(providerId),
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_PROTECT_FAILED",
                "The credential could not be protected.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainCredential);
        }

        try
        {
            await WriteAtomicallyAsync(path, protectedCredential, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_WRITE_FAILED",
                "The credential could not be stored.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCredential);
        }
    }

    public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = CredentialPathFor(providerId);
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RekallAgeStudioCredentialStoreException(
                "REKALL_CREDENTIAL_STORE_REMOVE_FAILED",
                "The credential could not be removed.",
                exception);
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask WriteAtomicallyAsync(string path, byte[] protectedCredential, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_credentialDirectory);
        var temporaryPath = System.IO.Path.Combine(
            _credentialDirectory,
            $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        Exception? primaryFailure = null;
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedCredential, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch when (primaryFailure is not null)
            {
                // Preserve the write or cancellation failure over a temporary-file cleanup failure.
            }
        }
    }

    private string CredentialPathFor(string providerId) => System.IO.Path.Combine(
        _credentialDirectory,
        providerId switch
        {
            OpenAiProviderId => "openai.dpapi",
            KimiProviderId => "kimi.dpapi",
            _ => throw new ArgumentException("Unsupported credential provider.", nameof(providerId))
        });

    private static byte[] EntropyFor(string providerId) => Utf8.GetBytes(EntropyTargetPrefix + providerId);
}
