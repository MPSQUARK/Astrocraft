#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUv;
layout(location = 2) in float inTextureIndex;

layout(location = 0) out vec2 fragUv;
layout(location = 1) out float fragTextureIndex;

layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 modelViewProjection;
} ubo;

void main() {
    gl_Position = ubo.modelViewProjection * vec4(inPosition, 1.0);
    fragUv = inUv;
    fragTextureIndex = inTextureIndex;
}
