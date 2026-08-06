# VoxelUniverse

Production replacement for the rejected `PlanetSystem` and `BlockPlanetSystem` prototypes.

## Boundaries

- The game is `doctorwhospacegame`.
- Planetcraft is reference material only.
- Nothing under `Playground` is modified.
- Ships are excluded until their separate future system.
- The old planet systems remain in place until this replacement compiles and its explicit installer succeeds.

## Current implementation layer

This first foundation layer contains:

- deterministic double-precision math and stable celestial-body IDs;
- analytic Kepler orbits and binary barycentre evaluation;
- 16×16×16 section addressing;
- packed palette-based block-state storage with immutable worker snapshots;
- stable six-face cube-sphere bases, address conversion, and seam canonicalization;
- a floating-origin component that changes only Unity render-space transforms;
- EditMode regression tests for the pure coordinate, orbit, storage, and seam rules.

It deliberately does not install scene objects or replace the current runtime yet. Generation, scheduling, meshing, collision, DDA interaction, inventory, saves, LOD rendering, atmosphere, lighting, oceans, and clouds will connect to these stable foundations in dependency order.
