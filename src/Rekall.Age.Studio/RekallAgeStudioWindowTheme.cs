using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Rekall.Age.Studio;

internal static class RekallAgeStudioWindowTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    internal static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Application.Current.FindResource(typeof(Window)) is Style studioWindowStyle)
        {
            window.Style = studioWindowStyle;
        }

        window.SourceInitialized += (_, _) => ApplyDarkChrome(window);
    }

    private static void ApplyDarkChrome(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = 1;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
