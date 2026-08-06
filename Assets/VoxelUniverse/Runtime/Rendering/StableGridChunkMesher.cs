using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
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

    internal sealed class StableGridChunkBuilder
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

        private static readonly Vector2[] BaseUv =
        {
            new Vector2(0f,0f), new Vector2(0f,1f),
            new Vector2(1f,1f), new Vector2(1f,0f)
        };

        private readonly StableCartesianVoxelGrid grid;
        private readonly Int3 chunkKey;
        private readonly Int3 origin;
        private int nextCell;
        public readonly StableGridChunkMeshData data = new StableGridChunkMeshData();

        public StableGridChunkBuilder(StableCartesianVoxelGrid owner, Int3 key)
        {
            grid = owner;
            chunkKey = key;
            origin = new Int3(key.x * 16, key.y * 16, key.z * 16);
        }

        public Int3 ChunkKey { get { return chunkKey; } }
        public bool Complete { get { return nextCell >= 4096; } }

        public int Process(int cellBudget)
        {
            int processed = 0;
            while (processed < cellBudget && nextCell < 4096)
            {
                int index = nextCell++;
                int x = index & 15;
                int y = (index >> 4) & 15;
                int z = (index >> 8) & 15;
                BuildCell(new Int3(origin.x + x, origin.y + y, origin.z + z),
                    new Vector3(x, y, z));
                processed++;
            }
            return processed;
        }

        private void BuildCell(Int3 cell, Vector3 local)
        {
            BlockState state = grid.GetBlock(cell);
            if (state.IsAir) return;
            BlockDefinition definition = BlockRegistry.Get(state.BlockId);
            bool water = definition.renderLayer == BlockRenderLayer.Water;
            bool fullCube = definition.collisionShape == BlockCollisionShape.FullCube;
            if (!water && !fullCube) return;

            Vector3 radial = grid.CellCenterLocal(cell).normalized;
            int outerFace = ClosestFace(radial);
            int innerFace = OppositeFace(outerFace);
            VoxelAddress address = grid.AddressForCell(cell);

            for (int face = 0; face < 6; face++)
            {
                BlockState neighbor = grid.GetBlock(cell + NeighborOffsets[face]);
                if (!ShouldRenderFace(definition, state, neighbor)) continue;
                int tile = GetTile(definition, state, address, face, outerFace, innerFace);
                AddFace(local, face, tile, water);
            }
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

        private int GetTile(BlockDefinition definition, BlockState state, VoxelAddress address,
            int face, int outerFace, int innerFace)
        {
            if (definition.orientationMode == BlockOrientationMode.RadialEastNorthAxis)
            {
                int axisFace = outerFace;
                if (state.Orientation == 2 || state.Orientation == 3)
                {
                    Vector3 east = grid.World.GetBlockBasis(address).east.ToVector3();
                    axisFace = ClosestFace(east);
                }
                else if (state.Orientation == 4 || state.Orientation == 5)
                {
                    Vector3 north = grid.World.GetBlockBasis(address).north.ToVector3();
                    axisFace = ClosestFace(north);
                }
                if (face == axisFace || face == OppositeFace(axisFace)) return definition.topTile;
                return definition.sideTile;
            }

            if (face == outerFace) return definition.topTile;
            if (face == innerFace) return definition.bottomTile;
            return definition.sideTile;
        }

        private void AddFace(Vector3 cellLocal, int face, int tile, bool water)
        {
            int first = data.vertices.Count;
            Rect rect = BlockRegistry.TileUv(tile);
            float padding = Mathf.Min(rect.width, rect.height) * 0.025f;
            float xMin = rect.xMin + padding;
            float xMax = rect.xMax - padding;
            float yMin = rect.yMin + padding;
            float yMax = rect.yMax - padding;
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

        private static int ClosestFace(Vector3 direction)
        {
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);
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
