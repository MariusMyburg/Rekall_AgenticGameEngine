#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in vec2 fragUv;
layout(location = 3) in vec3 fragWorldPosition;

layout(set = 0, binding = 0) uniform FrameUniform
{
    mat4 viewProjection;
    vec4 lightDirection;
    vec4 lightColor;
    vec4 lightPosition;
    vec4 cameraPosition;
    mat4 shadowViewProjection[4];
    vec4 shadowSplits;
    vec4 shadowParameters0;
    vec4 shadowParameters1;
    vec4 shadowCameraForward;
    vec4 additionalLightDirection;
    vec4 additionalLightColor;
    vec4 additionalLightPosition;
    vec4 additionalLightParameters;
    vec4 additionalLightColor2;
    vec4 additionalLightPosition2;
    vec4 additionalLightParameters2;
    vec4 additionalLightColor3;
    vec4 additionalLightPosition3;
    vec4 additionalLightParameters3;
    vec4 additionalLightColor4;
    vec4 additionalLightPosition4;
    vec4 additionalLightParameters4;
    vec4 additionalLightColor5;
    vec4 additionalLightPosition5;
    vec4 additionalLightParameters5;
    vec4 additionalLightColor6;
    vec4 additionalLightPosition6;
    vec4 additionalLightParameters6;
    vec4 additionalLightColor7;
    vec4 additionalLightPosition7;
    vec4 additionalLightParameters7;
    vec4 additionalLightColor8;
    vec4 additionalLightPosition8;
    vec4 additionalLightParameters8;
    vec4 additionalLightColor9;
    vec4 additionalLightPosition9;
    vec4 additionalLightParameters9;
    vec4 additionalLightColor10;
    vec4 additionalLightPosition10;
    vec4 additionalLightParameters10;
    vec4 additionalLightColor11;
    vec4 additionalLightPosition11;
    vec4 additionalLightParameters11;
    vec4 additionalLightColor12;
    vec4 additionalLightPosition12;
    vec4 additionalLightParameters12;
    vec4 additionalLightColor13;
    vec4 additionalLightPosition13;
    vec4 additionalLightParameters13;
    vec4 additionalLightColor14;
    vec4 additionalLightPosition14;
    vec4 additionalLightParameters14;
    vec4 additionalLightColor15;
    vec4 additionalLightPosition15;
    vec4 additionalLightParameters15;
    vec4 additionalLightColor16;
    vec4 additionalLightPosition16;
    vec4 additionalLightParameters16;
    vec4 spotLightColor;
    vec4 spotLightPosition;
    vec4 spotLightDirection;
    vec4 spotLightParameters;
    vec4 spotLightColor2;
    vec4 spotLightPosition2;
    vec4 spotLightDirection2;
    vec4 spotLightParameters2;
    vec4 spotLightColor3;
    vec4 spotLightPosition3;
    vec4 spotLightDirection3;
    vec4 spotLightParameters3;
    vec4 spotLightColor4;
    vec4 spotLightPosition4;
    vec4 spotLightDirection4;
    vec4 spotLightParameters4;
    vec4 environmentParameters;
    vec4 environmentAmbientSkyColor;
    vec4 environmentAmbientGroundColor;
} frame;

layout(set = 0, binding = 1) uniform texture2D environmentTexture;
layout(set = 0, binding = 2) uniform sampler environmentSampler;

layout(set = 1, binding = 0) uniform DrawUniformBuffer
{
    mat4 model;
    vec4 materialFactors;
    vec4 emissiveFactors;
    vec4 atmosphereFactors0;
    vec4 atmosphereFactors1;
    vec4 atmosphereColor0;
    vec4 atmosphereColor1;
    vec4 atmosphereColor2;
    vec4 cloudFactors;
    vec4 cloudColor;
    vec4 cloudShadowFactors;
    vec4 surfaceWaterFactors;
    vec4 shadowFactors;
} draw;

