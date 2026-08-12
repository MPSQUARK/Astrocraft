#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in float fragTextureIndex;
layout(location = 2) in vec3 fragWorldPos;
layout(location = 3) in vec3 fragRayDir;
layout(location = 4) in vec3 fragNormal;
layout(location = 5) in float fragAo;

layout(location = 0) out vec4 outColor;

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

layout(set = 0, binding = 1) uniform sampler2DArray blockTextures;

layout(set = 0, binding = 2) readonly buffer InventoryBuffer {
    int slots[];
} inventory;

float hash31(vec3 p) {
    return fract(sin(dot(p, vec3(12.9898, 78.233, 45.164))) * 43758.5453);
}

vec3 sunDirection() {
    float angle = ubo.timeOfDay * 6.2831853;
    return normalize(vec3(cos(angle) * 0.42, sin(angle) * 0.58 + 0.12, 0.74));
}

vec3 moonDirection() {
    vec3 sun = sunDirection();
    return normalize(vec3(-sun.x * 0.85 + 0.08, max(-sun.y * 0.55 + 0.28, 0.05), -sun.z * 0.9 - 0.12));
}

float hash21(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

bool blockTextureUsesVariant(int texIdx) {
    return texIdx == 1 || texIdx == 2 || texIdx == 3 || texIdx == 4 || texIdx == 8
        || texIdx == 38 || texIdx == 39 || texIdx == 40 || texIdx == 53
        || texIdx == 13 || texIdx == 14 || texIdx == 16 || texIdx == 17 || texIdx == 19 || texIdx == 21
        || texIdx == 22 || texIdx == 23 || texIdx == 24 || texIdx == 25 || texIdx == 26 || texIdx == 27
        || texIdx == 55 || texIdx == 56 || texIdx == 57 || texIdx == 58 || texIdx == 59 || texIdx == 60 || texIdx == 61;
}

bool isAlphaCutoutTile(int texIdx) {
    return texIdx == 8 || texIdx == 38 || texIdx == 39 || texIdx == 40 || texIdx == 53
        || texIdx == 29 || texIdx == 30 || texIdx == 31 || texIdx == 62 || texIdx == 63 || texIdx == 64;
}

vec2 variantBlockUv(vec2 uv, vec3 worldPos, int texIdx) {
    if (!blockTextureUsesVariant(texIdx)) {
        return uv;
    }

    vec3 blockCell = floor(worldPos + vec3(0.0001));
    float h = hash31(blockCell + vec3(float(texIdx) * 0.37));
    int rot = int(floor(h * 4.0)) & 3;
    vec2 rotated = uv;
    if (rot == 1) {
        rotated = vec2(1.0 - uv.y, uv.x);
    } else if (rot == 2) {
        rotated = vec2(1.0 - uv.x, 1.0 - uv.y);
    } else if (rot == 3) {
        rotated = vec2(uv.y, 1.0 - uv.x);
    }

    bool terrainTile = texIdx == 1 || texIdx == 2 || texIdx == 3 || texIdx == 4 || texIdx == 8
        || texIdx == 26 || texIdx == 38 || texIdx == 39 || texIdx == 40
        || texIdx == 55 || texIdx == 56 || texIdx == 57 || texIdx == 58 || texIdx == 59 || texIdx == 60 || texIdx == 61;
    if (terrainTile) {
        return rotated;
    }

    if (hash31(blockCell + vec3(texIdx, 53.0, 0.0)) > 0.5) {
        rotated.x = 1.0 - rotated.x;
    }

    float scale = 0.86 + hash31(blockCell + vec3(texIdx, 41.0, 0.0)) * 0.28;
    rotated = (rotated - 0.5) * scale + 0.5;

    vec2 jitter = vec2(
        hash31(blockCell + vec3(texIdx, 17.0, 0.0)),
        hash31(blockCell + vec3(texIdx, 0.0, 31.0))) - 0.5;
    jitter *= 0.22;
    return fract(rotated + jitter);
}

vec3 blockTintColor(vec3 worldPos, int texIdx) {
    bool terrainTile = texIdx == 1 || texIdx == 2 || texIdx == 3 || texIdx == 4 || texIdx == 8
        || texIdx == 26 || texIdx == 38 || texIdx == 39 || texIdx == 40
        || texIdx == 47 || texIdx == 50 || texIdx == 51;
    if (terrainTile) {
        return vec3(1.0);
    }

    if (!blockTextureUsesVariant(texIdx)) {
        return vec3(1.0);
    }

    vec3 blockCell = floor(worldPos + vec3(0.0001));
    float h = hash31(blockCell * 1.7 + vec3(texIdx));
    float h2 = hash31(blockCell * 2.3 + vec3(texIdx + 11));
    float h3 = hash31(blockCell * 3.1 + vec3(texIdx + 23));
    vec3 tint = vec3(
        0.98 + h * 0.04,
        0.98 + h2 * 0.04,
        0.98 + h3 * 0.04);
    return tint;
}

float noise2D(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float hash13(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

float noise3D(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash13(i);
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
    float nx00 = mix(n000, n100, f.x);
    float nx10 = mix(n010, n110, f.x);
    float nx01 = mix(n001, n101, f.x);
    float nx11 = mix(n011, n111, f.x);
    float nxy0 = mix(nx00, nx10, f.y);
    float nxy1 = mix(nx01, nx11, f.y);
    return mix(nxy0, nxy1, f.z);
}

float terrainOccupancy(vec3 worldPos) {
    vec3 p = worldPos * 0.11;
    float base = noise3D(p);
    float detail = noise3D(p * 2.7 + vec3(4.1, 1.7, 9.3)) * 0.45;
    float heightBias = smoothstep(42.0, 88.0, worldPos.y) * 0.55;
    float density = base * 0.72 + detail - heightBias;
    return smoothstep(0.38, 0.62, density);
}

// Approximate overworld column height for sun-shadow ray tests (world-space, pitch-independent).
float fractalNoise2D(vec2 p, float seed) {
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    for (int octave = 0; octave < 4; octave++) {
        value += noise2D(p * frequency + vec2(seed * 0.013, seed * 0.027)) * amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

float approxSurfaceHeight(vec2 xz) {
    float continental = fractalNoise2D(xz * 0.004, 0.0);
    float hills = fractalNoise2D(xz * 0.018, 1000.0);
    float detail = fractalNoise2D(xz * 0.06, 2000.0);
    float combined = continental * 0.5 + hills * 0.38 + detail * 0.12;
    return clamp(28.0 + combined * 20.0 - 5.0, 8.0, 60.0);
}

// Approximates how deep a fragment sits below the (noise-estimated) ground surface and uses that
// to darken ambient/sky lighting underground. There is no real per-block skylight propagation in
// this renderer (sunShadow only tests direct sun visibility), so enclosed spaces like caves and the
// critic showcase chamber previously received full bright ambient skylight even when fully sealed
// under a stone roof, reading as a lit corridor instead of a dark cave lit by lava/glowstone.
float caveSkyExposure(vec3 worldPos) {
    float surfaceY = approxSurfaceHeight(worldPos.xz);
    float depthBelow = surfaceY - worldPos.y;
    return clamp(1.0 - depthBelow / 9.0, 0.12, 1.0);
}

bool voxelBlocksSun(vec3 cell, vec3 sourceCell) {
  ivec3 c = ivec3(floor(cell + 0.001));
  ivec3 source = ivec3(floor(sourceCell + 0.001));
  if (c == source) {
    return false;
  }

  float surfaceY = approxSurfaceHeight(vec2(float(c.x), float(c.z)));
  if (float(c.y) <= surfaceY + 0.5) {
    return true;
  }

  // Sparse vertical columns for canopy-like occlusion above the height field.
  float treeHash = hash21(vec2(float(c.x), float(c.z)) * 1.37 + vec2(17.0, 41.0));
  if (treeHash > 0.90 && float(c.y) <= surfaceY + 5.5) {
    return true;
  }

  return false;
}

vec2 cloudWind() {
    float anim = ubo.timeOfDay * 6.2831853;
    return vec2(cos(anim * 0.72), sin(anim * 0.58)) * 18.0
        + vec2(sin(anim * 0.19), cos(anim * 0.24)) * 6.5;
}

float cloudDensityField(vec3 worldPos, float layerHeight, float layerHalfWidth, float coverage, vec2 wind) {
    float vertical = exp(-pow((worldPos.y - layerHeight) / layerHalfWidth, 2.0) * 1.35);
    if (vertical < 0.01) {
        return 0.0;
    }

    vec3 samplePos = worldPos * vec3(0.0048, 0.0012, 0.0048) + vec3(wind.x, 0.0, wind.y);
    float warp = noise3D(samplePos * 0.48 + vec3(2.1, 5.7, 1.3));
    samplePos += vec3(warp * 2.4, warp * 0.22, noise3D(samplePos.zxy * 0.58) * 2.2);

    float density = noise3D(samplePos);
    density += noise3D(samplePos * 2.35 + vec3(4.2, 1.8, 9.1)) * 0.52;
    density += noise3D(samplePos * 5.4 + vec3(11.3, 6.4, 2.7)) * 0.26;
    density += noise3D(samplePos * 9.8 + vec3(3.7, 14.2, 6.1)) * 0.13;
    // Normalize by total octave weight (1.91) so the mean sits near 0.5 and the coverage
    // threshold below actually carves out scattered clouds instead of a near-solid layer.
    density /= 1.91;
    density = smoothstep(coverage - 0.16, coverage + 0.18, density);

    float erosion = noise3D(samplePos * 7.2 + vec3(19.2, 3.1, 7.4));
    float wisps = noise3D(samplePos * 12.5 + vec3(8.3, 21.7, 4.6));
    density *= mix(0.42, 1.0, smoothstep(0.10, 0.62, erosion));
    density *= mix(0.50, 1.0, smoothstep(0.14, 0.66, wisps));
    return density * vertical;
}

float sampleProceduralSkyClouds(vec3 rayDir, vec3 sunDir, float dayFactor) {
    if (rayDir.y < 0.01 || dayFactor < 0.08) {
        return 0.0;
    }

    vec2 wind = cloudWind() * 0.0034;
    vec2 cloudUv = rayDir.xz / (rayDir.y + 0.14) * 0.062 + wind;
    vec2 stretchedUv = vec2(cloudUv.x * 2.35, cloudUv.y * 0.60);

    float coarse = noise2D(stretchedUv * 28.0);
    float detail = noise2D(stretchedUv * 64.0 + vec2(4.1, 9.6));
    float billow = noise2D(stretchedUv * 42.0 + vec2(11.2, 6.8));
    float strata = noise2D(vec2(stretchedUv.x * 46.0, stretchedUv.y * 10.0 + wind.x * 0.4));
    float fine = noise2D(stretchedUv * 132.0 + vec2(17.3, 2.8));
    float wisp = noise2D(stretchedUv * 210.0 + vec2(31.7, 8.4));
    float micro = noise2D(stretchedUv * 288.0 + vec2(44.1, 12.6));
    float shape = coarse * 0.24 + detail * 0.18 + billow * 0.12 + strata * 0.26 + fine * 0.12 + wisp * 0.06 + micro * 0.02;

    // Scattered strata with readable blue zenith between puffs.
    float coverage = mix(0.42, 0.54, dayFactor);
    float density = smoothstep(coverage - 0.12, coverage + 0.14, shape);
    density *= smoothstep(0.01, 0.08, rayDir.y);

    float upFactor = smoothstep(0.12, 0.68, rayDir.y);
    density *= mix(1.18, 0.52, upFactor);

    float sunFacing = pow(max(dot(rayDir, sunDir), 0.0), 1.2);
    density *= mix(0.85, 1.22, sunFacing * dayFactor);

    float erosion = noise2D(cloudUv * 96.0 + vec2(22.4, 5.8));
    float puff = noise2D(cloudUv * 74.0 + vec2(6.8, 18.2));
    float filament = noise2D(cloudUv * 168.0 + vec2(27.5, 14.3));
    density *= mix(0.48, 1.0, smoothstep(0.08, 0.58, erosion));
    density *= mix(0.56, 1.0, smoothstep(0.12, 0.64, puff));
    density *= mix(0.72, 1.0, smoothstep(0.18, 0.72, filament));
    return clamp(density, 0.0, 1.0);
}

float sampleClouds(vec3 rayDir, vec3 sunDir, float dayFactor) {
    if (rayDir.y <= 0.001) {
        return 0.0;
    }

    float skyClouds = sampleProceduralSkyClouds(rayDir, sunDir, dayFactor);
    if (rayDir.y < 0.18 || dayFactor < 0.10) {
        float lowUpBoost = smoothstep(0.12, 0.88, rayDir.y);
        float cloudDensity = clamp(skyClouds * mix(1.0, 1.34, lowUpBoost * dayFactor), 0.0, 1.0);
        float sunFacing = pow(max(dot(rayDir, sunDir), 0.0), 2.2);
        cloudDensity *= mix(1.0, 1.32, sunFacing * dayFactor);
        return cloudDensity;
    }

    vec3 rayOrigin = ubo.cameraPosition;
    vec2 wind = cloudWind();
    float coverage = mix(0.36, 0.46, dayFactor);
    float transmittance = 1.0;
    float accumulated = 0.0;

    int layerCount = rayDir.y > 0.42 ? 4 : 2;
    int marchSteps = rayDir.y > 0.55 ? 10 : 6;

    const float layerHeights[4] = float[](52.0, 78.0, 104.0, 138.0);
    const float layerWidths[4] = float[](18.0, 22.0, 26.0, 32.0);
    const float layerWeights[4] = float[](0.78, 0.66, 0.54, 0.40);

    for (int layer = 0; layer < 4; layer++) {
        if (layer >= layerCount) {
            break;
        }

        float layerHeight = layerHeights[layer];
        float layerHalfWidth = layerWidths[layer];
        float layerCoverage = coverage - float(layer) * 0.028;

        float layerBottom = layerHeight - layerHalfWidth;
        float layerTop = layerHeight + layerHalfWidth;
        float tBottom = (layerBottom - rayOrigin.y) / rayDir.y;
        float tTop = (layerTop - rayOrigin.y) / rayDir.y;
        float tMin = max(0.0, min(tBottom, tTop));
        float tMax = max(tBottom, tTop);

        bool insideLayer = rayOrigin.y >= layerBottom && rayOrigin.y <= layerTop;
        if (tMax <= tMin) {
            if (!insideLayer) {
                continue;
            }
            tMin = 0.0;
            tMax = rayDir.y > 0.0
                ? (layerTop - rayOrigin.y) / rayDir.y
                : (layerBottom - rayOrigin.y) / rayDir.y;
            if (tMax <= tMin) {
                continue;
            }
        }

        const int MAX_STEPS = 10;
        float stepLen = (tMax - tMin) / float(marchSteps);
        for (int step = 0; step < MAX_STEPS; step++) {
            if (step >= marchSteps || transmittance < 0.04) {
                break;
            }

            float t = tMin + (float(step) + 0.5) * stepLen;
            vec3 samplePos = rayOrigin + rayDir * t;
            float density = cloudDensityField(samplePos, layerHeight, layerHalfWidth, layerCoverage, wind);
            density *= layerWeights[layer];

            float absorption = 1.0 - exp(-density * stepLen * 0.86);
            accumulated += transmittance * absorption;
            transmittance *= 1.0 - absorption * 0.76;
        }
    }

    float cloudDensity = clamp(accumulated, 0.0, 1.0);
    cloudDensity *= smoothstep(0.0, 0.02, rayDir.y);

    cloudDensity = clamp(1.0 - (1.0 - cloudDensity) * (1.0 - skyClouds * 0.90), 0.0, 1.0);

    // Fade clouds out toward the zenith so straight-up views show clear blue sky, not a solid ceiling.
    float zenithFade = smoothstep(0.50, 0.94, rayDir.y);
    cloudDensity = clamp(cloudDensity * mix(1.0, 0.32, zenithFade), 0.0, 1.0);

    float sunFacing = pow(max(dot(rayDir, sunDir), 0.0), 2.2);
    cloudDensity *= mix(1.0, 1.32, sunFacing * dayFactor);
    return cloudDensity;
}

vec3 horizonHazeColor(vec3 rayDir, float dayFactor) {
    float elevation = clamp(rayDir.y, -1.0, 1.0);
    float horizonBand = pow(1.0 - abs(elevation), 2.4);
    vec3 hazeNight = vec3(0.14, 0.32, 0.68);
    vec3 hazeDay = vec3(0.16, 0.42, 0.86);
    vec3 haze = mix(hazeNight, hazeDay, dayFactor);
    vec3 horizonLift = mix(vec3(0.16, 0.40, 0.82), vec3(0.30, 0.56, 0.96), dayFactor);
    vec3 warmGlow = mix(vec3(0.14, 0.34, 0.68), vec3(0.36, 0.54, 0.86), dayFactor);
    vec3 tinted = mix(haze, horizonLift, horizonBand * mix(0.55, 0.70, dayFactor));
    tinted.b = max(tinted.b, tinted.g * 1.06);
    tinted.r = min(tinted.r, tinted.b * 0.80);
    return mix(tinted, warmGlow, horizonBand * horizonBand * mix(0.01, 0.02, dayFactor));
}

vec3 terrainFogColor(float dayFactor) {
    return mix(vec3(0.42, 0.62, 0.92), vec3(0.56, 0.76, 0.98), dayFactor);
}

float terrainFogAmount(float dist, float dayFactor) {
    if (dist < 24.0) {
        return 0.0;
    }
    float fogDist = dist - 24.0;
    float distFog = 1.0 - exp(-fogDist * mix(0.0065, 0.0050, dayFactor));
    float edgeFog = smoothstep(120.0, 280.0, dist) * mix(0.38, 0.30, dayFactor);
    return clamp(distFog * 0.28 + edgeFog, 0.0, 0.72);
}

float volumetricGodRays(vec3 viewDir, vec3 sunDir, float dayFactor) {
    if (dayFactor < 0.12) {
        return 0.0;
    }

    float cosTheta = max(dot(viewDir, sunDir), 0.0);
    float sunAngle = acos(clamp(cosTheta, 0.0, 1.0));

    float g = 0.90;
    float hgDenom = 1.0 + g * g - 2.0 * g * cosTheta;
    float hg = (1.0 - g * g) / (pow(hgDenom, 1.5) + 0.0001);
    float forwardCore = pow(cosTheta, 9.0) * hg * 0.82;
    float broadGlow = pow(cosTheta, 2.6) * 0.16;
    float mistyHalo = pow(cosTheta, 1.5) * smoothstep(0.0, 0.62, viewDir.y) * 0.12;

    vec3 sunOrtho = abs(sunDir.y) < 0.92
        ? normalize(cross(sunDir, vec3(0.0, 1.0, 0.0)))
        : normalize(cross(sunDir, vec3(1.0, 0.0, 0.0)));
    vec3 sunUp = cross(sunOrtho, sunDir);
    float radialAngle = atan(dot(viewDir, sunUp), dot(viewDir, sunOrtho));
    float streakPhase = radialAngle * 7.4 + ubo.timeOfDay * 3.6;
    float streakA = pow(0.5 + 0.5 * sin(streakPhase * 4.2), 2.6);
    float streakB = pow(0.5 + 0.5 * sin(streakPhase * 10.6 + 1.4), 2.0);
    float streakC = pow(0.5 + 0.5 * sin(streakPhase * 6.1 + 2.8), 2.2);
    float streakNoise = noise3D(vec3(radialAngle * 4.1, sunAngle * 16.0, ubo.timeOfDay * 2.1));
    float streakMask = smoothstep(0.06, 0.50, streakNoise);
    float streaks = mix(streakA, streakB, 0.42) * 0.78 + streakC * 0.38;
    streaks *= streakMask;

    float horizonBoost = mix(1.0, 1.38, pow(1.0 - abs(viewDir.y), 2.0));
    float sunFalloff = smoothstep(0.01, 0.88, cosTheta);
    float sunAlign = pow(cosTheta, 0.88);
    float radialFalloff = exp(-sunAngle * sunAngle * 38.0);
    float rays = (forwardCore + broadGlow + mistyHalo + streaks * sunFalloff * 0.78) * sunAlign * horizonBoost;
    rays *= mix(0.82, 1.0, radialFalloff);
    return rays * mix(0.58, 0.90, dayFactor);
}

vec3 toneMapSky(vec3 color) {
    color.b = max(color.b, max(color.r, color.g) * 0.98);
    color.g = max(color.g, color.b * 0.90);
    color.r = min(color.r, color.b * 0.92);
    float peakLum = max(color.r, max(color.g, color.b));
    vec3 compressed = color / (color + vec3(0.58));
    color = mix(color * 0.94, compressed, smoothstep(0.72, 1.45, peakLum));
    color.b = mix(color.b, max(color.b, color.g * 1.08), 0.82);
    color.r = min(color.r, color.b * 0.94);
    return min(color, vec3(0.82, 0.88, 0.98));
}

vec3 sampleStarField(vec3 rayDir, float dayFactor) {
    float nightVisibility = 1.0 - smoothstep(0.02, 0.22, dayFactor);
    nightVisibility *= smoothstep(-0.04, 0.12, rayDir.y);
    nightVisibility = nightVisibility * nightVisibility;
    if (nightVisibility < 0.001) {
        return vec3(0.0);
    }

    vec3 dir = normalize(rayDir);
    vec3 accum = vec3(0.0);
    const float STAR_SCALE = 124.0;

    for (int z = -1; z <= 1; z++) {
        for (int y = -1; y <= 1; y++) {
            for (int x = -1; x <= 1; x++) {
                vec3 cell = floor(dir * STAR_SCALE) + vec3(float(x), float(y), float(z));
                float starHash = hash13(cell + vec3(17.0, 41.0, 93.0));
                if (starHash < 0.9928) {
                    continue;
                }

                vec3 starPos = vec3(
                    hash13(cell + vec3(11.0, 0.0, 0.0)),
                    hash13(cell + vec3(0.0, 23.0, 0.0)),
                    hash13(cell + vec3(0.0, 0.0, 37.0)));
                vec3 local = fract(dir * STAR_SCALE) - starPos - vec3(float(x), float(y), float(z));
                float dist2 = dot(local, local);
                float starSize = 0.0016 + hash13(cell + vec3(53.0, 0.0, 0.0)) * 0.0028;
                float star = exp(-dist2 / (starSize * starSize));
                float starGlow = exp(-dist2 / (starSize * starSize * 5.5)) * 0.28;

                vec3 starTint = mix(
                    vec3(0.82, 0.90, 1.0),
                    vec3(1.0, 0.96, 0.88),
                    hash13(cell + vec3(61.0, 71.0, 0.0)));
                float brightness = 0.48 + (starHash - 0.9928) * 88.0;
                accum += starTint * (star + starGlow) * brightness;
            }
        }
    }

    float twinkle = 0.76 + 0.24 * sin(ubo.timeOfDay * 36.0 + dot(dir, vec3(3.7, 7.1, 11.3)) * 8.5);
    return accum * nightVisibility * twinkle * 0.72;
}

vec3 sampleAurora(vec3 rayDir, float nightFactor) {
    if (nightFactor < 0.62 || rayDir.y < 0.06) {
        return vec3(0.0);
    }

    float band = smoothstep(0.08, 0.42, rayDir.y) * (1.0 - smoothstep(0.68, 0.94, rayDir.y));
    vec3 auroraPos = vec3(rayDir.x * 5.5, ubo.timeOfDay * 0.35 + rayDir.y * 3.2, rayDir.z * 5.5);
    float curtain = noise3D(auroraPos) * 0.55 + noise3D(auroraPos * 2.1 + vec3(4.2, 1.8, 6.4)) * 0.45;
    float wave = 0.5 + 0.5 * sin(rayDir.x * 14.0 + rayDir.z * 9.0 + ubo.timeOfDay * 5.5);
    float strength = band * smoothstep(0.38, 0.72, curtain) * mix(0.55, 1.0, wave);
    vec3 green = vec3(0.18, 0.92, 0.52);
    vec3 teal = vec3(0.22, 0.78, 0.88);
    vec3 violet = vec3(0.58, 0.28, 0.94);
    vec3 auroraColor = mix(mix(violet, teal, wave), green, curtain);
    return auroraColor * strength * nightFactor * 0.16;
}

vec3 atmosphericScattering(vec3 viewDir, vec3 sunDir, float dayFactor) {
    float cosTheta = dot(viewDir, sunDir);
    float zenith = max(viewDir.y, 0.0);

    // Approximate optical depth through atmosphere along the view ray.
    float rayleighDepth = exp(-2.8 * (1.0 - zenith));
    float mieDepth = exp(-1.1 * (1.0 - zenith));

    // Rayleigh phase (isotropic + forward).
    float rayleighPhase = 0.75 * (1.0 + cosTheta * cosTheta);

    // Mie phase (Henyey-Greenstein, g = 0.76).
    float g = 0.76;
    float mieDenom = 1.0 + g * g - 2.0 * g * cosTheta;
    float miePhase = (1.0 - g * g) / (mieDenom * sqrt(mieDenom) + 0.0001);
    miePhase = clamp(miePhase, 0.0, 12.0) * 0.08;

    vec3 betaRayleigh = vec3(0.14, 0.42, 1.24);
    vec3 betaMie = vec3(0.48, 0.66, 0.98);

    vec3 extinction = exp(-(betaRayleigh * rayleighDepth + betaMie * mieDepth));
    vec3 inscatter = (betaRayleigh * rayleighPhase + betaMie * miePhase) * (vec3(1.0) - extinction);

    vec3 sunTint = mix(vec3(0.34, 0.56, 0.94), vec3(1.12, 1.02, 0.82), dayFactor);
    return inscatter * sunTint * mix(0.30, 0.76, dayFactor);
}

vec3 sampleSky(vec3 rayDir) {
    vec3 SUN_DIR = sunDirection();
    vec3 MOON_DIR = moonDirection();
    float dayFactor = clamp(SUN_DIR.y * 1.6 + 0.35, 0.0, 1.0);
    float nightFactor = 1.0 - dayFactor;

    float elevation = clamp(rayDir.y, -1.0, 1.0);
    float horizonBand = pow(1.0 - abs(elevation), 2.2);

    vec3 zenith = mix(vec3(0.06, 0.34, 0.78), vec3(0.14, 0.52, 1.02), dayFactor);
    vec3 midSky = mix(vec3(0.10, 0.48, 0.88), vec3(0.22, 0.64, 1.04), dayFactor);
    vec3 horizon = horizonHazeColor(rayDir, dayFactor);
    horizon = mix(horizon, mix(vec3(0.28, 0.52, 0.88), vec3(0.48, 0.70, 0.96), dayFactor), 0.62);
    vec3 horizonGlow = mix(vec3(0.38, 0.58, 0.86), vec3(0.64, 0.80, 0.96), dayFactor) * horizonBand;

    vec3 color = mix(horizon, midSky, smoothstep(-0.04, 0.38, elevation));
    color = mix(color, zenith, smoothstep(0.08, 0.82, elevation));
    color += horizonGlow * 0.05;
    color += atmosphericScattering(rayDir, SUN_DIR, dayFactor) * 0.26;

    float sunDot = dot(rayDir, SUN_DIR);
    float sunAngle = acos(clamp(sunDot, -1.0, 1.0));
    float sunSkyFill = exp(-sunAngle * sunAngle * 420.0) * dayFactor;
    color += vec3(0.88, 0.84, 0.68) * sunSkyFill * 0.05;
    color = max(color, vec3(0.08, 0.18, 0.36) * sunSkyFill * 0.38);

    float moonDot = dot(rayDir, MOON_DIR);
    float moonAngle = acos(clamp(moonDot, -1.0, 1.0));
    float moonRadius = 0.018;
    float moonDisc = smoothstep(moonRadius + 0.012, moonRadius - 0.002, moonAngle);
    float moonRim = smoothstep(moonRadius + 0.028, moonRadius + 0.004, moonAngle)
        * (1.0 - moonDisc);
    float moonGlow = pow(max(moonDot, 0.0), 96.0) * 0.62;
    vec3 moonCore = vec3(0.88, 0.92, 1.0);
    vec3 moonRimColor = vec3(0.62, 0.72, 0.92);
    color += moonCore * moonDisc * nightFactor * 0.72;
    color += moonRimColor * moonRim * nightFactor * 0.38;
    color += vec3(0.38, 0.48, 0.76) * moonGlow * nightFactor;

    // Mask cloud darken/mix near sun so sky base stays bright behind the disc.
    float sunExclusionMask = smoothstep(0.028, 0.095, sunAngle);

    float clouds = sampleClouds(rayDir, SUN_DIR, dayFactor);
    clouds *= sunExclusionMask;
    float sunCloud = pow(max(sunDot, 0.0), 2.0);

    vec2 cloudColorUv = rayDir.xz / (rayDir.y + 0.18) * 0.072 + cloudWind() * 0.0022;
    float cloudHueA = noise2D(cloudColorUv * 22.0);
    float cloudHueB = noise2D(cloudColorUv * 52.0 + vec2(8.4, 3.1));
    float cloudHueVar = cloudHueA * 0.58 + cloudHueB * 0.42;

    vec3 cloudShadow = mix(vec3(0.18, 0.34, 0.58), vec3(0.30, 0.48, 0.72), dayFactor);
    cloudShadow = mix(cloudShadow, vec3(0.22, 0.40, 0.62), cloudHueVar * 0.32);
    vec3 cloudMid = mix(vec3(0.52, 0.68, 0.86), vec3(0.70, 0.82, 0.94), dayFactor);
    cloudMid = mix(cloudMid, vec3(0.62, 0.74, 0.90), cloudHueVar * 0.26);
    vec3 cloudLit = mix(cloudShadow, cloudMid, clouds * 0.96 + cloudHueVar * 0.18);
    cloudLit = mix(cloudLit, vec3(0.92, 0.88, 0.74), sunCloud * dayFactor * 0.48);
    vec3 cloudBright = mix(cloudLit, vec3(0.98, 0.94, 0.80), pow(sunCloud, 4.2) * dayFactor * 0.34);
    cloudBright = mix(cloudBright, vec3(1.02, 0.96, 0.82), pow(sunCloud, 7.5) * dayFactor * 0.16);
    float cloudLum = dot(cloudBright, vec3(0.299, 0.587, 0.114));
    cloudBright = mix(vec3(cloudLum), cloudBright, mix(1.42, 1.58, dayFactor));
    float cloudShadowDarken = 1.0 - clouds * 0.22 * dayFactor * sunExclusionMask;
    color *= cloudShadowDarken;
    color = mix(color, cloudBright, clouds * mix(0.96, 1.0, dayFactor));
    color += cloudBright * clouds * clouds * 0.04 * dayFactor;
    color += cloudBright * clouds * 0.03 * dayFactor * horizonBand;

    // Sun composited after clouds so volumetric cloud mix/darken cannot replace the bright disc.
    // Tight, contained disc (smaller radius, tamed corona/halo/bloom) so it reads as a warm sphere
    // rather than an overexposed blob washing out the surrounding sky.
    float sunRadius = 0.034;
    float sunDisc = smoothstep(sunRadius + 0.014, sunRadius - 0.001, sunAngle) * dayFactor;
    float sunInner = smoothstep(sunRadius + 0.005, sunRadius - 0.003, sunAngle) * dayFactor;
    float sunCorona = exp(-sunAngle * sunAngle * 260.0) * 0.16 * dayFactor;
    float sunHalo = pow(max(sunDot, 0.0), 60.0) * 0.07 * dayFactor;
    float sunBloom = pow(max(sunDot, 0.0), 9.0) * 0.022 * dayFactor;
    float upViewDim = mix(1.0, 0.62, smoothstep(0.18, 0.72, rayDir.y));
    color += vec3(1.20, 0.94, 0.58) * sunDisc * 0.10 * upViewDim;
    color += vec3(1.16, 0.90, 0.54) * sunInner * 0.06 * upViewDim;
    color += vec3(1.10, 0.86, 0.50) * sunCorona * 0.12 * upViewDim;
    color += vec3(1.04, 0.82, 0.48) * sunHalo * 0.10 * upViewDim;
    color += vec3(1.00, 0.78, 0.44) * sunBloom * 0.08 * upViewDim;

    // Warm crepuscular beams — boosted relative to the contained disc so rays read clearly.
    vec3 godRayTint = mix(vec3(1.10, 0.66, 0.26), vec3(1.20, 0.78, 0.34), dayFactor);
    color += godRayTint * volumetricGodRays(rayDir, SUN_DIR, dayFactor) * 0.24;

    color += sampleStarField(rayDir, dayFactor);
    color += sampleAurora(rayDir, nightFactor);

    float aerialHaze = horizonBand * mix(0.0006, 0.0012, dayFactor);
    vec3 hazeTarget = mix(vec3(0.24, 0.46, 0.82), vec3(0.42, 0.62, 0.90), dayFactor);
    color = mix(color, hazeTarget, aerialHaze);
    color.b = max(color.b, max(color.r, color.g) * 0.94);
    color.r = min(color.r, color.b * 0.88);
    color.g = min(color.g, color.b * 0.94);
    color = mix(color, vec3(color.r * 0.78, color.g * 0.88, color.b), horizonBand * 0.06 * dayFactor);

    return toneMapSky(color);
}

vec3 surfaceBlockCell(vec3 worldPos, vec3 surfaceNormal) {
    return floor(worldPos - normalize(surfaceNormal) * 0.501 + 0.001);
}

// Lightweight sky for terrain fog/reflections — avoids volumetric cloud march and god rays.
vec3 cheapHorizonSkyColor(vec3 rayDir, float dayFactor) {
    vec3 sunDir = sunDirection();
    float elevation = clamp(rayDir.y, -1.0, 1.0);
    float horizonBand = pow(1.0 - abs(elevation), 2.2);

    vec3 zenith = mix(vec3(0.08, 0.26, 0.54), vec3(0.24, 0.62, 1.02), dayFactor);
    vec3 midSky = mix(vec3(0.12, 0.42, 0.68), vec3(0.42, 0.74, 1.00), dayFactor);
    vec3 horizon = horizonHazeColor(rayDir, dayFactor);
    vec3 horizonGlow = mix(vec3(0.46, 0.64, 0.86), vec3(0.88, 0.94, 1.0), dayFactor) * horizonBand;

    vec3 color = mix(horizon, midSky, smoothstep(-0.04, 0.38, elevation));
    color = mix(color, zenith, smoothstep(0.08, 0.82, elevation));
    color += horizonGlow * 0.10;
    color += atmosphericScattering(rayDir, sunDir, dayFactor) * 0.42;

    float sunDot = dot(rayDir, sunDir);
    float sunAngle = acos(clamp(sunDot, -1.0, 1.0));
    float sunDisc = smoothstep(0.040, 0.020, sunAngle) * dayFactor;
    color += vec3(1.32, 1.06, 0.68) * sunDisc * 0.62;

    float skyClouds = sampleProceduralSkyClouds(rayDir, sunDir, dayFactor);
    vec2 cloudColorUv = rayDir.xz / (rayDir.y + 0.18) * 0.072;
    float cloudHueVar = noise2D(cloudColorUv * 28.0) * 0.5 + noise2D(cloudColorUv * 58.0 + vec2(5.2, 9.1)) * 0.5;
    vec3 cloudShadow = mix(vec3(0.32, 0.50, 0.74), vec3(0.48, 0.64, 0.84), dayFactor);
    vec3 cloudBright = mix(cloudShadow, vec3(0.78, 0.88, 0.98), skyClouds * 0.78 + cloudHueVar * 0.14);
    cloudBright = mix(cloudBright, vec3(1.28, 1.18, 0.96), skyClouds * dayFactor * 0.58);
    color = mix(color, cloudBright, skyClouds * mix(0.78, 0.92, dayFactor));

    return color;
}

// Pitch- and camera-orientation-independent sky tint for distance fog.
vec3 horizonFogSkyColor(vec3 worldPos, float dayFactor) {
    vec3 horiz = normalize(vec3(worldPos.x, 0.0, worldPos.z) + vec3(1e-4, 0.0, 1e-4));
    vec3 sky = cheapHorizonSkyColor(normalize(vec3(horiz.x, 0.24, horiz.z)), dayFactor);
    vec3 fogTint = mix(vec3(0.50, 0.70, 0.94), vec3(0.68, 0.84, 0.98), dayFactor);
    sky = mix(sky, fogTint, 0.42);
    sky.b = max(sky.b, sky.r * 0.90);
    sky.g = max(sky.g, sky.b * 0.86);
    return sky;
}

vec3 distanceFogSkyColor(vec3 worldPos, float dayFactor) {
    vec3 base = horizonFogSkyColor(worldPos, dayFactor);
    float dist = length(worldPos - ubo.cameraPosition);
    float distFade = smoothstep(22.0, 118.0, dist);
    // Keep the far haze blue-tinted (atmospheric perspective) rather than fading to near-white.
    vec3 farFog = mix(vec3(0.56, 0.74, 0.95), vec3(0.72, 0.86, 0.99), dayFactor);
    return mix(base, farFog, distFade * 0.58);
}

// Water reflections: stable sky base (pitch-independent) plus clamped directional detail for sun/clouds.
vec3 waterSkyReflection(vec3 worldPos, vec3 reflectDir, float dayFactor, float dist) {
    vec3 stableSky = distanceFogSkyColor(worldPos, dayFactor);

    vec3 detailDir = normalize(vec3(reflectDir.x, max(reflectDir.y, 0.12), reflectDir.z));
    vec3 detailSky = dist > 56.0
        ? cheapHorizonSkyColor(detailDir, dayFactor)
        : sampleSky(detailDir);

    float detailWeight = smoothstep(0.02, 0.55, reflectDir.y) * 0.42;
    vec3 reflection = mix(stableSky, detailSky, detailWeight);

    float distFade = clamp(1.0 - dist / 128.0, 0.52, 1.0);
    reflection *= distFade;

    vec3 sunDir = sunDirection();
    float sunSpec = pow(max(dot(reflectDir, sunDir), 0.0), 180.0) * dayFactor;
    float sunBloom = pow(max(dot(reflectDir, sunDir), 0.0), 24.0) * dayFactor * 0.18;
    reflection += vec3(1.12, 1.02, 0.82) * sunSpec * 0.58;
    reflection += vec3(1.06, 0.94, 0.76) * sunBloom;

    float horizonMirror = pow(1.0 - abs(reflectDir.y), 2.4) * mix(0.08, 0.16, dayFactor);
    reflection = mix(reflection, horizonHazeColor(reflectDir, dayFactor), horizonMirror);

    return reflection;
}

vec3 skyFogColor(vec3 worldPos) {
    vec3 toFrag = worldPos - ubo.cameraPosition;
    float dist = length(toFrag);

    float dayFactor = clamp(sunDirection().y * 1.6 + 0.35, 0.0, 1.0);
    vec3 hazeColor = terrainFogColor(dayFactor);
    vec3 skyColor = horizonFogSkyColor(worldPos, dayFactor);

    float distBlend = 1.0 - exp(-dist * mix(0.0009, 0.0006, dayFactor));
    float fogAmount = clamp(distBlend, 0.0, 0.22);

    return mix(skyColor, hazeColor, fogAmount * 0.38);
}

float sunVisibilityRayMarch(vec3 worldPos, vec3 lightDir, vec3 surfaceNormal, int maxSteps) {
    vec3 n = normalize(surfaceNormal);
    vec3 sourceCell = surfaceBlockCell(worldPos, n);
    vec3 origin = worldPos + n * 0.02;
    vec3 dir = normalize(lightDir);

    vec3 mapPos = floor(origin);
    vec3 deltaDist = abs(1.0 / max(abs(dir), vec3(0.0001)));
    ivec3 step = ivec3(sign(dir));
    vec3 sideDist = (sign(dir) * (mapPos - origin) + sign(dir) * 0.5 + 0.5) * deltaDist;

    float visibility = 1.0;
    const int MAX_MARCH_STEPS = 28;

    for (int i = 0; i < MAX_MARCH_STEPS; i++) {
        if (i >= maxSteps) {
            break;
        }

        if (voxelBlocksSun(mapPos, sourceCell)) {
            float marchDist = float(i + 1);
            float hardness = clamp(1.0 - marchDist * 0.024, 0.28, 1.0);
            visibility *= 1.0 - 0.68 * hardness;
            if (visibility < 0.06) {
                return 0.0;
            }
        }

        if (sideDist.x < sideDist.y) {
            if (sideDist.x < sideDist.z) {
                sideDist.x += deltaDist.x;
                mapPos.x += float(step.x);
            } else {
                sideDist.z += deltaDist.z;
                mapPos.z += float(step.z);
            }
        } else {
            if (sideDist.y < sideDist.z) {
                sideDist.y += deltaDist.y;
                mapPos.y += float(step.y);
            } else {
                sideDist.z += deltaDist.z;
                mapPos.z += float(step.z);
            }
        }
    }

    return clamp(visibility, 0.0, 1.0);
}

float softSunShadow(vec3 worldPos, vec3 sunDir, vec3 surfaceNormal, float dist) {
    vec3 n = normalize(surfaceNormal);
    float dayFactor = clamp(sunDir.y * 1.6 + 0.35, 0.0, 1.0);
    int maxMarchSteps = dist > 64.0 ? 14 : (dist > 32.0 ? 20 : 28);
    int samples = dist > 80.0 ? 3 : (dist > 48.0 ? 5 : (dist > 24.0 ? 7 : 10));
    if (dayFactor < 0.40) {
        samples = max(samples / 2, 2);
    }
    if (max(dot(n, sunDir), 0.0) < 0.15) {
        samples = max(samples / 2, 2);
    }

    vec3 refAxis = abs(sunDir.y) < 0.92 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 right = normalize(cross(sunDir, refAxis));
    vec3 up = cross(right, sunDir);
    float penumbra = mix(0.24, 0.16, smoothstep(20.0, 88.0, dist));

    float vis = 0.0;
    const int MAX_SAMPLES = 10;
    for (int i = 0; i < MAX_SAMPLES; i++) {
        if (i >= samples) {
            break;
        }

        float angle = (float(i) + hash31(worldPos + vec3(float(i) * 2.1, 3.7, 5.9))) / float(samples) * 6.2831853;
        float radius = penumbra * (0.55 + hash31(worldPos + vec3(float(i) * 1.3, 7.1, 2.9)) * 0.45);
        vec3 jitteredSun = normalize(sunDir + (right * cos(angle) + up * sin(angle)) * radius);
        vis += sunVisibilityRayMarch(worldPos, jitteredSun, surfaceNormal, maxMarchSteps);
    }
    vis /= float(samples);

    float shadow = vis * vis * (3.0 - 2.0 * vis);
    shadow = mix(shadow, sqrt(shadow), 0.28);
    return clamp(shadow, 0.10, 1.0);
}

vec3 hemisphereSkylight(vec3 normal, float dayFactor) {
    float upFactor = normal.y * 0.5 + 0.5;
    vec3 skyUp = mix(vec3(0.16, 0.22, 0.38), vec3(0.62, 0.84, 1.18), dayFactor);
    vec3 skyHorizon = mix(vec3(0.22, 0.26, 0.36), vec3(0.88, 0.94, 1.08), dayFactor);
    vec3 skyDown = mix(vec3(0.08, 0.10, 0.14), vec3(0.34, 0.40, 0.48), dayFactor) * 0.62;
    vec3 hemi = mix(skyDown, skyHorizon, smoothstep(-0.2, 0.25, normal.y));
    hemi = mix(hemi, skyUp, smoothstep(0.0, 0.85, upFactor));
    return hemi * mix(0.48, 0.82, dayFactor);
}

float contactAmbientOcclusion(vec3 worldPos, vec3 normal, float dist) {
    if (dist > 56.0) {
        return 1.0;
    }

    vec3 n = normalize(normal);
    vec3 sourceCell = surfaceBlockCell(worldPos, n);
    vec3 local = worldPos - sourceCell;

    float edgeX = (abs(n.x) > 0.5) ? min(local.y, 1.0 - local.y) * min(local.z, 1.0 - local.z) : 1.0;
    float edgeY = (abs(n.y) > 0.5) ? min(local.x, 1.0 - local.x) * min(local.z, 1.0 - local.z) : 1.0;
    float edgeZ = (abs(n.z) > 0.5) ? min(local.x, 1.0 - local.x) * min(local.y, 1.0 - local.y) : 1.0;
    float cornerProx = 1.0 - min(min(edgeX, edgeY), edgeZ);
    float cornerAo = mix(0.52, 1.0, smoothstep(0.0, 0.42, 1.0 - cornerProx));

    // World-space crease darkening along face tangent axes (independent of camera pitch).
    vec3 refAxis = abs(n.y) < 0.92 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangentA = normalize(cross(n, refAxis));
    vec3 tangentB = cross(n, tangentA);
    float crease = 0.0;
    const float creaseStep = 0.34;
    crease += step(0.52, voxelBlocksSun(floor(worldPos + tangentA * creaseStep + 0.001), sourceCell) ? 1.0 : 0.0);
    crease += step(0.52, voxelBlocksSun(floor(worldPos - tangentA * creaseStep + 0.001), sourceCell) ? 1.0 : 0.0);
    crease += step(0.52, voxelBlocksSun(floor(worldPos + tangentB * creaseStep + 0.001), sourceCell) ? 1.0 : 0.0);
    crease += step(0.52, voxelBlocksSun(floor(worldPos - tangentB * creaseStep + 0.001), sourceCell) ? 1.0 : 0.0);
    float creaseAo = mix(1.0, 0.58, clamp(crease * 0.32, 0.0, 0.82));

    // Diagonal corner contact: darken where two adjacent edges meet.
    float diag = min(edgeX, edgeY) * min(edgeY, edgeZ) * min(edgeX, edgeZ);
    float diagAo = mix(1.0, 0.64, smoothstep(0.78, 0.96, 1.0 - diag));

    // Vertical contact: darken undersides near block bottoms.
    float bottomContact = 1.0;
    if (n.y < -0.5) {
        bottomContact = mix(0.72, 1.0, smoothstep(0.0, 0.28, local.y));
    }

    return min(min(min(cornerAo, creaseAo), diagAo), bottomContact);
}

vec3 approximateHitColor(vec3 hitPos) {
    vec3 grad = vec3(
        terrainOccupancy(hitPos + vec3(0.12, 0.0, 0.0)) - terrainOccupancy(hitPos - vec3(0.12, 0.0, 0.0)),
        terrainOccupancy(hitPos + vec3(0.0, 0.12, 0.0)) - terrainOccupancy(hitPos - vec3(0.0, 0.12, 0.0)),
        terrainOccupancy(hitPos + vec3(0.0, 0.0, 0.12)) - terrainOccupancy(hitPos - vec3(0.0, 0.0, 0.12)));
    vec3 hitNormal = normalize(grad + vec3(0.001, 0.002, 0.001));
    vec3 sunDir = sunDirection();
    float dayFactor = clamp(sunDir.y * 1.6 + 0.35, 0.0, 1.0);
    vec3 skylight = hemisphereSkylight(hitNormal, dayFactor);
    float sunDot = max(dot(hitNormal, sunDir), 0.0);
    return skylight + vec3(1.05, 0.96, 0.84) * sunDot * dayFactor * 0.38;
}

vec3 traceEnvironmentReflection(vec3 worldPos, vec3 reflectDir, float maxDistance) {
    const int STEPS = 7;
    float stepSize = maxDistance / float(STEPS);
    vec3 pos = worldPos + reflectDir * 0.06;
    float dist = distance(worldPos, ubo.cameraPosition);
    float dayFactor = clamp(sunDirection().y * 1.6 + 0.35, 0.0, 1.0);

    for (int i = 0; i < STEPS; i++) {
        pos += reflectDir * stepSize;
        if (terrainOccupancy(pos) > 0.48) {
            return approximateHitColor(pos);
        }
    }

    return dist > 40.0 ? cheapHorizonSkyColor(reflectDir, dayFactor) : sampleSky(reflectDir);
}

vec3 applyEnvironmentReflection(vec3 baseColor, vec3 worldPos, vec3 normal, int texIdx, float dist) {
    vec3 viewDir = normalize(ubo.cameraPosition - worldPos);
    vec3 n = normalize(normal);
    vec3 reflectDir = reflect(-viewDir, n);
    float ndv = max(dot(n, viewDir), 0.0);
    float fresnel = pow(1.0 - ndv, texIdx == 9 ? 4.2 : 2.6);
    float maxTrace = texIdx == 9 ? 28.0 : 42.0;
    float quality = clamp(1.0 - dist / 96.0, 0.45, 1.0);

    vec3 reflected = traceEnvironmentReflection(worldPos, reflectDir, maxTrace * quality);
    float reflectStrength = texIdx == 9 ? 0.68 : 0.58;
    float depthFade = clamp((dist - 0.75) / 10.0, 0.0, 0.3);
    return mix(baseColor, reflected, fresnel * (reflectStrength - depthFade));
}

vec3 boostSaturation(vec3 color, float amount) {
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    return mix(vec3(luma), color, amount);
}

vec3 applyGrassTopFringe(vec3 color, vec3 worldPos, vec3 normal, int texIdx, float dayFactor) {
    bool isGrassTop = texIdx == 3 || texIdx == 26 || texIdx == 50 || texIdx == 51;
    bool isGrassSide = texIdx == 26 || texIdx == 51;
    if (!isGrassTop && !isGrassSide) {
        return color;
    }

    vec3 cell = floor(worldPos + vec3(0.0001));
    vec3 local = worldPos - cell;

    if (isGrassTop && normal.y > 0.5) {
        float edgeX = min(local.x, 1.0 - local.x);
        float edgeZ = min(local.z, 1.0 - local.z);
        float edgeDist = min(edgeX, edgeZ);
        float edgeMask = 1.0 - smoothstep(0.0, 0.08, edgeDist);
        if (edgeMask > 0.001) {
            float fringeNoise = noise2D(local.xz * 18.0 + cell.xz * 0.37) * 0.04;
            vec3 fringeDark = color * vec3(0.82, 0.94, 0.78);
            color = mix(color, fringeDark, edgeMask * (0.28 + fringeNoise) * mix(0.6, 1.0, dayFactor));
        }
    }

    if (isGrassSide && abs(normal.y) < 0.5) {
        float edgeX = min(local.x, 1.0 - local.x);
        float edgeZ = min(local.z, 1.0 - local.z);
        float edgeDist = min(edgeX, edgeZ);
        float edgeMask = 1.0 - smoothstep(0.0, 0.08, edgeDist);
        float topBand = smoothstep(0.72, 0.96, local.y);
        topBand = clamp(topBand + noise2D(vec2(local.x + local.z, local.y) * 14.0 + cell.xz * 0.29) * 0.04, 0.0, 1.0);

        if (edgeMask > 0.001 || topBand > 0.001) {
            vec3 sideFringeDark = color * vec3(0.78, 0.90, 0.72);
            color = mix(color, sideFringeDark, max(edgeMask * 0.24, topBand * 0.18) * mix(0.5, 0.8, dayFactor));
        }
    }

    return color;
}

vec3 terrainMicroVariation(vec3 color, vec3 worldPos, int texIdx) {
    bool terrainTile = texIdx == 1 || texIdx == 2 || texIdx == 3 || texIdx == 4 || texIdx == 8
        || texIdx == 10 || texIdx == 11 || texIdx == 12 || texIdx == 26 || texIdx == 38
        || texIdx == 39 || texIdx == 40 || texIdx == 47 || texIdx == 50 || texIdx == 51;
    if (!terrainTile) {
        return color;
    }

    vec3 cell = floor(worldPos + vec3(0.0001));
    float fine = noise2D(cell.xz * 0.73 + vec2(float(texIdx) * 0.17));
    float micro = hash31(cell + vec3(texIdx * 3, 17, 41));
    vec3 shift = vec3(micro - 0.5, fine - 0.5, (micro + fine) * 0.5 - 0.5) * 0.06;
    return clamp(color + shift, 0.0, 1.0);
}

vec3 enhanceTerrainColor(vec3 color, int texIdx, float dayFactor) {
    if (texIdx == 3 || texIdx == 26 || texIdx == 50 || texIdx == 51) {
        // Grass saturation cap: mild tint + modest boost so natural mid-greens from the atlas
        // stay natural instead of being pushed back into neon territory (was up to 1.22x).
        vec3 grassTint = vec3(1.01, 1.03, 0.98);
        color = boostSaturation(color * mix(vec3(1.0), grassTint, dayFactor * 0.25), mix(1.0, 1.08, dayFactor));
    } else if (texIdx == 2) {
        vec3 dirtTint = vec3(1.06, 1.02, 0.94);
        color = boostSaturation(color * mix(vec3(1.0), dirtTint, dayFactor * 0.38), mix(1.06, 1.16, dayFactor));
    } else if (texIdx == 1 || texIdx == 47) {
        color = boostSaturation(color, mix(1.06, 1.16, dayFactor));
    } else if (texIdx == 10 || texIdx == 11 || texIdx == 12) {
        vec3 oreTint = texIdx == 10 ? vec3(1.08, 1.02, 0.94)
            : texIdx == 11 ? vec3(1.12, 0.96, 0.86)
            : vec3(0.92, 0.94, 1.02);
        color = boostSaturation(color * oreTint, mix(1.10, 1.24, dayFactor));
    } else if (texIdx == 8 || texIdx == 38 || texIdx == 39 || texIdx == 40) {
        // Leaf saturation cap: same rationale as grass above (was up to 1.20x).
        vec3 leafTint = vec3(1.01, 1.04, 0.97);
        color = boostSaturation(color * mix(vec3(1.0), leafTint, dayFactor * 0.24), mix(1.0, 1.10, dayFactor));
    } else if (texIdx == 4 || texIdx == 49) {
        color = boostSaturation(color, mix(1.06, 1.14, dayFactor));
    }
    return color;
}

float minecraftFaceShade(vec3 normal) {
    vec3 n = normalize(normal);
    if (n.y > 0.5) {
        return 1.0;
    }
    if (n.y < -0.5) {
        return 0.5;
    }
    return 0.8;
}

float sampleNearbyBlockLight(vec3 worldPos, float bakedBlockLight) {
    // Stub for GPU neighbor sampling; CPU mesh builder bakes emissive blocks within radius.
    return bakedBlockLight;
}

vec3 lightBlock(vec3 texColor, vec3 faceNormal, vec3 worldPos, float vertexAo) {
    vec3 normal = normalize(faceNormal);
    float faceShade = minecraftFaceShade(normal);
    vec3 sunDir = sunDirection();
    vec3 moonDir = moonDirection();
    float dayFactor = clamp(sunDir.y * 1.6 + 0.35, 0.0, 1.0);
    float nightFactor = 1.0 - dayFactor;

    float blockLight = sampleNearbyBlockLight(worldPos, max(0.0, vertexAo - 1.0));
    float ao = clamp(vertexAo, 0.26, 1.0);
    float dist = distance(worldPos, ubo.cameraPosition);
    float contactAo = contactAmbientOcclusion(worldPos, normal, dist);
    ao = min(ao, contactAo);
    float aoCurve = mix(ao, pow(ao, 1.42), 0.58);

    float sunShadow = 1.0;
    if (dayFactor > 0.08) {
        sunShadow = softSunShadow(worldPos, sunDir, normal, dist);
        float aoLift = (aoCurve - 0.26) / 0.74;
        sunShadow = mix(sunShadow, 1.0, aoLift * 0.04);
    } else {
        sunShadow = mix(0.66, 1.0, aoCurve);
    }
    float moonShadow = mix(0.70, 1.0, aoCurve);

    float sunDiffuse = max(dot(normal, sunDir), 0.0);
    float moonDiffuse = max(dot(normal, moonDir), 0.0);

    vec3 sunColor = vec3(1.40, 1.34, 0.90);
    vec3 moonColor = vec3(0.72, 0.84, 1.08);
    vec3 skylight = hemisphereSkylight(normal, dayFactor);

    float sunLight = sunDiffuse * sunShadow * mix(1.22, 2.72, dayFactor);
    float moonLight = moonDiffuse * moonShadow * nightFactor * 0.44;

    float bounce = max(normal.y, 0.0) * mix(0.16, 0.30, dayFactor);
    float skyExposure = caveSkyExposure(worldPos);
    vec3 ambient = (skylight * mix(0.78, 1.14, aoCurve) + vec3(bounce)) * mix(0.22, 1.0, skyExposure);
    ambient *= mix(0.94, 1.04, dayFactor);

    vec3 lit = texColor * faceShade * (ambient + sunColor * sunLight + moonColor * moonLight);
    lit += texColor * faceShade * blockLight * 1.35;
    lit = max(lit, texColor * faceShade * mix(0.22, 0.14, dayFactor));

    float satBoost = mix(1.46, 1.88, dayFactor);
    lit = boostSaturation(lit, satBoost);

    return clamp(lit, 0.0, 1.52);
}

bool hudInRect(vec2 p, vec2 origin, vec2 size) {
    vec2 local = p - origin;
    return local.x >= 0.0 && local.x <= size.x && local.y >= 0.0 && local.y <= size.y;
}

// Minecraft GUI scale ~2 at 1080p (ice_and_lake_trees_gui.jpg: 182×22 hotbar, 9px icon step).
const float HUD_GUI_SCALE_AT_1080P = 2.0;
const float HUD_HOTBAR_WIDTH = 182.0;
const float HUD_HOTBAR_HEIGHT = 22.0;
const float HUD_ICON_STEP = 9.0;
const float HUD_ICON_TEXTURE = 9.0;
const float HUD_XP_BAR_HEIGHT = 5.0;
const vec3 HUD_MC_BORDER = vec3(0.545098, 0.545098, 0.545098); // #8B8B8B
const vec3 HUD_MC_INSET = vec3(0.215686, 0.215686, 0.215686);   // #373737
const vec3 HUD_MC_SHADOW = vec3(0.105882, 0.105882, 0.105882);  // #1B1B1B

float hudUiScale() {
    float heightScale = max(ubo.viewportSize.y / 1080.0, 0.67);
    return heightScale * HUD_GUI_SCALE_AT_1080P;
}

float hudHotbarSlotSize() {
    return (HUD_HOTBAR_WIDTH * hudUiScale()) / 9.0;
}

float hudIconStep() {
    return floor(HUD_ICON_STEP * hudUiScale());
}

float hudIconSize() {
    return floor(HUD_ICON_TEXTURE * hudUiScale());
}

bool mcHeartShape(int x, int y) {
    if (y == 0) return x == 2 || x == 3 || x == 6 || x == 7;
    if (y == 1) return x == 1 || x == 2 || x == 3 || x == 6 || x == 7 || x == 8;
    if (y == 2 || y == 3) return x >= 0 && x <= 8;
    if (y == 4) return x >= 0 && x <= 8;
    if (y == 5) return x >= 1 && x <= 7;
    if (y == 6) return x >= 2 && x <= 6;
    if (y == 7) return x >= 3 && x <= 5;
    if (y == 8) return x == 4;
    return false;
}

bool mcFoodShape(int x, int y) {
    if (y == 0) return x >= 1 && x <= 4;
    if (y == 1) return x >= 1 && x <= 5;
    if (y == 2) return x >= 0 && x <= 5;
    if (y == 3) return x >= 0 && x <= 5;
    if (y == 4) return x >= 1 && x <= 4;
    if (y == 5) return x >= 2 && x <= 3;
    if (y == 6) return x == 3;
    return false;
}

bool mcFoodBoneShape(int x, int y) {
    if (y == 0) return x == 6 || x == 7;
    if (y == 1) return x >= 5 && x <= 7;
    if (y == 2) return x >= 5 && x <= 7;
    if (y == 3) return x >= 5 && x <= 7;
    if (y == 4) return x >= 5 && x <= 7;
    if (y == 5) return x == 5 || x == 6;
    if (y == 6) return x == 5 || x == 6;
    if (y == 7) return x == 5 || x == 6;
    if (y == 8) return x == 5 || x == 6;
    return false;
}

bool mcOxygenPixel(int x, int y) {
    float cx = 4.0;
    float cy = 4.5;
    float r = length(vec2(float(x) - cx, float(y) - cy));
    return r < 3.2 && r > 1.6;
}

bool hudOnBorder(vec2 local, vec2 size, float border) {
    return local.x < border || local.x > size.x - border || local.y < border || local.y > size.y - border;
}

vec3 hudMixRect(vec3 color, vec2 screenPos, vec2 origin, vec2 size, vec3 fill, float alpha) {
    if (hudInRect(screenPos, origin, size)) {
        color = mix(color, fill, alpha);
    }
    return color;
}

int hudGlyphRow5x7(int glyph, int row) {
    if (row < 0 || row > 6) {
        return 0;
    }
    if (glyph == 32) {
        return 0;
    }
    if (glyph >= 48 && glyph <= 57) {
        int d = glyph - 48;
        if (d == 0) { int[7] rows = int[7](31, 17, 17, 17, 17, 17, 31); return rows[row]; }
        if (d == 1) { int[7] rows = int[7](4, 12, 4, 4, 4, 4, 14); return rows[row]; }
        if (d == 2) { int[7] rows = int[7](30, 1, 1, 14, 16, 16, 31); return rows[row]; }
        if (d == 3) { int[7] rows = int[7](30, 1, 1, 14, 1, 1, 30); return rows[row]; }
        if (d == 4) { int[7] rows = int[7](17, 17, 17, 31, 1, 1, 1); return rows[row]; }
        if (d == 5) { int[7] rows = int[7](31, 16, 16, 30, 1, 1, 30); return rows[row]; }
        if (d == 6) { int[7] rows = int[7](14, 16, 16, 30, 17, 17, 30); return rows[row]; }
        if (d == 7) { int[7] rows = int[7](31, 1, 2, 4, 8, 8, 8); return rows[row]; }
        if (d == 8) { int[7] rows = int[7](14, 17, 17, 14, 17, 17, 14); return rows[row]; }
        if (d == 9) { int[7] rows = int[7](14, 17, 17, 15, 1, 1, 14); return rows[row]; }
    }
    if (glyph == 65) { int[7] rows = int[7](14, 17, 17, 31, 17, 17, 17); return rows[row]; }
    if (glyph == 67) { int[7] rows = int[7](14, 17, 16, 16, 16, 17, 14); return rows[row]; }
    if (glyph == 68) { int[7] rows = int[7](30, 17, 17, 17, 17, 17, 30); return rows[row]; }
    if (glyph == 69) { int[7] rows = int[7](31, 16, 16, 30, 16, 16, 31); return rows[row]; }
    if (glyph == 73) { int[7] rows = int[7](14, 4, 4, 4, 4, 4, 14); return rows[row]; }
    if (glyph == 78) { int[7] rows = int[7](17, 25, 21, 19, 17, 17, 17); return rows[row]; }
    if (glyph == 79) { int[7] rows = int[7](14, 17, 17, 17, 17, 17, 14); return rows[row]; }
    if (glyph == 80) { int[7] rows = int[7](30, 17, 17, 30, 16, 16, 16); return rows[row]; }
    if (glyph == 82) { int[7] rows = int[7](30, 17, 17, 30, 20, 18, 17); return rows[row]; }
    if (glyph == 83) { int[7] rows = int[7](14, 17, 16, 14, 1, 17, 14); return rows[row]; }
    if (glyph == 84) { int[7] rows = int[7](31, 4, 4, 4, 4, 4, 4); return rows[row]; }
    if (glyph == 85) { int[7] rows = int[7](17, 17, 17, 17, 17, 17, 14); return rows[row]; }
    if (glyph == 86) { int[7] rows = int[7](17, 17, 17, 17, 10, 10, 4); return rows[row]; }
    if (glyph == 89) { int[7] rows = int[7](17, 17, 17, 14, 4, 4, 4); return rows[row]; }
    if (glyph == 66) { int[7] rows = int[7](30, 17, 17, 30, 17, 17, 30); return rows[row]; }
    if (glyph == 70) { int[7] rows = int[7](31, 16, 16, 30, 16, 16, 16); return rows[row]; }
    if (glyph == 71) { int[7] rows = int[7](14, 17, 16, 19, 17, 17, 15); return rows[row]; }
    if (glyph == 72) { int[7] rows = int[7](17, 17, 17, 31, 17, 17, 17); return rows[row]; }
    if (glyph == 75) { int[7] rows = int[7](17, 18, 20, 24, 20, 18, 17); return rows[row]; }
    if (glyph == 76) { int[7] rows = int[7](16, 16, 16, 16, 16, 16, 31); return rows[row]; }
    if (glyph == 77) { int[7] rows = int[7](17, 27, 21, 21, 17, 17, 17); return rows[row]; }
    if (glyph == 81) { int[7] rows = int[7](14, 17, 17, 17, 21, 17, 14); return rows[row]; }
    if (glyph == 87) { int[7] rows = int[7](17, 17, 17, 21, 21, 21, 17); return rows[row]; }
    if (glyph == 88) { int[7] rows = int[7](17, 17, 10, 4, 10, 17, 17); return rows[row]; }
    if (glyph == 90) { int[7] rows = int[7](31, 1, 2, 4, 8, 16, 31); return rows[row]; }
    if (glyph == 74) { int[7] rows = int[7](30, 5, 5, 5, 5, 5, 5); return rows[row]; }
    if (glyph == 100) { int[7] rows = int[7](0, 0, 14, 17, 17, 17, 30); return rows[row]; }
    if (glyph == 101) { int[7] rows = int[7](0, 0, 14, 31, 16, 16, 30); return rows[row]; }
    if (glyph == 105) { int[7] rows = int[7](4, 0, 4, 4, 4, 4, 4); return rows[row]; }
    if (glyph == 111) { int[7] rows = int[7](0, 0, 14, 17, 17, 17, 14); return rows[row]; }
    if (glyph == 117) { int[7] rows = int[7](0, 0, 17, 17, 17, 17, 14); return rows[row]; }
    if (glyph == 121) { int[7] rows = int[7](0, 0, 17, 17, 14, 4, 8); return rows[row]; }
    return 0;
}

int hudFlagBits() {
    return int(floor(ubo.hudFlags + 0.5));
}

float hudGlyph5x7(int glyph, vec2 local, float px) {
    int x = int(floor(local.x / px));
    int y = int(floor(local.y / px));
    if (x < 0 || x > 4 || y < 0 || y > 6) {
        return 0.0;
    }
    int rowBits = hudGlyphRow5x7(glyph, y);
    int mask = 1 << (4 - x);
    return float((rowBits & mask) != 0 ? 1 : 0);
}

vec3 hudDrawText(vec3 color, vec2 screenPos, vec2 origin, int glyph, vec3 textColor, float px) {
    vec2 size = vec2(px * 5.0, px * 7.0);
    if (!hudInRect(screenPos, origin, size)) {
        return color;
    }
    vec2 local = screenPos - origin;
    if (hudGlyph5x7(glyph, local, px) > 0.5) {
        color = mix(color, textColor, 0.92);
    }
    return color;
}

vec3 hudDrawString(vec3 color, vec2 screenPos, vec2 origin, int g0, int g1, int g2, int g3, int g4, int g5, vec3 textColor, float px, float spacing) {
    float advance = px * 5.0 + spacing;
    color = hudDrawText(color, screenPos, origin, g0, textColor, px);
    color = hudDrawText(color, screenPos, origin + vec2(advance, 0.0), g1, textColor, px);
    color = hudDrawText(color, screenPos, origin + vec2(advance * 2.0, 0.0), g2, textColor, px);
    color = hudDrawText(color, screenPos, origin + vec2(advance * 3.0, 0.0), g3, textColor, px);
    color = hudDrawText(color, screenPos, origin + vec2(advance * 4.0, 0.0), g4, textColor, px);
    color = hudDrawText(color, screenPos, origin + vec2(advance * 5.0, 0.0), g5, textColor, px);
    return color;
}

bool hudSurvivalPixel(int barIndex, int px, int py) {
    if (barIndex == 0) {
        return mcHeartShape(px, py);
    }
    if (barIndex == 1) {
        return mcFoodShape(px, py) || mcFoodBoneShape(px, py);
    }
    return mcOxygenPixel(px, py);
}

bool hudSurvivalEdge(int barIndex, int px, int py) {
    if (barIndex == 0) {
        return !mcHeartShape(px + 1, py) || !mcHeartShape(px - 1, py) || !mcHeartShape(px, py + 1) || !mcHeartShape(px, py - 1);
    }
    if (barIndex == 1) {
        bool inMeat = mcFoodShape(px, py);
        bool inBone = mcFoodBoneShape(px, py);
        if (!inMeat && !inBone) {
            return false;
        }
        bool meatEdge = inMeat && (!mcFoodShape(px + 1, py) || !mcFoodShape(px - 1, py) || !mcFoodShape(px, py + 1) || !mcFoodShape(px, py - 1));
        bool boneEdge = inBone && (!mcFoodBoneShape(px + 1, py) || !mcFoodBoneShape(px - 1, py) || !mcFoodBoneShape(px, py + 1) || !mcFoodBoneShape(px, py - 1));
        return meatEdge || boneEdge;
    }
    return !mcOxygenPixel(px + 1, py) || !mcOxygenPixel(px - 1, py);
}

vec3 hudSurvivalFillColor(int barIndex, int px, int py, bool isEmpty) {
    if (isEmpty) {
        return vec3(0.10, 0.10, 0.12);
    }
    if (barIndex == 0) {
        if (px <= 3 && py <= 2) {
            return vec3(1.0, 0.38, 0.38);
        }
        if (px <= 4 && py <= 1) {
            return vec3(1.0, 0.12, 0.12);
        }
        return vec3(0.88, 0.0, 0.0);
    }
    if (barIndex == 1) {
        if (mcFoodBoneShape(px, py)) {
            return vec3(1.0, 0.96, 0.86);
        }
        if (px <= 2 && py <= 2) {
            return vec3(0.96, 0.58, 0.10);
        }
        return vec3(0.78, 0.42, 0.02);
    }
    return vec3(0.16, 0.62, 0.90);
}

vec3 hudDrawSurvivalIcon(vec3 color, vec2 screenPos, vec2 origin, int barIndex, float strength) {
    float iconPx = hudIconSize();
    origin = floor(origin);
    vec2 size = vec2(iconPx, iconPx);
    if (!hudInRect(screenPos, origin, size)) {
        return color;
    }

    float cell = iconPx / 9.0;
    vec2 pixel = floor((screenPos - origin) / cell);
    int px = int(clamp(pixel.x, 0.0, 8.0));
    int py = int(clamp(pixel.y, 0.0, 8.0));

    if (!hudSurvivalPixel(barIndex, px, py)) {
        return color;
    }

    bool isHalf = strength > 0.01 && strength < 0.99;
    bool isEmpty = strength <= 0.01;
    if (isHalf && px > 4) {
        isEmpty = true;
    }

    vec3 pixelColor = hudSurvivalFillColor(barIndex, px, py, isEmpty);
    if (hudSurvivalEdge(barIndex, px, py)) {
        pixelColor = vec3(0.0);
    } else if (isHalf && !isEmpty) {
        pixelColor = mix(vec3(0.18, 0.18, 0.20), pixelColor, 0.72);
    }

    color = pixelColor;
    return color;
}

vec3 hudDrawSurvivalRow(vec3 color, vec2 screenPos, vec2 rowOrigin, int barIndex, float fillNorm, int iconCount, bool oxygenLow) {
    float iconStep = hudIconStep();
    float fillPoints = fillNorm * 20.0;

    for (int i = 0; i < iconCount; i++) {
        float fullThreshold = float(i + 1) * 2.0;
        float halfThreshold = float(i) * 2.0 + 1.0;
        float strength = fillPoints >= fullThreshold ? 1.0 : (fillPoints >= halfThreshold ? 0.5 : 0.0);
        vec2 iconOrigin = rowOrigin + vec2(float(i) * iconStep, 0.0);
        if (barIndex == 2 && oxygenLow && strength > 0.01) {
            float flash = 0.5 + 0.5 * sin(screenPos.x * 0.1 + screenPos.y * 0.08);
            if (flash > 0.65) {
                strength = 0.5;
            }
        }
        color = hudDrawSurvivalIcon(color, screenPos, iconOrigin, barIndex, strength);
    }

    return color;
}

vec3 hudBlockColor(int blockId) {
    if (blockId <= 0) {
        return vec3(0.09, 0.11, 0.15);
    }

    float t = float(blockId) / 32.0;
    return clamp(vec3(
        0.5 + 0.5 * sin(t * 6.2831853),
        0.5 + 0.5 * sin(t * 6.2831853 + 2.094),
        0.5 + 0.5 * sin(t * 6.2831853 + 4.188)
    ), vec3(0.15), vec3(0.95));
}

int inventorySlotIndex(int row, int col) {
    if (row < 3) {
        return 9 + row * 9 + col;
    }
    return col;
}

vec3 hudDrawSlotNumber(vec3 color, vec2 screenPos, vec2 origin, int value, vec3 textColor, float px) {
    if (value < 10) {
        return hudDrawText(color, screenPos, origin, 48 + value, textColor, px);
    }

    int tens = value / 10;
    int ones = value % 10;
    float advance = px * 5.0 + 1.0;
    color = hudDrawText(color, screenPos, origin, 48 + tens, textColor, px);
    return hudDrawText(color, screenPos, origin + vec2(advance, 0.0), 48 + ones, textColor, px);
}

vec3 hudDrawHotbarSlotNumber(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, int slotNumber, float hudScale) {
    float digitPx = max(4.0, floor(hudScale * 2.4));
    vec2 digitOrigin = floor(slotOrigin + vec2(2.0, 2.0));
    vec2 shadowOffset = vec2(max(1.0, floor(digitPx * 0.65)), max(1.0, floor(digitPx * 0.65)));
    vec2 shadowOrigin = digitOrigin + shadowOffset;
    color = hudDrawText(color, screenPos, shadowOrigin, 48 + slotNumber, vec3(0.0), digitPx);
    return hudDrawText(color, screenPos, digitOrigin, 48 + slotNumber, vec3(1.0), digitPx);
}

vec3 hudMcSlotBevel(vec2 local, vec2 slotSize, float hudScale, bool selected) {
    float bevel = max(1.0, floor(hudScale * 0.5));
    vec3 color = HUD_MC_INSET;

    if (local.x < bevel) {
        color = HUD_MC_BORDER;
    }
    if (local.y < bevel) {
        color = HUD_MC_BORDER;
    }
    if (local.x >= slotSize.x - bevel) {
        color = HUD_MC_SHADOW;
    }
    if (local.y >= slotSize.y - bevel) {
        color = HUD_MC_SHADOW;
    }

    if (selected) {
        float selBorder = max(1.0, floor(hudScale * 0.5));
        bool onSelBorder = local.x < selBorder || local.y < selBorder
            || local.x >= slotSize.x - selBorder || local.y >= slotSize.y - selBorder;
        if (onSelBorder) {
            color = vec3(1.0);
        }
    }

    return color;
}

vec3 hudDrawHotbarSlot(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, bool selected, float hudScale, bool drawRightDivider) {
    if (!hudInRect(screenPos, slotOrigin, slotSize)) {
        return color;
    }

    vec2 local = screenPos - slotOrigin;
    color = hudMcSlotBevel(local, slotSize, hudScale, selected);

    float divider = max(1.0, floor(hudScale * 0.45));
    if (drawRightDivider && local.x >= slotSize.x - divider) {
        color = HUD_MC_INSET;
    }

    return color;
}

vec3 hudDrawHotbarSelection(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, float hudScale) {
    if (!hudInRect(screenPos, slotOrigin, slotSize)) {
        return color;
    }

    vec2 local = screenPos - slotOrigin;
    float border = max(1.0, floor(hudScale * 0.5));
    bool onBorder = local.x < border || local.y < border
        || local.x >= slotSize.x - border || local.y >= slotSize.y - border;
    if (onBorder) {
        return vec3(1.0);
    }

    return color;
}

vec3 hudDrawInventorySlot(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, bool selected, float hudScale) {
    if (!hudInRect(screenPos, slotOrigin, slotSize)) {
        return color;
    }

    vec2 local = screenPos - slotOrigin;
    return hudMcSlotBevel(local, slotSize, hudScale, selected);
}

int hudUnpackSlotTexture(int packed) {
    return packed & 0xFFFF;
}

int hudUnpackSlotCount(int packed) {
    return packed >> 16;
}

vec3 hudDrawSlotIcon(vec3 color, vec2 screenPos, vec2 iconOrigin, vec2 iconSize, int textureIndex) {
    if (textureIndex <= 0 || !hudInRect(screenPos, iconOrigin, iconSize)) {
        return color;
    }

    vec2 uv = (screenPos - iconOrigin) / iconSize;
    vec3 texColor = texture(blockTextures, vec3(uv, float(textureIndex))).rgb;
    return mix(color, texColor, 0.94);
}

vec3 hudDrawStackCount(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, int count, float hudScale) {
    if (count <= 1) {
        return color;
    }

    float digitPx = max(3.0, floor(hudScale * 1.9));
    float digitWidth = digitPx * 5.0;
    float digitHeight = digitPx * 7.0;
    int digits = count >= 10 ? 2 : 1;
    float textWidth = digitWidth * float(digits) + max(0.0, float(digits - 1));
    vec2 digitOrigin = floor(slotOrigin + vec2(
        slotSize.x - textWidth - max(1.0, floor(hudScale * 0.35)),
        slotSize.y - digitHeight - max(1.0, floor(hudScale * 0.35))
    ));
    vec2 shadowOffset = vec2(max(1.0, floor(digitPx * 0.65)), max(1.0, floor(digitPx * 0.65)));
    color = hudDrawSlotNumber(color, screenPos, digitOrigin + shadowOffset, count, vec3(0.0), digitPx);
    return hudDrawSlotNumber(color, screenPos, digitOrigin, count, vec3(1.0), digitPx);
}

vec3 hudDrawPackedSlotContents(vec3 color, vec2 screenPos, vec2 slotOrigin, vec2 slotSize, int packed, float hudScale) {
    int textureIndex = hudUnpackSlotTexture(packed);
    int stackCount = hudUnpackSlotCount(packed);
    if (textureIndex <= 0) {
        return color;
    }

    float iconInset = max(2.0, floor(hudScale * 0.55));
    float iconSize = slotSize.x - iconInset * 2.0;
    vec2 iconOrigin = slotOrigin + vec2(iconInset, iconInset);
    color = hudDrawSlotIcon(color, screenPos, iconOrigin, vec2(iconSize, iconSize), textureIndex);
    return hudDrawStackCount(color, screenPos, slotOrigin, slotSize, stackCount, hudScale);
}

vec3 hudDrawCrosshair(vec3 color, vec2 screenPos, vec2 center, float hudScale) {
    vec2 pixel = floor(screenPos);
    vec2 centerPixel = floor(center);
    vec2 delta = pixel - centerPixel;
    vec2 ad = abs(delta);
    float arm = max(4.0, floor(hudScale * 2.0));
    float gap = max(2.0, floor(hudScale * 1.0));
    float thick = 1.0;
    bool vertical = ad.x < thick && ad.y >= gap && ad.y < gap + arm;
    bool horizontal = ad.y < thick && ad.x >= gap && ad.x < gap + arm;
    if (vertical || horizontal) {
        color = vec3(1.0);
    }
    return color;
}

vec3 hudDrawBreakProgressWheel(vec3 color, vec2 screenPos, vec2 center, float breakAmt, float hudScale) {
    vec2 wheelLocal = screenPos - center;
    float radius = length(wheelLocal);
    float innerR = 9.0 * hudScale;
    float outerR = 12.0 * hudScale;
    float ring = smoothstep(innerR - 0.6, innerR, radius) * (1.0 - smoothstep(outerR, outerR + 0.6, radius));
    if (ring < 0.01) {
        return color;
    }

    float angle = atan(wheelLocal.y, wheelLocal.x);
    float startAngle = -1.5707963;
    float relAngle = mod(angle - startAngle + 6.2831853, 6.2831853);
    float arcEnd = breakAmt * 6.2831853;
    float inArc = step(relAngle, arcEnd);

    vec3 trackColor = vec3(0.42, 0.42, 0.45);
    vec3 fillColor = vec3(0.88, 0.9, 0.94);
    color = mix(color, trackColor, ring * 0.55);
    color = mix(color, fillColor, ring * inArc * 0.95);
    return color;
}

vec3 hudDrawCharAt(vec3 color, vec2 screenPos, vec2 origin, int glyph, vec3 textColor, float px, float advance, int index) {
    return hudDrawText(color, screenPos, origin + vec2(advance * float(index), 0.0), glyph, textColor, px);
}

int hudBlockNameLength(int blockId) {
    if (blockId == 1) return 5;
    if (blockId == 2) return 4;
    if (blockId == 3) return 5;
    if (blockId == 4) return 4;
    if (blockId == 5) return 5;
    if (blockId == 6) return 3;
    if (blockId == 7) return 4;
    if (blockId == 8) return 6;
    if (blockId == 9) return 5;
    if (blockId == 10) return 8;
    if (blockId == 11) return 10;
    if (blockId == 12) return 8;
    if (blockId == 13) return 6;
    if (blockId == 14) return 7;
    if (blockId == 15) return 3;
    if (blockId == 16) return 4;
    if (blockId == 17) return 8;
    if (blockId == 18) return 5;
    if (blockId == 19) return 6;
    if (blockId == 20) return 9;
    if (blockId == 21) return 8;
    if (blockId == 22) return 4;
    if (blockId == 23) return 4;
    if (blockId == 24) return 9;
    if (blockId == 25) return 6;
    if (blockId == 26) return 4;
    if (blockId == 27) return 9;
    if (blockId == 28) return 10;
    if (blockId == 29) return 10;
    if (blockId == 30) return 12;
    if (blockId == 31) return 13;
    if (blockId == 32) return 13;
    if (blockId == 33) return 6;
    if (blockId == 34) return 10;
    if (blockId == 35) return 6;
    if (blockId == 36) return 8;
    if (blockId == 37) return 9;
    if (blockId == 38) return 10;
    if (blockId == 39) return 8;
    if (blockId == 40) return 12;
    if (blockId == 41) return 5;
    if (blockId == 42) return 4;
    if (blockId == 43) return 5;
    if (blockId <= 0) return 0;
    return 7;
}

vec3 hudDrawBlockName(vec3 color, vec2 screenPos, vec2 nameOrigin, int blockId, vec3 nameColor, float namePx, float nameAdvance) {
    if (blockId == 1) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 2) { color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 3) { color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 4) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 5) { color = hudDrawCharAt(color, screenPos, nameOrigin, 87, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 6) { color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 2); }
    else if (blockId == 7) { color = hudDrawCharAt(color, screenPos, nameOrigin, 87, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 8) { color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 9) { color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 10) { color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 11) { color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 9); }
    else if (blockId == 12) { color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 13) { color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 14) { color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 75, nameColor, namePx, nameAdvance, 6); }
    else if (blockId == 15) { color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 2); }
    else if (blockId == 16) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 87, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 17) { color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 18) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 19) { color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 75, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 20) { color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 87, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); }
    else if (blockId == 21) { color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 22) { color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 89, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 23) { color = hudDrawCharAt(color, screenPos, nameOrigin, 77, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 24) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); }
    else if (blockId == 25) { color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 26) { color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 27) { color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 72, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 8); }
    else if (blockId == 28) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 9); }
    else if (blockId == 29) { color = hudDrawCharAt(color, screenPos, nameOrigin, 74, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 9); }
    else if (blockId == 30) { color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 72, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 9); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 10); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 11); }
    else if (blockId == 31) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 9); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 10); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 11); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 12); }
    else if (blockId == 32) { color = hudDrawCharAt(color, screenPos, nameOrigin, 74, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 9); color = hudDrawCharAt(color, screenPos, nameOrigin, 86, nameColor, namePx, nameAdvance, 10); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 11); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 12); }
    else if (blockId == 33) { color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 34) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 87, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 89, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 9); }
    else if (blockId == 35) { color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 90, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 5); }
    else if (blockId == 36) { color = hudDrawCharAt(color, screenPos, nameOrigin, 77, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 89, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 77, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 37) { color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 84, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 8); }
    else if (blockId == 38) { color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 75, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 73, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 9); }
    else if (blockId == 39) { color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 68, nameColor, namePx, nameAdvance, 7); }
    else if (blockId == 40) { color = hudDrawCharAt(color, screenPos, nameOrigin, 74, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 85, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 4); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 5); color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 6); color = hudDrawCharAt(color, screenPos, nameOrigin, 71, nameColor, namePx, nameAdvance, 7); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 8); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 9); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 10); color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 11); }
    else if (blockId == 41) { color = hudDrawCharAt(color, screenPos, nameOrigin, 83, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 72, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); }
    else if (blockId == 42) { color = hudDrawCharAt(color, screenPos, nameOrigin, 70, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 82, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 78, nameColor, namePx, nameAdvance, 3); }
    else if (blockId == 43) { color = hudDrawCharAt(color, screenPos, nameOrigin, 65, nameColor, namePx, nameAdvance, 0); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 1); color = hudDrawCharAt(color, screenPos, nameOrigin, 80, nameColor, namePx, nameAdvance, 2); color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 3); color = hudDrawCharAt(color, screenPos, nameOrigin, 69, nameColor, namePx, nameAdvance, 4); }
    else if (blockId > 0) {
        color = hudDrawCharAt(color, screenPos, nameOrigin, 66, nameColor, namePx, nameAdvance, 0);
        color = hudDrawCharAt(color, screenPos, nameOrigin, 76, nameColor, namePx, nameAdvance, 1);
        color = hudDrawCharAt(color, screenPos, nameOrigin, 79, nameColor, namePx, nameAdvance, 2);
        color = hudDrawCharAt(color, screenPos, nameOrigin, 67, nameColor, namePx, nameAdvance, 3);
        color = hudDrawCharAt(color, screenPos, nameOrigin, 75, nameColor, namePx, nameAdvance, 4);
        color = hudDrawCharAt(color, screenPos, nameOrigin, 32, nameColor, namePx, nameAdvance, 5);
        int tens = blockId / 10;
        int ones = blockId % 10;
        if (tens > 0) {
            color = hudDrawCharAt(color, screenPos, nameOrigin, 48 + tens, nameColor, namePx, nameAdvance, 6);
            color = hudDrawCharAt(color, screenPos, nameOrigin, 48 + ones, nameColor, namePx, nameAdvance, 7);
        } else {
            color = hudDrawCharAt(color, screenPos, nameOrigin, 48 + ones, nameColor, namePx, nameAdvance, 6);
        }
    }
    return color;
}

