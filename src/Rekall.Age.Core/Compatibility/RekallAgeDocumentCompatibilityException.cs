namespace Rekall.Age.Core.Compatibility;

public sealed class RekallAgeDocumentCompatibilityException : Exception
{
    public RekallAgeDocumentCompatibilityException(
        string code,
        string documentKind,
        string documentPath,
        int? detectedVersion,
        int currentVersion,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        Code = code;
        DocumentKind = documentKind;
        DocumentPath = Path.GetFullPath(documentPath);
        DetectedVersion = detectedVersion;
        CurrentVersion = currentVersion;
    }

    public string Code { get; }

    public string DocumentKind { get; }

    public string DocumentPath { get; }

    public int? DetectedVersion { get; }

    public int CurrentVersion { get; }
}
