using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rekall.Age.Modules.Hosting.Windows;

public sealed class RekallAgeAppContainerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly RekallAgeWindowsNative.SafeKernelHandle _processHandle;
    private readonly RekallAgeModuleHostJob _job;
    private readonly FileStream _standardError;
    private readonly Task<string> _standardErrorTask;
    private int _disposed;

    private RekallAgeAppContainerProcess(
        Process process,
        RekallAgeWindowsNative.SafeKernelHandle processHandle,
        RekallAgeModuleHostJob job,
        FileStream standardInput,
        FileStream standardOutput,
        FileStream standardError)
    {
        _process = process;
        _processHandle = processHandle;
        _job = job;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        _standardError = standardError;
        _standardErrorTask = DrainStandardErrorAsync(standardError);
    }

    public int ProcessId => _process.Id;

    public Stream StandardInput { get; }

    public Stream StandardOutput { get; }

    public bool AssignedToJob => true;

    public uint ActiveProcessLimit => _job.Limits.ActiveProcessLimit;

    public long ProcessMemoryLimitBytes => _job.Limits.ProcessMemoryLimitBytes;

    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;

    public static RekallAgeAppContainerProcess Start(
        RekallAgeModuleHostStagedSession staged,
        RekallAgeAppContainerProfile profile,
        RekallAgeModuleHostJobLimits limits)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_PLATFORM_UNSUPPORTED",
                "Restricted module hosting requires Windows AppContainer support.");
        }

        var security = new RekallAgeWindowsNative.SecurityAttributes
        {
            Length = Marshal.SizeOf<RekallAgeWindowsNative.SecurityAttributes>(),
            InheritHandle = 1
        };
        CreatePipePair(ref security, out var childInput, out var brokerInput, brokerReads: false);
        CreatePipePair(ref security, out var brokerOutput, out var childOutput, brokerReads: true);
        CreatePipePair(ref security, out var brokerError, out var childError, brokerReads: true);
        RekallAgeWindowsNative.SafeKernelHandle? processHandle = null;
        RekallAgeModuleHostJob? job = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr capabilitiesPointer = IntPtr.Zero;
        IntPtr handlesPointer = IntPtr.Zero;
        IntPtr environmentPointer = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        try
        {
            var attributeSize = IntPtr.Zero;
            RekallAgeWindowsNative.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeSize);
            attributeList = Marshal.AllocHGlobal(attributeSize);
            if (!RekallAgeWindowsNative.InitializeProcThreadAttributeList(attributeList, 2, 0, ref attributeSize))
            {
                throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "InitializeProcThreadAttributeList");
            }

            var capabilities = new RekallAgeWindowsNative.SecurityCapabilities
            {
                AppContainerSid = profile.SidPointer
            };
            capabilitiesPointer = Marshal.AllocHGlobal(Marshal.SizeOf<RekallAgeWindowsNative.SecurityCapabilities>());
            Marshal.StructureToPtr(capabilities, capabilitiesPointer, false);
            if (!RekallAgeWindowsNative.UpdateProcThreadAttribute(
                attributeList,
                0,
                RekallAgeWindowsNative.ProcThreadAttributeSecurityCapabilities,
                capabilitiesPointer,
                (IntPtr)Marshal.SizeOf<RekallAgeWindowsNative.SecurityCapabilities>(),
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "UpdateProcThreadAttribute(SecurityCapabilities)");
            }

            var inheritedHandles = new[]
            {
                childInput.DangerousGetHandle(),
                childOutput.DangerousGetHandle(),
                childError.DangerousGetHandle()
            };
            handlesPointer = Marshal.AllocHGlobal(IntPtr.Size * inheritedHandles.Length);
            Marshal.Copy(inheritedHandles, 0, handlesPointer, inheritedHandles.Length);
            if (!RekallAgeWindowsNative.UpdateProcThreadAttribute(
                attributeList,
                0,
                RekallAgeWindowsNative.ProcThreadAttributeHandleList,
                handlesPointer,
                (IntPtr)(IntPtr.Size * inheritedHandles.Length),
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "UpdateProcThreadAttribute(HandleList)");
            }

            var startup = new RekallAgeWindowsNative.StartupInfoEx
            {
                StartupInfo = new RekallAgeWindowsNative.StartupInfo
                {
                    Cb = Marshal.SizeOf<RekallAgeWindowsNative.StartupInfoEx>(),
                    Flags = RekallAgeWindowsNative.StartfUseStdHandles,
                    StandardInput = childInput.DangerousGetHandle(),
                    StandardOutput = childOutput.DangerousGetHandle(),
                    StandardError = childError.DangerousGetHandle()
                },
                AttributeList = attributeList
            };
            environmentPointer = CreateSanitizedEnvironment();
            var commandLine = new StringBuilder($"\"{staged.HostExecutablePath}\"");
            if (!RekallAgeWindowsNative.CreateProcess(
                staged.HostExecutablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                RekallAgeWindowsNative.ExtendedStartupInfoPresent
                    | RekallAgeWindowsNative.CreateSuspended
                    | RekallAgeWindowsNative.CreateNoWindow
                    | RekallAgeWindowsNative.CreateUnicodeEnvironment,
                environmentPointer,
                staged.Root,
                ref startup,
                out var processInformation))
            {
                throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "CreateProcess(AppContainer)");
            }

            processHandle = new RekallAgeWindowsNative.SafeKernelHandle(processInformation.Process);
            threadHandle = processInformation.Thread;
            job = new RekallAgeModuleHostJob(limits);
            try
            {
                job.Assign(processInformation.Process);
            }
            catch
            {
                RekallAgeWindowsNative.TerminateProcess(processInformation.Process, 1);
                throw;
            }

            if (RekallAgeWindowsNative.ResumeThread(threadHandle) == uint.MaxValue)
            {
                RekallAgeWindowsNative.TerminateProcess(processInformation.Process, 1);
                throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "ResumeThread");
            }

            childInput.Dispose();
            childOutput.Dispose();
            childError.Dispose();
            var process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            return new RekallAgeAppContainerProcess(
                process,
                processHandle,
                job,
                new FileStream(brokerInput, FileAccess.Write, 4096, false),
                new FileStream(brokerOutput, FileAccess.Read, 4096, false),
                new FileStream(brokerError, FileAccess.Read, 4096, false));
        }
        catch
        {
            processHandle?.Dispose();
            job?.Dispose();
            childInput.Dispose();
            brokerInput.Dispose();
            brokerOutput.Dispose();
            childOutput.Dispose();
            brokerError.Dispose();
            childError.Dispose();
            throw;
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
            {
                RekallAgeWindowsNative.CloseHandle(threadHandle);
            }

            if (attributeList != IntPtr.Zero)
            {
                RekallAgeWindowsNative.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (capabilitiesPointer != IntPtr.Zero) Marshal.FreeHGlobal(capabilitiesPointer);
            if (handlesPointer != IntPtr.Zero) Marshal.FreeHGlobal(handlesPointer);
            if (environmentPointer != IntPtr.Zero) Marshal.FreeHGlobal(environmentPointer);
            GC.KeepAlive(profile);
        }
    }

    public void CloseInput() => StandardInput.Dispose();

    public async ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        if (!RekallAgeWindowsNative.GetExitCodeProcess(_processHandle.DangerousGetHandle(), out var exitCode))
        {
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_CRASHED", "GetExitCodeProcess");
        }

        return unchecked((int)exitCode);
    }

    public async ValueTask<string> ReadBoundedStandardErrorAsync(CancellationToken cancellationToken) =>
        await _standardErrorTask.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StandardInput.Dispose();
        StandardOutput.Dispose();
        if (!_process.HasExited)
        {
            RekallAgeWindowsNative.TerminateProcess(_processHandle.DangerousGetHandle(), 1);
        }

        try
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(exitTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Closing the job below remains the authoritative kill-on-close fallback.
        }

        _job.Dispose();
        _processHandle.Dispose();
        _process.Dispose();
        try
        {
            await _standardErrorTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            _standardError.Dispose();
        }
    }

    private static void CreatePipePair(
        ref RekallAgeWindowsNative.SecurityAttributes security,
        out SafeFileHandle first,
        out SafeFileHandle second,
        bool brokerReads)
    {
        if (!RekallAgeWindowsNative.CreatePipe(out var read, out var write, ref security, 0))
        {
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "CreatePipe");
        }

        var broker = brokerReads ? read : write;
        if (!RekallAgeWindowsNative.SetHandleInformation(
            broker,
            RekallAgeWindowsNative.HandleFlagInherit,
            0))
        {
            read.Dispose();
            write.Dispose();
            throw RekallAgeWindowsNative.NativeFailure("REKALL_MODULE_HOST_APP_CONTAINER_FAILED", "SetHandleInformation");
        }

        first = read;
        second = write;
    }

    private static IntPtr CreateSanitizedEnvironment()
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var entries = new List<string>
        {
            "COMPlus_EnableDiagnostics=0",
            "DOTNET_CLI_TELEMETRY_OPTOUT=1",
            "DOTNET_EnableDiagnostics=0",
            "DOTNET_NOLOGO=1",
            $"ComSpec={Path.Combine(systemRoot, "System32", "cmd.exe")}",
            $"OS={Environment.GetEnvironmentVariable("OS") ?? "Windows_NT"}",
            $"PATH={Path.Combine(systemRoot, "System32")};{systemRoot};{Path.Combine(systemRoot, "System32", "Wbem")}",
            $"SystemRoot={systemRoot}",
            $"WINDIR={systemRoot}"
        };
        foreach (var name in new[]
        {
            "ALLUSERSPROFILE",
            "APPDATA",
            "CommonProgramFiles",
            "CommonProgramFiles(x86)",
            "CommonProgramW6432",
            "HOMEDRIVE",
            "HOMEPATH",
            "LOCALAPPDATA",
            "LOGONSERVER",
            "NUMBER_OF_PROCESSORS",
            "PATHEXT",
            "PROCESSOR_ARCHITECTURE",
            "PROCESSOR_IDENTIFIER",
            "PROCESSOR_LEVEL",
            "PROCESSOR_REVISION",
            "ProgramData",
            "ProgramFiles",
            "ProgramFiles(x86)",
            "ProgramW6432",
            "PUBLIC",
            "SESSIONNAME",
            "SystemDrive",
            "TEMP",
            "TMP",
            "USERDOMAIN",
            "USERNAME",
            "USERPROFILE"
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                entries.Add($"{name}={value}");
            }
        }

        var bytes = Encoding.Unicode.GetBytes(
            string.Join('\0', entries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) + "\0\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static async Task<string> DrainStandardErrorAsync(Stream stream)
    {
        var retained = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            var remaining = RekallAgeModuleHostProtocol.MaximumStandardErrorBytes - (int)retained.Length;
            if (remaining > 0)
            {
                await retained.WriteAsync(buffer.AsMemory(0, Math.Min(read, remaining)));
            }
        }

        return Encoding.UTF8.GetString(retained.ToArray());
    }
}
