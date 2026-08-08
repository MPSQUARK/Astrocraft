You're right. Here is the consolidated document directly in the chat, formatted so you can paste it into Cursor as a project/design reference.

# Game Design & Technical Architecture Reference

## 1. Core Design Philosophy

The game should feel like a cohesive, Minecraft-inspired engineering/survival game with significantly deeper engineering, chemistry, physics, vehicles, automation and space systems.

The guiding philosophy is:

- **Minecraft-like interaction and construction**
- **FTD-inspired simplified but emergent physics**
- **KSP-inspired vehicle/space engineering where useful**
- **Tekkit/Mekanism-inspired industrial automation**
- **Chemlib-inspired chemistry and material representation**
- **Ad Astra-inspired planetary/space progression**
- Avoid unnecessarily realistic simulation where it does not improve gameplay.
- Prefer **simple, believable, gamified physics** over full simulation.
- Prefer systems that produce emergent behaviour rather than requiring players to manually configure every detail.
- Performance is a first-class design constraint.
- The server performs heavy simulation; clients should not need extremely powerful PCs.

The game should feel like **one coherent technological world**, not several unrelated mods bolted together.

---

# 2. World Structure

The world uses a hybrid dimensional model.

There are:

### Minecraft-style fictional dimensions

Equivalent in concept to dimensions such as the Nether, but they should have their own names and identities.

Do **not** simply call them "Nether" or "End."

### Physical planetary dimensions

The game also has a solar-system/planetary structure inspired by Ad Astra.

Examples:

- Moon
- Venus
- Mars
- other planets
- potentially additional celestial bodies

There should also be a **planet map** for navigating the solar system.

Planets have simplified environmental models.

For example:

- atmosphere
- atmospheric composition
- pressure
- temperature
- gravity
- potentially radiation

Atmosphere simulation should be simplified. The goal is to determine whether a player can breathe/survive, not simulate every atmospheric molecule.

A planet without a suitable atmosphere can therefore cause suffocation.

---

# 3. Planetary Travel

Planetary travel should **not** be fully continuous.

The intended model is:

```text
Build rocket
    ↓
Launch
    ↓
Reach required altitude
    ↓
Transition sequence
    ↓
Landing/arrival
    ↓
New planetary dimension
```

The player does not fly continuously through a seamless Minecraft-sized solar system.

This gives the game:

- believable progression
- manageable world simulation
- manageable rendering
- simpler networking
- simpler planetary generation

while still providing the feeling of physically travelling between planets.

---

# 4. Construction System

Construction should feel strongly Minecraft-like rather than feeling detached or entirely third-person like FTD.

The player physically constructs things in the world.

The player must be **physically within interaction range** to place blocks.

This is intentional.

For example, building a huge rocket should require infrastructure such as:

- scaffolding
- towers
- platforms
- cranes
- elevators
- temporary structures

A player should not be able to stand on the ground and remotely place blocks hundreds of metres above them.

This makes large-scale construction itself part of gameplay.

---

# 5. Building Modes

Normal construction uses Minecraft-style interaction.

The player can optionally enter an **advanced building mode**.

Advanced mode can provide:

- mirror
- rotate
- selection
- copy
- repeat
- other construction helpers

The intended interaction is similar to BuildCraft's selection/bounds concept.

The player can:

1. place a bounding area around a construction
2. use advanced selection tools inside that area
3. mirror/rotate/copy portions of the build

However, the player still needs to be physically within the appropriate range to actually place the blocks.

---

# 6. Blueprints

Blueprints should show a **projection/ghost of the complete structure**.

The player should be able to clearly see:

- where blocks need to go
- which components are missing
- the intended orientation
- the shape of the final structure

Blueprints are therefore a construction aid rather than a fully automatic construction system.

---

# 7. Grid System

The game uses two construction scales:

### Coarse grid

The normal Minecraft-style block grid.

Used for:

- terrain
- machines
- structural blocks
- major components
- damage
- primary construction

### Fixed sub-voxel grid

Used for smaller infrastructure.

Examples:

- cables
- pipes
- small supporting components
- infrastructure connections

The sub-grid is **fixed**, not arbitrary.

Machines are always coarse-grid blocks.

Small supporting/infrastructure components occupy the sub-grid.

---

# 8. Grid Interaction

The player can explicitly switch between:

- **Grid mode**
- **Sub-grid mode**

This is controlled by a button/key.

The player therefore knows exactly what layer they are editing.

Example:

```text
GRID MODE

[ MACHINE ][ BLOCK ][ BLOCK ]
```

versus:

```text
SUB-GRID MODE

[ cable ]
[ pipe  ]
[ small infrastructure ]
```

Different tools can interact with different layers.

---

# 9. Tools and Interaction Layers

Different tools should target different construction layers for UX clarity.

