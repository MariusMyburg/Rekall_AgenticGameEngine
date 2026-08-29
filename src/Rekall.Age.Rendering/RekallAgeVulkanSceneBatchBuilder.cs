using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanSceneBatchBuilder
{
    public RekallAgeVulkanSceneBatch Build(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        string? primaryLightEntityId = null,
        RekallAgeVulkanDirectionalLightInjection? directionalLight = null,
        RekallAgeVulkanEffectiveCamera? effectiveCamera = null)
    {
        var renderablesByEntityId = BuildRenderableLookup(frame);
        var vertices = BuildLocalVertices(meshes);
        var indices = FlattenIndices(renderablesByEntityId, frame.ActiveCamera, meshes, out var draws, out var bounds);
        effectiveCamera ??= ResolveEffectiveCamera(frame, bounds);
        return new RekallAgeVulkanSceneBatch(
            vertices,
            indices,
            draws,
            BuildFrameUniform(frame, renderablesByEntityId, effectiveCamera, primaryLightEntityId, directionalLight),
            BuildStereoFrame(frame, bounds))
        {
            EffectiveCamera = effectiveCamera
        };
    }

    public RekallAgeVulkanSceneBatch BuildDynamic(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        RekallAgeVulkanSceneBatch stableBatch,
        string? primaryLightEntityId = null,
        RekallAgeVulkanDirectionalLightInjection? directionalLight = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(stableBatch);
        if (meshes.Count != stableBatch.Draws.Count)
        {
            throw new ArgumentException("Stable batch topology must contain one draw for each source mesh.", nameof(stableBatch));
        }

        var renderablesByEntityId = BuildRenderableLookup(frame);
        var draws = new RekallAgeVulkanSceneDraw[stableBatch.Draws.Count];
        for (var i = 0; i < draws.Length; i++)
        {
            renderablesByEntityId.TryGetValue(meshes[i].EntityId, out var renderable);
            draws[i] = stableBatch.Draws[i] with
            {
                Model = CreateModelMatrix(renderable, frame.ActiveCamera)
            };
        }

        var effectiveCamera = frame.ActiveCamera is null || IsDefaultCamera(frame.ActiveCamera)
            ? stableBatch.EffectiveCamera
            : ResolveEffectiveCamera(frame, SceneBounds.Empty);
        return new RekallAgeVulkanSceneBatch(
            stableBatch.Vertices,
            stableBatch.Indices,
            draws,
            BuildFrameUniform(frame, renderablesByEntityId, effectiveCamera, primaryLightEntityId, directionalLight),
            BuildStereoFrame(frame, SceneBounds.Empty))
        {
            EffectiveCamera = effectiveCamera
        };
    }

    public RekallAgeVulkanEffectiveCamera ResolveEffectiveCamera(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(meshes);
        var renderablesByEntityId = BuildRenderableLookup(frame);
        var bounds = SceneBounds.Empty;
        foreach (var mesh in meshes)
        {
            renderablesByEntityId.TryGetValue(mesh.EntityId, out var renderable);
            var model = CreateModelMatrix(renderable, frame.ActiveCamera);
            foreach (var vertex in mesh.Vertices)
            {
                bounds = bounds.Include(Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), model));
            }
        }

        return ResolveEffectiveCamera(frame, bounds);
    }

    private static IReadOnlyList<RekallAgeVulkanSceneVertex> BuildLocalVertices(
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var vertexCount = 0;
        foreach (var mesh in meshes)
        {
            vertexCount = checked(vertexCount + mesh.Vertices.Count);
        }

        var vertices = new List<RekallAgeVulkanSceneVertex>(vertexCount);
        foreach (var mesh in meshes)
        {
            vertices.AddRange(mesh.Vertices);
        }

        return vertices;
    }

    private static IReadOnlyList<uint> FlattenIndices(
        IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> renderablesByEntityId,
        RekallAgeRuntimeViewportCamera? activeCamera,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        out IReadOnlyList<RekallAgeVulkanSceneDraw> draws,
        out SceneBounds bounds)
    {
        var indexCount = 0;
        foreach (var mesh in meshes)
        {
            indexCount = checked(indexCount + mesh.Indices.Count);
        }

        var indices = new List<uint>(indexCount);
        var ranges = new List<RekallAgeVulkanSceneDraw>(meshes.Count);
        bounds = SceneBounds.Empty;
        var vertexOffset = 0;
        foreach (var mesh in meshes)
        {
            renderablesByEntityId.TryGetValue(mesh.EntityId, out var renderable);
            var model = CreateModelMatrix(renderable, activeCamera);
            var isAtmosphereShell = mesh.Atmosphere is not null
                && mesh.Primitive.Equals("atmosphere", StringComparison.Ordinal);
            var isTransparent = isAtmosphereShell
                || mesh.CloudLayer is not null
                || mesh.Primitive.Equals("halo", StringComparison.Ordinal)
                || mesh.AlphaMode.Equals("blend", StringComparison.OrdinalIgnoreCase);
            ranges.Add(new RekallAgeVulkanSceneDraw(
                (uint)indices.Count,
                (uint)mesh.Indices.Count,
                vertexOffset,
                (uint)mesh.Vertices.Count,
                model,
                mesh.BaseColorTexture?.Id,
                mesh.MetallicRoughnessTexture?.Id,
                mesh.NormalTexture?.Id,
                mesh.OcclusionTexture?.Id,
                mesh.EmissiveTexture?.Id,
                mesh.CloudShadow?.TextureAssetId,
                mesh.SurfaceWater?.TextureAssetId,
                isAtmosphereShell
                    ? new Vector4(
                        Math.Clamp(mesh.Atmosphere!.ViewSampleCount, 4, 32),
                        Math.Clamp(mesh.Atmosphere.LightSampleCount, 2, 16),
                        0,
                        0)
                    : new Vector4(
                        Math.Clamp(mesh.MetallicFactor, 0, 1),
                        Math.Clamp(mesh.RoughnessFactor, 0.04f, 1),
                        mesh.NormalTexture is null ? 0 : Math.Clamp(mesh.NormalScale, 0, 4),
                        mesh.OcclusionTexture is null ? 0 : Math.Clamp(mesh.OcclusionStrength, 0, 1)),
                new Vector4(
                    Math.Clamp(mesh.EmissiveFactor.X, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.Y, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.Z, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.W, 0, 64)),
                mesh.Atmosphere?.AtmosphereFactors ?? Vector4.Zero,
                mesh.Atmosphere is null
                    ? Vector4.Zero
                    : isAtmosphereShell
                        ? mesh.Atmosphere.ScatteringFactors
                        : new Vector4(
                            mesh.Atmosphere.ScatteringFactors.X,
                            mesh.Atmosphere.ScatteringFactors.Y,
                            mesh.Atmosphere.ScatteringFactors.Z,
                            -MathF.Max(mesh.Atmosphere.ScatteringFactors.W, 0.0001f)),
                mesh.Atmosphere?.RayleighColor ?? Vector4.Zero,
                mesh.Atmosphere is null
                    ? Vector4.Zero
                    : new Vector4(
                        mesh.Atmosphere.MieColor.X,
                        mesh.Atmosphere.MieColor.Y,
                        mesh.Atmosphere.MieColor.Z,
                        mesh.Atmosphere.AerialPerspectiveStrength),
                mesh.Atmosphere?.OzoneFactors ?? Vector4.Zero,
                mesh.CloudLayer?.Factors ?? Vector4.Zero,
                mesh.CloudLayer?.Color ?? Vector4.Zero,
                mesh.CloudShadow?.Factors ?? Vector4.Zero,
                mesh.SurfaceWater?.Factors ?? Vector4.Zero,
                isTransparent,
                mesh.ShaderPipeline,
                mesh.EntityId,
                mesh.CastShadows,
                mesh.ReceiveShadows,
                mesh.ShadowLayerMask,
                mesh.AlphaMode,
                mesh.AlphaCutoff));
            foreach (var vertex in mesh.Vertices)
            {
                var world = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), model);
                bounds = bounds.Include(world);
            }

            indices.AddRange(mesh.Indices);
            vertexOffset = checked(vertexOffset + mesh.Vertices.Count);
        }

        draws = ranges;
        return indices;
    }

    private static RekallAgeVulkanSceneFrameUniform BuildFrameUniform(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> renderablesByEntityId,
        RekallAgeVulkanEffectiveCamera camera,
        string? primaryLightEntityId,
        RekallAgeVulkanDirectionalLightInjection? directionalLight)
    {
        var renderables = renderablesByEntityId.Values;
        var light = directionalLight is null
            ? string.IsNullOrWhiteSpace(primaryLightEntityId)
                ? ResolveFirstDirectionalLight(renderables) ?? ResolvePrimaryLight(renderablesByEntityId, renderables, primaryLightEntityId: null)
                : ResolvePrimaryLight(renderablesByEntityId, renderables, primaryLightEntityId)
            : directionalLight.Available
                ? new SceneLight(
                    directionalLight.EntityId,
                    directionalLight.Direction,
                    Vector4.Zero,
                    directionalLight.Color)
                : SceneLight.Disabled;
        var pointLightBudget = Math.Clamp(
            frame.ResolvedQualityPlan?.Lighting.MaximumPointLights ?? 4,
            1,
            16);
        var pointLightCandidates = ResolvePointLights(renderables, int.MaxValue);
        var pointLights = pointLightCandidates.Take(pointLightBudget).ToArray();
        var additionalLight = pointLights.Length == 0 ? SceneLight.Disabled : pointLights[0];
        if (string.Equals(light.EntityId, additionalLight.EntityId, StringComparison.Ordinal))
        {
            additionalLight = SceneLight.Disabled;
        }
        const int spotLightBudget = 4;
        var spotLightCandidates = ResolveSpotLights(renderables, int.MaxValue);
        var spotLights = spotLightCandidates.Take(spotLightBudget).ToArray();
        var environment = frame.Environment;
        var environmentParameters = environment is null
            ? new Vector4(1, 0, 11.2f, 0)
            : new Vector4(
                (float)Math.Clamp(environment.AmbientEnergy, 0, 16),
                (float)Math.Clamp(environment.Exposure, -8, 8),
                (float)Math.Clamp(environment.WhitePoint, 0.1, 64),
                environment.ToneMapper.Equals("agx", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        var environmentAmbientSkyColor = new Vector4(
            ParseColor(environment?.AmbientSkyColor),
            string.IsNullOrWhiteSpace(environment?.SkyAssetId) ? 0 : 1);
        var environmentAmbientGroundColor = new Vector4(ParseColor(environment?.AmbientGroundColor), 1);
        return new RekallAgeVulkanSceneFrameUniform(
            camera.ViewProjection,
            light.Direction,
            light.Color,
            light.Position,
            new Vector4(camera.Position, 1),
            camera.SoftwareViewProjection,
            additionalLight.Direction,
            additionalLight.Color,
            additionalLight.Position,
            new Vector4(additionalLight.Range, additionalLight.Priority, 0, 0),
            environmentParameters)
        {
            EnvironmentAmbientSkyColor = environmentAmbientSkyColor,
            EnvironmentAmbientGroundColor = environmentAmbientGroundColor,
            PointLightBudget = pointLightBudget,
            PointLights = pointLights.Select(item => new RekallAgeVulkanPointLight(
                item.EntityId ?? string.Empty, item.Color, item.Position, new Vector4(item.Range, item.Priority, 0, 0))).ToArray(),
            DroppedPointLightEntityIds = pointLightCandidates
                .Skip(pointLightBudget)
                .Select(item => item.EntityId ?? string.Empty)
                .ToArray(),
            SpotLightBudget = spotLightBudget,
            SpotLights = spotLights.Select(item => new RekallAgeVulkanSpotLight(
                item.EntityId ?? string.Empty,
                item.Color,
                item.Position,
                new Vector4(item.Direction, 0),
                new Vector4(
                    item.Range,
                    item.Priority,
                    MathF.Cos(item.InnerConeAngle * MathF.PI / 180f),
                    MathF.Cos(item.OuterConeAngle * MathF.PI / 180f)))).ToArray(),
            DroppedSpotLightEntityIds = spotLightCandidates
                .Skip(spotLightBudget)
                .Select(item => item.EntityId ?? string.Empty)
                .ToArray()
        };
    }

    private static IReadOnlyList<SceneLight> ResolvePointLights(
        IEnumerable<RekallAgeRuntimeViewportRenderable> renderables,
        int maximumCount)
    {
        return renderables
            .Where(renderable => renderable.Kind.Equals("light", StringComparison.Ordinal)
                && renderable.Intensity > 0.0001
                && IsPointLight(renderable))
            .OrderByDescending(renderable => renderable.LightPriority)
            .ThenByDescending(renderable => renderable.Intensity)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .Take(maximumCount)
            .Select(ToSceneLight)
            .ToArray();
    }

    private static SceneLight? ResolveFirstDirectionalLight(
        IEnumerable<RekallAgeRuntimeViewportRenderable> renderables)
    {
        var directional = renderables.FirstOrDefault(renderable =>
            renderable.Kind.Equals("light", StringComparison.Ordinal)
            && renderable.Intensity > 0.0001
            && !IsPointLight(renderable));
        return directional is null ? null : ToSceneLight(directional);
    }

    private static RekallAgeVulkanEffectiveCamera ResolveEffectiveCamera(
        RekallAgeRuntimeViewportFrame frame,
        SceneBounds bounds)
    {
        bounds = bounds.OrDefault();
        var center = new Vector3(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f,
            (bounds.MinZ + bounds.MaxZ) * 0.5f);
        var extent = MathF.Max(1f, MathF.Max(
            bounds.MaxX - bounds.MinX,
            MathF.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        var authored = frame.ActiveCamera;
        var autoFramed = authored is null || IsDefaultCamera(authored);
        var pose = ResolveCameraPose(authored, center, extent);
        var screenRight = NormalizeOrFallback(Vector3.Cross(pose.Forward, pose.Up), pose.Right);
        var screenUp = NormalizeOrFallback(Vector3.Cross(screenRight, pose.Forward), pose.Up);
        var view = Matrix4x4.CreateLookAt(pose.Eye, pose.Eye + pose.Forward, pose.Up);
        var softwareProjection = CreateProjection(authored, frame, extent);
        var softwareViewProjection = view * softwareProjection;
        var projection = softwareProjection;
        projection.M22 *= -1f;
        var nearClip = MathF.Max(0.001f, (float)(authored?.NearClip ?? 0.05));
        var farClip = MathF.Max(nearClip + 0.001f, (float)(authored?.FarClip ?? Math.Max(100f, extent * 16f)));
        var aspect = frame.Height <= 0 ? 1f : frame.Width / (float)frame.Height;
        var orthographic = authored?.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase) == true;
        var tangentOrHalfHeight = orthographic
            ? MathF.Max(0.001f, (float)(authored?.OrthographicSize ?? 10)) * 0.5f
            : MathF.Tan(ToRadians(Math.Clamp((float)(authored?.FieldOfViewDegrees ?? 65), 1f, 179f)) * 0.5f);
        return new RekallAgeVulkanEffectiveCamera(
            authored?.EntityId,
            pose.Eye,
            autoFramed
                ? new Vector3(0, 180, 0)
                : new Vector3((float)authored!.RotationX, (float)authored.RotationY, (float)authored.RotationZ),
            pose.Forward,
            screenRight,
            screenUp,
            nearClip,
            farClip,
            aspect,
            tangentOrHalfHeight,
            orthographic,
            autoFramed,
            view,
            projection,
            view * projection,
            softwareViewProjection);
    }

    private static RekallAgeVulkanSceneStereoFrame? BuildStereoFrame(
        RekallAgeRuntimeViewportFrame frame,
        SceneBounds bounds)
    {
        var camera = frame.HeadsetCamera ?? frame.ActiveCamera;
        if (frame.Stereo is not { Enabled: true } stereo || camera is null)
        {
            return null;
        }

        bounds = bounds.OrDefault();
        var center = new Vector3(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f,
            (bounds.MinZ + bounds.MaxZ) * 0.5f);
        var extent = MathF.Max(1f, MathF.Max(
            bounds.MaxX - bounds.MinX,
            MathF.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        var pose = ResolveCameraPose(camera, center, extent);
        var projection = CreateProjection(camera, frame, extent);
        projection.M22 *= -1f;
        var views = stereo.Eyes
            .Select(eye =>
            {
                var offset = pose.Right * (float)eye.OffsetX
                    + pose.Up * (float)eye.OffsetY
                    + pose.Forward * (float)eye.OffsetZ;
                var eyePosition = pose.Eye + offset;
                var view = Matrix4x4.CreateLookAt(eyePosition, eyePosition + pose.Forward, pose.Up);
                return new RekallAgeVulkanSceneViewUniform(
                    eye.Name,
                    eye.Index,
                    view * projection,
                    new Vector4(eyePosition, 1),
                    new Vector4(
                        (float)eye.ViewportX,
                        (float)eye.ViewportY,
                        (float)Math.Max(1, eye.ViewportWidth),
                        (float)Math.Max(1, eye.ViewportHeight)));
            })
            .OrderBy(view => view.Index)
            .ToArray();
        return new RekallAgeVulkanSceneStereoFrame(
            true,
            stereo.RenderMode,
            stereo.PreferSinglePassMultiview,
            views);
    }

    private static IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> BuildRenderableLookup(
        RekallAgeRuntimeViewportFrame frame)
    {
        var lookup = new Dictionary<string, RekallAgeRuntimeViewportRenderable>(
            frame.Renderables.Count,
            StringComparer.Ordinal);
        foreach (var renderable in frame.Renderables)
        {
            lookup.TryAdd(renderable.EntityId, renderable);
        }

        return lookup;
    }

    private static CameraPose ResolveCameraPose(
        RekallAgeRuntimeViewportCamera? camera,
        Vector3 fallbackCenter,
        float fallbackExtent)
    {
        if (camera is null || IsDefaultCamera(camera))
        {
            var fallbackEye = new Vector3(fallbackCenter.X, fallbackCenter.Y, fallbackCenter.Z + MathF.Max(3f, fallbackExtent * 2.5f));
            var fallbackForward = Vector3.Normalize(fallbackCenter - fallbackEye);
            var fallbackRight = Vector3.Normalize(Vector3.Cross(fallbackForward, Vector3.UnitY));
            var fallbackUp = Vector3.Normalize(Vector3.Cross(fallbackRight, fallbackForward));
            return new CameraPose(fallbackEye, fallbackForward, fallbackRight, fallbackUp);
        }

        var cameraEye = new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z);
        var cameraForward = DirectionFromEuler(camera.RotationX, camera.RotationY, camera.RotationZ);
        var rightVector = Rotate(1, 0, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        var upVector = Rotate(0, 1, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        return new CameraPose(
            cameraEye,
            cameraForward,
            NormalizeOrFallback(new Vector3(rightVector.X, rightVector.Y, rightVector.Z), Vector3.UnitX),
            NormalizeOrFallback(new Vector3(upVector.X, upVector.Y, upVector.Z), Vector3.UnitY));
    }

    private static bool IsDefaultCamera(RekallAgeRuntimeViewportCamera camera)
    {
        return Math.Abs(camera.X) < 0.0001
            && Math.Abs(camera.Y) < 0.0001
            && Math.Abs(camera.Z) < 0.0001
            && Math.Abs(camera.RotationX) < 0.0001
            && Math.Abs(camera.RotationY) < 0.0001
            && Math.Abs(camera.RotationZ) < 0.0001;
    }

    private static Matrix4x4 CreateProjection(
        RekallAgeRuntimeViewportCamera? camera,
        RekallAgeRuntimeViewportFrame frame,
        float extent)
    {
        var aspect = frame.Height <= 0 ? 1f : frame.Width / (float)frame.Height;
        var nearClip = MathF.Max(0.001f, (float)(camera?.NearClip ?? 0.05));
        var farClip = MathF.Max(nearClip + 0.001f, (float)(camera?.FarClip ?? Math.Max(100f, extent * 16f)));
        if (camera?.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase) == true)
        {
            var height = MathF.Max(0.001f, (float)camera.OrthographicSize);
            var projection = Matrix4x4.CreateOrthographic(height * aspect, height, nearClip, farClip);
            // Camera2D observes the XY plane from negative Z so its zero-rotation
            // forward vector points into the scene. A right-handed look-at matrix
            // mirrors X for that pose; cancel that mirror so authored +X remains
            // screen-right, matching Transform2D and editor conventions.
            if (camera.Kind.Equals("Camera2D", StringComparison.OrdinalIgnoreCase))
            {
                projection.M11 *= -1f;
            }

            return projection;
        }

        var fieldOfView = Math.Clamp((float)(camera?.FieldOfViewDegrees ?? 65), 1f, 179f);
        return Matrix4x4.CreatePerspectiveFieldOfView(
            ToRadians(fieldOfView),
            aspect,
            nearClip,
            farClip);
    }

    private static Matrix4x4 CreateModelMatrix(
        RekallAgeRuntimeViewportRenderable? renderable,
        RekallAgeRuntimeViewportCamera? activeCamera)
    {
        if (renderable is null)
        {
            return Matrix4x4.Identity;
        }

        if (IsCameraPlaneFacing(renderable) && activeCamera is not null)
        {
            return CreateCameraPlaneModelMatrix(renderable, activeCamera);
        }

        return Matrix4x4.CreateScale(
                (float)Math.Max(0.001, renderable.ScaleX),
                (float)Math.Max(0.001, renderable.ScaleY),
                (float)Math.Max(0.001, renderable.ScaleZ))
            * Matrix4x4.CreateRotationX(ToRadians(renderable.RotationX))
            * Matrix4x4.CreateRotationY(ToRadians(renderable.RotationY))
            * Matrix4x4.CreateRotationZ(ToRadians(renderable.RotationZ))
            * Matrix4x4.CreateTranslation((float)renderable.X, (float)renderable.Y, (float)renderable.Z);
    }

    private static bool IsCameraPlaneFacing(RekallAgeRuntimeViewportRenderable renderable)
    {
        return renderable.FacingMode.Equals("camera-plane", StringComparison.OrdinalIgnoreCase)
            || renderable.FacingMode.Equals("camera", StringComparison.OrdinalIgnoreCase)
            || renderable.FacingMode.Equals("billboard", StringComparison.OrdinalIgnoreCase);
    }

    private static Matrix4x4 CreateCameraPlaneModelMatrix(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportCamera camera)
    {
        var rightTuple = Rotate(1, 0, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        var upTuple = Rotate(0, 1, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        var right = -NormalizeOrFallback(new Vector3(rightTuple.X, rightTuple.Y, rightTuple.Z), Vector3.UnitX);
        var up = NormalizeOrFallback(new Vector3(upTuple.X, upTuple.Y, upTuple.Z), Vector3.UnitY);
        var forward = DirectionFromEuler(camera.RotationX, camera.RotationY, camera.RotationZ);
        var scaleX = (float)Math.Max(0.001, renderable.ScaleX);
        var scaleY = (float)Math.Max(0.001, renderable.ScaleY);
        var scaleZ = (float)Math.Max(0.001, renderable.ScaleZ);
        var normal = -forward;
        var vertical = -up;

        return new Matrix4x4(
            right.X * scaleX,
            right.Y * scaleX,
            right.Z * scaleX,
            0,
            normal.X * scaleY,
            normal.Y * scaleY,
            normal.Z * scaleY,
            0,
            vertical.X * scaleZ,
            vertical.Y * scaleZ,
            vertical.Z * scaleZ,
            0,
            (float)renderable.X,
            (float)renderable.Y,
            (float)renderable.Z,
            1);
    }

    private static SceneLight ResolvePrimaryLight(
        IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> renderablesByEntityId,
        IEnumerable<RekallAgeRuntimeViewportRenderable> renderables,
        string? primaryLightEntityId)
    {
        if (!string.IsNullOrWhiteSpace(primaryLightEntityId)
            && renderablesByEntityId.TryGetValue(primaryLightEntityId, out var selected))
        {
            if (selected.Kind.Equals("light", StringComparison.Ordinal)
                && selected.Intensity > 0.0001)
            {
                return ToSceneLight(selected);
            }
        }

        RekallAgeRuntimeViewportRenderable? firstLight = null;
        RekallAgeRuntimeViewportRenderable? firstPointLight = null;
        foreach (var renderable in renderables)
        {
            if (!renderable.Kind.Equals("light", StringComparison.Ordinal))
            {
                continue;
            }

            if (renderable.Intensity <= 0.0001)
            {
                continue;
            }

            firstLight ??= renderable;
            if (firstPointLight is null && IsPointLight(renderable))
            {
                firstPointLight = renderable;
            }
        }

        var light = firstPointLight ?? firstLight;
        if (light is null)
        {
            return new SceneLight(
                null,
                Vector3.Normalize(new Vector3(-0.45f, -0.65f, -0.6f)),
                new Vector4(0, 0, 0, 0),
                Vector4.One);
        }

        return ToSceneLight(light);
    }

    private static SceneLight ToSceneLight(RekallAgeRuntimeViewportRenderable light) =>
        new(
            light.EntityId,
            DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ),
            IsPointLight(light)
                ? new Vector4((float)light.X, (float)light.Y, (float)light.Z, 1)
                : new Vector4((float)light.X, (float)light.Y, (float)light.Z, 0),
            ResolveLightColor(light),
            (float)Math.Clamp(light.LightRange, 0.001, 1_000_000),
            light.LightPriority);

    private static Vector4 ResolveLightColor(RekallAgeRuntimeViewportRenderable light)
    {
        var color = ParseColor(light.MaterialColor);
        var intensity = (float)Math.Clamp(light.Intensity, 0.05, IsPointLight(light) || IsSpotLight(light) ? 16.0 : 4.0);
        return new Vector4(color.X * intensity, color.Y * intensity, color.Z * intensity, 1);
    }

    private static Vector3 ParseColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new Vector3(r / 255f, g / 255f, b / 255f);
        }

        return Vector3.One;
    }

    private static bool IsPointLight(RekallAgeRuntimeViewportRenderable renderable)
    {
        return renderable.Variant?.Contains("point", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSpotLight(RekallAgeRuntimeViewportRenderable renderable)
    {
        return renderable.Variant?.Contains("spot", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IReadOnlyList<SceneSpotLight> ResolveSpotLights(
        IEnumerable<RekallAgeRuntimeViewportRenderable> renderables,
        int maximumCount)
    {
        return renderables
            .Where(renderable => renderable.Kind.Equals("light", StringComparison.Ordinal)
                && renderable.Intensity > 0.0001
                && IsSpotLight(renderable))
            .OrderByDescending(renderable => renderable.LightPriority)
            .ThenByDescending(renderable => renderable.Intensity)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .Take(maximumCount)
            .Select(ToSceneSpotLight)
            .ToArray();
    }

    private static SceneSpotLight ToSceneSpotLight(RekallAgeRuntimeViewportRenderable light)
    {
        var inner = (float)Math.Clamp(light.LightInnerConeAngle, 0, 89);
        var outer = (float)Math.Clamp(light.LightOuterConeAngle, 0.001, 89);
        if (inner > outer)
        {
            inner = outer;
        }

        return new SceneSpotLight(
            light.EntityId,
            DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ),
            new Vector4((float)light.X, (float)light.Y, (float)light.Z, 1),
            ResolveLightColor(light),
            (float)Math.Clamp(light.LightRange, 0.001, 1_000_000),
            light.LightPriority,
            inner,
            outer);
    }

    private static Vector3 DirectionFromEuler(double degreesX, double degreesY, double degreesZ)
    {
        var vector = Rotate(0, 0, 1, degreesX, degreesY, degreesZ);
        return Vector3.Normalize(new Vector3(vector.X, vector.Y, vector.Z));
    }

    private static Vector3 NormalizeOrFallback(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared() < 0.000001f
            ? fallback
            : Vector3.Normalize(vector);
    }

    private static (float X, float Y, float Z) Rotate(float x, float y, float z, double degreesX, double degreesY, double degreesZ)
    {
        var rx = MathF.PI / 180f * (float)degreesX;
        var ry = MathF.PI / 180f * (float)degreesY;
        var rz = MathF.PI / 180f * (float)degreesZ;

        var cos = MathF.Cos(rx);
        var sin = MathF.Sin(rx);
        (y, z) = (y * cos - z * sin, y * sin + z * cos);

        cos = MathF.Cos(ry);
        sin = MathF.Sin(ry);
        (x, z) = (x * cos + z * sin, -x * sin + z * cos);

        cos = MathF.Cos(rz);
        sin = MathF.Sin(rz);
        (x, y) = (x * cos - y * sin, x * sin + y * cos);
        return (x, y, z);
    }

    private static float ToRadians(double degrees)
    {
        return MathF.PI / 180f * (float)degrees;
    }

    private readonly record struct SceneBounds(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)
    {
        public static SceneBounds Empty { get; } = new(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);

        public SceneBounds Include(Vector3 point)
        {
            return new SceneBounds(
                MathF.Min(MinX, point.X),
                MathF.Max(MaxX, point.X),
                MathF.Min(MinY, point.Y),
                MathF.Max(MaxY, point.Y),
                MathF.Min(MinZ, point.Z),
                MathF.Max(MaxZ, point.Z));
        }

        public SceneBounds OrDefault()
        {
            return MinX == float.MaxValue
                ? new SceneBounds(-1, 1, -1, 1, -1, 1)
                : this;
        }
    }

    private readonly record struct SceneLight(
        string? EntityId,
        Vector3 Direction,
        Vector4 Position,
        Vector4 Color,
        float Range = 10,
        int Priority = 0)
    {
        public static SceneLight Disabled { get; } = new(null, Vector3.Zero, Vector4.Zero, Vector4.Zero);
    }

    private readonly record struct SceneSpotLight(
        string? EntityId,
        Vector3 Direction,
        Vector4 Position,
        Vector4 Color,
        float Range,
        int Priority,
        float InnerConeAngle,
        float OuterConeAngle);

    private readonly record struct CameraPose(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up);
}
