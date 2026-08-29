using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace Rekall.Age.Studio;

[Flags]
internal enum RekallAgeStudioViewportDependencyChange
{
    None = 0,
    Assets = 1,
    Shaders = 2
}

internal interface IRekallAgeStudioViewportDependencyMonitor : IDisposable
{
    ValueTask<RekallAgeStudioViewportDependencyChange> PollAsync(
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioViewportDependencyMonitor :
    IRekallAgeStudioViewportDependencyMonitor
{
    private readonly string _projectRoot;
    private readonly FileSystemWatcher _watcher;
    private RekallAgeStudioViewportDependencyFingerprint _fingerprint;
    private int _potentialChange;
    private bool _disposed;

    internal RekallAgeStudioViewportDependencyMonitor(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(_projectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Studio viewport dependency root was not found: {_projectRoot}");
        }

        _watcher = new FileSystemWatcher(_projectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
            Filter = "*",
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnPotentialChange;
        _watcher.Created += OnPotentialChange;
        _watcher.Deleted += OnPotentialChange;
        _watcher.Renamed += OnPotentialChange;
        _watcher.Error += OnWatcherError;
        _fingerprint = RekallAgeStudioViewportDependencyFingerprint.Capture(_projectRoot);
    }

    public ValueTask<RekallAgeStudioViewportDependencyChange> PollAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _potentialChange, 0) == 0)
        {
            return ValueTask.FromResult(RekallAgeStudioViewportDependencyChange.None);
        }

        try
        {
            var current = RekallAgeStudioViewportDependencyFingerprint.Capture(_projectRoot);
            var change = RekallAgeStudioViewportDependencyChange.None;
            if (!string.Equals(current.AssetFingerprint, _fingerprint.AssetFingerprint, StringComparison.Ordinal))
            {
                change |= RekallAgeStudioViewportDependencyChange.Assets;
            }
            if (!string.Equals(current.ShaderFingerprint, _fingerprint.ShaderFingerprint, StringComparison.Ordinal))
            {
                change |= RekallAgeStudioViewportDependencyChange.Shaders;
            }
            _fingerprint = current;
            return ValueTask.FromResult(change);
        }
        catch (IOException)
        {
            // Editors commonly replace dependencies through a short-lived temporary file.
            // Retry after the next presentation rather than publishing an incoherent revision.
            Volatile.Write(ref _potentialChange, 1);
            return ValueTask.FromResult(RekallAgeStudioViewportDependencyChange.None);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnPotentialChange;
        _watcher.Created -= OnPotentialChange;
        _watcher.Deleted -= OnPotentialChange;
        _watcher.Renamed -= OnPotentialChange;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
    }

    private void OnPotentialChange(object sender, FileSystemEventArgs e) =>
        Volatile.Write(ref _potentialChange, 1);

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        Volatile.Write(ref _potentialChange, 1);
}

internal sealed record RekallAgeStudioViewportDependencyFingerprint(
    string AssetFingerprint,
    string ShaderFingerprint)
{
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tga", ".dds",
        ".ktx", ".ktx2", ".ppm", ".hdr", ".exr",
        ".glb", ".gltf", ".obj", ".fbx", ".dae", ".stl", ".ply", ".bin",
        ".ttf", ".otf", ".woff", ".woff2", ".fnt"
    };

    internal static RekallAgeStudioViewportDependencyFingerprint Capture(string projectRoot)
    {
        using var assets = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var shaders = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(projectRoot, path).Replace('\\', '/');
            var kind = Classify(relative);
            if (kind == RekallAgeStudioViewportDependencyChange.None) continue;
            var target = kind == RekallAgeStudioViewportDependencyChange.Shaders ? shaders : assets;
            Append(target, relative, path);
        }

        return new RekallAgeStudioViewportDependencyFingerprint(
            Convert.ToHexString(assets.GetHashAndReset()),
            Convert.ToHexString(shaders.GetHashAndReset()));
    }

    private static RekallAgeStudioViewportDependencyChange Classify(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.Equals("Shaders", StringComparison.OrdinalIgnoreCase)))
        {
            return RekallAgeStudioViewportDependencyChange.Shaders;
        }

        var extension = Path.GetExtension(relativePath);
        var assetDirectory = segments.Any(segment =>
            segment.Equals("Assets", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Materials", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Textures", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Models", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Fonts", StringComparison.OrdinalIgnoreCase));
        if (!assetDirectory) return RekallAgeStudioViewportDependencyChange.None;
        if (AssetExtensions.Contains(extension)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return RekallAgeStudioViewportDependencyChange.Assets;
        }

        return RekallAgeStudioViewportDependencyChange.None;
    }

    private static void Append(IncrementalHash hash, string relativePath, string path)
    {
        var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToUpperInvariant());
        hash.AppendData(pathBytes);
        hash.AppendData([0]);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        hash.AppendData([0xFF]);
    }
}