vec3 hudDrawXpBar(vec3 color, vec2 screenPos, vec2 origin, vec2 size, float hudScale) {
    if (!hudInRect(screenPos, origin, size)) {
        return color;
    }

    vec2 local = screenPos - origin;
    float frame = max(1.0, floor(hudScale * 0.45));
    color = vec3(0.0);
    if (local.x > frame && local.x < size.x - frame && local.y > frame && local.y < size.y - frame) {
        color = vec3(0.50, 0.78, 0.10);
    }

    if (hudOnBorder(local, size, frame)) {
        bool topLeftEdge = local.x < frame * 1.5 || local.y < frame * 1.5;
        color = topLeftEdge ? HUD_MC_BORDER : HUD_MC_SHADOW;
    }

    return color;
}

vec3 applyHud(vec3 color, vec2 screenPos) {
    vec2 center = ubo.viewportSize * 0.5;
    float hudScale = hudUiScale();
    int flags = hudFlagBits();
    bool mainMenu = (flags & 16) != 0;
    bool inventoryOpen = (flags & 2) != 0;
    bool isDead = (flags & 8) != 0;
    bool showOxygen = (flags & 4) != 0;
    bool oxygenLow = (flags & 32) != 0;
    bool pickupFlash = (flags & 512) != 0;
    bool inventoryFull = (flags & 256) != 0;

    if (mainMenu) {
        return color;
    }

    if (pickupFlash && !inventoryOpen && !isDead) {
        color = mix(color, vec3(0.82, 1.0, 0.68), 0.14);
    }

    float breakAmt = clamp(ubo.breakProgress, 0.0, 1.0);
    if (!isDead && breakAmt > 0.001 && !inventoryOpen) {
        color = hudDrawBreakProgressWheel(color, screenPos, center, breakAmt, hudScale);
    }

    float hotbarWidth = floor(HUD_HOTBAR_WIDTH * hudScale);
    float hotbarHeight = floor(HUD_HOTBAR_HEIGHT * hudScale);
    float slotSize = floor(hotbarWidth / 9.0);
    hotbarWidth = slotSize * 9.0;
    vec2 hotbarOrigin = vec2(floor(center.x - hotbarWidth * 0.5), floor(ubo.viewportSize.y - hotbarHeight - 2.0 * hudScale));
    float xpBarHeight = max(2.0, floor(HUD_XP_BAR_HEIGHT * hudScale));
    float xpGap = max(1.0, floor(1.0 * hudScale));
    vec2 xpBarOrigin = vec2(floor(hotbarOrigin.x), floor(hotbarOrigin.y - xpBarHeight - xpGap));
    vec2 xpBarSize = vec2(hotbarWidth, xpBarHeight);

    if (!isDead && !inventoryOpen) {
        color = hudDrawXpBar(color, screenPos, xpBarOrigin, xpBarSize, hudScale);

        const int iconsPerRow = 10;
        float iconSize = hudIconSize();
        float iconStep = hudIconStep();
        float rowGap = max(1.0, floor(1.0 * hudScale));
        float rowY = floor(xpBarOrigin.y - iconSize - rowGap);
        float rowWidth = float(iconsPerRow) * iconStep;
        vec2 heartsOrigin = vec2(floor(hotbarOrigin.x), rowY);
        vec2 foodOrigin = vec2(floor(hotbarOrigin.x + hotbarWidth - rowWidth), rowY);

        color = hudDrawSurvivalRow(color, screenPos, heartsOrigin, 0, ubo.survivalHud.x, iconsPerRow, false);
        color = hudDrawSurvivalRow(color, screenPos, foodOrigin, 1, ubo.survivalHud.y, iconsPerRow, false);

        if (showOxygen) {
            color = hudDrawSurvivalRow(color, screenPos, vec2(heartsOrigin.x, rowY - iconSize - rowGap), 2, ubo.survivalHud.z, iconsPerRow, oxygenLow);
        }
    }

    if (!inventoryOpen) {
        vec2 hotbarSize = vec2(hotbarWidth, hotbarHeight);
        if (hudInRect(screenPos, hotbarOrigin, hotbarSize)) {
            vec2 hotbarLocal = screenPos - hotbarOrigin;
            float frame = max(1.0, floor(hudScale * 0.5));
            if (hudOnBorder(hotbarLocal, hotbarSize, frame)) {
                bool topLeftEdge = hotbarLocal.x < frame * 1.5 || hotbarLocal.y < frame * 1.5;
                color = topLeftEdge ? HUD_MC_BORDER : HUD_MC_SHADOW;
            }
        }

        int selected = int(round(ubo.survivalHud.w * 8.0));
        for (int slot = 0; slot < 9; slot++) {
            vec2 slotOrigin = hotbarOrigin + vec2(float(slot) * slotSize, 0.0);
            vec2 slotSizeVec = vec2(slotSize, hotbarHeight);
            if (!hudInRect(screenPos, slotOrigin, slotSizeVec)) {
                continue;
            }

            bool selectedSlot = slot == selected;
            bool drawDivider = slot < 8;
            color = hudDrawHotbarSlot(color, screenPos, slotOrigin, slotSizeVec, false, hudScale, drawDivider);
            color = hudDrawHotbarSlotNumber(color, screenPos, slotOrigin, slotSizeVec, slot + 1, hudScale);
            color = hudDrawPackedSlotContents(color, screenPos, slotOrigin, slotSizeVec, inventory.slots[slot], hudScale);
            if (selectedSlot) {
                color = hudDrawHotbarSelection(color, screenPos, slotOrigin, slotSizeVec, hudScale);
            }
        }
    }

    if (!inventoryOpen && !isDead) {
        color = hudDrawCrosshair(color, screenPos, center, hudScale);
    }

    if (!inventoryOpen && !isDead && ubo.hasTarget > 0.5 && ubo.breakingBlockTexture > 0.5 && ubo.breakProgress < 0.01) {
        int blockId = int(ubo.breakingBlockTexture + 0.5);
        float namePx = 2.1 * hudScale;
        float nameAdvance = namePx * 5.0 + 3.0;
        int nameLen = hudBlockNameLength(blockId);
        float iconSize = 14.0 * hudScale;
        float panelW = (iconSize + 9.0 * hudScale + float(nameLen) * nameAdvance + 6.0 * hudScale);
        float panelH = 20.0 * hudScale;
        vec2 panelOrigin = vec2(floor(center.x - panelW * 0.5), floor(hotbarOrigin.y - panelH - 28.0 * hudScale));
        if (hudInRect(screenPos, panelOrigin, vec2(panelW, panelH))) {
            color = mix(color, vec3(0.04, 0.04, 0.06), 0.82);
            vec2 panelLocal = screenPos - panelOrigin;
            if (hudOnBorder(panelLocal, vec2(panelW, panelH), max(1.0, hudScale * 0.5))) {
                color = mix(color, vec3(0.55, 0.55, 0.58), 0.9);
            }
        }
        vec2 iconOrigin = panelOrigin + vec2(3.0 * hudScale, (panelH - iconSize) * 0.5);
        if (hudInRect(screenPos, iconOrigin, vec2(iconSize, iconSize))) {
            vec3 iconColor = hudBlockColor(blockId);
            color = mix(color, iconColor, 0.96);
        }
        vec3 nameColor = vec3(0.92, 0.94, 0.98);
        vec2 nameOrigin = panelOrigin + vec2(iconSize + 6.0 * hudScale, floor((panelH - namePx * 7.0) * 0.5));
        color = hudDrawBlockName(color, screenPos, nameOrigin, blockId, nameColor, namePx, nameAdvance);
    }

    if (inventoryFull && !inventoryOpen && !isDead) {
        float hintPx = 2.2 * hudScale;
        float hintAdvance = hintPx * 5.0 + 3.0;
        float hintWidth = hintAdvance * 14.0 - 3.0;
        vec2 hintOrigin = vec2(floor(center.x - hintWidth * 0.5), floor(hotbarOrigin.y - 52.0 * hudScale));
        vec3 hintColor = vec3(0.98, 0.55, 0.42);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 73, hintColor, hintPx, hintAdvance, 0);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 78, hintColor, hintPx, hintAdvance, 1);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 86, hintColor, hintPx, hintAdvance, 2);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 69, hintColor, hintPx, hintAdvance, 3);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 78, hintColor, hintPx, hintAdvance, 4);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 84, hintColor, hintPx, hintAdvance, 5);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 79, hintColor, hintPx, hintAdvance, 6);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 82, hintColor, hintPx, hintAdvance, 7);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 89, hintColor, hintPx, hintAdvance, 8);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 32, hintColor, hintPx, hintAdvance, 9);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 70, hintColor, hintPx, hintAdvance, 10);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 85, hintColor, hintPx, hintAdvance, 11);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 76, hintColor, hintPx, hintAdvance, 12);
        color = hudDrawCharAt(color, screenPos, hintOrigin, 76, hintColor, hintPx, hintAdvance, 13);
    }

    return color;
}

