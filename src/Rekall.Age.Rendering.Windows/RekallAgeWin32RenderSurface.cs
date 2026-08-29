using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Rekall.Age.Rendering.Windows;

public sealed class RekallAgeWin32RenderSurface : IDisposable
{
    private readonly Func<IntPtr, bool> _destroyHandle;
    private int _disposed;

    public RekallAgeWin32RenderSurface(
        IntPtr hwnd,
        int width,
        int height,
        bool ownsHandle = false,
        Func<IntPtr, bool>? destroyHandle = null)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Hwnd = hwnd;
        Width = width;
        Height = height;
        OwnsHandle = ownsHandle;
        _destroyHandle = destroyHandle ?? DestroyWindow;
    }

    public IntPtr Hwnd { get; }

    public int Width { get; }

    public int Height { get; }

    public bool OwnsHandle { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public static RekallAgeWin32RenderSurface CreateExternal(IntPtr hwnd, int width, int height) =>
        new(hwnd, width, height, ownsHandle: false);

    public static RekallAgeWin32RenderSurface CreateOwned(
        IntPtr hwnd,
        int width,
        int height,
        Func<IntPtr, bool>? destroyHandle = null) =>
        new(hwnd, width, height, ownsHandle: true, destroyHandle);

    public RekallAgeWin32RenderSurface WithSize(int width, int height) =>
        new(Hwnd, width, height, OwnsHandle, _destroyHandle);

    public RekallAgeWin32RenderSurface Clone() =>
        new(Hwnd, Width, Height, OwnsHandle, _destroyHandle);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (!OwnsHandle || Hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!_destroyHandle(Hwnd))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
