using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Rekall.Age.Rendering.Windows;

public readonly record struct RekallAgeWin32RenderSurfaceDescriptor
{
    public RekallAgeWin32RenderSurfaceDescriptor(IntPtr hwnd, int width, int height)
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
    }

    public IntPtr Hwnd { get; }

    public int Width { get; }

    public int Height { get; }
}

public sealed class RekallAgeWin32RenderSurface : IDisposable
{
    private readonly Func<IntPtr, bool> _destroyHandle;
    private int _disposed;

    private RekallAgeWin32RenderSurface(
        IntPtr hwnd,
        bool ownsHandle = false,
        Func<IntPtr, bool>? destroyHandle = null)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd));
        }

        Hwnd = hwnd;
        OwnsHandle = ownsHandle;
        _destroyHandle = destroyHandle ?? DestroyWindow;
    }

    public IntPtr Hwnd { get; }

    public bool OwnsHandle { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public static RekallAgeWin32RenderSurface CreateExternal(
        IntPtr hwnd,
        Func<IntPtr, bool>? destroyHandle = null) =>
        new(hwnd, ownsHandle: false, destroyHandle);

    public static RekallAgeWin32RenderSurface CreateOwned(
        IntPtr hwnd,
        Func<IntPtr, bool>? destroyHandle = null) =>
        new(hwnd, ownsHandle: true, destroyHandle);

    public RekallAgeWin32RenderSurfaceDescriptor Describe(int width, int height)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new RekallAgeWin32RenderSurfaceDescriptor(Hwnd, width, height);
    }

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