Examples:

### Coarse block tools

- Pickaxe
- Axe
- Shovel
- etc.

### Infrastructure tools

- Wrench → pipes
- Screwdriver → cables
- other specialized tools → other infrastructure

The tool determines what the player is trying to manipulate, while the grid/sub-grid mode provides an additional layer of control.

---

# 10. Block Placement and Orientation

Normal placement follows Minecraft-style rules:

- aim at a block face
- place against the face
- orientation is determined from the placement direction
- components have valid discrete orientations

However, the player can use the appropriate tool to **rotate a component after placement**.

This is important because sometimes the desired orientation is perpendicular to what natural face placement provides.

For example:

- engines
- machines
- turrets
- structural components
- directional blocks

Once part of a moving structure, the component naturally follows the structure's physics transform.

---

# 11. Collision Model

Collision should be simplified.

The desired feel is similar to Tekkit pipes:

- pipes/cables/etc. can have collisions
- objects can generally pass through them
- collisions are simplified
- they should not behave like perfectly solid full-size blocks

However, machines/tools that interact with them physically can still cause consequences.

Example:

> A machine trying to drill through a pipe can break the pipe.

This gives the world physical consequences without requiring expensive precise collision geometry for every tiny component.

---

# 12. Damage

Damage operates at the **coarse block level**.

Sub-grid components can be individually interacted with, but damage does not require per-sub-voxel destruction simulation.

For structural purposes, all infrastructure inside a coarse block is aggregated.

---

# 13. Structural Connectivity

Structural connectivity uses two complementary concepts.

### Primary structure

Coarse blocks are structurally connected through physical adjacency.

Minecraft-style adjacency forms the primary structural body.

### Explicit connections

Specialized components can create additional connections.

Examples:

- cables
- pipes
- joints
- mechanical connections
- supporting infrastructure

Therefore a cable can contribute to structural support.

This is intentional.

A hilarious example should be possible:

```text
Plane breaks in half

████████      ████████
████████──────████████
        cables
```

The cables could be the only thing keeping the two sections attached.

---

# 14. Structural Aggregation

Sub-grid infrastructure is aggregated into the coarse block representation for structural calculations.

Example:

```text
Coarse Block

cable
cable
pipe
beam
cable
```

becomes approximately:

```text
Structural Block
Strength = aggregated value
```

The structural solver does **not** individually evaluate every cable/pipe/etc.

Instead:

```text
Sub-grid components
        ↓
aggregate
        ↓
coarse block structural representation
        ↓
structural solver
```

This preserves emergent behaviour without making structural physics prohibitively expensive.

---

# 15. Structural Physics

Structural simulation uses a simplified model.

Materials have properties such as:

- stress
- strain
- strength
- failure thresholds

However, the system should **not evaluate every point in a large structure**.

Instead, it focuses on critical/weak points.

A simplified model such as the weakest-point/weakest-link approach is preferred.

When a structure fails:

1. identify the failed connection/block
2. determine whether the structure splits
3. separate disconnected components
4. recalculate each resulting structure
5. allow physics to act on each part independently

---

# 16. Structural Failure Cascades

Failures should naturally cascade where appropriate.

Example:

```text
Large structure
       ↓
support breaks
       ↓
load shifts
       ↓
next support exceeds capacity
       ↓
second failure
```

But failure does **not automatically destroy everything**.

If the failed portion falls away and removes the load from another support:

```text
Support fails
    ↓
Load falls away
    ↓
Remaining structure stabilizes
```

If the load instead falls onto another structure:

```text
Support fails
    ↓
Load transfers
    ↓
Next support overloaded
    ↓
Cascade
```

Physics determines the result.

---

# 17. Mass and Center of Mass

Mass matters for physics.

However, mass should not be tracked at unnecessary granularity.

Mass can be determined using lookup tables associated with block/material definitions.

For a connected structure:

1. precompute center of mass
2. precompute total mass
3. use these for physics
4. if the structure detaches, recalculate the resulting pieces

When a structure breaks:

```text
Original structure
      ↓
Connectivity split
      ↓
Structure A + Structure B
      ↓
recalculate mass/COM for A
recalculate mass/COM for B
```

---

# 18. Physics Philosophy

Physics should be **simplified and gamified**.

Do not attempt full KSP-style rigid-body simulation of every possible physical interaction.

The goal is:

> Physics should behave believably while remaining cheap enough for a Minecraft-scale multiplayer world.

Examples:

- objects under gravity can be batch calculated
- common gravitational acceleration can be applied in batches
- drag can then be applied per object
- simple cranes can be represented as one thing pulling on another
- rotational and hinge systems can be combined

---

# 19. Rotational / Hinge Systems

Hinges and rotational systems are treated as one general mechanical concept.

Players can construct rotational systems.

A turret-like system could be:

