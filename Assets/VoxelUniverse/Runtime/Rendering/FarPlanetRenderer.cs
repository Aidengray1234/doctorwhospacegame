using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class FarPlanetRenderer : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Material material;
        private GameObject surfaceObject;
        private GameObject fallbackSphere;
        [SerializeField] private Mesh surfaceMesh;
        private Material fallbackMaterial;
        [SerializeField] private int builtResolution;
        [SerializeField] private float builtRadius;
        [SerializeField] private int builtSeed;

        public void Configure(VoxelUniverseWorld voxelWorld, Material farMaterial)
        {
            world = voxelWorld;
            material = farMaterial;
            RebuildNow();
        }

        private void Awake() { EnsureBuilt(); }
        private void OnEnable() { EnsureBuilt(); }

        private void EnsureBuilt()
        {
            if (world == null || world.Settings == null) return;
            if (surfaceMesh == null || builtResolution != world.Settings.farFaceResolution
                || Mathf.Abs(builtRadius - world.Settings.groundRadius) > 0.01f
                || builtSeed != world.Settings.seed)
                RebuildNow();
        }

        public void RebuildNow()
        {
            if (world == null || world.Settings == null) return;
            EnsureObjects();
            VoxelUniverseSettings settings = world.Settings;
            int resolution = Mathf.Clamp(settings.farFaceResolution, 12, 64);
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
                    float radius = settings.groundRadius + displayHeight + 1f
                                   - settings.farSurfaceInset;
                    BlockState block = ocean
                        ? new BlockState(BlockRegistry.Water, 0, 0)
                        : SampleSurfaceBlock(face, u, v, resolution, surface);
                    Color32 color = BlockRegistry.Get(block.BlockId).topColor;
                    if (ocean) color = new Color32(38, 88, 166, 255);
                    AddTop(vertices, normals, colors, triangles, face, u, v,
                        resolution, radius, color);

                    AddDropWallIfNeeded(vertices, normals, colors, triangles,
                        face, u, v, resolution, radius, color, 1, 0);
                    AddDropWallIfNeeded(vertices, normals, colors, triangles,
                        face, u, v, resolution, radius, color, 0, 1);
                }
            }

            if (surfaceMesh == null)
            {
                surfaceMesh = new Mesh();
                surfaceMesh.name = "Complete Blocky Far Planet";
                surfaceMesh.indexFormat = IndexFormat.UInt32;
            }
            else surfaceMesh.Clear(false);
            surfaceMesh.SetVertices(vertices);
            surfaceMesh.SetNormals(normals);
            surfaceMesh.SetColors(colors);
            surfaceMesh.SetTriangles(triangles, 0, true);
            surfaceMesh.RecalculateBounds();

            MeshFilter filter = surfaceObject.GetComponent<MeshFilter>();
            filter.sharedMesh = surfaceMesh;
            MeshRenderer renderer = surfaceObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            float fallbackRadius = settings.groundRadius + settings.seaLevel - 1.5f;
            fallbackSphere.transform.localPosition = Vector3.zero;
            fallbackSphere.transform.localScale = Vector3.one * Mathf.Max(4f, fallbackRadius * 2f);
            MeshRenderer fallbackRenderer = fallbackSphere.GetComponent<MeshRenderer>();
            if (fallbackMaterial == null && material != null)
            {
                fallbackMaterial = new Material(material);
                fallbackMaterial.name = "Far Planet Hole Fallback Runtime";
                if (fallbackMaterial.HasProperty("_BaseColor"))
                    fallbackMaterial.SetColor("_BaseColor", new Color(0.08f, 0.22f, 0.11f, 1f));
                if (fallbackMaterial.HasProperty("_UseTexture"))
                    fallbackMaterial.SetFloat("_UseTexture", 0f);
            }
            fallbackRenderer.sharedMaterial = fallbackMaterial != null ? fallbackMaterial : material;
            fallbackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fallbackRenderer.receiveShadows = true;

            builtResolution = resolution;
            builtRadius = settings.groundRadius;
            builtSeed = settings.seed;
        }

        private int SampleSurface(CubeSphereFace face, int coarseU, int coarseV, int resolution)
        {
            VoxelAddress address = CoarseToAddress(face, coarseU, coarseV, resolution, 0);
            return world.GetSurfaceHeight(address.face, address.u, address.v);
        }

        private BlockState SampleSurfaceBlock(CubeSphereFace face, int coarseU, int coarseV,
            int resolution, int surface)
        {
            VoxelAddress address = CoarseToAddress(face, coarseU, coarseV, resolution, surface);
            return world.SampleGeneratedBlock(address);
        }

        private VoxelAddress CoarseToAddress(CubeSphereFace face, int coarseU, int coarseV,
            int resolution, int radial)
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

        private void AddDropWallIfNeeded(List<Vector3> vertices, List<Vector3> normals,
            List<Color32> colors, List<int> triangles, CubeSphereFace face, int u, int v,
            int resolution, float currentRadius, Color32 topColor, int du, int dv)
        {
            int neighborSurface = SampleSurface(face, u + du, v + dv, resolution);
            int neighborDisplay = Mathf.Max(neighborSurface, world.Settings.seaLevel);
            float neighborRadius = world.Settings.groundRadius + neighborDisplay + 1f
                                   - world.Settings.farSurfaceInset;
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
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 outward = ((a + b + c + d) * 0.25f).normalized;
            if (Vector3.Dot(normal, outward) > 0.65f) normal = -normal;
            Color32 wall = new Color32((byte)(topColor.r * 0.72f),
                (byte)(topColor.g * 0.72f), (byte)(topColor.b * 0.72f), 255);
            AddQuad(vertices, normals, colors, triangles, a, b, c, d, normal, wall);
        }

        private static void AddTop(List<Vector3> vertices, List<Vector3> normals,
            List<Color32> colors, List<int> triangles, CubeSphereFace face, int u, int v,
            int resolution, float radius, Color32 color)
        {
            Vector3 a = CubeSphereMapper.GridPointDirection(face, u, v, resolution).ToVector3() * radius;
            Vector3 b = CubeSphereMapper.GridPointDirection(face, u, v + 1, resolution).ToVector3() * radius;
            Vector3 c = CubeSphereMapper.GridPointDirection(face, u + 1, v + 1, resolution).ToVector3() * radius;
            Vector3 d = CubeSphereMapper.GridPointDirection(face, u + 1, v, resolution).ToVector3() * radius;
            Vector3 normal = ((a + b + c + d) * 0.25f).normalized;
            AddQuad(vertices, normals, colors, triangles, a, b, c, d, normal, color);
        }

        private static void AddQuad(List<Vector3> vertices, List<Vector3> normals,
            List<Color32> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 c,
            Vector3 d, Vector3 desiredNormal, Color32 color)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), desiredNormal) < 0f)
            {
                Vector3 swap = b; b = d; d = swap;
            }
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            normals.Add(desiredNormal); normals.Add(desiredNormal);
            normals.Add(desiredNormal); normals.Add(desiredNormal);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void EnsureObjects()
        {
            Transform surfaceTransform = transform.Find("Complete Blocky Far Planet");
            surfaceObject = surfaceTransform != null
                ? surfaceTransform.gameObject
                : new GameObject("Complete Blocky Far Planet");
            surfaceObject.transform.SetParent(transform, false);
            surfaceObject.transform.localPosition = Vector3.zero;
            if (surfaceObject.GetComponent<MeshFilter>() == null)
                surfaceObject.AddComponent<MeshFilter>();
            if (surfaceObject.GetComponent<MeshRenderer>() == null)
                surfaceObject.AddComponent<MeshRenderer>();

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
        }

        private void OnDestroy()
        {
            if (surfaceMesh != null)
            {
                if (Application.isPlaying) Destroy(surfaceMesh);
                else DestroyImmediate(surfaceMesh);
            }
            if (fallbackMaterial != null)
            {
                if (Application.isPlaying) Destroy(fallbackMaterial);
                else DestroyImmediate(fallbackMaterial);
            }
        }
    }
}