float distanceToAabbSurface(vec3 point, vec3 boxMin, vec3 boxMax) {
    vec3 center = (boxMin + boxMax) * 0.5;
    vec3 halfExtents = (boxMax - boxMin) * 0.5;
    vec3 offset = abs(point - center) - halfExtents;
    vec3 outside = max(offset, vec3(0.0));
    float outsideDistance = length(outside);
    float insideDistance = min(max(offset.x, max(offset.y, offset.z)), 0.0);
    return outsideDistance - insideDistance;
}

float distToSegment(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a;
    vec2 ba = b - a;
    float denom = max(dot(ba, ba), 0.0001);
    float h = clamp(dot(pa, ba) / denom, 0.0, 1.0);
    return length(pa - ba * h);
}

vec2 blockFaceUv(vec3 rel, float distToShell, float edgeX, float edgeY, float edgeZ) {
    if (abs(distToShell - edgeX) < 0.0005) {
        return rel.yz;
    }
    if (abs(distToShell - edgeY) < 0.0005) {
        return rel.xz;
    }
    return rel.xy;
}

vec3 applyTargetBlockOutline(vec3 color, vec3 worldPos) {
    if (ubo.hasTarget < 0.5) {
        return color;
    }

    vec3 blockMin = ubo.targetBlockMin;
    vec3 rel = worldPos - blockMin;
    vec3 oneMinus = vec3(1.0) - rel;
    float edgeX = min(rel.x, oneMinus.x);
    float edgeY = min(rel.y, oneMinus.y);
    float edgeZ = min(rel.z, oneMinus.z);
    float distToShell = min(edgeX, min(edgeY, edgeZ));
    if (distToShell > 0.035) {
        return color;
    }

    float edgeAlongFace = edgeX + edgeY + edgeZ - distToShell - max(edgeX, max(edgeY, edgeZ));
    const float outlineWidth = 0.020;
    if (edgeAlongFace < outlineWidth) {
        float outlineT = 1.0 - smoothstep(0.0, outlineWidth, edgeAlongFace);
        color = mix(color, vec3(0.02, 0.02, 0.03), outlineT * 0.92);
    }

    if (distToShell > 0.028) {
        return color;
    }

    float breakAmt = clamp(ubo.breakProgress, 0.0, 1.0);
    if (breakAmt > 0.01) {
        int crackStage = min(int(floor(breakAmt * 10.0)), 10);
        vec2 faceUv = blockFaceUv(rel, distToShell, edgeX, edgeY, edgeZ);
        float crackMask = 0.0;
        for (int stage = 0; stage < 10; stage++) {
            if (stage >= crackStage) {
                break;
            }
            float seed = float(stage) * 2.17 + ubo.breakingBlockTexture * 0.31;
            vec2 crackA = vec2(fract(sin(seed * 12.9898) * 43758.5453), fract(sin(seed * 78.233) * 43758.5453));
            vec2 crackB = vec2(fract(sin(seed * 45.164) * 43758.5453), fract(sin(seed * 91.173) * 43758.5453));
            float lineDist = distToSegment(faceUv, crackA, crackB);
            float lineWidth = 0.012 + float(stage) * 0.0015;
            crackMask = max(crackMask, smoothstep(lineWidth, lineWidth * 0.25, lineDist));
        }
        vec3 blockTint = texture(blockTextures, vec3(0.5, 0.5, ubo.breakingBlockTexture)).rgb;
        vec3 crackColor = blockTint * 0.28;
        color = mix(color, crackColor, crackMask * (0.45 + breakAmt * 0.4));
    }

    if (ubo.breakBurstTimer > 0.01) {
        vec3 blockCenter = ubo.targetBlockMin + vec3(0.5);
        float burstT = clamp(ubo.breakBurstTimer / 0.4, 0.0, 1.0);
        float dist = distance(worldPos, blockCenter);
        float shard = noise3D(worldPos * 11.0 + vec3((1.0 - burstT) * 18.0));
        float burstShell = smoothstep(1.1, 0.2, dist) * (1.0 - burstT);
        float burstSpark = step(0.78, shard) * burstShell * 0.7;
        vec3 burstColor = texture(blockTextures, vec3(0.5, 0.5, ubo.breakingBlockTexture)).rgb;
        color = mix(color, burstColor * 1.25, burstSpark);
    }

    return color;
}

