using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Product;
using Rekall.Age.Project;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Workflows.Web;

public delegate ValueTask<ReadOnlyMemory<byte>> RekallAgeWebContentFetch(
    string logicalPath,
    long maximumBytes,
    CancellationToken cancellationToken);

public sealed record RekallAgeWebBootstrapDiagnostic(string Code, string Message);

public sealed record RekallAgeWebBootstrapEntityFact(
    string Id,
    string Name,
    IReadOnlyList<string> ComponentTypes);

public sealed record RekallAgeWebBootstrapEvidence(
    bool Succeeded,
    string State,
    string? BuildIdentity,
    string? ProjectName,
    string? EntryScenePath,
    IReadOnlyList<string> ModuleIds,
    int RuntimeFrameIndex,
    IReadOnlyList<string> SystemIds,
    IReadOnlyList<RekallAgeWebBootstrapEntityFact> Entities,
    IReadOnlyList<RekallAgeWebBootstrapDiagnostic> Diagnostics);

public sealed class RekallAgeWebGameSession : IDisposable
{
    internal RekallAgeWebGameSession(
        RekallAgeWebGameManifest manifest,
        IRekallAgeGameContent content,
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeExecutionLoop executionLoop)
    {
        Manifest = manifest;
        Content = content;
        World = world;
        ExecutionLoop = executionLoop;
    }

    public RekallAgeWebGameManifest Manifest { get; }

    public IRekallAgeGameContent Content { get; }

    public RekallAgeRuntimeWorld World { get; private set; }

    public RekallAgeRuntimeExecutionLoop ExecutionLoop { get; }

    public async ValueTask AdvanceAsync(
        int frames,
        CancellationToken cancellationToken,
        RekallAgeRuntimeInputState? input = null)
    {
        var result = await ExecutionLoop.RunAsync(World, frames, cancellationToken, input).ConfigureAwait(false);
        World = result.World;
    }

    public void Dispose() => ExecutionLoop.Dispose();
}

public sealed class RekallAgeWebGameBootstrapResult : IDisposable
{
    internal RekallAgeWebGameBootstrapResult(
        RekallAgeWebBootstrapEvidence evidence,
        RekallAgeWebGameSession? session)
    {
        Evidence = evidence;
        Session = session;
    }

    public RekallAgeWebBootstrapEvidence Evidence { get; }

    public RekallAgeWebGameSession? Session { get; }

    public void Dispose() => Session?.Dispose();
}

public sealed class RekallAgeWebGameBootstrapper
{
    public const string ManifestPath = "game.manifest.json";
    public const int MaximumModuleRegistrations = 1024;
    public const int MaximumRuntimeSystemRegistrations = 4096;
    public const int MaximumEvidenceEntities = 256;
    public const int MaximumEvidenceComponentsPerEntity = 128;
    public const int MaximumEvidenceTextLength = 512;