```text
Base
 ↓
rotational axis
 ↓
turret
```

Similar to FTD's axis turret bases.

Mechanical rotation does not need to become an independent resource simulation system.

Mechanical quantities such as:

- RPM
- rotation speed
- angular velocity

are primarily **emergent outputs of the physics engine**.

For example:

```text
Machine display:

Rotation:
2 RPM
```

rather than maintaining a separate "mechanical power network" equivalent to electrical power.

---

# 20. Player-Controlled Physics

The player provides **intent**.

For example:

- throttle
- steering
- pitch
- yaw
- roll
- controls

The server performs authoritative physics.

Client:

```text
Player input
    ↓
prediction
    ↓
immediate visual response
```

Server:

```text
Player intent
    ↓
authoritative physics
    ↓
authoritative state
```

Client reconciles with server state.

---

# 21. Client vs Server Physics

The server is authoritative.

Physics should be deterministic/reproducible **where practical**, but exact bit-level client/server determinism is not required.

The client can use a much cheaper approximation.

This means players with less powerful PCs do not need to simulate the complete world locally.

Architecture:

```text
                 SERVER
                   │
          full simulation
                   │
          authoritative state
                   │
        ┌──────────┴──────────┐
        ↓                     ↓
    CLIENT A               CLIENT B
    prediction             prediction
        ↓                     ↓
       render                render
```

---

# 22. Simulation LOD

Simulation LOD is critical.

If the player is nearby:

```text
FULL SIMULATION
```

If the player is far away:

```text
REDUCED SIMULATION
```

If the player is absent:

```text
DORMANT / NEGLIGIBLE SIMULATION
```

This applies to more than rendering.

LOD can affect:

- physics
- machines
- mobs
- item entities
- networks
- fluids
- gases
- thermal systems
- structural systems
- environmental systems
- vehicles

---

# 23. Chunk Simulation

Only loaded/active chunks receive full simulation.

If a player is not nearby:

- reduce simulation rate
- aggregate entities
- simplify physics
- reduce rendering
- combine item types/stacks
- cull rendering entirely where appropriate

Force-loaded chunks can remain active, similar to chunk-loading systems in modded Minecraft, but should still benefit from appropriate reduced simulation LOD when possible.

The goal is:

> If nobody is observing or interacting with a system, don't spend the same computational budget on it as a system directly beside a player.

---

# 24. World Structures and Physics Promotion

Do not create a hard architectural distinction between "static blocks" and "vehicle blocks."

All constructed structures use the same underlying representation.

A stationary structure can exist at negligible physics cost.

When something requires physics, it is **promoted** into an active physics structure.

Examples:

- engine starts
- joint moves
- structure falls
- explosion occurs
- support fails
- collision occurs
- force is applied
- vehicle begins moving

Then:

```text
Stationary structure
        ↓
physics required
        ↓
PROMOTE
        ↓
physics structure
```

Once physical activity ceases:

```text
physics structure
        ↓
settled / inactive
        ↓
DEMOTE
        ↓
low-cost structure
```

The structure is never permanently "baked" into the world.

This is inspired by FTD's approach and keeps the architecture flexible.

---

# 25. Game Time

Use a **Minecraft-style global fixed game tick**.

All authoritative simulation uses the global game tick.

This includes:

- physics
- machines
- recipes
- networks
- mobs
- environment
- planetary systems
- resource production
- time-dependent processes

Do not create unrelated independent clocks.

A process should be represented as:

```text
Duration = 200 ticks
```

not:

```text
Duration = 10 real-world seconds
```

LOD can process multiple elapsed ticks at once while still using the same global tick model.

---

# 26. Energy Model

Energy is intentionally simplified.

Do not simulate low-level electrical engineering such as:

- voltage
- current
- individual electrical components

unless specifically needed for gameplay.

Instead use:

- power production
- power consumption
- power capacity
- power transfer capacity
- efficiency
- losses

A machine can simply say:

```text
Power:
Input: 500 kW
Capacity: 1 MW
```

---

# 27. Power Generation

Power production is based on simplified physical conditions.

Examples:

### Solar

Depends on:

- time of day
- biome
- potentially atmospheric/environmental conditions

### Wind

Depends on:

- height
- biome
- wind conditions

### Nuclear

Depends on:

- fuel
- fuel efficiency
- waste ratio
- reactor conditions

### Combustion

Depends on:

- fuel source
- fuel properties
- machine efficiency

The philosophy should feel similar to classic industrial Minecraft mods.

---

# 28. Power Networks

Power networks are aggregated.

Once connected:

> The exact path is not relevant for normal calculations.

The network is compiled into a representation containing what matters.

Topology is recalculated only when modified.

Example:

```text
Generator ─ Cable ─ Cable ─ Machine
```

becomes a compiled network representation.

