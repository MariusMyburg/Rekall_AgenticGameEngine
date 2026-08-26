using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Modeling;

public sealed class RigAuthoringTests
{
    [Fact]
    public async Task RigStorePersistsStableNamedHierarchyAndRuntimeResolverEvaluatesPoseDeltas()
    {
        var root = TestPaths.CreateTempDirectory();
        var rig = HumanoidRig();
        var store = new RekallAgeRigAssetStore();
        await store.SaveAsync(root, rig, CancellationToken.None);

        var loaded = await store.LoadAsync(root, "rig.warden", CancellationToken.None);
        var component = new RekallAgeRuntimeComponent(
            "Rekall.RigPose",
            new JsonObject
            {
                ["assetId"] = "rig.warden",
                ["skinIndex"] = 0,
                ["jointDeltas"] = new JsonArray(
                    new JsonObject
                    {
                        ["jointId"] = "chest",
                        ["matrix"] = JsonMatrix(
                            0, 1, 0, 0,
                            -1, 0, 0, 0,
                            0, 0, 1, 0,
                            0, 0, 0, 1)
                    })
            });

        var resolution = new RekallAgeRigPoseResolver().Resolve(root, component);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "Modeling", "Rigs", "rig.warden.age.rig.json"), store.GetRigPath(root, "rig.warden"));
        Assert.Equal(["root", "chest"], loaded.Joints.Select(joint => joint.JointId));
        Assert.Null(resolution.IssueCode);
        var skin = Assert.IsType<RekallAgeRuntimeViewportSkin>(resolution.Skin);
        Assert.Equal(0, skin.SkinIndex);
        Assert.Equal(2, skin.JointMatrices.Count);
        var transformed = Vector3.Transform(new Vector3(1, 1, 0), ToMatrix(skin.JointMatrices[1]));
        Assert.Equal(0, transformed.X, precision: 5);
        Assert.Equal(2, transformed.Y, precision: 5);
        Assert.Equal(0, transformed.Z, precision: 5);
    }

    [Fact]
    public void RigValidatorRejectsForwardParentsDuplicateJointIdsAndNonInvertibleBindTransforms()
    {
        var rig = RekallAgeRigAsset.Create(
            "rig.invalid",
            "Invalid Rig",
            [
                new("root", "Root", 1, Identity()),
                new("root", "Chest", null, Values(
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0))
            ]);

        var report = new RekallAgeRigValidator().Validate(rig);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_RIG_JOINT_ID_DUPLICATE");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_RIG_PARENT_ORDER_INVALID");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_RIG_BIND_MATRIX_NON_INVERTIBLE");
    }

    [Fact]
    public async Task RigStoreRejectsARequestedAssetIdThatDoesNotMatchTheDocument()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeRigAssetStore();
        await store.SaveAsync(root, HumanoidRig(), CancellationToken.None);
        var path = store.GetRigPath(root, "rig.warden");
        var text = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, text.Replace("rig.warden", "rig.other", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(root, "rig.warden", CancellationToken.None));

        Assert.Contains("REKALL_RIG_ASSET_ID_MISMATCH", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeFrameResolvesNativeRigPoseAndReportsMissingRigAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeRigAssetStore().SaveAsync(root, HumanoidRig(), CancellationToken.None);
        var valid = RiggedEntity("Rigged", "rig.warden");
        var missing = RiggedEntity("Missing", "rig.missing");
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(valid)
                .AddEntity(missing)) with { ProjectRoot = root };

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        var rigged = Assert.Single(frame.Renderables, item => item.EntityName == "Rigged");
        Assert.Equal(2, Assert.IsType<RekallAgeRuntimeViewportSkin>(rigged.Skin).JointMatrices.Count);
        Assert.Null(Assert.Single(frame.Renderables, item => item.EntityName == "Missing").Skin);
        Assert.Contains(frame.Observations, item =>
            item.Code == "REKALL_RIG_ASSET_NOT_FOUND" && item.Target == "Missing");
    }

    [Fact]
    public async Task RuntimeResolverAcceptsFiniteSinglePrecisionPoseMatricesFromModuleCode()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeRigAssetStore().SaveAsync(root, HumanoidRig(), CancellationToken.None);
        var rotation = Matrix4x4.CreateRotationZ(0.25f);
        var component = new RekallAgeRuntimeComponent("Rekall.RigPose", new JsonObject
        {
            ["assetId"] = "rig.warden",
            ["skinIndex"] = 0,
            ["jointDeltas"] = new JsonArray(new JsonObject
            {
                ["jointId"] = "chest",
                ["matrix"] = new JsonArray(
                    rotation.M11, rotation.M12, rotation.M13, rotation.M14,
                    rotation.M21, rotation.M22, rotation.M23, rotation.M24,
                    rotation.M31, rotation.M32, rotation.M33, rotation.M34,
                    rotation.M41, rotation.M42, rotation.M43, rotation.M44)
            })
        });

        var resolution = new RekallAgeRigPoseResolver().Resolve(root, component);

        Assert.Null(resolution.IssueCode);
        Assert.Equal(2, Assert.IsType<RekallAgeRuntimeViewportSkin>(resolution.Skin).JointMatrices.Count);
    }

    [Fact]
    public void RigEvaluatorResolvesJointIdsConsistentlyAcrossDictionaryComparers()
    {
        var evaluated = new RekallAgeRigEvaluator().Evaluate(
            HumanoidRig(),
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal)
            {
                ["CHEST"] = Values(
                    0, 1, 0, 0,
                    -1, 0, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1)
            });

        var transformed = Vector3.Transform(new Vector3(1, 1, 0), ToMatrix(evaluated.JointMatrices[1]));
        Assert.Equal(0, transformed.X, precision: 5);
        Assert.Equal(2, transformed.Y, precision: 5);
    }

    [Fact]
    public void RigEvaluatorPublishesPoseGlobalsSeparatelyFromSkinMatrices()
    {
        var evaluated = new RekallAgeRigEvaluator().Evaluate(
            HumanoidRig(),
            new Dictionary<string, IReadOnlyList<double>>
            {
                ["chest"] = Values(
                    0, 1, 0, 0,
                    -1, 0, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1)
            });

        Assert.Equal(2, evaluated.PoseGlobalMatrices.Count);
        var chestPose = ToMatrix(evaluated.PoseGlobalMatrices[1]);
        Assert.Equal(1, chestPose.M12, precision: 5);
        Assert.Equal(1, chestPose.M42, precision: 5);
        Assert.NotEqual(
            string.Join(",", evaluated.JointMatrices[1]),
            string.Join(",", evaluated.PoseGlobalMatrices[1]));
    }

    private static RekallAgeRigAsset HumanoidRig() => RekallAgeRigAsset.Create(
        "rig.warden",
        "Warden Rig",
        [
            new("root", "Root", null, Identity()),
            new("chest", "Chest", 0, Values(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 1, 0, 1))
        ]);

    private static RekallAgeEntityDocument RiggedEntity(string name, string assetId) =>
        RekallAgeEntityDocument.Create(name, ["rigged"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.RigPose", new JsonObject
            {
                ["assetId"] = assetId,
                ["jointDeltas"] = new JsonArray()
            }));

    private static IReadOnlyList<double> Identity() =>
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static IReadOnlyList<double> Values(params double[] values) => values;

    private static JsonArray JsonMatrix(params double[] values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static Matrix4x4 ToMatrix(IReadOnlyList<double> value) => new(
        (float)value[0], (float)value[1], (float)value[2], (float)value[3],
        (float)value[4], (float)value[5], (float)value[6], (float)value[7],
        (float)value[8], (float)value[9], (float)value[10], (float)value[11],
        (float)value[12], (float)value[13], (float)value[14], (float)value[15]);
}