layout(set = 2, binding = 0) uniform texture2D baseColorTexture;
layout(set = 2, binding = 1) uniform sampler baseColorSampler;
layout(set = 2, binding = 2) uniform texture2D normalTexture;
layout(set = 2, binding = 3) uniform sampler normalSampler;
layout(set = 2, binding = 4) uniform texture2D metallicRoughnessTexture;
layout(set = 2, binding = 5) uniform sampler metallicRoughnessSampler;
layout(set = 2, binding = 6) uniform texture2D occlusionTexture;
layout(set = 2, binding = 7) uniform sampler occlusionSampler;
layout(set = 2, binding = 8) uniform texture2D emissiveTexture;
layout(set = 2, binding = 9) uniform sampler emissiveSampler;
layout(set = 2, binding = 10) uniform texture2D cloudShadowTexture;
layout(set = 2, binding = 11) uniform sampler cloudShadowSampler;
layout(set = 2, binding = 12) uniform texture2D surfaceWaterTexture;
layout(set = 2, binding = 13) uniform sampler surfaceWaterSampler;
#ifdef REKALL_DIRECTIONAL_SHADOWS
layout(set = 3, binding = 0) uniform sampler2DArrayShadow directionalShadowAtlas;
#endif

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;
const int MAX_VIEW_SAMPLE_COUNT = 32;
const int MAX_LIGHT_SAMPLE_COUNT = 16;
const int MAX_SHADOW_FILTER_TAPS = 24;

vec2 directionToEquirectangularUv(vec3 direction)
{
    vec3 d = normalize(direction);
    return vec2(atan(d.z, d.x) / (2.0 * PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
}

vec3 sampleEnvironmentRadiance(vec3 direction, float roughness)
{
    float maximumLod = max(float(textureQueryLevels(sampler2D(environmentTexture, environmentSampler)) - 1), 0.0);
    vec3 encoded = textureLod(
        sampler2D(environmentTexture, environmentSampler),
        directionToEquirectangularUv(direction),
        clamp(roughness, 0.0, 1.0) * maximumLod).rgb;
    return pow(max(encoded, vec3(0.0)), vec3(2.2));
}

float sampleDirectionalShadow(vec3 worldPosition, vec3 normal)
{
#ifndef REKALL_DIRECTIONAL_SHADOWS
    return 1.0;
#else
    int cascadeCount = int(frame.shadowParameters0.x + 0.5);
    if (frame.shadowParameters1.z < 0.5 || draw.shadowFactors.y < 0.5 || cascadeCount <= 0)
    {
        return 1.0;
    }

    float viewDepth = dot(worldPosition - frame.cameraPosition.xyz, normalize(frame.shadowCameraForward.xyz));
    if (viewDepth <= 0.0 || viewDepth > frame.shadowParameters1.y)
    {
        return 1.0;
    }

    int cascadeIndex = 0;
    if (cascadeCount > 1 && viewDepth > frame.shadowSplits.x) cascadeIndex = 1;
    if (cascadeCount > 2 && viewDepth > frame.shadowSplits.y) cascadeIndex = 2;
    if (cascadeCount > 3 && viewDepth > frame.shadowSplits.z) cascadeIndex = 3;
    vec3 biasedPosition = worldPosition + normal * frame.shadowParameters0.w;
    vec4 shadowClip = frame.shadowViewProjection[cascadeIndex] * vec4(biasedPosition, 1.0);
    vec3 shadowNdc = shadowClip.xyz / max(abs(shadowClip.w), 0.000001);
    vec2 uv = shadowNdc.xy * 0.5 + 0.5;
    float referenceDepth = shadowNdc.z - frame.shadowParameters0.z;
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))) || referenceDepth <= 0.0 || referenceDepth >= 1.0)
    {
        return 1.0;
    }

    int tapCount = clamp(int(frame.shadowParameters1.x + 0.5), 1, MAX_SHADOW_FILTER_TAPS);
    float texel = 1.0 / max(frame.shadowParameters0.y, 1.0);
    float visibility = 0.0;
    for (int tap = 0; tap < MAX_SHADOW_FILTER_TAPS; ++tap)
    {
        if (tap >= tapCount) break;
        int x = (tap % 5) - 2;
        int y = ((tap * 3) % 5) - 2;
        visibility += texture(directionalShadowAtlas, vec4(uv + vec2(x, y) * texel, float(cascadeIndex), referenceDepth));
    }
    return visibility / float(tapCount);
#endif
}