---

# 29. Power Capacity and Failure

Power transfer is limited by cable capacity.

Failure is not simply:

```text
input > capacity → reject power
```

Instead:

```text
Power input exceeds transfer capacity
        OR
Required power draw exceeds transfer capacity
        ↓
excess power causes cable heating
        ↓
temperature increases
        ↓
structural failure threshold
        ↓
cable failure
```

So cables can be overloaded and potentially destroyed.

Simplified relationship:

```text
Base power transfer
+ efficiency
= effective output
```

Network losses can be calculated using a simplified model.

For example:

```text
Length × loss amount × output amount
```

can determine final output.

---

# 30. Universal Transport Network Architecture

Power, fluids, gases and similar systems should use a common conceptual architecture.

Every transport/storage component has concepts such as:

- capacity
- throughput
- operating range
- overload behaviour
- failure threshold
- failure consequence

Examples:

```text
Power cable
    overload → heat → failure

Fluid pipe
    overload → pressure → rupture

Gas pipe
    overload → pressure → rupture

Storage tank
    overload → pressure/temperature → rupture
```

This should be generic so future transport types can reuse the same framework.

---

# 31. Fluid Networks

Fluids are aggregated network quantities rather than individually simulated voxels.

A fluid network may track:

- fluid type
- quantity
- flow
- pressure
- temperature
- capacity
- throughput

Example:

```text
Tank
  ↓
Pipe
  ↓
Pump
  ↓
Pipe
  ↓
Machine
```

The network solver determines transfer.

The system should feel like modded Minecraft fluids without requiring Minecraft's exact implementation.

---

# 32. Infinite Fluid Sources

Fluid behaviour should resemble modded Minecraft.

Large natural reservoirs can contain huge amounts of fluid.

For example:

- oceans
- lava seas
- underground reservoirs

can be pumped and drained.

However:

> **Do not allow infinite-source creation mechanics such as the standard Minecraft two-source water infinite pool.**

A finite reservoir should actually be drainable.

A water-world planet may effectively have an enormous amount of water, but this is represented as a large resource/reservoir, not by simulating every raindrop.

---

# 33. Environmental Water

Do not simulate individual raindrops.

Use aggregate environmental rules.

For example:

```text
If raining
    ↓
container gains X amount
per Y ticks
```

This preserves the gameplay effect without expensive simulation.

---

# 34. Gas Networks

Gases use a model similar to fluids but must have believable gas behaviour.

Track simplified quantities such as:

- amount
- pressure
- temperature
- volume
- flow
- capacity

Do not attempt molecular simulation.

Gas pressure should behave sensibly in:

- pipes
- tanks
- closed systems
- atmospheric environments

---

# 35. Thermal Model

Temperature is a first-class simplified physical quantity.

Use:

- Kelvin where appropriate
- simplified heat transfer coefficients
- heat capacity where relevant
- transfer rates rather than microscopic thermal simulation

For example:

```text
Object A = 800 K
Object B = 300 K

Heat transfer
    ↓
simplified coefficient
    ↓
temperature changes
```

The game does not need to simulate every molecule.

---

# 36. Storage Tanks

Storage tanks are part of the same generic capacity/failure system.

Closed systems can accumulate pressure.

For example:

```text
Tank
 ↓
gas added
 ↓
pressure increases
 ↓
safe limit exceeded
 ↓
stress increases
 ↓
rupture
```

This applies to:

- fluid tanks
- gas tanks
- pressurized vessels
- thermal storage
- other bounded systems

---

# 37. Network Topology

Network topology is expensive and should be calculated only when necessary.

Normal operation:

```text
Compiled network
      ↓
fast simulation
      ↓
fast simulation
      ↓
fast simulation
```

Modification:

```text
block placed/broken
      ↓
topology changed
      ↓
invalidate network
      ↓
find connected components
      ↓
rebuild
      ↓
compile
      ↓
resume simulation
```

Failures count as topology changes.

Example:

```text
A ─ B ─ C ─ D ─ E
        X
     rupture
```

becomes:

```text
A ─ B ─ C     D ─ E
```

and the two resulting networks are recalculated.

Only affected networks should be recalculated.

---

# 38. Machine Ports

Machines use configurable ports.

Each side can independently be:

- Input
- Output
- Both
- Disabled

This configuration exists **per resource type**, provided the machine actually requires that resource.

Example:

```text
Machine

Water:
North = Input
South = Output
East  = Disabled
West  = Both

Power:
North = Input
South = Disabled
East  = Input
West  = Disabled
```

This is more flexible than Mekanism's overly restrictive "one resource type per face" approach.

---

# 39. Machine Resource Requests

Machines should not directly control networks.

Instead, machines declare:

- required inputs
- desired quantities
- available outputs
- power requirements
- relevant conditions

