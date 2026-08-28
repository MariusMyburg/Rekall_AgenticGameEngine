using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

/// <summary>
/// Authored geometry lives in the component's JSON. Re-parsing it on every frame
/// was the single largest cost in the interactive player, and because each parse
/// produced a fresh instance it also defeated the identity-keyed memo in
/// <see cref="RekallAgeRuntimeGeometrySignature"/>. Parsed geometry must therefore
/// be reused while the component's JSON object is unchanged, and must be re-read
/// as soon as that object is replaced.
/// </summary>
public sealed class RuntimeGeometryMeshReuseTests
{
    private static JsonObject GeometryProperties(double firstX) => new()
    {
        ["vertices"] = new JsonArray
        {
            new JsonObject { ["x"] = firstX, ["y"] = 0, ["z"] = 0 },
            new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 },
            new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }
        },
        ["indices"] = new JsonArray { 0, 1, 2 }
    };

    private static RekallAgeRuntimeWorld BuildWorld(JsonObject geometryProperties)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Triangle", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryMesh",
                    geometryProperties)));
        return new RekallAgeRuntimeWorldBuilder().Build(scene);
    }

    private static Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportGeometryMesh Mesh(
        RekallAgeRuntimeRenderFrameBuilder builder,
        RekallAgeRuntimeWorld world)
    {
        var frame = builder.Build(world, 640, 480, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables, item => item.GeometryMesh is not null);
        return renderable.GeometryMesh!;
    }

    [Fact]
    public void UnchangedGeometryComponentYieldsTheSameParsedMeshInstance()
    {
        var world = BuildWorld(GeometryProperties(0));
        var builder = new RekallAgeRuntimeRenderFrameBuilder();

        var first = Mesh(builder, world);
        var second = Mesh(builder, world);

        Assert.Same(first, second);
    }

    [Fact]
    public void ReplacingTheGeometryComponentJsonReparsesTheMesh()
    {
        var builder = new RekallAgeRuntimeRenderFrameBuilder();

        var original = Mesh(builder, BuildWorld(GeometryProperties(0)));
        var replaced = Mesh(builder, BuildWorld(GeometryProperties(0.5)));

        Assert.NotSame(original, replaced);
        Assert.Equal(0, original.Vertices[0].X);
        Assert.Equal(0.5, replaced.Vertices[0].X);
    }

    [Fact]
    public void ReusedMeshInstancesShareAContentSignature()
    {
        var world = BuildWorld(GeometryProperties(0));
        var builder = new RekallAgeRuntimeRenderFrameBuilder();

        // The identity-keyed signature memo only pays off when the mesh instance
        // survives across frames; this is the property the geometry cache key
        // depends on.
        Assert.Equal(
            RekallAgeRuntimeGeometrySignature.For(Mesh(builder, world)),
            RekallAgeRuntimeGeometrySignature.For(Mesh(builder, world)));
    }
}
