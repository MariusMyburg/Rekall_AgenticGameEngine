using System.Runtime.InteropServices;

namespace Rekall.Age.Modules.Hosting.Windows;

public sealed record RekallAgeModuleHostJobLimits(
    uint ActiveProcessLimit,
    long ProcessMemoryLimitBytes,
    long JobMemoryLimitBytes)
{
    public static RekallAgeModuleHostJobLimits RestrictedDefault { get; } = new(
        1,
        512L * 1024 * 1024,
        512L * 1024 * 1024);
}

internal sealed class RekallAgeModuleHostJob : IDisposable
{
    private readonly RekallAgeWindowsNative.SafeKernelHandle _handle;

    internal RekallAgeModuleHostJob(RekallAgeModuleHostJobLimits limits)
    {
        if (limits.ActiveProcessLimit < 1 || limits.ProcessMemoryLimitBytes < 1 || limits.JobMemoryLimitBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }

        Limits = limits;
        _handle = RekallAgeWindowsNative.CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_JOB_LIMIT_FAILED", "CreateJobObject");
        }

        var information = new RekallAgeWindowsNative.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new RekallAgeWindowsNative.JobObjectBasicLimitInformation
            {
                LimitFlags = RekallAgeWindowsNative.JobObjectLimitActiveProcess
                    | RekallAgeWindowsNative.JobObjectLimitDieOnUnhandledException
                    | RekallAgeWindowsNative.JobObjectLimitKillOnJobClose
                    | RekallAgeWindowsNative.JobObjectLimitProcessMemory
                    | RekallAgeWindowsNative.JobObjectLimitJobMemory,
                ActiveProcessLimit = limits.ActiveProcessLimit
            },
            ProcessMemoryLimit = (UIntPtr)checked((ulong)limits.ProcessMemoryLimitBytes),
            JobMemoryLimit = (UIntPtr)checked((ulong)limits.JobMemoryLimitBytes)
        };
        if (!RekallAgeWindowsNative.SetInformationJobObject(
            _handle,
            9,
            ref information,
            Marshal.SizeOf<RekallAgeWindowsNative.JobObjectExtendedLimitInformation>()))
        {
            _handle.Dispose();
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_JOB_LIMIT_FAILED", "SetInformationJobObject");
        }
    }

    internal RekallAgeModuleHostJobLimits Limits { get; }

    internal void Assign(IntPtr process)
    {
        if (!RekallAgeWindowsNative.AssignProcessToJobObject(_handle, process))
        {
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_JOB_LIMIT_FAILED", "AssignProcessToJobObject");
        }
    }

    public void Dispose() => _handle.Dispose();
}
