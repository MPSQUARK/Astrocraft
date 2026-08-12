# AstroCraft

Tekkit inspired mineclone (see `GameDesignDocument.md`).

## Projects

| Project | Description |
|---------|-------------|
| `AstroCraft.Core` | Shared simulation, world, blocks, networking protocol |
| `AstroCraft.Server` | Dedicated authoritative server (20 TPS) |
| `AstroCraft.Client` | Silk.NET Vulkan client with prediction |
| `AstroCraft.Tests` | Unit and integration tests |

## Requirements

- .NET 10 SDK
- Vulkan-capable GPU and drivers
- [Vulkan SDK](https://vulkan.lunarg.com/) with `glslc` on PATH or installed under `C:\VulkanSDK\<version>\` (shader compile is **required** — no embedded SPIR-V fallback)

Shaders are compiled from GLSL source with the official SDK tool:

```powershell
powershell -File scripts/compile-shaders.ps1
# vertex:  glslc shader.vert -o shader.vert.spv
# fragment: glslc shader.frag -o shader.frag.spv
```

`dotnet build` on `AstroCraft.Client` runs the same script automatically. Set `VULKAN_SDK` if installed outside the default path.

## Quick start

```powershell
# Terminal 1 — dedicated server (flat test world)
dotnet run --project src/AstroCraft.Server -- --name "LAN Game" --flat

# Terminal 2 — client
dotnet run --project src/AstroCraft.Client -- --connect 127.0.0.1 --name Player --flat

# Discover LAN servers (UDP broadcast on port 27015)
dotnet run --project src/AstroCraft.Client -- --discover --name Player
```

### LAN multiplayer

Default ports: **game 27016**, **discovery 27015**. Multiple clients can join the same server; each receives a unique player ID in `ServerWelcome`.

```powershell
# Host on the LAN (custom port optional)
dotnet run --project src/AstroCraft.Server -- --name "LAN Game" --port 27016 --flat

# Join by IP
dotnet run --project src/AstroCraft.Client -- --connect 192.168.1.10 --port 27016 --name Player --flat

# Or browse LAN servers
dotnet run --project src/AstroCraft.Client -- --discover --name Player --flat
```

Smoke scripts (build + short run):

```powershell
.\scripts\two-player-test.ps1          # server + 2 clients (~8s)
.\scripts\critic-playtest.ps1          # server + critic screenshot
```

## Controls

- **WASD** — move
- **Mouse** — look
- **Space** — jump
- **Shift** — sneak
- **Ctrl** — sprint
- **1–9** — hotbar
- **E** — inventory
- **LMB** — break block
- **RMB** — place block
- **Esc** — pause menu

## Build & test

```powershell
dotnet build AstroCraft.slnx
dotnet test
```

## Architecture notes

- Server owns all block placement and physics (anti-cheat by design)
- Client sends intent only; reconciles with server state
- LAN discovery on UDP port 27015; game traffic on 27016
- Procedural terrain + caves, or `--flat` for critics/testers

## References

See `docs/references.md` for Silk.NET, voxel meshing, and netcode references.
