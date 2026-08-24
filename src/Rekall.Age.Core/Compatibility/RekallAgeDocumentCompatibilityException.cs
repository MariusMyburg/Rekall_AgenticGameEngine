using Rekall.Age.Core.Commands;

namespace Rekall.Age.Core.Compatibility;

public sealed class RekallAgeDocumentCompatibilityException : RekallAgeCodedBoundaryException
{
    public RekallAgeDocumentCompatibilityException(
        string code,
        string documentKind,
        string documentPath,
        int? detectedVersion,
        int currentVersion,
        string message,
        Exception? innerException = null)
        : base(code, message, NormalizeDocumentPath(documentPath), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        DocumentKind = documentKind;
        DocumentPath = NormalizeDocumentPath(documentPath);
        DetectedVersion = detectedVersion;
        CurrentVersion = currentVersion;
    }

    public string DocumentKind { get; }

    public string DocumentPath { get; }

    public int? DetectedVersion { get; }

    public int CurrentVersion { get; }

    private static string NormalizeDocumentPath(string documentPath) =>
        Path.IsPathRooted(documentPath) ? Path.GetFullPath(documentPath) : documentPath;
}
