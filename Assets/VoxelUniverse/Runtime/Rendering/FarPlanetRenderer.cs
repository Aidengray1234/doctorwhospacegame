using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    /// <summary>
    /// Complete low-cost planet representation. A camera-centered mask removes the far
    /// surface underneath loaded near voxels, while the complete planet becomes visible
    /// again as the observer climbs into space.
    /// </summary>
    public sealed class FarPlanetRenderer : MonoBehaviour
    {
        private const string MaskedShaderName = "DoctorWho/Voxel Universe Far Masked";

        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Material material;
        [SerializeField] private Mesh surfaceMesh;
        [SerializeField] private int builtResolution;
        [SerializeField] private float builtRadius;
        [SerializeField] private int builtSeed;

        private GameObject surfaceObject;
        private GameObject fallbackSphere;
        private MeshRenderer surfaceRenderer;
        private MeshRenderer fallbackRenderer;
        private MaterialPropertyBlock propertyBlock;
        private bool ownsMaterial;

        public void Configure(VoxelUniverseWorld voxelWorld, Material farMaterial)
        {
            world = voxelWorld;
            material = farMaterial;
            EnsureMaskedMaterial();
            RebuildNow();
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            EnsureBuilt();
            UpdateLocalMask();
        }

        private void EnsureBuilt()
        {
            if (world == null || world.Settings == null) return;
            EnsureMaskedMaterial();
            if (surfaceMesh == null
                || builtResolution != world.Settings.farFaceResolution
                || Mathf.Abs(builtRadius - world.Settings.groundRadius) > 0.01f
                || builtSeed != world.Settings.seed)
                RebuildNow();
        }

        private void EnsureMaskedMaterial()
        {
            Shader maskedShader = Shader.Find(MaskedShaderName);
            if (maskedShader == null) return;

            if (material == null)
            {
                material = new Material(maskedShader);
                material.name = "Voxel Far Planet Masked Runtime";
                ownsMaterial = true;
            }
            else if (material.shader != maskedShader)
            {
                material.shader = maskedShader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Ambient"))
                material.SetFloat("_Ambient", 0.24f);
        }

        public void RebuildNow()
        {
            if (world == null || world.Settings == null) return;
            EnsureObjects();
            EnsureMaskedMaterial();

            VoxelUniverseSettings settings = world.Settings;
            int resolution = Mathf.Clamp(settings.farFaceResolution, 12, 64);
            float surfaceInset = Mathf.Max(2.25f, settings.farSurfaceInset);

            List<Vector3> vertices = new List<Vector3>(resolution * resolution * 6 * 8);
            List<Vector3> normals = new List<Vector3>(vertices.Capacity);
            List<Color32> colors = new List<Color32>(vertices.Capacity);
            List<int> triangles = new List<int>(resolution * resolution * 6 * 12);

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                CubeSphereFace face = (CubeSphereFace)faceIndex;
                for (int v = 0; v < resolution; v++)
                for (int u = 0; u < resolution; u++)
                {
                    int surface = SampleSurface(face, u, v, resolution);
                    bool ocean = surface < settings.seaLevel;
                    int displayHeight = ocean ? settings.seaLevel : surface;
                    float radius = settings.groundRadius + displayHeight + 1f - surfaceInset;
                    BlockState block = ocean
                        ? new BlockState(BlockRegistry.Water, 0, 0)
                        : SampleSurfaceBlock(face, u, v, resolution, surface);
                    Color32 color = BlockRegistry.Get(block.BlockId).topColor;
                    if (ocean) color = new Color32(38, 88, 166, 255);

                    AddTop(vertices, normals, colors, triangles,
                        face, u, v, resolution, radius, color);
                    AddDropWallIfNeeded(vertices, normals, colors, triangles,
                        face, u, v, resolution, radius, color, 1, 0, surfaceInset);
                    AddDropWallIfNeeded(vertices, normals, colors, triangles,
                        face, u, v, resolution, radius, color, 0, 1, surfaceInset);
                }
            }

            if (surfaceMesh == null)
            {
                surfaceMesh = new Mesh();
                surfaceMesh.name = "Complete Blocky Far Planet";
                surfaceMesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                surfaceMesh.Clear(false);
            }

            surfaceMesh.SetVertices(vertices);
            surfaceMesh.SetNormals(normals);
            surfaceMesh.SetColors(colors);
            surfaceMesh.SetTriangles(triangles, 0, true);
            surfaceMesh.RecalculateBounds();

            MeshFilter filter = surfaceObject.GetComponent<MeshFilter>();
            filter.sharedMesh = surfaceMesh;
            surfaceRenderer.sharedMaterial = material;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = true;

            float fallbackInset = Mathf.Max(settings.farFallbackInset, surfaceInset + 2f);
            float fallbackRadius = settings.groundRadius + settings.seaLevel - fallbackInset;
            fallbackSphere.transform.localPosition = Vector3.zero;
            fallbackSphere.transform.localScale = Vector3.one * Mathf.Max(4f, fallbackRadius * 2f);
            fallbackRenderer.sharedMaterial = material;
            fallbackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fallbackRenderer.receiveShadows = true;

            builtResolution = resolution;
            builtRadius = settings.groundRadius;
            builtSeed = settings.seed;
            UpdateLocalMask();
        }

        private void UpdateLocalMask()
        {
            if (world == null || world.Settings == null) return;
            EnsureObjects();
            if (surfaceRenderer == null || fallbackRenderer == null) return;

            Camera observerCamera = Camera.main;
            if (observerCamera == null) return;

            VoxelUniverseSettings settings = world.Settings;
            float altitude = world.GetAltitude(observerCamera.transform.position);
            float revealStart = Mathf.Max(24f, settings.nearTerrainMaxAltitude * 0.55f);
            float revealEnd = Mathf.Max(revealStart + 1f, settings.farFullVisibilityAltitude);
            float reveal = Mathf.InverseLerp(revealStart, revealEnd, altitude);
            reveal = reveal * reveal * (3f - 2f * reveal);

            float hideRadius = Mathf.Lerp(settings.farLocalHideRadius, 0f, reveal);
            float fadeWidth = Mathf.Lerp(settings.farLocalFadeWidth,
                Mathf.Max(2f, settings.farLocalFadeWidth * 0.35f), reveal);

            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetVector("_ObserverPosition", observerCamera.transform.position);
            propertyBlock.SetFloat("_HideRadius", hideRadius);
            propertyBlock.SetFloat("_FadeWidth", fadeWidth);
            propertyBlock.SetFloat("_MaskEnabled", 1f);
            surfaceRenderer.SetPropertyBlock(propertyBlock);
            fallbackRenderer.SetPropertyBlock(propertyBlock);
        }

        private int SampleSurface(CubeSphereFace face, int coarseU, int coarseV, int resolution)
        {
            VoxelAddress address = CoarseToAddress(face, coarseU, coarseV, resolution, 0);
            return world.GetSurfaceHeight(address.face, address.u, address.v);
        }

        private BlockState SampleSurfaceBlock(
            CubeSphereFace face,
            int coarseU,
            int coarseV,
            int resolution,
            int surface)
        {
            VoxelAddress address = CoarseToAddress(face, coarseU, coarseV, resolution, surface);
            return world.SampleGeneratedBlock(address);
        }

        private VoxelAddress CoarseToAddress(
            CubeSphereFace face,
            int coarseU,
            int coarseV,
            int resolution,
            int radial)
        {
            double normalizedU = ((coarseU + 0.5d) / resolution) * 2d - 1d;
            double normalizedV = ((coarseV + 0.5d) / resolution) * 2d - 1d;
            Double3 direction = CubeSphereMapper.FaceUvToDirection(face, normalizedU, normalizedV);
            CubeSphereFace canonicalFace;
            double u;
            double v;
            CubeSphereMapper.DirectionToFaceUv(direction, out canonicalFace, out u, out v);
            int cellU = Mathf.Clamp((int)System.Math.Floor((u + 1d) * 0.5d
                * world.Settings.faceCellResolution), 0, world.Settings.faceCellResolution - 1);
            int cellV = Mathf.Clamp((int)System.Math.Floor((v + 1d) * 0.5d
                * world.Settings.faceCellResolution), 0, world.Settings.faceCellResolution - 1);
            return new VoxelAddress(world.BodyId, canonicalFace, cellU, cellV, radial);
        }

        private void AddDropWallIfNeeded(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<int> triangles,
            CubeSphereFace face,
            int u,
            int v,
            int resolution,
            float currentRadius,
            Color32 topColor,
            int du,
            int dv,
            float surfaceInset)
        {
            int neighborSurface = SampleSurface(face, u + du, v + dv, resolution);
            int neighborDisplay = Mathf.Max(neighborSurface, world.Settings.seaLevel);
            float neighborRadius = world.Settings.groundRadius + neighborDisplay + 1f - surfaceInset;
            if (neighborRadius >= currentRadius - 0.05f) return;

            Vector3 d0;
            Vector3 d1;
            if (du != 0)
            {
                d0 = CubeSphereMapper.GridPointDirection(face, u + 1, v, resolution).ToVector3();
                d1 = CubeSphereMapper.GridPointDirection(face, u + 1, v + 1, resolution).ToVector3();
            }
            else
            {
                d0 = CubeSphereMapper.GridPointDirection(face, u, v + 1, resolution).ToVector3();
                d1 = CubeSphereMapper.GridPointDirection(face, u + 1, v + 1, resolution).ToVector3();
            }

            Vector3 a = d0 * neighborRadius;
            Vector3 b = d1 * neighborRadius;
            Vector3 c = d1 * currentRadius;
            Vector3 d = d0 * currentRadius;
            Vector3 desiredNormal = ((a + b + c + d) * 0.25f).normalized;
            Vector3 calculated = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(calculated, desiredNormal) > 0.65f)
                desiredNormal = -desiredNormal;

            Color32 wall = new Color32(
                (byte)(topColor.r * 0.72f),
                (byte)(topColor.g * 0.72f),
                (byte)(topColor.b * 0.72f),
                255);
            AddQuad(vertices, normals, colors, triangles,
                a, b, c, d, desiredNormal, wall);
        }

        private static void AddTop(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<int> triangles,
            CubeSphereFace face,
            int u,
            int v,
            int resolution,
            float radius,
            Color32 color)
        {
            Vector3 a = CubeSphereMapper.GridPointDirection(face, u, v, resolution).ToVector3() * radius;
            Vector3 b = CubeSphereMapper.GridPointDirection(face, u, v + 1, resolution).ToVector3() * radius;
            Vector3 c = CubeSphereMapper.GridPointDirection(face, u + 1, v + 1, resolution).ToVector3() * radius;
            Vector3 d = CubeSphereMapper.GridPointDirection(face, u + 1, v, resolution).ToVector3() * radius;
            Vector3 normal = ((a + b + c + d) * 0.25f).normalized;
            AddQuad(vertices, normals, colors, triangles, a, b, c, d, normal, color);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 desiredNormal,
            Color32 color)
        {
            Vector3 calculated = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(calculated, desiredNormal) < 0f)
            {
                Vector3 swap = b;
                b = d;
                d = swap;
                calculated = Vector3.Cross(b - a, c - a);
            }
            Vector3 normal = calculated.sqrMagnitude > 0.0000001f
                ? calculated.normalized
                : desiredNormal.normalized;

            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private void EnsureObjects()
        {
            Transform surfaceTransform = transform.Find("Complete Blocky Far Planet");
            surfaceObject = surfaceTransform != null
                ? surfaceTransform.gameObject
                : new GameObject("Complete Blocky Far Planet");
            surfaceObject.transform.SetParent(transform, false);
            surfaceObject.transform.localPosition = Vector3.zero;
            MeshFilter filter = surfaceObject.GetComponent<MeshFilter>();
            if (filter == null) filter = surfaceObject.AddComponent<MeshFilter>();
            surfaceRenderer = surfaceObject.GetComponent<MeshRenderer>();
            if (surfaceRenderer == null) surfaceRenderer = surfaceObject.AddComponent<MeshRenderer>();

            Transform fallbackTransform = transform.Find("Far Planet Hole Fallback");
            fallbackSphere = fallbackTransform != null
                ? fallbackTransform.gameObject
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallbackSphere.name = "Far Planet Hole Fallback";
            fallbackSphere.transform.SetParent(transform, false);
            Collider collider = fallbackSphere.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            fallbackRenderer = fallbackSphere.GetComponent<MeshRenderer>();
            if (fallbackRenderer == null) fallbackRenderer = fallbackSphere.AddComponent<MeshRenderer>();
        }

        private void OnDestroy()
        {
            if (surfaceMesh != null)
            {
                if (Application.isPlaying) Destroy(surfaceMesh);
                else DestroyImmediate(surfaceMesh);
            }
            if (ownsMaterial && material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