vec3 applyInventoryOverlay(vec3 color, vec2 screenPos) {
    if ((hudFlagBits() & 2) == 0) {
        return color;
    }

    color *= 0.42;
    vec2 center = ubo.viewportSize * 0.5;
    int selected = int(round(ubo.survivalHud.w * 8.0));
    float hudScale = hudUiScale();

    float panelW = 176.0 * hudScale;
    float panelH = 166.0 * hudScale;
    float slotSize = 18.0 * hudScale;
    float marginX = 8.0 * hudScale;
    float storageTop = 17.0 * hudScale;
    float hotbarY = 141.0 * hudScale;
    vec2 panelOrigin = floor(center - vec2(panelW, panelH) * 0.5);

    vec2 panelLocal = screenPos - panelOrigin;
    if (hudInRect(screenPos, panelOrigin, vec2(panelW, panelH))) {
        vec3 panelFill = vec3(0.388, 0.388, 0.388);
        color = mix(color, panelFill, 0.94);
        float frame = max(1.0, floor(hudScale * 0.55));
        if (hudOnBorder(panelLocal, vec2(panelW, panelH), frame)) {
            bool topLeft = panelLocal.x < frame * 1.5 || panelLocal.y < frame * 1.5;
            vec3 borderColor = topLeft ? vec3(0.545, 0.545, 0.545) : vec3(0.219, 0.219, 0.219);
            color = mix(color, borderColor, 0.96);
        }
    }

    float titlePx = max(2.0, floor(hudScale * 1.35));
    float titleAdvance = titlePx * 5.0 + 3.0;
    float titleWidth = titleAdvance * 9.0 - 3.0;
    vec2 titleOrigin = panelOrigin + vec2(floor((panelW - titleWidth) * 0.5), floor(6.0 * hudScale));
    vec3 titleColor = vec3(0.22, 0.22, 0.22);
    color = hudDrawText(color, screenPos, titleOrigin, 73, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 78, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 86, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 69, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 78, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 84, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 79, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 7.0, 0.0), 82, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 8.0, 0.0), 89, titleColor, titlePx);

    for (int row = 0; row < 4; row++) {
        float rowY = row < 3 ? storageTop + float(row) * slotSize : hotbarY;
        for (int col = 0; col < 9; col++) {
            vec2 slotOrigin = vec2(panelOrigin.x + marginX + float(col) * slotSize, panelOrigin.y + rowY);
            if (!hudInRect(screenPos, slotOrigin, vec2(slotSize, slotSize))) {
                continue;
            }

            int slotIndex = inventorySlotIndex(row, col);
            int packed = inventory.slots[slotIndex];
            bool isHotbarRow = row == 3;
            bool isSelected = isHotbarRow && col == selected;

            color = hudDrawInventorySlot(color, screenPos, slotOrigin, vec2(slotSize, slotSize), isSelected, hudScale);
            color = hudDrawPackedSlotContents(color, screenPos, slotOrigin, vec2(slotSize, slotSize), packed, hudScale);
        }
    }

    float craftLeft = 98.0 * hudScale;
    float craftTop = 18.0 * hudScale;
    for (int craftRow = 0; craftRow < 2; craftRow++) {
        for (int craftCol = 0; craftCol < 2; craftCol++) {
            vec2 craftOrigin = panelOrigin + vec2(craftLeft + float(craftCol) * slotSize, craftTop + float(craftRow) * slotSize);
            color = hudDrawInventorySlot(color, screenPos, craftOrigin, vec2(slotSize, slotSize), false, hudScale);
        }
    }

    return color;
}

