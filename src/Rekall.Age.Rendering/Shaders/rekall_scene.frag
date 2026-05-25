#version 450

layout(location = 0) in vec3 fragNormal;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in vec2 fragUv;
layout(location = 3) in vec3 fragWorldPosition;

layout(set = 0, binding = 0) uniform FrameUniform
{
    mat4 viewProjection;
    vec4 lightDirection;
    vec4 lightColor;
    vec4 lightPosition;
} frame;

layout(set = 0, binding = 1) uniform sampler2D baseColorTexture;
layout(set = 0, binding = 2) uniform sampler2D normalTexture;
layout(set = 0, binding = 3) uniform sampler2D metallicRoughnessTexture;
layout(set = 0, binding = 4) uniform sampler2D occlusionTexture;
layout(set = 0, binding = 5) uniform sampler2D emissiveTexture;

layout(push_constant) uniform DrawPushConstants
{
    mat4 model;
    vec4 materialFactors;
    vec4 emissiveFactors;
} draw;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;

vec3 perturbNormal(vec3 normal)
{
    vec3 tangentNormal = texture(normalTexture, fragUv).xyz * 2.0 - 1.0;
    tangentNormal.xy *= draw.materialFactors.z;
    vec3 q1 = dFdx(fragWorldPosition);
    vec3 q2 = dFdy(fragWorldPosition);
    vec2 st1 = dFdx(fragUv);
    vec2 st2 = dFdy(fragUv);
    vec3 tangent = normalize(q1 * st2.t - q2 * st1.t);
    vec3 bitangent = normalize(-q1 * st2.s + q2 * st1.s);
    mat3 tbn = mat3(tangent, bitangent, normal);
    return normalize(tbn * tangentNormal);
}

float distributionGgx(vec3 normal, vec3 halfVector, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float ndoth = max(dot(normal, halfVector), 0.0);
    float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
    return a2 / max(PI * denom * denom, 0.0001);
}

float geometrySchlickGgx(float ndotv, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return ndotv / max(ndotv * (1.0 - k) + k, 0.0001);
}

vec3 fresnelSchlick(float cosTheta, vec3 f0)
{
    return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

void main()
{
    vec4 textureColor = texture(baseColorTexture, fragUv);
    vec3 albedo = pow(max(fragColor.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
    float metallic = 0.0;
    float roughness = clamp(draw.materialFactors.y, 0.04, 1.0);
    if (draw.materialFactors.x > 0.0001)
    {
        vec4 metalRough = texture(metallicRoughnessTexture, fragUv);
        metallic = clamp(metalRough.b * draw.materialFactors.x, 0.0, 1.0);
        roughness = clamp(metalRough.g * draw.materialFactors.y, 0.04, 1.0);
    }
    float occlusion = 1.0;
    if (draw.materialFactors.w > 0.0001)
    {
        occlusion = mix(1.0, texture(occlusionTexture, fragUv).r, draw.materialFactors.w);
    }
    vec3 normal = normalize(fragNormal);
    vec3 light = frame.lightPosition.w > 0.5
        ? normalize(frame.lightPosition.xyz - fragWorldPosition)
        : normalize(-frame.lightDirection.xyz);
    if (draw.materialFactors.z > 0.0001)
    {
        normal = perturbNormal(normal);
    }
    vec3 view = normalize(vec3(0.0, 0.0, 1.0));
    vec3 halfVector = normalize(view + light);
    float ndotl = max(dot(normal, light), 0.0);
    float ndotv = max(dot(normal, view), 0.0);
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    float d = distributionGgx(normal, halfVector, roughness);
    float g = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(ndotl, roughness);
    vec3 f = fresnelSchlick(max(dot(halfVector, view), 0.0), f0);
    vec3 specular = d * g * f / max(4.0 * ndotv * ndotl, 0.0001);
    vec3 diffuse = (1.0 - f) * (1.0 - metallic) * albedo / PI;
    vec3 ambient = albedo * 0.035 * occlusion;
    vec3 emissive = pow(max(texture(emissiveTexture, fragUv).rgb * draw.emissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * draw.emissiveFactors.a;
    vec3 color = emissive + ambient + (diffuse + specular) * frame.lightColor.rgb * ndotl * 2.4;
    vec3 lit = pow(color, vec3(1.0 / 2.2));
    outColor = vec4(lit, fragColor.a * textureColor.a);
}
