#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUv;
layout(location = 2) in float inTextureIndex;
layout(location = 3) in vec4 inNormal;
layout(location = 4) in float inAo;

layout(location = 0) out vec2 fragUv;
layout(location = 1) out float fragTextureIndex;
layout(location = 2) out vec3 fragWorldPos;
layout(location = 3) out vec3 fragRayDir;
layout(location = 4) out vec3 fragNormal;
layout(location = 5) out float fragAo;

layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 modelViewProjection;
    mat4 inverseViewProjection;
    vec3 cameraPosition;
    vec4 survivalHud;
    vec2 viewportSize;
    float hudFlags;
    float breakProgress;
    vec3 targetBlockMin;
    float hasTarget;
    float timeOfDay;
    float breakBurstTimer;
    float breakingBlockTexture;
    vec3 ghostBlockMin;
    float ghostActive;
    float ghostValid;
    float ghostTexture;
    float heldItemTexture;
    float hasHeldItem;
    float time;
} ubo;

vec3 applyFoliageWind(vec3 worldPos, vec4 normalWind) {
    float weight = normalWind.w;
    if (weight <= 0.001) {
        return worldPos;
    }

    float phase = worldPos.x * 0.45 + worldPos.z * 0.38 + ubo.time * 1.7;
    float phase2 = worldPos.x * 0.22 - worldPos.z * 0.31 + ubo.time * 2.3;
    vec3 offset;
    offset.x = sin(phase) * 0.035 * weight;
    offset.z = cos(phase2) * 0.028 * weight;
    offset.y = sin(phase * 0.7 + phase2) * 0.012 * weight;
    return worldPos + offset;
}

void main() {
    if (inTextureIndex < -0.5) {
        vec2 ndc = inPosition.xy;
        gl_Position = vec4(ndc, 1.0, 1.0);

        vec4 clip = vec4(ndc, 1.0, 1.0);
        vec4 worldHom = ubo.inverseViewProjection * clip;
        vec3 worldPos = worldHom.xyz / worldHom.w;
        fragRayDir = normalize(worldPos - ubo.cameraPosition);
        fragTextureIndex = -1.0;
        fragUv = vec2(0.0);
        fragWorldPos = ubo.cameraPosition;
        fragNormal = vec3(0.0, 1.0, 0.0);
        fragAo = 1.0;
        return;
    }

    vec3 worldPos = applyFoliageWind(inPosition, inNormal);
    gl_Position = ubo.modelViewProjection * vec4(worldPos, 1.0);
    fragUv = inUv;
    fragTextureIndex = inTextureIndex;
    fragWorldPos = worldPos;
    fragRayDir = vec3(0.0);
    fragNormal = inNormal.xyz;
    float rawAo = inAo;
    fragAo = rawAo;
}
