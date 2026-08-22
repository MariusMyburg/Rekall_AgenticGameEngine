using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class ShaderPreprocessorTests
{
    [Fact]
    public async Task NestedIncludesAndPragmaOnceCompileAndAffectPipelineHash()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(Path.Combine(shaderRoot, "lib"));
        var commonPath = Path.Combine(shaderRoot, "lib", "common.glslinc");
        await File.WriteAllTextAsync(commonPath, "#pragma once\nvec4 tint(vec4 value) { return value * 0.5; }");
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "lib", "color.glslinc"),
            "#include \"common.glslinc\"\n#include \"common.glslinc\"\nvec4 color() { return tint(vec4(1.0)); }");
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "include.vert"),
            "#version 450\nlayout(location = 0) in vec3 inPosition;\nvoid main(){gl_Position=vec4(inPosition,1.0);}");
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "include.frag"),
            "#version 450\n#include \"lib/color.glslinc\"\nlayout(location = 0) out vec4 outColor;\nvoid main(){outColor=color();}");

        var resolver = new RekallAgeProjectShaderPipelineResolver();
        var reference = new RekallAgeRuntimeViewportShaderPipeline("agent/include", "agent/include");
        var first = await resolver.ResolveAsync(root, reference, CancellationToken.None);

        Assert.True(first.Valid, string.Join(Environment.NewLine, first.Errors));
        Assert.Contains("vec4 color()", first.FragmentSource, StringComparison.Ordinal);
        Assert.Equal(1, Count(first.FragmentSource, "vec4 tint("));
        await File.WriteAllTextAsync(commonPath, "#pragma once\nvec4 tint(vec4 value) { return value * 0.25; }");
        var second = await resolver.ResolveAsync(root, reference, CancellationToken.None);
        Assert.True(second.Valid, string.Join(Environment.NewLine, second.Errors));
        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public async Task PreprocessorRejectsCycleAndTraversalWithStableDiagnostics()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders");
        Directory.CreateDirectory(shaderRoot);
        var entry = Path.Combine(shaderRoot, "entry.frag");
        var a = Path.Combine(shaderRoot, "a.glslinc");
        var b = Path.Combine(shaderRoot, "b.glslinc");
        await File.WriteAllTextAsync(entry, "#version 450\n#include \"a.glslinc\"");
        await File.WriteAllTextAsync(a, "#include \"b.glslinc\"");
        await File.WriteAllTextAsync(b, "#include \"a.glslinc\"");

        var cycle = await new RekallAgeShaderPreprocessor().ExpandFileAsync(root, entry, CancellationToken.None);
        Assert.False(cycle.Success);
        Assert.Contains(cycle.Diagnostics, diagnostic => diagnostic.Code == "REKALL_SHADER_INCLUDE_CYCLE");

        await File.WriteAllTextAsync(entry, "#version 450\n#include \"../escape.glslinc\"");
        var traversal = await new RekallAgeShaderPreprocessor().ExpandFileAsync(root, entry, CancellationToken.None);
        Assert.False(traversal.Success);
        Assert.Contains(traversal.Diagnostics, diagnostic => diagnostic.Code == "REKALL_SHADER_INCLUDE_PATH_INVALID");
    }

    [Fact]
    public async Task PreprocessorReportsMissingMalformedAndDepthLimits()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders");
        Directory.CreateDirectory(shaderRoot);
        var entry = Path.Combine(shaderRoot, "entry.frag");
        var preprocessor = new RekallAgeShaderPreprocessor();

        await File.WriteAllTextAsync(entry, "#version 450\n#include \"missing.glslinc\"");
        var missing = await preprocessor.ExpandFileAsync(root, entry, CancellationToken.None);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "REKALL_SHADER_INCLUDE_NOT_FOUND");

        await File.WriteAllTextAsync(entry, "#version 450\n#include missing.glslinc");
        var malformed = await preprocessor.ExpandFileAsync(root, entry, CancellationToken.None);
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Code == "REKALL_SHADER_INCLUDE_MALFORMED");

        await File.WriteAllTextAsync(entry, "#version 450\n#include \"depth-0.glslinc\"");
        for (var index = 0; index < 18; index++)
        {
            var next = index == 17 ? "float end;" : $"#include \"depth-{index + 1}.glslinc\"";
            await File.WriteAllTextAsync(Path.Combine(shaderRoot, $"depth-{index}.glslinc"), next);
        }

        var depth = await preprocessor.ExpandFileAsync(root, entry, CancellationToken.None);
        Assert.Contains(depth.Diagnostics, diagnostic => diagnostic.Code == "REKALL_SHADER_INCLUDE_DEPTH_LIMIT");
    }

    [Fact]
    public async Task IncludeCommandsWriteReadListAndPreprocessForCliAndMcp()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("shader-test", RekallAgeTransaction.Begin("shader include"), CancellationToken.None);
        var written = await new WriteShaderIncludeCommand().ExecuteAsync(
            new WriteShaderIncludeRequest(root, "agent/math", "float twice(float value) { return value * 2.0; }"),
            context);
        Assert.True(written.Ok, written.Summary);
        Assert.EndsWith("math.glslinc", written.Value.RelativePath, StringComparison.Ordinal);

        var read = await new ReadShaderSourceCommand().ExecuteAsync(
            new ReadShaderSourceRequest(root, "agent/math", "include"),
            context);
        Assert.True(read.Ok, read.Summary);
        Assert.Contains("float twice", read.Value.Source, StringComparison.Ordinal);
        var listed = await new ListShaderSourcesCommand().ExecuteAsync(new(root, false), context);
        Assert.Contains(listed.Value.Shaders, shader => shader.Name == "agent/math" && shader.Stage == "include");

        await File.WriteAllTextAsync(Path.Combine(root, "Shaders", "agent", "use.frag"),
            "#version 450\n#include \"math.glslinc\"\nlayout(location=0) out vec4 c; void main(){c=vec4(twice(0.5));}");
        var preprocessed = await new PreprocessShaderSourceCommand().ExecuteAsync(
            new PreprocessShaderSourceRequest(root, "agent/use", "fragment"),
            context);
        Assert.True(preprocessed.Ok, preprocessed.Summary);
        Assert.Contains("float twice", preprocessed.Value.ExpandedSource, StringComparison.Ordinal);
        Assert.Single(preprocessed.Value.Dependencies);

        var engineScope = await new PreprocessShaderSourceCommand().ExecuteAsync(
            new PreprocessShaderSourceRequest(root, "agent/use", "fragment", "engine"),
            context);
        Assert.False(engineScope.Ok);
        Assert.Contains(engineScope.Errors, error => error.Code == "REKALL_SHADER_SCOPE_INVALID");
    }

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
