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

float hash(float n)
{
    return fract(sin(n * 127.1) * 43758.5453);
}

float hash2(vec2 p)
{
    return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453);
}

const float WaterIor = 1.33;
// The glass pane's own authored world scale (Main.age.scene.json's "rainGlass" entity: scaleX 10,
// scaleZ 5.6), used to correct a droplet's UV-space radius into one that reads as round in world
// space rather than stretched to the plane's non-square aspect. This must MULTIPLY the vertical
// radius, not divide it - one UV unit of V covers less physical distance (5.6) than one UV unit of
// U covers (10), so matching a real round shape needs a *larger* UV-space radius vertically.
const float PlaneAspect = 10.0 / 5.6;

// A droplet's horizontal position as a pure function of height along the pane (not of time). Both
// the droplet's current body AND its trail evaluate this same curve, so the trail is literally the
// path already traced rather than a straight smear that doesn't match a wandering drop. RainGlassSystem
// (the C# runtime module) computes this exact same formula for real collision detection between the
// two simulated droplets below - both sides must stay identical for their rendered positions and
// their simulated collisions to agree.
float pathOffsetX(float y, float seed)
{
    return sin(y * 14.0 + seed * 23.0) * 0.55 + sin(y * 31.0 + seed * 7.0) * 0.18;
}

// One of the two genuinely simulated "big" droplets: centerY and radius are real per-frame state
// tracked and merged in C# (RainGlassSystem), not procedural noise - this function only renders
// whatever position/size the simulation already decided.
void evaluateSimulatedDroplet(
    vec2 uv,
    float centerY,
    float seed,
    float radius,
    out float heightProfile,
    out float wetMask,
    out vec2 delta,
    out vec2 radii)
{
    float bellyRadiusY = radius * PlaneAspect;
    float tailExtent = bellyRadiusY * 3.0;

    // The path's own curve evaluated at THIS pixel's row gives the droplet's x position at that
    // row - at the droplet's own current row (uv.y == centerY) this is exactly its live x
    // position; at rows above (its trailing tail), this is where the droplet was when it passed
    // through that row, so the trail naturally follows the same track the body is on.
    float dx = uv.x - pathOffsetX(uv.y, seed);
    float dy = uv.y - centerY;
    float taper = dy < 0.0 ? mix(1.0, 2.0, clamp(-dy / tailExtent, 0.0, 1.0)) : 1.0;
    radii = vec2(radius * taper, bellyRadiusY);
    delta = vec2(dx / radii.x, dy / radii.y);
    float dist2 = dot(delta, delta);
    heightProfile = clamp(1.0 - dist2, 0.0, 1.0);
    float bodyMask = smoothstep(0.0, 0.12, heightProfile);

    float trailFade = clamp(1.0 - (-dy) / (tailExtent * 2.0), 0.0, 1.0);
    float trailWidth = radius * 0.28;
    float trailShape = dy < 0.0 ? smoothstep(trailWidth, 0.0, abs(dx)) * trailFade * 0.22 : 0.0;
    wetMask = max(bodyMask, trailShape);
}

// A dense, static, jittered-grid layer of adhered droplets - the hundreds of small stationary beads
// that actually dominate a real rain-streaked window, as opposed to a handful of falling streaks.
// Each grid cell independently rolls whether it spawns a droplet at all (spawnChance), so coverage
// stays irregular/organic rather than a visible uniform lattice.
void evaluateStaticDroplet(
    vec2 uv,
    float cellsX,
    float cellsY,
    float spawnChance,
    float minRadius,
    float maxRadius,
    float layerSeed,
    out float heightProfile,
    out float wetMask,
    out vec2 delta,
    out vec2 radii)
{
    vec2 grid = uv * vec2(cellsX, cellsY);
    vec2 cellId = floor(grid);
    float cellHash = hash2(cellId + layerSeed);
    heightProfile = 0.0;
    wetMask = 0.0;
    delta = vec2(10.0);
    radii = vec2(1.0);
    if (cellHash > spawnChance)
    {
        return;
    }

    vec2 jitter = vec2(hash2(cellId + layerSeed + 1.0), hash2(cellId + layerSeed + 2.0));
    vec2 center = (cellId + 0.25 + jitter * 0.5) / vec2(cellsX, cellsY);
    float radiusUv = mix(minRadius, maxRadius, hash2(cellId + layerSeed + 3.0));
    float elongate = mix(1.0, 1.35, hash2(cellId + layerSeed + 4.0));
    radii = vec2(radiusUv, radiusUv * PlaneAspect * elongate);
    delta = (uv - center) / radii;
    float dist2 = dot(delta, delta);
    heightProfile = clamp(1.0 - dist2, 0.0, 1.0);
    wetMask = smoothstep(0.0, 0.18, heightProfile);
}

