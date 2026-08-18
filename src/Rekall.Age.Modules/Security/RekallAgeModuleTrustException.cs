using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Security;

public sealed class RekallAgeModuleTrustException : RekallAgeCodedBoundaryException
{
    public RekallAgeModuleTrustException(string code, string message, string target)
        : base(code, message, target)
    {
    }
}