    public async ValueTask<RekallAgeWebGameBootstrapResult> BootAsync(
        RekallAgeWebContentFetch fetch,
        IReadOnlyList<RekallAgeRuntimeModuleRegistration> registrations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(registrations);
        RekallAgeWebGameManifest? manifest = null;
        try
        {
            var manifestBytes = await fetch(
                ManifestPath,
                RekallAgeWebGameManifestCodec.MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            manifest = DecodeManifest(manifestBytes);
            ValidateModuleRegistrations(manifest, registrations);
            var content = new ManifestContent(manifest, fetch);

            var projectEntry = await content.ReadAsync(
                RekallAgeProjectStore.ManifestFileName,
                RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
                cancellationToken).ConfigureAwait(false);
            var declaredProject = manifest.Content.Single(entry =>
                entry.Path.Equals(RekallAgeProjectStore.ManifestFileName, StringComparison.Ordinal));
            if (!manifest.Project.ProjectManifestSha256.Equals(declaredProject.Sha256, StringComparison.Ordinal))
            {
                throw Failure(
                    "REKALL_WEB_PROJECT_IDENTITY_MISMATCH",
                    "The web project identity does not match the declared project-manifest content hash.");
            }
            var project = RekallAgeProjectManifestCodec.Deserialize(projectEntry.Bytes, projectEntry.LogicalPath);
            if (!project.Name.Equals(manifest.Project.Name, StringComparison.Ordinal))
            {
                throw Failure(
                    "REKALL_WEB_PROJECT_IDENTITY_MISMATCH",
                    $"Project manifest name '{project.Name}' does not match web identity '{manifest.Project.Name}'.");
            }

            var sceneName = EntrySceneName(manifest.EntryScenePath);
            var sceneEntry = await content.ReadAsync(
                manifest.EntryScenePath,
                RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
                cancellationToken).ConfigureAwait(false);
            var scene = RekallAgeSceneCodec.Deserialize(
                sceneEntry.Bytes,
                sceneName,
                sceneEntry.LogicalPath);
            var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
            var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(registrations);
            RekallAgeRuntimeRunResult firstFrame;
            try
            {
                firstFrame = await loop.RunAsync(world, 1, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                loop.Dispose();
                throw;
            }

            var session = new RekallAgeWebGameSession(
                manifest,
                content,
                firstFrame.World,
                loop);
            return new RekallAgeWebGameBootstrapResult(
                CreateSuccessEvidence(manifest, firstFrame),
                session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            var diagnostic = Diagnostic(error);
            return new RekallAgeWebGameBootstrapResult(
                new RekallAgeWebBootstrapEvidence(
                    false,
                    "failed",
                    manifest?.BuildIdentity,
                    manifest?.Project.Name,
                    manifest?.EntryScenePath,
                    manifest?.Modules.Select(module => module.Id).ToArray() ?? [],
                    0,
                    [],
                    [],
                    [diagnostic]),
                null);
        }
    }

    private static RekallAgeWebGameManifest DecodeManifest(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            return RekallAgeWebGameManifestCodec.DecodeAndValidate(bytes);
        }
        catch (InvalidDataException error)
        {
            throw Failure("REKALL_WEB_MANIFEST_INVALID", error.Message, error);
        }
    }

    private static void ValidateModuleRegistrations(
        RekallAgeWebGameManifest manifest,
        IReadOnlyList<RekallAgeRuntimeModuleRegistration> registrations)
    {
        if (registrations.Count > MaximumModuleRegistrations)
        {
            throw Failure(
                "REKALL_WEB_MODULE_REGISTRATION_LIMIT",
                $"Web runtime bootstrap supports at most {MaximumModuleRegistrations} static authored-module registrations.");
        }
        if (registrations.Sum(registration => (long)(registration.RuntimeSystems?.Count ?? 0))
            > MaximumRuntimeSystemRegistrations)
        {
            throw Failure(
                "REKALL_WEB_SYSTEM_REGISTRATION_LIMIT",
                $"Web runtime bootstrap supports at most {MaximumRuntimeSystemRegistrations} static runtime-system registrations.");
        }
        var registeredModules = registrations
            .Select(registration => registration.ModuleId is not null
                && registration.ModuleName is not null
                && registration.AssemblyIdentity is not null
                && registration.SourceFingerprint is not null
                    ? new RekallAgeWebModuleIdentity(
                        registration.ModuleId,
                        registration.ModuleName,
                        registration.AssemblyIdentity,
                        registration.SourceFingerprint)
                    : null)
            .ToArray();
        var orderedRegisteredModules = registeredModules
            .OfType<RekallAgeWebModuleIdentity>()
            .OrderBy(module => module.Id, StringComparer.Ordinal)
            .ToArray();
        if (orderedRegisteredModules.Length != registrations.Count
            || orderedRegisteredModules.Select(module => module.Id).Distinct(StringComparer.Ordinal).Count()
                != orderedRegisteredModules.Length
            || !orderedRegisteredModules.SequenceEqual(manifest.Modules))
        {
            throw Failure(
                "REKALL_WEB_MODULE_REGISTRATION_MISMATCH",
                "Static authored-module registrations do not match the canonical web manifest module identities.");
        }
    }

    private static string EntrySceneName(string entryScenePath)
    {
        const string suffix = ".age.scene.json";
        var fileName = Path.GetFileName(entryScenePath);
        if (!fileName.EndsWith(suffix, StringComparison.Ordinal)
            || fileName.Length == suffix.Length)
        {
            throw Failure(
                "REKALL_WEB_ENTRY_SCENE_INVALID",
                $"Entry scene '{entryScenePath}' must end with '{suffix}'.");
        }
        return fileName[..^suffix.Length];
    }

    private static RekallAgeWebBootstrapEvidence CreateSuccessEvidence(
        RekallAgeWebGameManifest manifest,
        RekallAgeRuntimeRunResult firstFrame) =>
        new(
            true,
            "runtime-world-ready",
            manifest.BuildIdentity,
            manifest.Project.Name,
            manifest.EntryScenePath,
            manifest.Modules.Select(module => module.Id).ToArray(),
            firstFrame.World.FrameIndex,
            firstFrame.SystemsRun
                .Take(MaximumRuntimeSystemRegistrations + 32)
                .Select(id => BoundedEvidenceText(id))
                .ToArray(),
            firstFrame.World.Entities
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                .Take(MaximumEvidenceEntities)
                .Select(entity => new RekallAgeWebBootstrapEntityFact(
                    BoundedEvidenceText(entity.Id),
                    BoundedEvidenceText(entity.Name),
                    entity.Components
                        .Select(component => component.Type)
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .Take(MaximumEvidenceComponentsPerEntity)
                        .Select(type => BoundedEvidenceText(type))
                        .ToArray()))
                .ToArray(),
            []);

    private static RekallAgeWebBootstrapDiagnostic Diagnostic(Exception error)
    {
        var diagnostic = error switch
        {
            BootstrapException bootstrap => new RekallAgeWebBootstrapDiagnostic(bootstrap.Code, bootstrap.Message),
            RekallAgeGameContentException content => new RekallAgeWebBootstrapDiagnostic(content.Code, content.Message),
            RekallAgeDocumentCompatibilityException compatibility => new RekallAgeWebBootstrapDiagnostic(compatibility.Code, compatibility.Message),
            System.Text.Json.JsonException json => new RekallAgeWebBootstrapDiagnostic("REKALL_WEB_CONTENT_INVALID", json.Message),
            InvalidDataException invalid => new RekallAgeWebBootstrapDiagnostic("REKALL_WEB_CONTENT_INVALID", invalid.Message),
            _ => new RekallAgeWebBootstrapDiagnostic("REKALL_WEB_BOOTSTRAP_FAILED", error.Message)
        };
        return diagnostic with { Message = BoundedEvidenceText(diagnostic.Message, 2048) };
    }

    private static string BoundedEvidenceText(string value, int maximumLength = MaximumEvidenceTextLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static BootstrapException Failure(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed class BootstrapException(string code, string message, Exception? inner = null)
        : IOException(message, inner)
    {
        public string Code { get; } = code;
    }

    private sealed class ManifestContent(
        RekallAgeWebGameManifest manifest,
        RekallAgeWebContentFetch fetch) : IRekallAgeGameContent
    {
        private readonly IReadOnlyDictionary<string, RekallAgeWebContentEntry> _entries = manifest.Content
            .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

        public async ValueTask<RekallAgeGameContentEntry> ReadAsync(
            string logicalPath,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, int.MaxValue);
            var normalized = RekallAgeGameContentPath.Normalize(logicalPath);
            if (!_entries.TryGetValue(normalized, out var declared))
            {
                throw new RekallAgeGameContentException(
                    "REKALL_WEB_CONTENT_UNDECLARED",
                    normalized,
                    $"Web game content '{normalized}' is not declared in the canonical manifest.");
            }
            if (declared.SizeBytes > maximumBytes)
            {
                throw new RekallAgeGameContentException(
                    "REKALL_GAME_CONTENT_TOO_LARGE",
                    normalized,
                    $"Web game content '{normalized}' is {declared.SizeBytes} bytes; the read limit is {maximumBytes} bytes.");
            }
            if (!_cache.TryGetValue(normalized, out var bytes))
            {
                var fetched = await fetch(
                    normalized,
                    Math.Max(1, declared.SizeBytes),
                    cancellationToken).ConfigureAwait(false);
                bytes = fetched.ToArray();
                if (bytes.LongLength != declared.SizeBytes)
                {
                    throw new RekallAgeGameContentException(
                        "REKALL_WEB_CONTENT_SIZE_MISMATCH",
                        normalized,
                        $"Web game content '{normalized}' size does not match its canonical manifest entry.");
                }
                var actualHash = RekallAgeDocumentRevision.Compute(bytes);
                if (!actualHash.Equals(declared.Sha256, StringComparison.Ordinal))
                {
                    throw new RekallAgeGameContentException(
                        "REKALL_WEB_CONTENT_HASH_MISMATCH",
                        normalized,
                        $"Web game content '{normalized}' hash does not match its canonical manifest entry.");
                }
                _cache.Add(normalized, bytes);
            }
            return new RekallAgeGameContentEntry(normalized, bytes.ToArray());
        }
    }
}
