using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Core.Persistence;

/// <summary>
/// Bounded, project-scoped persistent state: settings, campaign progress, save slots.
///
/// The engine had no persistence primitive at all, so an authored game could not remember a
/// settings change or a completed mission across a restart. This is the generic contract for
/// that - a slot is a named JSON document under the project's own state directory.
///
/// Slot names are validated rather than used as paths: a slot cannot traverse out of the state
/// directory, and the stored document is size-bounded on read and write, so a game cannot be
/// made to load an unbounded or arbitrary file through this surface.
/// </summary>
public static class RekallAgePersistentStateStore
{
    /// <summary>Generous enough for campaign progress, far below anything that would stall a load.</summary>
    public const long MaximumDocumentBytes = 1024L * 1024;

    private const string StateDirectoryName = "State";

    public static string ResolveSlotPath(string projectRoot, string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var normalized = NormalizeSlot(slot);
        var stateRoot = Path.GetFullPath(Path.Combine(projectRoot, StateDirectoryName));
        var path = Path.GetFullPath(Path.Combine(stateRoot, normalized + ".json"));

        // Defence in depth: NormalizeSlot already rejects separators, but the resolved path must
        // still land inside the project's state directory.
        if (!path.StartsWith(stateRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_STATE_SLOT_INVALID",
                path,
                $"Persistent state slot '{slot}' resolves outside the project state directory.");
        }

        return path;
    }

    public static async ValueTask<JsonObject?> ReadAsync(
        string projectRoot,
        string slot,
        CancellationToken cancellationToken)
    {
        var path = ResolveSlotPath(projectRoot, slot);
        if (!File.Exists(path))
        {
            return null;
        }

        var snapshot = await RekallAgeBoundedFileSnapshot
            .ReadAsync(path, MaximumDocumentBytes, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(snapshot.Bytes)) as JsonObject;
        }
        catch (JsonException exception)
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_STATE_DOCUMENT_MALFORMED",
                path,
                $"Persistent state slot '{slot}' is not a JSON object: {exception.Message}",
                exception);
        }
    }

    public static async ValueTask WriteAsync(
        string projectRoot,
        string slot,
        JsonObject document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = ResolveSlotPath(projectRoot, slot);
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaximumDocumentBytes)
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_STATE_DOCUMENT_TOO_LARGE",
                path,
                $"Persistent state slot '{slot}' is {bytes} bytes; the limit is {MaximumDocumentBytes} bytes.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await RekallAgeAtomicFile
            .WriteAllTextAsync(path, json, MaximumDocumentBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Slots are identifiers, not paths. Letters, digits, dash, underscore and dot only, so a
    /// slot can never traverse directories or name a device.
    /// </summary>
    private static string NormalizeSlot(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        var trimmed = slot.Trim();
        if (trimmed.Length > 64)
        {
            throw new ArgumentException("Persistent state slot names are limited to 64 characters.", nameof(slot));
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                throw new ArgumentException(
                    $"Persistent state slot '{slot}' may only contain letters, digits, '-', '_' and '.'.",
                    nameof(slot));
            }
        }

        if (trimmed is "." or ".." || trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Persistent state slot '{slot}' is not a valid slot name.", nameof(slot));
        }

        return trimmed;
    }
}