The network solver determines what can actually be delivered.

Example:

```text
Machine requests:

Water:     100 L/s
Hydrogen:   20 kg/s
Power:       5 kW
```

Network response:

```text
Water:
requested 100
available 100
received 100

Hydrogen:
requested 20
available 12
received 12

Power:
requested 5
available 5
received 5
```

The machine operates based on what it actually receives.

---

# 40. Machine Partial Operation

Machines can define their behaviour when resource requirements are not fully met.

Possible behaviours:

### Stall

Nothing happens until requirements are satisfied.

### Proportional

The machine operates at reduced throughput.

Example:

```text
100% fuel → 100% output
50% fuel  → 50% output
```

### Proportional + reversible

Progress increases when resources are supplied and reverses when the resource is removed.

Example:

```text
Heating process

300 K
 ↓
500 K
 ↓
800 K

Heat removed
 ↓
700 K
 ↓
500 K
 ↓
300 K
```

The machine/process defines which behaviour it uses.

---

# 41. Recipes and Processes

The recipe system is hybrid.

Simple recipes should be easy to define.

More advanced recipes support:

- multiple inputs
- multiple outputs
- proportions
- typed resources
- items
- fluids
- gases
- chemicals
- energy
- temperature
- pressure
- other conditions
- duration
- reversible processes

Simple:

```text
3 Iron
1 Carbon
→
Steel
```

Chemical:

```text
2 H₂ + O₂
→
2 H₂O
```

Industrial:

```text
100 kg A
25 kg B
2 MJ
800 K
5 bar
→
80 kg C
45 kg waste
```

---

# 42. Recipe Proportions

Most machines should support proportional recipes.

For example:

```text
A : B : C
2 : 1 : 4
```

The system should determine how much processing can occur based on available quantities.

This is especially important for:

- chemical reactions
- refining
- fuels
- nuclear processes
- industrial processing

---

# 43. Mass Conservation

The resource system should maintain mass conservation wherever physically meaningful.

If:

```text
100 kg input
```

becomes:

```text
80 kg product
```

the missing 20 kg should be accounted for as:

- byproduct
- waste
- gas
- impurity
- another output

rather than simply disappearing.

This does not require perfect real-world chemistry.

It is a simplified gameplay model with internally consistent mass accounting.

---

# 44. Chemistry Model

Chemistry uses three levels.

## Element

Examples:

- Iron
- Copper
- Hydrogen
- Oxygen
- Uranium

## Compound / Molecule

Examples:

- Water
- CO₂
- Methane
- Sulfuric acid

## Mixture / Material

Examples:

- crude oil
- natural gas
- air
- ores
- alloys

Each resource can have an associated composition.

The game does not simulate individual molecules.

---

# 45. Mixture Separation

Mixtures can be processed into their constituent materials.

Example:

```text
Crude Oil
   ↓
fractionation
   ├── light hydrocarbons
   ├── heavy hydrocarbons
   ├── gas
   └── impurities
```

Another example:

```text
Air
 ↓
separation
 ├── Nitrogen
 ├── Oxygen
 └── Argon
```

This gives chemistry depth while retaining a simple simulation model.

---

# 46. Chemical Reactions

Chemical reactions should use a simplified equation model.

For example:

```text
A + B + C ⇌ D + E
```

The system does not need to model reaction kinetics at molecular scale.

It needs:

- inputs
- outputs
- proportions
- energy
- temperature
- pressure where relevant
- reaction direction
- potentially efficiency/yield

Chemlib is an inspiration, but the system should be substantially simpler than a full chemistry simulator.

---

# 47. JEI-Style Knowledge Interface

The game should have a JEI-style lookup system.

Players can search for:

- items
- materials
- elements
- compounds
- machines
- recipes
- processes
- production chains

The interface should answer:

> "What can I make with this?"

and:

> "How do I obtain this?"

It should also expose process relationships.

For example:

```text
Crude Oil
 ↓
Refinery
 ↓
Fuel
 ↓
Combustion Generator
 ↓
Power
```

---

# 48. Materials

Materials should be data-defined and reusable across the game.

A material can have:

- mass
- structural strength
- stress/strain properties
- thermal properties
- temperature limits
- chemical composition
- density
- other gameplay properties

Mass should primarily be determined through lookup data rather than per-block expensive simulation.

---

# 49. Mechanical Power

Mechanical power is deliberately not treated as a separate network equivalent to electricity.

Mechanical behaviour emerges from physics.

The game can expose useful derived values:

```text
Rotation:
2 RPM

Torque:
X

Angular velocity:
Y
```

but there should not necessarily be a separate "mechanical power grid."

Mechanical systems can naturally interact through:

- shafts
- hinges
- rotational components
- joints
- physical forces

---

# 50. Tiered Infrastructure

