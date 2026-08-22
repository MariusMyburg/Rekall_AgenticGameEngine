using System.Text;
using System.Text.RegularExpressions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeShaderPreprocessDiagnostic(
    string Code,
    string Message,
    string Path,
    int Line = 0);

public sealed record RekallAgeShaderPreprocessResult(
    bool Success,
    string ExpandedSource,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<RekallAgeShaderPreprocessDiagnostic> Diagnostics);

public sealed partial class RekallAgeShaderPreprocessor
{
    private const int MaximumDepth = 16;
    private const int MaximumFiles = 64;
    private const int MaximumExpandedBytes = 1024 * 1024;

    public async ValueTask<RekallAgeShaderPreprocessResult> ExpandFileAsync(
        string projectRoot,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return await ExpandSourceAsync(projectRoot, sourcePath, source, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeShaderPreprocessResult> ExpandSourceAsync(
        string projectRoot,
        string sourcePath,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var shaderRoot = Path.GetFullPath(Path.Combine(projectRoot, "Shaders"));
        var entryPath = Path.GetFullPath(sourcePath);
        var diagnostics = new List<RekallAgeShaderPreprocessDiagnostic>();
        if (!IsWithinRoot(shaderRoot, entryPath) || HasReparsePoint(shaderRoot, entryPath))
        {
            diagnostics.Add(new(
                "REKALL_SHADER_INCLUDE_PATH_INVALID",
                "Shader source must stay inside the project Shaders directory and cannot cross a filesystem link.",
                entryPath));
            return new(false, string.Empty, [], diagnostics);
        }

        var state = new ExpansionState(shaderRoot, diagnostics);
        await state.ExpandAsync(entryPath, source, isEntry: true, depth: 0, cancellationToken).ConfigureAwait(false);
        return new(
            diagnostics.Count == 0,
            diagnostics.Count == 0 ? state.Builder.ToString() : string.Empty,
            state.Dependencies.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            diagnostics.Take(32).ToArray());
    }

    private sealed class ExpansionState
    {
        private readonly string _shaderRoot;
        private readonly List<RekallAgeShaderPreprocessDiagnostic> _diagnostics;
        private readonly HashSet<string> _stack = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _once = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private int _expandedBytes;

        public ExpansionState(string shaderRoot, List<RekallAgeShaderPreprocessDiagnostic> diagnostics)
        {
            _shaderRoot = shaderRoot;
            _diagnostics = diagnostics;
        }

        public StringBuilder Builder { get; } = new();

        public HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);

        public async ValueTask ExpandAsync(
            string path,
            string source,
            bool isEntry,
            int depth,
            CancellationToken cancellationToken)
        {
            if (_diagnostics.Count > 0 || _once.Contains(path))
            {
                return;
            }

            if (depth > MaximumDepth)
            {
                Add("REKALL_SHADER_INCLUDE_DEPTH_LIMIT", $"Shader include depth exceeds {MaximumDepth}.", path);
                return;
            }

            if (!_stack.Add(path))
            {
                Add("REKALL_SHADER_INCLUDE_CYCLE", "Shader include cycle detected.", path);
                return;
            }

            if (_files.Add(path) && _files.Count > MaximumFiles)
            {
                Add("REKALL_SHADER_INCLUDE_FILE_LIMIT", $"Shader include graph exceeds {MaximumFiles} files.", path);
                _stack.Remove(path);
                return;
            }

            if (!isEntry)
            {
                Dependencies.Add(Relative(path));
                Append($"// REKALL_INCLUDE_BEGIN {Relative(path)}\n", path);
            }

            var lines = source.ReplaceLineEndings("\n").Split('\n');
            for (var index = 0; index < lines.Length && _diagnostics.Count == 0; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[index];
                if (line.Trim().Equals("#pragma once", StringComparison.Ordinal))
                {
                    _once.Add(path);
                    continue;
                }

                var match = IncludeDirective().Match(line);
                if (match.Success)
                {
                    var includePath = ResolveInclude(path, match.Groups[1].Value, index + 1);
                    if (includePath is null)
                    {
                        break;
                    }

                    string includeSource;
                    try
                    {
                        includeSource = await File.ReadAllTextAsync(includePath, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Add("REKALL_SHADER_INCLUDE_NOT_FOUND", $"Shader include could not be read: {exception.Message}", includePath, index + 1);
                        break;
                    }

                    await ExpandAsync(includePath, includeSource, isEntry: false, depth + 1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (line.TrimStart().StartsWith("#include", StringComparison.Ordinal))
                {
                    Add("REKALL_SHADER_INCLUDE_MALFORMED", "Expected a full-line #include \"relative/path.glslinc\" directive.", path, index + 1);
                    break;
                }

                Append(line + "\n", path, index + 1);
            }

            if (!isEntry && _diagnostics.Count == 0)
            {
                Append($"// REKALL_INCLUDE_END {Relative(path)}\n", path);
            }

            _stack.Remove(path);
        }

        private string? ResolveInclude(string includingPath, string requested, int line)
        {
            if (string.IsNullOrWhiteSpace(requested)
                || Path.IsPathRooted(requested)
                || requested.Contains("..", StringComparison.Ordinal)
                || !Path.GetExtension(requested).Equals(".glslinc", StringComparison.OrdinalIgnoreCase))
            {
                Add("REKALL_SHADER_INCLUDE_PATH_INVALID", "Shader includes must be relative .glslinc paths without traversal.", includingPath, line);
                return null;
            }

            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(includingPath)!, requested));
            if (!IsWithinRoot(_shaderRoot, resolved) || HasReparsePoint(_shaderRoot, resolved))
            {
                Add("REKALL_SHADER_INCLUDE_PATH_INVALID", "Shader include must stay inside Shaders and cannot cross a filesystem link.", includingPath, line);
                return null;
            }

            if (!File.Exists(resolved))
            {
                Add("REKALL_SHADER_INCLUDE_NOT_FOUND", $"Shader include '{requested}' was not found.", resolved, line);
                return null;
            }

            return resolved;
        }

        private void Append(string text, string path, int line = 0)
        {
            _expandedBytes += Encoding.UTF8.GetByteCount(text);
            if (_expandedBytes > MaximumExpandedBytes)
            {
                Add("REKALL_SHADER_INCLUDE_SIZE_LIMIT", $"Expanded shader source exceeds {MaximumExpandedBytes} UTF-8 bytes.", path, line);
                return;
            }

            Builder.Append(text);
        }

        private string Relative(string path) => Path.GetRelativePath(_shaderRoot, path).Replace('\\', '/');

        private void Add(string code, string message, string path, int line = 0) =>
            _diagnostics.Add(new RekallAgeShaderPreprocessDiagnostic(code, message, path, line));
    }

    private static bool IsWithinRoot(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool HasReparsePoint(string root, string path)
    {
        for (var current = new FileInfo(path).Directory; current is not null && IsWithinRoot(root, current.FullName); current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    }

    [GeneratedRegex("^\\s*#include\\s+\\\"([^\\\"]+)\\\"\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeDirective();
}
