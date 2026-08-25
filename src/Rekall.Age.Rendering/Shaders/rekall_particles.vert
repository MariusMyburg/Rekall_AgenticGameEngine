#version 450

struct ParticleState
{
    vec4 positionAge;
    vec4 velocityLife;
    vec4 color;
    vec4 data;
};

struct ParticleEmitter
{
    uvec4 range;
    uvec4 seedFlags;
    vec4 originLife;
    vec4 directionCone;
    vec4 speedDrag;
    vec4 gravityEmission;
    vec4 sizeSoft;
    vec4 colorStart;
    vec4 colorEnd;
    vec4 flipbook;
};

layout(set = 0, binding = 0) uniform FrameUniform
{
    mat4 viewProjection;
} frame;
layout(set = 1, binding = 0, std430) readonly buffer Particles { ParticleState values[]; } particles;
layout(set = 1, binding = 1, std430) readonly buffer ActiveIndices { uint values[]; } activeIndices;
layout(set = 1, binding = 2, std430) readonly buffer Emitters { ParticleEmitter values[]; } emitters;

layout(push_constant) uniform ParticleDrawParameters
{
    vec4 cameraRight;
    vec4 cameraUp;
} parameters;

layout(location = 0) out vec2 fragUv;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out float fragDepth;
layout(location = 3) out float fragSoftFade;
layout(location = 4) out float fragEmissive;
layout(location = 5) flat out uint fragFlags;
layout(location = 6) out vec2 fragLocalUv;

const vec2 corners[6] = vec2[](
    vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(1.0, 1.0),
    vec2(-1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, 1.0));

void main()
{
    ParticleState particle = particles.values[activeIndices.values[gl_InstanceIndex]];
    ParticleEmitter emitter = emitters.values[floatBitsToUint(particle.data.w)];
    vec2 corner = corners[gl_VertexIndex];
    vec3 particlePosition = particle.positionAge.xyz + (emitter.seedFlags.y != 0u ? emitter.originLife.xyz : vec3(0.0));
    vec3 world = particlePosition
        + parameters.cameraRight.xyz * corner.x * particle.data.x
        + parameters.cameraUp.xyz * corner.y * particle.data.x;
    gl_Position = frame.viewProjection * vec4(world, 1.0);
    vec2 baseUv = corner * 0.5 + 0.5;
    uint columns = max(1u, uint(emitter.flipbook.x));
    uint rows = max(1u, uint(emitter.flipbook.y));
    uint frameCount = columns * rows;
    uint frame = emitter.flipbook.z > 0.0
        ? uint(floor(particle.positionAge.w * emitter.flipbook.z)) % frameCount
        : 0u;
    fragUv = (baseUv + vec2(frame % columns, frame / columns)) / vec2(columns, rows);
    fragLocalUv = baseUv;
    fragColor = particle.color;
    fragDepth = gl_Position.z / gl_Position.w;
    fragSoftFade = particle.data.y;
    fragEmissive = particle.data.z;
    fragFlags = (emitter.seedFlags.z != 0u ? 1u : 0u) | (emitter.seedFlags.w != 0u ? 2u : 0u);
}