Infrastructure should use simple tiers.

For example:

```text
Basic
Advanced
Elite
...
```

Each tier improves properties such as:

- throughput
- capacity
- pressure rating
- power transfer
- loss
- temperature tolerance
- durability

Pipes should be able to transport any compatible liquid rather than requiring a separate pipe type for every fluid.

The same general principle applies to other transport infrastructure.

---

# 51. Data-Driven Content

The content system uses a **hybrid architecture**.

Core gameplay capabilities are implemented in C#.

Game content is defined through external data files where practical.

At load time:

```text
Data files
    ↓
Loader
    ↓
Validation
    ↓
Reference/dependency resolution
    ↓
Compiled runtime definitions
    ↓
Fast runtime systems
```

Runtime systems should operate on efficient compiled representations rather than repeatedly parsing JSON/YAML/etc.

---

# 52. Data Files

Potential content categories:

```text
/content
    /blocks
    /machines
    /materials
    /elements
    /compounds
    /mixtures
    /fluids
    /gases
    /recipes
    /processes
    /vehicles
    /planets
    /dimensions
    /tools
    /quests
```

Exact file format can be selected during implementation.

The important architectural principle is:

> **Separate engine capabilities from game content.**

---

# 53. Modding

The initial modding system is **configuration/data-only**.

Mods can:

- add content
- modify content
- define recipes
- define machines within supported capabilities
- add materials
- add planets
- add other supported data

Mods cannot:

- execute arbitrary C#
- load arbitrary native plugins
- replace engine systems
- inject arbitrary code

The engine defines what is possible.

This provides safer multiplayer and easier versioning.

---

# 54. Internal Content Modularity

The game is internally modular but externally cohesive.

Potential internal modules:

```text
Core
├── Materials
├── Chemistry
├── Industry
├── Energy
├── Nuclear
├── Vehicles
├── Space
├── Exploration
└── World
```

But the player should experience one unified technology ecosystem.

There should be one universal:

```text
Water
Iron
Copper
Hydrogen
Oxygen
Uranium
Crude Oil
...
```

rather than separate versions created by different modules.

---

# 55. Technology Progression

Technology should form one interconnected progression.

Example:

```text
Raw resources
     ↓
Basic processing
     ↓
Materials
     ↓
Industrial machinery
     ↓
Chemistry
     ↓
Advanced energy
     ↓
Nuclear
     ↓
Advanced engineering
     ↓
Space
     ↓
Planetary exploration
```

Different modules extend this progression rather than creating isolated technology trees.

---

# 56. Simulation Philosophy

The most important performance principle is:

> **Topology and structural changes are expensive; steady-state operation is cheap.**

Examples:

### Network

```text
Topology changes
→ rebuild

Normal operation
→ cheap
```

### Structure

```text
Block breaks
→ connectivity recalculation

Stationary structure
→ negligible simulation
```

### Physics

```text
Physics required
→ activate detailed simulation

No physical activity
→ low-cost representation
```

### Chunk

```text
Player nearby
→ full simulation

Player far away
→ reduced simulation

Nobody active
→ dormant/aggregate state
```

---

# 57. General Simulation LOD

LOD is not merely a graphical feature.

It is a simulation strategy.

Potential levels:

### LOD 0 — Full

- full physics
- full machine updates
- full entity simulation
- full network simulation
- detailed interactions

### LOD 1 — Reduced

- reduced update frequency
- aggregated entities
- simplified physics
- simplified network/resource processing

### LOD 2 — Dormant

- no continuous active simulation
- persistent state retained
- elapsed time handled when necessary

When the system becomes relevant again, it can catch up using aggregated calculations.

---

# 58. Items and Entities

Items should use simplified representations.

If there are many identical items:

```text
1000 individual iron items
```

can become:

```text
Iron
Quantity: 1000
```

Different item types can be aggregated where appropriate.

Rendering can be culled entirely when outside relevant visual range.

---

# 59. Environmental Simulation

Do not simulate microscopic environmental events.

Examples:

### Rain

```text
Raining
+
elapsed ticks
→
container gains X water
```

### Wind

Use an aggregate wind value based on:

- biome
- altitude
- environment
- time/weather

### Solar

Use aggregate solar input based on:

- time of day
- biome
- planetary environment

The simulation should produce believable results, not physically model every individual event.

---

# 60. Server Architecture

The server owns:

- authoritative world state
- physics
- structural simulation
- machine simulation
- resource networks
- environmental simulation
- player authority
- game tick progression

Clients primarily handle:

- rendering
- input
- UI
- lightweight prediction
- local presentation

---

# 61. Networking Philosophy

The networking approach should be hybrid.

### State broadcast

Use an efficient UDP-style state transport for frequent state updates.

State should primarily be **diff-based**.

Instead of repeatedly sending complete world state:

