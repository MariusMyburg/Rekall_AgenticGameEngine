using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rekall.Age.Studio;

internal enum RekallAgeStudioDockRegion
{
    Left,
    Right,
    Bottom
}

internal enum RekallAgeStudioLayoutPreset
{
    Default,
    Authoring,
    Debug
}

internal sealed record RekallAgeStudioDockPanelLayout(
    string Id,
    RekallAgeStudioDockRegion Region,
    bool Visible,
    double Size,
    int Order);

internal sealed record RekallAgeStudioLayout(
    int Version,
    double WindowX,
    double WindowY,
    double WindowWidth,
    double WindowHeight,
    bool WindowMaximized,
    string ActiveOutputTab,
    IReadOnlyList<RekallAgeStudioDockPanelLayout> Panels)
{
    public const int CurrentVersion = 4;

    private static readonly string[] PanelIds = ["Hierarchy", "Inspector", "Output"];
    private static readonly HashSet<string> OutputTabs = new(
        ["Validation", "Assets", "Overview", "Actions", "Runtime", "Transactions", "Imports"],
        StringComparer.Ordinal);

    public string ActiveWorkspace { get; init; } = "Author";

    public static RekallAgeStudioLayout Default { get; } = new(
        CurrentVersion,
        double.NaN,
        double.NaN,
        1500,
        940,
        false,
        "Validation",
        [
            new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 340, 0),
            new("Inspector", RekallAgeStudioDockRegion.Right, true, 460, 0),
            new("Output", RekallAgeStudioDockRegion.Bottom, true, 260, 0)
        ])
    {
        ActiveWorkspace = "Author"
    };

    public RekallAgeStudioDockPanelLayout Panel(string id) =>
        Panels.First(panel => panel.Id.Equals(id, StringComparison.Ordinal));

    public static RekallAgeStudioLayout CreatePreset(RekallAgeStudioLayoutPreset preset) => preset switch
    {
        RekallAgeStudioLayoutPreset.Authoring => Default with
        {
            ActiveWorkspace = "Author",
            ActiveOutputTab = "Validation",
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 360, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 500, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 280, 0)
            ]
        },
        RekallAgeStudioLayoutPreset.Debug => Default with
        {
            ActiveWorkspace = "World",
            ActiveOutputTab = "Runtime",
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 330, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 460, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 420, 0)
            ]
        },
        _ => Default
    };

    public static RekallAgeStudioLayout? Normalize(RekallAgeStudioLayout? candidate)
    {
        if (candidate is null || candidate.Version is not (1 or 2 or 3 or CurrentVersion) || candidate.Panels is null)
        {
            return null;
        }

        var sourceVersion = candidate.Version;
        var legacyAuthoringLayout = sourceVersion == 1
            && string.Equals(candidate.ActiveOutputTab, "AI Agent", StringComparison.Ordinal);
        candidate = candidate with
        {
            Version = CurrentVersion,
            ActiveWorkspace = legacyAuthoringLayout ? "Author" : candidate.ActiveWorkspace,
            ActiveOutputTab = legacyAuthoringLayout ? "Validation" : candidate.ActiveOutputTab
        };

        var panels = candidate.Panels.ToArray();
        if (panels.Length != PanelIds.Length
            || panels.Any(panel => panel is null || string.IsNullOrWhiteSpace(panel.Id))
            || panels.Select(panel => panel.Id).Distinct(StringComparer.Ordinal).Count() != PanelIds.Length
            || PanelIds.Any(id => panels.All(panel => !panel.Id.Equals(id, StringComparison.Ordinal))))
        {
            return null;
        }
        if (sourceVersion < CurrentVersion)
        {
            panels = MigrateLegacyPanelWidths(panels);
        }

        var normalizedPanels = panels
            .Select(panel => panel with
            {
                Size = NormalizePanelSize(panel.Id, panel.Size),
                Order = Math.Clamp(panel.Order, 0, 16)
            })
            .OrderBy(panel => Array.IndexOf(PanelIds, panel.Id))
            .ToArray();
        return candidate with
        {
            WindowX = double.IsFinite(candidate.WindowX) ? candidate.WindowX : double.NaN,
            WindowY = double.IsFinite(candidate.WindowY) ? candidate.WindowY : double.NaN,
            WindowWidth = NormalizeDimension(candidate.WindowWidth, Default.WindowWidth, 1120, 3840),
            WindowHeight = NormalizeDimension(candidate.WindowHeight, Default.WindowHeight, 700, 2160),
            ActiveOutputTab = OutputTabs.Contains(candidate.ActiveOutputTab ?? string.Empty)
                ? candidate.ActiveOutputTab!
                : Default.ActiveOutputTab,
            ActiveWorkspace = candidate.ActiveWorkspace is "Author" or "World" or "Code" or "Modeling"
                ? candidate.ActiveWorkspace
                : Default.ActiveWorkspace,
            Panels = normalizedPanels
        };
    }

    private static double NormalizeDimension(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static RekallAgeStudioDockPanelLayout[] MigrateLegacyPanelWidths(
        RekallAgeStudioDockPanelLayout[] panels)
    {
        var hierarchy = panels.FirstOrDefault(panel => panel.Id.Equals("Hierarchy", StringComparison.Ordinal));
        var inspector = panels.FirstOrDefault(panel => panel.Id.Equals("Inspector", StringComparison.Ordinal));
        var replacement = (hierarchy?.Size, inspector?.Size) switch
        {
            (290, 370) => (Hierarchy: 340d, Inspector: 460d),
            (300, 390) => (Hierarchy: 360d, Inspector: 500d),
            (270, 340) => (Hierarchy: 330d, Inspector: 460d),
            _ => (
                Hierarchy: Math.Max(hierarchy!.Size, Default.Panel("Hierarchy").Size),
                Inspector: Math.Max(inspector!.Size, Default.Panel("Inspector").Size))
        };
        return panels.Select(panel => panel.Id switch
        {
            "Hierarchy" => panel with { Size = replacement.Hierarchy },
            "Inspector" => panel with { Size = replacement.Inspector },
            _ => panel
        }).ToArray();
    }

    private static double NormalizePanelSize(string id, double value)
    {
        var fallback = Default.Panel(id).Size;
        if (!double.IsFinite(value)) return fallback;
        return id.Equals("Output", StringComparison.Ordinal)
            ? Math.Clamp(value, 140, 640)
            : Math.Clamp(value, 180, 720);
    }
}

internal interface IRekallAgeStudioLayoutStore
{
    ValueTask<RekallAgeStudioLayout> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(RekallAgeStudioLayout layout, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioLayoutStore : IRekallAgeStudioLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public RekallAgeStudioLayoutStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rekall",
            "AGE",
            "Studio",
            "layout-v1.json"))
    {
    }

    internal RekallAgeStudioLayoutStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async ValueTask<RekallAgeStudioLayout> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return RekallAgeStudioLayout.Default;
        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var candidate = await JsonSerializer.DeserializeAsync<RekallAgeStudioLayout>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return RekallAgeStudioLayout.Normalize(candidate) ?? RekallAgeStudioLayout.Default;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return RekallAgeStudioLayout.Default;
        }
    }

    public async ValueTask SaveAsync(RekallAgeStudioLayout layout, CancellationToken cancellationToken)
    {
        var normalized = RekallAgeStudioLayout.Normalize(layout)
            ?? throw new ArgumentException("Studio layout is incomplete or incompatible.", nameof(layout));
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Studio layout directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
