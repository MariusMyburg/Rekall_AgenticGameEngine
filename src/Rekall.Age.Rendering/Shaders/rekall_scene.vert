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
    vec4 cameraPosition;
    mat4 shadowViewProjection[4];
    vec4 shadowSplits;
    vec4 shadowParameters0;
    vec4 shadowParameters1;
    vec4 shadowCameraForward;
    vec4 additionalLightDirection;
    vec4 additionalLightColor;
    vec4 additionalLightPosition;
    vec4 additionalLightParameters;
    vec4 additionalLightColor2;
    vec4 additionalLightPosition2;
    vec4 additionalLightParameters2;
    vec4 additionalLightColor3;
    vec4 additionalLightPosition3;
    vec4 additionalLightParameters3;
    vec4 additionalLightColor4;
    vec4 additionalLightPosition4;
    vec4 additionalLightParameters4;
    vec4 additionalLightColor5;
    vec4 additionalLightPosition5;
    vec4 additionalLightParameters5;
    vec4 additionalLightColor6;
    vec4 additionalLightPosition6;
    vec4 additionalLightParameters6;
    vec4 additionalLightColor7;
    vec4 additionalLightPosition7;
    vec4 additionalLightParameters7;
    vec4 additionalLightColor8;
    vec4 additionalLightPosition8;
    vec4 additionalLightParameters8;
    vec4 additionalLightColor9;
    vec4 additionalLightPosition9;
    vec4 additionalLightParameters9;
    vec4 additionalLightColor10;
    vec4 additionalLightPosition10;
    vec4 additionalLightParameters10;
    vec4 additionalLightColor11;
    vec4 additionalLightPosition11;
    vec4 additionalLightParameters11;
    vec4 additionalLightColor12;
    vec4 additionalLightPosition12;
    vec4 additionalLightParameters12;
    vec4 additionalLightColor13;
    vec4 additionalLightPosition13;
    vec4 additionalLightParameters13;
    vec4 additionalLightColor14;
    vec4 additionalLightPosition14;
    vec4 additionalLightParameters14;
    vec4 additionalLightColor15;
    vec4 additionalLightPosition15;
    vec4 additionalLightParameters15;
    vec4 additionalLightColor16;
    vec4 additionalLightPosition16;
    vec4 additionalLightParameters16;
    vec4 environmentParameters;
    vec4 environmentAmbientSkyColor;
    vec4 environmentAmbientGroundColor;
} frame;

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