// A cheap 4-tap box blur, applied only where a droplet's own lens softens the image behind it -
// real water droplets show a visibly softened, magnified view of whatever is behind them, not a
// crisp offset copy of the background.
vec4 sampleBlurred(vec2 uv, vec2 radius)
{
    vec4 total = vec4(0.0);
    total += texture(sampler2D(BaseColorTexture, BaseColorSampler), uv + vec2(-1.0, -1.0) * radius);
    total += texture(sampler2D(BaseColorTexture, BaseColorSampler), uv + vec2(1.0, -1.0) * radius);
    total += texture(sampler2D(BaseColorTexture, BaseColorSampler), uv + vec2(-1.0, 1.0) * radius);
    total += texture(sampler2D(BaseColorTexture, BaseColorSampler), uv + vec2(1.0, 1.0) * radius);
    return total * 0.25;
}

// Must match RainGlassSystem's own Drop0Seed/Drop1Seed exactly (including sharing one seed between
// them - see that class's own comment on why: different seeds make their X positions essentially
// unrelated even when Y coincides, defeating collision detection almost always).
const float Drop0Seed = 91.7 * 0.31;
const float Drop1Seed = Drop0Seed;
// Drop1's own radius never grows (only the survivor of a merge grows; the absorbed slot always
// respawns at this same starting size), so unlike Drop0's it never needs transmitting - matches
// RainGlassSystem's RespawnRadius1 constant exactly.
const float Drop1BaseRadius = 0.02;

