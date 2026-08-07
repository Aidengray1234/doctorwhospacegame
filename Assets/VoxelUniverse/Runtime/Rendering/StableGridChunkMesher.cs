using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Generation;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    internal sealed class StableGridChunkMeshData
    {
        public readonly List<Vector3> vertices = new List<Vector3>(8192);
        public readonly List<Vector3> normals = new List<Vector3>(8192);
        public readonly List<Vector2> uv = new List<Vector2>(8192);
        public readonly List<Color32> colors = new List<Color32>(8192);
        public readonly List<int> opaqueTriangles = new List<int>(12288);
        public readonly List<int> waterTriangles = new List<int>(2048);
    }

    internal sealed class StableGridChunkSnapshot
    {
        public const int Border = 1;
        public const int InnerSize = 16;
        public const int Size = InnerSize + Border * 2;
        public const int CellCount = Size * Size * Size;

        public readonly Int3 chunkKey;
        public readonly Int3 origin;
        public readonly int requestVersion;
        public readonly int faceCellResolution;
        public readonly BlockState[] blocks = new BlockState[CellCount];
        public VoxelAddress[] addresses = new VoxelAddress[CellCount];

        public StableGridChunkSnapshot(Int3 key, int version, int resolution)
        {
            chunkKey = key;
            requestVersion = version;
            faceCellResolution = resolution;
            origin = new Int3(key.x * InnerSize, key.y * InnerSize, key.z * InnerSize);
        }

        public static int Index(int x, int y, int z)
        {
            return x + Size * (z + Size * y);
        }

        public BlockState GetLocal(int x, int y, int z)
        {
            return blocks[Index(x, y, z)];
        }

        public VoxelAddress GetAddressLocal(int x, int y, int z)
        {
            return addresses[Index(x, y, z)];
        }

        public bool TryGetGlobal(Int3 cell, out BlockState state)
        {
            int x = cell.x - origin.x + Border;
            int y = cell.y - origin.y + Border;
            int z = cell.z - origin.z + Border;
            if (x < 0 || y < 0 || z < 0 || x >= Size || y >= Size || z >= Size)
            {
                state = BlockState.Air;
                return false;
            }
            state = GetLocal(x, y, z);
            return true;
        }
    }

    internal sealed class StableGridBuiltChunk
    {
        public StableGridChunkSnapshot snapshot;
        public StableGridChunkMeshData mesh;
        public Int3 chunkKey;
        public int requestVersion;
        public int sampledSurfaceColumns;
    }

    internal static class StableGridWorkerBuilder
    {
        private static readonly Int3[] NeighborOffsets =
        {
            new Int3(1,0,0), new Int3(-1,0,0), new Int3(0,1,0),
            new Int3(0,-1,0), new Int3(0,0,1), new Int3(0,0,-1)
        };

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back
        };

        private static readonly Vector3[,] FaceCorners =
        {
            { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
            { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
            { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
            { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
            { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
            { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
        };

        public static StableGridBuiltChunk Build(
            Int3 chunkKey,
            int requestVersion,
            VoxelUniverseSettings settings,
            CelestialBodyId bodyId,
            Dictionary<Int3, uint> editSnapshot)
        {
            StableGridChunkSnapshot snapshot = new StableGridChunkSnapshot(chunkKey, requestVersion, settings.faceCellResolution);
            VoxelTerrainGenerator generator = new VoxelTerrainGenerator(settings, bodyId);
            Dictionary<long, int> surfaceCache = new Dictionary<long, int>(1024);

            int sampledSurfaceColumns = 0;
            for (int y = 0; y < StableGridChunkSnapshot.Size; y++)
            for (int z = 0; z < StableGridChunkSnapshot.Size; z++)
            for (int x = 0; x < StableGridChunkSnapshot.Size; x++)
            {
                Int3 cell = new Int3(
                    snapshot.origin.x + x - StableGridChunkSnapshot.Border,
                    snapshot.origin.y + y - StableGridChunkSnapshot.Border,
                    snapshot.origin.z + z - StableGridChunkSnapshot.Border);

                Double3 localPosition = new Double3(
                    cell.x + 0.5d,
                    cell.y + 0.5d,
                    cell.z + 0.5d);

                VoxelAddress address = CubeSphereMapper.PositionToAddress(
                    bodyId, localPosition, settings.groundRadius, settings.faceCellResolution);

                int index = StableGridChunkSnapshot.Index(x, y, z);
                snapshot.addresses[index] = address;

                uint packed;
                if (editSnapshot != null && editSnapshot.TryGetValue(cell, out packed))
                {
                    snapshot.blocks[index] = BlockState.FromPacked(packed);
                    continue;
                }

                long surfaceKey = SurfaceKey(address);
                int surface;
                if (!surfaceCache.TryGetValue(surfaceKey, out surface))
                {
                    surface = generator.GetSurfaceHeight(address.face, address.u, address.v);
                    surfaceCache.Add(surfaceKey, surface);
                    sampledSurfaceColumns++;
                }
                snapshot.blocks[index] = generator.SampleBaseBlock(address, surface);
            }

            StableGridChunkMeshData mesh = BuildMesh(snapshot);
            // Addresses are needed only while building directional face UVs. The live
            // collision/cache snapshot keeps compact BlockState data only.
            snapshot.addresses = null;
            return new StableGridBuiltChunk
            {
                chunkKey = chunkKey,
                requestVersion = requestVersion,
                snapshot = snapshot,
                mesh = mesh,
                sampledSurfaceColumns = sampledSurfaceColumns
            };
        }

        private static StableGridChunkMeshData BuildMesh(StableGridChunkSnapshot snapshot)
        {
            StableGridChunkMeshData data = new StableGridChunkMeshData();
            int snapshotResolution = snapshot.faceCellResolution;
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int sx = x + StableGridChunkSnapshot.Border;
                int sy = y + StableGridChunkSnapshot.Border;
                int sz = z + StableGridChunkSnapshot.Border;
                BlockState state = snapshot.GetLocal(sx, sy, sz);
                if (state.IsAir) continue;

                BlockDefinition definition = BlockRegistry.Get(state.BlockId);
                bool water = definition.renderLayer == BlockRenderLayer.Water;
                bool fullCube = definition.collisionShape == BlockCollisionShape.FullCube;
                if (!water && !fullCube) continue;

                Int3 globalCell = new Int3(snapshot.origin.x + x, snapshot.origin.y + y,
                    snapshot.origin.z + z);
                Vector3 radial = new Vector3(globalCell.x + 0.5f, globalCell.y + 0.5f,
                    globalCell.z + 0.5f).normalized;
                int outerFace = ClosestFace(radial);
                int innerFace = OppositeFace(outerFace);
                VoxelAddress address = snapshot.GetAddressLocal(sx, sy, sz);

                for (int face = 0; face < 6; face++)
                {
                    Int3 n = NeighborOffsets[face];
                    BlockState neighbor = snapshot.GetLocal(sx + n.x, sy + n.y, sz + n.z);
                    if (!ShouldRenderFace(definition, state, neighbor)) continue;
                    int tile = GetTile(definition, state, address, face, outerFace, innerFace, snapshotResolution);
                    AddFace(data, new Vector3(x, y, z), face, tile, water);
                }
            }
            return data;
        }

        private static bool ShouldRenderFace(BlockDefinition definition, BlockState state,
            BlockState neighbor)
        {
            if (neighbor.IsAir) return true;
            BlockDefinition neighborDefinition = BlockRegistry.Get(neighbor.BlockId);
            if (definition.renderLayer == BlockRenderLayer.Water)
                return neighborDefinition.renderLayer != BlockRenderLayer.Water;
            if (definition.renderLayer == BlockRenderLayer.Transparent)
                return neighbor.BlockId != state.BlockId;
            return !BlockRegistry.IsOpaque(neighbor);
        }

        private static int GetTile(BlockDefinition definition, BlockState state,
            VoxelAddress address, int face, int outerFace, int innerFace, int faceCellResolution)
        {
            if (definition.orientationMode == BlockOrientationMode.RadialEastNorthAxis)
            {
                int axisFace = outerFace;
                if (state.Orientation == 2 || state.Orientation == 3)
                {
                    Vector3 east = CubeSphereMapper.GetCellTangentBasis(address.face,
                        address.u, address.v, faceCellResolution).east.ToVector3();
                    axisFace = ClosestFace(east);
                }
                else if (state.Orientation == 4 || state.Orientation == 5)
                {
                    Vector3 north = CubeSphereMapper.GetCellTangentBasis(address.face,
                        address.u, address.v, faceCellResolution).north.ToVector3();
                    axisFace = ClosestFace(north);
                }
                if (face == axisFace || face == OppositeFace(axisFace))
                    return definition.topTile;
                return definition.sideTile;
            }

            if (face == outerFace) return definition.topTile;
            if (face == innerFace) return definition.bottomTile;
            return definition.sideTile;
        }

        private static void AddFace(StableGridChunkMeshData data, Vector3 cellLocal,
            int face, int tile, bool water)
        {
            int first = data.vertices.Count;
            int maxTile = BlockRegistry.AtlasColumns * BlockRegistry.AtlasRows - 1;
            int clampedTile = Math.Max(0, Math.Min(maxTile, tile));
            int column = clampedTile % BlockRegistry.AtlasColumns;
            int row = clampedTile / BlockRegistry.AtlasColumns;
            float width = 1f / BlockRegistry.AtlasColumns;
            float height = 1f / BlockRegistry.AtlasRows;
            float x0 = column * width;
            float y0 = 1f - (row + 1) * height;
            float padding = Math.Min(width, height) * 0.035f;
            float xMin = x0 + padding;
            float xMax = x0 + width - padding;
            float yMin = y0 + padding;
            float yMax = y0 + height - padding;
            Vector2[] faceUv =
            {
                new Vector2(xMin,yMin), new Vector2(xMin,yMax),
                new Vector2(xMax,yMax), new Vector2(xMax,yMin)
            };

            for (int i = 0; i < 4; i++)
            {
                data.vertices.Add(cellLocal + FaceCorners[face, i]);
                data.normals.Add(FaceNormals[face]);
                data.uv.Add(faceUv[i]);
                data.colors.Add(new Color32(255,255,255,255));
            }

            List<int> triangles = water ? data.waterTriangles : data.opaqueTriangles;
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private static long SurfaceKey(VoxelAddress address)
        {
            // faceCellResolution is far below 2^28 in this project, so this is a
            // collision-free packed key for one canonical surface column.
            return ((long)((int)address.face & 7) << 56)
                   | ((long)(uint)address.u << 28)
                   | (uint)address.v;
        }

        private static int ClosestFace(Vector3 direction)
        {
            float ax = Math.Abs(direction.x);
            float ay = Math.Abs(direction.y);
            float az = Math.Abs(direction.z);
            if (ax >= ay && ax >= az) return direction.x >= 0f ? 0 : 1;
            if (ay >= az) return direction.y >= 0f ? 2 : 3;
            return direction.z >= 0f ? 4 : 5;
        }

        private static int OppositeFace(int face)
        {
            return (face & 1) == 0 ? face + 1 : face - 1;
        }
    }
}
