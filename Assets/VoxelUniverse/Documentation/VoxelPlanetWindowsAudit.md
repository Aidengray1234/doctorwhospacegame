# Voxel Planet Windows reference audit

`Voxel_Planet_Windows.zip` is a compiled Windows build made with Unity 5.0.2f1. It does not contain the original Unity project or C# source files, so its assemblies and serialized assets must not be copied directly into the Unity 2021 project.

The managed assembly exposes useful architectural names and relationships, including `PlanetCover`, `CoverPlanet`, `CheckCoverUpdate`, `viewDistance`, `viewDistanceUnloadSq`, `ChunkBuilder`, `LoadChunk`, `UnloadChunk`, `CurvePos`, and `SideAdjust`. These support the same broad pattern used by this replacement:

- detailed local voxel terrain around the player;
- a continuously available planet-cover representation outside local detail;
- distance-based loading and unloading;
- curved planet coordinates kept separate from the local playable mesh.

The replacement uses those concepts only. It does not import the Unity 5 binaries, old input code, old mesh colliders, or old scene data.
