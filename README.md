# AstroCraft

Foundation vertical slice of a Minecraft-inspired engineering survival game (see `GameDesignDocument.md`).

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
- [Vulkan SDK](https://vulkan.lunarg.com/) (required for shader compilation — `glslc` compiles `Shaders/*.vert`/`*.frag` to SPIR-V at build time when `VULKAN_SDK` is set)

## Quick start

```powershell
# Terminal 1 — dedicated server (flat test world)
dotnet run --project src/AstroCraft.Server -- --name "LAN Game" --flat

# Terminal 2 — client
dotnet run --project src/AstroCraft.Client -- --connect 127.0.0.1 --name Player --flat

# Discover LAN servers
dotnet run --project src/AstroCraft.Client -- --discover --name Player
```

## Controls

- **WASD** — move
- **Mouse** — look
- **Space** — jump
- **Shift** — sneak
- **Ctrl** — sprint
- **1–9** — hotbar
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
