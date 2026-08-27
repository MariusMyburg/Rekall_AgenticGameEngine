#version 450
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;
layout(location = 3) in vec2 inUv;
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
layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out vec2 fragUv;
layout(location = 3) out vec3 fragWorldPosition;
void main()
{
    vec4 world = Draw.Model * vec4(inPosition, 1.0);
    gl_Position = Frame.ViewProjection * world;
    fragNormal = normalize(mat3(Draw.Model) * inNormal);
    fragColor = inColor;
    fragUv = inUv;
    fragWorldPosition = world.xyz;
}