vec3 applyMainMenuOverlay(vec3 color, vec2 screenPos) {
    if ((hudFlagBits() & 16) == 0) {
        return color;
    }

    color *= 0.22;
    vec2 center = ubo.viewportSize * 0.5;
    int selected = int(round(ubo.survivalHud.w * 2.0));

    vec2 panelSize = vec2(420.0, 280.0);
    vec2 panelOrigin = center - panelSize * 0.5;
    vec2 panelLocal = screenPos - panelOrigin;
    if (hudInRect(screenPos, panelOrigin, panelSize)) {
        color = mix(color, vec3(0.05, 0.08, 0.14), 0.9);
        float vignette = smoothstep(panelSize.x * 0.5, panelSize.x * 0.15, abs(panelLocal.x - panelSize.x * 0.5));
        vignette *= smoothstep(panelSize.y * 0.5, panelSize.y * 0.15, abs(panelLocal.y - panelSize.y * 0.5));
        color = mix(color, vec3(0.10, 0.16, 0.28), vignette * 0.4);
        if (hudOnBorder(panelLocal, panelSize, 3.0)) {
            color = mix(color, vec3(0.42, 0.60, 0.88), 0.94);
        }
    }

    float titlePx = 5.0;
    float titleAdvance = titlePx * 5.0 + 6.0;
    float titleWidth = titleAdvance * 10.0 - 6.0;
    vec2 titleOrigin = center - vec2(titleWidth * 0.5, 88.0);
    vec3 titleColor = vec3(0.94, 0.97, 1.0);
    color = hudDrawText(color, screenPos, titleOrigin, 65, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 83, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 84, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 82, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 79, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 67, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 82, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 7.0, 0.0), 65, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 8.0, 0.0), 70, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 9.0, 0.0), 84, titleColor, titlePx);

    float optionPx = 3.2;
    float optionAdvance = optionPx * 5.0 + 5.0;
    float optionWidth = optionAdvance * 10.0;
    vec3 optionColor = vec3(0.78, 0.86, 0.96);
    vec3 selectedColor = vec3(0.96, 0.99, 1.0);

    for (int i = 0; i < 3; i++) {
        vec2 optionOrigin = center - vec2(optionWidth * 0.5, 20.0 - float(i) * 44.0);
        vec2 optionSize = vec2(optionWidth + 24.0, optionPx * 7.0 + 16.0);
        vec2 optionPanelOrigin = optionOrigin - vec2(12.0, 8.0);
        bool isSelected = i == selected;
        vec3 textColor = isSelected ? selectedColor : optionColor;

        if (hudInRect(screenPos, optionPanelOrigin, optionSize)) {
            vec3 fill = isSelected ? vec3(0.14, 0.22, 0.36) : vec3(0.08, 0.12, 0.20);
            color = mix(color, fill, isSelected ? 0.88 : 0.55);
            vec2 optLocal = screenPos - optionPanelOrigin;
            if (hudOnBorder(optLocal, optionSize, isSelected ? 2.5 : 1.5)) {
                color = mix(color, isSelected ? vec3(0.55, 0.72, 0.95) : vec3(0.28, 0.38, 0.55), 0.9);
            }
        }

        if (i == 0) {
            color = hudDrawText(color, screenPos, optionOrigin, 80, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 76, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 65, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 89, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 32, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 76, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 79, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), 67, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 8.0, 0.0), 65, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 9.0, 0.0), 76, textColor, optionPx);
        } else if (i == 1) {
            color = hudDrawText(color, screenPos, optionOrigin, 66, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 82, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 79, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 87, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 83, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 69, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 32, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), 76, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 8.0, 0.0), 65, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 9.0, 0.0), 78, textColor, optionPx);
        } else {
            color = hudDrawText(color, screenPos, optionOrigin, 81, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 85, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 73, textColor, optionPx);
            color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 84, textColor, optionPx);
        }
    }

    float hintPx = 2.2;
    float hintAdvance = hintPx * 5.0 + 4.0;
    float hintWidth = hintAdvance * 11.0;
    vec2 hintOrigin = center - vec2(hintWidth * 0.5, -108.0);
    vec3 hintColor = vec3(0.58, 0.68, 0.82);
    color = hudDrawText(color, screenPos, hintOrigin, 85, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance, 0.0), 80, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 2.0, 0.0), 68, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 3.0, 0.0), 79, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 4.0, 0.0), 87, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 5.0, 0.0), 78, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 6.0, 0.0), 32, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 7.0, 0.0), 69, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 8.0, 0.0), 78, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 9.0, 0.0), 84, hintColor, hintPx);
    color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 10.0, 0.0), 69, hintColor, hintPx);

    return color;
}

