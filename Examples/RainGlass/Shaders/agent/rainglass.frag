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

// Cheap hash for per-streak-column randomness; no texture/noise asset required.
float hash(float n)
{
    return fract(sin(n * 127.1) * 43758.5453);
}

void main()
{
    // The RainGlassSystem runtime module writes world-elapsed seconds into this entity's
    // Rekall.Material.roughnessFactor every frame (Draw.MaterialFactors.y), the same authored-
    // per-frame-property pattern Ridgebreaker used to drive a wheel motor - here driving a shader
    // parameter instead of physics, since AGE has no dedicated engine-time uniform.
    float time = Draw.MaterialFactors.y;

    const float columnCount = 48.0;
    float column = floor(fragUv.x * columnCount);
    float columnSeed = hash(column);
    float speed = 0.35 + columnSeed * 0.9;
    float phase = fract(fragUv.y + time * speed + columnSeed * 3.7);

    // A short bright droplet band trailing into a fading tail, repeated per column via the phase
    // wrap above so each column's streak falls continuously and independently of its neighbors.
    float streak = smoothstep(0.0, 0.03, phase) * (1.0 - smoothstep(0.03, 0.22, phase));
    float withinColumnX = fract(fragUv.x * columnCount) - 0.5;
    float streakWidth = 0.25 + 0.35 * hash(column + 11.0);
    float streakMask = streak * smoothstep(streakWidth * 0.5, 0.0, abs(withinColumnX));

    // Sample the same background image the streak sits in front of, offset sideways by the
    // streak's own shape - a cheap refraction approximation: real rivulets on glass bend the light
    // passing through them instead of just tinting it.
    vec2 refractionOffset = vec2(withinColumnX, 0.0) * streakMask * 0.035;
    vec4 refracted = texture(sampler2D(BaseColorTexture, BaseColorSampler), fragUv + refractionOffset);

    // A faint static base haze (glass is never perfectly invisible) plus the moving droplet
    // highlights, both alpha-blended over whatever sits behind this surface.
    float baseHaze = 0.08;
    vec3 color = mix(refracted.rgb, vec3(0.85, 0.93, 1.0), streakMask * 0.55);
    float alpha = clamp(baseHaze + streakMask * 0.6, 0.0, 1.0);
    outColor = vec4(color, alpha);
}
