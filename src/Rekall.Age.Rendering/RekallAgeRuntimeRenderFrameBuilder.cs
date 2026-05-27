using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRuntimeRenderFrameBuilder
{
    public RekallAgeRuntimeViewportFrame Build(
        RekallAgeRuntimeWorld world,
        int width,
        int height,
        bool debugOverlay)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Viewport height must be greater than zero.");
        }

        var cameras = world.Subsystems.Rendering.Cameras
            .Select(camera =>
            {
                var transform = FindTransform(world, camera.EntityId);
                return new RekallAgeRuntimeViewportCamera(
                    camera.EntityId,
                    camera.EntityName,
                    camera.Kind,
                    camera.Active,
                    transform.Position3D.X,
                    transform.Position3D.Y,
                    transform.Position3D.Z,
                    transform.Rotation3D.X,
                    transform.Rotation3D.Y,
                    transform.Rotation3D.Z,
                    camera.ProjectionMode,
                    camera.FieldOfViewDegrees,
                    camera.OrthographicSize,
                    camera.NearClip,
                    camera.FarClip,
                    camera.ClearColor,
                    camera.StereoMode,
                    camera.StereoRenderMode,
                    camera.InterpupillaryDistance,
                    camera.StereoConvergenceDistance,
                    camera.XrViewConfiguration,
                    camera.FoveatedRendering,
                    camera.CullingMask,
                    camera.RenderOrder,
                    camera.ViewportX,
                    camera.ViewportY,
                    camera.ViewportWidth,
                    camera.ViewportHeight);
            })
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.RenderOrder)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .ToArray();
        var activeCamera = cameras.FirstOrDefault(camera => camera.Active) ?? cameras.FirstOrDefault();
        var headsetCamera = cameras.FirstOrDefault(IsHeadsetCamera);
        var renderableCandidates = (debugOverlay
            ? BuildRenderables(world, activeCamera, height).Concat(BuildColliderDebugRenderables(world))
            : BuildRenderables(world, activeCamera, height))
            .ToArray();
        var renderables = renderableCandidates
            .Where(renderable => RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, activeCamera?.CullingMask))
            .OrderBy(renderable => renderable.SortKey)
            .ThenBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .ToArray();
        var culling = BuildCullingDiagnostics(renderableCandidates, activeCamera);
        var cameraViews = BuildCameraViews(cameras, renderableCandidates, width, height);

        return new RekallAgeRuntimeViewportFrame(
            world.SceneName,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            width,
            height,
            activeCamera,
            cameras,
            renderables,
            world.Subsystems.Rendering.UiLayers.Count,
            new RekallAgeRuntimeViewportOverlay(debugOverlay, world.Observations.Count),
            world.Observations
                .Select(observation => new RekallAgeRuntimeViewportObservation(
                    observation.Code,
                    observation.Severity,
                    observation.Subsystem,
                    observation.TargetName.Length > 0 ? observation.TargetName : observation.TargetId,
                    observation.Message))
                .ToArray(),
            BuildStereoSettings(headsetCamera, width, height),
            BuildPostProcessStack(world))
        {
            Culling = culling,
            CameraViews = cameraViews,
            HeadsetCamera = headsetCamera
        };
    }

    private static RekallAgeRuntimeViewportPostProcessStack? BuildPostProcessStack(RekallAgeRuntimeWorld world)
    {
        var stack = world.Subsystems.Rendering.PostProcessStacks
            .OrderByDescending(item => item.Enabled && item.Passes.Count > 0)
            .ThenBy(item => item.EntityName, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        return stack is null
            ? null
            : new RekallAgeRuntimeViewportPostProcessStack(
                stack.EntityId,
                stack.EntityName,
                stack.Enabled,
                stack.Passes
                    .Select(pass => new RekallAgeRuntimeViewportPostProcessPass(
                        pass.Name,
                        pass.Type,
                        pass.Input,
                        pass.Source,
                        pass.Output,
                        pass.Scale,
                        pass.Iterations,
                        pass.Threshold,
                        pass.Intensity,
                        pass.Radius,
                        pass.BlendMode))
                    .ToArray());
    }

    private static bool IsHeadsetCamera(RekallAgeRuntimeViewportCamera camera)
    {
        return camera.Active
            && camera.Kind.Equals("Camera3D", StringComparison.Ordinal)
            && (camera.StereoMode.Equals("stereo", StringComparison.OrdinalIgnoreCase)
                || camera.StereoMode.Equals("vr", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportCameraView> BuildCameraViews(
        IReadOnlyList<RekallAgeRuntimeViewportCamera> cameras,
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> candidates,
        int width,
        int height)
    {
        var renderCameras = cameras.Where(camera => camera.Active).ToArray();
        if (renderCameras.Length == 0)
        {
            renderCameras = cameras.ToArray();
        }

        return renderCameras
            .Select(camera =>
            {
                var renderables = candidates
                    .Where(renderable => RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, camera.CullingMask))
                    .OrderBy(renderable => renderable.SortKey)
                    .ThenBy(renderable => renderable.EntityName, StringComparer.Ordinal)
                    .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
                    .ToArray();
                var culled = candidates
                    .Where(renderable => !RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, camera.CullingMask))
                    .Select(renderable => new RekallAgeRuntimeViewportCulledRenderable(
                        renderable.EntityId,
                        renderable.EntityName,
                        renderable.Kind,
                        renderable.Layer,
                        "camera-culling-mask",
                        camera.EntityId,
                        camera.EntityName,
                        camera.CullingMask))
                    .OrderBy(renderable => renderable.EntityName, StringComparer.Ordinal)
                    .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
                    .ToArray();

                return new RekallAgeRuntimeViewportCameraView(
                    camera,
                    RekallAgeRuntimeViewportCameraRect.FromCamera(width, height, camera),
                    renderables,
                    culled);
            })
            .ToArray();
    }

    private static RekallAgeRuntimeViewportCulling BuildCullingDiagnostics(
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> candidates,
        RekallAgeRuntimeViewportCamera? activeCamera)
    {
        var culled = candidates
            .Where(renderable => !RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, activeCamera?.CullingMask))
            .Select(renderable => new RekallAgeRuntimeViewportCulledRenderable(
                renderable.EntityId,
                renderable.EntityName,
                renderable.Kind,
                renderable.Layer,
                "camera-culling-mask",
                activeCamera?.EntityId,
                activeCamera?.EntityName,
                activeCamera?.CullingMask ?? "*"))
            .OrderBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeRuntimeViewportCulling(culled.Length, culled);
    }

    private static RekallAgeRuntimeViewportStereoSettings? BuildStereoSettings(
        RekallAgeRuntimeViewportCamera? activeCamera,
        int width,
        int height)
    {
        if (activeCamera is null
            || !activeCamera.StereoMode.Equals("stereo", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var eyeSeparation = Math.Clamp(activeCamera.InterpupillaryDistance, 0, 1);
        var halfSeparation = eyeSeparation * 0.5;
        var renderMode = activeCamera.StereoRenderMode.Equals("side-by-side", StringComparison.OrdinalIgnoreCase)
            ? "side-by-side"
            : activeCamera.StereoRenderMode.Equals("dual-pass", StringComparison.OrdinalIgnoreCase)
                ? "dual-pass"
                : "single-pass-multiview";
        var preferMultiview = renderMode.Equals("single-pass-multiview", StringComparison.Ordinal);
        var cameraViewportX = Math.Clamp(activeCamera.ViewportX, 0, 1) * width;
        var cameraViewportY = Math.Clamp(activeCamera.ViewportY, 0, 1) * height;
        var cameraViewportWidth = Math.Max(1, Math.Clamp(activeCamera.ViewportWidth, 0.001, 1) * width);
        var cameraViewportHeight = Math.Max(1, Math.Clamp(activeCamera.ViewportHeight, 0.001, 1) * height);
        var eyeWidth = renderMode.Equals("side-by-side", StringComparison.Ordinal)
            ? Math.Max(1, cameraViewportWidth / 2.0)
            : Math.Max(1, cameraViewportWidth);
        var eyes = renderMode.Equals("side-by-side", StringComparison.Ordinal)
            ? new[]
            {
                new RekallAgeRuntimeViewportEye("left", 0, -halfSeparation, 0, 0, cameraViewportX, cameraViewportY, eyeWidth, cameraViewportHeight),
                new RekallAgeRuntimeViewportEye("right", 1, halfSeparation, 0, 0, cameraViewportX + eyeWidth, cameraViewportY, eyeWidth, cameraViewportHeight)
            }
            : new[]
            {
                new RekallAgeRuntimeViewportEye("left", 0, -halfSeparation, 0, 0, cameraViewportX, cameraViewportY, eyeWidth, cameraViewportHeight),
                new RekallAgeRuntimeViewportEye("right", 1, halfSeparation, 0, 0, cameraViewportX, cameraViewportY, eyeWidth, cameraViewportHeight)
            };
        return new RekallAgeRuntimeViewportStereoSettings(
            true,
            "stereo",
            renderMode,
            2,
            eyeSeparation,
            Math.Max(0.001, activeCamera.StereoConvergenceDistance),
            activeCamera.XrViewConfiguration,
            activeCamera.FoveatedRendering,
            preferMultiview,
            eyes);
    }

    private static IEnumerable<RekallAgeRuntimeViewportRenderable> BuildRenderables(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeViewportCamera? activeCamera,
        int viewportHeight)
    {
        foreach (var sprite in world.Subsystems.Rendering.Sprites)
        {
            var transform = FindTransform(world, sprite.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                sprite.EntityId,
                sprite.EntityName,
                "sprite",
                sprite.AssetId,
                transform.Position2D.X,
                transform.Position2D.Y,
                transform.Position3D.Z,
                100,
                RotationZ: transform.Rotation2D,
                ScaleX: transform.Scale2D.X,
                ScaleY: transform.Scale2D.Y,
                Layer: sprite.Layer);
        }

        foreach (var mesh in world.Subsystems.Rendering.Meshes)
        {
            var entity = FindEntity(world, mesh.EntityId);
            var transform = entity?.Transform ?? RekallAgeRuntimeTransform.Identity;
            var meshRendererComponent = entity?.Components.FirstOrDefault(component =>
                component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet");
            var planetComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.PlanetRenderer", StringComparison.Ordinal));
            var cloudLayerComponents = entity?.Components
                .Where(component => component.Type.Equals("Rekall.CloudLayerRenderer", StringComparison.Ordinal))
                .ToArray() ?? [];
            var cloudLayers = ExpandCloudLayerComponents(cloudLayerComponents);
            var atmosphereComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.AtmosphereRenderer", StringComparison.Ordinal));
            var materialComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.Material", StringComparison.Ordinal));
            var proceduralMaterialComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.ProceduralMaterial", StringComparison.Ordinal));
            var virtualGeometryComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.VirtualGeometry", StringComparison.Ordinal));
            var geometry = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.GeometryPrimitive", StringComparison.Ordinal));
            var geometryMeshComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.GeometryMesh", StringComparison.Ordinal));
            var lineSegmentsComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.LineSegments", StringComparison.Ordinal));
            var orbitComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.KeplerOrbit", StringComparison.Ordinal));
            var orbitPathComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.OrbitPathRenderer", StringComparison.Ordinal));
            var ringComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.RingRenderer", StringComparison.Ordinal));
            var starfieldComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.StarfieldRenderer", StringComparison.Ordinal));
            var markerComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.MarkerRenderer", StringComparison.Ordinal));
            var haloComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.HaloRenderer", StringComparison.Ordinal));
            var textLabelComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.TextLabelRenderer", StringComparison.Ordinal));
            var lodSelection = SelectLod(entity, activeCamera, transform);
            var isOrbitPathRenderable = mesh.Variant?.Equals("rekall.orbit.path", StringComparison.OrdinalIgnoreCase) == true;
            var isRingRenderable = mesh.Variant?.Equals("rekall.planet.ring", StringComparison.OrdinalIgnoreCase) == true;
            var isStarfieldRenderable = mesh.Variant?.Equals("rekall.space.starfield", StringComparison.OrdinalIgnoreCase) == true;
            var isMarkerRenderable = mesh.Variant?.Equals("rekall.marker", StringComparison.OrdinalIgnoreCase) == true;
            var isHaloRenderable = mesh.Variant?.Equals("rekall.halo", StringComparison.OrdinalIgnoreCase) == true;
            var isTextLabelRenderable = mesh.Variant?.Equals("rekall.text.label", StringComparison.OrdinalIgnoreCase) == true;
            if (isOrbitPathRenderable && !ReadBoolean(orbitPathComponent, "active", true))
            {
                continue;
            }

            if (isStarfieldRenderable && !ReadBoolean(starfieldComponent, "active", true))
            {
                continue;
            }

            if (isMarkerRenderable && !ReadBoolean(markerComponent, "active", true))
            {
                continue;
            }

            if (isHaloRenderable && !ReadBoolean(haloComponent, "active", true))
            {
                continue;
            }

            if (isTextLabelRenderable && !ReadBoolean(textLabelComponent, "active", true))
            {
                continue;
            }

            var primitive = ReadString(geometry, "primitive");
            var orbitPathMesh = isOrbitPathRenderable ? ReadOrbitPathMesh(orbitComponent, orbitPathComponent) : null;
            if (isOrbitPathRenderable && orbitPathMesh is null)
            {
                continue;
            }

            var ringMesh = isRingRenderable ? ReadRingMesh(ringComponent) : null;
            if (isRingRenderable && ringMesh is null)
            {
                continue;
            }

            var starfieldMesh = isStarfieldRenderable ? ReadStarfieldMesh(starfieldComponent) : null;
            if (isStarfieldRenderable && starfieldMesh is null)
            {
                continue;
            }

            var markerMesh = isMarkerRenderable ? ReadMarkerMesh(markerComponent) : null;
            if (isMarkerRenderable && markerMesh is null)
            {
                continue;
            }

            var haloMesh = isHaloRenderable ? ReadHaloMesh(haloComponent) : null;
            if (isHaloRenderable && haloMesh is null)
            {
                continue;
            }

            var geometryMesh = orbitPathMesh ?? ringMesh ?? starfieldMesh ?? markerMesh ?? haloMesh ?? ReadGeometryMesh(geometryMeshComponent);
            var lineSegments = isTextLabelRenderable
                ? ReadTextLabelLineSegments(textLabelComponent, activeCamera, transform, viewportHeight)
                : ReadLineSegments(lineSegmentsComponent);
            if (isTextLabelRenderable && lineSegments is null)
            {
                continue;
            }
            var materialColor = ReadString(materialComponent, "baseColor")
                ?? ReadString(materialComponent, "color")
                ?? ReadString(orbitPathComponent, "color")
                ?? ReadString(ringComponent, "color")
                ?? ReadString(starfieldComponent, "color")
                ?? ReadString(haloComponent, "color")
                ?? ReadString(textLabelComponent, "color")
                ?? ReadString(lineSegmentsComponent, "color")
                ?? ReadString(geometryMeshComponent, "color")
                ?? ReadString(geometry, "color");
            var markerColor = ReadString(markerComponent, "color");
            var haloColor = ReadString(haloComponent, "color");
            var textureAssetId = ReadString(materialComponent, "baseColorTexture")
                ?? ReadString(materialComponent, "texture")
                ?? ReadString(geometryMeshComponent, "textureAssetId")
                ?? ReadString(geometryMeshComponent, "texture")
                ?? ReadString(geometry, "textureAssetId")
                ?? ReadString(geometry, "texture")
                ?? ReadString(planetComponent, "surfaceTexture")
                ?? ReadString(planetComponent, "SurfaceTexture");
            primitive = lodSelection is { AssetId: not null, Primitive: null }
                ? null
                : lodSelection?.Primitive ?? primitive;
            textureAssetId = lodSelection?.TextureAssetId ?? textureAssetId;
            materialColor = lodSelection?.MaterialColor ?? materialColor;
            var normalTextureAssetId = ReadString(materialComponent, "normalTexture")
                ?? ReadString(planetComponent, "normalTexture")
                ?? ReadString(planetComponent, "NormalTexture");
            var metallicRoughnessTextureAssetId = ReadString(materialComponent, "metallicRoughnessTexture");
            var occlusionTextureAssetId = ReadString(materialComponent, "occlusionTexture");
            var emissiveTextureAssetId = ReadString(materialComponent, "emissiveTexture")
                ?? ReadString(planetComponent, "emissiveTexture");
            var emissiveColor = ReadString(materialComponent, "emissiveColor")
                ?? ReadString(planetComponent, "emissiveColor");
            var variant = geometryMesh is not null
                ? orbitPathMesh is not null
                    ? "rekall.orbit.path"
                    : ringMesh is not null ? "rekall.planet.ring" : starfieldMesh is not null ? "rekall.space.starfield" : markerMesh is not null ? "rekall.marker" : haloMesh is not null ? "rekall.halo" : "rekall.geometry.mesh"
                : planetComponent is not null
                ? "rekall.planet.surface"
                : isTextLabelRenderable
                ? "rekall.text.label"
                : string.IsNullOrWhiteSpace(primitive)
                ? lodSelection?.AssetId ?? mesh.AssetId
                : $"rekall.geometry.{primitive.Trim().ToLowerInvariant()}";
            var radius = Math.Max(0.0001, ReadNumber(planetComponent, "radius", 0.5));
            var renderTransform = orbitPathMesh is null ? transform : FindOrbitParentTransform(world, orbitComponent);
            var scaleMultiplier = Math.Max(0.0001, lodSelection?.ScaleMultiplier ?? 1);
            var usesAuthoredGeometryScale = orbitPathMesh is not null || ringMesh is not null || starfieldMesh is not null || markerMesh is not null || haloMesh is not null;
            var scaleX = (usesAuthoredGeometryScale ? 1 : planetComponent is null ? transform.Scale3D.X : transform.Scale3D.X * radius * 2) * scaleMultiplier;
            var scaleY = (usesAuthoredGeometryScale ? 1 : planetComponent is null ? transform.Scale3D.Y : transform.Scale3D.Y * radius * 2) * scaleMultiplier;
            var scaleZ = (usesAuthoredGeometryScale ? 1 : planetComponent is null ? transform.Scale3D.Z : transform.Scale3D.Z * radius * 2) * scaleMultiplier;
            var atmosphereHeight = planetComponent is not null && atmosphereComponent is not null && orbitPathMesh is null && markerMesh is null && haloMesh is null && !isTextLabelRenderable && lodSelection?.AssetId is null
                ? Math.Max(0, ReadNumber(atmosphereComponent, "height", 0.08))
                : 0;
            var atmosphereRadius = radius + atmosphereHeight;
            var atmosphereMaterial = atmosphereHeight > 0 && atmosphereComponent is not null
                ? ReadAtmosphereMaterial(atmosphereComponent, radius, atmosphereRadius)
                : null;
            var virtualGeometry = ReadVirtualGeometry(virtualGeometryComponent);
            var sortKey = mesh.SortKey
                + (entity?.Components.Any(component => component.Type.Equals("Rekall.Rigidbody3D", StringComparison.Ordinal)) == true ? 20 : 0);
            var surfaceEntityId = orbitPathMesh is not null
                ? $"{mesh.EntityId}:orbit-path"
                : ringMesh is not null ? $"{mesh.EntityId}:ring" : markerMesh is not null ? $"{mesh.EntityId}:marker" : haloMesh is not null ? $"{mesh.EntityId}:halo" : isTextLabelRenderable ? $"{mesh.EntityId}:label" : mesh.EntityId;
            yield return new RekallAgeRuntimeViewportRenderable(
                surfaceEntityId,
                mesh.EntityName,
                string.IsNullOrWhiteSpace(mesh.Kind) ? "mesh" : mesh.Kind,
                lodSelection?.AssetId ?? mesh.AssetId,
                renderTransform.Position3D.X,
                renderTransform.Position3D.Y,
                renderTransform.Position3D.Z,
                sortKey,
                Variant: lodSelection?.Variant ?? mesh.Variant ?? variant,
                RotationX: transform.Rotation3D.X,
                RotationY: transform.Rotation3D.Y,
                RotationZ: transform.Rotation3D.Z,
                ScaleX: scaleX,
                ScaleY: scaleY,
                ScaleZ: scaleZ,
                MaterialColor: markerMesh is not null ? markerColor ?? mesh.MaterialColor ?? materialColor : haloMesh is not null ? haloColor ?? mesh.MaterialColor ?? materialColor : lodSelection?.MaterialColor ?? mesh.MaterialColor ?? materialColor ?? ReadString(planetComponent, "color") ?? ReadString(planetComponent, "Color"),
                GeometryMesh: geometryMesh,
                TextureAssetId: isRingRenderable
                    ? ReadString(ringComponent, "texture") ?? ReadString(ringComponent, "Texture")
                    : lodSelection?.TextureAssetId ?? mesh.TextureAssetId ?? textureAssetId,
                MetallicRoughnessTextureAssetId: metallicRoughnessTextureAssetId,
                NormalTextureAssetId: normalTextureAssetId,
                OcclusionTextureAssetId: occlusionTextureAssetId,
                MetallicFactor: ReadNumber(materialComponent, "metallicFactor", 0),
                RoughnessFactor: ReadNumber(materialComponent, "roughnessFactor", 1),
                NormalScale: ReadNumber(materialComponent, "normalScale", 1),
                OcclusionStrength: ReadNumber(materialComponent, "occlusionStrength", 1),
                EmissiveColor: markerMesh is not null ? markerColor ?? materialColor : haloMesh is not null ? haloColor ?? materialColor : orbitPathMesh is not null || starfieldMesh is not null ? materialColor : emissiveColor,
                EmissiveTextureAssetId: emissiveTextureAssetId,
                EmissiveStrength: orbitPathMesh is not null
                    ? ReadNumber(orbitPathComponent, "emissiveStrength", 1.4)
                    : starfieldMesh is not null ? ReadNumber(starfieldComponent, "brightness", 2.2)
                    : markerMesh is not null ? ReadNumber(markerComponent, "emissiveStrength", 2)
                    : haloMesh is not null ? ReadNumber(haloComponent, "intensity", 1)
                    : ReadNumber(materialComponent, "emissiveStrength", ReadNumber(planetComponent, "emissiveStrength", 0)),
                ShaderPipeline: ToViewportShaderPipeline(mesh.ShaderPipeline) ?? ReadShaderPipeline(meshRendererComponent),
                LineSegments: lineSegments,
                Layer: mesh.Layer,
                ProceduralMaterial: ReadProceduralMaterial(proceduralMaterialComponent),
                Atmosphere: atmosphereMaterial,
                CloudShadow: ReadCloudShadowMaterial(cloudLayers, radius),
                SurfaceWater: ReadSurfaceWaterMaterial(planetComponent),
                MeshSlices: ReadMeshSlices(planetComponent, 0),
                MeshStacks: ReadMeshStacks(planetComponent, 0),
                FacingMode: isHaloRenderable
                    ? ReadString(haloComponent, "facingMode") ?? ReadString(haloComponent, "FacingMode") ?? "world"
                    : isTextLabelRenderable
                    ? ReadString(textLabelComponent, "facingMode") ?? ReadString(textLabelComponent, "FacingMode") ?? "world"
                    : "world",
                VirtualGeometry: virtualGeometry);

            if (planetComponent is not null
                && atmosphereComponent is not null
                && orbitPathMesh is null
                && markerMesh is null
                && haloMesh is null
                && !isTextLabelRenderable
                && lodSelection?.AssetId is null)
            {
                for (var cloudLayerIndex = 0; cloudLayerIndex < cloudLayers.Count; cloudLayerIndex++)
                {
                    var cloudLayerComponent = cloudLayers[cloudLayerIndex];
                    var cloudHeight = Math.Max(0, ReadNumber(cloudLayerComponent, "height", ReadNumber(cloudLayerComponent, "Height", 0.02)));
                    var cloudRadius = radius + cloudHeight;
                    var cloudScaleX = transform.Scale3D.X * cloudRadius * 2 * scaleMultiplier;
                    var cloudScaleY = transform.Scale3D.Y * cloudRadius * 2 * scaleMultiplier;
                    var cloudScaleZ = transform.Scale3D.Z * cloudRadius * 2 * scaleMultiplier;
                    var cloudColor = ReadString(cloudLayerComponent, "color")
                        ?? ReadString(cloudLayerComponent, "Color")
                        ?? "#ffffff";
                    var cloudLayerMaterial = ReadCloudLayerMaterial(cloudLayerComponent, cloudRadius, cloudColor);
                    if (cloudLayerMaterial.Coverage <= 0.0001)
                    {
                        continue;
                    }

                    yield return new RekallAgeRuntimeViewportRenderable(
                        cloudLayerIndex == 0 ? $"{mesh.EntityId}:clouds" : $"{mesh.EntityId}:clouds:{cloudLayerIndex}",
                        mesh.EntityName,
                        "mesh",
                        "rekall.planet.cloud-layer",
                        renderTransform.Position3D.X,
                        renderTransform.Position3D.Y,
                        renderTransform.Position3D.Z,
                        sortKey + 20 + cloudLayerIndex,
                        Variant: "rekall.planet.cloud-layer",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: cloudScaleX,
                        ScaleY: cloudScaleY,
                        ScaleZ: cloudScaleZ,
                        MaterialColor: cloudColor,
                        TextureAssetId: ReadString(cloudLayerComponent, "texture")
                            ?? ReadString(cloudLayerComponent, "Texture"),
                        RoughnessFactor: 1,
                        Layer: mesh.Layer,
                        Atmosphere: atmosphereMaterial,
                        CloudLayer: cloudLayerMaterial,
                        MeshSlices: ReadMeshSlices(cloudLayerComponent, ReadMeshSlices(planetComponent, 0)),
                        MeshStacks: ReadMeshStacks(cloudLayerComponent, ReadMeshStacks(planetComponent, 0)),
                        VirtualGeometry: virtualGeometry);
                }

                if (atmosphereHeight > 0 && ReadBoolean(atmosphereComponent, "renderShell", ReadBoolean(atmosphereComponent, "RenderShell", true)))
                {
                    var atmosphereScaleX = transform.Scale3D.X * atmosphereRadius * 2 * scaleMultiplier;
                    var atmosphereScaleY = transform.Scale3D.Y * atmosphereRadius * 2 * scaleMultiplier;
                    var atmosphereScaleZ = transform.Scale3D.Z * atmosphereRadius * 2 * scaleMultiplier;
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{mesh.EntityId}:atmosphere",
                        mesh.EntityName,
                        "mesh",
                        "rekall.planet.atmosphere",
                        renderTransform.Position3D.X,
                        renderTransform.Position3D.Y,
                        renderTransform.Position3D.Z,
                        sortKey + 40,
                        Variant: "rekall.planet.atmosphere",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: atmosphereScaleX,
                        ScaleY: atmosphereScaleY,
                        ScaleZ: atmosphereScaleZ,
                        MaterialColor: ReadString(atmosphereComponent, "rayleighColor")
                            ?? ReadString(atmosphereComponent, "RayleighColor")
                            ?? "#7fb6ff",
                        EmissiveColor: ReadString(atmosphereComponent, "mieColor")
                            ?? ReadString(atmosphereComponent, "MieColor")
                            ?? "#ffffff",
                        EmissiveStrength: Math.Max(0, ReadNumber(atmosphereComponent, "exposure", 1.2)),
                        Layer: mesh.Layer,
                        Atmosphere: atmosphereMaterial,
                        MeshSlices: ReadMeshSlices(atmosphereComponent, ReadMeshSlices(planetComponent, 0)),
                        MeshStacks: ReadMeshStacks(atmosphereComponent, ReadMeshStacks(planetComponent, 0)),
                        VirtualGeometry: virtualGeometry);
                }
            }
        }

        foreach (var light in world.Subsystems.Rendering.Lights)
        {
            var transform = FindTransform(world, light.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                light.EntityId,
                light.EntityName,
                "light",
                null,
                transform.Position3D.X,
                transform.Position3D.Y,
                transform.Position3D.Z,
                300,
                Variant: light.Kind,
                RotationX: transform.Rotation3D.X,
                RotationY: transform.Rotation3D.Y,
                RotationZ: transform.Rotation3D.Z,
                Intensity: light.Intensity,
                MaterialColor: light.Color,
                Layer: light.Layer);
        }

        foreach (var uiLayer in world.Subsystems.Rendering.UiLayers)
        {
            yield return new RekallAgeRuntimeViewportRenderable(
                uiLayer.EntityId,
                uiLayer.EntityName,
                "ui",
                null,
                0,
                uiLayer.Layer,
                0,
                400 + uiLayer.Layer);
        }
    }

    private static LodSelection? SelectLod(
        RekallAgeRuntimeEntity? entity,
        RekallAgeRuntimeViewportCamera? activeCamera,
        RekallAgeRuntimeTransform transform)
    {
        if (entity is null || activeCamera is null)
        {
            return null;
        }

        var component = entity.Components.FirstOrDefault(item =>
            item.Type.Equals("Rekall.LodGroup", StringComparison.Ordinal));
        if (component is null
            || !ReadBoolean(component, "active", true)
            || !TryGetPropertyValue(component.Properties, "levels", out var levelsNode)
            || levelsNode is not JsonArray levels)
        {
            return null;
        }

        var distance = Distance(activeCamera, transform);
        return levels
            .OfType<JsonObject>()
            .Select(level => ReadLodLevel(level))
            .Where(level => level is not null)
            .Select(level => level!)
            .OrderByDescending(level => level.MinDistance)
            .FirstOrDefault(level => distance >= level.MinDistance
                && (level.MaxDistance is null || distance < level.MaxDistance.Value));
    }

    private static LodSelection? ReadLodLevel(JsonObject level)
    {
        var primitive = NormalizePrimitive(ReadString(level, "primitive"));
        var assetId = EmptyToNull(ReadString(level, "assetId") ?? ReadString(level, "mesh"));
        var textureAssetId = EmptyToNull(ReadString(level, "textureAssetId") ?? ReadString(level, "texture"));
        var materialColor = EmptyToNull(ReadString(level, "materialColor") ?? ReadString(level, "color"));
        if (primitive is null && assetId is null && textureAssetId is null && materialColor is null)
        {
            return null;
        }

        return new LodSelection(
            Math.Max(0, ReadNumber(level, "minDistance", 0)),
            ReadOptionalNumber(level, "maxDistance"),
            assetId,
            primitive,
            textureAssetId,
            materialColor,
            Math.Max(0.0001, ReadNumber(level, "scaleMultiplier", 1)));
    }

    private static string? NormalizePrimitive(string? primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive))
        {
            return null;
        }

        var normalized = primitive.Trim().ToLowerInvariant();
        if (normalized.StartsWith("rekall.geometry.", StringComparison.Ordinal))
        {
            normalized = normalized["rekall.geometry.".Length..];
        }

        return normalized is "cube" or "sphere" or "cylinder" or "cone" or "plane" or "surface"
            ? normalized
            : null;
    }

    private static double Distance(
        RekallAgeRuntimeViewportCamera camera,
        RekallAgeRuntimeTransform transform)
    {
        var dx = transform.Position3D.X - camera.X;
        var dy = transform.Position3D.Y - camera.Y;
        var dz = transform.Position3D.Z - camera.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static IEnumerable<RekallAgeRuntimeViewportRenderable> BuildColliderDebugRenderables(RekallAgeRuntimeWorld world)
    {
        foreach (var entity in world.Entities)
        {
            var transform = entity.Transform;
            var collider = entity.Components.FirstOrDefault(component =>
                component.Type is
                    "Rekall.BoxCollider2D" or
                    "Rekall.CircleCollider2D" or
                    "Rekall.BoxCollider3D" or
                    "Rekall.SphereCollider3D" or
                    "Rekall.CapsuleCollider3D" or
                    "Rekall.MeshCollider");
            if (collider is null)
            {
                continue;
            }

            switch (collider.Type)
            {
                case "Rekall.BoxCollider2D":
                    var width2D = Math.Max(0.0001, ReadNumber(collider, "width", 1));
                    var height2D = Math.Max(0.0001, ReadNumber(collider, "height", 1));
                    var box2DColor = "#33ddff66";
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position2D.X,
                        transform.Position2D.Y,
                        transform.Position3D.Z,
                        910,
                        Variant: "rekall.debug.collider.lines",
                        RotationZ: transform.Rotation2D,
                        MaterialColor: box2DColor,
                        LineSegments: CreateWireRectangle(width2D, height2D));
                    break;
                case "Rekall.CircleCollider2D":
                    var radius2D = Math.Max(0.0001, ReadNumber(collider, "radius", 0.5));
                    var circle2DColor = "#ffea0066";
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position2D.X,
                        transform.Position2D.Y,
                        transform.Position3D.Z,
                        915,
                        Variant: "rekall.debug.collider.lines",
                        RotationZ: transform.Rotation2D,
                        MaterialColor: circle2DColor,
                        LineSegments: CreateWireCircle(radius2D));
                    break;
                case "Rekall.BoxCollider3D":
                    var width = Math.Max(0.0001, ReadNumber(collider, "width", 1));
                    var height = Math.Max(0.0001, ReadNumber(collider, "height", 1));
                    var depth = Math.Max(0.0001, ReadNumber(collider, "depth", 1));
                    var boxColor = "#33ddff66";
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        920,
                        Variant: "rekall.debug.collider.lines",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        MaterialColor: boxColor,
                        LineSegments: CreateWireBox(width, height, depth));
                    break;
                case "Rekall.SphereCollider3D":
                    var radius = Math.Max(0.0001, ReadNumber(collider, "radius", 0.5));
                    var sphereColor = "#ffea0066";
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        940,
                        Variant: "rekall.debug.collider.lines",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        MaterialColor: sphereColor,
                        LineSegments: CreateWireSphere(radius));
                    break;
                case "Rekall.CapsuleCollider3D":
                    var capsuleRadius = Math.Max(0.0001, ReadNumber(collider, "radius", 0.5));
                    var length = Math.Max(0.0001, ReadNumber(collider, "length", 1));
                    var capsuleColor = "#ff66ff66";
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        940,
                        Variant: "rekall.debug.collider.lines",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        MaterialColor: capsuleColor,
                        LineSegments: CreateWireCapsule(capsuleRadius, length));
                    break;
                case "Rekall.MeshCollider":
                    var geometryMesh = ReadGeometryMesh(entity.Components.FirstOrDefault(component =>
                        component.Type.Equals("Rekall.GeometryMesh", StringComparison.Ordinal)));
                    if (geometryMesh is null)
                    {
                        break;
                    }

                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        930,
                        Variant: "rekall.debug.collider.lines",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: transform.Scale3D.X,
                        ScaleY: transform.Scale3D.Y,
                        ScaleZ: transform.Scale3D.Z,
                        MaterialColor: "#66ff9966",
                        LineSegments: CreateWireFromTriangleMesh(geometryMesh));
                    break;
            }
        }
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireBox(
        double width,
        double height,
        double depth)
    {
        var x = width * 0.5;
        var y = height * 0.5;
        var z = depth * 0.5;
        var corners = new[]
        {
            new MeshVector3(-x, -y, -z),
            new MeshVector3(x, -y, -z),
            new MeshVector3(x, -y, z),
            new MeshVector3(-x, -y, z),
            new MeshVector3(-x, y, -z),
            new MeshVector3(x, y, -z),
            new MeshVector3(x, y, z),
            new MeshVector3(-x, y, z)
        };
        var builder = new LineSegmentsBuilder(DefaultWireThickness(width, height, depth));
        builder.AddSegment(corners[0], corners[1]);
        builder.AddSegment(corners[1], corners[2]);
        builder.AddSegment(corners[2], corners[3]);
        builder.AddSegment(corners[3], corners[0]);
        builder.AddSegment(corners[4], corners[5]);
        builder.AddSegment(corners[5], corners[6]);
        builder.AddSegment(corners[6], corners[7]);
        builder.AddSegment(corners[7], corners[4]);
        builder.AddSegment(corners[0], corners[4]);
        builder.AddSegment(corners[1], corners[5]);
        builder.AddSegment(corners[2], corners[6]);
        builder.AddSegment(corners[3], corners[7]);
        return builder.Build();
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireRectangle(
        double width,
        double height)
    {
        var x = width * 0.5;
        var y = height * 0.5;
        var corners = new[]
        {
            new MeshVector3(-x, -y, 0),
            new MeshVector3(x, -y, 0),
            new MeshVector3(x, y, 0),
            new MeshVector3(-x, y, 0)
        };
        var builder = new LineSegmentsBuilder(DefaultWireThickness(width, height, 0));
        builder.AddSegment(corners[0], corners[1]);
        builder.AddSegment(corners[1], corners[2]);
        builder.AddSegment(corners[2], corners[3]);
        builder.AddSegment(corners[3], corners[0]);
        return builder.Build();
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireCircle(double radius)
    {
        var builder = new LineSegmentsBuilder(DefaultWireThickness(radius * 2, radius * 2, 0));
        AddRing(builder, radius, 32, Axis.Z);
        return builder.Build();
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireSphere(double radius)
    {
        var builder = new LineSegmentsBuilder(DefaultWireThickness(radius * 2, radius * 2, radius * 2));
        AddRing(builder, radius, 32, Axis.Y);
        AddRing(builder, radius, 32, Axis.X);
        AddRing(builder, radius, 32, Axis.Z);
        return builder.Build();
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireCapsule(
        double radius,
        double length)
    {
        var builder = new LineSegmentsBuilder(DefaultWireThickness(radius * 2, length + radius * 2, radius * 2));
        var half = length * 0.5;
        AddRing(builder, radius, 24, Axis.Y, half);
        AddRing(builder, radius, 24, Axis.Y, -half);
        for (var index = 0; index < 8; index++)
        {
            var angle = index / 8.0 * Math.PI * 2;
            var x = Math.Cos(angle) * radius;
            var z = Math.Sin(angle) * radius;
            builder.AddSegment(new MeshVector3(x, -half, z), new MeshVector3(x, half, z));
        }

        AddCapsuleArc(builder, radius, half, Axis.X);
        AddCapsuleArc(builder, radius, half, Axis.Z);
        return builder.Build();
    }

    private static RekallAgeRuntimeViewportLineSegments CreateWireFromTriangleMesh(
        RekallAgeRuntimeViewportGeometryMesh geometry)
    {
        var builder = new LineSegmentsBuilder(0.025);
        var edges = new HashSet<(ushort A, ushort B)>();
        for (var index = 0; index + 2 < geometry.Indices.Count; index += 3)
        {
            AddEdge(edges, geometry.Indices[index], geometry.Indices[index + 1]);
            AddEdge(edges, geometry.Indices[index + 1], geometry.Indices[index + 2]);
            AddEdge(edges, geometry.Indices[index + 2], geometry.Indices[index]);
        }

        foreach (var (a, b) in edges)
        {
            var from = geometry.Vertices[a];
            var to = geometry.Vertices[b];
            builder.AddSegment(
                new MeshVector3(from.X, from.Y, from.Z),
                new MeshVector3(to.X, to.Y, to.Z));
        }

        return builder.Build();
    }

    private static void AddEdge(HashSet<(ushort A, ushort B)> edges, ushort a, ushort b)
    {
        edges.Add(a < b ? (a, b) : (b, a));
    }

    private static void AddRing(
        LineSegmentsBuilder builder,
        double radius,
        int segments,
        Axis normalAxis,
        double yOffset = 0)
    {
        var points = Enumerable.Range(0, segments)
            .Select(index =>
            {
                var angle = index / (double)segments * Math.PI * 2;
                var a = Math.Cos(angle) * radius;
                var b = Math.Sin(angle) * radius;
                return normalAxis switch
                {
                    Axis.X => new MeshVector3(yOffset, a, b),
                    Axis.Z => new MeshVector3(a, b, yOffset),
                    _ => new MeshVector3(a, yOffset, b)
                };
            })
            .ToArray();
        for (var index = 0; index < points.Length; index++)
        {
            builder.AddSegment(points[index], points[(index + 1) % points.Length]);
        }
    }

    private static void AddCapsuleArc(
        LineSegmentsBuilder builder,
        double radius,
        double halfLength,
        Axis sideAxis)
    {
        var segments = 12;
        for (var hemisphere = -1; hemisphere <= 1; hemisphere += 2)
        {
            var centerY = hemisphere * halfLength;
            MeshVector3? previous = null;
            for (var index = 0; index <= segments; index++)
            {
                var angle = index / (double)segments * Math.PI;
                var side = Math.Sin(angle) * radius;
                var y = centerY + Math.Cos(angle) * radius * hemisphere;
                var point = sideAxis == Axis.X
                    ? new MeshVector3(side, y, 0)
                    : new MeshVector3(0, y, side);
                if (previous is { } from)
                {
                    builder.AddSegment(from, point);
                    builder.AddSegment(new MeshVector3(-from.X, from.Y, -from.Z), new MeshVector3(-point.X, point.Y, -point.Z));
                }

                previous = point;
            }
        }
    }

    private static double DefaultWireThickness(double x, double y, double z)
    {
        return Math.Clamp(Math.Max(x, Math.Max(y, z)) * 0.0125, 0.015, 0.08);
    }

    private static RekallAgeRuntimeTransform FindTransform(RekallAgeRuntimeWorld world, string entityId)
    {
        return world.Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal))?.Transform
            ?? RekallAgeRuntimeTransform.Identity;
    }

    private static RekallAgeRuntimeEntity? FindEntity(RekallAgeRuntimeWorld world, string entityId)
    {
        return world.Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal));
    }

    private static RekallAgeRuntimeTransform FindOrbitParentTransform(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeComponent? orbitComponent)
    {
        var parentBodyId = ReadString(orbitComponent, "parentBodyId");
        if (string.IsNullOrWhiteSpace(parentBodyId))
        {
            return RekallAgeRuntimeTransform.Identity;
        }

        return world.Entities.FirstOrDefault(entity => entity.Components.Any(component =>
            component.Type.Equals("Rekall.CelestialBody", StringComparison.Ordinal)
            && (ReadString(component, "bodyId") ?? entity.Name).Equals(parentBodyId, StringComparison.Ordinal)))?.Transform
            ?? RekallAgeRuntimeTransform.Identity;
    }

    private static string? ReadString(RekallAgeRuntimeComponent? component, string name)
    {
        if (component is null
            || !TryGetPropertyValue(component.Properties, name, out var node)
            || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : null;
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : null;
    }

    private static JsonArray? ReadArray(RekallAgeRuntimeComponent component, string name)
    {
        return TryGetPropertyValue(component.Properties, name, out var node) && node is JsonArray array
            ? array
            : null;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static double ReadNumber(RekallAgeRuntimeComponent? component, string name, double fallback)
    {
        return component is null ? fallback : ReadNumber(component.Properties, name, fallback);
    }

    private static RekallAgeRuntimeViewportShaderPipeline? ReadShaderPipeline(RekallAgeRuntimeComponent? component)
    {
        var vertexShader = ReadString(component, "vertexShader");
        var fragmentShader = ReadString(component, "fragmentShader");
        return string.IsNullOrWhiteSpace(vertexShader) || string.IsNullOrWhiteSpace(fragmentShader)
            ? null
            : new RekallAgeRuntimeViewportShaderPipeline(vertexShader.Trim(), fragmentShader.Trim());
    }

    private static RekallAgeRuntimeViewportShaderPipeline? ToViewportShaderPipeline(
        RekallAgeRuntimeRenderShaderPipeline? pipeline)
    {
        return pipeline is null
            ? null
            : new RekallAgeRuntimeViewportShaderPipeline(
                pipeline.VertexShader.Trim(),
                pipeline.FragmentShader.Trim());
    }

    private static RekallAgeRuntimeViewportProceduralMaterial? ReadProceduralMaterial(RekallAgeRuntimeComponent? component)
    {
        if (component is null)
        {
            return null;
        }

        return new RekallAgeRuntimeViewportProceduralMaterial(
            EmptyToNull(ReadString(component, "generator")) ?? "checker",
            (int)Math.Clamp(Math.Round(ReadNumber(component, "resolution", 128)), 2, 2048),
            Math.Max(0.0001, ReadNumber(component, "scale", 8)),
            (int)Math.Clamp(Math.Round(ReadNumber(component, "seed", 0)), int.MinValue, int.MaxValue),
            EmptyToNull(ReadString(component, "baseColorA")) ?? "#ffffff",
            EmptyToNull(ReadString(component, "baseColorB")) ?? "#202020",
            Math.Clamp(ReadNumber(component, "metallicFactor", 0), 0, 1),
            Math.Clamp(ReadNumber(component, "roughnessA", 1), 0.04, 1),
            Math.Clamp(ReadNumber(component, "roughnessB", 1), 0.04, 1),
            Math.Clamp(ReadNumber(component, "normalStrength", 0), 0, 4),
            Math.Max(0, ReadNumber(component, "emissiveStrength", 0)));
    }

    private static RekallAgeRuntimeViewportVirtualGeometry? ReadVirtualGeometry(RekallAgeRuntimeComponent? component)
    {
        if (component is null)
        {
            return null;
        }

        return new RekallAgeRuntimeViewportVirtualGeometry(
            ReadBoolean(component, "enabled", true),
            Math.Max(0.001, ReadNumber(component, "targetPixelError", 1)),
            Math.Clamp((int)Math.Round(ReadNumber(component, "clusterTriangleCount", 128)), 1, 65_536),
            Math.Max(0, (int)Math.Round(ReadNumber(component, "maxSelectedTriangles", 0))),
            Math.Clamp((int)Math.Round(ReadNumber(component, "maxLodLevel", 8)), 0, 16),
            EmptyToNull(ReadString(component, "debugMode")) ?? "off");
    }

    private static RekallAgeRuntimeViewportAtmosphereMaterial ReadAtmosphereMaterial(
        RekallAgeRuntimeComponent component,
        double planetRadius,
        double atmosphereRadius)
    {
        return new RekallAgeRuntimeViewportAtmosphereMaterial(
            planetRadius,
            atmosphereRadius,
            ReadString(component, "rayleighColor") ?? ReadString(component, "RayleighColor") ?? "#7fb6ff",
            ReadString(component, "mieColor") ?? ReadString(component, "MieColor") ?? "#ffffff",
            Math.Max(0, ReadNumber(component, "density", 1)),
            Math.Max(0.001, ReadNumber(component, "densityFalloff", 0.18)),
            Math.Max(0, ReadNumber(component, "rayleighScattering", 0.006)),
            Math.Max(0, ReadNumber(component, "mieScattering", 0.002)),
            Math.Clamp(ReadNumber(component, "mieAnisotropy", 0.76), -0.99, 0.99),
            Math.Max(0, ReadNumber(component, "sunIntensity", 22)),
            Math.Max(0, ReadNumber(component, "exposure", 1.2)),
            (int)Math.Clamp(Math.Round(ReadNumber(component, "viewSampleCount", 16)), 4, 32),
            (int)Math.Clamp(Math.Round(ReadNumber(component, "lightSampleCount", 8)), 2, 16),
            ReadString(component, "ozoneAbsorptionColor") ?? ReadString(component, "OzoneAbsorptionColor") ?? "#ffd199",
            Math.Max(0, ReadNumber(component, "ozoneAbsorption", 0)),
            Math.Clamp(ReadNumber(component, "aerialPerspectiveStrength", 0.38), 0, 2));
    }

    private static RekallAgeRuntimeViewportCloudLayerMaterial ReadCloudLayerMaterial(
        RekallAgeRuntimeComponent component,
        double cloudRadius,
        string color)
    {
        return new RekallAgeRuntimeViewportCloudLayerMaterial(
            cloudRadius,
            color,
            ReadBoolean(component, "alphaFromTextureOnly", ReadBoolean(component, "AlphaFromTextureOnly", true)),
            Math.Clamp(ReadNumber(component, "coverage", ReadNumber(component, "Coverage", 1)), 0, 4),
            Math.Clamp(ReadNumber(component, "lambertianStrength", ReadNumber(component, "LambertianStrength", 0.45)), 0, 1),
            Math.Clamp(ReadNumber(component, "ambientStrength", ReadNumber(component, "AmbientStrength", 0.18)), 0, 2));
    }

    private static IReadOnlyList<RekallAgeRuntimeComponent> ExpandCloudLayerComponents(
        IReadOnlyList<RekallAgeRuntimeComponent> components)
    {
        if (components.Count == 0)
        {
            return [];
        }

        var expanded = new List<RekallAgeRuntimeComponent>();
        foreach (var component in components)
        {
            var layers = ReadArray(component, "layers") ?? ReadArray(component, "Layers");
            if (layers is null || layers.Count == 0)
            {
                expanded.Add(component);
                continue;
            }

            foreach (var layer in layers.OfType<JsonObject>())
            {
                var properties = component.Properties.DeepClone().AsObject();
                properties.Remove("layers");
                properties.Remove("Layers");
                foreach (var property in layer)
                {
                    properties[property.Key] = property.Value?.DeepClone();
                }

                expanded.Add(new RekallAgeRuntimeComponent(component.Type, properties));
            }
        }

        return expanded;
    }

    private static RekallAgeRuntimeViewportCloudShadowMaterial? ReadCloudShadowMaterial(
        IReadOnlyList<RekallAgeRuntimeComponent> components,
        double planetRadius)
    {
        foreach (var component in components)
        {
            if (!ReadBoolean(component, "castShadows", ReadBoolean(component, "CastShadows", true)))
            {
                continue;
            }

            var cloudHeight = Math.Max(0, ReadNumber(component, "height", ReadNumber(component, "Height", 0.02)));
            return ReadCloudShadowMaterial(component, planetRadius + cloudHeight);
        }

        return null;
    }

    private static RekallAgeRuntimeViewportCloudShadowMaterial? ReadCloudShadowMaterial(
        RekallAgeRuntimeComponent? component,
        double cloudRadius)
    {
        if (component is null
            || !ReadBoolean(component, "castShadows", ReadBoolean(component, "CastShadows", true)))
        {
            return null;
        }

        var texture = ReadString(component, "texture") ?? ReadString(component, "Texture");
        if (string.IsNullOrWhiteSpace(texture))
        {
            return null;
        }

        return new RekallAgeRuntimeViewportCloudShadowMaterial(
            texture.Trim(),
            Math.Max(0.0001, cloudRadius),
            Math.Clamp(ReadNumber(component, "shadowStrength", ReadNumber(component, "ShadowStrength", 0.35)), 0, 1));
    }

    private static RekallAgeRuntimeViewportSurfaceWaterMaterial? ReadSurfaceWaterMaterial(
        RekallAgeRuntimeComponent? component)
    {
        var texture = ReadString(component, "waterTexture") ?? ReadString(component, "WaterTexture");
        if (string.IsNullOrWhiteSpace(texture))
        {
            return null;
        }

        return new RekallAgeRuntimeViewportSurfaceWaterMaterial(
            texture.Trim(),
            Math.Clamp(ReadNumber(component, "waterCoverage", ReadNumber(component, "WaterCoverage", 1)), 0, 4),
            Math.Clamp(ReadNumber(component, "waterSpecularStrength", ReadNumber(component, "WaterSpecularStrength", 2.5)), 0, 8),
            Math.Clamp(ReadNumber(component, "waterRoughness", ReadNumber(component, "WaterRoughness", 0.06)), 0.01, 1));
    }

    private static int ReadMeshSlices(RekallAgeRuntimeComponent? component, int fallback)
    {
        return (int)Math.Clamp(ReadNumber(component, "meshSlices", ReadNumber(component, "MeshSlices", fallback)), 0, 512);
    }

    private static int ReadMeshStacks(RekallAgeRuntimeComponent? component, int fallback)
    {
        return (int)Math.Clamp(ReadNumber(component, "meshStacks", ReadNumber(component, "MeshStacks", fallback)), 0, 256);
    }

    private static RekallAgeRuntimeViewportLineSegments? ReadLineSegments(RekallAgeRuntimeComponent? component)
    {
        if (component is null
            || !TryGetPropertyValue(component.Properties, "segments", out var segmentsNode)
            || segmentsNode is not JsonArray segmentsArray)
        {
            return null;
        }

        var segments = new List<RekallAgeRuntimeViewportLineSegment>(segmentsArray.Count);
        foreach (var node in segmentsArray)
        {
            if (node is not JsonObject segment)
            {
                continue;
            }

            var fromX = ReadNumber(segment, "fromX", 0);
            var fromY = ReadNumber(segment, "fromY", 0);
            var fromZ = ReadNumber(segment, "fromZ", 0);
            var toX = ReadNumber(segment, "toX", 0);
            var toY = ReadNumber(segment, "toY", 0);
            var toZ = ReadNumber(segment, "toZ", 0);
            if (Math.Abs(toX - fromX) + Math.Abs(toY - fromY) + Math.Abs(toZ - fromZ) <= 0.000001)
            {
                continue;
            }

            segments.Add(new RekallAgeRuntimeViewportLineSegment(fromX, fromY, fromZ, toX, toY, toZ));
        }

        return segments.Count == 0
            ? null
            : new RekallAgeRuntimeViewportLineSegments(
                segments,
                Math.Max(0.0001, ReadNumber(component, "thickness", 0.02)));
    }

    private static RekallAgeRuntimeViewportLineSegments? ReadTextLabelLineSegments(
        RekallAgeRuntimeComponent? component,
        RekallAgeRuntimeViewportCamera? activeCamera,
        RekallAgeRuntimeTransform transform,
        int viewportHeight)
    {
        if (component is null)
        {
            return null;
        }

        var text = ReadString(component, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var size = ResolveTextLabelWorldHeight(component, activeCamera, transform, viewportHeight);
        var thickness = Math.Max(0.0001, ReadNumber(component, "thickness", Math.Max(0.01, size * 0.03)));
        var originX = ReadNumber(component, "offsetX", 0);
        var originY = ReadNumber(component, "offsetY", 0);
        var originZ = ReadNumber(component, "offsetZ", 0);
        var cursor = 0.0;
        var segments = new List<RekallAgeRuntimeViewportLineSegment>();
        foreach (var character in text.Trim().ToUpperInvariant())
        {
            if (character == ' ')
            {
                cursor += size * 0.7;
                continue;
            }

            var glyph = StrokeGlyph(character);
            if (glyph.Count == 0)
            {
                cursor += size * 0.45;
                continue;
            }

            AddStrokeGlyphSegments(segments, glyph, originX + cursor, originY, originZ, size);
            cursor += size * 0.76;
        }

        return segments.Count == 0
            ? null
            : new RekallAgeRuntimeViewportLineSegments(segments, thickness);
    }

    private static double ResolveTextLabelWorldHeight(
        RekallAgeRuntimeComponent component,
        RekallAgeRuntimeViewportCamera? activeCamera,
        RekallAgeRuntimeTransform transform,
        int viewportHeight)
    {
        var authoredSize = Math.Max(0.0001, ReadNumber(component, "size", 1));
        var minimumPixels = Math.Max(
            0,
            ReadNumber(component, "minimumScreenHeightPixels", ReadNumber(component, "MinimumScreenHeightPixels", 0)));
        if (minimumPixels <= 0 || activeCamera is null || viewportHeight <= 0)
        {
            return authoredSize;
        }

        var worldHeight = activeCamera.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase)
            ? activeCamera.OrthographicSize * minimumPixels / viewportHeight
            : ResolvePerspectiveMinimumWorldHeight(activeCamera, transform, minimumPixels, viewportHeight);
        return Math.Max(authoredSize, worldHeight);
    }

    private static double ResolvePerspectiveMinimumWorldHeight(
        RekallAgeRuntimeViewportCamera activeCamera,
        RekallAgeRuntimeTransform transform,
        double minimumPixels,
        int viewportHeight)
    {
        var dx = transform.Position3D.X - activeCamera.X;
        var dy = transform.Position3D.Y - activeCamera.Y;
        var dz = transform.Position3D.Z - activeCamera.Z;
        var distance = Math.Max(
            activeCamera.NearClip,
            Math.Sqrt(dx * dx + dy * dy + dz * dz));
        var fovRadians = Math.Clamp(activeCamera.FieldOfViewDegrees, 1, 179) * Math.PI / 180.0;
        var visibleWorldHeight = 2 * distance * Math.Tan(fovRadians * 0.5);
        return visibleWorldHeight * minimumPixels / viewportHeight;
    }

    private static IReadOnlyList<GlyphStroke> StrokeGlyph(char character)
    {
        return StrokeGlyphs.TryGetValue(character, out var glyph)
            ? glyph
            : Array.Empty<GlyphStroke>();
    }

    private static void AddStrokeGlyphSegments(
        List<RekallAgeRuntimeViewportLineSegment> segments,
        IReadOnlyList<GlyphStroke> glyph,
        double originX,
        double originY,
        double originZ,
        double size)
    {
        var width = size * 0.56;
        var topZ = originZ - size * 0.5;
        foreach (var stroke in glyph)
        {
            segments.Add(new RekallAgeRuntimeViewportLineSegment(
                originX + width * stroke.FromX / 4.0,
                originY,
                topZ + size * stroke.FromY / 6.0,
                originX + width * stroke.ToX / 4.0,
                originY,
                topZ + size * stroke.ToY / 6.0));
        }
    }

    private static readonly IReadOnlyDictionary<char, GlyphStroke[]> StrokeGlyphs = new Dictionary<char, GlyphStroke[]>
    {
        ['A'] = [S(0, 6, 2, 0), S(2, 0, 4, 6), S(1, 3, 3, 3)],
        ['B'] = [S(0, 0, 0, 6), S(0, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 2), S(4, 2, 3, 3), S(0, 3, 3, 3), S(3, 3, 4, 4), S(4, 4, 4, 5), S(4, 5, 3, 6), S(0, 6, 3, 6)],
        ['C'] = [S(4, 1, 3, 0), S(3, 0, 1, 0), S(1, 0, 0, 1), S(0, 1, 0, 5), S(0, 5, 1, 6), S(1, 6, 3, 6), S(3, 6, 4, 5)],
        ['D'] = [S(0, 0, 0, 6), S(0, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 5), S(4, 5, 3, 6), S(3, 6, 0, 6)],
        ['E'] = [S(4, 0, 0, 0), S(0, 0, 0, 6), S(0, 3, 3, 3), S(0, 6, 4, 6)],
        ['F'] = [S(0, 0, 0, 6), S(0, 0, 4, 0), S(0, 3, 3, 3)],
        ['G'] = [S(4, 1, 3, 0), S(3, 0, 1, 0), S(1, 0, 0, 1), S(0, 1, 0, 5), S(0, 5, 1, 6), S(1, 6, 4, 6), S(4, 6, 4, 3), S(4, 3, 2, 3)],
        ['H'] = [S(0, 0, 0, 6), S(4, 0, 4, 6), S(0, 3, 4, 3)],
        ['I'] = [S(0, 0, 4, 0), S(2, 0, 2, 6), S(0, 6, 4, 6)],
        ['J'] = [S(4, 0, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5)],
        ['K'] = [S(0, 0, 0, 6), S(4, 0, 0, 3), S(0, 3, 4, 6)],
        ['L'] = [S(0, 0, 0, 6), S(0, 6, 4, 6)],
        ['M'] = [S(0, 6, 0, 0), S(0, 0, 2, 3), S(2, 3, 4, 0), S(4, 0, 4, 6)],
        ['N'] = [S(0, 6, 0, 0), S(0, 0, 4, 6), S(4, 6, 4, 0)],
        ['O'] = [S(1, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5), S(0, 5, 0, 1), S(0, 1, 1, 0)],
        ['P'] = [S(0, 0, 0, 6), S(0, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 2), S(4, 2, 3, 3), S(3, 3, 0, 3)],
        ['Q'] = [S(1, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5), S(0, 5, 0, 1), S(0, 1, 1, 0), S(2, 4, 4, 6)],
        ['R'] = [S(0, 0, 0, 6), S(0, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 2), S(4, 2, 3, 3), S(3, 3, 0, 3), S(0, 3, 4, 6)],
        ['S'] = [S(4, 1, 3, 0), S(3, 0, 1, 0), S(1, 0, 0, 1), S(0, 1, 0, 2), S(0, 2, 1, 3), S(1, 3, 3, 3), S(3, 3, 4, 4), S(4, 4, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5)],
        ['T'] = [S(0, 0, 4, 0), S(2, 0, 2, 6)],
        ['U'] = [S(0, 0, 0, 5), S(0, 5, 1, 6), S(1, 6, 3, 6), S(3, 6, 4, 5), S(4, 5, 4, 0)],
        ['V'] = [S(0, 0, 2, 6), S(2, 6, 4, 0)],
        ['W'] = [S(0, 0, 0, 6), S(0, 6, 2, 3), S(2, 3, 4, 6), S(4, 6, 4, 0)],
        ['X'] = [S(0, 0, 4, 6), S(4, 0, 0, 6)],
        ['Y'] = [S(0, 0, 2, 3), S(4, 0, 2, 3), S(2, 3, 2, 6)],
        ['Z'] = [S(0, 0, 4, 0), S(4, 0, 0, 6), S(0, 6, 4, 6)],
        ['0'] = [S(1, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5), S(0, 5, 0, 1), S(0, 1, 1, 0), S(0, 6, 4, 0)],
        ['1'] = [S(1, 1, 2, 0), S(2, 0, 2, 6), S(0, 6, 4, 6)],
        ['2'] = [S(0, 1, 1, 0), S(1, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 2), S(4, 2, 0, 6), S(0, 6, 4, 6)],
        ['3'] = [S(0, 0, 4, 0), S(4, 0, 2, 3), S(2, 3, 4, 3), S(4, 3, 4, 5), S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5)],
        ['4'] = [S(0, 0, 0, 3), S(0, 3, 4, 3), S(4, 0, 4, 6)],
        ['5'] = [S(4, 0, 0, 0), S(0, 0, 0, 3), S(0, 3, 3, 3), S(3, 3, 4, 4), S(4, 4, 4, 5), S(4, 5, 3, 6), S(3, 6, 0, 6)],
        ['6'] = [S(4, 1, 3, 0), S(3, 0, 1, 0), S(1, 0, 0, 1), S(0, 1, 0, 5), S(0, 5, 1, 6), S(1, 6, 3, 6), S(3, 6, 4, 5), S(4, 5, 4, 4), S(4, 4, 3, 3), S(3, 3, 0, 3)],
        ['7'] = [S(0, 0, 4, 0), S(4, 0, 1, 6)],
        ['8'] = [S(1, 0, 3, 0), S(3, 0, 4, 1), S(4, 1, 4, 2), S(4, 2, 3, 3), S(3, 3, 1, 3), S(1, 3, 0, 2), S(0, 2, 0, 1), S(0, 1, 1, 0), S(1, 3, 0, 4), S(0, 4, 0, 5), S(0, 5, 1, 6), S(1, 6, 3, 6), S(3, 6, 4, 5), S(4, 5, 4, 4), S(4, 4, 3, 3)],
        ['9'] = [S(4, 5, 3, 6), S(3, 6, 1, 6), S(1, 6, 0, 5), S(0, 5, 0, 4), S(0, 4, 1, 3), S(1, 3, 4, 3), S(4, 3, 4, 1), S(4, 1, 3, 0), S(3, 0, 1, 0), S(1, 0, 0, 1)],
        ['-'] = [S(0, 3, 4, 3)],
        ['_'] = [S(0, 6, 4, 6)]
    };

    private static GlyphStroke S(int fromX, int fromY, int toX, int toY)
    {
        return new GlyphStroke(fromX, fromY, toX, toY);
    }

    private readonly record struct GlyphStroke(int FromX, int FromY, int ToX, int ToY);

    private static RekallAgeRuntimeViewportGeometryMesh? ReadGeometryMesh(RekallAgeRuntimeComponent? component)
    {
        if (component is null
            || !component.Properties.TryGetPropertyValue("vertices", out var verticesNode)
            || verticesNode is not JsonArray verticesArray
            || !component.Properties.TryGetPropertyValue("indices", out var indicesNode)
            || indicesNode is not JsonArray indicesArray)
        {
            return null;
        }

        var indices = new List<ushort>(indicesArray.Count);
        foreach (var node in indicesArray)
        {
            if (node is not JsonValue value || !TryReadUInt16(value, verticesArray.Count, out var index))
            {
                return null;
            }

            indices.Add(index);
        }

        if (indices.Count < 3 || indices.Count % 3 != 0)
        {
            return null;
        }

        var materialColor = ParseColor(ReadString(component, "color"));
        var vertices = new List<ParsedGeometryVertex>(verticesArray.Count);
        foreach (var node in verticesArray)
        {
            if (node is not JsonObject vertex)
            {
                return null;
            }

            var color = ReadVertexColor(vertex, materialColor);
            vertices.Add(new ParsedGeometryVertex(
                ReadNumber(vertex, "x", 0),
                ReadNumber(vertex, "y", 0),
                ReadNumber(vertex, "z", 0),
                ReadOptionalNumber(vertex, "nx") ?? ReadOptionalNumber(vertex, "normalX"),
                ReadOptionalNumber(vertex, "ny") ?? ReadOptionalNumber(vertex, "normalY"),
                ReadOptionalNumber(vertex, "nz") ?? ReadOptionalNumber(vertex, "normalZ"),
                color.R,
                color.G,
                color.B,
                color.A,
                ReadNumber(vertex, "u", 0),
                ReadNumber(vertex, "v", 0)));
        }

        if (vertices.Count == 0 || vertices.Count > ushort.MaxValue)
        {
            return null;
        }

        return new RekallAgeRuntimeViewportGeometryMesh(CreateGeometryVertices(vertices, indices), indices);
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadOrbitPathMesh(
        RekallAgeRuntimeComponent? orbitComponent,
        RekallAgeRuntimeComponent? orbitPathComponent)
    {
        if (orbitComponent is null
            || orbitPathComponent is null
            || !ReadBoolean(orbitPathComponent, "active", true))
        {
            return null;
        }

        var semiMajorAxisKm = Math.Max(0, ReadNumber(orbitComponent, "semiMajorAxisKm", 0));
        if (semiMajorAxisKm <= 0)
        {
            return null;
        }

        var segments = (int)Math.Clamp(ReadNumber(orbitPathComponent, "segments", 128), 8, 512);
        var thickness = Math.Max(0.001, ReadNumber(orbitPathComponent, "thickness", 0.035));
        var eccentricity = Math.Clamp(ReadNumber(orbitComponent, "eccentricity", 0), 0, 0.999999);
        var distanceScale = ReadNumber(orbitComponent, "distanceScale", 1);
        var verticalOffset = ReadNumber(orbitPathComponent, "verticalOffset", -0.05);
        var inclination = DegreesToRadians(ReadNumber(orbitComponent, "inclinationDegrees", 0));
        var longitudeOfAscendingNode = DegreesToRadians(ReadNumber(orbitComponent, "longitudeOfAscendingNodeDegrees", 0));
        var argumentOfPeriapsis = DegreesToRadians(ReadNumber(orbitComponent, "argumentOfPeriapsisDegrees", 0));
        var color = ParseColor(ReadString(orbitPathComponent, "color") ?? "#88aaff");
        var vertices = new List<RekallAgeRuntimeViewportGeometryVertex>(segments * 2);
        var indices = new List<ushort>(segments * 6);
        var points = Enumerable.Range(0, segments)
            .Select(index =>
            {
                var eccentricAnomaly = index / (double)segments * Math.PI * 2;
                var x = semiMajorAxisKm * (Math.Cos(eccentricAnomaly) - eccentricity);
                var y = semiMajorAxisKm * Math.Sqrt(1 - eccentricity * eccentricity) * Math.Sin(eccentricAnomaly);
                var point = Multiply(RotateOrbitPlane(x, y, inclination, longitudeOfAscendingNode, argumentOfPeriapsis), distanceScale);
                return new MeshVector3(point.X, point.Y + verticalOffset, point.Z);
            })
            .ToArray();

        for (var index = 0; index < points.Length; index++)
        {
            var previous = points[(index + points.Length - 1) % points.Length];
            var next = points[(index + 1) % points.Length];
            var tangent = Normalize(new MeshVector3(next.X - previous.X, next.Y - previous.Y, next.Z - previous.Z));
            var side = Normalize(Cross(tangent, new MeshVector3(0, 1, 0)));
            if (side.LengthSquared <= 0.000001)
            {
                side = new MeshVector3(1, 0, 0);
            }

            var half = thickness * 0.5;
            var point = points[index];
            vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
                point.X + side.X * half,
                point.Y + side.Y * half,
                point.Z + side.Z * half,
                0,
                1,
                0,
                color.R,
                color.G,
                color.B,
                color.A));
            vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
                point.X - side.X * half,
                point.Y - side.Y * half,
                point.Z - side.Z * half,
                0,
                1,
                0,
                color.R,
                color.G,
                color.B,
                color.A));
        }

        for (var index = 0; index < segments; index++)
        {
            var a = checked((ushort)(index * 2));
            var b = checked((ushort)(index * 2 + 1));
            var c = checked((ushort)(((index + 1) % segments) * 2));
            var d = checked((ushort)(((index + 1) % segments) * 2 + 1));
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
            indices.Add(b);
            indices.Add(c);
            indices.Add(d);
        }

        return new RekallAgeRuntimeViewportGeometryMesh(vertices, indices);
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadRingMesh(RekallAgeRuntimeComponent? ringComponent)
    {
        if (ringComponent is null)
        {
            return null;
        }

        var innerRadius = Math.Max(0.0001, ReadNumber(ringComponent, "innerRadius", ReadNumber(ringComponent, "InnerRadius", 1)));
        var outerRadius = Math.Max(innerRadius + 0.0001, ReadNumber(ringComponent, "outerRadius", ReadNumber(ringComponent, "OuterRadius", 2)));
        var segments = (int)Math.Clamp(ReadNumber(ringComponent, "segments", ReadNumber(ringComponent, "Segments", 192)), 16, 512);
        var color = ParseColor(ReadString(ringComponent, "color") ?? ReadString(ringComponent, "Color") ?? "#ffffffcc");
        var vertices = new List<RekallAgeRuntimeViewportGeometryVertex>(segments * 2);
        var indices = new List<ushort>(segments * 6);

        for (var index = 0; index < segments; index++)
        {
            var angle = index / (double)segments * Math.PI * 2;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var v = index / (double)segments;
            vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
                cos * outerRadius,
                0,
                sin * outerRadius,
                0,
                1,
                0,
                color.R,
                color.G,
                color.B,
                color.A,
                1,
                v));
            vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
                cos * innerRadius,
                0,
                sin * innerRadius,
                0,
                1,
                0,
                color.R,
                color.G,
                color.B,
                color.A,
                0,
                v));
        }

        for (var index = 0; index < segments; index++)
        {
            var a = checked((ushort)(index * 2));
            var b = checked((ushort)(index * 2 + 1));
            var c = checked((ushort)(((index + 1) % segments) * 2));
            var d = checked((ushort)(((index + 1) % segments) * 2 + 1));
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
            indices.Add(b);
            indices.Add(c);
            indices.Add(d);
        }

        return new RekallAgeRuntimeViewportGeometryMesh(vertices, indices);
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadStarfieldMesh(RekallAgeRuntimeComponent? starfieldComponent)
    {
        if (starfieldComponent is null)
        {
            return null;
        }

        var count = (int)Math.Clamp(Math.Round(ReadNumber(starfieldComponent, "count", 1200)), 1, 8000);
        var radius = Math.Max(1, ReadNumber(starfieldComponent, "radius", 18000));
        var size = Math.Max(0.0001, ReadNumber(starfieldComponent, "size", 2.5));
        var seed = (int)Math.Clamp(Math.Round(ReadNumber(starfieldComponent, "seed", 1337)), int.MinValue, int.MaxValue);
        var milkyWayStrength = Math.Clamp(ReadNumber(starfieldComponent, "milkyWayStrength", 0.35), 0, 1);
        var color = ParseColor(ReadString(starfieldComponent, "color") ?? "#dce8ffff");
        var random = new Random(seed);
        var vertices = new List<RekallAgeRuntimeViewportGeometryVertex>(count * 4);
        var indices = new List<ushort>(count * 6);

        for (var i = 0; i < count; i++)
        {
            var direction = random.NextDouble() < milkyWayStrength
                ? MilkyWayDirection(random)
                : UniformSphereDirection(random);
            var center = Multiply(direction, radius);
            var normal = Multiply(direction, -1);
            var referenceUp = Math.Abs(normal.Y) > 0.92
                ? new MeshVector3(1, 0, 0)
                : new MeshVector3(0, 1, 0);
            var right = Normalize(Cross(referenceUp, normal));
            var up = Normalize(Cross(normal, right));
            var brightness = 0.45 + random.NextDouble() * random.NextDouble() * 0.95;
            var half = size * (0.35 + brightness * 0.85);
            var baseIndex = checked((ushort)vertices.Count);
            var r = Math.Clamp(color.R * brightness, 0, 1);
            var g = Math.Clamp(color.G * brightness, 0, 1);
            var b = Math.Clamp(color.B * brightness, 0, 1);

            AddStarVertex(vertices, Add(center, Add(Multiply(right, -half), Multiply(up, -half))), normal, r, g, b, color.A, 0, 0);
            AddStarVertex(vertices, Add(center, Add(Multiply(right, half), Multiply(up, -half))), normal, r, g, b, color.A, 1, 0);
            AddStarVertex(vertices, Add(center, Add(Multiply(right, half), Multiply(up, half))), normal, r, g, b, color.A, 1, 1);
            AddStarVertex(vertices, Add(center, Add(Multiply(right, -half), Multiply(up, half))), normal, r, g, b, color.A, 0, 1);
            indices.Add(baseIndex);
            indices.Add((ushort)(baseIndex + 1));
            indices.Add((ushort)(baseIndex + 2));
            indices.Add(baseIndex);
            indices.Add((ushort)(baseIndex + 2));
            indices.Add((ushort)(baseIndex + 3));
        }

        return new RekallAgeRuntimeViewportGeometryMesh(vertices, indices);
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadMarkerMesh(RekallAgeRuntimeComponent? markerComponent)
    {
        if (markerComponent is null)
        {
            return null;
        }

        var size = Math.Max(0.0001, ReadNumber(markerComponent, "size", 1));
        var verticalOffset = ReadNumber(markerComponent, "verticalOffset", 0);
        var color = ParseColor(ReadString(markerComponent, "color") ?? "#ffffffcc");
        var vertices = new List<RekallAgeRuntimeViewportGeometryVertex>(4)
        {
            new(0, verticalOffset, -size, 0, 1, 0, color.R, color.G, color.B, color.A, 0.5, 0),
            new(size, verticalOffset, 0, 0, 1, 0, color.R, color.G, color.B, color.A, 1, 0.5),
            new(0, verticalOffset, size, 0, 1, 0, color.R, color.G, color.B, color.A, 0.5, 1),
            new(-size, verticalOffset, 0, 0, 1, 0, color.R, color.G, color.B, color.A, 0, 0.5)
        };
        return new RekallAgeRuntimeViewportGeometryMesh(vertices, [0, 2, 1, 0, 3, 2]);
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadHaloMesh(RekallAgeRuntimeComponent? haloComponent)
    {
        if (haloComponent is null)
        {
            return null;
        }

        var radius = Math.Max(0.0001, ReadNumber(haloComponent, "radius", 1));
        var verticalOffset = ReadNumber(haloComponent, "verticalOffset", 0);
        var segments = (int)Math.Clamp(Math.Round(ReadNumber(haloComponent, "segments", 48)), 8, 256);
        var rings = (int)Math.Clamp(Math.Round(ReadNumber(haloComponent, "rings", 1)), 1, 16);
        var falloff = Math.Clamp(ReadNumber(haloComponent, "falloff", 1), 0.1, 8);
        var color = ParseColor(ReadString(haloComponent, "color") ?? "#ffffff88");
        var vertices = new List<RekallAgeRuntimeViewportGeometryVertex>(1 + rings * segments)
        {
            new(0, verticalOffset, 0, 0, 1, 0, color.R, color.G, color.B, color.A, 0.5, 0.5)
        };
        var indices = new List<ushort>(segments * 3 + Math.Max(0, rings - 1) * segments * 6);
        for (var ring = 1; ring <= rings; ring++)
        {
            var t = ring / (double)rings;
            var ringRadius = radius * t;
            var alpha = ring == rings ? 0 : color.A * Math.Pow(1 - t, falloff);
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (double)segments * Math.PI * 2;
                var x = Math.Cos(angle) * ringRadius;
                var z = Math.Sin(angle) * ringRadius;
                vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
                    x,
                    verticalOffset,
                    z,
                    0,
                    1,
                    0,
                    color.R,
                    color.G,
                    color.B,
                    alpha,
                    0.5 + Math.Cos(angle) * t * 0.5,
                    0.5 + Math.Sin(angle) * t * 0.5));
            }
        }

        for (var index = 0; index < segments; index++)
        {
            indices.Add(0);
            indices.Add((ushort)(1 + index));
            indices.Add((ushort)(1 + ((index + 1) % segments)));
        }

        for (var ring = 2; ring <= rings; ring++)
        {
            var innerStart = 1 + (ring - 2) * segments;
            var outerStart = 1 + (ring - 1) * segments;
            for (var index = 0; index < segments; index++)
            {
                var next = (index + 1) % segments;
                var a = checked((ushort)(innerStart + index));
                var b = checked((ushort)(outerStart + index));
                var c = checked((ushort)(outerStart + next));
                var d = checked((ushort)(innerStart + next));
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                indices.Add(a);
                indices.Add(c);
                indices.Add(d);
            }
        }

        return new RekallAgeRuntimeViewportGeometryMesh(vertices, indices);
    }

    private static MeshVector3 UniformSphereDirection(Random random)
    {
        var z = random.NextDouble() * 2 - 1;
        var theta = random.NextDouble() * Math.PI * 2;
        var radius = Math.Sqrt(Math.Max(0, 1 - z * z));
        return new MeshVector3(
            Math.Cos(theta) * radius,
            z,
            Math.Sin(theta) * radius);
    }

    private static MeshVector3 MilkyWayDirection(Random random)
    {
        var longitude = random.NextDouble() * Math.PI * 2;
        var latitude = (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5) * 0.16;
        var cosLatitude = Math.Cos(latitude);
        return Normalize(new MeshVector3(
            Math.Cos(longitude) * cosLatitude,
            Math.Sin(latitude),
            Math.Sin(longitude) * cosLatitude));
    }

    private static void AddStarVertex(
        List<RekallAgeRuntimeViewportGeometryVertex> vertices,
        MeshVector3 position,
        MeshVector3 normal,
        double r,
        double g,
        double b,
        double a,
        double u,
        double v)
    {
        vertices.Add(new RekallAgeRuntimeViewportGeometryVertex(
            position.X,
            position.Y,
            position.Z,
            normal.X,
            normal.Y,
            normal.Z,
            r,
            g,
            b,
            a,
            u,
            v));
    }

    private static bool ReadBoolean(RekallAgeRuntimeComponent? component, string name, bool fallback)
    {
        if (component is null || !TryGetPropertyValue(component.Properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static SceneColor ReadVertexColor(JsonObject vertex, SceneColor fallback)
    {
        return new SceneColor(
            ReadUnit(vertex, "r", fallback.R),
            ReadUnit(vertex, "g", fallback.G),
            ReadUnit(vertex, "b", fallback.B),
            ReadUnit(vertex, "a", fallback.A));
    }

    private static double ReadUnit(JsonObject properties, string name, double fallback)
    {
        return Math.Clamp(ReadNumber(properties, name, fallback), 0, 1);
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        if (name.Length > 0)
        {
            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (properties.TryGetPropertyValue(pascalName, out node))
            {
                return true;
            }
        }

        node = null;
        return false;
    }

    private static double? ReadOptionalNumber(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node) && node is JsonValue value
            ? ReadNumber(value)
            : null;
    }

    private static double? ReadNumber(JsonValue value)
    {
        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportGeometryVertex> CreateGeometryVertices(
        IReadOnlyList<ParsedGeometryVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        var inferredNormals = InferNormals(vertices, indices);
        var result = new RekallAgeRuntimeViewportGeometryVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var normal = ResolveNormal(vertex, inferredNormals[i]);
            result[i] = new RekallAgeRuntimeViewportGeometryVertex(
                vertex.X,
                vertex.Y,
                vertex.Z,
                normal.X,
                normal.Y,
                normal.Z,
                vertex.R,
                vertex.G,
                vertex.B,
                vertex.A,
                vertex.U,
                vertex.V);
        }

        return result;
    }

    private static IReadOnlyList<MeshVector3> InferNormals(
        IReadOnlyList<ParsedGeometryVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        var normals = Enumerable.Repeat(new MeshVector3(0, 0, 0), vertices.Count).ToArray();
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            var a = vertices[aIndex];
            var b = vertices[bIndex];
            var c = vertices[cIndex];
            var normal = Normalize(Cross(
                new MeshVector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                new MeshVector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z)));
            normals[aIndex] = Add(normals[aIndex], normal);
            normals[bIndex] = Add(normals[bIndex], normal);
            normals[cIndex] = Add(normals[cIndex], normal);
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = Normalize(normals[i]);
        }

        return normals;
    }

    private static MeshVector3 ResolveNormal(ParsedGeometryVertex vertex, MeshVector3 inferred)
    {
        if (vertex.NormalX.HasValue || vertex.NormalY.HasValue || vertex.NormalZ.HasValue)
        {
            return Normalize(new MeshVector3(vertex.NormalX ?? 0, vertex.NormalY ?? 1, vertex.NormalZ ?? 0));
        }

        return inferred.LengthSquared <= 0.000001 ? new MeshVector3(0, 1, 0) : inferred;
    }

    private static bool TryReadUInt16(JsonValue value, int vertexCount, out ushort index)
    {
        index = 0;
        int integer;
        if (!value.TryGetValue<int>(out integer))
        {
            if (value.TryGetValue<long>(out var longValue) && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                integer = (int)longValue;
            }
            else
            {
                return false;
            }
        }

        if (integer < 0 || integer >= vertexCount || integer > ushort.MaxValue)
        {
            return false;
        }

        index = (ushort)integer;
        return true;
    }

    private static SceneColor ParseColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            var a = color.Length == 9
                && byte.TryParse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedAlpha)
                    ? parsedAlpha
                    : (byte)255;
            return new SceneColor(r / 255d, g / 255d, b / 255d, a / 255d);
        }

        return new SceneColor(0.35, 0.58, 0.85, 1);
    }

    private enum Axis
    {
        X,
        Y,
        Z
    }

    private readonly record struct SceneColor(double R, double G, double B, double A);

    private readonly record struct ParsedGeometryVertex(
        double X,
        double Y,
        double Z,
        double? NormalX,
        double? NormalY,
        double? NormalZ,
        double R,
        double G,
        double B,
        double A,
        double U,
        double V);

    private sealed record LodSelection(
        double MinDistance,
        double? MaxDistance,
        string? AssetId,
        string? Primitive,
        string? TextureAssetId,
        string? MaterialColor,
        double ScaleMultiplier)
    {
        public string? Variant => Primitive is null ? null : $"rekall.geometry.{Primitive}";
    }

    private readonly record struct MeshVector3(double X, double Y, double Z)
    {
        public double LengthSquared => X * X + Y * Y + Z * Z;
    }

    private sealed class LineSegmentsBuilder
    {
        private readonly double _thickness;
        private readonly List<RekallAgeRuntimeViewportLineSegment> _segments = [];

        public LineSegmentsBuilder(double thickness)
        {
            _thickness = Math.Max(0.001, thickness);
        }

        public void AddSegment(MeshVector3 from, MeshVector3 to)
        {
            if (new MeshVector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z).LengthSquared <= 0.000001)
            {
                return;
            }

            _segments.Add(new RekallAgeRuntimeViewportLineSegment(
                from.X,
                from.Y,
                from.Z,
                to.X,
                to.Y,
                to.Z));
        }

        public RekallAgeRuntimeViewportLineSegments Build()
        {
            return new RekallAgeRuntimeViewportLineSegments(_segments, _thickness);
        }
    }

    private static MeshVector3 Add(MeshVector3 left, MeshVector3 right)
    {
        return new MeshVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static MeshVector3 Cross(MeshVector3 left, MeshVector3 right)
    {
        return new MeshVector3(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private static MeshVector3 Normalize(MeshVector3 value)
    {
        var length = Math.Sqrt(value.LengthSquared);
        return length <= 0.000001
            ? new MeshVector3(0, 0, 0)
            : new MeshVector3(value.X / length, value.Y / length, value.Z / length);
    }

    private static MeshVector3 Multiply(MeshVector3 value, double scalar)
    {
        return new MeshVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }

    private static MeshVector3 RotateOrbitPlane(
        double x,
        double y,
        double inclination,
        double longitudeOfAscendingNode,
        double argumentOfPeriapsis)
    {
        var cosNode = Math.Cos(longitudeOfAscendingNode);
        var sinNode = Math.Sin(longitudeOfAscendingNode);
        var cosInc = Math.Cos(inclination);
        var sinInc = Math.Sin(inclination);
        var cosArg = Math.Cos(argumentOfPeriapsis);
        var sinArg = Math.Sin(argumentOfPeriapsis);

        return new MeshVector3(
            (cosNode * cosArg - sinNode * sinArg * cosInc) * x
                + (-cosNode * sinArg - sinNode * cosArg * cosInc) * y,
            (sinArg * sinInc) * x + (cosArg * sinInc) * y,
            (sinNode * cosArg + cosNode * sinArg * cosInc) * x
                + (-sinNode * sinArg + cosNode * cosArg * cosInc) * y);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
