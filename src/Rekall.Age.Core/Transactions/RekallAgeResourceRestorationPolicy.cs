using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Core.Transactions;

public interface IRekallAgeAppendOnlyResourceClassifier
{
    bool IsAppendOnly(string projectRoot, string confinedResourcePath);
}

public interface IRekallAgeResourceRestorationPolicy
{
    string ResolveRestorablePath(string projectRoot, string resourcePath);
}

public sealed class RekallAgeResourceRestorationException : RekallAgeCodedBoundaryException
{
    public const string ProtectedCode = "REKALL_RESOURCE_RESTORE_PROTECTED";
    public const string PathInvalidCode = "REKALL_RESOURCE_RESTORE_PATH_INVALID";

    public RekallAgeResourceRestorationException(
        string code,
        string message,
        string target,
        Exception? innerException = null)
        : base(code, message, target, innerException)
    {
    }
}

public sealed class RekallAgeResourceRestorationPolicy(
    params IRekallAgeAppendOnlyResourceClassifier[] appendOnlyClassifiers)
    : IRekallAgeResourceRestorationPolicy
{
    private readonly IReadOnlyList<IRekallAgeAppendOnlyResourceClassifier> _appendOnlyClassifiers =
        appendOnlyClassifiers?.ToArray()
        ?? throw new ArgumentNullException(nameof(appendOnlyClassifiers));

    public string ResolveRestorablePath(string projectRoot, string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        try
        {
            var root = Path.GetFullPath(projectRoot);
            var candidate = Path.IsPathRooted(resourcePath)
                ? resourcePath
                : Path.Combine(root, resourcePath);
            var confined = RekallAgeConfinedPath.Resolve(root, candidate, "Transaction restoration target");
            if (_appendOnlyClassifiers.Any(classifier => classifier.IsAppendOnly(root, confined)))
            {
                throw new RekallAgeResourceRestorationException(
                    RekallAgeResourceRestorationException.ProtectedCode,
                    $"Resource '{resourcePath}' is append-only and cannot be deleted or overwritten by transaction restoration.",
                    confined);
            }

            return confined;
        }
        catch (RekallAgeResourceRestorationException)
        {
            throw;
        }
        catch (Exception error) when (
            error is ArgumentException
                or InvalidDataException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            throw new RekallAgeResourceRestorationException(
                RekallAgeResourceRestorationException.PathInvalidCode,
                $"Resource '{resourcePath}' is not a confined restorable project path.",
                resourcePath,
                error);
        }
    }
}
