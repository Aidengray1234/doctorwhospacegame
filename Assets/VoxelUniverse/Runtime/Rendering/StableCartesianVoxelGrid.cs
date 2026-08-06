using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public struct StableGridRayHit
    {
        public Int3 cell;
        public Int3 adjacentCell;
        public VoxelAddress address;
        public VoxelAddress adjacentAddress;
        public BlockState block;
        public Vector3 normal;
        public float distance;
    }

    public sealed class StableCartesianVoxelGrid : MonoBehaviour
    {
        private sealed class ChunkRecord
        {
            public GameObject gameObject;
            public Mesh mesh;
            public float lastRequired;
            public bool queued;
            public bool dirty;
        }

        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material opaqueMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private StableGridEditStore editStore;
        [SerializeField, Range(2, 7)] private int chunkRadius = 4;
        [SerializeField, Range(2, 6)] private int verticalChunkRadius = 4;
        [SerializeField, Range(256, 6000)] private int cellBuildBudgetPerFrame = 1800;
        [SerializeField, Min(1f)] private float unloadDelaySeconds = 14f;
        [SerializeField, Range(48, 320)] private int maximumLoadedChunks = 176;

        private readonly Dictionary<Int3, ChunkRecord> chunks =
            new Dictionary<Int3, ChunkRecord>();
        private readonly Queue<Int3> buildQueue = new Queue<Int3>();
        private readonly HashSet<Int3> queuedKeys = new HashSet<Int3>();
        private StableGridChunkBuilder activeBuilder;
        private Int3 lastObserverChunk = new Int3(int.MinValue, int.MinValue, int.MinValue);
        private float nextRefreshTime;
        private Transform chunkRoot;
        private bool initialized;
        private int completedThisFrame;

        public VoxelUniverseWorld World { get { return world; } }
        public int LoadedChunkCount { get { return chunks.Count; } }
        public int QueuedChunkCount { get { return buildQueue.Count + (activeBuilder != null ? 1 : 0); } }
        public int CompletedThisFrame { get { return completedThisFrame; } }
        public bool HasReadyTerrain { get { return chunks.Count > 0 && CountReadyChunks() > 0; } }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material opaque, Material water, StableGridEditStore store)
        {
            world = voxelWorld;
            observer = trackingObserver;
            opaqueMaterial = opaque;
            waterMaterial = water != null ? water : opaque;
            editStore = store;
            Initialize();
        }

        private void Awake() { Initialize(); }
        private void OnEnable() { Initialize(); }

        private void Initialize()
        {
            if (initialized || world == null) return;
            initialized = true;
            EnsureRoot();
            if (editStore == null) editStore = GetComponent<StableGridEditStore>();
            if (editStore != null) editStore.Configure(world);
            if (observer == null)
            {
                DoctorWho.VoxelUniverse.Player.VoxelPlayerController player =
                    FindObjectOfType<DoctorWho.VoxelUniverse.Player.VoxelPlayerController>();
                if (player != null) observer = player.transform;
            }
            nextRefreshTime = 0f;
        }

        private void Update()
        {
            completedThisFrame = 0;
            if (world == null || world.Settings == null || observer == null) return;
            Int3 observerChunk = CellToChunk(WorldToCell(observer.position));
            if (observerChunk != lastObserverChunk || Time.unscaledTime >= nextRefreshTime)
            {
                lastObserverChunk = observerChunk;
                RefreshRequiredChunks(observerChunk);
                nextRefreshTime = Time.unscaledTime + 0.25f;
            }
            ProcessBuildBudget();
            UnloadExpired();
            TrimBudget();
        }

        public Int3 WorldToCell(Vector3 worldPosition)
        {
            Vector3 local = worldPosition - world.Center;
            return new Int3(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
        }

        public Vector3 CellCenterLocal(Int3 cell)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
        }

        public Vector3 CellCenterWorld(Int3 cell)
        {
            return world.Center + CellCenterLocal(cell);
        }

        public VoxelAddress AddressForCell(Int3 cell)
        {
            return world.GetAddress(CellCenterWorld(cell));
        }

        public Int3 CellForAddress(VoxelAddress address)
        {
            Vector3 local = world.GetBlockCenter(address) - world.Center;
            return new Int3(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
        }

        public BlockState GetBlock(Int3 cell)
        {
            uint packed;
            if (editStore != null && editStore.TryGet(cell, out packed))
                return BlockState.FromPacked(packed);
            return world.GetBlock(AddressForCell(cell));
        }

        public void SetBlock(Int3 cell, BlockState state)
        {
            if (editStore != null) editStore.Set(cell, state.Packed);
            VoxelAddress address = AddressForCell(cell);
            world.SetBlock(address, state);
            MarkCellDirty(cell);
        }

        public bool TryRaycast(Ray ray, float maxDistance, out StableGridRayHit hit)
        {
            hit = new StableGridRayHit();
            if (world == null || maxDistance <= 0f) return false;
            Vector3 origin = ray.origin - world.Center;
            Vector3 direction = ray.direction.normalized;
            Int3 cell = new Int3(Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y),
                Mathf.FloorToInt(origin.z));
            int stepX = direction.x > 0f ? 1 : direction.x < 0f ? -1 : 0;
            int stepY = direction.y > 0f ? 1 : direction.y < 0f ? -1 : 0;
            int stepZ = direction.z > 0f ? 1 : direction.z < 0f ? -1 : 0;
            float tDeltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.x);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.y);
            float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.z);
            float nextX = stepX > 0 ? cell.x + 1f : cell.x;
            float nextY = stepY > 0 ? cell.y + 1f : cell.y;
            float nextZ = stepZ > 0 ? cell.z + 1f : cell.z;
            float tMaxX = stepX == 0 ? float.PositiveInfinity : (nextX - origin.x) / direction.x;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (nextY - origin.y) / direction.y;
            float tMaxZ = stepZ == 0 ? float.PositiveInfinity : (nextZ - origin.z) / direction.z;
            Vector3 enteredNormal = Vector3.zero;
            float distance = 0f;

            for (int iteration = 0; iteration < 512 && distance <= maxDistance; iteration++)
            {
                BlockState state = GetBlock(cell);
                if (!state.IsAir && BlockRegistry.Get(state.BlockId).renderLayer != BlockRenderLayer.None)
                {
                    Int3 normalCell = new Int3(Mathf.RoundToInt(enteredNormal.x),
                        Mathf.RoundToInt(enteredNormal.y), Mathf.RoundToInt(enteredNormal.z));
                    Int3 adjacent = cell + normalCell;
                    hit.cell = cell;
                    hit.adjacentCell = adjacent;
                    hit.address = AddressForCell(cell);
                    hit.adjacentAddress = AddressForCell(adjacent);
                    hit.block = state;
                    hit.normal = enteredNormal;
                    hit.distance = distance;
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    cell.x += stepX;
                    distance = tMaxX;
                    tMaxX += tDeltaX;
                    enteredNormal = new Vector3(-stepX, 0f, 0f);
                }
                else if (tMaxY <= tMaxZ)
                {
                    cell.y += stepY;
                    distance = tMaxY;
                    tMaxY += tDeltaY;
                    enteredNormal = new Vector3(0f, -stepY, 0f);
                }
                else
                {
                    cell.z += stepZ;
                    distance = tMaxZ;
                    tMaxZ += tDeltaZ;
                    enteredNormal = new Vector3(0f, 0f, -stepZ);
                }
            }
            return false;
        }

        public void MarkCellDirty(Int3 cell)
        {
            Int3 key = CellToChunk(cell);
            QueueChunk(key, -1000);
            int lx = IntegerMath.PositiveMod(cell.x, 16);
            int ly = IntegerMath.PositiveMod(cell.y, 16);
            int lz = IntegerMath.PositiveMod(cell.z, 16);
            if (lx == 0) QueueChunk(new Int3(key.x - 1, key.y, key.z), -900);
            if (lx == 15) QueueChunk(new Int3(key.x + 1, key.y, key.z), -900);
            if (ly == 0) QueueChunk(new Int3(key.x, key.y - 1, key.z), -900);
            if (ly == 15) QueueChunk(new Int3(key.x, key.y + 1, key.z), -900);
            if (lz == 0) QueueChunk(new Int3(key.x, key.y, key.z - 1), -900);
            if (lz == 15) QueueChunk(new Int3(key.x, key.y, key.z + 1), -900);
        }

        private void RefreshRequiredChunks(Int3 observerChunk)
        {
            Vector3 observerLocal = observer.position - world.Center;
            List<KeyValuePair<Int3, float>> candidates = new List<KeyValuePair<Int3, float>>();
            float radiusWorld = chunkRadius * 16f + 14f;
            for (int dz = -chunkRadius; dz <= chunkRadius; dz++)
            for (int dy = -verticalChunkRadius; dy <= verticalChunkRadius; dy++)
            for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            {
                Int3 key = new Int3(observerChunk.x + dx, observerChunk.y + dy,
                    observerChunk.z + dz);
                Vector3 center = new Vector3((key.x + 0.5f) * 16f,
                    (key.y + 0.5f) * 16f, (key.z + 0.5f) * 16f);
                if ((center - observerLocal).sqrMagnitude > radiusWorld * radiusWorld) continue;
                if (!ChunkIntersectsTerrainShell(key)) continue;
                candidates.Add(new KeyValuePair<Int3, float>(key,
                    (center - observerLocal).sqrMagnitude));
            }
            candidates.Sort(delegate(KeyValuePair<Int3, float> a, KeyValuePair<Int3, float> b)
            { return a.Value.CompareTo(b.Value); });

            float now = Time.unscaledTime;
            for (int i = 0; i < candidates.Count; i++)
            {
                Int3 key = candidates[i].Key;
                ChunkRecord record;
                if (!chunks.TryGetValue(key, out record))
                {
                    record = new ChunkRecord();
                    chunks.Add(key, record);
                }
                record.lastRequired = now;
                if (record.gameObject == null || record.dirty) QueueChunk(key, i);
            }
        }

        private bool ChunkIntersectsTerrainShell(Int3 key)
        {
            Vector3 min = new Vector3(key.x * 16f, key.y * 16f, key.z * 16f);
            Vector3 max = min + Vector3.one * 16f;
            float minDistance = DistanceFromOriginToAabb(min, max);
            float maxDistance = 0f;
            for (int z = 0; z <= 1; z++)
            for (int y = 0; y <= 1; y++)
            for (int x = 0; x <= 1; x++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                maxDistance = Mathf.Max(maxDistance, corner.magnitude);
            }
            float shellMin = world.Settings.groundRadius + world.Settings.minimumRadialBlock - 2f;
            float shellMax = world.Settings.groundRadius + world.Settings.maximumRadialBlock + 2f;
            return maxDistance >= shellMin && minDistance <= shellMax;
        }

        private static float DistanceFromOriginToAabb(Vector3 min, Vector3 max)
        {
            float x = 0f < min.x ? min.x : 0f > max.x ? max.x : 0f;
            float y = 0f < min.y ? min.y : 0f > max.y ? max.y : 0f;
            float z = 0f < min.z ? min.z : 0f > max.z ? max.z : 0f;
            return new Vector3(x, y, z).magnitude;
        }

        private void QueueChunk(Int3 key, int priority)
        {
            ChunkRecord record;
            if (!chunks.TryGetValue(key, out record))
            {
                record = new ChunkRecord();
                chunks.Add(key, record);
            }
            record.dirty = true;
            if (record.queued || (activeBuilder != null && activeBuilder.ChunkKey == key)) return;
            record.queued = true;
            queuedKeys.Add(key);
            buildQueue.Enqueue(key);
        }

        private void ProcessBuildBudget()
        {
            int remaining = cellBuildBudgetPerFrame;
            while (remaining > 0)
            {
                if (activeBuilder == null)
                {
                    while (buildQueue.Count > 0)
                    {
                        Int3 key = buildQueue.Dequeue();
                        queuedKeys.Remove(key);
                        ChunkRecord record;
                        if (!chunks.TryGetValue(key, out record)) continue;
                        record.queued = false;
                        activeBuilder = new StableGridChunkBuilder(this, key);
                        break;
                    }
                    if (activeBuilder == null) return;
                }

                int used = activeBuilder.Process(remaining);
                remaining -= used;
                if (!activeBuilder.Complete) return;
                ApplyCompletedChunk(activeBuilder);
                activeBuilder = null;
                completedThisFrame++;
            }
        }

        private void ApplyCompletedChunk(StableGridChunkBuilder builder)
        {
            ChunkRecord record;
            if (!chunks.TryGetValue(builder.ChunkKey, out record)) return;
            EnsureRoot();
            if (record.gameObject == null)
            {
                record.gameObject = new GameObject("Stable Chunk " + builder.ChunkKey);
                record.gameObject.transform.SetParent(chunkRoot, false);
                record.gameObject.transform.localPosition = new Vector3(builder.ChunkKey.x * 16f,
                    builder.ChunkKey.y * 16f, builder.ChunkKey.z * 16f);
                record.gameObject.AddComponent<MeshFilter>();
                record.gameObject.AddComponent<MeshRenderer>();
            }

            Mesh mesh = new Mesh();
            mesh.name = "Stable Voxel Chunk " + builder.ChunkKey;
            mesh.indexFormat = builder.data.vertices.Count > 65000
                ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(builder.data.vertices);
            mesh.SetNormals(builder.data.normals);
            mesh.SetUVs(0, builder.data.uv);
            mesh.SetColors(builder.data.colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(builder.data.opaqueTriangles, 0, true);
            mesh.SetTriangles(builder.data.waterTriangles, 1, true);
            mesh.RecalculateBounds();

            MeshFilter filter = record.gameObject.GetComponent<MeshFilter>();
            MeshRenderer renderer = record.gameObject.GetComponent<MeshRenderer>();
            Mesh old = filter.sharedMesh;
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new Material[] { opaqueMaterial, waterMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            record.mesh = mesh;
            record.dirty = false;
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old);
                else DestroyImmediate(old);
            }
        }

        private void UnloadExpired()
        {
            float now = Time.unscaledTime;
            List<Int3> remove = null;
            foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
            {
                if (pair.Value.queued) continue;
                if (activeBuilder != null && activeBuilder.ChunkKey == pair.Key) continue;
                if (now - pair.Value.lastRequired < unloadDelaySeconds) continue;
                if (remove == null) remove = new List<Int3>();
                remove.Add(pair.Key);
            }
            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) RemoveChunk(remove[i]);
        }

        private void TrimBudget()
        {
            while (chunks.Count > maximumLoadedChunks)
            {
                bool found = false;
                Int3 oldestKey = default(Int3);
                float oldest = float.MaxValue;
                foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
                {
                    if (pair.Value.queued) continue;
                    if (activeBuilder != null && activeBuilder.ChunkKey == pair.Key) continue;
                    if (pair.Value.lastRequired >= oldest) continue;
                    oldest = pair.Value.lastRequired;
                    oldestKey = pair.Key;
                    found = true;
                }
                if (!found) break;
                RemoveChunk(oldestKey);
            }
        }

        private void RemoveChunk(Int3 key)
        {
            ChunkRecord record;
            if (!chunks.TryGetValue(key, out record)) return;
            if (record.gameObject != null)
            {
                if (Application.isPlaying) Destroy(record.gameObject);
                else DestroyImmediate(record.gameObject);
            }
            chunks.Remove(key);
            queuedKeys.Remove(key);
        }

        private int CountReadyChunks()
        {
            int count = 0;
            foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
                if (pair.Value.gameObject != null && !pair.Value.dirty) count++;
            return count;
        }

        private static Int3 CellToChunk(Int3 cell)
        {
            return new Int3(IntegerMath.FloorDiv(cell.x, 16),
                IntegerMath.FloorDiv(cell.y, 16), IntegerMath.FloorDiv(cell.z, 16));
        }

        private void EnsureRoot()
        {
            if (chunkRoot != null) return;
            Transform found = transform.Find("Stable Cartesian Chunks");
            if (found != null) chunkRoot = found;
            else
            {
                GameObject root = new GameObject("Stable Cartesian Chunks");
                root.transform.SetParent(transform, false);
                chunkRoot = root.transform;
            }
        }
    }
}
