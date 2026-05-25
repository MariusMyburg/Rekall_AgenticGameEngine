#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;
layout(location = 3) in vec2 inUv;

layout(set = 0, binding = 0) uniform FrameUniform
{
    mat4 viewProjection;
    vec4 lightDirection;
    vec4 lightColor;
    vec4 lightPosition;
} frame;

layout(push_constant) uniform DrawPushConstants
{
    mat4 model;
    vec4 materialFactors;
} draw;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out vec2 fragUv;
layout(location = 3) out vec3 fragWorldPosition;

void main()
{
    vec4 worldPosition = draw.model * vec4(inPosition, 1.0);
    gl_Position = frame.viewProjection * worldPosition;
    fragNormal = mat3(draw.model) * inNormal;
    fragColor = inColor;
    fragUv = inUv;
    fragWorldPosition = worldPosition.xyz;
}
