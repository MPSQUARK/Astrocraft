#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in float fragTextureIndex;
layout(location = 2) in vec3 fragWorldPos;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 modelViewProjection;
    vec3 cameraPosition;
} ubo;

layout(set = 0, binding = 1) uniform sampler2DArray blockTextures;

void main() {
    vec4 tex = texture(blockTextures, vec3(fragUv, fragTextureIndex));
    float dist = distance(fragWorldPos, ubo.cameraPosition);
    float fog = clamp((dist - 12.0) / 80.0, 0.0, 0.9);
    vec3 fogColor = vec3(0.04, 0.08, 0.18);
    vec3 lit = tex.rgb * (0.55 + 0.45 * clamp(fragWorldPos.y / 48.0, 0.35, 1.0));
    vec3 color = mix(lit, fogColor, fog);
    outColor = vec4(color, 1.0);
}
