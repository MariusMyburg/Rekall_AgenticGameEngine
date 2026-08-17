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
}
