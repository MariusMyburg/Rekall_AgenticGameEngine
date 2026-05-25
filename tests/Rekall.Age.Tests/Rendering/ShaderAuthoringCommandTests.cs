using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ShaderAuthoringCommandTests
{
    [Fact]
    public async Task WriteReadListAndValidateShaderSource()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader authoring"), CancellationToken.None);
        const string source = """
            #version 450

            layout(location = 0) out vec4 outColor;

            void main()
            {
                outColor = vec4(1.0);
            }
            """;

        var written = await new WriteShaderSourceCommand().ExecuteAsync(
            new WriteShaderSourceRequest(root, "agent/glow", "fragment", source),
            context);

        Assert.True(written.Ok, written.Summary);
        Assert.EndsWith(Path.Combine("Shaders", "agent", "glow.frag"), written.Value.RelativePath, StringComparison.Ordinal);
        Assert.True(written.Value.Validated);
        Assert.True(written.Value.Compiled);
        Assert.Contains(written.Value.Path, context.Transaction.ChangedResources);

        var read = await new ReadShaderSourceCommand().ExecuteAsync(
            new ReadShaderSourceRequest(root, "agent/glow", "fragment"),
            context);
        Assert.True(read.Ok, read.Summary);
        Assert.Equal(source.ReplaceLineEndings("\n"), read.Value.Source.ReplaceLineEndings("\n"));

        var listed = await new ListShaderSourcesCommand().ExecuteAsync(
            new ListShaderSourcesRequest(root),
            context);
        Assert.True(listed.Ok, listed.Summary);
        Assert.Contains(listed.Value.Shaders, shader => shader.Name == "agent/glow" && shader.Stage == "fragment");

        var validated = await new ValidateShaderSourceCommand().ExecuteAsync(
            new ValidateShaderSourceRequest(root, "agent/glow", "fragment"),
            context);
        Assert.True(validated.Ok, validated.Summary);
        Assert.True(validated.Value.Compiled);
        Assert.Empty(validated.Value.Errors);
    }

    [Fact]
    public async Task WriteShaderRejectsPathTraversal()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader authoring"), CancellationToken.None);

        var result = await new WriteShaderSourceCommand().ExecuteAsync(
            new WriteShaderSourceRequest(root, "../escape", "fragment", "#version 450\nvoid main(){}"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_SHADER_PATH_INVALID");
    }

    [Fact]
    public async Task ValidateInlineShaderRejectsUnsupportedStageAsCommandError()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader authoring"), CancellationToken.None);

        var result = await new ValidateShaderSourceCommand().ExecuteAsync(
            new ValidateShaderSourceRequest(root, "agent/glow", "compute", "#version 450\nvoid main(){}"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_SHADER_STAGE_INVALID");
    }

    [Fact]
    public async Task AssignShaderPipelineValidatesShadersAndProjectsToRuntimeFrame()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Shader Cube", ["mesh"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader assignment"), CancellationToken.None);

        await new WriteShaderSourceCommand().ExecuteAsync(
            new WriteShaderSourceRequest(
                root,
                "agent/basic",
                "vertex",
                """
                #version 450
                layout(location = 0) in vec3 inPosition;
                void main()
                {
                    gl_Position = vec4(inPosition, 1.0);
                }
                """),
            context);
        await new WriteShaderSourceCommand().ExecuteAsync(
            new WriteShaderSourceRequest(
                root,
                "agent/glow",
                "fragment",
                """
                #version 450
                layout(location = 0) out vec4 outColor;
                void main()
                {
                    outColor = vec4(0.1, 0.8, 1.0, 1.0);
                }
                """),
            context);

        var assigned = await new AssignShaderPipelineCommand().ExecuteAsync(
            new AssignShaderPipelineRequest(root, "Main", entity.Id, "agent/basic", "agent/glow"),
            context);

        Assert.True(assigned.Ok, assigned.Summary);
        Assert.True(assigned.Value.Validated);
        Assert.Equal("agent/basic", assigned.Value.VertexShader);
        Assert.Equal("agent/glow", assigned.Value.FragmentShader);
        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        var renderer = scene.GetRequiredEntity(entity.Id).Components.Single(component => component.Type == "Rekall.MeshRenderer");
        Assert.Equal("agent/basic", renderer.Properties["vertexShader"]!.GetValue<string>());
        Assert.Equal("agent/glow", renderer.Properties["fragmentShader"]!.GetValue<string>());

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables);
        Assert.NotNull(renderable.ShaderPipeline);
        Assert.Equal("agent/basic", renderable.ShaderPipeline.VertexShader);
        Assert.Equal("agent/glow", renderable.ShaderPipeline.FragmentShader);
        Assert.Contains(store.GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task AssignShaderPipelineRejectsMissingFragmentShader()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Shader Cube", ["mesh"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity),
            CancellationToken.None);

        var result = await new AssignShaderPipelineCommand().ExecuteAsync(
            new AssignShaderPipelineRequest(root, "Main", entity.Id, "agent/basic", "agent/missing"),
            new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader assignment"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_SHADER_NOT_FOUND");
    }
}
