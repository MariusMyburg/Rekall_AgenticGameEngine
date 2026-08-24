#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sceneDepth;
layout(set = 0, binding = 1) uniform FogFrameUniform
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
} frame;

layout(push_constant) uniform AnalyticFogParameters
{
    vec4 color;
    vec4 optical;
    vec4 cameraForwardFar;
    vec4 cameraRight;
    vec4 cameraUp;
    vec4 projection;
} parameters;

vec3 viewRay(vec2 uv)
{
    if (parameters.projection.z > 0.5) return normalize(parameters.cameraForwardFar.xyz);
    vec2 ndc = uv * 2.0 - 1.0;
    return normalize(
        parameters.cameraForwardFar.xyz
        + parameters.cameraRight.xyz * ndc.x * parameters.projection.x * parameters.projection.y
        + parameters.cameraUp.xyz * ndc.y * parameters.projection.x);
}

float linearViewDepth(float depth)
{
    float nearPlane = max(parameters.optical.w, 0.001);
    float farPlane = max(parameters.cameraForwardFar.w, nearPlane + 0.001);
    if (parameters.projection.z > 0.5) return mix(nearPlane, farPlane, depth);
    return nearPlane * farPlane / max(farPlane - depth * (farPlane - nearPlane), 0.000001);
}

void main()
{
    float depth = texture(sceneDepth, fragUv).r;
    float distanceToSurface = parameters.optical.z;
    float worldHeight = frame.cameraPosition.y;
    if (depth < 0.999999)
    {
        vec3 ray = viewRay(fragUv);
        distanceToSurface = linearViewDepth(depth)
            / max(dot(ray, normalize(parameters.cameraForwardFar.xyz)), 0.000001);
        vec3 worldPosition = frame.cameraPosition.xyz + ray * distanceToSurface;
        worldHeight = worldPosition.y;
    }

    float heightFactor = exp(-max(parameters.optical.y, 0.0) * max(worldHeight, 0.0));
    float opacity = 1.0 - exp(-max(parameters.optical.x, 0.0) * max(distanceToSurface, 0.0) * heightFactor);
    outColor = vec4(max(parameters.color.rgb, vec3(0.0)), clamp(opacity, 0.0, 0.996));
}
