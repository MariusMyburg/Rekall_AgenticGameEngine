using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public interface IRekallAgeOpenXrRuntimeProbe
{
    ValueTask<RekallAgeOpenXrProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

public sealed record RekallAgeOpenXrProbeResult(
    bool LoaderAvailable,
    bool RuntimeAvailable,
    string? LoaderName,
    IReadOnlyList<RekallAgeOpenXrExtensionInfo> InstanceExtensions,
    bool VulkanEnable2Available,
    bool PrimaryStereoReady,
    IReadOnlyList<string> Errors);

public sealed record RekallAgeOpenXrExtensionInfo(
    string Name,
    uint Version);

public static class RekallAgeOpenXrLoaderCandidateNames
{
    public static IReadOnlyList<string> ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["openxr_loader", "openxr_loader.dll"];
        }

        if (OperatingSystem.IsLinux())
        {
            return ["libopenxr_loader.so.1", "libopenxr_loader.so"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["libopenxr_loader.dylib", "openxr_loader"];
        }

        return ["openxr_loader"];
    }
}

public sealed class RekallAgeNativeOpenXrRuntimeProbe : IRekallAgeOpenXrRuntimeProbe
{
    private const int XrSuccess = 0;
    private const int XrTypeExtensionProperties = 2;
    private const int XrMaxExtensionNameSize = 128;

    public ValueTask<RekallAgeOpenXrProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (!TryLoadOpenXrLoader(out var handle, out var loaderName))
        {
            return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                false,
                false,
                null,
                [],
                false,
                false,
                ["OpenXR loader was not found. Install or select an OpenXR runtime such as SteamVR, Meta, Varjo, or Windows Mixed Reality."]));
        }

        try
        {
            if (!NativeLibrary.TryGetExport(handle, "xrEnumerateInstanceExtensionProperties", out var symbol))
            {
                return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                    true,
                    false,
                    loaderName,
                    [],
                    false,
                    false,
                    ["OpenXR loader does not export xrEnumerateInstanceExtensionProperties."]));
            }

            var enumerate = Marshal.GetDelegateForFunctionPointer<XrEnumerateInstanceExtensionPropertiesDelegate>(symbol);
            var countResult = enumerate(null, 0, out var count, null);
            if (countResult != XrSuccess)
            {
                return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                    true,
                    false,
                    loaderName,
                    [],
                    false,
                    false,
                    [$"xrEnumerateInstanceExtensionProperties failed with XrResult {countResult}."]));
            }

            var nativeProperties = new XrExtensionProperties[count];
            for (var i = 0; i < nativeProperties.Length; i++)
            {
                nativeProperties[i].Type = XrTypeExtensionProperties;
                nativeProperties[i].Next = IntPtr.Zero;
                nativeProperties[i].ExtensionName = new byte[XrMaxExtensionNameSize];
            }

            var propertiesResult = enumerate(null, count, out var written, nativeProperties);
            if (propertiesResult != XrSuccess)
            {
                return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                    true,
                    false,
                    loaderName,
                    [],
                    false,
                    false,
                    [$"OpenXR extension enumeration failed with XrResult {propertiesResult}."]));
            }

            var extensions = nativeProperties
                .Take(checked((int)Math.Min(count, written)))
                .Select(property => new RekallAgeOpenXrExtensionInfo(
                    ReadNullTerminatedAscii(property.ExtensionName),
                    property.ExtensionVersion))
                .Where(extension => !string.IsNullOrWhiteSpace(extension.Name))
                .OrderBy(extension => extension.Name, StringComparer.Ordinal)
                .ToArray();
            var vulkanEnable2 = extensions.Any(extension =>
                extension.Name.Equals("XR_KHR_vulkan_enable2", StringComparison.Ordinal));
            return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                true,
                true,
                loaderName,
                extensions,
                vulkanEnable2,
                vulkanEnable2,
                errors));
        }
        catch (Exception ex) when (ex is AccessViolationException or SEHException or MarshalDirectiveException or DllNotFoundException)
        {
            return ValueTask.FromResult(new RekallAgeOpenXrProbeResult(
                true,
                false,
                loaderName,
                [],
                false,
                false,
                [$"OpenXR runtime probe failed: {ex.Message}"]));
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    private static bool TryLoadOpenXrLoader(out IntPtr handle, out string? loaderName)
    {
        foreach (var candidate in RekallAgeOpenXrLoaderCandidateNames.ForCurrentPlatform())
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                loaderName = candidate;
                return true;
            }
        }

        handle = IntPtr.Zero;
        loaderName = null;
        return false;
    }

    private static string ReadNullTerminatedAscii(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEnumerateInstanceExtensionPropertiesDelegate(
        string? layerName,
        uint propertyCapacityInput,
        out uint propertyCountOutput,
        [In, Out] XrExtensionProperties[]? properties);

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtensionProperties
    {
        public int Type;

        public IntPtr Next;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = XrMaxExtensionNameSize)]
        public byte[] ExtensionName;

        public uint ExtensionVersion;
    }
}
