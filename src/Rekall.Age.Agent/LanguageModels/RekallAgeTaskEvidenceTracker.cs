using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

internal sealed class RekallAgeTaskEvidenceTracker
{
    private readonly Requirements _requirements;
    private readonly HashSet<int> _acceptedCaptureFrames = [];
    private bool _remoteSearchSucceeded;
    private bool _licensedRemoteImportSucceeded;
    private bool _shaderSourceAuthored;
    private bool _shaderValidated;
    private bool _shaderPipelineAssigned;

    public RekallAgeTaskEvidenceTracker(string task, bool enabled)
    {
        _requirements = enabled ? Requirements.FromTask(task) : Requirements.None;
        InitialInstruction = BuildInstruction(initial: true);
    }

    public string InitialInstruction { get; }

    public void Observe(string toolName, JsonObject arguments, JsonNode output, bool succeeded)
    {
        if (!succeeded)
        {
            return;
        }

        if (IsAuthoringMutation(toolName))
        {
            _acceptedCaptureFrames.Clear();
        }

        switch (toolName)
        {
            case "rekall.asset.search_remote_images":
                _remoteSearchSucceeded = true;
                break;
            case "rekall.asset.import_remote":
                _licensedRemoteImportSucceeded = !_requirements.RequireOpenLicense
                    || HasText(arguments, "license")
                    && HasText(arguments, "licenseUrl")
                    && HasText(arguments, "attribution");
                break;
            case "rekall.shader.write":
                _shaderSourceAuthored = true;
                break;
            case "rekall.shader.validate":
                _shaderValidated = true;
                break;
            case "rekall.shader.assign_pipeline":
                _shaderPipelineAssigned = true;
                break;
            case "rekall.render.capture_runtime_viewport":
                ObserveCapture(output);
                break;
        }
    }

    public bool TryBuildMissingEvidencePrompt(out string prompt)
    {
        var missing = MissingEvidence();
        if (missing.Count == 0)
        {
            prompt = string.Empty;
            return false;
        }

        prompt = "Task-specific completion evidence is incomplete. A package audit proves mechanical packaging, not the explicit visual request. Missing:\n- "
            + string.Join("\n- ", missing)
            + "\nContinue authoring from the current project. Repair only the missing requirements, capture fresh evidence after the latest authoring mutation, then rebuild/repackage and rerun the final package audit. Do not claim completion until every item above has direct passing tool evidence.";
        return true;
    }

    private string BuildInstruction(bool initial)
    {
        if (!_requirements.Any)
        {
            return string.Empty;
        }

        var requirements = new List<string>();
        if (_requirements.RequireRemoteImage)
        {
            requirements.Add(_requirements.RequireRemoteSearch
                ? "discover a suitable public image with rekall.asset.search_remote_images, then import the chosen exact URL with rekall.asset.import_remote"
                : "import the requested remote image with rekall.asset.import_remote");
        }
        if (_requirements.RequireOpenLicense)
        {
            requirements.Add("preserve non-empty attribution, license, and licenseUrl provenance on the imported asset");
        }
        if (_requirements.RequireCustomShader)
        {
            requirements.Add("author the requested custom shader with rekall.shader.write, validate it, and assign its pipeline to the visible background/renderable with rekall.shader.assign_pipeline; moving mesh stand-ins do not satisfy a custom-shader request");
        }
        if (_requirements.RequireFullViewportCoverage)
        {
            requirements.Add("capture a runtime viewport whose requested asset-backed background fills the window; REKALL_VIEWPORT_LOW_VISUAL_COVERAGE is failed evidence for this task");
        }
        if (_requirements.RequireDistinctTimeFrames)
        {
            requirements.Add("capture at least two accepted runtime viewport frames at distinct frame indices after the final mutation, in addition to executable runtime-state proof, so the time-varying visual is evidenced");
        }

        return (initial
                ? "Task-specific delivery checklist derived from the authoritative user request. This checklist is mandatory and supplements the generic engine workflow:\n- "
                : "Task-specific requirements:\n- ")
            + string.Join("\n- ", requirements)
            + "\nA generic package audit cannot substitute for any missing checklist item.";
    }

    private List<string> MissingEvidence()
    {
        var missing = new List<string>();
        if (_requirements.RequireRemoteSearch && !_remoteSearchSucceeded)
        {
            missing.Add("No successful rekall.asset.search_remote_images evidence for the requested internet image.");
        }
        if (_requirements.RequireRemoteImage && !_licensedRemoteImportSucceeded)
        {
            missing.Add(_requirements.RequireOpenLicense
                ? "No successful rekall.asset.import_remote evidence with attribution, license, and licenseUrl for the requested openly licensed image."
                : "No successful rekall.asset.import_remote evidence for the requested internet image.");
        }
        if (_requirements.RequireCustomShader && !_shaderSourceAuthored)
        {
            missing.Add("No successful rekall.shader.write evidence for the requested custom shader.");
        }
        if (_requirements.RequireCustomShader && !_shaderPipelineAssigned)
        {
            missing.Add("No successful rekall.shader.assign_pipeline evidence attaching the custom shader to visible authored content.");
        }
        if (_requirements.RequireCustomShader && !_shaderValidated)
        {
            missing.Add("No successful rekall.shader.validate evidence for the authored custom shader pipeline.");
        }
        if ((_requirements.RequireRemoteImage || _requirements.RequireFullViewportCoverage)
            && _acceptedCaptureFrames.Count == 0)
        {
            missing.Add("No fresh asset-backed full-coverage runtime viewport capture. The frame must be informative, contain an asset-backed renderable, remain below 95% dominant clear color, and must not report REKALL_VIEWPORT_LOW_VISUAL_COVERAGE.");
        }
        if (_requirements.RequireDistinctTimeFrames && _acceptedCaptureFrames.Count < 2)
        {
            missing.Add("Fewer than two accepted runtime viewport captures at distinct frame indices prove the requested moving visual.");
        }
        return missing;
    }

