using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.BlockPlanets
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class BlockPlanetChunk : MonoBehaviour
    {
        private static readonly Vector3Int[] NeighborOffsets =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
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

        private BlockPlanetWorld world;
        private BlockChunkCoord coord;
        private Mesh renderMesh;
        private Mesh collisionMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        public BlockChunkCoord Coord => coord;

        public void Initialize(BlockPlanetWorld owner, BlockChunkCoord value, Material opaqueMaterial, Material transparentMaterial)
        {
            world = owner;
            coord = value;
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
            meshRenderer.sharedMaterials = new[] { opaqueMaterial, transparentMaterial };
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            gameObject.layer = owner.gameObject.layer;
            Rebuild();
        }

        public void Rebuild()
        {
            if (world == null || world.Settings == null) return;
            int size = world.Settings.chunkSize;
            var vertices = new List<Vector3>(6144);
            var uvs = new List<Vector2>(6144);
            var colors = new List<Color>(6144);
            var opaqueTriangles = new List<int>(9216);
            var transparentTriangles = new List<int>(2048);
            var colliderVertices = new List<Vector3>(4096);
            var colliderTriangles = new List<int>(6144);

            int baseX = coord.x * size;
            int baseY = coord.y * size;
            int baseZ = coord.z * size;

            for (int y = 0; y < size; y++)
            {
                int gy = baseY + y;
                if (gy < world.Settings.minimumRadialBlock || gy >= world.Settings.maximumRadialBlock) continue;
                for (int z = 0; z < size; z++)
                {
                    int gz = baseZ + z;
                    for (int x = 0; x < size; x++)
                    {
                        int gx = baseX + x;
                        BlockId block = world.GetBlock(coord.face, gx, gy, gz);
                        if (!BlockCatalog.IsRenderable(block)) continue;

                        for (int side = 0; side < 6; side++)
                        {
                            Vector3Int offset = NeighborOffsets[side];
                            BlockId adjacent = world.GetBlock(coord.face, gx + offset.x, gy + offset.y, gz + offset.z);
                            if (!ShouldRenderFace(block, adjacent)) continue;

                            bool transparent = BlockCatalog.IsTransparent(block);
                            AddRenderFace(vertices, uvs, colors,
                                transparent ? transparentTriangles : opaqueTriangles,
                                coord.face, gx, gy, gz, side, block);

                            if (BlockCatalog.IsSolid(block))
                                AddColliderFace(colliderVertices, colliderTriangles, coord.face, gx, gy, gz, side);
                        }
                    }
                }
            }

            if (renderMesh == null)
            {
                renderMesh = new Mesh { name = coord + "_Render", indexFormat = IndexFormat.UInt32 };
                renderMesh.MarkDynamic();
            }
            renderMesh.Clear();
            renderMesh.SetVertices(vertices);
            renderMesh.SetUVs(0, uvs);
            renderMesh.SetColors(colors);
            renderMesh.subMeshCount = 2;
            renderMesh.SetTriangles(opaqueTriangles, 0, true);
            renderMesh.SetTriangles(transparentTriangles, 1, true);
            renderMesh.RecalculateNormals();
            renderMesh.RecalculateBounds();
            meshFilter.sharedMesh = renderMesh;

            if (collisionMesh == null)
                collisionMesh = new Mesh { name = coord + "_Collider", indexFormat = IndexFormat.UInt32 };
            collisionMesh.Clear();
            collisionMesh.SetVertices(colliderVertices);
            collisionMesh.SetTriangles(colliderTriangles, 0, true);
            collisionMesh.RecalculateBounds();
            meshCollider.sharedMesh = null;
            if (colliderTriangles.Count > 0) meshCollider.sharedMesh = collisionMesh;
        }

        private static bool ShouldRenderFace(BlockId block, BlockId adjacent)
        {
            if (!BlockCatalog.IsRenderable(adjacent)) return true;
            bool blockTransparent = BlockCatalog.IsTransparent(block);
            bool adjacentTransparent = BlockCatalog.IsTransparent(adjacent);
            if (!blockTransparent && !adjacentTransparent) return false;
            if (block == adjacent) return false;
            return blockTransparent != adjacentTransparent || block != adjacent;
        }

        private void AddRenderFace(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors,
            List<int> triangles, BlockPlanetFace face, int gx, int gy, int gz, int side, BlockId block)
        {
            Vector3[] points = GetFacePoints(face, gx, gy, gz, side);
            int start = vertices.Count;
            vertices.AddRange(points);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);

            Rect uv = BlockCatalog.TileUv(BlockCatalog.Tile(block, side));
            uvs.Add(new Vector2(uv.xMin, uv.yMin));
            uvs.Add(new Vector2(uv.xMin, uv.yMax));
            uvs.Add(new Vector2(uv.xMax, uv.yMax));
            uvs.Add(new Vector2(uv.xMax, uv.yMin));

            float shade = side == 2 ? 1f : side == 3 ? 0.58f : (side == 0 || side == 4 ? 0.82f : 0.70f);
            Color color = new Color(shade, shade, shade, BlockCatalog.IsTransparent(block) ? 0.76f : 1f);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        }

        private void AddColliderFace(List<Vector3> vertices, List<int> triangles,
            BlockPlanetFace face, int gx, int gy, int gz, int side)
        {
            Vector3[] points = GetFacePoints(face, gx, gy, gz, side);
            int start = vertices.Count;
            vertices.AddRange(points);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private Vector3[] GetFacePoints(BlockPlanetFace face, int gx, int gy, int gz, int side)
        {
            Vector3[] points = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = FaceCorners[side, i];
                points[i] = world.GridPoint(face, gx + corner.x, gy + corner.y, gz + corner.z) - world.Center;
            }

            Vector3 blockCenter = world.GridPoint(face, gx + 0.5f, gy + 0.5f, gz + 0.5f);
            Vector3Int offset = NeighborOffsets[side];
            Vector3 outside = world.GridPoint(face, gx + 0.5f + offset.x, gy + 0.5f + offset.y, gz + 0.5f + offset.z);
            Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]);
            if (Vector3.Dot(normal, outside - blockCenter) < 0f)
            {
                Vector3 temp = points[1];
                points[1] = points[3];
                points[3] = temp;
            }
            return points;
        }

        private void OnDestroy()
        {
            if (renderMesh != null) Destroy(renderMesh);
            if (collisionMesh != null) Destroy(collisionMesh);
        }
    }
}
