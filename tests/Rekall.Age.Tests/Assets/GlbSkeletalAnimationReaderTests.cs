using System.Numerics;
using Rekall.Age.Assets;
using Rekall.Age.Tests.Rendering;

namespace Rekall.Age.Tests.Assets;

public sealed class GlbSkeletalAnimationReaderTests
{
    [Fact]
    public async Task ReaderLoadsBoundedSkinHierarchyAndAnimationChannels()
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "animated.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateSingleJointAnimatedGlb());

        var asset = await RekallAgeGlbSkeletalAnimationReader.ReadAsync(path, CancellationToken.None);

        Assert.Equal(2, asset.Nodes.Count);
        Assert.Equal(-1, asset.Nodes[0].ParentIndex);
        Assert.Equal(0, asset.Nodes[1].ParentIndex);
        var skin = Assert.Single(asset.Skins);
        Assert.Equal("Rig", skin.Name);
        Assert.Equal([1], skin.JointNodeIndexes);
        Assert.Equal(Matrix4x4.Identity, Assert.Single(skin.InverseBindMatrices));
        var animation = Assert.Single(asset.Animations);
        Assert.Equal("Lift", animation.Name);
        Assert.Equal(1, animation.DurationSeconds, precision: 4);
        var channel = Assert.Single(animation.Channels);
        Assert.Equal(1, channel.NodeIndex);
        Assert.Equal("translation", channel.Path);
        Assert.Equal("linear", channel.Interpolation);
        Assert.Equal([0f, 1f], channel.Times);
        Assert.Equal(new Vector4(0, 0, 0, 0), channel.Values[0]);
        Assert.Equal(new Vector4(0, 2, 0, 0), channel.Values[1]);
    }

    [Fact]
    public async Task ReaderDecodesCubicSplineTangentValueTriplets()
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "cubic-animated.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb());

        var asset = await RekallAgeGlbSkeletalAnimationReader.ReadAsync(path, CancellationToken.None);

        var channel = Assert.Single(Assert.Single(asset.Animations).Channels);
        Assert.Equal("cubicspline", channel.Interpolation);
        Assert.Equal([0f, 1f], channel.Times);
        Assert.Equal([new Vector4(0, 0, 0, 0), new Vector4(0, 2, 0, 0)], channel.Values);
        Assert.Equal([new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)], channel.InTangents);
        Assert.Equal([new Vector4(0, 4, 0, 0), new Vector4(0, 0, 0, 0)], channel.OutTangents);
    }

    [Fact]
    public async Task ReaderRejectsCubicSplineWithNonTripledOutputCount()
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "malformed-cubic.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb(outputRecordCount: 5));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await RekallAgeGlbSkeletalAnimationReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("exactly three times", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderRejectsUnknownAnimationInterpolation()
    {
        var path = Path.Combine(TestPaths.CreateTempDirectory(), "unknown-interpolation.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb("BEZIER"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await RekallAgeGlbSkeletalAnimationReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("unsupported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bezier", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderRejectsNonFiniteCubicTangentsAndDuplicateTimes()
    {
        var root = TestPaths.CreateTempDirectory();
        var nonFinitePath = Path.Combine(root, "non-finite-cubic.glb");
        await File.WriteAllBytesAsync(
            nonFinitePath,
            GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb(nonFiniteOutput: true));
        var nonFinite = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await RekallAgeGlbSkeletalAnimationReader.ReadAsync(nonFinitePath, CancellationToken.None));
        Assert.Contains("finite", nonFinite.Message, StringComparison.Ordinal);

        var duplicatePath = Path.Combine(root, "duplicate-time-cubic.glb");
        await File.WriteAllBytesAsync(
            duplicatePath,
            GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb(duplicateTimes: true));
        var duplicate = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await RekallAgeGlbSkeletalAnimationReader.ReadAsync(duplicatePath, CancellationToken.None));
        Assert.Contains("strictly increasing", duplicate.Message, StringComparison.Ordinal);
    }
}