```text
STATE DIFF
```

is sent whenever relevant state changes.

### Periodic full snapshots

Occasionally send a complete/authoritative state snapshot to recover from missed updates and ensure synchronization.

Conceptually:

```text
Diff
Diff
Diff
Diff
Full snapshot
Diff
Diff
Diff
...
```

This prevents accumulated drift.

---

# 62. Commands / Player Intent

Player commands should use a reliable transport mechanism appropriate to the networking stack.

The player sends **intent**, not authoritative physics state.

Examples:

```text
Throttle = 0.8
Pitch = -0.2
Yaw = 0.4
```

rather than:

```text
My vehicle is now at X/Y/Z
```

The server determines the resulting state.

QUIC or an equivalent modern reliable transport can be evaluated during implementation.

---

# 63. Chat

Chat can use a separate reliable mechanism such as SignalR or equivalent infrastructure.

The exact protocol can be finalized during implementation.

The important separation is:

```text
Real-time world state
→ low-latency state transport

Player commands
→ reliable command channel

Chat
→ separate reliable messaging system
```

---

# 64. Client Prediction

The client should immediately react to player input using a crude local estimation.

Example:

```text
Player presses throttle
        ↓
client predicts movement
        ↓
render immediately
        ↓
server processes authoritative physics
        ↓
server state arrives
        ↓
client reconciles
```

The client's simulation does **not** need to exactly match the server.

This reduces perceived latency while keeping the server authoritative.

---

# 65. Physics Networking

Because physics can be expensive:

- server performs the real simulation
- client predicts using simplified physics
- server periodically sends authoritative state
- client corrects errors

This is particularly important for:

- rockets
- aircraft
- ships
- vehicles
- cranes
- rotating structures
- structural failures

---

# 66. Reference Inspiration

The following projects are conceptual references, not implementation requirements.

## Minecraft

Use as inspiration for:

- block interaction
- construction
- tools
- dimensions
- game ticks
- chunk loading
- player interaction
- simple resource representations
- inventory conventions
- mod-like progression

Important distinction:

> The game should not simply reproduce Minecraft mechanics wholesale. Minecraft is primarily a UX/construction reference.

---

## Tekkit / Classic Industrial Minecraft Mods

Use as inspiration for:

- pipes
- power networks
- machines
- industrial progression
- simplified transport systems
- large resource reservoirs
- practical automation
- pipe collision philosophy

Especially:

> Transport systems should be simple enough to understand but capable of producing meaningful failures.

---

## Mekanism

Use as inspiration for:

- configurable machine ports
- industrial machinery
- resource transport
- tiers
- power systems

But improve upon its port limitations.

Desired behaviour:

```text
Each resource type
+
Each face
=
Input / Output / Both / Disabled
```

rather than restricting a face to a single resource type.

---

## Chemlib

Use as inspiration for:

- elements
- compounds
- molecules
- chemical processes
- material identity

But intentionally simplify the model.

Desired philosophy:

```text
A + B + C ⇌ D + E
```

rather than detailed molecular simulation.

---

## From The Depths

Use as inspiration for:

- simplified physics
- vehicle construction
- emergent engineering
- rotational systems
- structural behaviour
- player-built vehicles
- gamified physics
- physics-driven mechanical systems

But avoid making construction feel detached/third-person.

The player should still physically build things Minecraft-style.

---

## KSP

Use as inspiration for:

- rocket engineering
- center of mass
- planetary travel concepts
- vehicle physics concepts

But **do not attempt full KSP-level simulation**.

The game must remain capable of running a Minecraft-scale multiplayer world.

---

## Ad Astra

Use as inspiration for:

- planets
- planetary dimensions
- space progression
- planetary maps
- atmosphere/environment concepts

But planetary travel should use the simplified transition model rather than a completely continuous solar-system simulation.

---

## BuildCraft

Use as inspiration for:

- construction selection/bounds
- advanced building tools
- blueprint-like workflows
- practical construction infrastructure

---

# 67. Key Architectural Principles for Cursor

When implementing the game, Cursor should prioritize the following.

### Principle 1 — Keep systems data-driven

Avoid hardcoding individual machines/materials/recipes into engine logic.

Prefer:

```text
Engine capability
+
Data definition
=
Game content
```

---

### Principle 2 — Compile content at load time

Do not repeatedly parse raw configuration files during gameplay.

Use:

```text
Raw definition
→ validated definition
→ compiled runtime definition
```

---

### Principle 3 — Avoid per-block expensive simulation

Use:

- lookup tables
- aggregation
- batching
- cached values
- network compilation
- structural aggregation

---

### Principle 4 — Recalculate only after topology changes

Networks and structures should be cached.

Changes invalidate caches.

Do not repeatedly rediscover the same connectivity.

---

