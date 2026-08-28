#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sceneHdr;
layout(set = 0, binding = 1) uniform sampler2D bloomPyramid;

layout(push_constant) uniform ToneMapParameters
{
    float exposure;
    float whitePoint;
    float saturation;
    float contrast;
    float gradeStrength;
    float bloomIntensity;
    float bloomRadius;
    float lensDirtStrength;
    float lensDirtScale;
} parameters;

vec3 agxCurve(vec3 value)
{
    // AgX-style log-domain sigmoid fitted for a stable real-time shoulder.
    value = max(value, vec3(0.0));
    vec3 logValue = clamp((log2(max(value, vec3(1e-6))) + 10.0) / 16.5, 0.0, 1.0);
    vec3 sigmoid = logValue * logValue * (3.0 - 2.0 * logValue);
    return sigmoid * sigmoid * (3.0 - 2.0 * sigmoid);
}

vec3 upsampleBloom(vec2 uv)
{
    vec2 texel = max(parameters.bloomRadius, 0.05) / vec2(textureSize(bloomPyramid, 0));
    vec3 sum = texture(bloomPyramid, uv).rgb * 4.0;
    sum += texture(bloomPyramid, uv + vec2(texel.x, 0.0)).rgb * 2.0;
    sum += texture(bloomPyramid, uv - vec2(texel.x, 0.0)).rgb * 2.0;
    sum += texture(bloomPyramid, uv + vec2(0.0, texel.y)).rgb * 2.0;
    sum += texture(bloomPyramid, uv - vec2(0.0, texel.y)).rgb * 2.0;
    sum += texture(bloomPyramid, uv + texel).rgb;
    sum += texture(bloomPyramid, uv - texel).rgb;
    sum += texture(bloomPyramid, uv + vec2(texel.x, -texel.y)).rgb;
    sum += texture(bloomPyramid, uv + vec2(-texel.x, texel.y)).rgb;
    return sum * (1.0 / 16.0);
}

float dirtHash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float dirtNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(dirtHash(i), dirtHash(i + vec2(1.0, 0.0)), u.x),
               mix(dirtHash(i + vec2(0.0, 1.0)), dirtHash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Rotated-octave fbm. Rotating between octaves matters here: stacking axis
// aligned value noise leaves an obvious square grid, which reads as blocky
// artefacts rather than grime.
float dirtFbm(vec2 p, int octaves)
{
    const mat2 turn = mat2(0.8, -0.6, 0.6, 0.8);
    float total = 0.0;
    float amp = 0.5;
    float norm = 0.0;
    for (int i = 0; i < octaves; ++i)
    {
        total += dirtNoise(p) * amp;
        norm += amp;
        p = turn * p * 2.03 + 19.7;
        amp *= 0.5;
    }
    return total / max(norm, 0.0001);
}

// Procedural lens-dirt mask: broad smudges, sparse specks and faint radial
// streaks, weighted toward the frame edges the way grime settles on real glass.
// Generated rather than sampled so the effect needs no extra descriptor binding
// or authored texture, and stays resolution independent.
float lensDirtMask(vec2 uv)
{
    vec2 centred = uv - 0.5;
    // Work in an aspect-corrected space so the pattern is not stretched on wide
    // frames, and so "scale" means the same thing at any resolution.
    vec2 aspect = vec2(textureSize(sceneHdr, 0));
    vec2 p = vec2(centred.x * (aspect.x / max(aspect.y, 1.0)), centred.y);
    float scale = max(parameters.lensDirtScale, 0.05);

    float smudge = dirtFbm(p * 3.4 * scale, 5);
    smudge = smoothstep(0.46, 0.78, smudge);

    float speckField = dirtFbm(p * 26.0 * scale + 41.7, 3);
    float speck = smoothstep(0.70, 0.86, speckField);

    float ang = atan(p.y, p.x);
    float streak = dirtFbm(vec2(ang * 1.7, length(p) * 4.5) * scale + 7.1, 4);
    streak = smoothstep(0.52, 0.84, streak) * 0.5;

    float edgeBias = 0.30 + 0.70 * smoothstep(0.05, 0.70, length(centred));

    return clamp((smudge * 0.55 + speck * 0.35 + streak) * edgeBias, 0.0, 1.0);
}

// Wide, low-frequency sample of the bloom pyramid. Grime on a lens scatters light
// across the whole element, so the glare a dirty lens shows is driven by bright
// sources well away from the pixel being shaded - a tight bloom tap would only
// ever light up dirt sitting directly on top of a highlight.
vec3 veilingGlare(vec2 uv)
{
    vec3 sum = vec3(0.0);
    float weight = 0.0;
    for (int i = 0; i < 12; ++i)
    {
        float a = 6.2831853 * float(i) / 12.0;
        for (int r = 1; r <= 3; ++r)
        {
            float radius = 0.055 * float(r);
            vec2 offset = vec2(cos(a), sin(a)) * radius;
            float w = 1.0 / float(r);
            sum += texture(bloomPyramid, clamp(uv + offset, 0.001, 0.999)).rgb * w;
            weight += w;
        }
    }
    return sum / max(weight, 0.0001);
}

void main()
{
    vec3 hdr = texture(sceneHdr, fragUv).rgb;
    vec3 bloom = upsampleBloom(fragUv) * max(parameters.bloomIntensity, 0.0);
    // Dirt only scatters light that is already blooming, so it rides the bloom
    // term rather than being laid over the finished image: a clean lens
    // (strength 0) leaves the result identical to before. The local term picks
    // out grime sitting on a highlight, the veiling term spreads haze across the
    // element from bright sources anywhere in frame.
    if (parameters.lensDirtStrength > 0.0)
    {
        float mask = lensDirtMask(fragUv);
        bloom *= 1.0 + mask * parameters.lensDirtStrength * 0.6;
        bloom += veilingGlare(fragUv) * mask * parameters.lensDirtStrength
                 * max(parameters.bloomIntensity, 0.0) * 0.9;
    }
    hdr += bloom;
    hdr *= exp2(parameters.exposure);
    // 11.2 is the conventional neutral scene-white reference. Authored white
    // points adjust highlight placement without crushing all midtones.
    hdr *= 11.2 / max(parameters.whitePoint, 0.0001);
    vec3 graded = agxCurve(hdr);
    float luminance = dot(graded, vec3(0.2126, 0.7152, 0.0722));
    graded = mix(vec3(luminance), graded, max(parameters.saturation, 0.0));
    graded = (graded - 0.5) * max(parameters.contrast, 0.0) + 0.5;
    graded = mix(agxCurve(hdr), graded, clamp(parameters.gradeStrength, 0.0, 1.0));
    vec3 outputColor = pow(max(graded, vec3(0.0)), vec3(1.0 / 2.2));
    outColor = vec4(clamp(outputColor, 0.0, 0.996), 1.0);
}
