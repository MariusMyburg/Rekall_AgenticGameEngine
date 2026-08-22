using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;
using Veldrid;

namespace Rekall.Age.Player.Windows;

internal sealed record RekallAgeRuntimeGpuExecutionReport(
    int EnabledWorkloads,
    int ExecutedWorkloads,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics);

/// <summary>Caches, compiles, and records module-authored workloads into the active Player frame.</summary>
internal sealed class RekallAgeVeldridRuntimeGpuWorkloadExecutor : IDisposable
{
    public const string SceneColorImport = "engine.scene-color";
    public const string OutputRenderTargetImport = "engine.output";

    private static readonly JsonSerializerOptions HashOptions = CreateHashOptions();
    private readonly RekallAgeVeldridRenderingDevice _device;
    private readonly Dictionary<string, CachedWorkload> _cache = new(StringComparer.Ordinal);
    private Texture? _sceneColor;
    private Framebuffer? _output;
    private RekallAgeGraphicsResourceHandle _sceneColorHandle;
    private RekallAgeGraphicsResourceHandle _outputHandle;
    private bool _disposed;

    public RekallAgeVeldridRuntimeGpuWorkloadExecutor(GraphicsDevice device, CommandList commands) =>
        _device = new(device, commands);

    public RekallAgeRuntimeGpuExecutionReport Record(
        IReadOnlyList<RekallAgeRuntimeGpuWorkload> workloads,
        Texture sceneColor,
        Framebuffer output)
    {
        ArgumentNullException.ThrowIfNull(workloads);
        ArgumentNullException.ThrowIfNull(sceneColor);
        ArgumentNullException.ThrowIfNull(output);
        ThrowIfDisposed();
        EnsureImports(sceneColor, output);

        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        var enabled = workloads.Where(workload => workload.Enabled).ToArray();
        foreach (var duplicate in enabled.GroupBy(workload => workload.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_DUPLICATE", $"Enabled runtime workload ID '{duplicate.Key}' occurs more than once.", duplicate.Key));
        if (diagnostics.Count > 0) return new(enabled.Length, 0, diagnostics);

        var activeIds = enabled.Select(workload => workload.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _cache.Keys.Where(id => !activeIds.Contains(id)).ToArray()) Remove(stale);

        var executed = 0;
        foreach (var workload in enabled)
        {
            var hash = JsonSerializer.Serialize(workload, HashOptions);
            if (!_cache.TryGetValue(workload.Id, out var cached) || !cached.Hash.Equals(hash, StringComparison.Ordinal))
            {
                Remove(workload.Id);
                var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
                    workload,
                    _device,
                    new Dictionary<string, RekallAgeGraphicsResourceHandle>(StringComparer.Ordinal)
                    {
                        [SceneColorImport] = _sceneColorHandle,
                        [OutputRenderTargetImport] = _outputHandle
                    });
                cached = new(hash, compiled);
                _cache.Add(workload.Id, cached);
            }

            if (!cached.Compiled.Valid)
            {
                diagnostics.AddRange(cached.Compiled.Diagnostics);
                continue;
            }

            var submitted = _device.Submit(cached.Compiled.CommandBuffer!);
            if (!submitted.Valid) diagnostics.AddRange(submitted.Diagnostics);
            else executed++;
        }
        return new(enabled.Length, executed, diagnostics);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var cached in _cache.Values) cached.Compiled.Dispose();
        _cache.Clear();
        DestroyImports();
        _device.Dispose();
        _disposed = true;
    }

    public void InvalidateFrameResources()
    {
        ThrowIfDisposed();
        foreach (var cached in _cache.Values) cached.Compiled.Dispose();
        _cache.Clear();
        DestroyImports();
    }

    private void EnsureImports(Texture sceneColor, Framebuffer output)
    {
        if (ReferenceEquals(sceneColor, _sceneColor) && ReferenceEquals(output, _output)) return;
        InvalidateFrameResources();
        _sceneColor = sceneColor;
        _output = output;
        _sceneColorHandle = _device.ImportTexture(sceneColor, SceneColorImport);
        _outputHandle = _device.ImportRenderTarget(output, OutputRenderTargetImport);
    }

    private void DestroyImports()
    {
        if (_sceneColorHandle.IsValid) _device.Destroy(_sceneColorHandle);
        if (_outputHandle.IsValid) _device.Destroy(_outputHandle);
        _sceneColorHandle = default;
        _outputHandle = default;
        _sceneColor = null;
        _output = null;
    }

    private void Remove(string id)
    {
        if (!_cache.Remove(id, out var cached)) return;
        cached.Compiled.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static JsonSerializerOptions CreateHashOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CachedWorkload(string Hash, RekallAgeCompiledGpuWorkload Compiled);
}
