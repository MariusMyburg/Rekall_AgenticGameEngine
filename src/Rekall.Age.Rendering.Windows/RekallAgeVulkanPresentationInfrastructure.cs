namespace Rekall.Age.Rendering.Windows;

internal readonly record struct RekallAgeCleanupRegistration(string Name, Action Cleanup);

internal static class RekallAgeBestEffortCleanup
{
    public static void RunInReverse(
        IReadOnlyList<RekallAgeCleanupRegistration> registrations,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(log);
        List<Exception>? errors = null;
        for (var index = registrations.Count - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            try
            {
                registration.Cleanup();
            }
            catch (Exception exception)
            {
                errors ??= [];
                errors.Add(exception);
                try
                {
                    log($"Vulkan presentation cleanup issue target={registration.Name}: {exception.Message}");
                }
                catch
                {
                    // Cleanup must continue even when host diagnostics are unavailable.
                }
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more Vulkan presentation resources failed to clean up.", errors);
        }
    }
}

internal static class RekallAgeVulkanRgbaReadback
{
    public static byte[] CopyToTightlyPackedRgba(
        int width,
        int height,
        int rowPitch,
        bool bgra,
        Func<int, byte> readByte)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(readByte);
        var packedRowBytes = checked(width * 4);
        if (rowPitch < packedRowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowPitch),
                "Mapped texture row pitch cannot be smaller than one tightly packed RGBA row.");
        }

        var pixels = new byte[checked(packedRowBytes * height)];
        for (var y = 0; y < height; y++)
        {
            var sourceRowOffset = checked(y * rowPitch);
            var destinationRowOffset = checked(y * packedRowBytes);
            for (var x = 0; x < packedRowBytes; x += 4)
            {
                var sourceOffset = sourceRowOffset + x;
                var destinationOffset = destinationRowOffset + x;
                pixels[destinationOffset] = readByte(sourceOffset + (bgra ? 2 : 0));
                pixels[destinationOffset + 1] = readByte(sourceOffset + 1);
                pixels[destinationOffset + 2] = readByte(sourceOffset + (bgra ? 0 : 2));
                pixels[destinationOffset + 3] = 255;
            }
        }

        return pixels;
    }
}
