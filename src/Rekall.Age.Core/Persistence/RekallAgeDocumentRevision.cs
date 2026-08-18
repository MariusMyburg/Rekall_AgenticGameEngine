using System.Security.Cryptography;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Core.Persistence;

public static class RekallAgeDocumentRevision
{
    public const string Missing = "missing";

    public static string Compute(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static bool IsValid(string revision) =>
        revision.Equals(Missing, StringComparison.Ordinal) ||
        (revision.Length == 64 && revision.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'));
}

public sealed class RekallAgeDocumentRevisionException : RekallAgeCodedBoundaryException
{
    public RekallAgeDocumentRevisionException(
        string code,
        string path,
        string message,
        string expectedRevision,
        string currentRevision)
        : base(code, message, path)
    {
        ExpectedRevision = expectedRevision;
        CurrentRevision = currentRevision;
    }

    public string ExpectedRevision { get; }

    public string CurrentRevision { get; }
}
