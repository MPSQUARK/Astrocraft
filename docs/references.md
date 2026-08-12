# AstroCraft — ReferenceMaterial

Gathered before implementation (gauntlet spec requirement).

## Silk.NET + Vulkan

- [Silk.NET Vulkan discussion #601](https://github.com/dotnet/Silk.NET/discussions/601) — use `WindowOptions.DefaultVulkan`, `window.VkSurface` for surface creation, cache `Vk.GetApi()`
- [SilkVulkanTutorial](https://github.com/dfkeenan/SilkVulkanTutorial) — C# port of Overvoorde Vulkan tutorial
- [Vulkan Tutorial (C)](https://vulkan-tutorial.com/) — behavioral reference for pipeline setup
- Requires **Vulkan SDK** installed for validation layers and shader compilation (`glslc`)

## Minecraft UX (behavioral reference only — no asset copying)

- First-person movement: WASD + mouse look, space jump, shift sneak
- Block placement: aim at face, place adjacent block
- Block breaking: hold attack, progress indicator, drop to inventory
- Chunk streaming: load/unload around player view distance
- Hotbar: 9 slots, scroll/number keys

## Voxel meshing

- Per-chunk face culling: only render faces adjacent to transparent/air blocks
- Optional greedy meshing for fewer triangles (optimize after naive works)
- Chunk mesh rebuild on block change in chunk + neighbors

## LAN discovery

- UDP broadcast on fixed port (e.g. 27015) with magic packet `ASTROCRAFT_DISCOVER`
- Server responds with `ASTROCRAFT_ANNOUNCE` + server name + game port
- Client lists discovered servers; connect via TCP/UDP to game port

## Server-authoritative netcode

- Client sends **intent**: `PlayerInput` (movement vector, look, actions), `PlaceBlock`, `BreakBlock`
- Server validates range, block rules, applies to world
- Server broadcasts **state diffs**: player positions, block changes, chunk data on join
- Periodic full snapshot every N ticks for drift recovery
- Client predicts local movement; reconciles on server update

## Performance baseline (30 FPS @ 1080p)

- Target: GTX 1060 / RX 580 class or modern iGPU (Intel Iris Xe / AMD Radeon integrated)
- Strategies: chunk mesh caching, frustum culling, limit view distance initially (8–12 chunks)

## Sci-fi art direction

- Cool blues (#2a3f5f, #4a90d9), industrial grays (#3d3d3d, #5a5a5a), accent cyan (#00d4ff)
- Procedural pixel textures 16×16 per block face, generated in code (no Minecraft copies)
