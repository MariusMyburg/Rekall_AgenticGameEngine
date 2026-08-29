#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform EnvironmentBackgroundParameters
{
    vec4 cameraForward;
    vec4 cameraRight;
    vec4 cameraUp;
    vec4 projection;
} parameters;
layout(set = 0, binding = 1) uniform texture2D environmentTexture;
layout(set = 0, binding = 2) uniform sampler environmentSampler;

const float PI = 3.14159265358979323846;

vec2 directionToEnvironmentUv(vec3 direction)
{
    direction = normalize(direction);
    return vec2(
        atan(direction.z, direction.x) / (2.0 * PI) + 0.5,
        acos(clamp(direction.y, -1.0, 1.0)) / PI);
}

void main()
{
    vec2 ndc = fragUv * 2.0 - 1.0;
    vec3 forward = length(parameters.cameraForward.xyz) > 0.0001
        ? normalize(parameters.cameraForward.xyz)
        : vec3(0.0, 0.0, 1.0);
    vec2 environmentUv = directionToEnvironmentUv(forward);
    if (parameters.projection.z <= 0.5)
    {
        float halfVertical = atan(max(parameters.projection.x, 0.0001));
        float halfHorizontal = atan(max(parameters.projection.x * parameters.projection.y, 0.0001));
        environmentUv += vec2(
            ndc.x * halfHorizontal / (2.0 * PI),
            -ndc.y * halfVertical / PI);
    }
    environmentUv = vec2(fract(environmentUv.x), clamp(environmentUv.y, 0.0, 1.0));
    vec3 encoded = textureLod(
        sampler2D(environmentTexture, environmentSampler),
        environmentUv,
        0.0).rgb;
    vec3 radiance = pow(max(encoded, vec3(0.0)), vec3(2.2));
    // Alpha is the explicit geometry-coverage channel consumed by tone mapping.
    // A sky is background, not geometry, even though it is drawn fullscreen.
    outColor = vec4(radiance, 0.0);
}
