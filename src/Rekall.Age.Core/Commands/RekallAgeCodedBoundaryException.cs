namespace Rekall.Age.Core.Commands;

public abstract class RekallAgeCodedBoundaryException : Exception
{
    protected RekallAgeCodedBoundaryException(string code, string message, string target)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string Target { get; }
}