vec3 hudDrawSettingSlider(vec3 color, vec2 screenPos, vec2 origin, vec2 size, float value, bool selected) {
    if (!hudInRect(screenPos, origin, size)) {
        return color;
    }

    vec3 fill = selected ? vec3(0.34, 0.34, 0.34) : vec3(0.18, 0.18, 0.18);
    color = mix(color, fill, 0.92);
    vec2 local = screenPos - origin;
    float knobX = clamp(value, 0.0, 1.0) * (size.x - 8.0) + 4.0;
    if (abs(local.x - knobX) < 4.0 && local.y > size.y * 0.55) {
        color = mix(color, vec3(0.92, 0.92, 0.95), 0.95);
    } else if (local.y > size.y * 0.55 && local.x < knobX) {
        color = mix(color, vec3(0.48, 0.72, 0.95), 0.85);
    }
    return color;
}

vec3 applyPlacementGhost(vec3 color, vec3 worldPos) {
    if (ubo.ghostActive < 0.5) {
        return color;
    }

    vec3 rel = worldPos - ubo.ghostBlockMin;
    if (any(lessThan(rel, vec3(0.0))) || any(greaterThanEqual(rel, vec3(1.0)))) {
        return color;
    }

    vec3 ghostColor = ubo.ghostValid > 0.5 ? vec3(0.2, 0.9, 0.35) : vec3(0.92, 0.25, 0.2);
    vec3 blockTint = texture(blockTextures, vec3(0.5, 0.5, ubo.ghostTexture)).rgb;
    ghostColor = mix(ghostColor, blockTint, 0.45);
    float edge = min(min(min(rel.x, 1.0 - rel.x), min(rel.y, 1.0 - rel.y)), min(rel.z, 1.0 - rel.z));
    float alpha = mix(0.22, 0.42, smoothstep(0.12, 0.0, edge));
    return mix(color, ghostColor, alpha);
}

