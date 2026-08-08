#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in float fragTextureIndex;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform sampler2DArray blockTextures;

void main() {
    outColor = texture(blockTextures, vec3(fragUv, fragTextureIndex));
}
