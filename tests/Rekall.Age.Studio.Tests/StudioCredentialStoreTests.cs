using System.IO;
using System.Text;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioCredentialStoreTests
{
    [Fact]
    public async Task WriteReadAndRemoveAsyncKeepsProviderCredentialsIsolatedAndProtected()
    {
        using var directory = new TemporaryDirectory();
        var store = new RekallAgeStudioDpapiCredentialStore(directory.Path);
        const string openAiCredential = "openai-sentinel-secret";
        const string kimiCredential = "kimi-sentinel-secret";

        await store.WriteAsync("openai", openAiCredential, CancellationToken.None);
        await store.WriteAsync("kimi", kimiCredential, CancellationToken.None);

        Assert.Equal(openAiCredential, await store.ReadAsync("openai", CancellationToken.None));
        Assert.Equal(kimiCredential, await store.ReadAsync("kimi", CancellationToken.None));
        AssertCredentialFileDoesNotContain(directory.File("openai.dpapi"), openAiCredential, kimiCredential);
        AssertCredentialFileDoesNotContain(directory.File("kimi.dpapi"), openAiCredential, kimiCredential);
        Assert.DoesNotContain(openAiCredential, store.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(kimiCredential, store.ToString(), StringComparison.Ordinal);

        await store.RemoveAsync("openai", CancellationToken.None);

        Assert.Null(await store.ReadAsync("openai", CancellationToken.None));
        Assert.Equal(kimiCredential, await store.ReadAsync("kimi", CancellationToken.None));
    }

    [Fact]
    public async Task OperationsRejectUnsupportedProvidersAndWhitespaceCredentials()
    {
        using var directory = new TemporaryDirectory();
        var store = new RekallAgeStudioDpapiCredentialStore(directory.Path);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ReadAsync("ollama", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.RemoveAsync("codex", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.WriteAsync("openai", " \t ", CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task ReadAsyncReturnsARedactedStableFailureForCorruptProtectedPayloads()
    {
        using var directory = new TemporaryDirectory();
        var store = new RekallAgeStudioDpapiCredentialStore(directory.Path);
        var corruptPayload = Encoding.UTF8.GetBytes("corrupt-payload-sentinel");
        await File.WriteAllBytesAsync(directory.File("openai.dpapi"), corruptPayload);

        var exception = await Assert.ThrowsAsync<RekallAgeStudioCredentialStoreException>(async () =>
            await store.ReadAsync("openai", CancellationToken.None));

        Assert.Equal("REKALL_CREDENTIAL_STORE_CORRUPT", exception.Code);
        Assert.DoesNotContain("corrupt-payload-sentinel", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(corruptPayload), exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultStoreUsesTheApprovedSetupRootOverrideForCredentialFiles()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable(RekallAgeStudioLanguageModelSetupStore.SetupRootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(RekallAgeStudioLanguageModelSetupStore.SetupRootEnvironmentVariable, directory.Path);

            var store = new RekallAgeStudioDpapiCredentialStore();

            Assert.Equal(directory.File("Credentials"), store.CredentialDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RekallAgeStudioLanguageModelSetupStore.SetupRootEnvironmentVariable, previous);
        }
    }

    private static void AssertCredentialFileDoesNotContain(string path, params string[] credentials)
    {
        var content = File.ReadAllBytes(path);
        var utf8 = Encoding.UTF8.GetString(content);
        var utf16 = Encoding.Unicode.GetString(content);
        foreach (var credential in credentials)
        {
            Assert.DoesNotContain(credential, utf8, StringComparison.Ordinal);
            Assert.DoesNotContain(credential, utf16, StringComparison.Ordinal);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rekall-age-credentials-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
