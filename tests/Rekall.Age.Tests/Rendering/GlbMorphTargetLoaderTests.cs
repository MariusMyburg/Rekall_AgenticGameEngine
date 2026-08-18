using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class GlbMorphTargetLoaderTests
{
    [Fact]
    public async Task MetadataExposesOrderedNamesAndSeparateMeshAndNodeWeights()
    {
        var path = await WriteFixtureAsync();

        var metadata = await RekallAgeGlbMetadataReader.ReadAsync(path, CancellationToken.None);

        var mesh = Assert.Single(metadata!.Meshes);
        Assert.Equal(2, mesh.MorphTargetCount);
        Assert.Equal(["wide", "raised"], mesh.MorphTargetNames);
        Assert.Equal([0.25, -0.5], mesh.DefaultMorphWeights);
        var node = Assert.Single(metadata.Nodes);
        Assert.Equal([0.5, 0.75], node.MorphWeights);
        Assert.Equal(["POSITION", "NORMAL"], metadata.SupportedMorphTargetSemantics);
        Assert.Contains(metadata.MorphTargetLimitations, limitation => limitation.Contains("TANGENT", StringComparison.Ordinal));
        Assert.Contains(metadata.MorphTargetLimitations, limitation => limitation.Contains("weights animation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoaderUsesMeshDefaultsThenZerosWhenNodeAndMeshWeightsAreAbsent()
    {
        var meshDefaultBytes = MutateJson(root => ((JsonObject)((JsonArray)root["nodes"]!)[0]!).Remove("weights"));
        var meshDefault = Assert.Single(await LoadAsync(meshDefaultBytes));
        Assert.Equal([0.25f, -0.5f], meshDefault.DefaultMorphWeights);

        var zeroBytes = MutateJson(root =>
        {
            ((JsonObject)((JsonArray)root["nodes"]!)[0]!).Remove("weights");
            ((JsonObject)((JsonArray)root["meshes"]!)[0]!).Remove("weights");
        });
        var zeroDefault = Assert.Single(await LoadAsync(zeroBytes));
        Assert.Equal([0f, 0f], zeroDefault.DefaultMorphWeights);
    }

    [Fact]
    public async Task LoaderPreservesTransformedAlignedMorphTargetsAndNodeDefaults()
    {
        var path = await WriteFixtureAsync();

        var mesh = Assert.Single(await new RekallAgeGlbMeshLoader()
            .LoadAsync("morph-asset", path, CancellationToken.None));

        Assert.Equal([0.5f, 0.75f], mesh.DefaultMorphWeights);
        Assert.Equal(["wide", "raised"], mesh.MorphTargets.Select(target => target.Name));
        Assert.All(mesh.MorphTargets, target =>
        {
            Assert.Equal(mesh.Vertices.Count, target.PositionDeltas.Count);
            Assert.Equal(mesh.Vertices.Count, target.NormalDeltas.Count);
        });
        AssertVector(new Vector3(0, 2, 0), mesh.MorphTargets[0].PositionDeltas[0]);
        AssertVector(new Vector3(-2, 0, 0), mesh.MorphTargets[1].PositionDeltas[0]);
        AssertVector(new Vector3(-1, 0, 0), mesh.MorphTargets[0].NormalDeltas[0]);
    }

    [Fact]
    public async Task LoaderRejectsExcessiveTargetCountBeforeAccessorAllocation()
    {
        var bytes = MutateJson(root =>
        {
            var mesh = (JsonObject)((JsonArray)root["meshes"]!)[0]!;
            var primitive = (JsonObject)((JsonArray)mesh["primitives"]!)[0]!;
            var targets = (JsonArray)primitive["targets"]!;
            var template = targets[0]!.DeepClone();
            while (targets.Count < 65) targets.Add(template.DeepClone());
            mesh["weights"] = new JsonArray(Enumerable.Repeat(0, 65).Select(value => JsonValue.Create(value)).ToArray());
            ((JsonObject)mesh["extras"]!)["targetNames"] = new JsonArray(
                Enumerable.Range(0, 65).Select(index => JsonValue.Create($"target-{index}")).ToArray());
        });

        var exception = await AssertInvalidAsync(bytes);
        Assert.Contains("1 to 64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoaderRejectsExcessiveDeclaredDeltaVectorsBeforeReadingPayload()
    {
        var bytes = MutateJson(root =>
        {
            var accessors = (JsonArray)root["accessors"]!;
            for (var index = 2; index <= 5; index++) ((JsonObject)accessors[index]!)["count"] = 4_194_305;
        });

        var exception = await AssertInvalidAsync(bytes);
        Assert.Contains("4194304", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("sparse")]
    [InlineData("quantized")]
    public async Task LoaderRejectsInvalidMorphAccessorContracts(string mutation)
    {
        var bytes = MutateJson(root =>
        {
            var accessor = (JsonObject)((JsonArray)root["accessors"]!)[2]!;
            if (mutation == "count") accessor["count"] = 2;
            if (mutation == "sparse") accessor["sparse"] = new JsonObject { ["count"] = 1 };
            if (mutation == "quantized") accessor["componentType"] = 5123;
        });

        var exception = await AssertInvalidAsync(bytes);
        Assert.Contains("non-sparse float VEC3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoaderRejectsUnsupportedTangentSemantic()
    {
        var bytes = MutateJson(root =>
        {
            var mesh = (JsonObject)((JsonArray)root["meshes"]!)[0]!;
            var primitive = (JsonObject)((JsonArray)mesh["primitives"]!)[0]!;
            ((JsonObject)((JsonArray)primitive["targets"]!)[0]!)["TANGENT"] = 2;
        });

        var exception = await AssertInvalidAsync(bytes);
        Assert.Contains("TANGENT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoaderRejectsNonFiniteAndExcessiveDeltaPayloads()
    {
        foreach (var value in new[] { float.NaN, 1_000_001f })
        {
            var bytes = GlbTestMeshFactory.CreateMorphTriangleGlb();
            var binaryOffset = FindBinaryOffset(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(binaryOffset + 72, 4),
                BitConverter.SingleToInt32Bits(value));

            var exception = await AssertInvalidAsync(bytes);
            Assert.Contains("non-finite or exceeds", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LoaderAndMetadataRejectBadDefaultsNamesAndCompoundLayouts()
    {
        var cases = new[]
        {
            MutateJson(root => ((JsonObject)((JsonArray)root["meshes"]!)[0]!)["weights"] = new JsonArray(0.5)),
            MutateJson(root => ((JsonArray)((JsonObject)((JsonObject)((JsonArray)root["meshes"]!)[0]!)["extras"]!)["targetNames"]!)[0] = new string('x', 129)),
            MutateJson(root => ((JsonObject)((JsonArray)root["nodes"]!)[0]!)["weights"] = new JsonArray(0.5)),
            MutateJson(root =>
            {
                var meshes = (JsonArray)root["meshes"]!;
                var clone = (JsonObject)meshes[0]!.DeepClone();
                ((JsonArray)((JsonObject)clone["extras"]!)["targetNames"]!)[0] = "other";
                meshes.Add(clone);
            })
        };

        foreach (var bytes in cases)
        {
            _ = await AssertInvalidAsync(bytes);
            _ = await AssertMetadataInvalidAsync(bytes);
        }
    }

    private static async Task<string> WriteFixtureAsync()
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "morph.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateMorphTriangleGlb());
        return path;
    }

    private static async Task<InvalidDataException> AssertInvalidAsync(byte[] bytes)
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "invalid-morph.glb");
        await File.WriteAllBytesAsync(path, bytes);
        return await Assert.ThrowsAsync<InvalidDataException>(() => new RekallAgeGlbMeshLoader()
            .LoadAsync("invalid", path, CancellationToken.None).AsTask());
    }

    private static async Task<InvalidDataException> AssertMetadataInvalidAsync(byte[] bytes)
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "invalid-morph-metadata.glb");
        await File.WriteAllBytesAsync(path, bytes);
        return await Assert.ThrowsAsync<InvalidDataException>(() => RekallAgeGlbMetadataReader
            .ReadAsync(path, CancellationToken.None).AsTask());
    }

    private static async Task<IReadOnlyList<RekallAgeVulkanSceneMesh>> LoadAsync(byte[] bytes)
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "morph.glb");
        await File.WriteAllBytesAsync(path, bytes);
        return await new RekallAgeGlbMeshLoader().LoadAsync("morph", path, CancellationToken.None);
    }

    private static byte[] MutateJson(Action<JsonObject> mutate)
    {
        var source = GlbTestMeshFactory.CreateMorphTriangleGlb();
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12, 4)));
        var root = JsonNode.Parse(source.AsSpan(20, jsonLength))!.AsObject();
        mutate(root);
        var binaryOffset = 20 + jsonLength;
        var binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binaryOffset, 4)));
        var binary = source.AsSpan(binaryOffset + 8, binaryLength).ToArray();
        return BuildGlb(Encoding.UTF8.GetBytes(root.ToJsonString()), binary);
    }

    private static int FindBinaryOffset(byte[] glb)
    {
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12, 4)));
        return 20 + jsonLength + 8;
    }

    private static byte[] BuildGlb(byte[] json, byte[] binary)
    {
        static byte[] Pad(byte[] source)
        {
            var length = (source.Length + 3) & ~3;
            Array.Resize(ref source, length);
            return source;
        }
        var originalJsonLength = json.Length;
        var jsonBytes = Pad(json);
        if (jsonBytes.Length > originalJsonLength) Array.Fill(jsonBytes, (byte)0x20, originalJsonLength, jsonBytes.Length - originalJsonLength);
        var binaryBytes = Pad(binary);
        var output = new byte[12 + 8 + jsonBytes.Length + 8 + binaryBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), checked((uint)output.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), checked((uint)jsonBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16, 4), 0x4E4F534A);
        jsonBytes.CopyTo(output, 20);
        var offset = 20 + jsonBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, 4), checked((uint)binaryBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset + 4, 4), 0x004E4942);
        binaryBytes.CopyTo(output, offset + 8);
        return output;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 4);
        Assert.Equal(expected.Y, actual.Y, precision: 4);
        Assert.Equal(expected.Z, actual.Z, precision: 4);
    }
}