vec3 applyHeldItem(vec3 color, vec2 screenPos) {
    if (ubo.hasHeldItem < 0.5 || (hudFlagBits() & 2) != 0 || (hudFlagBits() & 1) != 0 || (hudFlagBits() & 1024) != 0) {
        return color;
    }

    float hudScale = hudUiScale();
    float itemSize = 56.0 * hudScale;
    vec2 origin = vec2(ubo.viewportSize.x - itemSize - 14.0 * hudScale, ubo.viewportSize.y - itemSize - 14.0 * hudScale);
    if (!hudInRect(screenPos, origin, vec2(itemSize, itemSize))) {
        return color;
    }

    vec2 uv = (screenPos - origin) / itemSize;
    vec3 texColor = texture(blockTextures, vec3(uv, ubo.heldItemTexture)).rgb;
    return mix(color, texColor, 0.94);
}

vec3 applyPauseOverlay(vec3 color, vec2 screenPos) {
    if ((hudFlagBits() & 1) == 0) {
        return color;
    }

    color *= 0.52;
    color = mix(color, vec3(0.0), 0.46);

    vec2 center = ubo.viewportSize * 0.5;
    bool settingsOpen = (hudFlagBits() & 64) != 0;
    int selected = int(round(ubo.survivalHud.w * 2.0));

    float titlePx = 4.0;
    float titleAdvance = titlePx * 5.0 + 5.0;
    vec3 titleColor = vec3(0.96, 0.96, 0.96);
    if (settingsOpen) {
        float titleWidth = titleAdvance * 8.0 - 5.0;
        vec2 titleOrigin = center - vec2(titleWidth * 0.5, 88.0);
        color = hudDrawText(color, screenPos, titleOrigin, 83, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 69, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 84, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 84, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 73, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 78, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 71, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 7.0, 0.0), 83, titleColor, titlePx);
    } else {
        float titleWidth = titleAdvance * 9.0 - 5.0;
        vec2 titleOrigin = center - vec2(titleWidth * 0.5, 88.0);
        color = hudDrawText(color, screenPos, titleOrigin, 71, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 65, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 77, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 69, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 32, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 77, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 69, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 7.0, 0.0), 78, titleColor, titlePx);
        color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 8.0, 0.0), 85, titleColor, titlePx);
    }

    float optionPx = 3.2;
    float optionAdvance = optionPx * 5.0 + 5.0;
    float optionWidth = optionAdvance * 10.0;
    vec3 optionColor = vec3(0.72, 0.72, 0.72);
    vec3 selectedColor = vec3(1.0, 1.0, 1.0);

    if (!settingsOpen) {
        for (int i = 0; i < 3; i++) {
            vec2 optionOrigin = center - vec2(optionWidth * 0.5, 20.0 - float(i) * 44.0);
            vec2 optionSize = vec2(optionWidth + 24.0, optionPx * 7.0 + 16.0);
            vec2 optionPanelOrigin = optionOrigin - vec2(12.0, 8.0);
            bool isSelected = i == selected;
            vec3 textColor = isSelected ? selectedColor : optionColor;

            if (hudInRect(screenPos, optionPanelOrigin, optionSize)) {
                vec3 fill = isSelected ? vec3(0.34, 0.34, 0.34) : vec3(0.18, 0.18, 0.18);
                color = mix(color, fill, 0.92);
                vec2 optLocal = screenPos - optionPanelOrigin;
                if (hudOnBorder(optLocal, optionSize, isSelected ? 2.0 : 1.5)) {
                    color = mix(color, isSelected ? vec3(0.95, 0.95, 0.95) : vec3(0.08, 0.08, 0.08), 0.95);
                }
            }

            if (i == 0) {
                color = hudDrawText(color, screenPos, optionOrigin, 82, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 69, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 83, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 85, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 77, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 69, textColor, optionPx);
            } else if (i == 1) {
                color = hudDrawText(color, screenPos, optionOrigin, 83, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 69, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 84, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 84, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 73, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 78, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 71, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), 83, textColor, optionPx);
            } else {
                color = hudDrawText(color, screenPos, optionOrigin, 68, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 73, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 83, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 67, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 79, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 78, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 78, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), 69, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 8.0, 0.0), 67, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 9.0, 0.0), 84, textColor, optionPx);
            }
        }
    } else {
        float fovValue = clamp(ubo.survivalHud.x, 0.0, 1.0);
        float sensitivityValue = clamp(ubo.survivalHud.y, 0.0, 1.0);
        bool invertY = ubo.survivalHud.z > 0.5;

        for (int i = 0; i < 4; i++) {
            vec2 optionOrigin = center - vec2(optionWidth * 0.5, 20.0 - float(i) * 44.0);
            vec2 optionSize = vec2(optionWidth + 24.0, optionPx * 7.0 + 16.0);
            vec2 optionPanelOrigin = optionOrigin - vec2(12.0, 8.0);
            bool isSelected = i == selected;
            vec3 textColor = isSelected ? selectedColor : optionColor;

            if (hudInRect(screenPos, optionPanelOrigin, optionSize)) {
                vec3 fill = isSelected ? vec3(0.34, 0.34, 0.34) : vec3(0.18, 0.18, 0.18);
                color = mix(color, fill, 0.92);
                vec2 optLocal = screenPos - optionPanelOrigin;
                if (hudOnBorder(optLocal, optionSize, isSelected ? 2.0 : 1.5)) {
                    color = mix(color, isSelected ? vec3(0.95, 0.95, 0.95) : vec3(0.08, 0.08, 0.08), 0.95);
                }
            }

            if (i == 0) {
                color = hudDrawText(color, screenPos, optionOrigin, 70, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 79, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 86, textColor, optionPx);
                vec2 sliderOrigin = optionOrigin + vec2(optionAdvance * 4.0, optionPx * 2.0);
                color = hudDrawSettingSlider(color, screenPos, sliderOrigin, vec2(120.0, optionPx * 7.0 + 8.0), fovValue, isSelected);
            } else if (i == 1) {
                color = hudDrawText(color, screenPos, optionOrigin, 77, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 111, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 117, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 115, textColor, optionPx);
                vec2 sliderOrigin = optionOrigin + vec2(optionAdvance * 5.0, optionPx * 2.0);
                color = hudDrawSettingSlider(color, screenPos, sliderOrigin, vec2(120.0, optionPx * 7.0 + 8.0), sensitivityValue, isSelected);
            } else if (i == 2) {
                color = hudDrawText(color, screenPos, optionOrigin, 73, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 110, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 118, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 101, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 114, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 116, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 32, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), invertY ? 79 : 78, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 8.0, 0.0), 70, textColor, optionPx);
            } else {
                color = hudDrawText(color, screenPos, optionOrigin, 66, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 97, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 99, textColor, optionPx);
                color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 107, textColor, optionPx);
            }
        }
    }

    float hintPx = 2.2;
    float hintAdvance = hintPx * 5.0 + 4.0;
    vec3 hintColor = vec3(0.52, 0.52, 0.52);
    if (settingsOpen) {
        float hintWidth = hintAdvance * 13.0;
        vec2 hintOrigin = center - vec2(hintWidth * 0.5, -108.0);
        color = hudDrawText(color, screenPos, hintOrigin, 69, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance, 0.0), 115, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 2.0, 0.0), 67, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 3.0, 0.0), 32, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 4.0, 0.0), 116, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 5.0, 0.0), 111, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 6.0, 0.0), 32, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 7.0, 0.0), 114, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 8.0, 0.0), 101, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 9.0, 0.0), 116, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 10.0, 0.0), 117, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 11.0, 0.0), 114, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 12.0, 0.0), 110, hintColor, hintPx);
    } else {
        float hintWidth = hintAdvance * 11.0;
        vec2 hintOrigin = center - vec2(hintWidth * 0.5, -108.0);
        color = hudDrawText(color, screenPos, hintOrigin, 85, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance, 0.0), 80, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 2.0, 0.0), 68, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 3.0, 0.0), 79, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 4.0, 0.0), 87, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 5.0, 0.0), 78, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 6.0, 0.0), 32, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 7.0, 0.0), 69, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 8.0, 0.0), 78, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 9.0, 0.0), 84, hintColor, hintPx);
        color = hudDrawText(color, screenPos, hintOrigin + vec2(hintAdvance * 10.0, 0.0), 69, hintColor, hintPx);
    }

    return color;
}