    private void ObserveCapture(JsonNode output)
    {
        if (output["value"] is not JsonObject value
            || !ReadBoolean(value, "captured")
            || value["frameAnalysis"] is not JsonObject analysis
            || !ReadBoolean(analysis, "analyzed")
            || !ReadBoolean(analysis, "visuallyInformative"))
        {
            return;
        }

        if (_requirements.RequireRemoteImage && ReadInt32(value, "assetBackedRenderableCount") < 1)
        {
            return;
        }

        if (_requirements.RequireFullViewportCoverage
            && (ReadDouble(analysis, "dominantColorRatio") >= 0.95
                || HasWarning(analysis, "REKALL_VIEWPORT_LOW_VISUAL_COVERAGE")))
        {
            return;
        }

        _acceptedCaptureFrames.Add(ReadInt32(value, "frameIndex"));
    }

    private static bool IsAuthoringMutation(string toolName) =>
        toolName is "rekall.asset.import_remote"
            or "rekall.module.write_source"
            or "rekall.module.scaffold_runtime_system"
            or "rekall.module.scaffold_playable"
            or "rekall.shader.write"
            or "rekall.shader.assign_pipeline"
            or "rekall.workflow.create_blueprint_project"
            or "rekall.scene.apply_blueprint"
        || toolName.StartsWith("rekall.entity.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.component.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.property.", StringComparison.Ordinal);

    private static bool HasWarning(JsonObject analysis, string warning) =>
        analysis["warningCodes"] is JsonArray warnings
        && warnings.Any(item => item?.GetValue<string>().Equals(warning, StringComparison.Ordinal) == true);

    private static bool HasText(JsonObject value, string propertyName) =>
        Find(value, propertyName) is JsonValue property
        && property.TryGetValue<string>(out var text)
        && !string.IsNullOrWhiteSpace(text);

    private static bool ReadBoolean(JsonObject value, string propertyName) =>
        Find(value, propertyName) is JsonValue property
        && property.TryGetValue<bool>(out var result)
        && result;

    private static int ReadInt32(JsonObject value, string propertyName) =>
        Find(value, propertyName) is JsonValue property
        && property.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static double ReadDouble(JsonObject value, string propertyName) =>
        Find(value, propertyName) is JsonValue property
        && property.TryGetValue<double>(out var result)
            ? result
            : 0;

    private static JsonNode? Find(JsonObject value, string propertyName) =>
        value.FirstOrDefault(property => property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)).Value;

    private sealed record Requirements(
        bool RequireRemoteImage,
        bool RequireRemoteSearch,
        bool RequireOpenLicense,
        bool RequireCustomShader,
        bool RequireFullViewportCoverage,
        bool RequireDistinctTimeFrames)
    {
        public static Requirements None { get; } = new(false, false, false, false, false, false);

        public bool Any => this != None;

        public static Requirements FromTask(string task)
        {
            var request = ExtractUserRequest(task).ToLowerInvariant();
            var image = ContainsAny(request, "image", "photo", "picture", "texture", "background");
            var remote = image && ContainsAny(request, "internet", "online", "from the web", "remote url", "https://");
            var suppliedUrl = request.Contains("https://", StringComparison.Ordinal);
            var openLicense = remote && ContainsAny(request, "openly licensed", "open licensed", "creative commons", "public domain", "license");
            var shader = ContainsAny(request, "custom shader", "shader effect", "as a shader", "using a shader");
            var fullViewport = ContainsAny(
                request,
                "full-window",
                "full window",
                "full-screen",
                "full screen",
                "stretched across the window",
                "fills the window",
                "fill the window");
            var timeVarying = ContainsAny(request, "moving", "animated", "animating", "time-varying", "time varying");
            return new Requirements(remote, remote && !suppliedUrl, openLicense, shader, fullViewport, timeVarying);
        }

        private static string ExtractUserRequest(string task)
        {
            const string startTag = "<user-request>";
            const string endTag = "</user-request>";
            var start = task.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return task;
            }
            start += startTag.Length;
            var end = task.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? task[start..] : task[start..end];
        }

        private static bool ContainsAny(string value, params string[] candidates) =>
            candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
    }
}
