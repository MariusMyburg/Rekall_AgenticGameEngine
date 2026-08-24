#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform AnalyticFogParameters
{
    vec4 color;
    vec4 optical;
} parameters;

void main()
{
    float distanceFactor = mix(0.25, max(parameters.optical.z, 0.25), clamp(fragUv.y, 0.0, 1.0));
    float heightFactor = exp(-max(parameters.optical.y, 0.0) * max(1.0 - fragUv.y, 0.0));
    float opacity = 1.0 - exp(-max(parameters.optical.x, 0.0) * distanceFactor * heightFactor);
    outColor = vec4(max(parameters.color.rgb, vec3(0.0)), clamp(opacity, 0.0, 0.996));
}
