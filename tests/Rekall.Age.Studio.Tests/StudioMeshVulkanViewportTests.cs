using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioMeshVulkanViewportTests
{
    [Theory]
    [InlineData(RekallAgeGeometryDomain.Point, 1UL)]
    [InlineData(RekallAgeGeometryDomain.Edge, 11UL)]
    [InlineData(RekallAgeGeometryDomain.Face, 21UL)]
    [InlineData(RekallAgeGeometryDomain.Corner, 31UL)]
    public void BuilderEmitsVulkanGeometryAndExactDomainPicking(RekallAgeGeometryDomain domain, ulong expectedId)
    {
        var result = new RekallAgeStudioMeshVulkanFrameBuilder().Build(
            Quad(), domain, [], 640, 360, preview: false,
            RekallAgeStudioViewportCamera.Identity,
            RekallAgeStudioViewportRenderStyle.SmoothShaded);

        var mesh = Assert.Single(result.Frame.Renderables, item => item.EntityId == "__studio_edit_mesh");
        Assert.Equal(4, mesh.GeometryMesh!.Vertices.Count);
        Assert.Equal(6, mesh.GeometryMesh.Indices.Count);
        var center = result.Interaction.ElementCenters[(domain, expectedId)];
        Assert.Equal(expectedId, result.Interaction.Pick(domain, center.X, center.Y));
    }

    [Theory]
    [InlineData(RekallAgeGeometryDomain.Point, "__studio_edit_selected_point_1")]
    [InlineData(RekallAgeGeometryDomain.Edge, "__studio_edit_selected_edge_11")]
    [InlineData(RekallAgeGeometryDomain.Face, "__studio_edit_selected_face_21")]
    [InlineData(RekallAgeGeometryDomain.Corner, "__studio_edit_selected_corner_31")]
    public void BuilderEmitsNativeSelectionOverlaysForEveryEditDomain(
        RekallAgeGeometryDomain domain,
        string expectedOverlayId)
    {
        var selected = domain switch
        {
            RekallAgeGeometryDomain.Point => 1UL,
            RekallAgeGeometryDomain.Edge => 11UL,
            RekallAgeGeometryDomain.Face => 21UL,
            _ => 31UL
        };

        var result = new RekallAgeStudioMeshVulkanFrameBuilder().Build(
            Quad(), domain, [selected], 640, 360, preview: true,
            RekallAgeStudioViewportCamera.Identity,
            RekallAgeStudioViewportRenderStyle.Wireframe);

        var overlay = Assert.Single(result.Frame.Renderables, item => item.EntityId == expectedOverlayId);
        Assert.Equal("studio-editor", overlay.Layer);
        Assert.Equal("#ff9f32", overlay.EmissiveColor);
        Assert.Contains(result.Frame.Renderables, item => item.EntityId == "__studio_grid");
        Assert.True(result.Interaction.IsPreview);
    }

    [Fact]
    public void SelectedPointsExposeNativeAxisGizmoAndCameraChangesProjection()
    {
        var builder = new RekallAgeStudioMeshVulkanFrameBuilder();
        var initial = builder.Build(Quad(), RekallAgeGeometryDomain.Point, [1], 640, 360, false,
            RekallAgeStudioViewportCamera.Identity, RekallAgeStudioViewportRenderStyle.SmoothShaded);
        var orbited = builder.Build(Quad(), RekallAgeGeometryDomain.Point, [1], 640, 360, false,
            RekallAgeStudioViewportCamera.Identity with { Yaw = 0.7, Pitch = 0.2 },
            RekallAgeStudioViewportRenderStyle.SmoothShaded);

        Assert.Equal(3, initial.Frame.Renderables.Count(item => item.EntityId.StartsWith("__studio_mesh_gizmo_", StringComparison.Ordinal)));
        Assert.NotEqual(
            initial.Interaction.ElementCenters[(RekallAgeGeometryDomain.Point, 2)],
            orbited.Interaction.ElementCenters[(RekallAgeGeometryDomain.Point, 2)]);
    }

    [Fact]
    public void IdentityCameraUsesThreeQuarterFramingInsteadOfCollapsingTheGroundPlane()
    {
        var ground = RekallAgeMeshAsset.Create("ground", "Ground",
            new(
                PointIds: [1, 2, 3, 4], Positions: [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1)],
                EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
                FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
                CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));

        var result = new RekallAgeStudioMeshVulkanFrameBuilder().Build(
            ground, RekallAgeGeometryDomain.Point, [], 640, 360, false,
            RekallAgeStudioViewportCamera.Identity, RekallAgeStudioViewportRenderStyle.SmoothShaded);
        var projected = ground.Topology.PointIds
            .Select(id => result.Interaction.ElementCenters[(RekallAgeGeometryDomain.Point, id)])
            .ToArray();

        Assert.True(projected.Max(point => point.Y) - projected.Min(point => point.Y) > 20);
        Assert.True(projected.Max(point => point.X) - projected.Min(point => point.X) > 20);
    }

    [Fact]
    public async Task PreviewSessionSubmitsBuilderFrameAtRequestedStyleAndDimensions()
    {
        var presenter = new RecordingPresenter();
        await using var session = new RekallAgeStudioMeshVulkanPreviewSession(presenter);
        session.SetRenderStyle(RekallAgeStudioViewportRenderStyle.Clay);

        var presented = await session.PresentAsync(
            "C:\\Project", Quad(), RekallAgeGeometryDomain.Face, [21], 800, 450, false,
            RekallAgeStudioViewportCamera.Identity, CancellationToken.None);

        Assert.Equal((800, 450), (presenter.Frame!.Width, presenter.Frame.Height));
        Assert.Equal(RekallAgeStudioViewportRenderStyle.Clay, presenter.Context!.RenderStyle);
        Assert.Equal(21UL, presented.Interaction.Pick(
            RekallAgeGeometryDomain.Face,
            presented.Interaction.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].X,
            presented.Interaction.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].Y));
    }

    [Fact]
    public async Task PreviewSessionPresentsAnEmptyNativeGridAndViewModelOwnsItsDisposal()
    {
        var presenter = new RecordingPresenter();
        var session = new RekallAgeStudioMeshVulkanPreviewSession(presenter);
        await using var viewModel = new RekallAgeStudioViewModel(
            new Rekall.Age.Editor.RekallAgeWorkbenchSession(Rekall.Age.Workflows.RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());
        viewModel.AttachMeshVulkanPreviewSession(session);

        await viewModel.PresentMeshViewportAtHostSizeAsync(new(640, 360, 640, 360, true));

        Assert.Contains(presenter.Frame!.Renderables, item => item.EntityId == "__studio_grid");
        await viewModel.DisposeAsync();
        Assert.True(presenter.IsDisposalComplete);
    }

    [Fact]
    public async Task MeshOrbitPanZoomAndStyleChangesRePresentTheAttachedNativeViewport()
    {
        var presenter = new RecordingPresenter();
        var session = new RekallAgeStudioMeshVulkanPreviewSession(presenter);
        await using var viewModel = new RekallAgeStudioViewModel(
            new Rekall.Age.Editor.RekallAgeWorkbenchSession(Rekall.Age.Workflows.RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());
        viewModel.AttachMeshVulkanPreviewSession(session);
        await viewModel.PresentMeshViewportAtHostSizeAsync(new(640, 360, 640, 360, true));
        var camera = presenter.Frame!.ActiveCamera;

        viewModel.OrbitMeshViewport(0.2, 0.1);
        await WaitForAsync(() => presenter.Frame!.ActiveCamera != camera);
        camera = presenter.Frame!.ActiveCamera;
        viewModel.PanMeshViewport(30, -15);
        await WaitForAsync(() => presenter.Frame!.ActiveCamera != camera);
        camera = presenter.Frame!.ActiveCamera;
        viewModel.ZoomMeshViewport(1.4);
        await WaitForAsync(() => presenter.Frame!.ActiveCamera != camera);
        viewModel.MeshViewportRenderStyle = "Clay";
        await WaitForAsync(() => presenter.Context!.RenderStyle == RekallAgeStudioViewportRenderStyle.Clay);

        Assert.Equal(RekallAgeStudioViewportRenderStyle.Clay, presenter.Context!.RenderStyle);
    }

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4], Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));

    private sealed class RecordingPresenter : IRekallAgeStudioViewportPresenter
    {
        public RekallAgeStudioViewportMetrics Metrics => new(800, 450, 800, 450, true);
        public bool IsDisposalComplete { get; private set; }
        public RekallAgeRuntimeViewportFrame? Frame { get; private set; }
        public RekallAgeStudioPresentationContext? Context { get; private set; }
        public int PresentationCount { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            RekallAgeStudioPresentationContext context,
            CancellationToken cancellationToken)
        {
            Frame = frame;
            Context = context;
            PresentationCount++;
            return ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(frame, "test-gpu"));
        }

        public ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask InvalidateShadersAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.CompletedTask;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class EmptyModel : Rekall.Age.Agent.LanguageModels.IRekallAgeLanguageModelClient
    {
        public string ProviderId => "test";
        public ValueTask<IReadOnlyList<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelInfo>>([]);
        public ValueTask<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelResponse> ChatAsync(
            Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelResponse(
                ProviderId, request.Model, string.Empty, string.Empty, [], "stop",
                new Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelUsage(0, 0, 0)));
    }
}
