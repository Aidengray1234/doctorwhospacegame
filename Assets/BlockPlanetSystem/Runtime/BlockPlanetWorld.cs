using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoctorWho.BlockPlanets
{
    [DisallowMultipleComponent]
    public sealed class BlockPlanetWorld : MonoBehaviour
    {
        [SerializeField] private BlockPlanetSettings settings;
        [SerializeField] private Material blockMaterial;
        [SerializeField] private Material transparentMaterial;
        [SerializeField] private Material farMaterial;
        [SerializeField] private Transform observer;

        private readonly Dictionary<BlockChunkCoord, BlockPlanetChunk> chunks = new Dictionary<BlockChunkCoord, BlockPlanetChunk>();
        private readonly List<BlockChunkCoord> buildQueue = new List<BlockChunkCoord>();
        private readonly HashSet<BlockChunkCoord> desired = new HashSet<BlockChunkCoord>();
        private readonly HashSet<BlockChunkCoord> keep = new HashSet<BlockChunkCoord>();
        private readonly Dictionary<BlockAddress, BlockId> edits = new Dictionary<BlockAddress, BlockId>();
        private readonly Dictionary<SurfaceKey, int> surfaceCache = new Dictionary<SurfaceKey, int>();
        private Transform chunkRoot;
        private BlockPlanetNoise noise;
        private float nextRefresh;

        private struct SurfaceKey : IEquatable<SurfaceKey>
        {
            public BlockPlanetFace face;
            public int x;
            public int z;
            public SurfaceKey(BlockPlanetFace face, int x, int z) { this.face = face; this.x = x; this.z = z; }
            public bool Equals(SurfaceKey other) => face == other.face && x == other.x && z == other.z;
            public override bool Equals(object obj) => obj is SurfaceKey && Equals((SurfaceKey)obj);
            public override int GetHashCode() { unchecked { return ((int)face * 397 ^ x) * 397 ^ z; } }
        }

        private struct BuildRequest
        {
            public BlockChunkCoord coord;
            public int priority;
            public BuildRequest(BlockChunkCoord coord, int priority) { this.coord = coord; this.priority = priority; }
        }

        public BlockPlanetSettings Settings => settings;
        public int LoadedChunkCount => chunks.Count;
        public int QueuedChunkCount => buildQueue.Count;
        public Vector3 Center => transform.position;
        public float SafetyRadius => settings != null ? settings.SafetyRadius : 1f;

        public void Configure(BlockPlanetSettings value, Material opaque, Material transparent, Material distant, Transform trackingTarget)
        {
            settings = value;
            blockMaterial = opaque;
            transparentMaterial = transparent;
            farMaterial = distant;
            observer = trackingTarget;
            noise = settings != null ? new BlockPlanetNoise(settings.seed) : null;
            surfaceCache.Clear();
            EnsureRoots();
            EnsureFarProxy();
            EnsureSafetyShell();
        }

        public void SetObserver(Transform value) => observer = value;

        private void Awake()
        {
            if (settings != null) noise = new BlockPlanetNoise(settings.seed);
            EnsureRoots();
            EnsureFarProxy();
            EnsureSafetyShell();
        }

        private void Update()
        {
            if (!Application.isPlaying || settings == null || observer == null) return;
            if (noise == null) noise = new BlockPlanetNoise(settings.seed);
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + settings.streamingRefreshSeconds;
                RefreshStreaming();
            }
            BuildQueued(settings.chunkBuildsPerFrame);
        }

        private void EnsureRoots()
        {
            if (chunkRoot != null) return;
            Transform found = transform.Find("Loaded Block Chunks");
            if (found != null) chunkRoot = found;
            else
            {
                GameObject root = new GameObject("Loaded Block Chunks");
                root.transform.SetParent(transform, false);
                chunkRoot = root.transform;
            }
        }

        private void EnsureFarProxy()
        {
            Transform found = transform.Find("Distant Block Planet");
            GameObject proxy = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            proxy.name = "Distant Block Planet";
            proxy.transform.SetParent(transform, false);
            if (settings != null)
            {
                float proxyRadius = settings.radius + settings.seaLevel - 2.5f;
                proxy.transform.localPosition = Vector3.zero;
                proxy.transform.localScale = Vector3.one * proxyRadius * 2f;
            }
            Collider oldCollider = proxy.GetComponent<Collider>();
            if (oldCollider != null)
            {
                if (Application.isPlaying) Destroy(oldCollider); else DestroyImmediate(oldCollider);
            }
            MeshRenderer renderer = proxy.GetComponent<MeshRenderer>();
            if (renderer != null && farMaterial != null) renderer.sharedMaterial = farMaterial;
        }

        private void EnsureSafetyShell()
        {
            if (settings == null) return;
            Transform found = transform.Find("Core Safety Collider");
            GameObject shell = found != null ? found.gameObject : new GameObject("Core Safety Collider");
            shell.transform.SetParent(transform, false);
            shell.transform.localPosition = Vector3.zero;
            SphereCollider collider = shell.GetComponent<SphereCollider>();
            if (collider == null) collider = shell.AddComponent<SphereCollider>();
            collider.radius = settings.SafetyRadius;
            collider.isTrigger = false;
        }

        public void ForceStreamingRefresh()
        {
            nextRefresh = 0f;
            RefreshStreaming();
        }

        public int BuildImmediateNearObserver(int count)
        {
            if (settings == null || observer == null) return 0;
            RefreshStreaming();
            return BuildQueued(Mathf.Max(1, count));
        }

        private int BuildQueued(int count)
        {
            int built = 0;
            while (built < count && buildQueue.Count > 0)
            {
                BlockChunkCoord coord = buildQueue[0];
                buildQueue.RemoveAt(0);
                if (!desired.Contains(coord) || chunks.ContainsKey(coord)) continue;
                BuildChunk(coord);
                built++;
            }
            return built;
        }

        private void RefreshStreaming()
        {
            if (settings == null || observer == null) return;
            BlockAddress observerBlock = BlockPlanetMath.WorldToBlock(observer.position, Center, settings);
            int observerChunkX = BlockPlanetMath.FloorDiv(observerBlock.x, settings.chunkSize);
            int observerChunkY = Mathf.Clamp(BlockPlanetMath.FloorDiv(observerBlock.y, settings.chunkSize), settings.MinimumChunkY, settings.MaximumChunkY);
            int observerChunkZ = BlockPlanetMath.FloorDiv(observerBlock.z, settings.chunkSize);
            int chunksAcross = settings.ChunksAcrossFace;

            desired.Clear();
            keep.Clear();
            var requests = new List<BuildRequest>(256);
            AddStreamingArea(observerBlock.face, observerChunkX, observerChunkY, observerChunkZ,
                settings.horizontalChunkRadius + settings.unloadPadding, false, chunksAcross, requests);
            AddStreamingArea(observerBlock.face, observerChunkX, observerChunkY, observerChunkZ,
                settings.horizontalChunkRadius, true, chunksAcross, requests);

            requests.Sort((a, b) => a.priority.CompareTo(b.priority));
            buildQueue.Clear();
            for (int i = 0; i < requests.Count; i++)
            {
                BlockChunkCoord coord = requests[i].coord;
                if (!chunks.ContainsKey(coord) && !buildQueue.Contains(coord)) buildQueue.Add(coord);
            }

            var remove = new List<BlockChunkCoord>();
            foreach (KeyValuePair<BlockChunkCoord, BlockPlanetChunk> pair in chunks)
                if (!keep.Contains(pair.Key)) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) RemoveChunk(remove[i]);
        }

        private void AddStreamingArea(BlockPlanetFace centerFace, int centerX, int observerY, int centerZ,
            int radius, bool queueBuilds, int chunksAcross, List<BuildRequest> requests)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radius * radius + 1) continue;
                    BlockPlanetFace face = centerFace;
                    int cx = centerX + dx;
                    int cz = centerZ + dz;
                    NormalizeChunk(ref face, ref cx, ref cz, chunksAcross);
                    int sampleX = cx * settings.chunkSize + settings.chunkSize / 2;
                    int sampleZ = cz * settings.chunkSize + settings.chunkSize / 2;
                    int surfaceChunkY = Mathf.Clamp(BlockPlanetMath.FloorDiv(GetSurfaceHeight(face, sampleX, sampleZ), settings.chunkSize), settings.MinimumChunkY, settings.MaximumChunkY);
                    int minY = Mathf.Clamp(Mathf.Min(surfaceChunkY - settings.verticalChunkRadius, observerY - settings.verticalChunkRadius), settings.MinimumChunkY, settings.MaximumChunkY);
                    int maxY = Mathf.Clamp(Mathf.Max(surfaceChunkY + 1, observerY + settings.verticalChunkRadius), settings.MinimumChunkY, settings.MaximumChunkY);
                    for (int cy = minY; cy <= maxY; cy++)
                    {
                        BlockChunkCoord coord = new BlockChunkCoord(face, cx, cy, cz);
                        keep.Add(coord);
                        if (!queueBuilds) continue;
                        desired.Add(coord);
                        if (!chunks.ContainsKey(coord))
                        {
                            int priority = (dx * dx + dz * dz) * 100 + Mathf.Abs(cy - observerY) * 12 + Mathf.Abs(cy - surfaceChunkY) * 3;
                            requests.Add(new BuildRequest(coord, priority));
                        }
                    }
                }
            }
        }

        private void NormalizeChunk(ref BlockPlanetFace face, ref int cx, ref int cz, int chunksAcross)
        {
            if (cx >= 0 && cx < chunksAcross && cz >= 0 && cz < chunksAcross) return;
            int blockX = cx * settings.chunkSize + settings.chunkSize / 2;
            int blockZ = cz * settings.chunkSize + settings.chunkSize / 2;
            BlockPlanetMath.NormalizeCell(ref face, ref blockX, ref blockZ, settings.faceResolution);
            cx = Mathf.Clamp(BlockPlanetMath.FloorDiv(blockX, settings.chunkSize), 0, chunksAcross - 1);
            cz = Mathf.Clamp(BlockPlanetMath.FloorDiv(blockZ, settings.chunkSize), 0, chunksAcross - 1);
        }

        private void BuildChunk(BlockChunkCoord coord)
        {
            EnsureRoots();
            GameObject go = new GameObject(coord.ToString());
            go.transform.SetParent(chunkRoot, false);
            BlockPlanetChunk chunk = go.AddComponent<BlockPlanetChunk>();
            chunk.Initialize(this, coord, blockMaterial, transparentMaterial);
            chunks.Add(coord, chunk);
        }

        private void RemoveChunk(BlockChunkCoord coord)
        {
            BlockPlanetChunk chunk;
            if (!chunks.TryGetValue(coord, out chunk)) return;
            chunks.Remove(coord);
            if (chunk == null) return;
            if (Application.isPlaying) Destroy(chunk.gameObject); else DestroyImmediate(chunk.gameObject);
        }

        public bool IsAreaReady(Vector3 worldPosition)
        {
            if (settings == null) return false;
            BlockAddress address = BlockPlanetMath.WorldToBlock(worldPosition, Center, settings);
            int surface = GetSurfaceHeight(address.face, address.x, address.z);
            BlockChunkCoord surfaceChunk = new BlockChunkCoord(address.face,
                BlockPlanetMath.FloorDiv(address.x, settings.chunkSize),
                BlockPlanetMath.FloorDiv(surface, settings.chunkSize),
                BlockPlanetMath.FloorDiv(address.z, settings.chunkSize));
            BlockChunkCoord below = new BlockChunkCoord(surfaceChunk.face, surfaceChunk.x, surfaceChunk.y - 1, surfaceChunk.z);
            return chunks.ContainsKey(surfaceChunk) && (surfaceChunk.y <= settings.MinimumChunkY || chunks.ContainsKey(below));
        }

        public Vector3 GridPoint(BlockPlanetFace face, float x, float y, float z)
            => Center + BlockPlanetMath.GridPoint(face, x, y, z, settings.faceResolution, settings.radius);

        public int GetSurfaceHeight(BlockPlanetFace face, int x, int z)
        {
            BlockPlanetMath.NormalizeCell(ref face, ref x, ref z, settings.faceResolution);
            SurfaceKey key = new SurfaceKey(face, x, z);
            int cached;
            if (surfaceCache.TryGetValue(key, out cached)) return cached;
            float u = (x + 0.5f) / settings.faceResolution * 2f - 1f;
            float v = (z + 0.5f) / settings.faceResolution * 2f - 1f;
            Vector3 direction = BlockPlanetMath.FaceUvToDirection(face, u, v);
            Vector3 warped = noise.Warp(direction * 3.1f, 0.85f, 0.55f);
            float continent = noise.Fbm(warped * 1.25f, 5);
            continent = Mathf.Sign(continent) * Mathf.Pow(Mathf.Abs(continent), 1.25f);
            float mountains = Mathf.SmoothStep(0.48f, 0.88f, noise.Ridged(warped * 4.2f + Vector3.one * 19.7f, 4));
            float detail = noise.Fbm(warped * 13.5f + Vector3.one * 41.3f, 3);
            float height = settings.baseHeight + continent * settings.continentHeight
                         + mountains * settings.mountainHeight * Mathf.Clamp01(continent * 1.4f + 0.55f)
                         + detail * settings.detailHeight;
            int result = Mathf.Clamp(Mathf.RoundToInt(height), settings.minimumRadialBlock + 5, settings.maximumRadialBlock - 2);
            surfaceCache[key] = result;
            return result;
        }

        public BlockId GetBlock(BlockPlanetFace face, int x, int y, int z)
        {
            if (y < settings.minimumRadialBlock || y >= settings.maximumRadialBlock) return BlockId.Air;
            BlockPlanetMath.NormalizeCell(ref face, ref x, ref z, settings.faceResolution);
            BlockAddress address = new BlockAddress(face, x, y, z);
            BlockId edit;
            if (edits.TryGetValue(address, out edit)) return edit;
            int surface = GetSurfaceHeight(face, x, z);
            if (y > surface) return y <= settings.seaLevel ? BlockId.Water : BlockId.Air;
            if (y <= settings.minimumRadialBlock + 3) return BlockId.Bedrock;

            int depth = surface - y;
            Vector3 direction = GridPoint(face, x + 0.5f, y + 0.5f, z + 0.5f) - Center;
            Vector3 sample = direction.normalized * 19f + Vector3.one * (y * 0.17f);
            if (depth > 5 && y > settings.minimumRadialBlock + 5 && settings.caveAmount > 0f)
            {
                float cave = noise.Fbm(sample, 3);
                if (cave > Mathf.Lerp(0.86f, 0.64f, settings.caveAmount)) return BlockId.Air;
            }

            if (depth > 5)
            {
                float ore = noise.Value3(sample * 2.6f + Vector3.one * 71.3f);
                if (depth > 24 && ore > 0.82f) return BlockId.DiamondOre;
                if (depth > 18 && ore > 0.72f) return BlockId.GoldOre;
                if (depth > 11 && ore > 0.62f) return BlockId.IronOre;
                if (ore > 0.53f) return BlockId.CoalOre;
            }

            float latitude = Mathf.Abs((GridPoint(face, x + 0.5f, 0f, z + 0.5f) - Center).normalized.y);
            if (y == surface)
            {
                if (surface <= settings.seaLevel + 1) return BlockId.Sand;
                if (latitude > 0.72f || surface > settings.baseHeight + settings.continentHeight * 0.72f) return BlockId.Snow;
                return BlockId.Grass;
            }
            if (depth <= 3) return surface <= settings.seaLevel + 1 ? BlockId.Sand : BlockId.Dirt;
            if (depth == 4 && noise.Value3(sample * 1.7f) > 0.65f) return BlockId.Gravel;
            return BlockId.Stone;
        }

        public Vector3 GetSurfacePoint(Vector3 direction)
        {
            BlockPlanetFace face; int x; int z;
            BlockPlanetMath.DirectionToCell(direction, settings.faceResolution, out face, out x, out z);
            int height = GetSurfaceHeight(face, x, z);
            float u = (x + 0.5f) / settings.faceResolution * 2f - 1f;
            float v = (z + 0.5f) / settings.faceResolution * 2f - 1f;
            return Center + BlockPlanetMath.FaceUvToDirection(face, u, v) * (settings.radius + height + 1f);
        }

        public void SetBlock(BlockAddress address, BlockId block)
        {
            BlockPlanetFace face = address.face;
            int x = address.x;
            int z = address.z;
            BlockPlanetMath.NormalizeCell(ref face, ref x, ref z, settings.faceResolution);
            address = new BlockAddress(face, x, address.y, z);
            edits[address] = block;
            RebuildChunksAround(address);
        }

        private void RebuildChunksAround(BlockAddress address)
        {
            BlockChunkCoord center = BlockPlanetMath.BlockToChunk(address, settings.chunkSize);
            var rebuild = new List<BlockPlanetChunk>();
            foreach (KeyValuePair<BlockChunkCoord, BlockPlanetChunk> pair in chunks)
            {
                if (pair.Key.face != center.face) continue;
                if (Mathf.Abs(pair.Key.x - center.x) <= 1 && Mathf.Abs(pair.Key.y - center.y) <= 1 && Mathf.Abs(pair.Key.z - center.z) <= 1)
                    rebuild.Add(pair.Value);
            }
            for (int i = 0; i < rebuild.Count; i++) if (rebuild[i] != null) rebuild[i].Rebuild();
        }

        public bool TryModify(Ray ray, bool place, BlockId placeBlock, out BlockId affectedBlock, float range = 8f)
        {
            affectedBlock = BlockId.Air;
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, range, ~0, QueryTriggerInteraction.Ignore)) return false;
            BlockPlanetChunk chunk = hit.collider.GetComponent<BlockPlanetChunk>();
            if (chunk == null) return false;
            Vector3 samplePoint = hit.point + hit.normal * (place ? 0.08f : -0.08f);
            BlockAddress address = BlockPlanetMath.WorldToBlock(samplePoint, Center, settings);
            BlockId existing = GetBlock(address.face, address.x, address.y, address.z);
            if (!place)
            {
                if (!BlockCatalog.CanBreak(existing)) return false;
                affectedBlock = existing;
                SetBlock(address, BlockId.Air);
                return true;
            }
            if (existing != BlockId.Air && existing != BlockId.Water) return false;
            affectedBlock = placeBlock;
            SetBlock(address, placeBlock);
            return true;
        }
    }
}
