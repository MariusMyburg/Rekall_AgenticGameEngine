#version 450

layout(set = 2, binding = 0) uniform sampler2D sceneDepth;
layout(set = 2, binding = 1) uniform sampler2D particleTexture;

layout(location = 0) in vec2 fragUv;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in float fragDepth;
layout(location = 3) in float fragSoftFade;
layout(location = 4) in float fragEmissive;
layout(location = 5) flat in uint fragFlags;
layout(location = 6) in vec2 fragLocalUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 textureColor = texture(particleTexture, fragUv);
    float edge = max(0.0, 1.0 - length(fragLocalUv * 2.0 - 1.0));
    float sampledDepth = texture(sceneDepth, gl_FragCoord.xy / vec2(textureSize(sceneDepth, 0))).r;
    float soft = fragSoftFade > 0.0 ? clamp((sampledDepth - fragDepth) / fragSoftFade, 0.0, 1.0) : 1.0;
    float alpha = fragColor.a * textureColor.a * edge * soft;
    if (alpha <= 0.001) discard;
    float lighting = (fragFlags & 1u) != 0u ? 0.72 : 1.0;
    vec3 hdr = max(fragColor.rgb * textureColor.rgb * max(fragEmissive, 1.0) * lighting, vec3(0.0));
    bool additive = (fragFlags & 2u) != 0u;
    outColor = vec4(hdr * alpha, additive ? 0.0 : alpha);
}
