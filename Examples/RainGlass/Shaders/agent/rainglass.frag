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

const float WaterIor = 1.33;

void main()
{
    // The RainGlassSystem runtime module writes world-elapsed seconds (wrapped well under this
    // slot's [0, 64] clamp) into this entity's Rekall.Material.emissiveStrength every frame - the
    // same authored-per-frame-property pattern Ridgebreaker used to drive a wheel motor, here
    // driving a shader parameter instead of physics, since AGE has no dedicated engine-time uniform.
    float time = Draw.EmissiveFactors.w;

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

    // Model each droplet as a partial-sphere lens bulging out of the flat glass, rather than
    // faking the distortion with a fixed-strength UV nudge. heightProfile is the droplet's own
    // cross-sectional height (a paraboloid, 1 at its center and 0 at its rim); its derivative gives
    // a real local surface normal, tilting more steeply toward the droplet's edges exactly like an
    // actual bead of water does.
    float dropletRadius = max(streakWidth * 0.5, 0.001);
    float xInDroplet = withinColumnX / dropletRadius;
    float heightProfile = clamp(1.0 - xInDroplet * xInDroplet, 0.0, 1.0);
    float dHeightDx = -2.0 * xInDroplet / dropletRadius;
    vec3 dropletNormal = normalize(vec3(-dHeightDx, 0.0, 1.0));
    // Outside a droplet the surface is just flat glass (normal pointing straight at the camera);
    // blend toward the lens normal only where a droplet actually sits, by streak coverage.
    vec3 surfaceNormal = normalize(mix(vec3(0.0, 0.0, 1.0), dropletNormal, streakMask * heightProfile));

    vec3 viewDirection = normalize(Frame.CameraPosition.xyz - fragWorldPosition);
    // Real Snell's-law refraction (GLSL's built-in refract()) bending the view ray through the
    // droplet's surface, air-to-water eta = 1.0 / WaterIor - not a fabricated distortion strength.
    vec3 refractedDirection = refract(-viewDirection, surfaceNormal, 1.0 / WaterIor);
    // The scene behind this pane is only a flat texture (no real depth to ray-march through), so
    // the refracted direction's lateral component approximates a thin-film parallax offset into
    // that texture rather than a true traced ray - the same simplification every real-time "rain on
    // glass" shader makes without a full scene depth buffer to refract against.
    vec2 refractionOffset = refractedDirection.xy * streakMask * heightProfile * 0.08;
    vec4 refractedColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fragUv + refractionOffset);

    // Schlick's Fresnel approximation: real water reflects almost nothing head-on (~2%) and nearly
    // everything at grazing angles, producing the bright rim highlight along a droplet's edge
    // instead of a flat, uniform tint.
    float cosTheta = clamp(dot(viewDirection, surfaceNormal), 0.0, 1.0);
    const float fresnelAtNormalIncidence = 0.02;
    float fresnel = fresnelAtNormalIncidence
        + (1.0 - fresnelAtNormalIncidence) * pow(1.0 - cosTheta, 5.0);
    vec3 color = mix(refractedColor.rgb, vec3(0.95, 0.98, 1.0), fresnel * streakMask);

    float baseHaze = 0.06;
    float alpha = clamp(baseHaze + streakMask * 0.7, 0.0, 1.0);
    outColor = vec4(color, alpha);
}