// Raised from 0.36 towards the conventional 0.5 alpha-test cutoff used by cutout foliage so the
// large band of ambiguous mid-alpha texels (previously 92-255 passed) shrinks; fewer near-threshold
// texels means less flicker/"z-fighting"-like popping between overlapping leaf faces as the view
// angle or mip level changes, and less sky visibly bleeding through half-transparent leaf pixels.
const float LEAVES_ALPHA_CUTOFF = 0.5;

bool isFluidTile(int texIdx) {
    return texIdx == 5 || texIdx == 6 || texIdx == 28;
}

vec2 fluidScrollUv(vec2 uv, int texIdx) {
    float t = ubo.timeOfDay * 24.0;
    if (texIdx == 5) {
        vec2 flow = vec2(0.048, 0.032);
        vec2 ripple = vec2(
            sin(t * 1.45 + uv.y * 9.5 + uv.x * 4.2),
            cos(t * 1.15 + uv.x * 8.0 + uv.y * 5.5)) * 0.018;
        vec2 shimmer = vec2(
            sin(t * 2.2 + dot(uv, vec2(6.1, 4.3))),
            cos(t * 1.85 - dot(uv, vec2(5.4, 7.2)))) * 0.006;
        return uv + flow * t + ripple + shimmer;
    }

    vec2 flow = vec2(-0.035, 0.048);
    vec2 ripple = vec2(
        sin(t * 1.65 + uv.y * 11.0),
        cos(t * 1.25 + uv.x * 9.0)) * 0.014;
    return uv + flow * t + ripple;
}

vec3 tintFluidColor(vec3 texColor, int texIdx) {
    if (texIdx == 5) {
        vec3 waterBlue = vec3(0.10, 0.28, 0.58);
        vec3 waterDeep = vec3(0.04, 0.14, 0.38);
        vec3 tinted = mix(mix(texColor, waterDeep, 0.28), waterBlue, 0.48);
        return boostSaturation(tinted, 1.16);
    }
    if (texIdx == 28) {
        return mix(texColor, vec3(1.05, 0.42, 0.08), 0.35);
    }
    return mix(texColor, vec3(0.30, 0.58, 0.50), 0.38);
}

vec3 waterSurfaceNormal(vec3 baseNormal, vec3 worldPos) {
    float t = ubo.timeOfDay * 24.0;
    vec2 wavePos = worldPos.xz * 1.22;
    float wave1 = sin(t * 1.28 + wavePos.x * 2.6 + wavePos.y * 1.4);
    float wave2 = cos(t * 0.98 + wavePos.y * 3.1 - wavePos.x * 2.0);
    float wave3 = sin(t * 1.62 - wavePos.x * 3.8 + wavePos.y * 2.5) * 0.48;
    float ripple = noise2D(wavePos * 0.85 + vec2(t * 0.18, -t * 0.14)) * 0.35;
    vec3 perturb = vec3(
        (wave1 + ripple) * 0.016 + wave3 * 0.007,
        0.0,
        (wave2 - ripple) * 0.014 - wave3 * 0.006);
    return normalize(baseNormal + perturb);
}

vec3 applyFluidFresnel(vec3 litColor, vec3 worldPos, vec3 normal, int texIdx, float dist, vec3 fogColor, float fog) {
    vec3 viewDir = normalize(ubo.cameraPosition - worldPos);

    if (texIdx == 5) {
        vec3 n = waterSurfaceNormal(normalize(normal), worldPos);
        float ndv = max(dot(n, viewDir), 0.0);
        float f0 = 0.022;
        float fresnel = f0 + (1.0 - f0) * pow(1.0 - ndv, 5.0);
        vec3 reflectDir = reflect(-viewDir, n);

        float dayFactor = clamp(sunDirection().y * 1.6 + 0.35, 0.0, 1.0);
        vec3 skyReflection = waterSkyReflection(worldPos, reflectDir, dayFactor, dist);
        skyReflection = mix(skyReflection, fogColor, fog * 0.82);

        float depthAbsorb = clamp(dist / 28.0, 0.0, 1.0);
        vec3 shallowWater = mix(vec3(0.06, 0.24, 0.48), vec3(0.10, 0.34, 0.62), dayFactor);
        vec3 deepWater = mix(vec3(0.02, 0.10, 0.28), vec3(0.04, 0.18, 0.42), dayFactor);
        vec3 waterBody = mix(shallowWater, deepWater, depthAbsorb);
        vec3 baseColor = mix(litColor, waterBody, mix(0.32, 0.48, depthAbsorb));
        baseColor = boostSaturation(baseColor, 1.14);

        float grazing = pow(1.0 - ndv, 3.2);
        float reflectMix = clamp(fresnel * 0.92 + grazing * 0.22, 0.0, 0.94);
        vec3 color = mix(baseColor, skyReflection, reflectMix);

        float sunDir = max(dot(reflectDir, sunDirection()), 0.0);
        color += vec3(1.08, 0.98, 0.78) * pow(sunDir, 220.0) * dayFactor * (0.28 + grazing * 0.42);

        return mix(color, fogColor, fog * 0.36);
    }

    vec3 n = normalize(normal);
    float ndv = max(dot(n, viewDir), 0.0);

    float fresnel = pow(1.0 - ndv, 2.8);
    vec3 reflected = skyFogColor(worldPos);
    reflected = mix(reflected, fogColor, fog * 0.55);
    float depthFade = clamp((dist - 0.5) / 7.0, 0.0, 1.0);
    float seeThrough = fresnel * 0.62 + depthFade * 0.28;
    vec3 color = mix(litColor, reflected, seeThrough * 0.58);
    vec3 edgeTint = vec3(0.42, 0.74, 0.64);
    color = mix(color, edgeTint, fresnel * 0.42);
    return mix(color, fogColor, fog * 0.35);
}

vec3 applyJeiOverlay(vec3 color, vec2 screenPos) {
    if ((hudFlagBits() & 1024) == 0) {
        return color;
    }

    color *= 0.55;
    color = mix(color, vec3(0.0), 0.44);

    vec2 center = ubo.viewportSize * 0.5;
    float hudScale = hudUiScale();
    float titlePx = 4.0 * hudScale;
    float titleAdvance = titlePx * 5.0 + 5.0;
    vec3 titleColor = vec3(0.96, 0.96, 0.96);
    float titleWidth = titleAdvance * 7.0 - 5.0;
    vec2 titleOrigin = center - vec2(titleWidth * 0.5, 96.0 * hudScale);
    color = hudDrawText(color, screenPos, titleOrigin, 82, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 69, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 67, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 73, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 80, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 69, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 83, titleColor, titlePx);

    float optionPx = 3.2 * hudScale;
    float optionAdvance = optionPx * 5.0 + 5.0;
    float optionWidth = optionAdvance * 12.0;
    vec2 optionOrigin = center - vec2(optionWidth * 0.5, 12.0 * hudScale);
    vec2 optionSize = vec2(optionWidth + 24.0 * hudScale, optionPx * 7.0 + 16.0 * hudScale);
    vec2 optionPanelOrigin = optionOrigin - vec2(12.0 * hudScale, 8.0 * hudScale);

    if (hudInRect(screenPos, optionPanelOrigin, optionSize)) {
        color = mix(color, vec3(0.34, 0.34, 0.34), 0.92);
        vec2 optLocal = screenPos - optionPanelOrigin;
        if (hudOnBorder(optLocal, optionSize, max(2.0, hudScale))) {
            color = mix(color, vec3(0.95, 0.95, 0.95), 0.95);
        }
    }

    vec3 hintColor = vec3(0.78, 0.78, 0.78);
    color = hudDrawText(color, screenPos, optionOrigin, 85, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance, 0.0), 80, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 2.0, 0.0), 47, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 3.0, 0.0), 68, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 4.0, 0.0), 79, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 5.0, 0.0), 78, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 6.0, 0.0), 32, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 7.0, 0.0), 74, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 8.0, 0.0), 47, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 9.0, 0.0), 69, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 10.0, 0.0), 83, hintColor, optionPx);
    color = hudDrawText(color, screenPos, optionOrigin + vec2(optionAdvance * 11.0, 0.0), 67, hintColor, optionPx);

    return color;
}

vec3 applyDeathOverlay(vec3 color, vec2 screenPos) {
    if ((hudFlagBits() & 8) == 0) {
        return color;
    }

    color *= 0.22;
    vec2 center = ubo.viewportSize * 0.5;
    float hudScale = hudUiScale();
    vec2 panelSize = vec2(380.0 * hudScale, 200.0 * hudScale);
    vec2 panelLocal = screenPos - center;
    if (abs(panelLocal.x) < panelSize.x * 0.5 && abs(panelLocal.y) < panelSize.y * 0.5) {
        color = mix(color, vec3(0.1, 0.02, 0.02), 0.9);
        if (hudOnBorder(abs(panelLocal), panelSize, max(2.0, hudScale))) {
            color = mix(color, vec3(0.85, 0.2, 0.2), 0.92);
        }
    }

    float titlePx = 4.2 * hudScale;
    float titleAdvance = titlePx * 5.0 + 5.0;
    float titleWidth = titleAdvance * 7.0 + titlePx * 5.0;
    vec2 titleOrigin = center - vec2(titleWidth * 0.5, 42.0 * hudScale);
    vec3 titleColor = vec3(0.98, 0.35, 0.35);
    color = hudDrawText(color, screenPos, titleOrigin, 89, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance, 0.0), 121, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 2.0, 0.0), 117, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 3.0, 0.0), 32, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 4.0, 0.0), 68, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 5.0, 0.0), 105, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 6.0, 0.0), 101, titleColor, titlePx);
    color = hudDrawText(color, screenPos, titleOrigin + vec2(titleAdvance * 7.0, 0.0), 100, titleColor, titlePx);

    float subPx = 2.4 * hudScale;
    float subAdvance = subPx * 5.0 + 4.0;
    float subWidth = subAdvance * 10.0 - 4.0;
    vec2 subOrigin = center - vec2(subWidth * 0.5, -8.0 * hudScale);
    vec3 subColor = vec3(0.78, 0.8, 0.86);
    color = hudDrawText(color, screenPos, subOrigin, 82, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance, 0.0), 69, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 2.0, 0.0), 83, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 3.0, 0.0), 80, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 4.0, 0.0), 65, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 5.0, 0.0), 87, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 6.0, 0.0), 78, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 7.0, 0.0), 32, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 8.0, 0.0), 73, subColor, subPx);
    color = hudDrawText(color, screenPos, subOrigin + vec2(subAdvance * 9.0, 0.0), 78, subColor, subPx);

    int seconds = max(1, int(ceil(ubo.breakProgress)));
    int tens = seconds / 10;
    int ones = seconds % 10;
    float countPx = 5.5 * hudScale;
    float digitAdvance = countPx * 5.0 + 6.0;
    float countWidth = tens > 0 ? digitAdvance * 2.0 - 6.0 : countPx * 5.0;
    vec2 countOrigin = center - vec2(countWidth * 0.5, -34.0 * hudScale);
    vec3 countColor = vec3(0.95, 0.95, 0.98);
    if (tens > 0) {
        color = hudDrawText(color, screenPos, countOrigin, 48 + tens, countColor, countPx);
        color = hudDrawText(color, screenPos, countOrigin + vec2(digitAdvance, 0.0), 48 + ones, countColor, countPx);
    } else {
        color = hudDrawText(color, screenPos, countOrigin, 48 + ones, countColor, countPx);
    }

    return color;
}

void main() {
    vec3 color;

    if (fragTextureIndex < -0.5) {
        color = sampleSky(normalize(fragRayDir));
    } else {
        int texIdx = int(fragTextureIndex + 0.5);
        vec2 sampleUv = isFluidTile(texIdx)
            ? fluidScrollUv(fragUv, texIdx)
            : variantBlockUv(fragUv, fragWorldPos, texIdx);
        vec4 tex = texture(blockTextures, vec3(sampleUv, fragTextureIndex));

        if (isAlphaCutoutTile(texIdx) && tex.a < LEAVES_ALPHA_CUTOFF) {
            discard;
        }

        float dist = distance(fragWorldPos, ubo.cameraPosition);
        float dayFactor = clamp(sunDirection().y * 1.6 + 0.35, 0.0, 1.0);
        vec3 fogColor = terrainFogColor(dayFactor);
        float skyBlend = clamp((dist - 4.0) / 78.0, 0.0, 1.0) * dayFactor;
        fogColor = mix(fogColor, distanceFogSkyColor(fragWorldPos, dayFactor), skyBlend);

        float fog = terrainFogAmount(dist, dayFactor);
        if (fragRayDir.y < -0.12) {
            fog *= mix(1.0, 0.34, smoothstep(-0.12, -0.52, fragRayDir.y));
        }
        if (dist < 28.0 && fragRayDir.y < -0.2) {
            fog *= mix(0.30, 1.0, dist / 28.0);
        }
        if (texIdx == 5) {
            fog *= 0.38;
            fogColor = mix(fogColor, distanceFogSkyColor(fragWorldPos, dayFactor), 0.28);
        }
        vec3 texColor = isFluidTile(texIdx) ? tintFluidColor(tex.rgb, texIdx) : tex.rgb * blockTintColor(fragWorldPos, texIdx);
        texColor = terrainMicroVariation(texColor, fragWorldPos, texIdx);
        texColor = enhanceTerrainColor(texColor, texIdx, dayFactor);
        texColor = applyGrassTopFringe(texColor, fragWorldPos, normalize(fragNormal), texIdx, dayFactor);
        vec3 lit = lightBlock(texColor, normalize(fragNormal), fragWorldPos, fragAo);
        if (texIdx == 5) {
            lit = mix(lit, lit * 0.72 + hemisphereSkylight(normalize(fragNormal), dayFactor) * 0.62, 0.45);
            lit = boostSaturation(lit, 1.18);
        } else if (texIdx == 28) {
            lit += texColor * 3.05;
            lit = mix(lit, vec3(1.0, 0.36, 0.06), 0.26);
        } else if (texIdx == 20) {
            lit += texColor * 0.82;
        } else if (texIdx == 10 || texIdx == 11 || texIdx == 12) {
            float oreGlow = max(0.0, fragAo - 1.0);
            vec3 oreEmissive = texIdx == 11
                ? vec3(1.08, 0.62, 0.28)
                : texIdx == 10 ? vec3(0.98, 0.88, 0.68) : vec3(0.72, 0.78, 0.88);
            lit += texColor * oreEmissive * (0.42 + oreGlow * 0.85);
            lit = mix(lit, oreEmissive * 0.28, 0.12);
        }
        if (isFluidTile(texIdx)) {
            color = applyFluidFresnel(lit, fragWorldPos, fragNormal, texIdx, dist, fogColor, fog);
        } else {
            color = mix(lit, fogColor, fog);
        }
        if (texIdx == 9) {
            vec3 glassLit = lit * 0.62 + texColor * 0.28;
            color = mix(glassLit, fogColor, fog * 0.65);
            color = applyEnvironmentReflection(color, fragWorldPos, fragNormal, texIdx, dist);
            color += texColor * (1.0 - max(dot(normalize(fragNormal), normalize(ubo.cameraPosition - fragWorldPos)), 0.0)) * 0.06;
            outColor = vec4(color, 0.58);
            return;
        }
        color = applyTargetBlockOutline(color, fragWorldPos);
        color = applyPlacementGhost(color, fragWorldPos);
    }

    color = applyHud(color, gl_FragCoord.xy);
    color = applyHeldItem(color, gl_FragCoord.xy);
    color = applyInventoryOverlay(color, gl_FragCoord.xy);
    color = applyMainMenuOverlay(color, gl_FragCoord.xy);
    color = applyPauseOverlay(color, gl_FragCoord.xy);
    color = applyJeiOverlay(color, gl_FragCoord.xy);
    color = applyDeathOverlay(color, gl_FragCoord.xy);
    outColor = vec4(color, 1.0);
}