void main()
{
    // RainGlassSystem (the C# runtime module) runs a genuine two-body simulation - not shader-side
    // procedural noise - persisting each droplet's real Y position and, for the one that can grow,
    // its real radius across frames, detecting real overlap between them, and merging by
    // conserving 2D area when they touch. Only 3 raw numbers fit through this custom shader's fixed
    // Draw uniform slots (Rekall.Material's MetallicFactor/RoughnessFactor/EmissiveStrength,
    // repurposed here exactly like Ridgebreaker repurposed a motor property, not as PBR factors) -
    // drop0Y, drop0Radius (scaled x8 on the way in to clear the renderer's own [0.04, 1]
    // RoughnessFactor floor, divided back out here), and drop1Y.
    float drop0Y = Draw.MaterialFactors.x;
    float drop0Radius = Draw.MaterialFactors.y / 8.0;
    float drop1Y = Draw.EmissiveFactors.w;

    float bestHeight = 0.0;
    float combinedWetMask = 0.0;
    vec2 bestDelta = vec2(0.0);
    vec2 bestRadii = vec2(1.0);

    float height0, wet0;
    vec2 delta0, radii0;
    evaluateSimulatedDroplet(fragUv, drop0Y, Drop0Seed, drop0Radius, height0, wet0, delta0, radii0);
    combinedWetMask = max(combinedWetMask, wet0);
    if (height0 > bestHeight) { bestHeight = height0; bestDelta = delta0; bestRadii = radii0; }

    float height1, wet1;
    vec2 delta1, radii1;
    evaluateSimulatedDroplet(fragUv, drop1Y, Drop1Seed, Drop1BaseRadius, height1, wet1, delta1, radii1);
    combinedWetMask = max(combinedWetMask, wet1);
    if (height1 > bestHeight) { bestHeight = height1; bestDelta = delta1; bestRadii = radii1; }

    // Three static size classes - large, medium, and small - layered together for the dense,
    // multi-scale coverage a real rain-soaked window shows instead of a handful of uniform dots.
    float heightL, wetL;
    vec2 deltaL, radiiL;
    evaluateStaticDroplet(fragUv, 16.0, 10.0, 0.55, 0.014, 0.026, 11.0, heightL, wetL, deltaL, radiiL);
    combinedWetMask = max(combinedWetMask, wetL);
    if (heightL > bestHeight) { bestHeight = heightL; bestDelta = deltaL; bestRadii = radiiL; }

    float heightM, wetM;
    vec2 deltaM, radiiM;
    evaluateStaticDroplet(fragUv, 34.0, 20.0, 0.45, 0.006, 0.013, 37.0, heightM, wetM, deltaM, radiiM);
    combinedWetMask = max(combinedWetMask, wetM);
    if (heightM > bestHeight) { bestHeight = heightM; bestDelta = deltaM; bestRadii = radiiM; }

    float heightS, wetS;
    vec2 deltaS, radiiS;
    evaluateStaticDroplet(fragUv, 64.0, 40.0, 0.4, 0.0025, 0.006, 61.0, heightS, wetS, deltaS, radiiS);
    combinedWetMask = max(combinedWetMask, wetS);
    if (heightS > bestHeight) { bestHeight = heightS; bestDelta = deltaS; bestRadii = radiiS; }

    float heightProfile = bestHeight;
    float dropMask = smoothstep(0.0, 0.12, heightProfile);
    vec2 delta = bestDelta;
    vec2 radii = bestRadii;

    // A genuine local surface normal from the height profile's own derivative - the droplet
    // actually bulges out of the flat glass and bends light through its curve, rather than a
    // fixed-strength UV nudge faking the effect. The base/flat-glass normal points toward the
    // camera (negative Z here: the camera sits at a lower world Z than this pane).
    vec2 dHeight = -2.0 * delta / radii;
    vec3 dropletNormal = normalize(vec3(-dHeight, -1.0));
    vec3 surfaceNormal = normalize(mix(vec3(0.0, 0.0, -1.0), dropletNormal, dropMask * heightProfile));

    vec3 viewDirection = normalize(Frame.CameraPosition.xyz - fragWorldPosition);
    // Real Snell's-law refraction (GLSL's built-in refract()) bending the view ray through the
    // droplet's curved surface, air-to-water eta = 1.0 / WaterIor - not a fabricated distortion
    // strength, and genuinely two-dimensional. A much larger displacement than a subtle nudge: a
    // real droplet visibly magnifies/warps what is behind it, close to a small fisheye lens.
    vec3 refractedDirection = refract(-viewDirection, surfaceNormal, 1.0 / WaterIor);
    vec2 refractionOffset = refractedDirection.xy * dropMask * 0.16;
    // The scene behind this pane is only a flat texture (no real depth to ray-march through), so
    // the refracted direction's lateral component approximates a thin-film parallax/magnification
    // offset into that texture rather than a true traced ray - the same simplification every
    // real-time "rain on glass" shader makes without a full scene depth buffer to refract against.
    // A small blur radius (scaled by droplet presence) softens what's seen through each droplet,
    // instead of a crisp offset copy of the background - part of what actually reads as "glass".
    vec4 refractedColor = sampleBlurred(fragUv + refractionOffset, vec2(0.003) * dropMask);

    // Schlick's Fresnel approximation: real water reflects almost nothing head-on (~2%) and nearly
    // everything at grazing angles. cosTheta falls toward 0 approaching the droplet's rim (the
    // surface normal tips away from the view there), so fresnel naturally peaks at the rim on its
    // own - gated only by dropMask (the droplet's silhouette).
    float cosTheta = clamp(dot(viewDirection, surfaceNormal), 0.0, 1.0);
    const float fresnelAtNormalIncidence = 0.02;
    float fresnel = fresnelAtNormalIncidence
        + (1.0 - fresnelAtNormalIncidence) * pow(1.0 - cosTheta, 5.0);

    // A soft, broad highlight rather than a hard cartoon dot - real photographed droplets show a
    // gentle bright patch, not a sharp pinpoint glint.
    vec3 lightDirection = normalize(-Frame.LightDirection.xyz);
    vec3 halfVector = normalize(lightDirection + viewDirection);
    float specular = pow(max(dot(surfaceNormal, halfVector), 0.0), 40.0) * dropMask * 0.5;

    vec3 color = mix(refractedColor.rgb, vec3(0.9, 0.95, 1.0), fresnel * dropMask * 0.6);
    color += vec3(specular) * Frame.LightColor.rgb;

    float alpha = clamp(combinedWetMask * 0.9, 0.0, 1.0);
    outColor = vec4(color, alpha);
}
