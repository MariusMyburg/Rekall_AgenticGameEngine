namespace Rekall.Age.Rendering;

public sealed record RekallAgeRenderPlanDocument(
    string Name,
    string BackendId,
    IReadOnlyList<RekallAgeRenderResourceDescriptor> Resources,
    IReadOnlyList<RekallAgeRenderPipelineDescriptor> Pipelines,
    IReadOnlyList<RekallAgeRenderCommandBuffer> CommandBuffers)
{
    public static RekallAgeRenderPlanDocument Create(string backendId, string name)
    {
        if (string.IsNullOrWhiteSpace(backendId))
        {
            throw new ArgumentException("Render backend id is required.", nameof(backendId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Render plan name is required.", nameof(name));
        }

        return new RekallAgeRenderPlanDocument(
            name.Trim(),
            backendId.Trim().ToLowerInvariant(),
            Array.Empty<RekallAgeRenderResourceDescriptor>(),
            Array.Empty<RekallAgeRenderPipelineDescriptor>(),
            Array.Empty<RekallAgeRenderCommandBuffer>());
    }

    public RekallAgeRenderPlanDocument AddResource(RekallAgeRenderResourceDescriptor resource)
    {
        return this with
        {
            Resources = Resources
                .Where(existing => !existing.Id.Equals(resource.Id, StringComparison.Ordinal))
                .Append(resource)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public RekallAgeRenderPlanDocument AddPipeline(RekallAgeRenderPipelineDescriptor pipeline)
    {
        return this with
        {
            Pipelines = Pipelines
                .Where(existing => !existing.Id.Equals(pipeline.Id, StringComparison.Ordinal))
                .Append(pipeline)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public RekallAgeRenderPlanDocument RecordCommandBuffer(RekallAgeRenderCommandBuffer commandBuffer)
    {
        return this with
        {
            CommandBuffers = CommandBuffers
                .Where(existing => !existing.Id.Equals(commandBuffer.Id, StringComparison.Ordinal))
                .Append(commandBuffer)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }
}

public sealed record RekallAgeRenderResourceDescriptor(
    string Id,
    string Kind,
    string Format,
    IReadOnlyList<string> Usage)
{
    public static RekallAgeRenderResourceDescriptor Create(
        string id,
        string kind,
        string format,
        IEnumerable<string> usage)
    {
        return new RekallAgeRenderResourceDescriptor(
            Require(id, "Render resource id is required."),
            Require(kind, "Render resource kind is required."),
            Require(format, "Render resource format is required."),
            usage
                .Select(item => item.Trim().ToLowerInvariant())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
    }

    private static string Require(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value.Trim();
    }
}

public sealed record RekallAgeRenderPipelineDescriptor(
    string Id,
    string BindPoint,
    string Layout,
    IReadOnlyList<string> Shaders);

public sealed record RekallAgeRenderCommandBuffer(
    string Id,
    string Queue,
    IReadOnlyList<RekallAgeRenderCommand> Commands)
{
    public static RekallAgeRenderCommandBuffer Create(
        string id,
        string queue,
        IReadOnlyList<RekallAgeRenderCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Command buffer id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new ArgumentException("Command buffer queue is required.", nameof(queue));
        }

        return new RekallAgeRenderCommandBuffer(
            id.Trim(),
            queue.Trim().ToLowerInvariant(),
            commands.ToArray());
    }
}

public sealed record RekallAgeRenderCommand(
    string Op,
    string Label,
    IReadOnlyDictionary<string, string> Arguments);
