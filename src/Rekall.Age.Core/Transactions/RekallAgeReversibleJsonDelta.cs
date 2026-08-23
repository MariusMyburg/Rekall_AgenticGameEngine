using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Core.Transactions;

public sealed record RekallAgeReversibleJsonDeltaOperation(
    string Kind,
    string Path,
    bool BeforeExists,
    bool AfterExists,
    JsonNode? BeforeValue = null,
    JsonNode? AfterValue = null,
    int Start = 0,
    IReadOnlyList<JsonNode?>? BeforeValues = null,
    IReadOnlyList<JsonNode?>? AfterValues = null);

public sealed record RekallAgeTransactionResourceDeltaEntry(
    string Path,
    string RelativePath,
    string Format,
    string BeforeSha256,
    string AfterSha256,
    long EncodedSizeBytes,
    IReadOnlyList<RekallAgeReversibleJsonDeltaOperation> Operations);

public static class RekallAgeReversibleJsonDelta
{
    public const string Format = "reversible-json-splice-v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = Persistence.RekallAgePersistedJson.MaximumDocumentDepth
    };

    public static RekallAgeTransactionResourceDeltaEntry? Create(
        string path,
        string relativePath,
        byte[] beforeBytes,
        byte[] afterBytes)
    {
        JsonNode? before;
        JsonNode? after;
        try
        {
            before = JsonNode.Parse(beforeBytes);
            after = JsonNode.Parse(afterBytes);
        }
        catch (JsonException)
        {
            return null;
        }
        if (before is null || after is null)
        {
            return null;
        }
        var operations = new List<RekallAgeReversibleJsonDeltaOperation>();
        Diff(before, after, string.Empty, operations);
        if (operations.Count == 0)
        {
            return null;
        }
        var encodedSize = JsonSerializer.SerializeToUtf8Bytes(operations, JsonOptions).LongLength;
        if (encodedSize >= beforeBytes.LongLength)
        {
            return null;
        }
        return new(
            path,
            relativePath,
            Format,
            Sha(beforeBytes),
            Sha(afterBytes),
            encodedSize,
            operations);
    }

    public static byte[] ApplyInverse(RekallAgeTransactionResourceDeltaEntry delta, byte[] currentBytes)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (!delta.Format.Equals(Format, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported reversible delta format '{delta.Format}'.");
        }
        if (!Sha(currentBytes).Equals(delta.AfterSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Current resource does not match the reversible delta after-state.");
        }
        var root = JsonNode.Parse(currentBytes)
            ?? throw new InvalidOperationException("Current JSON resource is empty.");
        foreach (var operation in delta.Operations.Reverse())
        {
            ApplyInverse(root, operation);
        }
        var restored = Encoding.UTF8.GetBytes(root.ToJsonString(JsonOptions) + Environment.NewLine);
        if (!Sha(restored).Equals(delta.BeforeSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Reversible delta did not reconstruct the expected before-state.");
        }
        return restored;
    }

    private static void Diff(JsonNode before, JsonNode after, string path, ICollection<RekallAgeReversibleJsonDeltaOperation> operations)
    {
        if (JsonNode.DeepEquals(before, after)) return;
        if (before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            foreach (var key in beforeObject.Select(item => item.Key).Union(afterObject.Select(item => item.Key), StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                var beforeExists = beforeObject.TryGetPropertyValue(key, out var beforeValue);
                var afterExists = afterObject.TryGetPropertyValue(key, out var afterValue);
                var childPath = path + "/" + Escape(key);
                if (beforeExists && afterExists && beforeValue is not null && afterValue is not null)
                {
                    Diff(beforeValue, afterValue, childPath, operations);
                }
                else if (beforeExists != afterExists || !JsonNode.DeepEquals(beforeValue, afterValue))
                {
                    operations.Add(new("replace", childPath, beforeExists, afterExists, beforeValue?.DeepClone(), afterValue?.DeepClone()));
                }
            }
            return;
        }
        if (before is JsonArray beforeArray && after is JsonArray afterArray)
        {
            var prefix = 0;
            while (prefix < beforeArray.Count && prefix < afterArray.Count && JsonNode.DeepEquals(beforeArray[prefix], afterArray[prefix])) prefix++;
            var suffix = 0;
            while (suffix < beforeArray.Count - prefix && suffix < afterArray.Count - prefix
                && JsonNode.DeepEquals(beforeArray[beforeArray.Count - 1 - suffix], afterArray[afterArray.Count - 1 - suffix])) suffix++;
            operations.Add(new(
                "splice",
                path,
                true,
                true,
                Start: prefix,
                BeforeValues: beforeArray.Skip(prefix).Take(beforeArray.Count - prefix - suffix).Select(value => value?.DeepClone()).ToArray(),
                AfterValues: afterArray.Skip(prefix).Take(afterArray.Count - prefix - suffix).Select(value => value?.DeepClone()).ToArray()));
            return;
        }
        operations.Add(new("replace", path, true, true, before.DeepClone(), after.DeepClone()));
    }

    private static void ApplyInverse(JsonNode root, RekallAgeReversibleJsonDeltaOperation operation)
    {
        if (operation.Kind == "splice")
        {
            var array = Resolve(root, operation.Path) as JsonArray
                ?? throw new InvalidOperationException($"Delta path '{operation.Path}' is not an array.");
            for (var index = 0; index < (operation.AfterValues?.Count ?? 0); index++) array.RemoveAt(operation.Start);
            for (var index = 0; index < (operation.BeforeValues?.Count ?? 0); index++) array.Insert(operation.Start + index, operation.BeforeValues![index]?.DeepClone());
            return;
        }
        var (parent, token) = ResolveParent(root, operation.Path);
        if (parent is JsonObject obj)
        {
            if (operation.BeforeExists) obj[token] = operation.BeforeValue?.DeepClone(); else obj.Remove(token);
            return;
        }
        throw new InvalidOperationException($"Delta replace path '{operation.Path}' has an unsupported parent.");
    }

    private static JsonNode Resolve(JsonNode root, string path)
    {
        var current = root;
        foreach (var token in Tokens(path))
        {
            current = current is JsonObject obj ? obj[token]! : current.AsArray()[int.Parse(token)]!;
        }
        return current;
    }

    private static (JsonNode Parent, string Token) ResolveParent(JsonNode root, string path)
    {
        var tokens = Tokens(path).ToArray();
        if (tokens.Length == 0) throw new InvalidOperationException("Root replacement is unsupported.");
        var parentPath = tokens.Length == 1 ? string.Empty : "/" + string.Join('/', tokens[..^1].Select(Escape));
        return (Resolve(root, parentPath), tokens[^1]);
    }

    private static IEnumerable<string> Tokens(string path) =>
        string.IsNullOrEmpty(path) ? [] : path.Split('/').Skip(1).Select(Unescape);

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string Unescape(string value) => value.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
