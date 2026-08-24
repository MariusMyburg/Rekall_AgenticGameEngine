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
    vec2 outputSize;
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
    vec2 texel = 1.0 / vec2(textureSize(bloomPyramid, 0));
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

void main()
{
    vec3 hdr = texture(sceneHdr, fragUv).rgb;
    hdr += upsampleBloom(fragUv) * max(parameters.bloomIntensity, 0.0);
    hdr *= exp2(parameters.exposure);
    hdr /= max(parameters.whitePoint, 0.0001);
    vec3 graded = agxCurve(hdr);
    float luminance = dot(graded, vec3(0.2126, 0.7152, 0.0722));
    graded = mix(vec3(luminance), graded, max(parameters.saturation, 0.0));
    graded = (graded - 0.5) * max(parameters.contrast, 0.0) + 0.5;
    graded = mix(agxCurve(hdr), graded, clamp(parameters.gradeStrength, 0.0, 1.0));
    vec3 outputColor = pow(max(graded, vec3(0.0)), vec3(1.0 / 2.2));
    outColor = vec4(clamp(outputColor, 0.0, 0.996), 1.0);
}
