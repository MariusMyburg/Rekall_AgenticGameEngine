#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sceneDepth;

layout(push_constant) uniform SsaoParameters
{
    vec4 texelRadiusStrength;
    vec4 depthProjection;
    vec4 execution;
} parameters;

const vec2 sampleDisk[12] = vec2[](
    vec2(1.000000, 0.000000),
    vec2(0.866025, 0.500000),
    vec2(0.500000, 0.866025),
    vec2(0.000000, 1.000000),
    vec2(-0.500000, 0.866025),
    vec2(-0.866025, 0.500000),
    vec2(-1.000000, 0.000000),
    vec2(-0.866025, -0.500000),
    vec2(-0.500000, -0.866025),
    vec2(0.000000, -1.000000),
    vec2(0.500000, -0.866025),
    vec2(0.866025, -0.500000));

float linearViewDepth(float depth)
{
    float nearPlane = max(parameters.depthProjection.x, 0.001);
    float farPlane = max(parameters.depthProjection.y, nearPlane + 0.001);
    if (parameters.depthProjection.w > 0.5)
    {
        return mix(nearPlane, farPlane, depth);
    }

    return nearPlane * farPlane
        / max(farPlane - depth * (farPlane - nearPlane), 0.000001);
}

void main()
{
    float centerRaw = texture(sceneDepth, fragUv).r;
    if (centerRaw >= 0.999999)
    {
        outColor = vec4(1.0);
        return;
    }

    float centerDepth = linearViewDepth(centerRaw);
    float angle = parameters.execution.y * 2.39996323;
    float cosine = cos(angle);
    float sine = sin(angle);
    mat2 rotation = mat2(cosine, -sine, sine, cosine);
    vec2 texel = max(parameters.texelRadiusStrength.xy, vec2(0.0000001));
    float radius = max(parameters.texelRadiusStrength.z, 0.0);
    float bias = max(parameters.depthProjection.z, 0.0);
    int sampleCount = clamp(int(parameters.execution.x + 0.5), 1, 12);
    float depthRange = max(centerDepth * 0.06, 0.25);
    float occlusion = 0.0;
    float weightSum = 0.0;

    for (int index = 0; index < 12; index++)
    {
        if (index >= sampleCount)
        {
            break;
        }

        float ring = 0.35 + 0.65 * (float(index + 1) / float(sampleCount));
        vec2 sampleUv = clamp(
            fragUv + rotation * sampleDisk[index] * texel * radius * ring,
            texel * 0.5,
            vec2(1.0) - texel * 0.5);
        float sampleRaw = texture(sceneDepth, sampleUv).r;
        if (sampleRaw >= 0.999999)
        {
            continue;
        }

        float sampleDepth = linearViewDepth(sampleRaw);
        float separation = centerDepth - sampleDepth;
        float rangeWeight = 1.0 - smoothstep(0.0, depthRange, abs(separation));
        occlusion += step(bias, separation) * rangeWeight;
        weightSum += rangeWeight;
    }

    float normalized = weightSum > 0.000001 ? occlusion / weightSum : 0.0;
    float strength = max(parameters.texelRadiusStrength.w, 0.0);
    float floorValue = clamp(parameters.execution.z, 0.0, 1.0);
    float multiplier = mix(1.0, floorValue, clamp(normalized * strength, 0.0, 1.0));
    outColor = vec4(vec3(multiplier), 1.0);
}
