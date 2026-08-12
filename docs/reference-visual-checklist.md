# Reference Visual Checklist

Compare gauntlet captures against assets in `ReferenceMaterial/`. **Quality judgment is performed by agent critics**, not PowerShell scripts.

## Smoke harness (mechanical only)

`scripts/critic-gauntlet.ps1` runs build, optional tests, server bootstrap, and in-game critic capture. It writes `docs/critic-screenshots/smoke-report.json`.

| Mode | Command | Steps | When to use |
|------|---------|-------|-------------|
| **Fast** | `.\scripts\critic-gauntlet.ps1 -Mode Fast` | shader compile → build → server → client capture → smoke report (**no tests**) | Loop iteration; target **<3 min** wall-clock |
| **Full** | `.\scripts\critic-gauntlet.ps1 -Mode Full` | `dotnet test` → build → capture → smoke report | Victory gate before Round PASS |

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `ClientTimeoutSeconds` | 180 | Hard kill client if capture stalls |
| `CriticSeconds` | 45 | In-game critic capture duration |
| `MinScreenshotBytes` | 51200 | Blank/broken frame detector |
| `MinFps` | 30 | Minimum FPS from critic FPS report |

Examples:

```powershell
# Fast smoke loop (no tests)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/critic-gauntlet.ps1 -Mode Fast

# Full smoke gate (includes tests)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/critic-gauntlet.ps1 -Mode Full

# Wrapper that re-validates smoke report
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/critic-visual-check.ps1
```

**Smoke pass rules (mechanical only):**

- Fast: `buildPassed` + all 5 angle shots ≥ `MinScreenshotBytes` + FPS ≥ 30
- Full: Fast + `testsPassed`

Smoke does **not** judge visual quality, code quality, or todo completeness.

## Agent critics (quality judgment)

After a **builder batch** completes:

1. Write handoff JSON — see `memories/session/builder-batch-handoff.template.json`
2. Run smoke harness
3. Match references — `scripts/critic-match-references.ps1 -Keywords clouds,grass,hud`
4. Spawn **three fresh critic subagents** (never the builders):
   - **Code critic** — `/code` skill on changed files only
   - **Todo/scope critic** — handoff vs `gauntlet-round24-tasks.md` + `gauntlet-loop.md`
   - **Vision critic** — 5 capture angles vs keyword-matched reference images

Prompt templates: `memories/session/critic-agent-prompts.md`

JSON schema: `memories/session/critic-report.schema.json`

Outputs under `docs/critic-screenshots/critic-batch-<stamp>/`:

| File | Critic |
|------|--------|
| `critic-code.json` | Code quality |
| `critic-todo.json` | Todo/scope alignment |
| `critic-vision.json` | Visual vs reference |

Verdicts: `PASS` | `FAIL` | `BLOCKED`. Main orchestrator reads JSON and spawns fixer/builder agents from `issues[]` and `topPriority`.

If smoke `mechanicalPass` is false → vision/code critics return `BLOCKED` (fix smoke first).

## Reference image matching

Reference filenames in `ReferenceMaterial/` describe their content. The orchestrator (or `critic-match-references.ps1`) selects relevant images by matching handoff **keywords** to filenames — curation only, no scoring.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/critic-match-references.ps1 -Keywords clouds,godrays,grass,hud
```

## Capture angles

Critic mode captures 5 perspectives (unchanged):

| Angle | File | Typical use |
|-------|------|-------------|
| center | `critic-center.png` | HUD, terrain, block outline |
| look-left | `critic-look-left.png` | Horizon haze, distance fog |
| look-right | `critic-look-right.png` | Distance fog, terrain |
| look-up | `critic-look-up.png` | Sky, clouds, sun, god-rays |
| look-down | `critic-look-down.png` | Grass, caves, lava/ores |

## Rubric categories (for vision critic)

### Minecraft parity (mandatory — fail if broken)
- **Mesh integrity:** No see-through ground/terrain; all exposed block faces visible (tops, sides, bottoms at cliffs)
- **Grass blocks:** Green top + brown dirt sides (not uniform green cubes)
- **Plants:** Cross-plane tall grass/flowers, not full blocks
- **Solid columns:** No hollow chunk LOD shells visible within view distance
- **HUD / interaction:** Per gauntlet-loop Round 20 UX table and GDD §66 construction reference

### Reference aesthetic (HUD, Atmosphere, Terrain, Lighting)

### HUD
- Hearts left-aligned above hotbar (10 icons, 9px scale) — ref: `ice_and_lake_trees_gui.jpg`
- Hunger right-aligned above hotbar — ref: `ice_and_lake_trees_gui.jpg`
- Hotbar 182px wide, dark gray inset slots — ref: `ice_and_lake_trees_gui.jpg`
- Crosshair small white center — ref: `ice_and_lake_trees_gui.jpg`

### Atmosphere
- Soft blue-white horizon haze — ref: `floatingIsland_sky_fog_clouds.png`
- Wispy volumetric clouds — ref: `floatingIsland_sky_fog_clouds.png`
- Distant terrain fog fade — ref: `viewfromabove_grass_fog_oil_bow.png`
- Warm sun disc + god-rays — ref: `forrest_hill_godrays.png`

### Terrain
- Vibrant grass tops + fringe — ref: `pots_plants_grass.png`
- Stone/dirt detail — ref: `water_river_lake_structure_grass_sand_trees.png`
- Block AO in corners — ref: `water_river_lake_structure_grass_sand_trees.png`
- Thin dark block outline — ref: `water_river_lake_structure_grass_sand_trees.png`
- Lava/ores in caves — ref: `caves_stone_ores_minerals_crystals.png`

### Lighting
- Directional sun shadows — ref: `forrest_hill_godrays.png`
- Saturated natural colors — ref: `water_river_lake_structure_grass_sand_trees.png`
- Night sky (when applicable) — ref: `night_sky_aurora_snow_treetops.png`

## Smoke report fields

`docs/critic-screenshots/smoke-report.json`:

| Field | Meaning |
|-------|---------|
| `reportType` | `"smoke"` |
| `mechanicalPass` | All mechanical checks passed |
| `overallPass` | Same as `mechanicalPass` (legacy alias) |
| `buildPassed` | `dotnet build` succeeded |
| `testsPassed` | `dotnet test` succeeded (Full mode) |
| `criticFps` | FPS from critic FPS report / window title |
| `proceduralScreenshotDir` | Directory with 5 angle PNGs |
| `proceduralAngleShots` | Array of `{ angle, path, bytes }` |
| `gaps` | Mechanical failure reasons |

## Workflow summary

1. **Builders finish batch** → write handoff JSON
2. **Smoke harness** → mechanical pass/fail
3. **Reference matcher** → keyword-filtered reference list
4. **Three agent critics** → structured JSON verdicts
5. **Orchestrator** → spawn fixers from critic `issues[]` until all PASS

Removed (no longer used): byte-tier visual gap checklist, pixel `vision-compare`, inline OpenAI API `vision-critic` scripts.
