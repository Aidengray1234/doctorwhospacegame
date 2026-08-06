using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class StablePlanetCoverRenderer : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material coverMaterial;
        [SerializeField, Range(32, 112)] private int middleResolution = 72;
        [SerializeField, Range(12, 48)] private int farResolution = 28;
        [SerializeField, Range(0.5f, 4f)] private float middleInset = 1.15f;
        [SerializeField, Range(2f, 8f)] private float farInset = 4f;
        [SerializeField, Min(128f)] private float middleMaximumAltitude = 900f;

        private Transform farRoot;
        private Transform middleRoot;
        private int buildStage;
        private int buildFace;
        private bool ready;
        private bool legacyDisabled;

        public bool Ready { get { return ready; } }
        public int BuiltFaces { get { return buildStage * 6 + buildFace; } }

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material material)
        {
            world = voxelWorld;
            observer = trackingObserver;
            coverMaterial = material;
            EnsureRoots();
        }

        private void Awake() { EnsureRoots(); }

        private void Update()
        {
            if (world == null || world.Settings == null) return;
            EnsureRoots();
            if (!ready) BuildNextFace();
            if (observer != null && middleRoot != null)
                middleRoot.gameObject.SetActive(world.GetAltitude(observer.position) <= middleMaximumAltitude);
            if (ready && !legacyDisabled)
            {
                DisableLegacyCoverRenderers();
                legacyDisabled = true;
            }
        }

        private void BuildNextFace()
        {
            if (buildStage >= 2)
            {
                ready = true;
                return;
            }
            bool far = buildStage == 0;
            Transform parent = far ? farRoot : middleRoot;
            int resolution = far ? farResolution : middleResolution;
            float inset = far ? farInset : middleInset;
            BuildFace(parent, (CubeSphereFace)buildFace, resolution, inset, far);
            buildFace++;
            if (buildFace >= 6)
            {
                buildFace = 0;
                buildStage++;
            }
        }

        private void BuildFace(Transform parent, CubeSphereFace face, int resolution,
            float inset, bool far)
        {
            Transform old = parent.Find(face.ToString());
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }
            GameObject go = new GameObject(face.ToString());
            go.transform.SetParent(parent, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = coverMaterial;
            renderer.shadowCastingMode = far ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !far;

            int side = resolution + 1;
            List<Vector3> vertices = new List<Vector3>(side * side);
            List<Vector3> normals = new List<Vector3>(side * side);
            List<Color32> colors = new List<Color32>(side * side);
            List<int> triangles = new List<int>(resolution * resolution * 6);

            for (int v = 0; v <= resolution; v++)
            for (int u = 0; u <= resolution; u++)
            {
                Vector3 direction = CubeSphereMapper.GridPointDirection(face, u, v, resolution).ToVector3();
                VoxelAddress sample = world.GetAddress(world.Center
                    + direction * world.Settings.groundRadius);
                int height = world.GetSurfaceHeight(sample.face, sample.u, sample.v);
                if (far) height = Mathf.RoundToInt(height * 0.5f) * 2;
                float radius = world.Settings.groundRadius + height + 0.5f - inset;
                vertices.Add(direction * radius);
                normals.Add(direction);
                VoxelAddress surface = new VoxelAddress(world.BodyId, sample.face,
                    sample.u, sample.v, height);
                BlockState state = world.SampleGeneratedBlock(surface);
                Color32 color = BlockRegistry.Get(state.BlockId).GetFallbackColor(BlockTextureFace.Outer);
                if (state.BlockId == BlockRegistry.Water)
                    color = new Color32(52, 105, 180, 255);
                colors.Add(color);
            }

            for (int v = 0; v < resolution; v++)
            for (int u = 0; u < resolution; u++)
            {
                int a = v * side + u;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[d] - vertices[a]);
                Vector3 outward = (vertices[a] + vertices[b] + vertices[c] + vertices[d]).normalized;
                if (Vector3.Dot(cross, outward) >= 0f)
                {
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
                else
                {
                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = (far ? "Far" : "Middle") + " Stable Planet " + face;
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
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
        }

        private void DisableLegacyCoverRenderers()
        {
            MonoBehaviour[] behaviours = world.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this) continue;
                string name = behaviour.GetType().Name;
                if (name.IndexOf("Stable", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("FarPlanet", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("MiddlePlanet", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("InfinitePlanet", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    behaviour.enabled = false;
            }
        }
    }
}
