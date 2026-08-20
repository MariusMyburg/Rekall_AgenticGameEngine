using System.ComponentModel;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Rekall.Age.Modules.Hosting.Windows;

[SupportedOSPlatform("windows")]
public sealed class RekallAgeAppContainerProfile : IDisposable
{
    private const string ProfileName = "RekallAGE.ModuleHost.Restricted.v1";
    private readonly RekallAgeWindowsNative.SafeSidHandle _sid;

    private RekallAgeAppContainerProfile(RekallAgeWindowsNative.SafeSidHandle sid)
    {
        _sid = sid;
        Sid = new SecurityIdentifier(sid.DangerousGetHandle()).Value;
    }

    public string Sid { get; }

    public IReadOnlyList<string> Capabilities { get; } = Array.Empty<string>();

    internal IntPtr SidPointer => _sid.DangerousGetHandle();

    public static RekallAgeAppContainerProfile OpenOrCreate()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_PLATFORM_UNSUPPORTED",
                "Restricted module hosting requires Windows AppContainer support.");
        }

        var result = RekallAgeWindowsNative.CreateAppContainerProfile(
            ProfileName,
            "Rekall AGE Restricted Module Host",
            "No-capability execution profile for agent-authored Rekall AGE modules.",
            IntPtr.Zero,
            0,
            out var sid);
        if (result < 0)
        {
            var win32 = result & 0xffff;
            if (win32 != RekallAgeWindowsNative.ErrorAlreadyExists)
            {
                throw new RekallAgeModuleHostException(
                    "REKALL_MODULE_HOST_APP_CONTAINER_FAILED",
                    $"CreateAppContainerProfile failed with HRESULT 0x{result:X8}.",
                    ProfileName);
            }

            result = RekallAgeWindowsNative.DeriveAppContainerSidFromAppContainerName(ProfileName, out sid);
            if (result < 0)
            {
                throw new RekallAgeModuleHostException(
                    "REKALL_MODULE_HOST_APP_CONTAINER_FAILED",
                    $"DeriveAppContainerSidFromAppContainerName failed with HRESULT 0x{result:X8}.",
                    ProfileName);
            }
        }

        return new RekallAgeAppContainerProfile(new RekallAgeWindowsNative.SafeSidHandle(sid));
    }

    public void GrantReadExecute(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(fullRoot);
        }

        Grant(fullRoot, RekallAgeWindowsNative.SubContainersAndObjectsInherit);
        foreach (var directory in Directory.EnumerateDirectories(fullRoot, "*", SearchOption.AllDirectories))
        {
            Grant(directory, RekallAgeWindowsNative.SubContainersAndObjectsInherit);
        }

        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            Grant(file, 0);
        }
    }

    public void Dispose() => _sid.Dispose();

    private void Grant(string path, uint inheritance)
    {
        var result = RekallAgeWindowsNative.GetNamedSecurityInfo(
            path,
            RekallAgeWindowsNative.SeFileObject,
            RekallAgeWindowsNative.DaclSecurityInformation,
            out _,
            out _,
            out var oldAcl,
            out _,
            out var descriptor);
        if (result != 0)
        {
            throw AccessFailure("GetNamedSecurityInfo", path, result);
        }

        IntPtr newAcl = IntPtr.Zero;
        try
        {
            var access = new RekallAgeWindowsNative.ExplicitAccess
            {
                AccessPermissions = RekallAgeWindowsNative.FileGenericReadExecute,
                AccessMode = RekallAgeWindowsNative.GrantAccess,
                Inheritance = inheritance,
                Trustee = new RekallAgeWindowsNative.Trustee
                {
                    TrusteeForm = RekallAgeWindowsNative.TrusteeIsSid,
                    TrusteeType = RekallAgeWindowsNative.TrusteeIsUser,
                    Name = SidPointer
                }
            };
            result = RekallAgeWindowsNative.SetEntriesInAcl(1, [access], oldAcl, out newAcl);
            if (result != 0)
            {
                throw AccessFailure("SetEntriesInAcl", path, result);
            }

            result = RekallAgeWindowsNative.SetNamedSecurityInfo(
                path,
                RekallAgeWindowsNative.SeFileObject,
                RekallAgeWindowsNative.DaclSecurityInformation,
                IntPtr.Zero,
                IntPtr.Zero,
                newAcl,
                IntPtr.Zero);
            if (result != 0)
            {
                throw AccessFailure("SetNamedSecurityInfo", path, result);
            }
        }
        finally
        {
            if (newAcl != IntPtr.Zero)
            {
                RekallAgeWindowsNative.LocalFree(newAcl);
            }

            if (descriptor != IntPtr.Zero)
            {
                RekallAgeWindowsNative.LocalFree(descriptor);
            }

            GC.KeepAlive(_sid);
        }
    }

    private static RekallAgeModuleHostException AccessFailure(string operation, string path, uint error) => new(
        "REKALL_MODULE_HOST_APP_CONTAINER_FAILED",
        $"{operation} failed with Windows error {error}.",
        path,
        new Win32Exception((int)error));
}
