# Studio 3D Viewport Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Studio's mesh-editing viewport a real orbit/pan/zoom/ortho-perspective camera, replacing today's fixed axonometric projection, without disturbing existing picking/gizmo/preview behavior.

**Architecture:** `RekallAgeStudioMeshViewportRenderer.Project` becomes camera-aware: it rotates the point by an additional yaw/pitch delta relative to the existing fixed isometric basis (so the identity camera reproduces today's output exactly), then projects onto that basis with optional perspective divide. Camera state lives on `RekallAgeStudioViewModel` per open mesh session; mouse handlers in `ModelingWorkspace.xaml.cs` translate middle-drag/scroll/shift-drag into camera mutator calls, following the exact pattern already used for the transform gizmo drag.

**Tech Stack:** C# / WPF (`Rekall.Age.Studio`), xUnit (`Rekall.Age.Studio.Tests`)

**Spec:** `docs/superpowers/specs/2026-08-26-studio-3d-viewport-navigation-design.md`

## Global Constraints

- The identity/default camera (yaw=0, pitch=0, zoom=1, pan=(0,0), orthographic=true) must reproduce today's exact `Project` output bit-for-bit, so every existing `StudioMeshViewportTests`/`StudioViewportInteractionTests` test keeps passing unmodified.
- Left-mouse-button interaction (gizmo drag, element selection) must not change at all — orbit/pan only bind to middle-mouse and scroll.
- Camera state resets when a different mesh asset is opened; it persists across re-renders of the same mesh (parameter edits, selection changes).

---

### Task 1: Camera-aware `Project` with a regression-proven identity camera

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioMeshViewportRenderer.cs:189` (the `Project` method and its call sites)
- Test: `tests/Rekall.Age.Studio.Tests/StudioMeshViewportTests.cs`

**Interfaces:**
- Produces: `internal readonly record struct RekallAgeStudioViewportCamera(double Yaw, double Pitch, double Zoom, double PanX, double PanY, bool Orthographic)` with `public static RekallAgeStudioViewportCamera Identity { get; } = new(0, 0, 1, 0, 0, true);`
- Produces: `RekallAgeStudioMeshViewportRenderer.Project(RekallAgeGeometryVector3 point, RekallAgeStudioViewportCamera camera)` (was a zero-arg-camera static `Project(RekallAgeGeometryVector3)`).

- [ ] **Step 1: Write the failing identity-camera regression test**

```csharp
[Theory]
[InlineData(1, 0, 0)]
[InlineData(0, 1, 0)]
[InlineData(0, 0, 1)]
[InlineData(1.5, -2.25, 3.75)]
public void IdentityCameraReproducesLegacyAxonometricProjectionExactly(double x, double y, double z)
{
    var point = new RekallAgeGeometryVector3(x, y, z);
    var legacy = new Point((point.X - point.Z) / Math.Sqrt(2), (point.X + point.Z - 2 * point.Y) / Math.Sqrt(6));

    var projected = RekallAgeStudioMeshViewportRenderer.Project(point, RekallAgeStudioViewportCamera.Identity);

    Assert.Equal(legacy.X, projected.X, precision: 10);
    Assert.Equal(legacy.Y, projected.Y, precision: 10);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter FullyQualifiedName~IdentityCameraReproducesLegacyAxonometricProjectionExactly`
Expected: FAIL — `Project` does not yet accept a camera parameter (compile error), or (once stubbed) numeric mismatch.

- [ ] **Step 3: Implement the camera-aware projection**

Replace the existing `Project` method in `RekallAgeStudioMeshViewportRenderer.cs` with:

```csharp
internal static readonly RekallAgeGeometryVector3 DefaultRight = new(1 / Math.Sqrt(2), 0, -1 / Math.Sqrt(2));
internal static readonly RekallAgeGeometryVector3 DefaultUp = new(1 / Math.Sqrt(6), -2 / Math.Sqrt(6), 1 / Math.Sqrt(6));
internal static readonly RekallAgeGeometryVector3 DefaultForward = Cross(DefaultRight, DefaultUp);

public static Point Project(RekallAgeGeometryVector3 point, RekallAgeStudioViewportCamera camera)
{
    var (right, up, forward) = OrbitBasis(camera.Yaw, camera.Pitch);
    var x = Dot(point, right);
    var y = Dot(point, up);
    var depth = Dot(point, forward);
    var perspective = camera.Orthographic ? 1.0 : camera.Zoom / Math.Max(0.05, camera.Zoom - depth);
    return new Point(
        x * camera.Zoom * perspective + camera.PanX,
        y * camera.Zoom * perspective + camera.PanY);
}

private static (RekallAgeGeometryVector3 Right, RekallAgeGeometryVector3 Up, RekallAgeGeometryVector3 Forward) OrbitBasis(double yaw, double pitch)
{
    var right = RotateAroundUp(DefaultRight, yaw);
    var forward = RotateAroundUp(DefaultForward, yaw);
    var up = DefaultUp;
    if (pitch != 0)
    {
        up = RotateAroundAxis(up, right, pitch);
        forward = RotateAroundAxis(forward, right, pitch);
    }
    return (right, up, forward);
}

private static RekallAgeGeometryVector3 RotateAroundUp(RekallAgeGeometryVector3 v, double angle)
{
    var cos = Math.Cos(angle); var sin = Math.Sin(angle);
    var axis = new RekallAgeGeometryVector3(0, 1, 0);
    return RotateAroundAxis(v, axis, angle);
}

private static RekallAgeGeometryVector3 RotateAroundAxis(RekallAgeGeometryVector3 v, RekallAgeGeometryVector3 axis, double angle)
{
    // Rodrigues' rotation formula.
    var a = Normalize(axis);
    var cos = Math.Cos(angle); var sin = Math.Sin(angle);
    var dot = Dot(v, a);
    var cross = Cross(a, v);
    return new RekallAgeGeometryVector3(
        v.X * cos + cross.X * sin + a.X * dot * (1 - cos),
        v.Y * cos + cross.Y * sin + a.Y * dot * (1 - cos),
        v.Z * cos + cross.Z * sin + a.Z * dot * (1 - cos));
}

private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(
    a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 v)
{
    var length = Math.Sqrt(Dot(v, v));
    return length <= 1e-12 ? v : new(v.X / length, v.Y / length, v.Z / length);
}
```

Add the camera record in the same file, above the renderer class:

```csharp
internal readonly record struct RekallAgeStudioViewportCamera(double Yaw, double Pitch, double Zoom, double PanX, double PanY, bool Orthographic)
{
    public static RekallAgeStudioViewportCamera Identity { get; } = new(0, 0, 1, 0, 0, true);
}
```

Note `RotateAroundUp` rotates around world `(0,1,0)`, not the camera's own up — this is standard orbit-camera yaw (yaw always turns around the world vertical, independent of pitch), matching Blender's default orbit behavior.

Every internal call site that used the old zero-argument `Project(point)` (inside `Render`, and `GizmoAxis`) must now pass a camera; thread a `RekallAgeStudioViewportCamera camera` parameter through `Render(...)` (see Task 2) and through `GizmoAxis(axis, origin, camera)`.

- [ ] **Step 4: Run the test and confirm it passes**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter FullyQualifiedName~IdentityCameraReproducesLegacyAxonometricProjectionExactly`
Expected: PASS (4/4 theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Studio/RekallAgeStudioMeshViewportRenderer.cs tests/Rekall.Age.Studio.Tests/StudioMeshViewportTests.cs
git commit -m "feat: add camera-aware mesh viewport projection"
```

### Task 2: Thread the camera through `Render`/`ResolveTranslation` and prove orbit/pan/zoom/ortho behavior

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioMeshViewportRenderer.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioMeshViewportTests.cs`

**Interfaces:**
- Consumes: `RekallAgeStudioViewportCamera`, `Project(point, camera)` from Task 1.
- Produces: `RekallAgeStudioMeshViewportFrame` gains a `RekallAgeStudioViewportCamera Camera` field (last positional parameter, after `TransformGizmo`) so `ResolveTranslation` can re-derive axis directions with the exact camera the frame was rendered with. `Render(...)` gains a `RekallAgeStudioViewportCamera camera = default` parameter defaulting to `RekallAgeStudioViewportCamera.Identity` (via `RekallAgeStudioViewportCamera camera = default` — since the record's default `default` value has `Zoom = 0`, explicitly default it in the method body instead: `camera = camera == default ? RekallAgeStudioViewportCamera.Identity : camera;` is fragile: **use an explicit optional parameter `RekallAgeStudioViewportCamera? camera = null` and `var activeCamera = camera ?? RekallAgeStudioViewportCamera.Identity;` inside the method** so every existing call site (`Render(mesh, domain, ids, w, h, preview)`, no camera argument) keeps compiling unchanged and gets the identity camera.

- [ ] **Step 1: Write the failing orbit/pan/zoom/ortho tests**

```csharp
[Fact]
public void OrbitingNinetyDegreesYawMapsRightAxisOntoForwardAxis()
{
    var camera = RekallAgeStudioViewportCamera.Identity with { Yaw = Math.PI / 2 };
    var origin = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(0, 0, 0), camera);
    var alongOldRight = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(1, 0, -1), camera);
    var alongOldForward = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(1, 0, 1), camera);

    // After a 90-degree yaw the projected X spread from moving along the OLD forward axis
    // should now resemble what moving along the OLD right axis used to produce, proving the
    // view actually rotated rather than staying fixed.
    Assert.NotEqual(alongOldRight.X - origin.X, alongOldForward.X - origin.X, 6);
}

[Fact]
public void ZoomScalesProjectedSpread()
{
    var mesh = Quad();
    var wide = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { Zoom = 2 });
    var narrow = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { Zoom = 0.5 });

    double Spread(RekallAgeStudioMeshViewportFrame frame) => frame.Points.Max(p => p.Position.X) - frame.Points.Min(p => p.Position.X);
    Assert.True(Spread(wide) > Spread(narrow));
}

[Fact]
public void PanOffsetsProjectedCenter()
{
    var mesh = Quad();
    var panned = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { PanX = 50 });
    var unpanned = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity);

    var pannedCenterX = panned.Points.Average(p => p.Position.X);
    var unpannedCenterX = unpanned.Points.Average(p => p.Position.X);
    Assert.True(Math.Abs(pannedCenterX - unpannedCenterX) > 1);
}

[Fact]
public void OrthographicAndPerspectiveProduceDifferentDepthResponse()
{
    var deep = new RekallAgeGeometryVector3(0, 0, 5);
    var shallow = new RekallAgeGeometryVector3(0, 0, -5);
    var perspectiveCamera = RekallAgeStudioViewportCamera.Identity with { Orthographic = false, Zoom = 4 };

    var orthoDeep = RekallAgeStudioMeshViewportRenderer.Project(deep, RekallAgeStudioViewportCamera.Identity);
    var orthoShallow = RekallAgeStudioMeshViewportRenderer.Project(shallow, RekallAgeStudioViewportCamera.Identity);
    var perspDeep = RekallAgeStudioMeshViewportRenderer.Project(deep, perspectiveCamera);
    var perspShallow = RekallAgeStudioMeshViewportRenderer.Project(shallow, perspectiveCamera);

    var orthoRatio = (orthoDeep.X - orthoShallow.X);
    var perspRatio = (perspDeep.X - perspShallow.X);
    Assert.NotEqual(orthoRatio, perspRatio, 3);
}
```

- [ ] **Step 2: Run and verify all four fail**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~OrbitingNinetyDegreesYawMapsRightAxisOntoForwardAxis|FullyQualifiedName~ZoomScalesProjectedSpread|FullyQualifiedName~PanOffsetsProjectedCenter|FullyQualifiedName~OrthographicAndPerspectiveProduceDifferentDepthResponse"`
Expected: FAIL (compile errors — `Render` has no camera overload yet; `Camera` field missing).

- [ ] **Step 3: Thread the camera through `Render` and `ResolveTranslation`**

In `RekallAgeStudioMeshViewportRenderer.cs`:
- Change the `RekallAgeStudioMeshViewportFrame` record to add `RekallAgeStudioViewportCamera Camera` as its final field.
- Change `Render`'s signature to `public RekallAgeStudioMeshViewportFrame Render(RekallAgeMeshAsset mesh, RekallAgeGeometryDomain activeDomain, IReadOnlyCollection<ulong> selectedIds, int width, int height, bool preview, RekallAgeStudioViewportCamera? camera = null)`; compute `var activeCamera = camera ?? RekallAgeStudioViewportCamera.Identity;` as the first line, and pass `activeCamera` to every `Project(...)` call inside the method (the `raw = mesh.Topology.Positions.Select(Project)` line becomes `.Select(point => Project(point, activeCamera))`) and to `GizmoAxis(axis, origin, activeCamera)`. Pass `activeCamera` as the final constructor argument when building the returned frame.
- Change `GizmoAxis` to `private static RekallAgeStudioMeshTransformGizmoAxis GizmoAxis(RekallAgeStudioMeshTransformAxis axis, Point origin, RekallAgeStudioViewportCamera camera)`, using `Project(AxisVector(axis), camera)` internally.
- Change `ResolveTranslation` to read `frame.Camera` instead of calling the old parameterless `Project`: `var projectedAxis = Project(AxisVector(gesture.Axis), frame.Camera);`.

- [ ] **Step 4: Run the full mesh-viewport test file and confirm everything passes**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter FullyQualifiedName~StudioMeshViewportTests`
Expected: PASS, including the pre-existing tests (`PointSelectionExposesAxisGizmoAndConvertsScreenDragToMeshTranslation`, `MeshViewportAutoFramesAndPicksStablePointEdgeFaceAndCornerIds`, `MeshViewportRendersAProductionGridBehindEditableGeometry`) unmodified.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Studio/RekallAgeStudioMeshViewportRenderer.cs tests/Rekall.Age.Studio.Tests/StudioMeshViewportTests.cs
git commit -m "feat: thread orbit camera through mesh viewport rendering"
```

### Task 3: View-model camera state, mutators, and reset-on-mesh-switch

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs` (fields near `_meshViewportRenderer` at line 89; `RefreshMeshEditingState` at line 1925-1950; the mesh-open flow around line 1616-1625)
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: `RekallAgeStudioViewportCamera` from Task 1/2.
- Produces public methods on `RekallAgeStudioViewModel`:
  - `void OrbitMeshViewport(double deltaYaw, double deltaPitch)`
  - `void PanMeshViewport(double deltaX, double deltaY)`
  - `void ZoomMeshViewport(double factor)` (multiplies current zoom; factor > 1 zooms in)
  - `void ToggleMeshViewportProjection()`
  - `void FrameSelectedMeshViewport()` (resets pan to 0,0 and zoom to 1, keeping yaw/pitch)

- [ ] **Step 1: Write the failing view-model tests**

```csharp
[Fact]
public async Task OrbitingMeshViewportChangesRenderedImageWithoutMutatingMeshData()
{
    var (viewModel, projectRoot) = await OpenProjectWithMeshAsync();
    var before = viewModel.MeshViewportImage;

    viewModel.OrbitMeshViewport(0.4, 0.1);

    Assert.NotSame(before, viewModel.MeshViewportImage);
}

[Fact]
public async Task OpeningADifferentMeshAssetResetsCameraButReopeningSameMeshPreservesIt()
{
    var (viewModel, projectRoot) = await OpenProjectWithTwoMeshesAsync();
    viewModel.OrbitMeshViewport(0.6, 0.2);
    viewModel.ZoomMeshViewport(2);
    var orbitedImage = viewModel.MeshViewportImage;

    viewModel.SelectedMeshAssetId = viewModel.MeshAssetIds.Last();
    await viewModel.OpenMeshAssetCommand.ExecuteAsync(null);
    var otherMeshDefaultImage = viewModel.MeshViewportImage;

    viewModel.SelectedMeshAssetId = viewModel.MeshAssetIds.First();
    await viewModel.OpenMeshAssetCommand.ExecuteAsync(null);

    Assert.NotSame(orbitedImage, otherMeshDefaultImage);
    // Re-opening the first mesh after orbiting a different one starts that mesh fresh at identity,
    // not carrying over the second mesh's camera state.
}
```

Adapt `OpenProjectWithMeshAsync`/`OpenProjectWithTwoMeshesAsync` to whatever existing helper `StudioViewModelTests.cs` already uses to stand up a project + open a mesh asset (search the file for the existing pattern used by mesh-editing tests, e.g. around any test that already calls `OpenMeshAssetCommand`, and reuse that setup verbatim rather than inventing a new one).

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~OrbitingMeshViewportChangesRenderedImageWithoutMutatingMeshData|FullyQualifiedName~OpeningADifferentMeshAssetResetsCameraButReopeningSameMeshPreservesIt"`
Expected: FAIL — `OrbitMeshViewport` etc. do not exist yet.

- [ ] **Step 3: Add camera state and mutators to the view model**

Near the `_meshViewportRenderer` field (line 89), add:

```csharp
private RekallAgeStudioViewportCamera _meshViewportCamera = RekallAgeStudioViewportCamera.Identity;
```

Add public methods (near the other `*MeshViewport*` methods, e.g. after `CancelMeshViewportTransform` around line 1042):

```csharp
public void OrbitMeshViewport(double deltaYaw, double deltaPitch)
{
    _meshViewportCamera = _meshViewportCamera with
    {
        Yaw = _meshViewportCamera.Yaw + deltaYaw,
        Pitch = Math.Clamp(_meshViewportCamera.Pitch + deltaPitch, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01)
    };
    RefreshMeshEditingState();
}

public void PanMeshViewport(double deltaX, double deltaY)
{
    _meshViewportCamera = _meshViewportCamera with
    {
        PanX = _meshViewportCamera.PanX + deltaX,
        PanY = _meshViewportCamera.PanY + deltaY
    };
    RefreshMeshEditingState();
}

public void ZoomMeshViewport(double factor)
{
    if (!double.IsFinite(factor) || factor <= 0) return;
    _meshViewportCamera = _meshViewportCamera with
    {
        Zoom = Math.Clamp(_meshViewportCamera.Zoom * factor, 0.05, 50)
    };
    RefreshMeshEditingState();
}

public void ToggleMeshViewportProjection()
{
    _meshViewportCamera = _meshViewportCamera with { Orthographic = !_meshViewportCamera.Orthographic };
    RefreshMeshEditingState();
}

public void FrameSelectedMeshViewport()
{
    _meshViewportCamera = _meshViewportCamera with { PanX = 0, PanY = 0, Zoom = 1 };
    RefreshMeshEditingState();
}
```

Update the `Render` call inside `RefreshMeshEditingState` (line 1947) to pass the camera:

```csharp
_meshViewportFrame = _meshViewportRenderer.Render(mesh, MeshEditDomain, _modeling.SelectedElementIds, 640, 360, _modeling.Preview is not null, _meshViewportCamera);
```

Reset the camera at the point the mesh-open flow begins (immediately before or after the existing `await _modeling.OpenAsync(...)` call around line 1622): `_meshViewportCamera = RekallAgeStudioViewportCamera.Identity;`.

- [ ] **Step 4: Run the view-model tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~OrbitingMeshViewportChangesRenderedImageWithoutMutatingMeshData|FullyQualifiedName~OpeningADifferentMeshAssetResetsCameraButReopeningSameMeshPreservesIt"`
Expected: PASS.

- [ ] **Step 5: Run the full Studio test project to check for regressions**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj`
Expected: PASS, same total count as before plus the new tests.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs
git commit -m "feat: add orbit/pan/zoom/projection camera state to Studio view model"
```

### Task 4: Wire mouse/keyboard input and a Frame-Selected/ortho-toggle UI

**Files:**
- Modify: `src/Rekall.Age.Studio/ModelingWorkspace.xaml.cs`
- Modify: `src/Rekall.Age.Studio/ModelingWorkspace.xaml` (around the `MeshViewportImage` `<Image>` element, line 70, and the mesh-viewport toolbar buttons near lines 87-95)

**Interfaces:**
- Consumes: `OrbitMeshViewport`, `PanMeshViewport`, `ZoomMeshViewport`, `ToggleMeshViewportProjection`, `FrameSelectedMeshViewport` from Task 3.

- [ ] **Step 1: Add middle-drag orbit/pan and scroll-zoom handlers**

In `ModelingWorkspace.xaml.cs`, add fields and handlers alongside the existing `_dragging` field and `OnMeshMouseDown`/`OnMeshMouseMove`/`OnMeshMouseUp`:

```csharp
private bool _orbiting;
private bool _panning;
private System.Windows.Point _lastNavigationPoint;

private void OnMeshViewportMouseDown(object sender, MouseButtonEventArgs e)
{
    if (ViewModel is null || sender is not Image image || e.ChangedButton != MouseButton.Middle) return;
    _lastNavigationPoint = e.GetPosition(image);
    _panning = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
    _orbiting = !_panning;
    image.CaptureMouse();
    e.Handled = true;
}

private void OnMeshViewportMouseMoveForNavigation(object sender, MouseEventArgs e)
{
    if (ViewModel is null || sender is not Image image || (!_orbiting && !_panning)) return;
    var point = e.GetPosition(image);
    var delta = point - _lastNavigationPoint;
    _lastNavigationPoint = point;
    if (_orbiting) ViewModel.OrbitMeshViewport(delta.X * 0.01, delta.Y * 0.01);
    else ViewModel.PanMeshViewport(delta.X, delta.Y);
    e.Handled = true;
}

private void OnMeshViewportMouseUpForNavigation(object sender, MouseButtonEventArgs e)
{
    if (sender is not Image image || e.ChangedButton != MouseButton.Middle) return;
    _orbiting = false;
    _panning = false;
    image.ReleaseMouseCapture();
    e.Handled = true;
}

private void OnMeshViewportWheel(object sender, MouseWheelEventArgs e)
{
    if (ViewModel is null) return;
    ViewModel.ZoomMeshViewport(e.Delta > 0 ? 1.1 : 1 / 1.1);
    e.Handled = true;
}
```

`OnMeshMouseMove` (the existing gizmo-drag handler) must ignore the event while `_orbiting || _panning` is true — add `if (_orbiting || _panning) return;` as its first line so navigation and gizmo dragging can never fire on the same mouse-move.

- [ ] **Step 2: Wire the new handlers and toolbar buttons in XAML**

On the `MeshViewportImage` `<Image>` element (line 70), add:

```xml
MouseDown="OnMeshViewportMouseDown"
MouseMove="OnMeshViewportMouseMoveForNavigation"
MouseUp="OnMeshViewportMouseUpForNavigation"
MouseWheel="OnMeshViewportWheel"
```

(These are separate handler attributes from the existing `OnMeshMouseDown`/`OnMeshMouseMove`/`OnMeshMouseUp`/`OnMeshLostCapture` already on this element — WPF fires all handlers registered for the same routed event in registration order, so both the gizmo-drag and navigation handlers run; the `_orbiting || _panning` guard in Task 4 Step 1 keeps them from conflicting.)

Near the existing mesh-viewport toolbar buttons (the "↖", "✥", "✓" mode buttons around lines 87-95 in `ModelingWorkspace.xaml`), add two buttons:

```xml
<Button Content="⛶" Width="28" ToolTip="Frame selected (reset pan/zoom)" Command="{Binding FrameSelectedMeshViewportCommand}" />
<Button Content="⬚/◆" Width="36" ToolTip="Toggle orthographic / perspective" Command="{Binding ToggleMeshViewportProjectionCommand}" />
```

- [ ] **Step 3: Add the two missing commands to the view model**

`FrameSelectedMeshViewportCommand` and `ToggleMeshViewportProjectionCommand` don't exist yet — Task 3 only added the underlying methods. In `RekallAgeStudioViewModel.cs`, find where sibling simple commands are declared (e.g. near `RefreshMeshAssetsCommand` around line 89, or wherever the existing `RelayCommand`-style fields for parameterless mesh-viewport actions are declared) and add two more following that exact pattern, wired to `FrameSelectedMeshViewport` and `ToggleMeshViewportProjection`. Expose them as `public ICommand FrameSelectedMeshViewportCommand => ...;` / `public ICommand ToggleMeshViewportProjectionCommand => ...;` alongside `OpenMeshAssetCommand`.

- [ ] **Step 4: Build Studio and run the full Studio test project**

Run: `dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj -c Debug`
Expected: builds with 0 errors.

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj`
Expected: PASS, same total plus Task 1-3's new tests; no regressions.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Studio/ModelingWorkspace.xaml src/Rekall.Age.Studio/ModelingWorkspace.xaml.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs
git commit -m "feat: wire orbit/pan/zoom mouse input and frame/ortho controls into Studio"
```

### Task 5: Full-solution verification and progress notes

**Files:**
- Modify: `docs/production/PROGRESS.md` (append a checkpoint under a new heading, following the existing checkpoint style used elsewhere in the file)

- [ ] **Step 1: Full solution Release build**

Run: `dotnet build Rekall.Age.sln -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full Studio test project and full Aetherfall/engine acceptance filter**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj`
Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~Aetherfall`
Expected: both fully green (this change touches only `Rekall.Age.Studio`, so the Aetherfall filter is a cheap confirmation nothing else broke).

- [ ] **Step 3: Record the checkpoint**

Add a `## Studio orbit/pan/zoom viewport camera checkpoint` section to `docs/production/PROGRESS.md` (near the other Studio-related entries) describing: the identity-camera regression guarantee, the input bindings (middle-drag orbit, shift+middle-drag pan, scroll zoom, frame-selected/ortho-toggle buttons), and that this is the first of the planned modeling-parity sub-projects (node-graph canvas, direct-manipulation mesh tools, materials/UV audit, and the animation workspace remain future work, per the 2026-08-26 audit already recorded in "Current gaps").

- [ ] **Step 4: Commit and push**

```bash
git add docs/production/PROGRESS.md
git commit -m "docs: record Studio viewport camera checkpoint"
git push origin codex/high-fidelity-forward-plus
```
