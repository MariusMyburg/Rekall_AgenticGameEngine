#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;
layout(location = 3) in vec2 inUv;

layout(set = 0, binding = 0) uniform ShadowCascadeUniform
{
    mat4 viewProjection;
    vec4 parameters;
} shadow;

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

layout(location = 0) out vec2 fragUv;

void main()
{
    vec4 worldPosition = draw.model * vec4(inPosition, 1.0);
    gl_Position = shadow.viewProjection * worldPosition;
    gl_Position.z += shadow.parameters.x * gl_Position.w;
    fragUv = inUv;
}
