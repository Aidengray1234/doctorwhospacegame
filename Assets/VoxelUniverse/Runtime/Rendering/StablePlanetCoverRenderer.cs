using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Generation;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class StablePlanetCoverRenderer : MonoBehaviour
    {
        private sealed class CoverFaceData
        {
            public CubeSphereFace face;
            public bool far;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Color32[] colors;
            public int[] triangles;
        }

        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material coverMaterial;
        [SerializeField] private StableCartesianVoxelGrid stableGrid;
        [SerializeField, Range(96, 192)] private int middleResolution = 144;
        [SerializeField, Range(20, 64)] private int farResolution = 32;
        [SerializeField, Range(0.5f, 4f)] private float middleInset = 1.35f;
        [SerializeField, Range(2f, 10f)] private float farInset = 5f;
        [SerializeField, Min(512f)] private float orbitalStartAltitude = 1500f;
        [SerializeField, Min(768f)] private float orbitalFullAltitude = 2200f;
        [SerializeField, Range(4f, 32f)] private float coverOverlapBlocks = 14f;

        private Transform farRoot;
        private Transform middleRoot;
        private readonly List<MeshRenderer> middleRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> farRenderers = new List<MeshRenderer>();
        private readonly Queue<CoverFaceData> completed = new Queue<CoverFaceData>();
        private VoxelJobScheduler scheduler;
        private bool jobsScheduled;
        private int middleFacesReady;
        private int farFacesReady;
        private bool legacyDisabled;

        public bool Ready { get { return middleFacesReady >= 6; } }
        public bool OrbitalReady { get { return farFacesReady >= 6; } }
        public int MiddleFacesReady { get { return middleFacesReady; } }
        public int FarFacesReady { get { return farFacesReady; } }
        public bool OrbitalVisible
        {
            get
            {
                return farRoot != null && farRoot.gameObject.activeSelf;
            }
        }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material material)
        {
            Configure(voxelWorld, trackingObserver, material,
                stableGrid != null ? stableGrid : FindObjectOfType<StableCartesianVoxelGrid>());
        }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material material, StableCartesianVoxelGrid grid)
        {
            world = voxelWorld;
            observer = trackingObserver;
            coverMaterial = material;
            stableGrid = grid;
            middleResolution = Mathf.Clamp(Mathf.Max(middleResolution, 160), 96, 192);
            farResolution = Mathf.Clamp(farResolution, 24, 64);
            orbitalStartAltitude = Mathf.Max(1500f, orbitalStartAltitude);
            orbitalFullAltitude = Mathf.Max(orbitalStartAltitude + 600f,
                orbitalFullAltitude);
            coverOverlapBlocks = Mathf.Clamp(coverOverlapBlocks, 8f, 24f);
            EnsureRoots();
            if (Application.isPlaying) EnsureJobs();
        }

        private void Awake()
        {
            EnsureRoots();
            if (farRoot != null) farRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            EnsureRoots();
            if (farRoot != null) farRoot.gameObject.SetActive(false);
            if (Application.isPlaying) EnsureJobs();
        }

        private void Update()
        {
            if (world == null || world.Settings == null) return;
            EnsureRoots();
            if (stableGrid == null) stableGrid = FindObjectOfType<StableCartesianVoxelGrid>();
            EnsureJobs();

            if (scheduler != null)
                scheduler.PumpMainThread(4);

            int uploadBudget = 1;
            while (uploadBudget-- > 0 && completed.Count > 0)
                ApplyFace(completed.Dequeue());

            UpdateVisibilityAndMask();

            if (Ready && !legacyDisabled)
            {
                DisableLegacyCoverRenderers();
                legacyDisabled = true;
            }
        }

        private void EnsureJobs()
        {
            if (jobsScheduled || !Application.isPlaying || world == null
                || world.Settings == null || coverMaterial == null) return;

            jobsScheduled = true;
            scheduler = new VoxelJobScheduler(1);

            CubeSphereFace observerFace = (CubeSphereFace)0;
            if (observer != null)
            {
                Vector3 local = observer.position - world.Center;
                if (local.sqrMagnitude > 0.001f)
                {
                    double faceU;
                    double faceV;
                    CubeSphereMapper.DirectionToFaceUv(
                        Double3.FromVector3(local.normalized),
                        out observerFace, out faceU, out faceV);
                }
            }

            for (int face = 0; face < 6; face++)
            {
                CubeSphereFace captured = (CubeSphereFace)face;
                int priority = captured == observerFace ? -1000 : face * 10;
                ScheduleFace(captured, false, middleResolution, middleInset, priority);
            }

            for (int face = 0; face < 6; face++)
            {
                CubeSphereFace captured = (CubeSphereFace)face;
                ScheduleFace(captured, true, farResolution, farInset, 1000 + face * 10);
            }
        }

        private void ScheduleFace(CubeSphereFace face, bool far, int resolution,
            float inset, int priority)
        {
            var settings = world.Settings;
            var bodyId = world.BodyId;
            scheduler.Schedule(priority,
                delegate
                {
                    VoxelTerrainGenerator generator = new VoxelTerrainGenerator(settings, bodyId);
                    return BuildFaceData(generator, settings.groundRadius, face, resolution,
                        inset, far);
                },
                delegate(object result)
                {
                    CoverFaceData data = result as CoverFaceData;
                    if (data != null) completed.Enqueue(data);
                });
        }

        private static CoverFaceData BuildFaceData(VoxelTerrainGenerator generator,
            float groundRadius, CubeSphereFace face, int resolution, float inset, bool far)
        {
            int side = resolution + 1;
            Vector3[] vertices = new Vector3[side * side];
            Vector3[] normals = new Vector3[side * side];
            Color32[] colors = new Color32[side * side];
            int[] triangles = new int[resolution * resolution * 6];

            for (int v = 0; v <= resolution; v++)
            for (int u = 0; u <= resolution; u++)
            {
                int index = v * side + u;
                Double3 d = CubeSphereMapper.GridPointDirection(face, u, v, resolution);
                Vector3 direction = d.ToVector3();
                VoxelAddress surface = generator.FindSurfaceAddress(d);
                int height = surface.radial;
                if (far) height = (int)Math.Round(height / 4.0) * 4;
                float radius = groundRadius + height + 0.5f - inset;
                vertices[index] = direction * radius;
                normals[index] = direction;

                BlockState state = generator.SampleBaseBlock(surface, surface.radial);
                Color32 color = BlockRegistry.Get(state.BlockId)
                    .GetFallbackColor(BlockTextureFace.Outer);
                if (state.BlockId == BlockRegistry.Water)
                    color = new Color32(52, 105, 180, 255);
                colors[index] = color;
            }

            int t = 0;
            for (int v = 0; v < resolution; v++)
            for (int u = 0; u < resolution; u++)
            {
                int a = v * side + u;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;

                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a],
                    vertices[d] - vertices[a]);
                Vector3 outward = (vertices[a] + vertices[b] + vertices[c]
                    + vertices[d]).normalized;

                if (Vector3.Dot(cross, outward) >= 0f)
                {
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = c;
                }
                else
                {
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                }
            }

            return new CoverFaceData
            {
                face = face,
                far = far,
                vertices = vertices,
                normals = normals,
                colors = colors,
                triangles = triangles
            };
        }

        private void ApplyFace(CoverFaceData data)
        {
            Transform parent = data.far ? farRoot : middleRoot;
            string name = data.face.ToString();
            Transform old = parent.Find(name);
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = coverMaterial;
            renderer.shadowCastingMode = data.far
                ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !data.far;

            Mesh mesh = new Mesh();
            mesh.name = (data.far ? "Orbital" : "Horizon") + " Planet " + data.face;
            mesh.indexFormat = data.vertices.Length > 65000
                ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = data.vertices;
            mesh.normals = data.normals;
            mesh.colors32 = data.colors;
            mesh.triangles = data.triangles;
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;

            if (data.far)
            {
                farRenderers.Add(renderer);
                farFacesReady++;
            }
            else
            {
                middleRenderers.Add(renderer);
                middleFacesReady++;
            }
        }

        private void UpdateVisibilityAndMask()
        {
            if (observer == null || world == null) return;

            Vector3 observerLocal = observer.position - world.Center;
            float altitude = observerLocal.magnitude - world.Settings.groundRadius;
            Vector3 direction = observerLocal.sqrMagnitude > 0.001f
                ? observerLocal.normalized : Vector3.up;

            float coverage = stableGrid != null ? stableGrid.ReadyCoverageRadius : 0f;
            float holeRadius = Mathf.Max(0f, coverage - coverOverlapBlocks);

            // Once the player is high enough that detailed blocks are no longer the active
            // representation, close the local hole and show a continuous high-detail planet.
            if (altitude > Mathf.Max(220f, world.Settings.nearTerrainMaxAltitude + 80f))
                holeRadius = 0f;

            float holeCos = 1.1f;
            if (holeRadius > 0.5f)
            {
                float angle = Mathf.Clamp(holeRadius / Mathf.Max(1f,
                    world.Settings.groundRadius), 0f, 1.2f);
                holeCos = Mathf.Cos(angle);
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetVector("_ObserverDirection", new Vector4(direction.x,
                direction.y, direction.z, 0f));
            block.SetFloat("_HoleCos", holeCos);
            block.SetFloat("_Brightness", 1f);

            for (int i = 0; i < middleRenderers.Count; i++)
            {
                MeshRenderer r = middleRenderers[i];
                if (r != null) r.SetPropertyBlock(block);
            }

            float start = Mathf.Max(orbitalStartAltitude,
                world.Settings.groundRadius * 5f);
            float full = Mathf.Max(orbitalFullAltitude, start + world.Settings.groundRadius * 2f);
            bool farVisible = altitude >= start;
            bool middleVisible = altitude <= full;

            if (middleRoot != null) middleRoot.gameObject.SetActive(middleVisible);
            if (farRoot != null) farRoot.gameObject.SetActive(farVisible && OrbitalReady);

            if (farVisible && OrbitalReady)
            {
                MaterialPropertyBlock farBlock = new MaterialPropertyBlock();
                farBlock.SetVector("_ObserverDirection", new Vector4(direction.x,
                    direction.y, direction.z, 0f));
                farBlock.SetFloat("_HoleCos", 1.1f);
                float blend = Mathf.InverseLerp(start, full, altitude);
                farBlock.SetFloat("_Brightness", Mathf.Lerp(0.92f, 1f, blend));
                for (int i = 0; i < farRenderers.Count; i++)
                {
                    MeshRenderer r = farRenderers[i];
                    if (r != null) r.SetPropertyBlock(farBlock);
                }
            }
        }

        public void ResetGeneratedMeshes()
        {
            DisposeScheduler();
            jobsScheduled = false;
            middleFacesReady = 0;
            farFacesReady = 0;
            middleRenderers.Clear();
            farRenderers.Clear();
            completed.Clear();
            EnsureRoots();
            ClearChildren(middleRoot);
            ClearChildren(farRoot);
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private void EnsureRoots()
        {
            if (farRoot == null)
            {
                Transform found = transform.Find("Stable Far Planet Cover");
                if (found != null) farRoot = found;
                else
                {
                    GameObject go = new GameObject("Stable Far Planet Cover");
                    go.transform.SetParent(transform, false);
                    farRoot = go.transform;
                }
            }

            if (middleRoot == null)
            {
                Transform found = transform.Find("Stable Middle Planet Cover");
                if (found != null) middleRoot = found;
                else
                {
                    GameObject go = new GameObject("Stable Middle Planet Cover");
                    go.transform.SetParent(transform, false);
                    middleRoot = go.transform;
                }
            }

            if (middleRenderers.Count == 0 && middleRoot != null)
                middleRenderers.AddRange(middleRoot.GetComponentsInChildren<MeshRenderer>(true));
            if (farRenderers.Count == 0 && farRoot != null)
                farRenderers.AddRange(farRoot.GetComponentsInChildren<MeshRenderer>(true));
        }

        private void DisableLegacyCoverRenderers()
        {
            MonoBehaviour[] behaviours = world.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this) continue;
                if (stableGrid != null && behaviour.transform.IsChildOf(stableGrid.transform))
                    continue;
                string name = behaviour.GetType().Name;
                if (name.IndexOf("Stable", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("FarPlanet", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("MiddlePlanet", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("InfinitePlanet", StringComparison.OrdinalIgnoreCase) >= 0)
                    behaviour.enabled = false;
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
