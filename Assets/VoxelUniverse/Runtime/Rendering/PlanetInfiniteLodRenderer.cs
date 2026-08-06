using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    /// <summary>
    /// Complete-planet middle and far clipmaps. The far surface always covers the whole
    /// body, while camera-centred shader holes allow the no-warp cube patch and middle
    /// surface to overlap without exposing unloaded terrain.
    /// </summary>
    public sealed class PlanetInfiniteLodRenderer : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material sourceMaterial;
        [SerializeField] private TangentVoxelClipmap tangentPatch;
        [SerializeField] private GameObject middleObject;
        [SerializeField] private GameObject farObject;
        [SerializeField] private Mesh middleMesh;
        [SerializeField] private Mesh farMesh;
        [SerializeField] private int builtMiddleResolution;
        [SerializeField] private int builtFarResolution;
        [SerializeField] private float builtRadius;
        [SerializeField] private int builtSeed;

        private MaterialPropertyBlock middleProperties;
        private MaterialPropertyBlock farProperties;

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver,
            Material lodMaterial)
        {
            world = voxelWorld;
            observer = trackingObserver;
            sourceMaterial = lodMaterial;
            if (tangentPatch == null) tangentPatch = GetComponent<TangentVoxelClipmap>();
            EnsureBuilt(true);
        }

        private void Awake()
        {
            if (world == null) world = GetComponent<VoxelUniverseWorld>();
            if (tangentPatch == null) tangentPatch = GetComponent<TangentVoxelClipmap>();
        }

        private void Start()
        {
            EnsureBuilt(false);
        }

        private void Update()
        {
            if (world == null || observer == null || world.Settings == null) return;
            EnsureBuilt(false);
            UpdateClipProperties();
        }

        private void EnsureBuilt(bool force)
        {
            if (world == null || world.Settings == null || sourceMaterial == null) return;
            EnsureObjects();
            if (middleMesh == null && middleObject != null)
                middleMesh = middleObject.GetComponent<MeshFilter>().sharedMesh;
            if (farMesh == null && farObject != null)
                farMesh = farObject.GetComponent<MeshFilter>().sharedMesh;

            VoxelUniverseSettings settings = world.Settings;
            bool changed = force || middleMesh == null || farMesh == null
                || builtMiddleResolution != settings.middleFaceResolution
                || builtFarResolution != settings.farFaceResolution
                || Mathf.Abs(builtRadius - settings.groundRadius) > 0.01f
                || builtSeed != settings.seed;
            if (!changed)
            {
                ApplyMaterialAndRendererSettings();
                return;
            }

            ReplaceMesh(ref middleMesh, BuildSurfaceMesh(
                settings.middleFaceResolution, settings.middleSurfaceInset,
                "Middle Planet Surface"));
            ReplaceMesh(ref farMesh, BuildSurfaceMesh(
                settings.farFaceResolution, settings.farSurfaceInset,
                "Complete Far Planet Surface"));
            middleObject.GetComponent<MeshFilter>().sharedMesh = middleMesh;
            farObject.GetComponent<MeshFilter>().sharedMesh = farMesh;
            ApplyMaterialAndRendererSettings();

            builtMiddleResolution = settings.middleFaceResolution;
            builtFarResolution = settings.farFaceResolution;
            builtRadius = settings.groundRadius;
            builtSeed = settings.seed;
            UpdateClipProperties();
        }

        private void EnsureObjects()
        {
            middleObject = FindOrCreate(middleObject, "Middle Planet Clipmap");
            farObject = FindOrCreate(farObject, "Complete Far Planet Clipmap");
            if (middleProperties == null) middleProperties = new MaterialPropertyBlock();
            if (farProperties == null) farProperties = new MaterialPropertyBlock();
        }

        private void ApplyMaterialAndRendererSettings()
        {
            MeshRenderer middleRenderer = middleObject.GetComponent<MeshRenderer>();
            MeshRenderer farRenderer = farObject.GetComponent<MeshRenderer>();
            middleRenderer.sharedMaterial = sourceMaterial;
            farRenderer.sharedMaterial = sourceMaterial;
            middleRenderer.shadowCastingMode = ShadowCastingMode.On;
            middleRenderer.receiveShadows = true;
            farRenderer.shadowCastingMode = ShadowCastingMode.Off;
            farRenderer.receiveShadows = false;
        }

        private GameObject FindOrCreate(GameObject existing, string name)
        {
            GameObject value = existing;
            if (value == null)
            {
                Transform found = transform.Find(name);
                value = found != null ? found.gameObject : new GameObject(name);
            }
            value.name = name;
            value.transform.SetParent(transform, false);
            value.transform.localPosition = Vector3.zero;
            value.transform.localRotation = Quaternion.identity;
            value.transform.localScale = Vector3.one;
            if (value.GetComponent<MeshFilter>() == null) value.AddComponent<MeshFilter>();
            if (value.GetComponent<MeshRenderer>() == null) value.AddComponent<MeshRenderer>();
            return value;
        }

        private Mesh BuildSurfaceMesh(int requestedResolution, float inset, string meshName)
        {
            int resolution = Mathf.Clamp(requestedResolution, 10, 64);
            int faceVertexCount = (resolution + 1) * (resolution + 1);
            List<Vector3> vertices = new List<Vector3>(faceVertexCount * 6);
            List<Vector3> normals = new List<Vector3>(faceVertexCount * 6);
            List<Color32> colors = new List<Color32>(faceVertexCount * 6);
            List<int> triangles = new List<int>(resolution * resolution * 6 * 6);

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                CubeSphereFace face = (CubeSphereFace)faceIndex;
                int faceStart = vertices.Count;
                for (int v = 0; v <= resolution; v++)
                for (int u = 0; u <= resolution; u++)
                {
                    Double3 directionD = CubeSphereMapper.GridPointDirection(face, u, v, resolution);
                    Vector3 direction = directionD.ToVector3().normalized;
                    VoxelAddress address = DirectionToAddress(directionD, 0);
                    int surface = world.GetSurfaceHeight(address.face, address.u, address.v);
                    bool ocean = surface < world.Settings.seaLevel;
                    int displayHeight = ocean ? world.Settings.seaLevel : surface;
                    float radius = world.Settings.groundRadius + displayHeight + 1f - inset;
                    vertices.Add(direction * radius);
                    normals.Add(direction);
                    BlockState block = ocean
                        ? new BlockState(BlockRegistry.Water, 0, 0)
                        : world.SampleGeneratedBlock(new VoxelAddress(
                            world.BodyId, address.face, address.u, address.v, surface));
                    Color32 color = ocean
                        ? new Color32(37, 91, 171, 255)
                        : BlockRegistry.Get(block.BlockId).topColor;
                    colors.Add(color);
                }

                int row = resolution + 1;
                for (int v = 0; v < resolution; v++)
                for (int u = 0; u < resolution; u++)
                {
                    int a = faceStart + u + row * v;
                    int b = faceStart + u + row * (v + 1);
                    int c = faceStart + (u + 1) + row * (v + 1);
                    int d = faceStart + (u + 1) + row * v;
                    Vector3 desired = (vertices[a] + vertices[b]
                        + vertices[c] + vertices[d]).normalized;
                    if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a],
                        vertices[c] - vertices[a]), desired) >= 0f)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(d); triangles.Add(c);
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private VoxelAddress DirectionToAddress(Double3 direction, int radial)
        {
            CubeSphereFace face;
            double u;
            double v;
            CubeSphereMapper.DirectionToFaceUv(direction, out face, out u, out v);
            int resolution = world.Settings.faceCellResolution;
            int cellU = Mathf.Clamp((int)System.Math.Floor((u + 1d) * 0.5d * resolution),
                0, resolution - 1);
            int cellV = Mathf.Clamp((int)System.Math.Floor((v + 1d) * 0.5d * resolution),
                0, resolution - 1);
            return new VoxelAddress(world.BodyId, face, cellU, cellV, radial);
        }

        private void UpdateClipProperties()
        {
            if (middleObject == null || farObject == null || observer == null) return;
            Vector3 direction = (observer.position - world.Center).normalized;
            float altitude = world.GetAltitude(observer.position);
            bool patchVisible = tangentPatch != null && tangentPatch.Ready;
            bool spaceView = altitude > world.Settings.tangentPatchMaxAltitude + 20f;

            float baseRadius = Mathf.Max(8f, world.Settings.groundRadius);
            float innerAngle = Mathf.Clamp(world.Settings.middleInnerRadiusBlocks / baseRadius,
                0.01f, 1.15f);
            float outerAngle = Mathf.Clamp(world.Settings.middleOuterRadiusBlocks / baseRadius,
                innerAngle + 0.02f, 1.35f);
            float farHoleAngle = Mathf.Clamp(world.Settings.farHoleRadiusBlocks / baseRadius,
                innerAngle + 0.02f, outerAngle - 0.02f);

            MeshRenderer middleRenderer = middleObject.GetComponent<MeshRenderer>();
            MeshRenderer farRenderer = farObject.GetComponent<MeshRenderer>();
            middleRenderer.GetPropertyBlock(middleProperties);
            farRenderer.GetPropertyBlock(farProperties);

            middleProperties.SetFloat("_ClipMode", 1f);
            middleProperties.SetVector("_FocusDirectionOS", direction);
            middleProperties.SetFloat("_InnerCos", patchVisible ? Mathf.Cos(innerAngle) : 1.1f);
            middleProperties.SetFloat("_OuterCos", Mathf.Cos(outerAngle));
            farProperties.SetFloat("_ClipMode", 2f);
            farProperties.SetVector("_FocusDirectionOS", direction);
            farProperties.SetFloat("_InnerCos", spaceView ? 1.1f : Mathf.Cos(farHoleAngle));
            farProperties.SetFloat("_OuterCos", -1f);
            middleRenderer.SetPropertyBlock(middleProperties);
            farRenderer.SetPropertyBlock(farProperties);

            middleObject.SetActive(!spaceView);
            farObject.SetActive(true);
        }

        private static void ReplaceMesh(ref Mesh target, Mesh replacement)
        {
            if (target != null && target != replacement)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(target);
                else UnityEngine.Object.DestroyImmediate(target);
            }
            target = replacement;
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying) return;
            if (middleMesh != null) Destroy(middleMesh);
            if (farMesh != null) Destroy(farMesh);
        }
    }
}
