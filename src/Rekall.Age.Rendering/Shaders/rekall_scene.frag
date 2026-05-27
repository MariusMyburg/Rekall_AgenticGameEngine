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
} frame;

layout(set = 0, binding = 1) uniform sampler2D baseColorTexture;
layout(set = 0, binding = 2) uniform sampler2D normalTexture;
layout(set = 0, binding = 3) uniform sampler2D metallicRoughnessTexture;
layout(set = 0, binding = 4) uniform sampler2D occlusionTexture;
layout(set = 0, binding = 5) uniform sampler2D emissiveTexture;
layout(set = 0, binding = 6) uniform sampler2D cloudShadowTexture;
layout(set = 0, binding = 7) uniform sampler2D surfaceWaterTexture;

layout(push_constant) uniform DrawPushConstants
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
} draw;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;
const int MAX_VIEW_SAMPLE_COUNT = 32;
const int MAX_LIGHT_SAMPLE_COUNT = 16;

vec3 perturbNormal(vec3 normal)
{
    vec3 tangentNormal = texture(normalTexture, fragUv).xyz * 2.0 - 1.0;
    tangentNormal.xy *= draw.materialFactors.z;
    vec3 q1 = dFdx(fragWorldPosition);
    vec3 q2 = dFdy(fragWorldPosition);
    vec2 st1 = dFdx(fragUv);
    vec2 st2 = dFdy(fragUv);
    vec3 tangent = normalize(q1 * st2.t - q2 * st1.t);
    vec3 bitangent = normalize(-q1 * st2.s + q2 * st1.s);
    mat3 tbn = mat3(tangent, bitangent, normal);
    return normalize(tbn * tangentNormal);
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
    if (!hasAtmosphereData())
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
    if (draw.cloudShadowFactors.x <= 0.5)
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
    float coverage = texture(cloudShadowTexture, sphericalUv(cloudPoint - planetCenter)).a;
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
    vec4 water = texture(surfaceWaterTexture, uv);
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
    vec3 scattering = surfaceAerialPerspectiveScattering(rayOrigin, rayDirection, rayStart, rayEnd, normalize(lightDirection));
    vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
    float surfaceSun = smoothstep(-0.08, 0.18, dot(surfaceNormal, normalize(lightDirection)));
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
    vec3 sunDirection = frame.lightPosition.w > 0.5
        ? normalize(frame.lightPosition.xyz - fragWorldPosition)
        : normalize(-frame.lightDirection.xyz);
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
    return vec4(pow(mapped, vec3(1.0 / 2.2)), alpha);
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

    vec4 textureColor = texture(baseColorTexture, fragUv);
    float textureCoverage = cloudAlphaFromTextureOnly()
        ? textureColor.a
        : max(max(textureColor.r, textureColor.g), textureColor.b) * textureColor.a;
    float alpha = clamp(textureCoverage * max(draw.cloudFactors.y, 0.0) * draw.cloudColor.a, 0.0, 1.0);
    if (alpha < 0.01)
    {
        discard;
    }

    vec3 cloudBase = cloudAlphaFromTextureOnly() ? vec3(1.0) : pow(max(textureColor.rgb, vec3(0.0)), vec3(2.2));
    vec3 normal = normalize(fragWorldPosition - planetCenter);
    vec3 light = frame.lightPosition.w > 0.5
        ? normalize(frame.lightPosition.xyz - fragWorldPosition)
        : normalize(-frame.lightDirection.xyz);
    float lambertian = max(dot(normal, light), 0.0);
    float skyVisibility = cloudSkyVisibility(fragWorldPosition, light, planetCenter);
    float lightTerm = mix(1.0, lambertian, clamp(draw.cloudFactors.z, 0.0, 1.0));
    vec3 directTransmittance = surfaceAtmosphereTransmittance(fragWorldPosition, light);
    float ambientTerm = max(draw.cloudFactors.w, 0.0) * mix(0.04, 1.0, skyVisibility);
    vec3 color = cloudBase
        * pow(max(draw.cloudColor.rgb, vec3(0.0)), vec3(2.2))
        * (frame.lightColor.rgb * directTransmittance * lightTerm * skyVisibility * 3.2 + vec3(ambientTerm));
    color = applySurfaceAerialPerspective(color, fragWorldPosition, light);
    return vec4(pow(max(color, vec3(0.0)), vec3(1.0 / 2.2)), alpha);
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

    vec4 textureColor = texture(baseColorTexture, fragUv);
    vec3 albedo = pow(max(fragColor.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
    vec4 metalRough = texture(metallicRoughnessTexture, fragUv);
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
        occlusion = mix(1.0, texture(occlusionTexture, fragUv).r, draw.materialFactors.w);
    }
    vec3 light = frame.lightPosition.w > 0.5
        ? normalize(frame.lightPosition.xyz - fragWorldPosition)
        : normalize(-frame.lightDirection.xyz);
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
    float ambientStrength = hasAtmosphereData() ? spaceAmbientFloor() : 0.035;
    vec3 ambient = albedo * ambientStrength * occlusion;
    vec3 waterFresnel = fresnelSchlick(ndotv, vec3(0.02));
    ambient += waterFresnel * frame.lightColor.rgb * directTransmittance * waterCoverage * 0.018;
    vec3 emissive = pow(max(texture(emissiveTexture, fragUv).rgb * draw.emissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * draw.emissiveFactors.a;
    float cloudShadow = sampleCloudShadow(fragWorldPosition, light);
    vec3 color = emissive + ambient + (diffuse + specular) * frame.lightColor.rgb * directTransmittance * cloudShadow * ndotl * 1.8;
    color = applySurfaceAerialPerspective(color, fragWorldPosition, light);
    vec3 mapped = vec3(1.0) - exp(-max(color, vec3(0.0)) * 1.15);
    vec3 lit = pow(mapped, vec3(1.0 / 2.2));
    float surfaceAlpha = hasAtmosphereData() ? fragColor.a : fragColor.a * textureColor.a;
    outColor = vec4(lit, surfaceAlpha);
}
