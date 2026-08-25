using Rekall.Age.Rendering.Abstractions;
using Silk.NET.Vulkan;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanGpuPassTimestampSample(
    string Name,
    ulong StartTimestamp,
    ulong EndTimestamp);

/// <summary>
/// Converts Vulkan timestamp-query values and attaches them to backend-neutral frame diagnostics.
/// Native query-pool ownership is integrated by the Vulkan scene renderer; this type keeps conversion independently testable.
/// </summary>
public sealed unsafe class RekallAgeVulkanGpuProfiler : IDisposable
{
    private readonly Vk? _vk;
    private readonly Device _device;
    private readonly double _timestampPeriodNanoseconds;
    private readonly uint _timestampValidBits;
    private readonly RekallAgeVulkanGpuQueryPoolLifecycle? _lifecycle;
    private readonly NativeSlot[] _slots;
    private ulong _nextFenceToken;
    private bool _disposed;

    public RekallAgeVulkanGpuProfiler()
    {
        _slots = [];
    }

    internal RekallAgeVulkanGpuProfiler(
        Vk vk,
        PhysicalDevice physicalDevice,
        Device device,
        uint queueFamilyIndex,
        int slotCount = 2)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        _timestampPeriodNanoseconds = properties.Limits.TimestampPeriod;

        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
        if (queueFamilyIndex < queueFamilyCount)
        {
            var queueFamilies = stackalloc QueueFamilyProperties[checked((int)queueFamilyCount)];
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies);
            _timestampValidBits = queueFamilies[queueFamilyIndex].TimestampValidBits;
        }

        if (IsSupported)
        {
            _lifecycle = new RekallAgeVulkanGpuQueryPoolLifecycle(slotCount);
            _slots = Enumerable.Range(0, slotCount).Select(_ => new NativeSlot()).ToArray();
        }
        else
        {
            _slots = [];
        }
    }

    internal bool IsSupported =>
        _vk is not null
        && _device.Handle != 0
        && double.IsFinite(_timestampPeriodNanoseconds)
        && _timestampPeriodNanoseconds > 0
        && _timestampValidBits is > 0 and <= 64;

    public static RekallAgeGpuFrameTimingReport ResolveCompletedFrame(
        int frameIndex,
        double timestampPeriodNanoseconds,
        uint timestampValidBits,
        IReadOnlyList<RekallAgeVulkanGpuPassTimestampSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(timestampPeriodNanoseconds)
            || timestampPeriodNanoseconds <= 0
            || timestampValidBits is 0 or > 64
            || samples.Count == 0)
        {
            return RekallAgeGpuFrameTimingReport.Unavailable(frameIndex);
        }

        var timings = new RekallAgeGpuPassTiming[samples.Count];
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var ticks = TimestampDelta(sample.StartTimestamp, sample.EndTimestamp, timestampValidBits);
            var nanoseconds = ticks * timestampPeriodNanoseconds;
            if (!double.IsFinite(nanoseconds) || nanoseconds < 0)
            {
                return RekallAgeGpuFrameTimingReport.Unavailable(frameIndex);
            }

            timings[index] = new RekallAgeGpuPassTiming(
                sample.Name,
                nanoseconds,
                nanoseconds / 1_000_000d);
        }

        var totalTicks = TimestampDelta(samples[0].StartTimestamp, samples[^1].EndTimestamp, timestampValidBits);
        var totalNanoseconds = totalTicks * timestampPeriodNanoseconds;
        if (!double.IsFinite(totalNanoseconds) || totalNanoseconds < 0)
        {
            return RekallAgeGpuFrameTimingReport.Unavailable(frameIndex);
        }

        return new RekallAgeGpuFrameTimingReport(
            true,
            null,
            frameIndex,
            timings,
            totalNanoseconds,
            totalNanoseconds / 1_000_000d,
            "vulkan-timestamp-query");
    }

    public static IReadOnlyList<RekallAgeHighFidelityFramePassReport> AttachTimings(
        IReadOnlyList<RekallAgeHighFidelityFramePassReport> passes,
        RekallAgeGpuFrameTimingReport timings)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(timings);
        if (!timings.Available)
        {
            return passes;
        }

        var byName = timings.Passes.ToDictionary(item => item.Name, StringComparer.Ordinal);
        return passes.Select(pass => byName.TryGetValue(pass.Name, out var timing)
            ? pass with
            {
                GpuNanoseconds = timing.Nanoseconds,
                GpuMilliseconds = timing.Milliseconds
            }
            : pass).ToArray();
    }

    internal static ulong TimestampDelta(ulong start, ulong end, uint validBits)
    {
        if (validBits == 64)
        {
            return unchecked(end - start);
        }

        var mask = (1UL << checked((int)validBits)) - 1UL;
        return unchecked((end - start) & mask);
    }

    internal RekallAgeVulkanGpuFrameQuery? BeginFrame(
        int frameIndex,
        string qualitySignature,
        IReadOnlyList<string> orderedPassNames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(orderedPassNames);
        if (!IsSupported || orderedPassNames.Count == 0 || _lifecycle is null || _vk is null)
        {
            return null;
        }

        var queryCount = checked(orderedPassNames.Count * 2);
        var lease = _lifecycle.Acquire(frameIndex, queryCount);
        try
        {
            var slot = _slots[lease.SlotIndex];
            if (slot.QueryPool.Handle == 0 || slot.Capacity < queryCount)
            {
                if (slot.QueryPool.Handle != 0)
                {
                    _vk.DestroyQueryPool(_device, slot.QueryPool, null);
                }

                var createInfo = new QueryPoolCreateInfo
                {
                    SType = StructureType.QueryPoolCreateInfo,
                    QueryType = QueryType.Timestamp,
                    QueryCount = checked((uint)queryCount)
                };
                ThrowIfFailed(
                    _vk.CreateQueryPool(_device, &createInfo, null, out slot.QueryPool),
                    "vkCreateQueryPool");
                slot.Capacity = queryCount;
            }

            slot.Lease = lease;
            slot.QualitySignature = qualitySignature;
            slot.PassNames = orderedPassNames.ToArray();
            return new RekallAgeVulkanGpuFrameQuery(_vk, slot.QueryPool, lease, slot.PassNames);
        }
        catch
        {
            _lifecycle.CancelRecording(lease);
            throw;
        }
    }

    internal RekallAgeGpuFrameTimingReport ReadCompletedPriorFrame(string qualitySignature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported || _lifecycle is null || _vk is null)
        {
            return RekallAgeGpuFrameTimingReport.Unavailable(0);
        }

        foreach (var slot in _slots)
        {
            if (slot.Lease is not { } lease || !_lifecycle.CanRead(lease))
            {
                continue;
            }

            var raw = new ulong[lease.QueryCount];
            Result result;
            fixed (ulong* values = raw)
            {
                result = _vk.GetQueryPoolResults(
                    _device,
                    slot.QueryPool,
                    0,
                    checked((uint)lease.QueryCount),
                    checked((nuint)(raw.Length * sizeof(ulong))),
                    values,
                    sizeof(ulong),
                    QueryResultFlags.Result64Bit);
            }

            _lifecycle.MarkRead(lease);
            slot.Lease = null;
            if (result != Result.Success
                || !string.Equals(slot.QualitySignature, qualitySignature, StringComparison.Ordinal))
            {
                continue;
            }

            var samples = slot.PassNames.Select((name, index) =>
                new RekallAgeVulkanGpuPassTimestampSample(
                    name,
                    raw[index * 2],
                    raw[index * 2 + 1])).ToArray();
            return ResolveCompletedFrame(
                lease.FrameIndex,
                _timestampPeriodNanoseconds,
                _timestampValidBits,
                samples);
        }

        return RekallAgeGpuFrameTimingReport.Unavailable(0);
    }

    internal ulong MarkSubmitted(RekallAgeVulkanGpuFrameQuery frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifecycle is null)
        {
            return 0;
        }

        var token = ++_nextFenceToken;
        if (token == 0)
        {
            token = ++_nextFenceToken;
        }
        _lifecycle.MarkSubmitted(frame.Lease, token);
        return token;
    }

    internal void MarkFenceCompleted(ulong fenceToken)
    {
        if (fenceToken != 0)
        {
            _ = _lifecycle?.MarkFenceCompleted(fenceToken);
        }
    }

    internal void CancelRecording(RekallAgeVulkanGpuFrameQuery? frame)
    {
        if (frame is not null)
        {
            _lifecycle?.CancelRecording(frame.Lease);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_vk is not null && _device.Handle != 0)
        {
            foreach (var slot in _slots)
            {
                if (slot.QueryPool.Handle != 0)
                {
                    _vk.DestroyQueryPool(_device, slot.QueryPool, null);
                }
            }
        }

        _disposed = true;
    }

    private static void ThrowIfFailed(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{operation} failed with Vulkan result {result}.");
        }
    }

    private sealed class NativeSlot
    {
        public QueryPool QueryPool;
        public int Capacity;
        public RekallAgeVulkanGpuQueryPoolLease? Lease;
        public string QualitySignature = string.Empty;
        public IReadOnlyList<string> PassNames = Array.Empty<string>();
    }
}

