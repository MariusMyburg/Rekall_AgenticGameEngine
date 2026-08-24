using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Playback;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class CapturePlayablePackageFrameRequestTests
{
    [Fact]
    public void CaptureRequestsAcceptPositionalFinalNullWithoutAmbiguity()
    {
        var direct = new CapturePlayableFrameRequest(
            "Project",
            "Main",
            "Output",
            1,
            320,
            180,
            null);
        var package = new CapturePlayablePackageFrameRequest(
            "Package",
            "Output",
            1,
            320,
            180,
            null);

        Assert.Null(direct.Inputs);
        Assert.Null(package.Inputs);
    }

    [Fact]
    public async Task RegistryDeserializesGenericInputFramesForDirectCaptureRequest()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DirectRequestProbeCommand());

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.capture_direct_request",
            """
            {
              "projectRoot":"Project",
              "sceneName":"Main",
              "outputDirectory":"Output",
              "inputFrames":[{
                "pressedKeys":["D"],
                "pressedKeysThisFrame":["D"],
                "semanticActions":[{"name":"move.horizontal","value":1,"isDown":true,"wasPressed":true}],
                "deltaSeconds":0.25
              }]
            }
            """,
            Context("direct request JSON"));

        Assert.True(result.Ok, result.Summary);
        var request = Assert.IsType<CapturePlayableFrameRequest>(result.Value);
        Assert.Null(request.Inputs);
        var frame = Assert.Single(request.InputFrames!);
        Assert.Equal(0.25, frame.DeltaSeconds);
        Assert.Equal(["D"], frame.PressedKeys);
        var action = Assert.Single(frame.SemanticActions!);
        Assert.Equal("move.horizontal", action.Name);
        Assert.True(action.WasPressed);
    }

    [Fact]
    public async Task RegistryDeserializesGenericInputFramesForPackageCaptureRequest()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new PackageRequestProbeCommand());

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.capture_package_request",
            """
            {
              "packagePath":"Package",
              "outputDirectory":"Output",
              "inputFrames":[{
                "releasedKeysThisFrame":["Space"],
                "semanticActions":[{"name":"fire","value":0,"isDown":false,"wasReleased":true}],
                "deltaSeconds":0.5
              }]
            }
            """,
            Context("package request JSON"));

        Assert.True(result.Ok, result.Summary);
        var request = Assert.IsType<CapturePlayablePackageFrameRequest>(result.Value);
        Assert.Null(request.Inputs);
        var frame = Assert.Single(request.InputFrames!);
        Assert.Equal(0.5, frame.DeltaSeconds);
        Assert.Equal(["Space"], frame.ReleasedKeysThisFrame);
        var action = Assert.Single(frame.SemanticActions!);
        Assert.Equal("fire", action.Name);
        Assert.True(action.WasReleased);
    }

    [Fact]
    public void CaptureRequestSchemasDocumentInputFramesAsCanonical()
    {
        Assert.Contains("inputFrames", new CapturePlayableFrameCommand().Schema.Description, StringComparison.Ordinal);
        Assert.Contains("inputFrames", new CapturePlayablePackageFrameCommand().Schema.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeOutputRecoveryPreservesCanonicalInputFrames()
    {
        var packagePath = TestPaths.CreateTempDirectory();
        var inputFrames = new RekallAgeRuntimeInputFrame[]
        {
            new(SemanticActions: [new("fire", 1, true, true)]) { DeltaSeconds = 0.25 }
        };
        var request = new CapturePlayablePackageFrameRequest(
            packagePath,
            Path.Combine(packagePath, "Proof"),
            InputFrames: inputFrames);

        var unsafeOutput = CapturePlayablePackageFrameCommand.TryCreateUnsafeOutputError(request, out var error);

        Assert.True(unsafeOutput);
        var retry = Assert.Single(error.SuggestedCommands!);
        Assert.Equal(inputFrames, retry.Arguments["inputFrames"]);
        Assert.DoesNotContain("inputs", retry.Arguments.Keys);
    }

    private static RekallAgeCommandContext Context(string name) => new(
        "mcp",
        RekallAgeTransaction.Begin(name),
        CancellationToken.None);

    private sealed class DirectRequestProbeCommand
        : IRekallAgeCommand<CapturePlayableFrameRequest, CapturePlayableFrameRequest>
    {
        public string Name => "rekall.test.capture_direct_request";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Probes direct capture request JSON.",
            typeof(CapturePlayableFrameRequest).FullName!,
            typeof(CapturePlayableFrameRequest).FullName!);

        public ValueTask<RekallAgeCommandResult<CapturePlayableFrameRequest>> ExecuteAsync(
            CapturePlayableFrameRequest request,
            RekallAgeCommandContext context) =>
            ValueTask.FromResult(RekallAgeCommandResult<CapturePlayableFrameRequest>.Success(request, "Probed."));
    }

    private sealed class PackageRequestProbeCommand
        : IRekallAgeCommand<CapturePlayablePackageFrameRequest, CapturePlayablePackageFrameRequest>
    {
        public string Name => "rekall.test.capture_package_request";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Probes package capture request JSON.",
            typeof(CapturePlayablePackageFrameRequest).FullName!,
            typeof(CapturePlayablePackageFrameRequest).FullName!);

        public ValueTask<RekallAgeCommandResult<CapturePlayablePackageFrameRequest>> ExecuteAsync(
            CapturePlayablePackageFrameRequest request,
            RekallAgeCommandContext context) =>
            ValueTask.FromResult(RekallAgeCommandResult<CapturePlayablePackageFrameRequest>.Success(request, "Probed."));
    }
}
