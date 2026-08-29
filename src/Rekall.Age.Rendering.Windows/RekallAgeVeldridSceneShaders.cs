namespace Rekall.Age.Rendering.Windows;

internal static class RekallAgeVeldridSceneShaders
{
    internal const string DirectionalShadowVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;

        layout(set = 0, binding = 0) uniform DirectionalShadowFrameUniformBuffer
        {
            mat4 ViewProjection;
        } ShadowFrame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
        } Draw;

        void main()
        {
            gl_Position = ShadowFrame.ViewProjection * Draw.Model * vec4(Position, 1.0);
        }
        """;

    internal const string SceneVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;
        layout(location = 1) in vec3 Normal;
        layout(location = 2) in vec4 Color;
        layout(location = 3) in vec2 UV;

        layout(set = 0, binding = 0) uniform FrameUniformBuffer
        {
            mat4 ViewProjection;
            vec4 LightDirection;
            vec4 LightColor;
            vec4 LightPosition;
            vec4 CameraPosition;
            vec4 AdditionalLightDirection;
            vec4 AdditionalLightColor;
            vec4 AdditionalLightPosition;
            vec4 AdditionalLightParameters;
            vec4 AdditionalLightColor2;
            vec4 AdditionalLightPosition2;
            vec4 AdditionalLightParameters2;
            vec4 AdditionalLightColor3;
            vec4 AdditionalLightPosition3;
            vec4 AdditionalLightParameters3;
            vec4 AdditionalLightColor4;
            vec4 AdditionalLightPosition4;
            vec4 AdditionalLightParameters4;
            vec4 SpotLightColor;
            vec4 SpotLightPosition;
            vec4 SpotLightDirection;
            vec4 SpotLightParameters;
            vec4 SpotLightColor2;
            vec4 SpotLightPosition2;
            vec4 SpotLightDirection2;
            vec4 SpotLightParameters2;
            vec4 SpotLightColor3;
            vec4 SpotLightPosition3;
            vec4 SpotLightDirection3;
            vec4 SpotLightParameters3;
            vec4 SpotLightColor4;
            vec4 SpotLightPosition4;
            vec4 SpotLightDirection4;
            vec4 SpotLightParameters4;
            mat4 ShadowViewProjection0;
            mat4 ShadowViewProjection1;
            mat4 ShadowViewProjection2;
            mat4 ShadowViewProjection3;
            vec4 ShadowSplitDepths;
            vec4 ShadowParameters;
            vec4 EnvironmentParameters;
            vec4 EnvironmentAmbientSkyColor;
            vec4 EnvironmentAmbientGroundColor;
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
            vec4 AtmosphereFactors0;
            vec4 AtmosphereFactors1;
            vec4 AtmosphereColor0;
            vec4 AtmosphereColor1;
            vec4 AtmosphereColor2;
            vec4 CloudFactors;
            vec4 CloudColor;
            vec4 CloudShadowFactors;
            vec4 SurfaceWaterFactors;
            vec4 ShadowFactors;
        } Draw;

        layout(location = 0) out vec3 fsin_Normal;
        layout(location = 1) out vec4 fsin_Color;
        layout(location = 2) out vec2 fsin_UV;
        layout(location = 3) out vec3 fsin_WorldPosition;

        void main()
        {
            vec4 worldPosition = Draw.Model * vec4(Position, 1.0);
            gl_Position = Frame.ViewProjection * worldPosition;
            fsin_Normal = mat3(Draw.Model) * Normal;
            fsin_Color = Color;
            fsin_UV = UV;
            fsin_WorldPosition = worldPosition.xyz;
        }
        """;

    internal const string SceneFragmentShader = """
        #version 450

        layout(location = 0) in vec3 fsin_Normal;
        layout(location = 1) in vec4 fsin_Color;
        layout(location = 2) in vec2 fsin_UV;
        layout(location = 3) in vec3 fsin_WorldPosition;

        layout(set = 0, binding = 0) uniform FrameUniformBuffer
        {
            mat4 ViewProjection;
            vec4 LightDirection;
            vec4 LightColor;
            vec4 LightPosition;
            vec4 CameraPosition;
            vec4 AdditionalLightDirection;
            vec4 AdditionalLightColor;
            vec4 AdditionalLightPosition;
            vec4 AdditionalLightParameters;
            vec4 AdditionalLightColor2;
            vec4 AdditionalLightPosition2;
            vec4 AdditionalLightParameters2;
            vec4 AdditionalLightColor3;
            vec4 AdditionalLightPosition3;
            vec4 AdditionalLightParameters3;
            vec4 AdditionalLightColor4;
            vec4 AdditionalLightPosition4;
            vec4 AdditionalLightParameters4;
            vec4 SpotLightColor;
            vec4 SpotLightPosition;
            vec4 SpotLightDirection;
            vec4 SpotLightParameters;
            vec4 SpotLightColor2;
            vec4 SpotLightPosition2;
            vec4 SpotLightDirection2;
            vec4 SpotLightParameters2;
            vec4 SpotLightColor3;
            vec4 SpotLightPosition3;
            vec4 SpotLightDirection3;
            vec4 SpotLightParameters3;
            vec4 SpotLightColor4;
            vec4 SpotLightPosition4;
            vec4 SpotLightDirection4;
            vec4 SpotLightParameters4;
            mat4 ShadowViewProjection0;
            mat4 ShadowViewProjection1;
            mat4 ShadowViewProjection2;
            mat4 ShadowViewProjection3;
            vec4 ShadowSplitDepths;
            vec4 ShadowParameters;
            vec4 EnvironmentParameters;
            vec4 EnvironmentAmbientSkyColor;
            vec4 EnvironmentAmbientGroundColor;
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
            vec4 AtmosphereFactors0;
            vec4 AtmosphereFactors1;
            vec4 AtmosphereColor0;
            vec4 AtmosphereColor1;
            vec4 AtmosphereColor2;
            vec4 CloudFactors;
            vec4 CloudColor;
            vec4 CloudShadowFactors;
            vec4 SurfaceWaterFactors;
            vec4 ShadowFactors;
        } Draw;

        layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
        layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
        layout(set = 2, binding = 2) uniform texture2D NormalTexture;
        layout(set = 2, binding = 3) uniform sampler NormalSampler;
        layout(set = 2, binding = 4) uniform texture2D MetallicRoughnessTexture;
        layout(set = 2, binding = 5) uniform sampler MetallicRoughnessSampler;
        layout(set = 2, binding = 6) uniform texture2D OcclusionTexture;
        layout(set = 2, binding = 7) uniform sampler OcclusionSampler;
        layout(set = 2, binding = 8) uniform texture2D EmissiveTexture;
        layout(set = 2, binding = 9) uniform sampler EmissiveSampler;
        layout(set = 2, binding = 10) uniform texture2D CloudShadowTexture;
        layout(set = 2, binding = 11) uniform sampler CloudShadowSampler;
        layout(set = 2, binding = 12) uniform texture2D SurfaceWaterTexture;
        layout(set = 2, binding = 13) uniform sampler SurfaceWaterSampler;
        layout(set = 0, binding = 1) uniform texture2DArray DirectionalShadowAtlas;
        layout(set = 0, binding = 2) uniform sampler DirectionalShadowSampler;

        struct InteractiveFogVolume
        {
            vec4 PositionShape;
            vec4 HalfExtentsDensity;
            vec4 AlbedoAnisotropy;
            vec4 EmissionHeightFalloff;
            vec4 BlendPriority;
            mat4 WorldToLocal;
        };

        layout(set = 0, binding = 3) uniform InteractiveFogUniformBuffer
        {
            vec4 Settings;
            InteractiveFogVolume Volumes[8];
        } Fog;

        layout(set = 0, binding = 4) uniform texture2D EnvironmentTexture;
        layout(set = 0, binding = 5) uniform sampler EnvironmentSampler;

        layout(location = 0) out vec4 fsout_Color;const float PI = 3.14159265359;
        const int MAX_VIEW_SAMPLE_COUNT = 32;
        const int MAX_LIGHT_SAMPLE_COUNT = 16;

        vec2 directionToEquirectangularUv(vec3 direction)
        {
            vec3 d = normalize(direction);
            return vec2(atan(d.z, d.x) / (2.0 * PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
        }

        vec3 sampleEnvironmentRadiance(vec3 direction, float roughness)
        {
            float maximumLod = max(float(textureQueryLevels(sampler2D(EnvironmentTexture, EnvironmentSampler)) - 1), 0.0);
            vec3 encoded = textureLod(
                sampler2D(EnvironmentTexture, EnvironmentSampler),
                directionToEquirectangularUv(direction),
                clamp(roughness, 0.0, 1.0) * maximumLod).rgb;
            return pow(max(encoded, vec3(0.0)), vec3(2.2));
        }

        vec3 perturbNormal(vec3 normal)
        {
            vec3 tangentNormal = texture(sampler2D(NormalTexture, NormalSampler), fsin_UV).xyz * 2.0 - 1.0;
            tangentNormal.xy *= Draw.MaterialFactors.z;
            vec3 q1 = dFdx(fsin_WorldPosition);
            vec3 q2 = dFdy(fsin_WorldPosition);
            vec2 st1 = dFdx(fsin_UV);
            vec2 st2 = dFdy(fsin_UV);
            float determinant = st1.s * st2.t - st1.t * st2.s;
            if (abs(determinant) <= 0.0000001)
            {
                return normal;
            }
            vec3 tangentRaw = q1 * st2.t - q2 * st1.t;
            vec3 tangentProjected = tangentRaw - normal * dot(normal, tangentRaw);
            float tangentLengthSquared = dot(tangentProjected, tangentProjected);
            if (tangentLengthSquared <= 0.0000001)
            {
                return normal;
            }
            vec3 tangent = tangentProjected * inversesqrt(tangentLengthSquared);
            vec3 bitangent = normalize(cross(normal, tangent)) * sign(determinant);
            mat3 tbn = mat3(tangent, bitangent, normal);
            vec3 mapped = tbn * tangentNormal;
            float mappedLengthSquared = dot(mapped, mapped);
            return mappedLengthSquared <= 0.0000001 ? normal : mapped * inversesqrt(mappedLengthSquared);
        }

        float distributionGgx(vec3 normal, vec3 halfVector, float roughness)
        {
            float a = roughness * roughness;
            float a2 = a * a;
            float ndoth = max(dot(normal, halfVector), 0.0);
            float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
            return a2 / max(PI * denom * denom, 0.0001);
        }

        float geometrySchlickGgx(float ndotv, float roughness)
        {
            float r = roughness + 1.0;
            float k = (r * r) / 8.0;
            return ndotv / max(ndotv * (1.0 - k) + k, 0.0001);
        }

        vec3 fresnelSchlick(float cosTheta, vec3 f0)
        {
            return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
        }

        float phaseRayleigh(float cosTheta)
        {
            return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
        }

        float phaseMie(float cosTheta, float anisotropy)
        {
            float g = clamp(anisotropy, -0.99, 0.99);
            float g2 = g * g;
            float denom = pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.0001), 1.5);
            return 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + cosTheta * cosTheta)) / max((2.0 + g2) * denom, 0.0001);
        }

        bool intersectSphere(vec3 origin, vec3 direction, vec3 center, float radius, out vec2 hit)
        {
            vec3 local = origin - center;
            float b = dot(local, direction);
            float c = dot(local, local) - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0.0)
            {
                hit = vec2(0.0);
                return false;
            }

            float root = sqrt(discriminant);
            hit = vec2(-b - root, -b + root);
            return hit.y >= 0.0;
        }

        float atmosphereDensityAtPoint(vec3 point, vec3 center, float planetRadius, float atmosphereRadius, float density, float falloff)
        {
            float height = max(0.0, length(point - center) - planetRadius);
            float normalizedHeight = clamp(height / max(atmosphereRadius - planetRadius, 0.0001), 0.0, 1.0);
            return max(density, 0.0) * exp(-normalizedHeight / max(falloff, 0.001));
        }

        float integrateOpticalDepth(vec3 origin, vec3 direction, float rayLength, vec3 center, float planetRadius, float atmosphereRadius, float density, float falloff, int sampleCount)
        {
            float stepSize = rayLength / float(sampleCount);
            float opticalDepth = 0.0;
            for (int i = 0; i < MAX_LIGHT_SAMPLE_COUNT; i++)
            {
                if (i >= sampleCount)
                {
                    break;
                }

                float t = (float(i) + 0.5) * stepSize;
                vec3 samplePoint = origin + direction * t;
                opticalDepth += atmosphereDensityAtPoint(samplePoint, center, planetRadius, atmosphereRadius, density, falloff) * stepSize;
            }

            return opticalDepth;
        }

        bool hasAtmosphereData()
        {
            return Draw.AtmosphereFactors0.y > 0.0;
        }

        bool isAtmosphereShell()
        {
            return hasAtmosphereData() && Draw.AtmosphereFactors1.w >= 0.0;
        }

        float atmosphereSunIntensity()
        {
            return abs(Draw.AtmosphereFactors1.w);
        }

        vec3 atmosphereLightColor()
        {
            return max(Frame.LightColor.rgb, vec3(0.0));
        }

        float ozoneAbsorption()
        {
            return max(Draw.AtmosphereColor2.w, 0.0);
        }

        bool shouldDiscardAtmosphereBackHemisphere(vec3 rayOrigin, vec3 rayDirection, vec3 planetCenter, float atmosphereRadius)
        {
            float cameraRadius = length(rayOrigin - planetCenter);
            if (cameraRadius <= atmosphereRadius)
            {
                return false;
            }

            vec3 shellNormal = normalize(fsin_WorldPosition - planetCenter);
            return dot(shellNormal, rayDirection) > 0.0;
        }

        vec3 atmosphereExtinction()
        {
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            return Draw.AtmosphereColor0.rgb * rayleighStrength
                + Draw.AtmosphereColor1.rgb * mieStrength
                + Draw.AtmosphereColor2.rgb * ozoneAbsorption();
        }

        vec3 surfaceAtmosphereExtinction()
        {
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            vec3 rayleighWavelengthWeight = vec3(0.45, 0.95, 1.85);
            return rayleighWavelengthWeight * rayleighStrength
                + vec3(mieStrength)
                + Draw.AtmosphereColor2.rgb * ozoneAbsorption();
        }

        float planetShadowFactor(vec3 samplePoint, vec3 sunDirection, vec3 planetCenter)
        {
            vec3 localUp = normalize(samplePoint - planetCenter);
            return smoothstep(-0.03, 0.08, dot(localUp, sunDirection));
        }

        float spaceAmbientFloor()
        {
            return 0.0;
        }

        float aerialPerspectiveStrength()
        {
            return clamp(Draw.AtmosphereColor1.w, 0.0, 2.0);
        }

        vec3 surfaceAtmosphereTransmittance(vec3 surfacePosition, vec3 lightDirection)
        {
            if (!hasAtmosphereData())
            {
                return vec3(1.0);
            }

            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            vec3 rayOrigin = surfacePosition + surfaceNormal * max(planetRadius * 0.001, 0.0001);
            vec3 rayDirection = normalize(lightDirection);

            vec2 groundHit;
            if (intersectSphere(rayOrigin, rayDirection, planetCenter, planetRadius, groundHit) && groundHit.x > 0.0)
            {
                return vec3(0.0);
            }

            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return vec3(1.0);
            }

            float rayLength = max(atmosphereHit.y, 0.0);
            float opticalDepth = integrateOpticalDepth(
                rayOrigin,
                rayDirection,
                rayLength,
                planetCenter,
                planetRadius,
                atmosphereRadius,
                density,
                densityFalloff,
                8);
            return exp(-opticalDepth * surfaceAtmosphereExtinction());
        }

        vec2 sphericalUv(vec3 direction)
        {
            vec3 n = normalize(direction);
            float u = atan(n.z, n.x) / (2.0 * PI) + 0.5;
            float v = acos(clamp(n.y, -1.0, 1.0)) / PI;
            return vec2(u, v);
        }

        float sampleCloudShadow(vec3 surfacePosition, vec3 lightDirection)
        {
            if (Draw.CloudShadowFactors.x <= 0.5)
            {
                return 1.0;
            }

            vec3 planetCenter = Draw.Model[3].xyz;
            float cloudRadius = max(Draw.CloudShadowFactors.y, 0.0001);
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            vec3 rayOrigin = surfacePosition + surfaceNormal * max(cloudRadius * 0.0001, 0.0001);
            vec2 cloudHit;
            if (!intersectSphere(rayOrigin, normalize(lightDirection), planetCenter, cloudRadius, cloudHit) || cloudHit.y <= 0.0)
            {
                return 1.0;
            }

            float hitDistance = cloudHit.x > 0.0 ? cloudHit.x : cloudHit.y;
            vec3 cloudPoint = rayOrigin + normalize(lightDirection) * hitDistance;
            float coverage = texture(sampler2D(CloudShadowTexture, CloudShadowSampler), sphericalUv(cloudPoint - planetCenter)).a;
            float strength = clamp(Draw.CloudShadowFactors.z, 0.0, 1.0);
            float daylight = planetShadowFactor(surfacePosition, normalize(lightDirection), planetCenter);
            return clamp(1.0 - coverage * strength * daylight, 0.0, 1.0);
        }

        bool hasSurfaceWater()
        {
            return Draw.SurfaceWaterFactors.x > 0.5;
        }

        float surfaceWaterSpecularStrength()
        {
            return clamp(Draw.SurfaceWaterFactors.z, 0.0, 8.0);
        }

        float sampleSurfaceWaterCoverage(vec2 uv, vec3 baseTextureColor, out vec3 waterTint)
        {
            vec4 water = texture(sampler2D(SurfaceWaterTexture, SurfaceWaterSampler), uv);
            waterTint = mix(vec3(0.006, 0.075, 0.34), pow(max(water.rgb, vec3(0.0)), vec3(2.2)), 0.35);
            float waterColorPresence = max(max(water.r, water.g), water.b) * water.a;
            float baseBlueDominance = baseTextureColor.b - max(baseTextureColor.r, baseTextureColor.g);
            float baseSaturation = max(max(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b)
                - min(min(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b);
            float authoredWaterRegion = smoothstep(0.015, 0.12, baseBlueDominance)
                * smoothstep(0.03, 0.18, baseSaturation);
            float mask = max(waterColorPresence * authoredWaterRegion, waterColorPresence * 0.65);
            return clamp(mask * clamp(Draw.SurfaceWaterFactors.y, 0.0, 4.0), 0.0, 1.0);
        }

        vec3 surfaceAerialPerspectiveScattering(vec3 rayOrigin, vec3 rayDirection, float rayStart, float rayEnd, vec3 sunDirection)
        {
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            float mieAnisotropy = clamp(Draw.AtmosphereFactors1.z, -0.99, 0.99);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 beta = surfaceAtmosphereExtinction();
            float rayLength = max(rayEnd - rayStart, 0.0);
            if (rayLength <= 0.0001)
            {
                return vec3(0.0);
            }

            const int aerialSampleCount = 10;
            float stepSize = rayLength / float(aerialSampleCount);
            float viewOpticalDepth = 0.0;
            vec3 scattered = vec3(0.0);
            for (int i = 0; i < aerialSampleCount; i++)
            {
                float t = rayStart + (float(i) + 0.5) * stepSize;
                vec3 samplePoint = rayOrigin + rayDirection * t;
                float localDensity = atmosphereDensityAtPoint(samplePoint, planetCenter, planetRadius, atmosphereRadius, density, densityFalloff);
                viewOpticalDepth += localDensity * stepSize;

                float horizonLight = planetShadowFactor(samplePoint, sunDirection, planetCenter);
                if (horizonLight <= 0.0001)
                {
                    continue;
                }

                vec2 lightHit;
                if (!intersectSphere(samplePoint, sunDirection, planetCenter, atmosphereRadius, lightHit))
                {
                    continue;
                }

                vec2 lightGroundHit;
                bool hitsPlanetOnLightRay = intersectSphere(samplePoint, sunDirection, planetCenter, planetRadius, lightGroundHit);
                if (hitsPlanetOnLightRay && lightGroundHit.y > 0.0)
                {
                    continue;
                }

                float lightDepth = integrateOpticalDepth(samplePoint, sunDirection, max(lightHit.y, 0.0), planetCenter, planetRadius, atmosphereRadius, density, densityFalloff, 6);
                vec3 transmittance = exp(-(viewOpticalDepth + lightDepth) * beta);
                scattered += localDensity * horizonLight * transmittance * stepSize;
            }

            float mu = dot(rayDirection, sunDirection);
            vec3 rayleigh = Draw.AtmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
            vec3 mie = Draw.AtmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
            return (rayleigh + mie) * scattered * atmosphereSunIntensity() * atmosphereLightColor();
        }

        vec3 applySurfaceAerialPerspective(vec3 surfaceColor, vec3 surfacePosition, vec3 lightDirection)
        {
            if (!hasAtmosphereData())
            {
                return surfaceColor;
            }

            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(surfacePosition - rayOrigin);
            float surfaceDistance = length(surfacePosition - rayOrigin);

            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return surfaceColor;
            }

            float rayStart = max(atmosphereHit.x, 0.0);
            float rayEnd = min(surfaceDistance, atmosphereHit.y);
            if (rayEnd <= rayStart)
            {
                return surfaceColor;
            }

            float opticalDepth = integrateOpticalDepth(
                rayOrigin + rayDirection * rayStart,
                rayDirection,
                rayEnd - rayStart,
                planetCenter,
                planetRadius,
                atmosphereRadius,
                density,
                densityFalloff,
                10);
            float strength = aerialPerspectiveStrength();
            float cameraAtmosphereRatio = length(rayOrigin - planetCenter) / atmosphereRadius;
            float lowAltitudeView = 1.0 - smoothstep(1.0, 1.35, cameraAtmosphereRatio);
            float effectiveStrength = strength * mix(1.0, 0.32, lowAltitudeView);
            vec3 transmittance = exp(-opticalDepth * surfaceAtmosphereExtinction() * effectiveStrength);
            vec3 scattering = surfaceAerialPerspectiveScattering(rayOrigin, rayDirection, rayStart, rayEnd, normalize(lightDirection));
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            float surfaceSun = smoothstep(-0.08, 0.18, dot(surfaceNormal, normalize(lightDirection)));
            vec3 scatteringTint = mix(vec3(1.0), vec3(0.55, 0.78, 1.35), lowAltitudeView);
            return surfaceColor * transmittance + scattering * scatteringTint * effectiveStrength * surfaceSun;
        }

        vec4 renderAtmosphere()
        {
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            float mieAnisotropy = clamp(Draw.AtmosphereFactors1.z, -0.99, 0.99);
            float sunIntensity = atmosphereSunIntensity();
            int viewSampleCount = int(clamp(Draw.MaterialFactors.x, 4.0, float(MAX_VIEW_SAMPLE_COUNT)));
            int lightSampleCount = int(clamp(Draw.MaterialFactors.y, 2.0, float(MAX_LIGHT_SAMPLE_COUNT)));
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(fsin_WorldPosition - rayOrigin);
            vec3 sunDirection = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            if (shouldDiscardAtmosphereBackHemisphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius))
            {
                discard;
            }

            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return vec4(0.0);
            }

            vec2 groundHit;
            float rayStart = max(atmosphereHit.x, 0.0);
            float rayEnd = atmosphereHit.y;
            bool hitsGround = intersectSphere(rayOrigin, rayDirection, planetCenter, planetRadius, groundHit);
            if (hitsGround && groundHit.x > 0.0)
            {
                discard;
            }

            float rayLength = max(rayEnd - rayStart, 0.0);
            if (rayLength <= 0.0001)
            {
                return vec4(0.0);
            }

            float stepSize = rayLength / float(viewSampleCount);
            float viewOpticalDepth = 0.0;
            vec3 scattered = vec3(0.0);
            vec3 beta = atmosphereExtinction();
            for (int i = 0; i < MAX_VIEW_SAMPLE_COUNT; i++)
            {
                if (i >= viewSampleCount)
                {
                    break;
                }

                float t = rayStart + (float(i) + 0.5) * stepSize;
                vec3 samplePoint = rayOrigin + rayDirection * t;
                float localDensity = atmosphereDensityAtPoint(samplePoint, planetCenter, planetRadius, atmosphereRadius, density, densityFalloff);
                float horizonLight = planetShadowFactor(samplePoint, sunDirection, planetCenter);
                if (horizonLight <= 0.0001)
                {
                    continue;
                }

                viewOpticalDepth += localDensity * stepSize;

                vec2 lightHit;
                bool exitsAtmosphere = intersectSphere(samplePoint, sunDirection, planetCenter, atmosphereRadius, lightHit);
                vec2 lightGroundHit;
                bool hitsPlanetOnLightRay = intersectSphere(samplePoint, sunDirection, planetCenter, planetRadius, lightGroundHit);
                bool shadowed = hitsPlanetOnLightRay && lightGroundHit.y > 0.0;
                if (!exitsAtmosphere || shadowed)
                {
                    continue;
                }

                float lightDepth = integrateOpticalDepth(samplePoint, sunDirection, max(lightHit.y, 0.0), planetCenter, planetRadius, atmosphereRadius, density, densityFalloff, lightSampleCount);
                vec3 transmittance = exp(-(viewOpticalDepth + lightDepth) * beta);
                scattered += localDensity * horizonLight * transmittance * stepSize;
            }

            float mu = dot(rayDirection, sunDirection);
            vec3 rayleigh = Draw.AtmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
            vec3 mie = Draw.AtmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
            vec3 color = (rayleigh + mie) * scattered * sunIntensity * atmosphereLightColor();
            vec3 shellNormal = normalize(fsin_WorldPosition - planetCenter);
            vec3 viewDirection = normalize(rayOrigin - fsin_WorldPosition);
            float limb = pow(clamp(1.0 - abs(dot(shellNormal, viewDirection)), 0.0, 1.0), 3.5);
            float sunlitRim = smoothstep(-0.22, 0.18, dot(shellNormal, sunDirection));
            color += Draw.AtmosphereColor0.rgb * atmosphereLightColor() * limb * sunlitRim * rayleighStrength * sunIntensity * 0.18;
            vec3 mapped = vec3(1.0) - exp(-color * max(Draw.EmissiveFactors.a, 0.0));
            float alpha = clamp(max(max(mapped.r, mapped.g), mapped.b) * 1.8, 0.0, 0.9);
            return vec4(pow(mapped, vec3(1.0 / 2.2)), alpha);
        }

        bool isCloudLayer()
        {
            return Draw.CloudFactors.y > 0.0;
        }

        bool cloudAlphaFromTextureOnly()
        {
            return Draw.CloudFactors.x > 0.5;
        }

        float cloudSkyVisibility(vec3 cloudPosition, vec3 sunDirection, vec3 planetCenter)
        {
            return planetShadowFactor(cloudPosition, sunDirection, planetCenter);
        }

        vec4 renderCloudLayer()
        {
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(fsin_WorldPosition - rayOrigin);
            vec3 planetCenter = Draw.Model[3].xyz;
            float shellRadius = max(length(fsin_WorldPosition - planetCenter), 0.0001);
            if (shouldDiscardAtmosphereBackHemisphere(rayOrigin, rayDirection, planetCenter, shellRadius))
            {
                discard;
            }

            vec3 normal = normalize(fsin_WorldPosition - planetCenter);
            vec3 view = normalize(rayOrigin - fsin_WorldPosition);
            vec3 light = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            float lambertian = max(dot(normal, light), 0.0);
            float skyVisibility = cloudSkyVisibility(fsin_WorldPosition, light, planetCenter);

            vec4 textureColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fsin_UV);
            float textureCoverage = cloudAlphaFromTextureOnly()
                ? textureColor.a
                : max(max(textureColor.r, textureColor.g), textureColor.b) * textureColor.a;
            float slantPath = clamp(1.0 / max(abs(dot(normal, view)), 0.22), 1.0, 3.25);
            float opticalDepth = textureCoverage * max(Draw.CloudFactors.y, 0.0) * max(Draw.CloudColor.a, 0.0) * slantPath;
            float alpha = clamp((1.0 - exp(-opticalDepth)) * mix(0.32, 1.0, skyVisibility), 0.0, 0.72);
            if (alpha < 0.01)
            {
                discard;
            }

            vec3 cloudBase = cloudAlphaFromTextureOnly()
                ? vec3(0.92, 0.95, 1.0)
                : mix(vec3(0.88, 0.91, 0.96), pow(max(textureColor.rgb, vec3(0.0)), vec3(2.2)), 0.82);
            float lightTerm = mix(1.0, lambertian, clamp(Draw.CloudFactors.z, 0.0, 1.0));
            vec3 directTransmittance = surfaceAtmosphereTransmittance(fsin_WorldPosition, light);
            float ambientTerm = max(Draw.CloudFactors.w, 0.0) * mix(0.04, 1.0, skyVisibility);
            float sunView = clamp(dot(light, view), 0.0, 1.0);
            float silverLining = pow(sunView, 12.0) * smoothstep(0.0, 0.35, lambertian) * skyVisibility;
            vec3 color = cloudBase
                * pow(max(Draw.CloudColor.rgb, vec3(0.0)), vec3(2.2))
                * (Frame.LightColor.rgb * directTransmittance * lightTerm * skyVisibility * 2.65 + vec3(ambientTerm));
            color += Frame.LightColor.rgb * directTransmittance * silverLining * 0.75;
            color = applySurfaceAerialPerspective(color, fsin_WorldPosition, light);
            return vec4(pow(max(color, vec3(0.0)), vec3(1.0 / 2.2)), alpha);
        }

        mat4 directionalShadowMatrix(int cascadeIndex)
        {
            if (cascadeIndex == 0) return Frame.ShadowViewProjection0;
            if (cascadeIndex == 1) return Frame.ShadowViewProjection1;
            if (cascadeIndex == 2) return Frame.ShadowViewProjection2;
            return Frame.ShadowViewProjection3;
        }

        float sampleDirectionalShadow(vec3 worldPosition, vec3 normal, vec3 lightDirection)
        {
            int cascadeCount = int(Frame.ShadowParameters.x + 0.5);
            if (cascadeCount <= 0 || Draw.ShadowFactors.x < 0.5)
            {
                return 1.0;
            }

            float viewDistance = distance(Frame.CameraPosition.xyz, worldPosition);
            int cascadeIndex = 0;
            if (cascadeCount > 1 && viewDistance > Frame.ShadowSplitDepths.x) cascadeIndex = 1;
            if (cascadeCount > 2 && viewDistance > Frame.ShadowSplitDepths.y) cascadeIndex = 2;
            if (cascadeCount > 3 && viewDistance > Frame.ShadowSplitDepths.z) cascadeIndex = 3;

            vec3 offsetPosition = worldPosition + normal * Frame.ShadowParameters.z;
            vec4 shadowClip = directionalShadowMatrix(cascadeIndex) * vec4(offsetPosition, 1.0);
            if (shadowClip.w <= 0.00001)
            {
                return 1.0;
            }

            vec3 shadowNdc = shadowClip.xyz / shadowClip.w;
            vec2 shadowUv = shadowNdc.xy * 0.5 + 0.5;
            if (shadowUv.x <= 0.0 || shadowUv.x >= 1.0 || shadowUv.y <= 0.0 || shadowUv.y >= 1.0 || shadowNdc.z <= 0.0 || shadowNdc.z >= 1.0)
            {
                return 1.0;
            }

            float slopeBias = Frame.ShadowParameters.y * (1.0 + 2.0 * (1.0 - max(dot(normal, lightDirection), 0.0)));
            float referenceDepth = shadowNdc.z - slopeBias;
            float texel = Frame.ShadowParameters.w;
            float visibility = 0.0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    float storedDepth = texture(
                        sampler2DArray(DirectionalShadowAtlas, DirectionalShadowSampler),
                        vec3(shadowUv + vec2(x, y) * texel, float(cascadeIndex))).r;
                    visibility += referenceDepth <= storedDepth ? 1.0 : 0.0;
                }
            }
            return mix(0.22, 1.0, visibility / 9.0);
        }

        float interactiveFogInfluence(int volumeIndex, vec3 worldPosition)
        {
            InteractiveFogVolume volume = Fog.Volumes[volumeIndex];
            int shape = int(volume.PositionShape.w + 0.5);
            float influence = 1.0;
            if (shape == 1)
            {
                vec3 local = (volume.WorldToLocal * vec4(worldPosition, 1.0)).xyz;
                vec3 remaining = volume.HalfExtentsDensity.xyz - abs(local);
                float edge = min(remaining.x, min(remaining.y, remaining.z));
                if (edge <= 0.0) return 0.0;
                float blend = volume.BlendPriority.x;
                influence = blend <= 0.0001 ? 1.0 : smoothstep(0.0, blend, edge);
            }
            else if (shape == 2)
            {
                vec3 local = (volume.WorldToLocal * vec4(worldPosition, 1.0)).xyz;
                float normalizedRadius = length(local / max(volume.HalfExtentsDensity.xyz, vec3(0.001)));
                if (normalizedRadius >= 1.0) return 0.0;
                float normalizedBlend = volume.BlendPriority.x / max(max(volume.HalfExtentsDensity.x, volume.HalfExtentsDensity.y), volume.HalfExtentsDensity.z);
                influence = normalizedBlend <= 0.0001
                    ? 1.0
                    : smoothstep(0.0, normalizedBlend, 1.0 - normalizedRadius);
            }

            float height = max(0.0, worldPosition.y - volume.PositionShape.y);
            return influence * exp(-height * volume.EmissionHeightFalloff.w);
        }

        vec3 applyInteractiveFog(vec3 surfaceColor, vec3 surfacePosition, vec3 lightDirection)
        {
            int volumeCount = int(Fog.Settings.x + 0.5);
            if (volumeCount <= 0)
            {
                return surfaceColor;
            }

            vec3 ray = surfacePosition - Frame.CameraPosition.xyz;
            float rayLength = min(length(ray), 140.0);
            if (rayLength <= 0.001)
            {
                return surfaceColor;
            }

            vec3 rayDirection = normalize(ray);
            const int stepCount = 6;
            float stepSize = rayLength / float(stepCount);
            float transmittance = 1.0;
            vec3 integrated = vec3(0.0);
            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
            {
                vec3 samplePosition = Frame.CameraPosition.xyz + rayDirection * ((float(stepIndex) + 0.5) * stepSize);
                for (int volumeIndex = 0; volumeIndex < 8; volumeIndex++)
                {
                    if (volumeIndex >= volumeCount) break;
                    InteractiveFogVolume volume = Fog.Volumes[volumeIndex];
                    float influence = interactiveFogInfluence(volumeIndex, samplePosition);
                    float opticalDepth = volume.HalfExtentsDensity.w * influence * stepSize * Fog.Settings.z;
                    if (opticalDepth <= 0.000001) continue;

                    float extinction = exp(-opticalDepth);
                    float forwardPhase = mix(0.34, 0.78, pow(max(dot(rayDirection, lightDirection), 0.0), mix(2.0, 8.0, max(volume.AlbedoAnisotropy.w, 0.0))));
                    float shadowVisibility = sampleDirectionalShadow(samplePosition, lightDirection, lightDirection);
                    vec3 fogLight = volume.AlbedoAnisotropy.rgb
                        * (vec3(0.16) + Frame.LightColor.rgb * forwardPhase * shadowVisibility * 0.58)
                        + volume.EmissionHeightFalloff.rgb;
                    integrated += transmittance * fogLight * (1.0 - extinction);
                    transmittance *= extinction;
                }
            }
            return surfaceColor * transmittance + integrated;
        }

        void main()
        {
            if (isAtmosphereShell())
            {
                fsout_Color = renderAtmosphere();
                return;
            }

            if (isCloudLayer())
            {
                fsout_Color = renderCloudLayer();
                return;
            }

            vec4 textureColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fsin_UV);
            vec3 albedo = pow(max(fsin_Color.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
            vec4 metalRough = texture(sampler2D(MetallicRoughnessTexture, MetallicRoughnessSampler), fsin_UV);
            float metallic = clamp(metalRough.b * Draw.MaterialFactors.x, 0.0, 1.0);
            float roughness = clamp(metalRough.g * Draw.MaterialFactors.y, 0.04, 1.0);
            vec3 waterTint = vec3(0.0);
            float waterCoverage = hasSurfaceWater() ? sampleSurfaceWaterCoverage(fsin_UV, textureColor.rgb, waterTint) : 0.0;
            if (waterCoverage > 0.0001)
            {
                albedo = mix(albedo, waterTint, waterCoverage * 0.78);
                roughness = mix(roughness, clamp(Draw.SurfaceWaterFactors.w, 0.01, 1.0), waterCoverage);
                metallic = mix(metallic, 0.0, waterCoverage);
            }
            float occlusion = 1.0;
            if (Draw.MaterialFactors.w > 0.0001)
            {
                occlusion = mix(1.0, texture(sampler2D(OcclusionTexture, OcclusionSampler), fsin_UV).r, Draw.MaterialFactors.w);
            }
            vec3 light = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            vec3 view = normalize(Frame.CameraPosition.xyz - fsin_WorldPosition);
            vec3 normal = hasAtmosphereData()
                ? normalize(fsin_WorldPosition - Draw.Model[3].xyz)
                : normalize(fsin_Normal);
            if (dot(normal, view) < 0.0)
            {
                normal = -normal;
            }

            if (Draw.MaterialFactors.z > 0.0001)
            {
                normal = perturbNormal(normal);
            }
            vec3 halfVector = normalize(view + light);
            float ndotl = max(dot(normal, light), 0.0);
            float ndotv = max(dot(normal, view), 0.0);
            vec3 f0 = mix(mix(vec3(0.04), albedo, metallic), vec3(0.02), waterCoverage);
            float d = distributionGgx(normal, halfVector, roughness);
            float g = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(ndotl, roughness);
            vec3 f = fresnelSchlick(max(dot(halfVector, view), 0.0), f0);
            vec3 specular = d * g * f / max(4.0 * ndotv * ndotl, 0.0001);
            specular *= mix(1.0, surfaceWaterSpecularStrength(), waterCoverage);
            vec3 diffuse = (1.0 - f) * (1.0 - metallic) * albedo / PI;
            diffuse *= mix(1.0, 0.42, waterCoverage);
            vec3 directTransmittance = surfaceAtmosphereTransmittance(fsin_WorldPosition, light);
            float ambientStrength = hasAtmosphereData()
                ? spaceAmbientFloor()
                : 0.12 * max(Frame.EnvironmentParameters.x, 0.0);
            float ambientHemisphere = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
            vec3 environmentAmbientColor = mix(Frame.EnvironmentAmbientGroundColor.rgb, Frame.EnvironmentAmbientSkyColor.rgb, ambientHemisphere);
            bool hasEnvironmentImage = Frame.EnvironmentAmbientSkyColor.a > 0.5;
            vec3 environmentDiffuse = hasEnvironmentImage
                ? sampleEnvironmentRadiance(normal, 0.82)
                : environmentAmbientColor;
            vec3 environmentSpecular = hasEnvironmentImage
                ? sampleEnvironmentRadiance(reflect(-view, normal), roughness)
                : environmentAmbientColor * mix(0.28, 1.0, 1.0 - roughness);
            vec3 ambientFresnel = fresnelSchlick(ndotv, f0);
            vec3 ambientDiffuse = (1.0 - ambientFresnel) * (1.0 - metallic) * albedo;
            vec3 ambient = (ambientDiffuse * environmentDiffuse + ambientFresnel * environmentSpecular)
                * ambientStrength
                * occlusion;
            vec3 waterFresnel = fresnelSchlick(ndotv, vec3(0.02));
            ambient += waterFresnel * Frame.LightColor.rgb * directTransmittance * waterCoverage * 0.018;
            vec3 emissive = pow(max(texture(sampler2D(EmissiveTexture, EmissiveSampler), fsin_UV).rgb * Draw.EmissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * Draw.EmissiveFactors.a;
            float cloudShadow = sampleCloudShadow(fsin_WorldPosition, light);
            float directionalShadow = sampleDirectionalShadow(fsin_WorldPosition, normal, light);
            vec3 color = emissive + ambient + (diffuse + specular) * Frame.LightColor.rgb * directTransmittance * cloudShadow * directionalShadow * ndotl * 1.8;
            vec4 practicalColors[4] = vec4[](Frame.AdditionalLightColor, Frame.AdditionalLightColor2, Frame.AdditionalLightColor3, Frame.AdditionalLightColor4);
            vec4 practicalPositions[4] = vec4[](Frame.AdditionalLightPosition, Frame.AdditionalLightPosition2, Frame.AdditionalLightPosition3, Frame.AdditionalLightPosition4);
            vec4 practicalParameters[4] = vec4[](Frame.AdditionalLightParameters, Frame.AdditionalLightParameters2, Frame.AdditionalLightParameters3, Frame.AdditionalLightParameters4);
            for (int practicalIndex = 0; practicalIndex < 4; practicalIndex++)
            {
                if (dot(practicalColors[practicalIndex].rgb, practicalColors[practicalIndex].rgb) <= 0.000001) continue;
                vec3 practicalOffset = practicalPositions[practicalIndex].xyz - fsin_WorldPosition;
                float practicalDistance = length(practicalOffset);
                vec3 practicalLight = practicalOffset / max(practicalDistance, 0.0001);
                float practicalRange = max(practicalParameters[practicalIndex].x, 0.001);
                float practicalWindow = pow(clamp(1.0 - practicalDistance / practicalRange, 0.0, 1.0), 2.0);
                float practicalAttenuation = practicalWindow / (1.0 + 0.045 * practicalDistance * practicalDistance);
                vec3 practicalHalf = normalize(view + practicalLight);
                float practicalNdotL = max(dot(normal, practicalLight), 0.0);
                float practicalD = distributionGgx(normal, practicalHalf, roughness);
                float practicalG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(practicalNdotL, roughness);
                vec3 practicalF = fresnelSchlick(max(dot(practicalHalf, view), 0.0), f0);
                vec3 practicalSpecular = practicalD * practicalG * practicalF / max(4.0 * ndotv * practicalNdotL, 0.0001);
                vec3 practicalDiffuse = (1.0 - practicalF) * (1.0 - metallic) * albedo / PI;
                color += (practicalDiffuse + practicalSpecular) * practicalColors[practicalIndex].rgb * practicalNdotL * practicalAttenuation * 4.5;
            }
            vec4 spotColors[4] = vec4[](Frame.SpotLightColor, Frame.SpotLightColor2, Frame.SpotLightColor3, Frame.SpotLightColor4);
            vec4 spotPositions[4] = vec4[](Frame.SpotLightPosition, Frame.SpotLightPosition2, Frame.SpotLightPosition3, Frame.SpotLightPosition4);
            vec4 spotDirections[4] = vec4[](Frame.SpotLightDirection, Frame.SpotLightDirection2, Frame.SpotLightDirection3, Frame.SpotLightDirection4);
            vec4 spotParameters[4] = vec4[](Frame.SpotLightParameters, Frame.SpotLightParameters2, Frame.SpotLightParameters3, Frame.SpotLightParameters4);
            for (int spotIndex = 0; spotIndex < 4; spotIndex++)
            {
                if (dot(spotColors[spotIndex].rgb, spotColors[spotIndex].rgb) <= 0.000001) continue;
                vec3 spotOffset = spotPositions[spotIndex].xyz - fsin_WorldPosition;
                float spotDistance = length(spotOffset);
                vec3 spotLight = spotOffset / max(spotDistance, 0.0001);
                float spotRange = max(spotParameters[spotIndex].x, 0.001);
                float spotWindow = pow(clamp(1.0 - spotDistance / spotRange, 0.0, 1.0), 2.0);
                float spotDistanceAttenuation = spotWindow / (1.0 + 0.045 * spotDistance * spotDistance);
                vec3 spotForward = normalize(spotDirections[spotIndex].xyz);
                float spotCos = dot(-spotLight, spotForward);
                float spotInnerCos = spotParameters[spotIndex].z;
                float spotOuterCos = spotParameters[spotIndex].w;
                float spotConeAttenuation = clamp((spotCos - spotOuterCos) / max(spotInnerCos - spotOuterCos, 0.0001), 0.0, 1.0);
                spotConeAttenuation *= spotConeAttenuation;
                float spotAttenuation = spotDistanceAttenuation * spotConeAttenuation;
                if (spotAttenuation <= 0.0001) continue;
                vec3 spotHalf = normalize(view + spotLight);
                float spotNdotL = max(dot(normal, spotLight), 0.0);
                float spotD = distributionGgx(normal, spotHalf, roughness);
                float spotG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(spotNdotL, roughness);
                vec3 spotF = fresnelSchlick(max(dot(spotHalf, view), 0.0), f0);
                vec3 spotSpecular = spotD * spotG * spotF / max(4.0 * ndotv * spotNdotL, 0.0001);
                vec3 spotDiffuse = (1.0 - spotF) * (1.0 - metallic) * albedo / PI;
                color += (spotDiffuse + spotSpecular) * spotColors[spotIndex].rgb * spotNdotL * spotAttenuation * 4.5;
            }
            color = applySurfaceAerialPerspective(color, fsin_WorldPosition, light);
            color = applyInteractiveFog(color, fsin_WorldPosition, light);
            // The scene target is a floating-point HDR buffer, so this pass writes linear
            // radiance and leaves exposure, tone mapping and gamma to the present pass -
            // matching the Vulkan capture path, which tone maps at present too. Doing it here
            // instead forced the present pass to work on gamma-encoded LDR values, which is
            // why the interactive path could not implement AgX or a white point at all.
            vec3 lit = max(color, vec3(0.0));
            float surfaceAlpha = hasAtmosphereData() ? fsin_Color.a : fsin_Color.a * textureColor.a;
            // Draw.ShadowFactors.y/.z are repurposed as AlphaCutoff and an "is this a mask-mode
            // draw" flag, rather than adding new fields to this shared uniform struct. A masked
            // material discards below its cutoff instead of blending, the real alpha-tested cutout
            // behavior (foliage, chain-link) as opposed to AlphaMode "blend"'s soft transparency.
            if (Draw.ShadowFactors.z > 0.5 && surfaceAlpha < Draw.ShadowFactors.y)
            {
                discard;
            }

            fsout_Color = vec4(lit, surfaceAlpha);
        }
        """;

    internal const string PresentVertexShader = """
        #version 450

        layout(location = 0) out vec2 fsin_UV;

        void main()
        {
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );
            vec2 position = positions[gl_VertexIndex];
            gl_Position = vec4(position, 0.0, 1.0);
            fsin_UV = position * 0.5 + 0.5;
        }
        """;

    internal const string PresentFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fsin_UV;
        layout(set = 0, binding = 0) uniform texture2D SceneTexture;
        layout(set = 0, binding = 1) uniform sampler SceneSampler;
        layout(set = 0, binding = 2) uniform texture2D SceneDepthTexture;
        layout(set = 0, binding = 3) uniform sampler SceneDepthSampler;
        layout(set = 1, binding = 0) uniform PostProcessUniformBuffer
        {
            vec4 PostProcessParameters;
            vec4 ScreenParameters;
            vec4 AmbientOcclusionParameters;
            mat4 InverseViewProjection;
            vec4 CameraPosition;
            vec4 EnvironmentParameters;
            vec4 SolidBackgroundEncoded;
            vec4 SolidBackgroundLinear;
        };
        layout(set = 1, binding = 1) uniform texture2D EnvironmentTexture;
        layout(set = 1, binding = 2) uniform sampler EnvironmentSampler;

        layout(location = 0) out vec4 fsout_Color;

        float luma(vec3 color)
        {
            return dot(color, vec3(0.299, 0.587, 0.114));
        }

        vec2 directionToEnvironmentUv(vec3 direction)
        {
            const float pi = 3.14159265358979323846;
            direction = normalize(direction);
            return vec2(
                atan(direction.z, direction.x) / (2.0 * pi) + 0.5,
                acos(clamp(direction.y, -1.0, 1.0)) / pi);
        }

        vec3 environmentBackground(vec2 uv)
        {
            vec2 ndc = uv * 2.0 - 1.0;
            vec4 nearH = InverseViewProjection * vec4(ndc, 0.0, 1.0);
            vec4 farH = InverseViewProjection * vec4(ndc, 1.0, 1.0);
            vec3 nearWorld = nearH.xyz / max(abs(nearH.w), 0.000001);
            vec3 farWorld = farH.xyz / max(abs(farH.w), 0.000001);
            vec3 ray = normalize(farWorld - nearWorld);
            vec3 encoded = textureLod(
                sampler2D(EnvironmentTexture, EnvironmentSampler),
                directionToEnvironmentUv(ray),
                0.0).rgb;
            return pow(max(encoded, vec3(0.0)), vec3(2.2));
        }

        // Kept byte-identical to agxCurve in Shaders/rekall_tonemap.frag so the interactive
        // player and the Vulkan capture path apply the same display transform. If one of these
        // changes, the other must change with it.
        vec3 agxCurve(vec3 value)
        {
            value = max(value, vec3(0.0));
            vec3 logValue = clamp((log2(max(value, vec3(1e-6))) + 10.0) / 16.5, 0.0, 1.0);
            vec3 sigmoid = logValue * logValue * (3.0 - 2.0 * logValue);
            return sigmoid * sigmoid * (3.0 - 2.0 * sigmoid);
        }

        vec3 applyDisplayTransform(vec3 hdr)
        {
            hdr *= exp2(EnvironmentParameters.x);
            // 11.2 is the conventional neutral scene-white reference; an authored white point
            // moves highlight placement without crushing midtones.
            hdr *= 11.2 / max(EnvironmentParameters.y, 0.0001);

            // EnvironmentParameters.z selects AgX; anything else keeps the exponential curve
            // this path used before, so scenes that never asked for AgX are unchanged.
            vec3 graded = EnvironmentParameters.z > 0.5
                ? agxCurve(hdr)
                : vec3(1.0) - exp(-max(hdr, vec3(0.0)) * 1.15);
            return pow(max(graded, vec3(0.0)), vec3(1.0 / 2.2));
        }

        float dirtHash(vec2 p)
        {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
        }

        float dirtNoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(dirtHash(i), dirtHash(i + vec2(1.0, 0.0)), u.x),
                       mix(dirtHash(i + vec2(0.0, 1.0)), dirtHash(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        float dirtFbm(vec2 p, int octaves)
        {
            const mat2 turn = mat2(0.8, -0.6, 0.6, 0.8);
            float total = 0.0;
            float amp = 0.5;
            float norm = 0.0;
            for (int i = 0; i < octaves; ++i)
            {
                total += dirtNoise(p) * amp;
                norm += amp;
                p = turn * p * 2.03 + 19.7;
                amp *= 0.5;
            }
            return total / max(norm, 0.0001);
        }

        float lensDirtMask(vec2 uv)
        {
            vec2 centred = uv - 0.5;
            vec2 aspect = vec2(textureSize(sampler2D(SceneTexture, SceneSampler), 0));
            vec2 p = vec2(centred.x * (aspect.x / max(aspect.y, 1.0)), centred.y);
            float smudge = smoothstep(0.46, 0.78, dirtFbm(p * 3.4, 5));
            float speck = smoothstep(0.70, 0.86, dirtFbm(p * 26.0 + 41.7, 3));
            float ang = atan(p.y, p.x);
            float streak = smoothstep(0.52, 0.84, dirtFbm(vec2(ang * 1.7, length(p) * 4.5) + 7.1, 4)) * 0.5;
            float edgeBias = 0.30 + 0.70 * smoothstep(0.05, 0.70, length(centred));
            return clamp((smudge * 0.55 + speck * 0.35 + streak) * edgeBias, 0.0, 1.0);
        }

        vec3 brightPass(vec3 color)
        {
            float brightness = max(max(color.r, color.g), color.b);
            float threshold = PostProcessParameters.x;
            // Keep only the amount by which this pixel exceeds the threshold, scaled back onto
            // its own hue. The previous smoothstep(threshold, 1.0, brightness) knee was written
            // for an LDR scene target: on the floating-point target it saturates to 1.0 for any
            // value above 1, so it returned the pixel at full strength and bloom became a set of
            // offset copies of the scene rather than a glow.
            float excess = max(brightness - threshold, 0.0);
            return color * (excess / max(brightness, 0.0001));
        }

        vec3 sampleBloom(vec2 uv, vec2 texel)
        {
            // One centred tap per downsampled level, weighted so the coarse levels supply the
            // wide falloff. Offset taps are deliberately absent: on a mip chain each tap is
            // already an average of many pixels, so a ring of them only stamps the sampling
            // pattern into the image - the diamond clusters that appeared around bright drives.
            vec3 bloom = vec3(0.0);
            float weight = 0.0;
            for (int level = 1; level <= 5; ++level)
            {
                float lod = float(level);
                float levelWeight = 1.0 / float(level);
                bloom += brightPass(textureLod(sampler2D(SceneTexture, SceneSampler), uv, lod).rgb) * levelWeight;
                weight += levelWeight;
            }

            return bloom / max(weight, 0.0001);
        }

        float resolveAmbientOcclusion(vec2 uv, vec2 texel)
        {
            int sampleCount = int(AmbientOcclusionParameters.x + 0.5);
            if (sampleCount <= 0)
            {
                return 1.0;
            }

            float centerDepth = texture(sampler2D(SceneDepthTexture, SceneDepthSampler), uv).r;
            if (centerDepth >= 0.99999)
            {
                return 1.0;
            }

            const vec2 directions[12] = vec2[](
                vec2(1.0, 0.0), vec2(0.707, 0.707), vec2(0.0, 1.0), vec2(-0.707, 0.707),
                vec2(-1.0, 0.0), vec2(-0.707, -0.707), vec2(0.0, -1.0), vec2(0.707, -0.707),
                vec2(0.383, 0.924), vec2(-0.924, 0.383), vec2(-0.383, -0.924), vec2(0.924, -0.383));
            vec4 centerWorldH = InverseViewProjection * vec4(uv * 2.0 - 1.0, centerDepth, 1.0);
            vec3 centerWorld = centerWorldH.xyz / max(abs(centerWorldH.w), 0.00001);
            float centerDistance = distance(CameraPosition.xyz, centerWorld);
            float radius = AmbientOcclusionParameters.y;
            float bias = AmbientOcclusionParameters.w;
            float occlusion = 0.0;
            for (int index = 0; index < 12; ++index)
            {
                if (index >= sampleCount)
                {
                    break;
                }

                float ring = 0.45 + 0.55 * (float(index + 1) / float(sampleCount));
                vec2 sampleUv = clamp(uv + directions[index] * texel * radius * ring, texel, vec2(1.0) - texel);
                float sampleDepth = texture(sampler2D(SceneDepthTexture, SceneDepthSampler), sampleUv).r;
                if (sampleDepth >= 0.99999)
                {
                    continue;
                }
                vec4 sampleWorldH = InverseViewProjection * vec4(sampleUv * 2.0 - 1.0, sampleDepth, 1.0);
                vec3 sampleWorld = sampleWorldH.xyz / max(abs(sampleWorldH.w), 0.00001);
                float depthDelta = centerDistance - distance(CameraPosition.xyz, sampleWorld);
                float blocker = smoothstep(bias, bias + 0.42, depthDelta);
                float rangeWeight = 1.0 - smoothstep(0.2, 3.5, depthDelta);
                occlusion += blocker * rangeWeight;
            }

            float normalized = occlusion / float(sampleCount);
            return clamp(1.0 - normalized * AmbientOcclusionParameters.z, 0.55, 1.0);
        }

        vec4 resolveFxaa(vec2 texel)
        {
            vec4 center = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV, 0.0);
            vec3 nw = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(-1.0, -1.0), 0.0).rgb;
            vec3 ne = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(1.0, -1.0), 0.0).rgb;
            vec3 sw = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(-1.0, 1.0), 0.0).rgb;
            vec3 se = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(1.0, 1.0), 0.0).rgb;
            float lumaCenter = luma(center.rgb);
            float lumaNw = luma(nw);
            float lumaNe = luma(ne);
            float lumaSw = luma(sw);
            float lumaSe = luma(se);
            float lumaMin = min(lumaCenter, min(min(lumaNw, lumaNe), min(lumaSw, lumaSe)));
            float lumaMax = max(lumaCenter, max(max(lumaNw, lumaNe), max(lumaSw, lumaSe)));
            float edgeContrast = lumaMax - lumaMin;
            if (edgeContrast < max(0.0312, lumaMax * 0.125))
            {
                return center;
            }

            vec2 direction = vec2(
                -((lumaNw + lumaNe) - (lumaSw + lumaSe)),
                 ((lumaNw + lumaSw) - (lumaNe + lumaSe)));
            float directionReduce = max((lumaNw + lumaNe + lumaSw + lumaSe) * 0.0078125, 0.0009765625);
            float directionScale = 1.0 / (min(abs(direction.x), abs(direction.y)) + directionReduce);
            direction = clamp(direction * directionScale, vec2(-8.0), vec2(8.0)) * texel;

            vec3 rgbA = 0.5 * (
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * (1.0 / 3.0 - 0.5)).rgb +
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * (2.0 / 3.0 - 0.5)).rgb);
            vec3 rgbB = rgbA * 0.5 + 0.25 * (
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * -0.5).rgb +
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * 0.5).rgb);
            float lumaB = luma(rgbB);
            return vec4((lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB, center.a);
        }

        void main()
        {
            vec2 texel = 1.0 / vec2(textureSize(sampler2D(SceneTexture, SceneSampler), 0));
            vec4 resolved = resolveFxaa(texel);
            float sceneDepth = texture(sampler2D(SceneDepthTexture, SceneDepthSampler), fsin_UV).r;
            // Scene alpha is an explicit accumulated geometry-coverage channel. Do not use
            // depth here: blend-mode draws correctly disable depth writes. The uniform flag
            // identifies a bound sky explicitly, so a valid 1x1 environment is not mistaken
            // for the engine's fallback texture.
            float sceneCoverage = resolved.a;
            if (SolidBackgroundLinear.w < 0.5)
            {
                // Scene colour was blended over the deterministic fallback clear. Replace
                // only the uncovered portion with the bound sky, including partial coverage
                // from transparent geometry and antialiased edges.
                vec3 sky = environmentBackground(fsin_UV);
                resolved.rgb += (sky - SolidBackgroundLinear.rgb) * (1.0 - sceneCoverage);
            }
            vec3 bloom = sampleBloom(fsin_UV, texel);
            float ambientOcclusion = resolveAmbientOcclusion(fsin_UV, texel);

            // The scene target is now linear HDR, so this pass owns the whole display
            // transform - matching the Vulkan capture path's tone-map pass. Bloom is added in
            // linear light before exposure, exactly as it is there.
            vec3 hdr = resolved.rgb * ambientOcclusion
                + bloom * PostProcessParameters.y * PostProcessParameters.w;

            if (EnvironmentParameters.w > 0.5)
            {
                hdr *= 1.0 + lensDirtMask(fsin_UV) * EnvironmentParameters.w * 0.6;
            }

            vec3 color = applyDisplayTransform(hdr);
            if (SolidBackgroundLinear.w > 0.5)
            {
                // A solid clear/background colour is a display-referred authoring value. Keep
                // exposure, tone-map, bloom and other effects available to the composed scene,
                // but subtract the baseline display transform from untouched background pixels
                // so their baseline appearance agrees with the Inspector swatch.
                vec3 baseline = applyDisplayTransform(SolidBackgroundLinear.rgb);
                color += (SolidBackgroundEncoded.rgb - baseline) * (1.0 - sceneCoverage);
            }
            float backgroundAlpha = SolidBackgroundLinear.w > 0.5
                ? SolidBackgroundEncoded.a
                : 1.0;
            float outputAlpha = sceneCoverage + backgroundAlpha * (1.0 - sceneCoverage);
            fsout_Color = vec4(clamp(color, 0.0, 1.0), clamp(outputAlpha, 0.0, 1.0));
        }
        """;

    internal const string HudVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;
        layout(location = 1) in vec4 Color;
        layout(location = 2) in vec2 UV;

        layout(location = 0) out vec4 fsin_Color;
        layout(location = 1) out vec2 fsin_UV;

        void main()
        {
            gl_Position = vec4(Position, 1.0);
            fsin_Color = Color;
            fsin_UV = UV;
        }
        """;

    internal const string HudFragmentShader = """
        #version 450

        layout(location = 0) in vec4 fsin_Color;
        layout(location = 1) in vec2 fsin_UV;
        layout(set = 0, binding = 0) uniform texture2D SurfaceTexture;
        layout(set = 0, binding = 1) uniform sampler SurfaceSampler;

        layout(location = 0) out vec4 fsout_Color;

        void main()
        {
            fsout_Color = fsin_Color * texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_UV);
        }
        """;
}
