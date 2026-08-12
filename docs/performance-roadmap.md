# AstroCraft performance roadmap

Items completed in the recent performance pass are omitted here. Pick these up when you want another round of optimization.

## GPU staging buffer pool

**Goal:** Reuse host-visible staging buffers for chunk mesh uploads instead of allocating a new `VkBuffer` per upload.

**Why:** `VulkanRenderer.UploadVertices` creates and binds fresh GPU memory for every new chunk mesh. Under streaming load this causes allocation churn and driver overhead.

**Approach:**
- Ring buffer or size-bucket pool (e.g. 64 KiB / 256 KiB / 1 MiB / 4 MiB)
- `Rent(size)` → map → write → copy to device-local vertex buffer (optional upgrade) → `Return`
- Integrate with `ChunkMeshCache.ApplyMeshBuild` upload path

## GPU compute for parallel workloads

**Goal:** Offload embarrassingly parallel work to compute shaders where CPU/thread-pool cost is high.

**Candidates:**
- Physics / collision broad-phase for many entities
- Procedural decoration passes (foliage density, ore clusters) on server
- Light propagation (if extended beyond current vertex AO hack)

**Prerequisites:** Compute pipeline in Vulkan renderer, SSBO conventions, sync between compute and graphics queues.

## Multi-draw indirect

**Goal:** Further reduce CPU draw submission cost after batched vertex buffers.

**Why:** Draw batching already collapses opaque/transparent to one draw each. MDI helps if we split batches by material pass or reintroduce per-region buffers without per-chunk `CmdDraw`.

**Approach:**
- `VkBuffer` of `VkDrawIndirectCommand` built each frame from visible regions
- `vkCmdDrawIndirect` / `vkCmdDrawIndexedIndirect` in `DrawChunks`
- Pair with device-local batched vertex buffers and staging pool

## Optional follow-ups (lower priority)

- **Device-local chunk VBs** — Upload once via staging pool; batch copies on GPU instead of CPU merge each frame
- **Transparent sort** — Back-to-front chunk or quad order for cleaner alpha on foliage/water
- **Occlusion culling** — Hi-Z or chunk PVS for large view distances
