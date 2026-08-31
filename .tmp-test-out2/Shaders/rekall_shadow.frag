#version 450

layout(location = 0) in vec2 fragUv;

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

void main()
{
    if (draw.shadowFactors.z > 0.5
        && texture(sampler2D(baseColorTexture, baseColorSampler), fragUv).a < draw.shadowFactors.w)
    {
        discard;
    }
}
