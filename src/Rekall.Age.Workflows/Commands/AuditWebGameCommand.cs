using System.Net;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Workflows.Commands;

public sealed record AuditWebGameRequest(
    string ProjectRoot,
    string SceneName = "Main",
    string? OutputDirectory = null);

public sealed record RekallAgeWebGameAuditCheck(
    string Name,
    bool Passed,
    string Summary);

public sealed record AuditWebGameResult(
    bool Ready,
    PublishWebGameResult Publish,
    IReadOnlyList<RekallAgeWebGameAuditCheck> Checks,
    bool BrowserSmokeFrameVerified,
    string BrowserSmokeFrameSummary);

/// <summary>
/// Proves a published web game by publishing it and then verifying it, one operation, the same shape as
/// <see cref="AuditPlayablePackageCommand"/> for the native playable package. This closes the manifest/hash,
/// compatibility, module-registry, capability-coverage, relocation, and static-server-boot checks the publishing
/// plan calls for. It deliberately does NOT claim a "browser smoke frame" check: no in-browser session is run here,
/// so <see cref="AuditWebGameResult.BrowserSmokeFrameVerified"/> is always reported false with an explicit reason
/// rather than silently omitted or faked as passing, and it is kept out of the <see cref="AuditWebGameResult.Ready"/>
/// gate so the command stays usable pending that real browser verification (tracked separately).
/// </summary>
public sealed class AuditWebGameCommand : IRekallAgeCommand<AuditWebGameRequest, AuditWebGameResult>
{
    private readonly PublishWebGameCommand _publish = new();

