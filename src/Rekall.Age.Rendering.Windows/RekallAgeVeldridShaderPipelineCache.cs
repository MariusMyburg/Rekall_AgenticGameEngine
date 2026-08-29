using System.Text;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Veldrid;
using Veldrid.SPIRV;

namespace Rekall.Age.Rendering.Windows;

internal sealed record RekallAgeVeldridShaderPipelineSelection(
    Pipeline Pipeline,
    string ContentHash,
    bool RetainedPreviousValid);

internal sealed class RekallAgeVeldridShaderPipelineCache : IDisposable
{
    private const int MaximumCachedPipelinePairs = 64;
    private readonly string _projectRoot;
    private readonly ResourceFactory _factory;
    private readonly VertexLayoutDescription _vertexLayout;
    private readonly ResourceLayout[] _resourceLayouts;
    private readonly OutputDescription _output;
    private readonly Action _waitForIdle;
    private readonly Action<string> _log;
    private readonly Dictionary<RekallAgeRuntimeViewportShaderPipeline, Entry> _entries = [];
    private readonly Dictionary<string, PipelinePair> _pairsByContentHash = new(StringComparer.Ordinal);
    private bool _disposed;

    public RekallAgeVeldridShaderPipelineCache(
        string projectRoot,
        ResourceFactory factory,
        VertexLayoutDescription vertexLayout,
        IReadOnlyList<ResourceLayout> resourceLayouts,
        OutputDescription output,
        Action waitForIdle,
        Action<string> log)
    {
        _projectRoot = projectRoot;
        _factory = factory;
        _vertexLayout = vertexLayout;
        _resourceLayouts = resourceLayouts.ToArray();
        _output = output;
        _waitForIdle = waitForIdle;
        _log = log;
    }

    public RekallAgeVeldridShaderPipelineSelection Resolve(
        RekallAgeRuntimeViewportShaderPipeline reference,
        bool transparent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_entries.TryGetValue(reference, out var entry) && !entry.Dirty)
        {
            return Select(entry.Pair, transparent, retainedPreviousValid: false);
        }

        var resolved = new RekallAgeProjectShaderPipelineResolver()
            .ResolveAsync(_projectRoot, reference, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (!resolved.Valid)
        {
            var diagnostic = string.Join(" | ", resolved.Errors.Take(16));
            if (entry is not null)
            {
                entry.Dirty = false;
                _log(
                    $"REKALL_SHADER_HOT_RELOAD_RETAINED: '{reference.VertexShader}' + " +
                    $"'{reference.FragmentShader}' remained on {entry.Pair.ContentHash}. {diagnostic}");
                return Select(entry.Pair, transparent, retainedPreviousValid: true);
            }

            throw new InvalidOperationException(
                $"Project shader pipeline '{reference.VertexShader}' + '{reference.FragmentShader}' is invalid: {diagnostic}");
        }

        if (!_pairsByContentHash.TryGetValue(resolved.Key.ContentHash, out var pair))
        {
            TrimUnusedPairs();
            pair = CreatePair(resolved);
            _pairsByContentHash.Add(resolved.Key.ContentHash, pair);
        }

        _entries[reference] = new Entry(pair);
        _log(
            $"Project shader pipeline ready vertex={reference.VertexShader} fragment={reference.FragmentShader} " +
            $"hash={resolved.Key.ContentHash}.");
        return Select(pair, transparent, retainedPreviousValid: false);
    }

    public void InvalidateAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var entry in _entries.Values)
        {
            entry.Dirty = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_pairsByContentHash.Count > 0)
        {
            _waitForIdle();
        }

        foreach (var pair in _pairsByContentHash.Values.Reverse())
        {
            pair.Transparent.Dispose();
            pair.Opaque.Dispose();
        }

        _pairsByContentHash.Clear();
        _entries.Clear();
    }

    private PipelinePair CreatePair(RekallAgeResolvedShaderPipeline asset)
    {
        var shaders = _factory.CreateFromSpirv(
            new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(asset.VertexSource),
                "main"),
            new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(asset.FragmentSource),
                "main"));
        try
        {
            var shaderSet = new ShaderSetDescription([_vertexLayout], shaders);
            var opaque = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                RekallAgeVeldridBlendStates.SceneCoverage,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shaderSet,
                _resourceLayouts,
                _output));
            try
            {
                var transparent = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    RekallAgeVeldridBlendStates.SceneCoverage,
                    new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
                    RasterizerStateDescription.CullNone,
                    PrimitiveTopology.TriangleList,
                    shaderSet,
                    _resourceLayouts,
                    _output));
                return new PipelinePair(asset.Key.ContentHash, opaque, transparent);
            }
            catch
            {
                opaque.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (var shader in shaders)
            {
                shader.Dispose();
            }
        }
    }

    private void TrimUnusedPairs()
    {
        if (_pairsByContentHash.Count < MaximumCachedPipelinePairs)
        {
            return;
        }

        var activeHashes = _entries.Values
            .Select(entry => entry.Pair.ContentHash)
            .ToHashSet(StringComparer.Ordinal);
        var removable = _pairsByContentHash
            .Where(item => !activeHashes.Contains(item.Key))
            .Take(Math.Max(1, _pairsByContentHash.Count - MaximumCachedPipelinePairs + 1))
            .ToArray();
        if (removable.Length == 0)
        {
            throw new InvalidOperationException(
                $"REKALL_SHADER_PIPELINE_CACHE_LIMIT: {MaximumCachedPipelinePairs} active project shader pipelines are already resident.");
        }

        _waitForIdle();
        foreach (var item in removable)
        {
            item.Value.Transparent.Dispose();
            item.Value.Opaque.Dispose();
            _pairsByContentHash.Remove(item.Key);
        }
    }

    private static RekallAgeVeldridShaderPipelineSelection Select(
        PipelinePair pair,
        bool transparent,
        bool retainedPreviousValid) =>
        new(
            transparent ? pair.Transparent : pair.Opaque,
            pair.ContentHash,
            retainedPreviousValid);

    private sealed class Entry(PipelinePair pair)
    {
        public PipelinePair Pair { get; } = pair;

        public bool Dirty { get; set; }
    }

    private sealed record PipelinePair(string ContentHash, Pipeline Opaque, Pipeline Transparent);
}