vec3 perturbNormal(vec3 normal)
{
    vec3 tangentNormal = texture(sampler2D(normalTexture, normalSampler), fragUv).xyz * 2.0 - 1.0;
    tangentNormal.xy *= draw.materialFactors.z;
    vec3 q1 = dFdx(fragWorldPosition);
    vec3 q2 = dFdy(fragWorldPosition);
    vec2 st1 = dFdx(fragUv);
    vec2 st2 = dFdy(fragUv);
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

vec3 fresnelSchlickRoughness(float cosTheta, vec3 f0, float roughness)
{
    // Split-sum IBL approximation used when an authored environment supplies a
    // hemispherical sky/ground radiance but no reflection cubemap. This keeps metals
    // physically reflective instead of turning them into black diffuse voids; a future
    // probe/cubemap path can replace the radiance lookup without changing materials.
    vec3 grazing = max(vec3(1.0 - roughness), f0);
    return f0 + (grazing - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
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
    return draw.atmosphereFactors0.y > 0.0;
}

bool isAtmosphereShell()
{
    return hasAtmosphereData() && draw.atmosphereFactors1.w >= 0.0;
}

float atmosphereSunIntensity()
{
    return abs(draw.atmosphereFactors1.w);
}

vec3 atmosphereLightColor()
{
    return max(frame.lightColor.rgb, vec3(0.0));
}

vec3 primaryLightVector(vec3 worldPosition)
{
    return frame.lightPosition.w > 0.5
        ? frame.lightPosition.xyz - worldPosition
        : -frame.lightDirection.xyz;
}

bool hasPrimaryLight(vec3 worldPosition)
{
    vec3 lightVector = primaryLightVector(worldPosition);
    return dot(frame.lightColor.rgb, frame.lightColor.rgb) > 0.000001
        && dot(lightVector, lightVector) > 0.000001;
}

vec3 primaryLightDirection(vec3 worldPosition)
{
    vec3 lightVector = primaryLightVector(worldPosition);
    float lightLength = length(lightVector);
    return lightLength > 0.000001 ? lightVector / lightLength : vec3(0.0);
}

float ozoneAbsorption()
{
    return max(draw.atmosphereColor2.w, 0.0);
}

bool shouldDiscardAtmosphereBackHemisphere(vec3 rayOrigin, vec3 rayDirection, vec3 planetCenter, float atmosphereRadius)
{
    float cameraRadius = length(rayOrigin - planetCenter);
    if (cameraRadius <= atmosphereRadius)
    {
        return false;
    }

    vec3 shellNormal = normalize(fragWorldPosition - planetCenter);
    return dot(shellNormal, rayDirection) > 0.0;
}

vec3 atmosphereExtinction()
{
    float rayleighStrength = max(draw.atmosphereFactors1.x, 0.0);
    float mieStrength = max(draw.atmosphereFactors1.y, 0.0);
    return draw.atmosphereColor0.rgb * rayleighStrength
        + draw.atmosphereColor1.rgb * mieStrength
        + draw.atmosphereColor2.rgb * ozoneAbsorption();
}

vec3 surfaceAtmosphereExtinction()
{
    float rayleighStrength = max(draw.atmosphereFactors1.x, 0.0);
    float mieStrength = max(draw.atmosphereFactors1.y, 0.0);
    vec3 rayleighWavelengthWeight = vec3(0.45, 0.95, 1.85);
    return rayleighWavelengthWeight * rayleighStrength
        + vec3(mieStrength)
        + draw.atmosphereColor2.rgb * ozoneAbsorption();
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
    return clamp(draw.atmosphereColor1.w, 0.0, 2.0);
}

vec3 surfaceAtmosphereTransmittance(vec3 surfacePosition, vec3 lightDirection)
{
    if (!hasAtmosphereData() || dot(lightDirection, lightDirection) <= 0.000001)
    {
        return vec3(1.0);
    }

    float planetRadius = max(draw.atmosphereFactors0.x, 0.0001);
    float atmosphereRadius = max(draw.atmosphereFactors0.y, planetRadius + 0.0001);
    float density = max(draw.atmosphereFactors0.z, 0.0);
    float densityFalloff = max(draw.atmosphereFactors0.w, 0.001);
    vec3 planetCenter = draw.model[3].xyz;
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
    if (draw.cloudShadowFactors.x <= 0.5 || dot(lightDirection, lightDirection) <= 0.000001)
    {
        return 1.0;
    }

    vec3 planetCenter = draw.model[3].xyz;
    float cloudRadius = max(draw.cloudShadowFactors.y, 0.0001);
    vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
    vec3 rayOrigin = surfacePosition + surfaceNormal * max(cloudRadius * 0.0001, 0.0001);
    vec2 cloudHit;
    if (!intersectSphere(rayOrigin, normalize(lightDirection), planetCenter, cloudRadius, cloudHit) || cloudHit.y <= 0.0)
    {
        return 1.0;
    }

    float hitDistance = cloudHit.x > 0.0 ? cloudHit.x : cloudHit.y;
    vec3 cloudPoint = rayOrigin + normalize(lightDirection) * hitDistance;
    float coverage = texture(sampler2D(cloudShadowTexture, cloudShadowSampler), sphericalUv(cloudPoint - planetCenter)).a;
    float strength = clamp(draw.cloudShadowFactors.z, 0.0, 1.0);
    float daylight = planetShadowFactor(surfacePosition, normalize(lightDirection), planetCenter);
    return clamp(1.0 - coverage * strength * daylight, 0.0, 1.0);
}

bool hasSurfaceWater()
{
    return draw.surfaceWaterFactors.x > 0.5;
}

float surfaceWaterSpecularStrength()
{
    return clamp(draw.surfaceWaterFactors.z, 0.0, 8.0);
}

float sampleSurfaceWaterCoverage(vec2 uv, vec3 baseTextureColor, out vec3 waterTint)
{
    vec4 water = texture(sampler2D(surfaceWaterTexture, surfaceWaterSampler), uv);
    waterTint = mix(vec3(0.006, 0.075, 0.34), pow(max(water.rgb, vec3(0.0)), vec3(2.2)), 0.35);
    float waterColorPresence = max(max(water.r, water.g), water.b) * water.a;
    float baseBlueDominance = baseTextureColor.b - max(baseTextureColor.r, baseTextureColor.g);
    float baseSaturation = max(max(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b)
        - min(min(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b);
    float authoredWaterRegion = smoothstep(0.015, 0.12, baseBlueDominance)
        * smoothstep(0.03, 0.18, baseSaturation);
    float mask = max(waterColorPresence * authoredWaterRegion, waterColorPresence * 0.65);
    return clamp(mask * clamp(draw.surfaceWaterFactors.y, 0.0, 4.0), 0.0, 1.0);
}

vec3 surfaceAerialPerspectiveScattering(vec3 rayOrigin, vec3 rayDirection, float rayStart, float rayEnd, vec3 sunDirection)
{
    float planetRadius = max(draw.atmosphereFactors0.x, 0.0001);
    float atmosphereRadius = max(draw.atmosphereFactors0.y, planetRadius + 0.0001);
    float density = max(draw.atmosphereFactors0.z, 0.0);
    float densityFalloff = max(draw.atmosphereFactors0.w, 0.001);
    float rayleighStrength = max(draw.atmosphereFactors1.x, 0.0);
    float mieStrength = max(draw.atmosphereFactors1.y, 0.0);
    float mieAnisotropy = clamp(draw.atmosphereFactors1.z, -0.99, 0.99);
    vec3 planetCenter = draw.model[3].xyz;
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
    vec3 rayleigh = draw.atmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
    vec3 mie = draw.atmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
    return (rayleigh + mie) * scattered * atmosphereSunIntensity() * atmosphereLightColor();
}

vec3 applySurfaceAerialPerspective(vec3 surfaceColor, vec3 surfacePosition, vec3 lightDirection)
{
    if (!hasAtmosphereData())
    {
        return surfaceColor;
    }

    float planetRadius = max(draw.atmosphereFactors0.x, 0.0001);
    float atmosphereRadius = max(draw.atmosphereFactors0.y, planetRadius + 0.0001);
    float density = max(draw.atmosphereFactors0.z, 0.0);
    float densityFalloff = max(draw.atmosphereFactors0.w, 0.001);
    vec3 planetCenter = draw.model[3].xyz;
    vec3 rayOrigin = frame.cameraPosition.xyz;
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
    bool directLightAvailable = dot(frame.lightColor.rgb, frame.lightColor.rgb) > 0.000001
        && dot(lightDirection, lightDirection) > 0.000001;
    vec3 scattering = directLightAvailable
        ? surfaceAerialPerspectiveScattering(rayOrigin, rayDirection, rayStart, rayEnd, normalize(lightDirection))
        : vec3(0.0);
    vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
    float surfaceSun = directLightAvailable
        ? smoothstep(-0.08, 0.18, dot(surfaceNormal, normalize(lightDirection)))
        : 0.0;
    vec3 scatteringTint = mix(vec3(1.0), vec3(0.55, 0.78, 1.35), lowAltitudeView);
    return surfaceColor * transmittance + scattering * scatteringTint * effectiveStrength * surfaceSun;
}

vec4 renderAtmosphere()
{
    float planetRadius = max(draw.atmosphereFactors0.x, 0.0001);
    float atmosphereRadius = max(draw.atmosphereFactors0.y, planetRadius + 0.0001);
    float density = max(draw.atmosphereFactors0.z, 0.0);
    float densityFalloff = max(draw.atmosphereFactors0.w, 0.001);
    float rayleighStrength = max(draw.atmosphereFactors1.x, 0.0);
    float mieStrength = max(draw.atmosphereFactors1.y, 0.0);
    float mieAnisotropy = clamp(draw.atmosphereFactors1.z, -0.99, 0.99);
    float sunIntensity = atmosphereSunIntensity();
    int viewSampleCount = int(clamp(draw.materialFactors.x, 4.0, float(MAX_VIEW_SAMPLE_COUNT)));
    int lightSampleCount = int(clamp(draw.materialFactors.y, 2.0, float(MAX_LIGHT_SAMPLE_COUNT)));
    vec3 planetCenter = draw.model[3].xyz;
    vec3 rayOrigin = frame.cameraPosition.xyz;
    vec3 rayDirection = normalize(fragWorldPosition - rayOrigin);
    if (!hasPrimaryLight(fragWorldPosition))
    {
        return vec4(0.0);
    }
    vec3 sunDirection = primaryLightDirection(fragWorldPosition);
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
    vec3 rayleigh = draw.atmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
    vec3 mie = draw.atmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
    vec3 color = (rayleigh + mie) * scattered * sunIntensity * atmosphereLightColor();
    vec3 shellNormal = normalize(fragWorldPosition - planetCenter);
    vec3 viewDirection = normalize(rayOrigin - fragWorldPosition);
    float limb = pow(clamp(1.0 - abs(dot(shellNormal, viewDirection)), 0.0, 1.0), 3.5);
    float sunlitRim = smoothstep(-0.22, 0.18, dot(shellNormal, sunDirection));
    color += draw.atmosphereColor0.rgb * atmosphereLightColor() * limb * sunlitRim * rayleighStrength * sunIntensity * 0.18;
    vec3 mapped = vec3(1.0) - exp(-color * max(draw.emissiveFactors.a, 0.0));
    float alpha = clamp(max(max(mapped.r, mapped.g), mapped.b) * 1.8, 0.0, 0.9);
#ifdef REKALL_HDR_SCENE_OUTPUT
    return vec4(max(color, vec3(0.0)), alpha);
#else
    return vec4(pow(mapped, vec3(1.0 / 2.2)), alpha);
#endif
}

bool isCloudLayer()
{
    return draw.cloudFactors.y > 0.0;
}

bool cloudAlphaFromTextureOnly()
{
    return draw.cloudFactors.x > 0.5;
}

float cloudSkyVisibility(vec3 cloudPosition, vec3 sunDirection, vec3 planetCenter)
{
    return planetShadowFactor(cloudPosition, sunDirection, planetCenter);
}

vec4 renderCloudLayer()
{
    vec3 rayOrigin = frame.cameraPosition.xyz;
    vec3 rayDirection = normalize(fragWorldPosition - rayOrigin);
    vec3 planetCenter = draw.model[3].xyz;
    float shellRadius = max(length(fragWorldPosition - planetCenter), 0.0001);
    if (shouldDiscardAtmosphereBackHemisphere(rayOrigin, rayDirection, planetCenter, shellRadius))
    {
        discard;
    }

    vec3 normal = normalize(fragWorldPosition - planetCenter);
    vec3 view = normalize(rayOrigin - fragWorldPosition);
    vec3 light = primaryLightDirection(fragWorldPosition);
    float lambertian = max(dot(normal, light), 0.0);
    float skyVisibility = cloudSkyVisibility(fragWorldPosition, light, planetCenter);

    vec4 textureColor = texture(sampler2D(baseColorTexture, baseColorSampler), fragUv);
    float textureCoverage = cloudAlphaFromTextureOnly()
        ? textureColor.a
        : max(max(textureColor.r, textureColor.g), textureColor.b) * textureColor.a;
    float slantPath = clamp(1.0 / max(abs(dot(normal, view)), 0.22), 1.0, 3.25);
    float opticalDepth = textureCoverage * max(draw.cloudFactors.y, 0.0) * max(draw.cloudColor.a, 0.0) * slantPath;
    float alpha = clamp((1.0 - exp(-opticalDepth)) * mix(0.32, 1.0, skyVisibility), 0.0, 0.72);
    if (alpha < 0.01)
    {
        discard;
    }

    vec3 cloudBase = cloudAlphaFromTextureOnly()
        ? vec3(0.92, 0.95, 1.0)
        : mix(vec3(0.88, 0.91, 0.96), pow(max(textureColor.rgb, vec3(0.0)), vec3(2.2)), 0.82);
    float lightTerm = mix(1.0, lambertian, clamp(draw.cloudFactors.z, 0.0, 1.0));
    vec3 directTransmittance = surfaceAtmosphereTransmittance(fragWorldPosition, light);
    float ambientTerm = max(draw.cloudFactors.w, 0.0) * mix(0.04, 1.0, skyVisibility);
    float sunView = clamp(dot(light, view), 0.0, 1.0);
    float silverLining = pow(sunView, 12.0) * smoothstep(0.0, 0.35, lambertian) * skyVisibility;
    vec3 color = cloudBase
        * pow(max(draw.cloudColor.rgb, vec3(0.0)), vec3(2.2))
        * (frame.lightColor.rgb * directTransmittance * lightTerm * skyVisibility * 2.65 + vec3(ambientTerm));
    color += frame.lightColor.rgb * directTransmittance * silverLining * 0.75;
    color = applySurfaceAerialPerspective(color, fragWorldPosition, light);
#ifdef REKALL_HDR_SCENE_OUTPUT
    return vec4(max(color, vec3(0.0)), alpha);
#else
    return vec4(pow(max(color, vec3(0.0)), vec3(1.0 / 2.2)), alpha);
#endif
}

void main()
{
    if (isAtmosphereShell())
    {
        outColor = renderAtmosphere();
        return;
    }

    if (isCloudLayer())
    {
        outColor = renderCloudLayer();
        return;
    }

    vec4 textureColor = texture(sampler2D(baseColorTexture, baseColorSampler), fragUv);
    vec3 albedo = pow(max(fragColor.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
    vec4 metalRough = texture(sampler2D(metallicRoughnessTexture, metallicRoughnessSampler), fragUv);
    float metallic = clamp(metalRough.b * draw.materialFactors.x, 0.0, 1.0);
    float roughness = clamp(metalRough.g * draw.materialFactors.y, 0.04, 1.0);
    vec3 waterTint = vec3(0.0);
    float waterCoverage = hasSurfaceWater() ? sampleSurfaceWaterCoverage(fragUv, textureColor.rgb, waterTint) : 0.0;
    if (waterCoverage > 0.0001)
    {
        albedo = mix(albedo, waterTint, waterCoverage * 0.78);
        roughness = mix(roughness, clamp(draw.surfaceWaterFactors.w, 0.01, 1.0), waterCoverage);
        metallic = mix(metallic, 0.0, waterCoverage);
    }
    float occlusion = 1.0;
    if (draw.materialFactors.w > 0.0001)
    {
        occlusion = mix(1.0, texture(sampler2D(occlusionTexture, occlusionSampler), fragUv).r, draw.materialFactors.w);
    }
    vec3 light = primaryLightDirection(fragWorldPosition);
    vec3 view = normalize(frame.cameraPosition.xyz - fragWorldPosition);
    vec3 normal = hasAtmosphereData()
        ? normalize(fragWorldPosition - draw.model[3].xyz)
        : normalize(fragNormal);
    if (dot(normal, view) < 0.0)
    {
        normal = -normal;
    }

    if (draw.materialFactors.z > 0.0001)
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
    vec3 directTransmittance = surfaceAtmosphereTransmittance(fragWorldPosition, light);
    float ambientStrength = hasAtmosphereData()
        ? spaceAmbientFloor()
        : 0.12 * max(frame.environmentParameters.x, 0.0);
    float ambientHemisphere = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 environmentAmbientColor = mix(frame.environmentAmbientGroundColor.rgb, frame.environmentAmbientSkyColor.rgb, ambientHemisphere);
    vec3 ambientFresnel = fresnelSchlickRoughness(ndotv, f0, roughness);
    vec3 ambientDiffuse = (1.0 - ambientFresnel) * (1.0 - metallic) * albedo;
    bool hasEnvironmentImage = frame.environmentAmbientSkyColor.a > 0.5;
    vec3 imageDiffuse = hasEnvironmentImage
        ? sampleEnvironmentRadiance(normal, 0.82)
        : environmentAmbientColor;
    vec3 imageSpecular = hasEnvironmentImage
        ? sampleEnvironmentRadiance(reflect(-view, normal), roughness)
        : environmentAmbientColor * mix(0.28, 1.0, 1.0 - roughness);
    vec3 ambientSpecular = ambientFresnel * imageSpecular;
    vec3 ambient = (ambientDiffuse * imageDiffuse + ambientSpecular)
        * ambientStrength
        * occlusion;
    vec3 waterFresnel = fresnelSchlick(ndotv, vec3(0.02));
    ambient += waterFresnel * frame.lightColor.rgb * directTransmittance * waterCoverage * 0.018;
    vec3 emissive = pow(max(texture(sampler2D(emissiveTexture, emissiveSampler), fragUv).rgb * draw.emissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * draw.emissiveFactors.a;
    float cloudShadow = sampleCloudShadow(fragWorldPosition, light);
    float directionalShadow = sampleDirectionalShadow(fragWorldPosition, normal);
    vec3 additionalDirect = vec3(0.0);
    vec4 practicalColors[16] = vec4[](frame.additionalLightColor, frame.additionalLightColor2, frame.additionalLightColor3, frame.additionalLightColor4, frame.additionalLightColor5, frame.additionalLightColor6, frame.additionalLightColor7, frame.additionalLightColor8, frame.additionalLightColor9, frame.additionalLightColor10, frame.additionalLightColor11, frame.additionalLightColor12, frame.additionalLightColor13, frame.additionalLightColor14, frame.additionalLightColor15, frame.additionalLightColor16);
    vec4 practicalPositions[16] = vec4[](frame.additionalLightPosition, frame.additionalLightPosition2, frame.additionalLightPosition3, frame.additionalLightPosition4, frame.additionalLightPosition5, frame.additionalLightPosition6, frame.additionalLightPosition7, frame.additionalLightPosition8, frame.additionalLightPosition9, frame.additionalLightPosition10, frame.additionalLightPosition11, frame.additionalLightPosition12, frame.additionalLightPosition13, frame.additionalLightPosition14, frame.additionalLightPosition15, frame.additionalLightPosition16);
    vec4 practicalParameters[16] = vec4[](frame.additionalLightParameters, frame.additionalLightParameters2, frame.additionalLightParameters3, frame.additionalLightParameters4, frame.additionalLightParameters5, frame.additionalLightParameters6, frame.additionalLightParameters7, frame.additionalLightParameters8, frame.additionalLightParameters9, frame.additionalLightParameters10, frame.additionalLightParameters11, frame.additionalLightParameters12, frame.additionalLightParameters13, frame.additionalLightParameters14, frame.additionalLightParameters15, frame.additionalLightParameters16);
    int practicalCount = clamp(int(frame.additionalLightDirection.w + 0.5), 0, 16);
    for (int practicalIndex = 0; practicalIndex < practicalCount; practicalIndex++)
    {
        if (dot(practicalColors[practicalIndex].rgb, practicalColors[practicalIndex].rgb) <= 0.000001) continue;
        vec3 practicalOffset = practicalPositions[practicalIndex].xyz - fragWorldPosition;
        float practicalDistance = length(practicalOffset);
        vec3 additionalLight = practicalOffset / max(practicalDistance, 0.0001);
        float practicalRange = max(practicalParameters[practicalIndex].x, 0.001);
        float practicalWindow = pow(clamp(1.0 - practicalDistance / practicalRange, 0.0, 1.0), 2.0);
        float practicalAttenuation = practicalWindow / (1.0 + 0.045 * practicalDistance * practicalDistance);
        vec3 additionalHalfVector = normalize(view + additionalLight);
        float additionalNdotl = max(dot(normal, additionalLight), 0.0);
        float additionalD = distributionGgx(normal, additionalHalfVector, roughness);
        float additionalG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(additionalNdotl, roughness);
        vec3 additionalF = fresnelSchlick(max(dot(additionalHalfVector, view), 0.0), f0);
        vec3 additionalSpecular = additionalD * additionalG * additionalF / max(4.0 * ndotv * additionalNdotl, 0.0001);
        additionalSpecular *= mix(1.0, surfaceWaterSpecularStrength(), waterCoverage);
        vec3 additionalDiffuse = (1.0 - additionalF) * (1.0 - metallic) * albedo / PI;
        additionalDiffuse *= mix(1.0, 0.42, waterCoverage);
        vec3 additionalTransmittance = surfaceAtmosphereTransmittance(fragWorldPosition, additionalLight);
        additionalDirect += (additionalDiffuse + additionalSpecular)
            * practicalColors[practicalIndex].rgb
            * additionalTransmittance
            * additionalNdotl
            * practicalAttenuation
            * 4.5;
    }
    vec4 spotColors[4] = vec4[](frame.spotLightColor, frame.spotLightColor2, frame.spotLightColor3, frame.spotLightColor4);
    vec4 spotPositions[4] = vec4[](frame.spotLightPosition, frame.spotLightPosition2, frame.spotLightPosition3, frame.spotLightPosition4);
    vec4 spotDirections[4] = vec4[](frame.spotLightDirection, frame.spotLightDirection2, frame.spotLightDirection3, frame.spotLightDirection4);
    vec4 spotParameters[4] = vec4[](frame.spotLightParameters, frame.spotLightParameters2, frame.spotLightParameters3, frame.spotLightParameters4);
    int spotCount = clamp(int(frame.spotLightDirection.w + 0.5), 0, 4);
    for (int spotIndex = 0; spotIndex < spotCount; spotIndex++)
    {
        if (dot(spotColors[spotIndex].rgb, spotColors[spotIndex].rgb) <= 0.000001) continue;
        vec3 spotOffset = spotPositions[spotIndex].xyz - fragWorldPosition;
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
        vec3 spotHalfVector = normalize(view + spotLight);
        float spotNdotl = max(dot(normal, spotLight), 0.0);
        float spotD = distributionGgx(normal, spotHalfVector, roughness);
        float spotG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(spotNdotl, roughness);
        vec3 spotF = fresnelSchlick(max(dot(spotHalfVector, view), 0.0), f0);
        vec3 spotSpecular = spotD * spotG * spotF / max(4.0 * ndotv * spotNdotl, 0.0001);
        spotSpecular *= mix(1.0, surfaceWaterSpecularStrength(), waterCoverage);
        vec3 spotDiffuse = (1.0 - spotF) * (1.0 - metallic) * albedo / PI;
        spotDiffuse *= mix(1.0, 0.42, waterCoverage);
        vec3 spotTransmittance = surfaceAtmosphereTransmittance(fragWorldPosition, spotLight);
        additionalDirect += (spotDiffuse + spotSpecular)
            * spotColors[spotIndex].rgb
            * spotTransmittance
            * spotNdotl
            * spotAttenuation
            * 4.5;
    }
    vec3 color = emissive
        + ambient
        + (diffuse + specular) * frame.lightColor.rgb * directTransmittance * cloudShadow * directionalShadow * ndotl * 1.8
        + additionalDirect;
    color = applySurfaceAerialPerspective(color, fragWorldPosition, light);
    float surfaceAlpha = hasAtmosphereData() ? fragColor.a : fragColor.a * textureColor.a;
    // draw.shadowFactors.z/.w carry AlphaMask/AlphaCutoff (packed here alongside CastShadows/
    // ReceiveShadows in .x/.y rather than adding new uniform fields). A masked material discards
    // below its cutoff instead of blending - the real alpha-tested cutout behavior (foliage,
    // chain-link) as opposed to AlphaMode "blend"'s soft transparency.
    if (draw.shadowFactors.z > 0.5 && surfaceAlpha < draw.shadowFactors.w)
    {
        discard;
    }
#ifdef REKALL_HDR_SCENE_OUTPUT
    outColor = vec4(max(color, vec3(0.0)), surfaceAlpha);
#else
    vec3 mapped = vec3(1.0) - exp(-max(color, vec3(0.0)) * 1.15);
    vec3 lit = pow(mapped, vec3(1.0 / 2.2));
    outColor = vec4(lit, surfaceAlpha);
#endif
}
