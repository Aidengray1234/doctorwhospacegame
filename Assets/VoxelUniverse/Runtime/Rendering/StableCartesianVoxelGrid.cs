using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Generation;
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
            public StableGridChunkSnapshot snapshot;
            public float lastRequired;
            public bool pending;
            public bool ready;
            public bool dirty;
            public int requestVersion;
            public int priority;
        }

        private struct ChunkPriority
        {
            public Int3 key;
            public int priority;
            public float distanceSq;
        }

        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material opaqueMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private StableGridEditStore editStore;
        [SerializeField, Range(2, 6)] private int chunkRadius = 4;
        [SerializeField, Range(1, 4)] private int verticalChunkRadius = 3;
        [SerializeField, Range(2, 16)] private int maximumOutstandingWorkerJobs = 8;
        [SerializeField, Range(1, 4)] private int meshUploadsPerFrame = 2;
        [SerializeField, Min(1f)] private float unloadDelaySeconds = 14f;
        [SerializeField, Range(48, 320)] private int maximumTrackedChunks = 176;
        [SerializeField, Range(0.05f, 0.5f)] private float refreshInterval = 0.16f;
        [SerializeField, Range(0f, 4f)] private float predictionSeconds = 1.35f;

        private readonly Dictionary<Int3, ChunkRecord> chunks =
            new Dictionary<Int3, ChunkRecord>();
        private readonly HashSet<Int3> desiredKeys = new HashSet<Int3>();
        private readonly List<ChunkPriority> desiredOrder = new List<ChunkPriority>(256);
        private readonly Queue<StableGridBuiltChunk> completedBuilds =
            new Queue<StableGridBuiltChunk>();
        private readonly Dictionary<Int3, BlockState> fallbackBlockCache =
            new Dictionary<Int3, BlockState>(4096);

        private VoxelJobScheduler scheduler;
        private Int3 lastObserverChunk = new Int3(int.MinValue, int.MinValue, int.MinValue);
        private Int3 supportChunk;
        private bool hasSupportChunk;
        private float nextRefreshTime;
        private Transform chunkRoot;
        private bool initialized;
        private int nextRequestVersion;
        private int completedThisFrame;
        private int uploadedThisFrame;
        private int sampledColumnsLastCompleted;
        private Vector3 previousObserverPosition;
        private Vector3 observerVelocity;
        private float readyCoverageRadius;

        public VoxelUniverseWorld World { get { return world; } }
        public int LoadedChunkCount { get { return ReadyChunkCount; } }
        public int RequestedChunkCount { get { return chunks.Count; } }
        public int QueuedChunkCount { get { return scheduler != null ? scheduler.QueuedCount : 0; } }
        public int ActiveWorkerCount { get { return scheduler != null ? scheduler.ActiveWorkerCount : 0; } }
        public int PendingUploadCount { get { return completedBuilds.Count; } }
        public int CompletedThisFrame { get { return completedThisFrame; } }
        public int UploadedThisFrame { get { return uploadedThisFrame; } }
        public int LastSurfaceColumnsSampled { get { return sampledColumnsLastCompleted; } }
        public float ReadyCoverageRadius { get { return readyCoverageRadius; } }
        public bool HasReadyTerrain { get { return hasSupportChunk && IsChunkReady(supportChunk); } }

        public int ReadyChunkCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
                    if (pair.Value.ready) count++;
                return count;
            }
        }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material opaque, Material water, StableGridEditStore store)
        {
            world = voxelWorld;
            observer = trackingObserver;
            opaqueMaterial = opaque;
            waterMaterial = water != null ? water : opaque;
            editStore = store;
            initialized = false;
            Initialize();
        }

        private void Awake() { Initialize(); }
        private void OnEnable() { Initialize(); }

        private void Initialize()
        {
            if (initialized || world == null || world.Settings == null) return;
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

            DisposeScheduler();
            if (Application.isPlaying)
                scheduler = new VoxelJobScheduler(Mathf.Max(1, world.Settings.workerCount));

            previousObserverPosition = observer != null ? observer.position : Vector3.zero;
            nextRefreshTime = 0f;
        }

        private void Update()
        {
            completedThisFrame = 0;
            uploadedThisFrame = 0;
            if (world == null || world.Settings == null || observer == null) return;
            if (scheduler == null && Application.isPlaying)
                scheduler = new VoxelJobScheduler(Mathf.Max(1, world.Settings.workerCount));
            if (scheduler == null) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector3 measuredVelocity = (observer.position - previousObserverPosition) / dt;
            previousObserverPosition = observer.position;
            observerVelocity = Vector3.Lerp(observerVelocity, measuredVelocity,
                1f - Mathf.Exp(-8f * dt));

            scheduler.PumpMainThread(Mathf.Max(4, world.Settings.mainThreadCallbacksPerFrame));
            ApplyCompletedMeshes(meshUploadsPerFrame);

            Int3 observerChunk = CellToChunk(WorldToCell(observer.position));
            if (observerChunk != lastObserverChunk || Time.unscaledTime >= nextRefreshTime)
            {
                lastObserverChunk = observerChunk;
                RefreshRequiredChunks(observerChunk);
                nextRefreshTime = Time.unscaledTime + refreshInterval;
            }

            ScheduleMore();
            UpdateReadyCoverage();
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
            return CubeSphereMapper.PositionToAddress(world.BodyId,
                new Double3(cell.x + 0.5d, cell.y + 0.5d, cell.z + 0.5d),
                world.Settings.groundRadius, world.Settings.faceCellResolution);
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

            Int3 key = CellToChunk(cell);
            ChunkRecord record;
            BlockState cached;
            if (chunks.TryGetValue(key, out record) && record.snapshot != null
                && record.snapshot.TryGetGlobal(cell, out cached))
                return cached;

            BlockState fallback;
            if (fallbackBlockCache.TryGetValue(cell, out fallback))
                return fallback;

            fallback = world.SampleGeneratedBlock(AddressForCell(cell));
            if (fallbackBlockCache.Count >= 8192)
                fallbackBlockCache.Clear();
            fallbackBlockCache[cell] = fallback;
            return fallback;
        }

        public void SetBlock(Int3 cell, BlockState state)
        {
            if (editStore != null) editStore.Set(cell, state.Packed);
            fallbackBlockCache[cell] = state;
            world.SetBlock(AddressForCell(cell), state);
            MarkCellDirty(cell);
        }

        public bool HasSupportTerrainAt(Vector3 worldPosition)
        {
            Int3 key = ComputeSupportChunk(worldPosition);
            return IsChunkReady(key);
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
            MarkChunkDirty(key, -100000);
            int lx = IntegerMath.PositiveMod(cell.x, 16);
            int ly = IntegerMath.PositiveMod(cell.y, 16);
            int lz = IntegerMath.PositiveMod(cell.z, 16);
            if (lx == 0) MarkChunkDirty(new Int3(key.x - 1, key.y, key.z), -95000);
            if (lx == 15) MarkChunkDirty(new Int3(key.x + 1, key.y, key.z), -95000);
            if (ly == 0) MarkChunkDirty(new Int3(key.x, key.y - 1, key.z), -95000);
            if (ly == 15) MarkChunkDirty(new Int3(key.x, key.y + 1, key.z), -95000);
            if (lz == 0) MarkChunkDirty(new Int3(key.x, key.y, key.z - 1), -95000);
            if (lz == 15) MarkChunkDirty(new Int3(key.x, key.y, key.z + 1), -95000);
            ScheduleMore();
        }

        private void RefreshRequiredChunks(Int3 observerChunk)
        {
            desiredKeys.Clear();
            desiredOrder.Clear();

            Vector3 observerLocal = observer.position - world.Center;
            Vector3 predictedLocal = observerLocal + observerVelocity * predictionSeconds;
            Vector3 forward = observer.forward;
            Vector3 radial = observerLocal.sqrMagnitude > 0.001f
                ? observerLocal.normalized : Vector3.up;
            forward = Vector3.ProjectOnPlane(forward, radial).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = observer.forward.normalized;

            supportChunk = ComputeSupportChunk(observer.position);
            hasSupportChunk = true;

            float radiusWorld = chunkRadius * 16f + 18f;
            for (int dz = -chunkRadius; dz <= chunkRadius; dz++)
            for (int dy = -verticalChunkRadius; dy <= verticalChunkRadius; dy++)
            for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            {
                Int3 key = new Int3(observerChunk.x + dx, observerChunk.y + dy,
                    observerChunk.z + dz);
                if (!ChunkIntersectsTerrainShell(key)) continue;

                Vector3 center = ChunkCenterLocal(key);
                float distanceSq = (center - predictedLocal).sqrMagnitude;
                if (distanceSq > radiusWorld * radiusWorld) continue;

                Vector3 toward = center - observerLocal;
                float forwardBias = toward.sqrMagnitude > 0.01f
                    ? Vector3.Dot(toward.normalized, forward) : 1f;
                int priority = Mathf.RoundToInt(distanceSq * 0.65f);
                if (forwardBias > 0f) priority -= Mathf.RoundToInt(forwardBias * 700f);
                if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1 && Mathf.Abs(dz) <= 1)
                    priority -= 2500;

                AddDesired(key, priority, distanceSq);
            }

            // Always force the actual support chunk and its immediate neighbors to the front,
            // even if the player's spawn point happens to sit across a Cartesian chunk boundary.
            AddDesired(supportChunk, -100000, 0f);
            AddDesired(new Int3(supportChunk.x + 1, supportChunk.y, supportChunk.z), -99000, 1f);
            AddDesired(new Int3(supportChunk.x - 1, supportChunk.y, supportChunk.z), -99000, 1f);
            AddDesired(new Int3(supportChunk.x, supportChunk.y + 1, supportChunk.z), -99000, 1f);
            AddDesired(new Int3(supportChunk.x, supportChunk.y - 1, supportChunk.z), -99000, 1f);
            AddDesired(new Int3(supportChunk.x, supportChunk.y, supportChunk.z + 1), -99000, 1f);
            AddDesired(new Int3(supportChunk.x, supportChunk.y, supportChunk.z - 1), -99000, 1f);

            desiredOrder.Sort(delegate(ChunkPriority a, ChunkPriority b)
            {
                int c = a.priority.CompareTo(b.priority);
                return c != 0 ? c : a.distanceSq.CompareTo(b.distanceSq);
            });

            float now = Time.unscaledTime;
            for (int i = 0; i < desiredOrder.Count; i++)
            {
                Int3 key = desiredOrder[i].key;
                ChunkRecord record = GetOrCreateRecord(key);
                record.lastRequired = now;
                record.priority = desiredOrder[i].priority;
            }
        }

        private void AddDesired(Int3 key, int priority, float distanceSq)
        {
            if (!ChunkIntersectsTerrainShell(key)) return;
            if (desiredKeys.Add(key))
            {
                desiredOrder.Add(new ChunkPriority
                {
                    key = key,
                    priority = priority,
                    distanceSq = distanceSq
                });
                return;
            }

            for (int i = 0; i < desiredOrder.Count; i++)
            {
                if (desiredOrder[i].key != key) continue;
                if (priority < desiredOrder[i].priority)
                {
                    ChunkPriority updated = desiredOrder[i];
                    updated.priority = priority;
                    updated.distanceSq = Mathf.Min(updated.distanceSq, distanceSq);
                    desiredOrder[i] = updated;
                }
                break;
            }
        }

        private void ScheduleMore()
        {
            if (scheduler == null) return;
            int outstanding = scheduler.QueuedCount + scheduler.ActiveWorkerCount;
            int available = Mathf.Max(0, maximumOutstandingWorkerJobs - outstanding);
            if (available <= 0) return;

            for (int i = 0; i < desiredOrder.Count && available > 0; i++)
            {
                Int3 key = desiredOrder[i].key;
                ChunkRecord record = GetOrCreateRecord(key);
                if (record.pending || (record.ready && !record.dirty)) continue;
                ScheduleChunk(key, record, desiredOrder[i].priority);
                available--;
            }
        }

        private void ScheduleChunk(Int3 key, ChunkRecord record, int priority)
        {
            if (scheduler == null || world == null || world.Settings == null) return;

            record.pending = true;
            record.dirty = true;
            record.requestVersion = ++nextRequestVersion;
            int version = record.requestVersion;
            VoxelUniverseSettings settings = world.Settings;
            var bodyId = world.BodyId;
            Int3 origin = new Int3(key.x * 16, key.y * 16, key.z * 16);
            Dictionary<Int3, uint> edits = editStore != null
                ? editStore.CaptureRegion(
                    new Int3(origin.x - 1, origin.y - 1, origin.z - 1),
                    new Int3(origin.x + 16, origin.y + 16, origin.z + 16))
                : null;

            scheduler.Schedule(priority,
                delegate
                {
                    return StableGridWorkerBuilder.Build(key, version, settings, bodyId, edits);
                },
                delegate(object result)
                {
                    ChunkRecord current;
                    if (!chunks.TryGetValue(key, out current)) return;
                    if (current.requestVersion != version) return;
                    current.pending = false;

                    StableGridBuiltChunk built = result as StableGridBuiltChunk;
                    if (built == null)
                    {
                        current.dirty = true;
                        return;
                    }

                    completedBuilds.Enqueue(built);
                    completedThisFrame++;
                });
        }

        private void ApplyCompletedMeshes(int budget)
        {
            int applied = 0;
            while (applied < Mathf.Max(1, budget) && completedBuilds.Count > 0)
            {
                StableGridBuiltChunk built = completedBuilds.Dequeue();
                ChunkRecord record;
                if (!chunks.TryGetValue(built.chunkKey, out record)) continue;
                if (record.requestVersion != built.requestVersion) continue;
                ApplyCompletedChunk(record, built);
                sampledColumnsLastCompleted = built.sampledSurfaceColumns;
                applied++;
                uploadedThisFrame++;
            }
        }

        private void ApplyCompletedChunk(ChunkRecord record, StableGridBuiltChunk built)
        {
            EnsureRoot();

            if (record.gameObject == null)
            {
                record.gameObject = new GameObject("Stable Chunk " + built.chunkKey);
                record.gameObject.transform.SetParent(chunkRoot, false);
                record.gameObject.transform.localPosition = new Vector3(
                    built.chunkKey.x * 16f, built.chunkKey.y * 16f, built.chunkKey.z * 16f);
                record.gameObject.AddComponent<MeshFilter>();
                record.gameObject.AddComponent<MeshRenderer>();
            }

            StableGridChunkMeshData data = built.mesh;
            Mesh mesh = new Mesh();
            mesh.name = "Stable Worker Chunk " + built.chunkKey;
            mesh.indexFormat = data.vertices.Count > 65000
                ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.vertices);
            mesh.SetNormals(data.normals);
            mesh.SetUVs(0, data.uv);
            mesh.SetColors(data.colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(data.opaqueTriangles, 0, true);
            mesh.SetTriangles(data.waterTriangles, 1, true);
            mesh.RecalculateBounds();

            MeshFilter filter = record.gameObject.GetComponent<MeshFilter>();
            MeshRenderer renderer = record.gameObject.GetComponent<MeshRenderer>();
            Mesh old = filter.sharedMesh;
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new Material[] { opaqueMaterial, waterMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            record.mesh = mesh;
            record.snapshot = built.snapshot;
            record.ready = true;
            record.dirty = false;

            if (old != null)
            {
                if (Application.isPlaying) Destroy(old);
                else DestroyImmediate(old);
            }
        }

        private void UpdateReadyCoverage()
        {
            if (observer == null)
            {
                readyCoverageRadius = 0f;
                return;
            }

            Vector3 observerLocal = observer.position - world.Center;
            float nearestMissing = float.PositiveInfinity;
            bool foundReady = false;

            for (int i = 0; i < desiredOrder.Count; i++)
            {
                ChunkPriority desired = desiredOrder[i];
                ChunkRecord record;
                bool ready = chunks.TryGetValue(desired.key, out record) && record.ready && !record.dirty;
                if (ready)
                {
                    foundReady = true;
                    continue;
                }

                float d = Vector3.Distance(observerLocal, ChunkCenterLocal(desired.key));
                if (d < nearestMissing) nearestMissing = d;
            }

            if (!foundReady)
            {
                readyCoverageRadius = 0f;
                return;
            }

            if (float.IsPositiveInfinity(nearestMissing))
                readyCoverageRadius = chunkRadius * 16f;
            else
                readyCoverageRadius = Mathf.Clamp(nearestMissing - 18f, 0f, chunkRadius * 16f);
        }

        private Int3 ComputeSupportChunk(Vector3 worldPosition)
        {
            Vector3 local = worldPosition - world.Center;
            Vector3 radial = local.sqrMagnitude > 0.001f ? local.normalized : Vector3.up;
            VoxelAddress surface = world.FindSurfaceAddress(radial);
            Vector3 surfaceLocal = world.GetBlockCenter(surface) - world.Center;
            Int3 surfaceCell = new Int3(Mathf.FloorToInt(surfaceLocal.x),
                Mathf.FloorToInt(surfaceLocal.y), Mathf.FloorToInt(surfaceLocal.z));
            return CellToChunk(surfaceCell);
        }

        private bool IsChunkReady(Int3 key)
        {
            ChunkRecord record;
            return chunks.TryGetValue(key, out record) && record.ready && !record.dirty;
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

            float shellMin = world.Settings.groundRadius + world.Settings.minimumRadialBlock - 3f;
            float shellMax = world.Settings.groundRadius + world.Settings.maximumRadialBlock + 3f;
            return maxDistance >= shellMin && minDistance <= shellMax;
        }

        private static float DistanceFromOriginToAabb(Vector3 min, Vector3 max)
        {
            float x = 0f < min.x ? min.x : 0f > max.x ? max.x : 0f;
            float y = 0f < min.y ? min.y : 0f > max.y ? max.y : 0f;
            float z = 0f < min.z ? min.z : 0f > max.z ? max.z : 0f;
            return new Vector3(x, y, z).magnitude;
        }

        private void MarkChunkDirty(Int3 key, int priority)
        {
            ChunkRecord record = GetOrCreateRecord(key);
            record.dirty = true;
            record.priority = Mathf.Min(record.priority, priority);
            record.lastRequired = Time.unscaledTime;
            if (desiredKeys.Add(key))
            {
                desiredOrder.Add(new ChunkPriority
                {
                    key = key,
                    priority = priority,
                    distanceSq = 0f
                });
                desiredOrder.Sort(delegate(ChunkPriority a, ChunkPriority b)
                {
                    return a.priority.CompareTo(b.priority);
                });
            }
        }

        private ChunkRecord GetOrCreateRecord(Int3 key)
        {
            ChunkRecord record;
            if (!chunks.TryGetValue(key, out record))
            {
                record = new ChunkRecord
                {
                    dirty = true,
                    priority = int.MaxValue
                };
                chunks.Add(key, record);
            }
            return record;
        }

        private void UnloadExpired()
        {
            float now = Time.unscaledTime;
            List<Int3> remove = null;
            foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
            {
                ChunkRecord record = pair.Value;
                if (desiredKeys.Contains(pair.Key)) continue;
                if (record.pending) continue;
                if (now - record.lastRequired < unloadDelaySeconds) continue;
                if (remove == null) remove = new List<Int3>();
                remove.Add(pair.Key);
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) RemoveChunk(remove[i]);
        }

        private void TrimBudget()
        {
            while (chunks.Count > maximumTrackedChunks)
            {
                bool found = false;
                Int3 oldestKey = default(Int3);
                float oldest = float.MaxValue;
                foreach (KeyValuePair<Int3, ChunkRecord> pair in chunks)
                {
                    if (desiredKeys.Contains(pair.Key) || pair.Value.pending) continue;
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
            desiredKeys.Remove(key);
        }

        private static Int3 CellToChunk(Int3 cell)
        {
            return new Int3(IntegerMath.FloorDiv(cell.x, 16),
                IntegerMath.FloorDiv(cell.y, 16), IntegerMath.FloorDiv(cell.z, 16));
        }

        private static Vector3 ChunkCenterLocal(Int3 key)
        {
            return new Vector3((key.x + 0.5f) * 16f,
                (key.y + 0.5f) * 16f, (key.z + 0.5f) * 16f);
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

        private void DisposeScheduler()
        {
            if (scheduler == null) return;
            scheduler.Dispose();
            scheduler = null;
        }

        private void OnDestroy()
        {
            DisposeScheduler();
        }
    }
}
