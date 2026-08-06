using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.Planets
{
    [ExecuteAlways]
    public sealed class PlanetPrototypeGenerator : MonoBehaviour
    {
        [SerializeField] private PlanetGenerationSettings settings;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material oceanMaterial;
        [SerializeField] private Material atmosphereMaterial;
        [SerializeField] private Transform observer;
        [SerializeField] private bool generateOnEnable = true;

        private readonly Dictionary<PatchKey, Patch> active = new Dictionary<PatchKey, Patch>();
        private readonly Queue<PatchKey> buildQueue = new Queue<PatchKey>();
        private readonly HashSet<PatchKey> queued = new HashSet<PatchKey>();
        private readonly HashSet<PatchKey> wanted = new HashSet<PatchKey>();
        private PlanetNoise noise;
        private Transform patchRoot;
        private Camera observerCamera;
        private int lastSeed;
        private int lastResolution;

        public PlanetGenerationSettings Settings => settings;
        public int ActivePatchCount => active.Count;
        public int QueuedPatchCount => buildQueue.Count;

        public void Configure(PlanetGenerationSettings generationSettings, Material terrain, Material ocean)
        {
            settings = generationSettings;
            terrainMaterial = terrain;
            oceanMaterial = ocean;
            noise = settings != null ? new PlanetNoise(settings) : null;
        }

        public void ConfigureV2(PlanetGenerationSettings generationSettings, Material terrain, Material ocean, Material atmosphere, Transform viewTarget)
        {
            settings = generationSettings;
            terrainMaterial = terrain;
            oceanMaterial = ocean;
            atmosphereMaterial = atmosphere;
            observer = viewTarget;
            noise = settings != null ? new PlanetNoise(settings) : null;
        }

        private void OnEnable()
        {
            if (settings != null) noise = new PlanetNoise(settings);
            EnsureRoots();
            if (generateOnEnable && Application.isPlaying) Regenerate();
        }

        private void OnDisable() => ClearPatches();

        private void Update()
        {
            if (settings == null || noise == null) return;
            EnsureRoots();
            if (lastSeed != settings.seed || lastResolution != settings.patchResolution) Regenerate();
            ResolveObserver();
            SelectWantedPatches();
            ReconcilePatches();
            ProcessBuildQueue();
        }

        [ContextMenu("Regenerate Planet V2")]
        public void Regenerate()
        {
            if (settings == null) return;
            noise = new PlanetNoise(settings);
            lastSeed = settings.seed;
            lastResolution = settings.patchResolution;
            ClearPatches();
            EnsureRoots();
            EnsureOceanAndAtmosphere();
            SelectWantedPatches();
            ReconcilePatches();
            // Never drain the complete quadtree synchronously in Edit Mode.
            // Update() processes a small time-sliced preview budget instead.
        }

        public float SurfaceRadius(Vector3 worldDirection) => noise == null ? settings.radius : noise.Radius(worldDirection.normalized);

        public bool TryFindSurface(Vector3 direction, out Vector3 worldPoint, out Vector3 worldNormal)
        {
            direction.Normalize();
            float r = SurfaceRadius(direction);
            worldPoint = transform.position + direction * r;
            float epsilon = Mathf.Max(1f, settings.maxTerrainHeight * .002f);
            Vector3 tangent = Vector3.Cross(direction, Mathf.Abs(direction.y) > .9f ? Vector3.right : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
            Vector3 p0 = direction * r;
            Vector3 p1d = (direction + tangent * (epsilon / settings.radius)).normalized;
            Vector3 p2d = (direction + bitangent * (epsilon / settings.radius)).normalized;
            Vector3 p1 = p1d * SurfaceRadius(p1d);
            Vector3 p2 = p2d * SurfaceRadius(p2d);
            worldNormal = Vector3.Cross(p2 - p0, p1 - p0).normalized;
            if (Vector3.Dot(worldNormal, direction) < 0f) worldNormal = -worldNormal;
            return true;
        }

        public void SetObserver(Transform value) => observer = value;

        private void EnsureRoots()
        {
            if (patchRoot == null)
            {
                Transform existing = transform.Find("Planet V2 Patches");
                if (existing != null) patchRoot = existing;
                else
                {
                    var go = new GameObject("Planet V2 Patches");
                    patchRoot = go.transform;
                    patchRoot.SetParent(transform, false);
                }
            }
        }

        private void ResolveObserver()
        {
            if (observer == null && Camera.main != null) observer = Camera.main.transform;
            observerCamera = observer != null ? observer.GetComponentInChildren<Camera>() : null;
            if (observerCamera == null) observerCamera = Camera.main;
        }

        private void SelectWantedPatches()
        {
            wanted.Clear();
            for (int face = 0; face < 6; face++) SelectNode(new PatchKey(face, 0, 0, 0));
        }

        private void SelectNode(PatchKey key)
        {
            Vector3 centerDir = CubeToSphere(FacePoint(key.face, key.CenterU, key.CenterV));
            Vector3 center = transform.position + centerDir * SurfaceRadius(centerDir);
            float patchSpan = settings.radius * 2f / (1 << key.lod);
            float distance = observer == null ? float.MaxValue : Vector3.Distance(observer.position, center);
            float projected = observerCamera == null ? patchSpan / Mathf.Max(1f, distance) * 1000f : patchSpan / Mathf.Max(1f, distance) * (Screen.height / (2f * Mathf.Tan(observerCamera.fieldOfView * .5f * Mathf.Deg2Rad)));
            bool nearSurface = observer != null && distance < patchSpan * 4f;
            bool split = key.lod < settings.maxLod && (projected > settings.lodScreenError * 25f || nearSurface);
            if (!split)
            {
                wanted.Add(key);
                return;
            }

            int x = key.x * 2, y = key.y * 2, lod = key.lod + 1;
            SelectNode(new PatchKey(key.face, lod, x, y));
            SelectNode(new PatchKey(key.face, lod, x + 1, y));
            SelectNode(new PatchKey(key.face, lod, x, y + 1));
            SelectNode(new PatchKey(key.face, lod, x + 1, y + 1));
        }

        private void ReconcilePatches()
        {
            var remove = ListPool<PatchKey>.Get();
            foreach (var pair in active) if (!wanted.Contains(pair.Key)) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) RemovePatch(remove[i]);
            ListPool<PatchKey>.Release(remove);

            foreach (PatchKey key in wanted)
            {
                if (active.ContainsKey(key) || queued.Contains(key)) continue;
                buildQueue.Enqueue(key);
                queued.Add(key);
            }
        }

        private void ProcessBuildQueue()
        {
            int budget = Mathf.Max(1, settings.maxBuildsPerFrame);
            while (budget-- > 0 && buildQueue.Count > 0)
            {
                PatchKey key = buildQueue.Dequeue();
                queued.Remove(key);
                if (wanted.Contains(key) && !active.ContainsKey(key)) BuildPatch(key);
            }
        }

        private void BuildPatch(PatchKey key)
        {
            int r = settings.patchResolution;
            int coreCount = (r + 1) * (r + 1);
            int edgeCount = (r + 1) * 4;
            var vertices = new Vector3[coreCount + edgeCount];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new List<int>(r * r * 6 + r * 24);
            float du = 1f / (1 << key.lod);
            float u0 = key.x * du;
            float v0 = key.y * du;

            for (int y = 0; y <= r; y++)
            for (int x = 0; x <= r; x++)
            {
                int i = x + y * (r + 1);
                float u = Mathf.Lerp(u0, u0 + du, x / (float)r);
                float v = Mathf.Lerp(v0, v0 + du, y / (float)r);
                Vector3 dir = CubeToSphere(FacePoint(key.face, u, v));
                float h = noise.Height01(dir);
                vertices[i] = dir * (settings.radius + h * settings.maxTerrainHeight);
                normals[i] = SampleNormal(dir);
                float slope = 1f - Mathf.Clamp01(Vector3.Dot(normals[i], dir));
                colors[i] = noise.Biome(dir, h, slope);
            }

            for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                int i = x + y * (r + 1);
                triangles.Add(i); triangles.Add(i + r + 2); triangles.Add(i + r + 1);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + r + 2);
            }

            int skirt = coreCount;
            AddSkirt(vertices, normals, colors, triangles, ref skirt, r, x => x, settings.skirtDepth);
            AddSkirt(vertices, normals, colors, triangles, ref skirt, r, x => x + r * (r + 1), settings.skirtDepth);
            AddSkirt(vertices, normals, colors, triangles, ref skirt, r, y => y * (r + 1), settings.skirtDepth);
            AddSkirt(vertices, normals, colors, triangles, ref skirt, r, y => r + y * (r + 1), settings.skirtDepth);

            var mesh = new Mesh { name = key.ToString(), indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(key.ToString());
            go.transform.SetParent(patchRoot, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = terrainMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            Vector3 patchCenter = transform.TransformPoint(mesh.bounds.center);
            float observerDistance = observer == null ? float.MaxValue : Vector3.Distance(observer.position, patchCenter);
            MeshCollider collider = null;
            if (observerDistance <= settings.colliderDistance || key.lod >= settings.maxLod - 1)
            {
                collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }
            active[key] = new Patch(go, mesh, collider);
        }

        private Vector3 SampleNormal(Vector3 dir)
        {
            float e = Mathf.Max(.35f, settings.radius * .0004f);
            Vector3 t = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized;
            Vector3 b = Vector3.Cross(dir, t).normalized;
            Vector3 d1 = (dir + t * e / settings.radius).normalized;
            Vector3 d2 = (dir + b * e / settings.radius).normalized;
            Vector3 p0 = dir * noise.Radius(dir), p1 = d1 * noise.Radius(d1), p2 = d2 * noise.Radius(d2);
            Vector3 n = Vector3.Cross(p2 - p0, p1 - p0).normalized;
            return Vector3.Dot(n, dir) < 0f ? -n : n;
        }

        private static void AddSkirt(Vector3[] v, Vector3[] n, Color[] c, List<int> t, ref int skirt, int r, Func<int, int> core, float depth)
        {
            int first = skirt;
            for (int i = 0; i <= r; i++)
            {
                int source = core(i);
                v[skirt] = v[source] - v[source].normalized * depth;
                n[skirt] = n[source]; c[skirt] = c[source]; skirt++;
            }
            for (int i = 0; i < r; i++)
            {
                int a = core(i), b = core(i + 1), sa = first + i, sb = first + i + 1;
                t.Add(a); t.Add(sb); t.Add(sa); t.Add(a); t.Add(b); t.Add(sb);
            }
        }

        private void RemovePatch(PatchKey key)
        {
            if (!active.TryGetValue(key, out Patch patch)) return;
            active.Remove(key);
            if (Application.isPlaying) { Destroy(patch.mesh); Destroy(patch.gameObject); }
            else { DestroyImmediate(patch.mesh); DestroyImmediate(patch.gameObject); }
        }

        private void ClearPatches()
        {
            foreach (var pair in active)
            {
                if (pair.Value == null) continue;
                if (Application.isPlaying) { Destroy(pair.Value.mesh); Destroy(pair.Value.gameObject); }
                else { DestroyImmediate(pair.Value.mesh); DestroyImmediate(pair.Value.gameObject); }
            }
            active.Clear(); buildQueue.Clear(); queued.Clear(); wanted.Clear();
            if (patchRoot != null)
            {
                if (Application.isPlaying) Destroy(patchRoot.gameObject); else DestroyImmediate(patchRoot.gameObject);
                patchRoot = null;
            }
        }

        private void EnsureOceanAndAtmosphere()
        {
            EnsureSphere("Planet V2 Ocean", settings.radius + settings.seaLevel * settings.maxTerrainHeight, oceanMaterial, 0);
            EnsureSphere("Planet V2 Atmosphere", settings.radius + settings.maxTerrainHeight + 85f, atmosphereMaterial, 1);
        }

        private void EnsureSphere(string name, float radius, Material material, int layerOffset)
        {
            Transform found = transform.Find(name);
            GameObject go = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name; go.transform.SetParent(transform, false); go.transform.localScale = Vector3.one * radius * 2f;
            Collider col = go.GetComponent<Collider>(); if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            MeshRenderer mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.sharedMaterial = material;
        }

        private static Vector3 FacePoint(int face, float u, float v)
        {
            float a = u * 2f - 1f, b = v * 2f - 1f;
            switch (face)
            {
                case 0: return new Vector3(1f, b, -a);
                case 1: return new Vector3(-1f, b, a);
                case 2: return new Vector3(a, 1f, -b);
                case 3: return new Vector3(a, -1f, b);
                case 4: return new Vector3(a, b, 1f);
                default: return new Vector3(-a, b, -1f);
            }
        }

        private static Vector3 CubeToSphere(Vector3 p)
        {
            float x2 = p.x * p.x, y2 = p.y * p.y, z2 = p.z * p.z;
            return new Vector3(
                p.x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 * .5f - z2 * .5f + y2 * z2 / 3f)),
                p.y * Mathf.Sqrt(Mathf.Max(0f, 1f - z2 * .5f - x2 * .5f + z2 * x2 / 3f)),
                p.z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * .5f - y2 * .5f + x2 * y2 / 3f))).normalized;
        }

        private readonly struct PatchKey : IEquatable<PatchKey>
        {
            public readonly int face, lod, x, y;
            public PatchKey(int face, int lod, int x, int y) { this.face = face; this.lod = lod; this.x = x; this.y = y; }
            public float CenterU => (x + .5f) / (1 << lod);
            public float CenterV => (y + .5f) / (1 << lod);
            public bool Equals(PatchKey other) => face == other.face && lod == other.lod && x == other.x && y == other.y;
            public override bool Equals(object obj) => obj is PatchKey other && Equals(other);
            public override int GetHashCode() { unchecked { int h = face; h = h * 397 ^ lod; h = h * 397 ^ x; return h * 397 ^ y; } }
            public override string ToString() => $"Face {face} LOD {lod} [{x},{y}]";
        }

        private sealed class Patch
        {
            public readonly GameObject gameObject; public readonly Mesh mesh; public readonly MeshCollider collider;
            public Patch(GameObject go, Mesh mesh, MeshCollider collider) { gameObject = go; this.mesh = mesh; this.collider = collider; }
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> pool = new Stack<List<T>>();
            public static List<T> Get() => pool.Count > 0 ? pool.Pop() : new List<T>();
            public static void Release(List<T> list) { list.Clear(); pool.Push(list); }
        }
    }
}