    public string Name => "rekall.game.audit_web";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Publishes and then proves an authored project's web build in one operation: manifest/hash integrity, engine/module/capability compatibility, byte-identical relocation from the staged content closure, WebAssembly runtime identity, and that the output directory actually boots as a static server. Does not perform a real browser smoke test -- see BrowserSmokeFrameVerified.",
        typeof(AuditWebGameRequest).FullName!,
        typeof(AuditWebGameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AuditWebGameResult>> ExecuteAsync(
        AuditWebGameRequest request,
        RekallAgeCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var publish = await _publish.ExecuteAsync(
            new PublishWebGameRequest(request.ProjectRoot, request.SceneName, request.OutputDirectory),
            context);
        if (!publish.Ok)
        {
            var failedResult = new AuditWebGameResult(
                false,
                publish.Value,
                [new RekallAgeWebGameAuditCheck("publish", false, publish.Summary)],
                false,
                "Not verified: publishing failed before a browser session could be considered.");
            return RekallAgeCommandResult<AuditWebGameResult>.Failure(
                failedResult,
                "Web game audit failed: publishing did not succeed.",
                publish.Errors);
        }

        var checks = new List<RekallAgeWebGameAuditCheck>();
        RekallAgeWebGameManifest? manifest = null;
        try
        {
            var manifestBytes = await File.ReadAllBytesAsync(publish.Value.ManifestPath, context.CancellationToken);
            manifest = RekallAgeWebGameManifestCodec.DecodeAndValidate(manifestBytes);
            checks.Add(new RekallAgeWebGameAuditCheck(
                "manifest-integrity",
                true,
                "Published manifest decodes, and its engine/module-SDK/project-schema identity and content listing are internally canonical and self-consistent."));
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            checks.Add(new RekallAgeWebGameAuditCheck("manifest-integrity", false, error.Message));
        }

        checks.Add(CheckModuleCoverage(request.ProjectRoot, manifest));
        checks.Add(CheckContentRelocation(publish.Value, manifest));
        checks.Add(CheckRuntimeIdentity(publish.Value.OutputDirectory));
        // The browser-wasm publish serves everything from wwwroot -- the same directory the staged game content
        // (game.manifest.json and its siblings) was copied into, not the raw dotnet publish output root, which
        // also holds non-served build artifacts (the managed .dll, deps.json, etc).
        var webRoot = Path.GetDirectoryName(publish.Value.ManifestPath) ?? publish.Value.OutputDirectory;
        var serverCheck = await CheckStaticServerBootAsync(webRoot, context.CancellationToken);
        checks.Add(serverCheck);

        var ready = checks.All(check => check.Passed);
        var result = new AuditWebGameResult(
            ready,
            publish.Value,
            checks,
            false,
            "Not yet implemented: this audit gathers source/build/relocation/static-server evidence only (evidence-hierarchy tiers 1-3). No real browser or Chromium session has loaded this build, so real player launch and visual/gameplay proof (tiers 4-5) are not claimed here.");

        if (!ready)
        {
            return RekallAgeCommandResult<AuditWebGameResult>.Failure(
                result,
                "Web game audit failed.",
                checks.Where(check => !check.Passed)
                    .Select(check => new RekallAgeCommandError("REKALL_WEB_GAME_AUDIT_FAILED", check.Summary, check.Name))
                    .ToArray());
        }

        return RekallAgeCommandResult<AuditWebGameResult>.Success(result, "Web game audit passed.");
    }

    private static RekallAgeWebGameAuditCheck CheckModuleCoverage(string projectRoot, RekallAgeWebGameManifest? manifest)
    {
        if (manifest is null)
        {
            return new RekallAgeWebGameAuditCheck(
                "module-registry-coverage",
                false,
                "Skipped: no valid manifest was available to compare against the authored project's modules.");
        }

        try
        {
            var discovery = new RekallAgeWebModuleRegistryGenerator().Discover(projectRoot);
            var expectedIds = discovery.Modules.Select(module => module.Identity.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var manifestIds = manifest.Modules.Select(module => module.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (!expectedIds.SequenceEqual(manifestIds, StringComparer.Ordinal))
            {
                return new RekallAgeWebGameAuditCheck(
                    "module-registry-coverage",
                    false,
                    $"Manifest module IDs [{string.Join(", ", manifestIds)}] do not match the authored project's discovered modules [{string.Join(", ", expectedIds)}].");
            }

            return new RekallAgeWebGameAuditCheck(
                "module-registry-coverage",
                true,
                $"All {expectedIds.Length} authored module(s) are present in the published manifest.");
        }
        catch (InvalidDataException error)
        {
            return new RekallAgeWebGameAuditCheck("module-registry-coverage", false, error.Message);
        }
    }

    private static RekallAgeWebGameAuditCheck CheckContentRelocation(PublishWebGameResult publish, RekallAgeWebGameManifest? manifest)
    {
        if (manifest is null)
        {
            return new RekallAgeWebGameAuditCheck(
                "content-relocation",
                false,
                "Skipped: no valid manifest was available to check declared content against the published output.");
        }

        var contentRoot = Path.GetDirectoryName(publish.ManifestPath)!;
        var missing = new List<string>();
        var mismatched = new List<string>();
        foreach (var entry in manifest.Content)
        {
            var path = Path.Combine(contentRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing.Add(entry.Path);
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var hash = Rekall.Age.Core.Persistence.RekallAgeDocumentRevision.Compute(bytes);
            if (!string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
            {
                mismatched.Add(entry.Path);
            }
        }

        if (missing.Count > 0 || mismatched.Count > 0)
        {
            var summary = string.Join(" ", new[]
            {
                missing.Count > 0 ? $"Missing: {string.Join(", ", missing)}." : null,
                mismatched.Count > 0 ? $"Hash mismatch: {string.Join(", ", mismatched)}." : null
            }.Where(text => text is not null));
            return new RekallAgeWebGameAuditCheck("content-relocation", false, summary);
        }

        return new RekallAgeWebGameAuditCheck(
            "content-relocation",
            true,
            $"All {manifest.Content.Count} declared content file(s) relocated byte-identical to the published output, matching their manifest hash.");
    }

    private static RekallAgeWebGameAuditCheck CheckRuntimeIdentity(string outputDirectory)
    {
        var files = Directory.Exists(outputDirectory)
            ? Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories).ToArray()
            : Array.Empty<string>();
        var hasBootLoader = files.Any(path => Path.GetFileName(path).Equals("dotnet.js", StringComparison.OrdinalIgnoreCase));
        var hasWasm = files.Any(path => Path.GetExtension(path).Equals(".wasm", StringComparison.OrdinalIgnoreCase));
        var hasIndex = files.Any(path => Path.GetFileName(path).Equals("index.html", StringComparison.OrdinalIgnoreCase));
        if (hasBootLoader && hasWasm && hasIndex)
        {
            return new RekallAgeWebGameAuditCheck(
                "runtime-identity",
                true,
                "The published output contains the .NET WebAssembly boot loader, at least one .wasm module, and index.html.");
        }

        var missing = new List<string>();
        if (!hasBootLoader) missing.Add("dotnet.js");
        if (!hasWasm) missing.Add("*.wasm");
        if (!hasIndex) missing.Add("index.html");
        return new RekallAgeWebGameAuditCheck(
            "runtime-identity",
            false,
            $"Published output is missing required WebAssembly runtime artifact(s): {string.Join(", ", missing)}.");
    }

    private static async ValueTask<RekallAgeWebGameAuditCheck> CheckStaticServerBootAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return new RekallAgeWebGameAuditCheck("static-server-boot", false, $"Published output directory '{outputDirectory}' does not exist.");
        }

        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetAvailablePort()}/";
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException error)
        {
            return new RekallAgeWebGameAuditCheck("static-server-boot", false, $"Could not bind a loopback listener: {error.Message}");
        }

        try
        {
            var serverTask = Task.Run(() => ServeOnceAsync(listener, outputDirectory), cancellationToken);
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var indexResponse = await httpClient.GetAsync(new Uri(prefix + "index.html"), cancellationToken);
            var manifestResponse = await httpClient.GetAsync(new Uri(prefix + "game.manifest.json"), cancellationToken);
            await serverTask;
            if (indexResponse.IsSuccessStatusCode && manifestResponse.IsSuccessStatusCode)
            {
                return new RekallAgeWebGameAuditCheck(
                    "static-server-boot",
                    true,
                    "The published output directory served index.html and game.manifest.json over a real loopback HTTP listener.");
            }

            return new RekallAgeWebGameAuditCheck(
                "static-server-boot",
                false,
                $"Static server responded index.html={(int)indexResponse.StatusCode}, game.manifest.json={(int)manifestResponse.StatusCode}.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return new RekallAgeWebGameAuditCheck("static-server-boot", false, $"Static server boot failed: {error.Message}");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ServeOnceAsync(HttpListener listener, string outputDirectory)
    {
        for (var i = 0; i < 2; i++)
        {
            HttpListenerContext requestContext;
            try
            {
                requestContext = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var relativePath = requestContext.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
            var filePath = Path.GetFullPath(Path.Combine(outputDirectory, relativePath));
            if (!filePath.StartsWith(Path.GetFullPath(outputDirectory), StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                requestContext.Response.StatusCode = 404;
                requestContext.Response.Close();
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(filePath);
            requestContext.Response.ContentLength64 = bytes.Length;
            await requestContext.Response.OutputStream.WriteAsync(bytes);
            requestContext.Response.Close();
        }
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
