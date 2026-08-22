using System.Collections.ObjectModel;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeInMemoryRenderingDevice : IRekallAgeRenderingDevice
{
    private readonly object _gate = new();
    private readonly List<ResourceSlot> _slots = [];
    private bool _disposed;

    public RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities capabilities)
    {
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        DeviceId = Guid.NewGuid();
    }

    public Guid DeviceId { get; }

    public RekallAgeRenderingDeviceCapabilities Capabilities { get; }

    public int SubmissionCount { get; private set; }

    public RekallAgeGraphicsResourceCreationResult CreateBuffer(RekallAgeBufferDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        var validation = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities);
        return validation.Valid
            ? Create(RekallAgeGraphicsResourceKind.Buffer, descriptor, descriptor.Label, descriptor.SizeBytes)
            : new(default, validation.Diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateTexture(RekallAgeTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        var validation = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities);
        return validation.Valid
            ? Create(RekallAgeGraphicsResourceKind.Texture, descriptor, descriptor.Label, EstimateTextureBytes(descriptor))
            : new(default, validation.Diagnostics);
    }

    public RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var diagnostics = ValidateHandleLocked(handle, expectedKind: null);
            if (diagnostics.Count > 0)
            {
                return new(diagnostics);
            }

            _slots[handle.Slot] = _slots[handle.Slot] with { Resource = null };
            return new([]);
        }
    }

    public IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CommandEncoder(this, label);
    }

    public RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commandBuffer);
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (!commandBuffer.Finished)
        {
            diagnostics.Add(new("REKALL_GPU_COMMAND_BUFFER_UNFINISHED", "Only finished immutable command buffers can be submitted.", commandBuffer.Label));
        }
        if (commandBuffer.DeviceId != DeviceId)
        {
            diagnostics.Add(new("REKALL_GPU_HANDLE_FOREIGN", "Command buffer belongs to another rendering device.", commandBuffer.Label));
        }

        lock (_gate)
        {
            foreach (var copy in commandBuffer.Commands.OfType<RekallAgeCopyBufferCommand>())
            {
                diagnostics.AddRange(ValidateCopyLocked(copy.Source, copy.SourceOffset, copy.Destination, copy.DestinationOffset, copy.SizeBytes));
            }
            if (diagnostics.Count == 0)
            {
                SubmissionCount++;
            }
        }
        return new(diagnostics);
    }

    public IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            return _slots
                .Where(slot => slot.Resource is not null)
                .Select(slot => slot.Resource!)
                .OrderBy(resource => resource.Handle.Slot)
                .Select(resource => new RekallAgeGraphicsResourceInspection(
                    resource.Handle,
                    resource.Label,
                    resource.EstimatedBytes,
                    resource.Descriptor))
                .ToArray();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _slots.Clear();
            _disposed = true;
        }
    }

    private RekallAgeGraphicsResourceCreationResult Create(
        RekallAgeGraphicsResourceKind kind,
        object descriptor,
        string? label,
        ulong estimatedBytes)
    {
        lock (_gate)
        {
            var slotIndex = _slots.FindIndex(slot => slot.Resource is null);
            if (slotIndex < 0)
            {
                slotIndex = _slots.Count;
                _slots.Add(new ResourceSlot(0, null));
            }
            var generation = checked(_slots[slotIndex].Generation + 1);
            var handle = new RekallAgeGraphicsResourceHandle(DeviceId, kind, slotIndex, generation);
            _slots[slotIndex] = new(generation, new Resource(handle, descriptor, label, estimatedBytes));
            return new(handle, []);
        }
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateCopy(
        RekallAgeGraphicsResourceHandle source,
        ulong sourceOffset,
        RekallAgeGraphicsResourceHandle destination,
        ulong destinationOffset,
        ulong sizeBytes)
    {
        lock (_gate)
        {
            return ValidateCopyLocked(source, sourceOffset, destination, destinationOffset, sizeBytes);
        }
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateCopyLocked(
        RekallAgeGraphicsResourceHandle source,
        ulong sourceOffset,
        RekallAgeGraphicsResourceHandle destination,
        ulong destinationOffset,
        ulong sizeBytes)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        diagnostics.AddRange(ValidateHandleLocked(source, RekallAgeGraphicsResourceKind.Buffer));
        diagnostics.AddRange(ValidateHandleLocked(destination, RekallAgeGraphicsResourceKind.Buffer));
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }

        var sourceDescriptor = (RekallAgeBufferDescriptor)_slots[source.Slot].Resource!.Descriptor;
        var destinationDescriptor = (RekallAgeBufferDescriptor)_slots[destination.Slot].Resource!.Descriptor;
        if (!sourceDescriptor.Usage.HasFlag(RekallAgeBufferUsage.CopySource)
            || !destinationDescriptor.Usage.HasFlag(RekallAgeBufferUsage.TransferDestination))
        {
            diagnostics.Add(new("REKALL_GPU_COPY_USAGE_INVALID", "Buffer copy requires CopySource and TransferDestination usage.", null));
        }
        if (sizeBytes == 0
            || sourceOffset > sourceDescriptor.SizeBytes
            || sizeBytes > sourceDescriptor.SizeBytes - sourceOffset
            || destinationOffset > destinationDescriptor.SizeBytes
            || sizeBytes > destinationDescriptor.SizeBytes - destinationOffset)
        {
            diagnostics.Add(new("REKALL_GPU_COPY_RANGE_INVALID", "Buffer copy range is empty or outside a resource.", null));
        }
        return diagnostics;
    }

    private List<RekallAgeGraphicsDiagnostic> ValidateHandleLocked(
        RekallAgeGraphicsResourceHandle handle,
        RekallAgeGraphicsResourceKind? expectedKind)
    {
        if (!handle.IsValid || handle.DeviceId != DeviceId)
        {
            return [new("REKALL_GPU_HANDLE_FOREIGN", "Resource handle is invalid or belongs to another rendering device.", handle.ToString())];
        }
        if (expectedKind.HasValue && handle.Kind != expectedKind.Value)
        {
            return [new("REKALL_GPU_HANDLE_KIND_INVALID", $"Expected {expectedKind.Value} but received {handle.Kind}.", handle.ToString())];
        }
        if (handle.Slot >= _slots.Count
            || _slots[handle.Slot].Generation != handle.Generation
            || _slots[handle.Slot].Resource is null)
        {
            return [new("REKALL_GPU_HANDLE_STALE", "Resource handle is stale or has been destroyed.", handle.ToString())];
        }
        return [];
    }

    private static ulong EstimateTextureBytes(RekallAgeTextureDescriptor descriptor)
    {
        var bytesPerPixel = descriptor.Format is RekallAgeTextureFormat.Rgba16Float ? 8UL : 4UL;
        return checked((ulong)descriptor.Width * (ulong)descriptor.Height * (ulong)descriptor.Depth
            * (ulong)descriptor.ArrayLayers * bytesPerPixel);
    }

    private sealed class CommandEncoder : IRekallAgeGraphicsCommandEncoder
    {
        private readonly RekallAgeInMemoryRenderingDevice _device;
        private readonly string? _label;
        private readonly List<RekallAgeGraphicsCommand> _commands = [];
        private bool _finished;
        private bool _disposed;

        public CommandEncoder(RekallAgeInMemoryRenderingDevice device, string? label)
        {
            _device = device;
            _label = label;
        }

        public RekallAgeGraphicsValidationResult CopyBuffer(
            RekallAgeGraphicsResourceHandle source,
            ulong sourceOffset,
            RekallAgeGraphicsResourceHandle destination,
            ulong destinationOffset,
            ulong sizeBytes)
        {
            EnsureWritable();
            var diagnostics = _device.ValidateCopy(source, sourceOffset, destination, destinationOffset, sizeBytes);
            if (diagnostics.Count == 0)
            {
                _commands.Add(new RekallAgeCopyBufferCommand(source, sourceOffset, destination, destinationOffset, sizeBytes));
            }
            return new(diagnostics);
        }

        public RekallAgeGraphicsCommandBuffer Finish()
        {
            EnsureWritable();
            _finished = true;
            return new(
                _device.DeviceId,
                _label,
                new ReadOnlyCollection<RekallAgeGraphicsCommand>(_commands.ToArray()));
        }

        public void Dispose() => _disposed = true;

        private void EnsureWritable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_finished)
            {
                throw new InvalidOperationException("Command encoder has already been finished.");
            }
        }
    }

    private sealed record Resource(
        RekallAgeGraphicsResourceHandle Handle,
        object Descriptor,
        string? Label,
        ulong EstimatedBytes);

    private sealed record ResourceSlot(uint Generation, Resource? Resource);
}
