using System.Globalization;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanRenderPlanExecutor
{
    private readonly IRekallAgeVulkanRenderPassCapture _capture;

    public RekallAgeVulkanRenderPlanExecutor()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public RekallAgeVulkanRenderPlanExecutor(IRekallAgeVulkanRenderPassCapture capture)
    {
        _capture = capture;
    }

    public async ValueTask<RekallAgeRenderPlanExecutionResult> ExecuteAsync(
        RekallAgeRenderPlanDocument plan,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!plan.BackendId.Equals("vulkan", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Render backend '{plan.BackendId}' cannot be executed by the Vulkan render plan executor.");
        }

        var commands = plan.CommandBuffers
            .OrderBy(buffer => buffer.Id, StringComparer.Ordinal)
            .SelectMany(buffer => buffer.Commands)
            .ToArray();
        var begin = commands.FirstOrDefault(command => Is(command, "begin-render-pass"))
            ?? throw new InvalidOperationException("Vulkan render plan execution requires a begin-render-pass command.");
        if (!begin.Arguments.TryGetValue("target", out var targetId) || string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException("Vulkan begin-render-pass command requires a target argument.");
        }

        var target = plan.Resources.FirstOrDefault(resource => resource.Id.Equals(targetId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Vulkan render target '{targetId}' is missing from the render plan.");
        if (!target.Kind.Equals("image", StringComparison.OrdinalIgnoreCase)
            || !target.Usage.Any(usage => usage.Equals("color-attachment", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Vulkan render target '{targetId}' must be an image with color-attachment usage.");
        }

        var unsupported = commands.FirstOrDefault(command =>
            !Is(command, "begin-render-pass")
            && !Is(command, "end-render-pass")
            && !Is(command, "clear"));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"Vulkan render plan execution supports clear render passes in this build; command '{unsupported.Op}' is not executable yet.");
        }

        var clearCommand = commands.FirstOrDefault(command => Is(command, "clear"));
        var clearColor = RekallAgeVulkanClearColor.Normalize(clearCommand is null
            ? null
            : ParseClearColor(clearCommand.Arguments));
        var width = ParseUInt(begin.Arguments.GetValueOrDefault("width"), 64);
        var height = ParseUInt(begin.Arguments.GetValueOrDefault("height"), 64);
        var preferredDeviceType = begin.Arguments.GetValueOrDefault("preferredDeviceType");
        var capture = await _capture.CaptureClearRenderPassAsync(
            width,
            height,
            target.Format,
            string.IsNullOrWhiteSpace(preferredDeviceType) ? "discrete-gpu" : preferredDeviceType,
            outputDirectory,
            clearColor,
            cancellationToken);
        if (!capture.Captured)
        {
            throw new InvalidOperationException(
                $"Vulkan render plan capture failed: {string.Join(" ", capture.Errors)}".Trim());
        }

        return new RekallAgeRenderPlanExecutionResult(
            capture.OutputPath,
            capture.NonZeroBytes > 0,
            checked((int)capture.Width),
            checked((int)capture.Height));
    }

    private static bool Is(RekallAgeRenderCommand command, string op)
    {
        return command.Op.Equals(op, StringComparison.OrdinalIgnoreCase);
    }

    private static uint ParseUInt(string? value, uint defaultValue)
    {
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static RekallAgeVulkanClearColor ParseClearColor(IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("color", out var hex))
        {
            return ParseHexColor(hex);
        }

        return new RekallAgeVulkanClearColor(
            ParseFloat(arguments.GetValueOrDefault("r"), RekallAgeVulkanClearColor.Default.R),
            ParseFloat(arguments.GetValueOrDefault("g"), RekallAgeVulkanClearColor.Default.G),
            ParseFloat(arguments.GetValueOrDefault("b"), RekallAgeVulkanClearColor.Default.B),
            ParseFloat(arguments.GetValueOrDefault("a"), RekallAgeVulkanClearColor.Default.A));
    }

    private static RekallAgeVulkanClearColor ParseHexColor(string value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return RekallAgeVulkanClearColor.Default;
        }

        return byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new RekallAgeVulkanClearColor(r / 255f, g / 255f, b / 255f, 1f)
            : RekallAgeVulkanClearColor.Default;
    }

    private static float ParseFloat(string? value, float defaultValue)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }
}