internal sealed unsafe class RekallAgeVulkanGpuFrameQuery
{
    private readonly Vk _vk;
    private readonly QueryPool _queryPool;
    private readonly IReadOnlyDictionary<string, int> _indices;

    internal RekallAgeVulkanGpuFrameQuery(
        Vk vk,
        QueryPool queryPool,
        RekallAgeVulkanGpuQueryPoolLease lease,
        IReadOnlyList<string> orderedPassNames)
    {
        _vk = vk;
        _queryPool = queryPool;
        Lease = lease;
        _indices = orderedPassNames
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
    }

    internal RekallAgeVulkanGpuQueryPoolLease Lease { get; }

    internal void Reset(CommandBuffer commandBuffer) =>
        _vk.CmdResetQueryPool(commandBuffer, _queryPool, 0, checked((uint)Lease.QueryCount));

    internal void BeginPass(CommandBuffer commandBuffer, string name)
    {
        if (_indices.TryGetValue(name, out var index))
        {
            _vk.CmdWriteTimestamp(
                commandBuffer,
                PipelineStageFlags.TopOfPipeBit,
                _queryPool,
                checked((uint)(index * 2)));
        }
    }

    internal void EndPass(CommandBuffer commandBuffer, string name)
    {
        if (_indices.TryGetValue(name, out var index))
        {
            _vk.CmdWriteTimestamp(
                commandBuffer,
                PipelineStageFlags.BottomOfPipeBit,
                _queryPool,
                checked((uint)(index * 2 + 1)));
        }
    }
}