### Principle 5 — Use aggregation aggressively

Examples:

```text
Many items → stack/count

Many sub-grid connections → coarse structural representation

Large network → compiled network

Many distant entities → aggregate simulation

Stationary structure → negligible physics
```

---

### Principle 6 — Physics should be emergent but simplified

Don't implement physics simply because real-world physics contains a variable.

Implement it when it produces useful gameplay.

---

### Principle 7 — Server authority

The server is the source of truth.

Clients predict and render.

---

### Principle 8 — Fixed game ticks

All authoritative simulation uses the global Minecraft-style game tick.

---

### Principle 9 — Failures should create consequences

Don't silently clamp unrealistic inputs.

Prefer:

```text
Overload
→ stress
→ failure
→ topology change
→ recalculation
```

where appropriate.

---

### Principle 10 — Keep the game coherent

Every system should feel like it belongs to the same world.

Avoid:

- duplicate materials
- duplicate fluids
- duplicate energy systems
- isolated mod-like mechanics
- unnecessarily complicated simulation

---

# 68. Example End-to-End Scenario

Consider a player building a rocket.

### Construction

The player builds a coarse-grid rocket:

```text
      Nose
       ▲
       │
    [Tank]
    [Tank]
  [Machine]
  [Engine]
```

They physically climb a tower to place the upper blocks.

---

### Infrastructure

They switch to sub-grid mode.

They place:

- fuel pipes
- oxygen pipes
- cables

using:

- wrench
- screwdriver

---

### Structural system

The infrastructure contributes to structural support.

The system aggregates all sub-grid connections into the coarse structural representation.

---

### Resource network

Fuel pipes form a compiled network.

Oxygen pipes form another.

Power cables form another.

No repeated graph traversal occurs during normal operation.

---

### Engine

The engine requests:

```text
Fuel
Oxidizer
Power
```

The networks determine what is actually available.

If fuel is limited, the engine may run proportionally.

---

### Launch

Engine thrust creates a physical force.

The rocket is automatically promoted into an active physics structure.

The server runs authoritative physics.

The client predicts movement.

---

### Structural failure

An overloaded connection fails.

The structure splits.

Mass and center of mass are recalculated for the resulting pieces.

One part falls away.

The remaining rocket stabilizes because the load has disappeared.

---

### Space transition

The rocket reaches the required altitude.

The game transitions into the target planetary dimension.

---

### Planet

The player arrives on another planet.

The atmosphere system determines:

- atmospheric pressure
- breathable composition
- suffocation risk

No individual atmospheric molecules are simulated.

---

# 69. Example Industrial Scenario

Player builds a chemical plant.

```text
Crude Oil Tank
      ↓
Refinery
      ↓
Fuel ──────────────→ Generator
      ↓
Chemical feedstock
      ↓
Chemical Reactor
```

The refinery uses a mixture separation process.

The reactor uses:

```text
A + B + C ⇌ D + E
```

The reactor requires:

- input chemicals
- power
- temperature
- pressure

The heating system can partially progress.

If heat is removed:

```text
progress decreases
```

The resulting materials obey mass accounting.

The pipes have finite throughput.

If the player pushes too much fluid:

```text
flow ↑
pressure ↑
pipe stress ↑
rupture
```

The rupture changes network topology.

The network is rebuilt.

The factory may stop receiving fuel.

The generator loses input.

Power drops.

Machines stall or reduce production.

This creates an emergent industrial failure chain without requiring a microscopic simulation.

---

# 70. Final High-Level Architecture

The overall system should conceptually look like:

```text
                         GAME
                          │
              ┌───────────┴───────────┐
              │                       │
           CLIENT                   SERVER
              │                       │
       ┌──────┼──────┐       ┌────────┼─────────┐
       │      │      │       │        │         │
    Render  Input  UI     World     Physics   Simulation
                          │          │          │
                          ├──────────┼──────────┤
                          │          │          │
                       Structures Networks   Machines
                          │          │          │
                          └──────────┼──────────┘
                                     │
                               Global Tick
                                     │
                         ┌───────────┼───────────┐
                         │           │           │
                     Chemistry    Energy     Environment
                         │           │           │
                         └───────────┼───────────┘
                                     │
                              Data Definitions
                                     │
                         ┌───────────┴───────────┐
                         │                       │
                    Core Systems            Game Content
                                               │
                                      External Configuration
```

The central architectural idea is:

> **A large, persistent, Minecraft-scale world should be cheap by default. Detailed simulation is activated only where the player or gameplay requires it. Expensive calculations happen when topology, structure or state meaningfully changes; steady-state systems operate from cached/compiled representations.**

And the central gameplay idea is:

> **Give players simple, understandable engineering rules that interact to produce surprisingly complex and sometimes hilarious emergent behaviour.**

That should be the north star for implementation.
