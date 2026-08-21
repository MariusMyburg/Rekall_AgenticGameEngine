#version 450
layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in vec2 fragUv;
layout(location = 3) in vec3 fragWorldPosition;
layout(set = 0, binding = 0) uniform FrameUniformBuffer
{
    mat4 ViewProjection;
    vec4 LightDirection;
    vec4 LightColor;
    vec4 LightPosition;
    vec4 CameraPosition;
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
} Draw;
layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
layout(location = 0) out vec4 outColor;
void main()
{
    vec4 base = texture(sampler2D(BaseColorTexture, BaseColorSampler), fragUv) * fragColor;
    vec3 lightDirection = Frame.LightPosition.w > 0.5
        ? normalize(Frame.LightPosition.xyz - fragWorldPosition)
        : normalize(-Frame.LightDirection.xyz);
    float diffuse = 0.25 + 0.75 * max(dot(normalize(fragNormal), lightDirection), 0.0);
    vec3 agentTint = vec3(0.78, 0.12, 1.0);
    float roughnessAccent = clamp(Draw.MaterialFactors.y, 0.0, 1.0) * 0.08;
    vec3 color = mix(base.rgb, agentTint, 0.82) * Frame.LightColor.rgb * (diffuse + roughnessAccent);
    outColor = vec4(color, base.a);
}