internal sealed record RekallAgeVulkanGpuQueryPoolLease(
    int SlotIndex,
    int Generation,
    int FrameIndex,
    int QueryCount);

/// <summary>
/// Small state machine used by native query-pool slots. A slot becomes resettable only after fence completion and readback.
/// </summary>
internal sealed class RekallAgeVulkanGpuQueryPoolLifecycle
{
    private readonly Slot[] _slots;

    public RekallAgeVulkanGpuQueryPoolLifecycle(int slotCount)
    {
        if (slotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        }

        _slots = Enumerable.Range(0, slotCount).Select(_ => new Slot()).ToArray();
    }

    public RekallAgeVulkanGpuQueryPoolLease Acquire(int frameIndex, int queryCount)
    {
        if (queryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queryCount));
        }

        for (var index = 0; index < _slots.Length; index++)
        {
            var slot = _slots[index];
            if (slot.State is not SlotState.Free and not SlotState.Read)
            {
                continue;
            }

            slot.Generation++;
            slot.FrameIndex = frameIndex;
            slot.QueryCount = queryCount;
            slot.FenceToken = 0;
            slot.State = SlotState.Recording;
            return new RekallAgeVulkanGpuQueryPoolLease(
                index,
                slot.Generation,
                frameIndex,
                queryCount);
        }

        throw new InvalidOperationException("No completed Vulkan timestamp query-pool slot is available for reset or reuse.");
    }

    public void MarkSubmitted(RekallAgeVulkanGpuQueryPoolLease lease, ulong fenceToken)
    {
        var slot = Require(lease, SlotState.Recording);
        if (fenceToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fenceToken));
        }

        slot.FenceToken = fenceToken;
        slot.State = SlotState.InFlight;
    }

    public bool MarkFenceCompleted(ulong fenceToken)
    {
        var slot = _slots.FirstOrDefault(item =>
            item.State == SlotState.InFlight && item.FenceToken == fenceToken);
        if (slot is null)
        {
            return false;
        }

        slot.State = SlotState.Completed;
        return true;
    }

    public bool CanRead(RekallAgeVulkanGpuQueryPoolLease lease) =>
        Matches(lease, SlotState.Completed);

    public bool CancelRecording(RekallAgeVulkanGpuQueryPoolLease lease)
    {
        if (!Matches(lease, SlotState.Recording))
        {
            return false;
        }

        _slots[lease.SlotIndex].State = SlotState.Read;
        return true;
    }

    public void MarkRead(RekallAgeVulkanGpuQueryPoolLease lease)
    {
        var slot = Require(lease, SlotState.Completed);
        slot.State = SlotState.Read;
    }

    public bool CanReset(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            return false;
        }

        return _slots[slotIndex].State is SlotState.Free or SlotState.Read;
    }

    private bool Matches(RekallAgeVulkanGpuQueryPoolLease lease, SlotState expected)
    {
        if ((uint)lease.SlotIndex >= (uint)_slots.Length)
        {
            return false;
        }

        var slot = _slots[lease.SlotIndex];
        return slot.Generation == lease.Generation
            && slot.FrameIndex == lease.FrameIndex
            && slot.QueryCount == lease.QueryCount
            && slot.State == expected;
    }

    private Slot Require(RekallAgeVulkanGpuQueryPoolLease lease, SlotState expected)
    {
        if (!Matches(lease, expected))
        {
            throw new InvalidOperationException($"Vulkan timestamp query-pool slot is not in the required {expected} state.");
        }

        return _slots[lease.SlotIndex];
    }

    private sealed class Slot
    {
        public SlotState State;
        public int Generation;
        public int FrameIndex;
        public int QueryCount;
        public ulong FenceToken;
    }

    private enum SlotState
    {
        Free,
        Recording,
        InFlight,
        Completed,
        Read
    }
}
