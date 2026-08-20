using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Hosting;

public sealed class RekallAgeModuleHostException : RekallAgeCodedBoundaryException
{
    public RekallAgeModuleHostException(
        string code,
        string message,
        string target = "module-host-protocol",
        Exception? innerException = null)
        : base(code, message, target, innerException)
    {
    }
}
