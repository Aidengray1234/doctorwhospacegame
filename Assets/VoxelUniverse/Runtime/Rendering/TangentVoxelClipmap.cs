using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Interaction;
using DoctorWho.VoxelUniverse.Saves;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public struct TangentPatchFrame
    {
        public Vector3 originWorld;
        public Vector3 east;
        public Vector3 up;
        public Vector3 north;
        public Quaternion rotation;
        public Quaternion inverseRotation;
        public int anchorRadial;
        public int radius;
        public int minY;
        public int maxY;

        public Vector3 LocalToWorld(Vector3 local)
        {
            return originWorld + rotation * local;
        }

        public Vector3 WorldToLocal(Vector3 world)
        {
            return inverseRotation * (world - originWorld);
        }

        public bool ContainsCell(Vector3Int cell, int margin)
        {
            return cell.x >= -radius - margin && cell.x <= radius + margin
                && cell.z >= -radius - margin && cell.z <= radius + margin
                && cell.y >= minY - margin && cell.y <= maxY + margin;
        }
    }

    public struct TangentVoxelRayHit
    {
        public bool valid;
        public float distance;
        public Vector3 point;
        public Vector3Int cell;
        public Vector3Int adjacentCell;
        public VoxelAddress address;
        public VoxelAddress adjacentAddress;
        public BlockState block;
        public VoxelHitFace face;
    }

    /// <summary>
    /// Player-centred no-warp renderer. Logical terrain remains spherical, but nearby
    /// blocks are sampled into a true one-metre Cartesian cube grid. Curvature appears
    /// as Minecraft-style height steps instead of bending individual blocks.
    /// </summary>
    public sealed class TangentVoxelClipmap : MonoBehaviour
    {
        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>(8192);
            public readonly List<Vector3> normals = new List<Vector3>(8192);
            public readonly List<Vector2> uv = new List<Vector2>(8192);
            public readonly List<Color32> colors = new List<Color32>(8192);
            public readonly List<int> opaqueTriangles = new List<int>(12288);
            public readonly List<int> transparentTriangles = new List<int>(4096);
        }

        private struct TileRequest
        {
            public int x;
            public int z;
        }

        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material opaqueMaterial;
        [SerializeField] private Material transparentMaterial;

        private readonly Queue<TileRequest> pendingTiles = new Queue<TileRequest>();
        private GameObject activeRoot;
        private GameObject buildingRoot;
        private TangentPatchFrame activeFrame;
        private TangentPatchFrame buildingFrame;
        private VoxelSaveSystem saveSystem;
        private int observedEditCount = -1;
        private bool forceRebuild = true;
        private int generation;
        private int builtTiles;
        private int totalTiles;
        private float lastBuildMilliseconds;

        public bool Ready
        {
            get
            {
                return activeRoot != null && activeRoot.activeSelf && world != null
                    && observer != null && world.Settings != null
                    && world.GetAltitude(observer.position) <= world.Settings.tangentPatchMaxAltitude;
            }
        }

        public TangentPatchFrame ActiveFrame { get { return activeFrame; } }
        public int BuiltTiles { get { return builtTiles; } }
        public int TotalTiles { get { return totalTiles; } }
        public float LastBuildMilliseconds { get { return lastBuildMilliseconds; } }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material opaque, Material transparent)
        {
            world = voxelWorld;
            observer = trackingObserver;
            opaqueMaterial = opaque;
            transparentMaterial = transparent != null ? transparent : opaque;
            saveSystem = world != null ? world.GetComponent<VoxelSaveSystem>() : null;
            forceRebuild = true;
        }

        private void Awake()
        {
            if (world == null) world = GetComponent<VoxelUniverseWorld>();
            if (saveSystem == null && world != null) saveSystem = world.GetComponent<VoxelSaveSystem>();
        }

        private void Start()
        {
            ValidateCubeMath();
            forceRebuild = true;
        }

        private void Update()
        {
            if (world == null || observer == null || world.Settings == null) return;
            DisableLegacyNearSectionRendering();

            float altitude = world.GetAltitude(observer.position);
            bool nearEnabled = altitude <= world.Settings.tangentPatchMaxAltitude;
            if (activeRoot != null) activeRoot.SetActive(nearEnabled);
            if (!nearEnabled)
            {
                CancelBuild();
                return;
            }

            int editCount = saveSystem != null ? saveSystem.EditCount : 0;
            if (observedEditCount != editCount)
            {
                observedEditCount = editCount;
                forceRebuild = true;
            }

            if (buildingRoot == null && (forceRebuild || ShouldRecenter()))
                BeginBuild();

            if (buildingRoot != null)
            {
                int budget = Mathf.Max(1, world.Settings.tangentPatchTilesPerFrame);
                float started = Time.realtimeSinceStartup;
                for (int i = 0; i < budget && pendingTiles.Count > 0; i++)
                {
                    BuildNextTile();
                    builtTiles++;
                }
                lastBuildMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;
                if (pendingTiles.Count == 0) FinishBuild();
            }
        }

        public void NotifyLogicalEdit()
        {
            forceRebuild = true;
        }

        public bool ContainsWorldPosition(Vector3 worldPosition, int margin)
        {
            if (!Ready) return false;
            Vector3 local = activeFrame.WorldToLocal(worldPosition);
            Vector3Int cell = RoundToCell(local);
            return activeFrame.ContainsCell(cell, margin);
        }

        public bool TryGetCell(Vector3 worldPosition, out Vector3Int cell)
        {
            cell = default(Vector3Int);
            if (!Ready) return false;
            cell = RoundToCell(activeFrame.WorldToLocal(worldPosition));
            return activeFrame.ContainsCell(cell, 0);
        }

        public Vector3 CellCenterWorld(Vector3Int cell)
        {
            return activeFrame.LocalToWorld(new Vector3(cell.x, cell.y, cell.z));
        }

        public void GetCellAxes(out Vector3 east, out Vector3 up, out Vector3 north)
        {
            east = activeFrame.east;
            up = activeFrame.up;
            north = activeFrame.north;
        }

        public BlockState SampleCell(Vector3Int cell, out VoxelAddress address)
        {
            Vector3 center = activeFrame.LocalToWorld(new Vector3(cell.x, cell.y, cell.z));
            address = world.GetAddress(center);
            return world.GetBlock(address);
        }

        public bool IsSolidCell(Vector3Int cell)
        {
            if (!Ready || !activeFrame.ContainsCell(cell, 1)) return false;
            VoxelAddress address;
            return BlockRegistry.IsSolid(SampleCell(cell, out address));
        }

        public bool TryFindCellForAddress(VoxelAddress address, Vector3 nearWorldPosition,
            out Vector3Int result)
        {
            result = default(Vector3Int);
            if (!Ready) return false;
            Vector3Int estimate = RoundToCell(activeFrame.WorldToLocal(nearWorldPosition));
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int y = -3; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
            {
                Vector3Int cell = estimate + new Vector3Int(x, y, z);
                if (!activeFrame.ContainsCell(cell, 0)) continue;
                VoxelAddress sampledAddress;
                SampleCell(cell, out sampledAddress);
                if (!sampledAddress.Equals(address)) continue;
                float distance = (CellCenterWorld(cell) - nearWorldPosition).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                result = cell;
                found = true;
            }
            return found;
        }

        public bool Raycast(Ray worldRay, float maxDistance, out TangentVoxelRayHit hit)
        {
            hit = new TangentVoxelRayHit();
            if (!Ready) return false;

            Vector3 origin = activeFrame.WorldToLocal(worldRay.origin) + Vector3.one * 0.5f;
            Vector3 direction = activeFrame.inverseRotation * worldRay.direction;
            if (direction.sqrMagnitude < 0.999f) direction.Normalize();

            Vector3Int cell = new Vector3Int(
                Mathf.FloorToInt(origin.x),
                Mathf.FloorToInt(origin.y),
                Mathf.FloorToInt(origin.z));
            Vector3Int step = new Vector3Int(
                direction.x >= 0f ? 1 : -1,
                direction.y >= 0f ? 1 : -1,
                direction.z >= 0f ? 1 : -1);

            float tDeltaX = Mathf.Abs(direction.x) > 0.000001f
                ? Mathf.Abs(1f / direction.x) : float.PositiveInfinity;
            float tDeltaY = Mathf.Abs(direction.y) > 0.000001f
                ? Mathf.Abs(1f / direction.y) : float.PositiveInfinity;
            float tDeltaZ = Mathf.Abs(direction.z) > 0.000001f
                ? Mathf.Abs(1f / direction.z) : float.PositiveInfinity;
            float tMaxX = FirstBoundary(origin.x, direction.x, cell.x, step.x);
            float tMaxY = FirstBoundary(origin.y, direction.y, cell.y, step.y);
            float tMaxZ = FirstBoundary(origin.z, direction.z, cell.z, step.z);

            Vector3Int previous = cell;
            VoxelHitFace enteredFace = VoxelHitFace.Outer;
            float distance = 0f;
            for (int iteration = 0; iteration < 512 && distance <= maxDistance; iteration++)
            {
                Vector3Int centeredCell = cell;
                if (activeFrame.ContainsCell(centeredCell, 0))
                {
                    VoxelAddress address;
                    BlockState block = SampleCell(centeredCell, out address);
                    BlockDefinition definition = BlockRegistry.Get(block.BlockId);
                    if (!block.IsAir && !definition.liquid)
                    {
                        VoxelAddress adjacentAddress;
                        SampleCell(previous, out adjacentAddress);
                        hit.valid = true;
                        hit.distance = distance;
                        hit.point = worldRay.origin + worldRay.direction * distance;
                        hit.cell = centeredCell;
                        hit.adjacentCell = previous;
                        hit.address = address;
                        hit.adjacentAddress = adjacentAddress;
                        hit.block = block;
                        hit.face = enteredFace;
                        return true;
                    }
                }
                else if (iteration > 0) return false;

                previous = cell;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    cell.x += step.x;
                    distance = tMaxX;
                    tMaxX += tDeltaX;
                    enteredFace = step.x > 0 ? VoxelHitFace.West : VoxelHitFace.East;
                }
                else if (tMaxY <= tMaxZ)
                {
                    cell.y += step.y;
                    distance = tMaxY;
                    tMaxY += tDeltaY;
                    enteredFace = step.y > 0 ? VoxelHitFace.Inner : VoxelHitFace.Outer;
                }
                else
                {
                    cell.z += step.z;
                    distance = tMaxZ;
                    tMaxZ += tDeltaZ;
                    enteredFace = step.z > 0 ? VoxelHitFace.South : VoxelHitFace.North;
                }
            }
            return false;
        }

        private static float FirstBoundary(float origin, float direction, int cell, int step)
        {
            if (Mathf.Abs(direction) <= 0.000001f) return float.PositiveInfinity;
            float boundary = step > 0 ? cell + 1f : cell;
            return Mathf.Max(0f, (boundary - origin) / direction);
        }

        private bool ShouldRecenter()
        {
            if (activeRoot == null) return true;
            Vector3 local = activeFrame.WorldToLocal(observer.position);
            float horizontal = new Vector2(local.x, local.z).magnitude;
            Vector3 currentUp = (observer.position - world.Center).normalized;
            float angle = Vector3.Angle(activeFrame.up, currentUp);
            return horizontal >= world.Settings.tangentPatchRecenterDistance || angle >= 2.25f;
        }

        private void BeginBuild()
        {
            forceRebuild = false;
            generation++;
            buildingFrame = CreateFrame();
            buildingRoot = new GameObject("No-Warp Tangent Patch Building " + generation);
            buildingRoot.transform.SetParent(transform, true);
            buildingRoot.transform.position = buildingFrame.originWorld;
            buildingRoot.transform.rotation = buildingFrame.rotation;
            buildingRoot.SetActive(false);

            pendingTiles.Clear();
            int radius = buildingFrame.radius;
            int tileSize = world.Settings.tangentPatchTileSize;
            int minimumTile = Mathf.FloorToInt((float)-radius / tileSize);
            int maximumTile = Mathf.FloorToInt((float)radius / tileSize);
            for (int z = minimumTile; z <= maximumTile; z++)
            for (int x = minimumTile; x <= maximumTile; x++)
                pendingTiles.Enqueue(new TileRequest { x = x, z = z });
            totalTiles = pendingTiles.Count;
            builtTiles = 0;
        }

        private TangentPatchFrame CreateFrame()
        {
            Vector3 relative = observer.position - world.Center;
            Vector3 up = relative.sqrMagnitude > 0.001f ? relative.normalized : Vector3.up;
            VoxelAddress centerAddress = world.GetAddress(observer.position);
            FaceBasis logicalBasis = world.GetBlockBasis(centerAddress);

            // Parallel-transport the previous patch east vector onto the new tangent plane.
            // This prevents a camera turn or a cube-face seam from rotating the terrain grid.
            Vector3 east = activeRoot != null
                ? Vector3.ProjectOnPlane(activeFrame.east, up)
                : Vector3.ProjectOnPlane(logicalBasis.east.ToVector3(), up);
            if (east.sqrMagnitude < 0.01f)
                east = Vector3.ProjectOnPlane(logicalBasis.north.ToVector3(), up);
            if (east.sqrMagnitude < 0.01f) east = Vector3.Cross(Vector3.up, up);
            if (east.sqrMagnitude < 0.01f) east = Vector3.Cross(Vector3.forward, up);
            east.Normalize();
            Vector3 north = Vector3.Cross(east, up).normalized;
            if (activeRoot == null && Vector3.Dot(north,
                logicalBasis.north.ToVector3()) < 0f)
            {
                east = -east;
                north = -north;
            }
            Quaternion rotation = Quaternion.LookRotation(north, up);

            int surface = world.GetSurfaceHeight(centerAddress.face, centerAddress.u, centerAddress.v);
            Vector3 origin = world.Center + up * (world.Settings.groundRadius + surface + 0.5f);
            return new TangentPatchFrame
            {
                originWorld = origin,
                east = east,
                up = up,
                north = north,
                rotation = rotation,
                inverseRotation = Quaternion.Inverse(rotation),
                anchorRadial = surface,
                radius = world.Settings.tangentPatchRadius,
                minY = -world.Settings.tangentPatchBlocksBelow,
                maxY = world.Settings.tangentPatchBlocksAbove
            };
        }

        private void BuildNextTile()
        {
            TileRequest request = pendingTiles.Dequeue();
            int tileSize = world.Settings.tangentPatchTileSize;
            int startX = Mathf.Max(-buildingFrame.radius, request.x * tileSize);
            int endX = Mathf.Min(buildingFrame.radius, request.x * tileSize + tileSize - 1);
            int startZ = Mathf.Max(-buildingFrame.radius, request.z * tileSize);
            int endZ = Mathf.Min(buildingFrame.radius, request.z * tileSize + tileSize - 1);
            if (startX > endX || startZ > endZ) return;

            int sizeX = endX - startX + 3;
            int sizeZ = endZ - startZ + 3;
            int sizeY = buildingFrame.maxY - buildingFrame.minY + 3;
            int sampleStartX = startX - 1;
            int sampleStartZ = startZ - 1;
            int sampleStartY = buildingFrame.minY - 1;
            BlockState[] samples = new BlockState[sizeX * sizeY * sizeZ];

            for (int z = 0; z < sizeZ; z++)
            for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
            {
                Vector3 local = new Vector3(sampleStartX + x, sampleStartY + y, sampleStartZ + z);
                Vector3 worldCenter = buildingFrame.LocalToWorld(local);
                VoxelAddress address = world.GetAddress(worldCenter);
                samples[Index(x, y, z, sizeX, sizeY)] = world.GetBlock(address);
            }

            MeshBuilder builder = new MeshBuilder();
            for (int z = startZ; z <= endZ; z++)
            for (int y = buildingFrame.minY; y <= buildingFrame.maxY; y++)
            for (int x = startX; x <= endX; x++)
            {
                int sx = x - sampleStartX;
                int sy = y - sampleStartY;
                int sz = z - sampleStartZ;
                BlockState state = samples[Index(sx, sy, sz, sizeX, sizeY)];
                BlockDefinition definition = BlockRegistry.Get(state.BlockId);
                if (definition.renderLayer == BlockRenderLayer.None) continue;

                AddFaceIfVisible(builder, state, definition, BlockTextureFace.Outer,
                    samples[Index(sx, sy + 1, sz, sizeX, sizeY)], new Vector3(x, y, z));
                AddFaceIfVisible(builder, state, definition, BlockTextureFace.Inner,
                    samples[Index(sx, sy - 1, sz, sizeX, sizeY)], new Vector3(x, y, z));
                AddFaceIfVisible(builder, state, definition, BlockTextureFace.West,
                    samples[Index(sx - 1, sy, sz, sizeX, sizeY)], new Vector3(x, y, z));
                AddFaceIfVisible(builder, state, definition, BlockTextureFace.East,
                    samples[Index(sx + 1, sy, sz, sizeX, sizeY)], new Vector3(x, y, z));
                AddFaceIfVisible(builder, state, definition, BlockTextureFace.South,
                    samples[Index(sx, sy, sz - 1, sizeX, sizeY)], new Vector3(x, y, z));
                AddFaceIfVisible(builder, state, definition, BlockTextureFace.North,
                    samples[Index(sx, sy, sz + 1, sizeX, sizeY)], new Vector3(x, y, z));
            }

            if (builder.vertices.Count == 0) return;
            GameObject tile = new GameObject("Cube Tile " + request.x + "," + request.z);
            tile.transform.SetParent(buildingRoot.transform, false);
            MeshFilter filter = tile.AddComponent<MeshFilter>();
            MeshRenderer renderer = tile.AddComponent<MeshRenderer>();
            Mesh mesh = new Mesh();
            mesh.name = tile.name;
            mesh.indexFormat = builder.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(builder.vertices);
            mesh.SetNormals(builder.normals);
            mesh.SetUVs(0, builder.uv);
            mesh.SetColors(builder.colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(builder.opaqueTriangles, 0, true);
            mesh.SetTriangles(builder.transparentTriangles, 1, true);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { opaqueMaterial, transparentMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static int Index(int x, int y, int z, int sizeX, int sizeY)
        {
            return x + sizeX * (y + sizeY * z);
        }

        private static void AddFaceIfVisible(MeshBuilder builder, BlockState state,
            BlockDefinition definition, BlockTextureFace face, BlockState neighbor,
            Vector3 center)
        {
            BlockDefinition neighborDefinition = BlockRegistry.Get(neighbor.BlockId);
            bool visible;
            if (definition.renderLayer == BlockRenderLayer.Water)
                visible = neighborDefinition.renderLayer != BlockRenderLayer.Water;
            else if (definition.renderLayer == BlockRenderLayer.Transparent)
                visible = neighbor.BlockId != state.BlockId;
            else
                visible = !neighborDefinition.solid
                    || neighborDefinition.renderLayer == BlockRenderLayer.Transparent
                    || neighborDefinition.renderLayer == BlockRenderLayer.Water;
            if (!visible) return;
            AddCubeFace(builder, center, state, definition, face);
        }

        private static void AddCubeFace(MeshBuilder builder, Vector3 center, BlockState state,
            BlockDefinition definition, BlockTextureFace face)
        {
            const float half = 0.5f;
            Vector3 a;
            Vector3 b;
            Vector3 c;
            Vector3 d;
            Vector3 normal;
            float shade;
            switch (face)
            {
                case BlockTextureFace.Outer:
                    a = center + new Vector3(-half, half, -half);
                    b = center + new Vector3(-half, half, half);
                    c = center + new Vector3(half, half, half);
                    d = center + new Vector3(half, half, -half);
                    normal = Vector3.up; shade = 1f; break;
                case BlockTextureFace.Inner:
                    a = center + new Vector3(-half, -half, -half);
                    b = center + new Vector3(half, -half, -half);
                    c = center + new Vector3(half, -half, half);
                    d = center + new Vector3(-half, -half, half);
                    normal = Vector3.down; shade = 0.56f; break;
                case BlockTextureFace.West:
                    a = center + new Vector3(-half, -half, -half);
                    b = center + new Vector3(-half, -half, half);
                    c = center + new Vector3(-half, half, half);
                    d = center + new Vector3(-half, half, -half);
                    normal = Vector3.left; shade = 0.76f; break;
                case BlockTextureFace.East:
                    a = center + new Vector3(half, -half, -half);
                    b = center + new Vector3(half, half, -half);
                    c = center + new Vector3(half, half, half);
                    d = center + new Vector3(half, -half, half);
                    normal = Vector3.right; shade = 0.86f; break;
                case BlockTextureFace.South:
                    a = center + new Vector3(-half, -half, -half);
                    b = center + new Vector3(-half, half, -half);
                    c = center + new Vector3(half, half, -half);
                    d = center + new Vector3(half, -half, -half);
                    normal = Vector3.back; shade = 0.70f; break;
                default:
                    a = center + new Vector3(-half, -half, half);
                    b = center + new Vector3(half, -half, half);
                    c = center + new Vector3(half, half, half);
                    d = center + new Vector3(-half, half, half);
                    normal = Vector3.forward; shade = 0.81f; break;
            }

            int tile = definition.GetTextureTile(face, state.Orientation);
            Color32 color = Shade(definition.GetFallbackColor(face), shade,
                definition.renderLayer == BlockRenderLayer.Water
                    || definition.renderLayer == BlockRenderLayer.Transparent);
            bool transparent = definition.renderLayer == BlockRenderLayer.Water
                || definition.renderLayer == BlockRenderLayer.Transparent;
            AddQuad(builder, a, b, c, d, normal, color, tile, transparent);
        }

        private static Color32 Shade(Color32 source, float shade, bool preserveTint)
        {
            if (!preserveTint)
            {
                byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * shade), 0, 255);
                return new Color32(value, value, value, 255);
            }
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.r * shade), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.g * shade), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.b * shade), 0, 255), source.a);
        }

        private static void AddQuad(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c,
            Vector3 d, Vector3 normal, Color32 color, int tile, bool transparent)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                Vector3 swap = b;
                b = d;
                d = swap;
            }
            int start = builder.vertices.Count;
            builder.vertices.Add(a);
            builder.vertices.Add(b);
            builder.vertices.Add(c);
            builder.vertices.Add(d);
            for (int i = 0; i < 4; i++)
            {
                builder.normals.Add(normal);
                builder.colors.Add(color);
            }

            Rect rect = BlockRegistry.TileUv(tile);
            const float inset = 0.0015f;
            float x0 = rect.xMin + inset;
            float x1 = rect.xMax - inset;
            float y0 = rect.yMin + inset;
            float y1 = rect.yMax - inset;
            builder.uv.Add(new Vector2(x0, y0));
            builder.uv.Add(new Vector2(x0, y1));
            builder.uv.Add(new Vector2(x1, y1));
            builder.uv.Add(new Vector2(x1, y0));

            List<int> triangles = transparent
                ? builder.transparentTriangles : builder.opaqueTriangles;
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private void FinishBuild()
        {
            GameObject old = activeRoot;
            activeRoot = buildingRoot;
            activeFrame = buildingFrame;
            buildingRoot = null;
            activeRoot.name = "No-Warp Tangent Cube Patch";
            activeRoot.SetActive(true);
            if (old != null) DestroyOwned(old);
        }

        private void CancelBuild()
        {
            pendingTiles.Clear();
            if (buildingRoot != null) DestroyOwned(buildingRoot);
            buildingRoot = null;
        }

        private void DisableLegacyNearSectionRendering()
        {
            if (world == null) return;
            Transform legacy = world.transform.Find("Near Voxel Sections");
            if (legacy != null && legacy.gameObject.activeSelf) legacy.gameObject.SetActive(false);
        }

        private static Vector3Int RoundToCell(Vector3 local)
        {
            return new Vector3Int(
                Mathf.FloorToInt(local.x + 0.5f),
                Mathf.FloorToInt(local.y + 0.5f),
                Mathf.FloorToInt(local.z + 0.5f));
        }

        private static void ValidateCubeMath()
        {
            Vector3 center = Vector3.zero;
            Vector3 eastNeighbor = Vector3.right;
            Vector3 northNeighbor = Vector3.forward;
            bool valid = Mathf.Abs(Vector3.Distance(center, eastNeighbor) - 1f) < 0.00001f
                && Mathf.Abs(Vector3.Distance(center, northNeighbor) - 1f) < 0.00001f;
            if (!valid) Debug.LogError("[Voxel Universe] No-warp cube-grid self-test failed.");
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            CancelBuild();
            if (activeRoot != null) DestroyOwned(activeRoot);
        }
    }
}
