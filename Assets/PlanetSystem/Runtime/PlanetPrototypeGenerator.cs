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
        [SerializeField] private bool generateOnEnable = true;

        public PlanetGenerationSettings Settings => settings;

        public void Configure(PlanetGenerationSettings generationSettings, Material terrain, Material ocean)
        {
            settings = generationSettings;
            terrainMaterial = terrain;
            oceanMaterial = ocean;
        }

        private void OnEnable()
        {
            if (generateOnEnable && transform.Find("Generated Terrain") == null) Regenerate();
        }

        [ContextMenu("Regenerate Planet")]
        public void Regenerate()
        {
            if (settings == null) return;

            Transform old = transform.Find("Generated Terrain");
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }

            var root = new GameObject("Generated Terrain").transform;
            root.SetParent(transform, false);

            var combinedVertices = new List<Vector3>();
            var combinedTriangles = new List<int>();
            Vector3[] normals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            for (int i = 0; i < normals.Length; i++) CreateFace(root, normals[i], i, combinedVertices, combinedTriangles);

            var collisionObject = new GameObject("Seamless Terrain Collider");
            collisionObject.transform.SetParent(root, false);
            Mesh collisionMesh = new Mesh { name = "Planet Combined Collider", indexFormat = IndexFormat.UInt32 };
            collisionMesh.SetVertices(combinedVertices);
            collisionMesh.SetTriangles(combinedTriangles, 0);
            collisionMesh.RecalculateBounds();
            collisionObject.AddComponent<MeshCollider>().sharedMesh = collisionMesh;

            CreateOcean(root);
        }

        public float SurfaceRadius(Vector3 worldDirection)
        {
            Vector3 dir = worldDirection.normalized;
            return settings.radius + SampleHeight(dir) * settings.maxTerrainHeight;
        }

        private void CreateFace(Transform root, Vector3 localUp, int index, List<Vector3> combinedVertices, List<int> combinedTriangles)
        {
            int r = settings.faceResolution;
            Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
            Vector3 axisB = Vector3.Cross(localUp, axisA);
            var vertices = new Vector3[r * r];
            var colors = new Color[r * r];
            var triangles = new int[(r - 1) * (r - 1) * 6];
            int ti = 0;

            for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                int vi = x + y * r;
                Vector2 p = new Vector2(x, y) / (r - 1f);
                Vector3 cube = localUp + (p.x - .5f) * 2f * axisA + (p.y - .5f) * 2f * axisB;
                Vector3 dir = cube.normalized;
                float h = SampleHeight(dir);
                vertices[vi] = dir * (settings.radius + h * settings.maxTerrainHeight);
                colors[vi] = SampleBiome(dir, h);

                if (x != r - 1 && y != r - 1)
                {
                    triangles[ti++] = vi;
                    triangles[ti++] = vi + r + 1;
                    triangles[ti++] = vi + r;
                    triangles[ti++] = vi;
                    triangles[ti++] = vi + 1;
                    triangles[ti++] = vi + r + 1;
                }
            }

            var mesh = new Mesh { name = $"Planet Face {index}", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject($"Terrain Face {index}");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

            int baseIndex = combinedVertices.Count;
            combinedVertices.AddRange(vertices);
            for (int i = 0; i < triangles.Length; i++) combinedTriangles.Add(baseIndex + triangles[i]);
        }

        private float SampleHeight(Vector3 p)
        {
            Vector3 offset = new Vector3(settings.seed * .0013f, settings.seed * .0021f, settings.seed * .0037f);
            Vector3 warped = p + new Vector3(
                Fractal(p * 2.1f + offset, 3) - .5f,
                Fractal(p * 2.3f - offset, 3) - .5f,
                Fractal(p * 1.9f + offset * .5f, 3) - .5f) * .18f;

            float continent = Fractal(warped * settings.radius * settings.continentFrequency + offset, settings.octaves);
            continent = Mathf.SmoothStep(0f, 1f, continent);
            float ridged = 1f - Mathf.Abs(Fractal(warped * settings.radius * settings.mountainFrequency - offset, Mathf.Max(3, settings.octaves - 1)) * 2f - 1f);
            ridged = Mathf.Pow(Mathf.Clamp01(ridged), 3.1f);
            float detail = Fractal(warped * settings.radius * settings.detailFrequency + offset * 2f, 3) - .5f;
            float shelf = Mathf.SmoothStep(.34f, .72f, continent);
            return Mathf.Clamp((shelf - .46f) * settings.continentStrength + ridged * shelf * settings.mountainStrength + detail * settings.detailStrength, -1f, 1f);
        }

        private float Fractal(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 1f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float xy = Mathf.PerlinNoise(p.x, p.y);
                float yz = Mathf.PerlinNoise(p.y + 31.7f, p.z + 17.1f);
                float zx = Mathf.PerlinNoise(p.z + 73.9f, p.x + 9.2f);
                sum += ((xy + yz + zx) / 3f) * amp;
                total += amp;
                amp *= settings.persistence;
                p *= settings.lacunarity;
            }
            return sum / Mathf.Max(.0001f, total);
        }

        private Color SampleBiome(Vector3 dir, float h)
        {
            float latitude = Mathf.Abs(dir.y);
            float moisture = Fractal(dir * 4.6f + Vector3.one * settings.seed * .0007f, 3);
            if (h < settings.seaLevel + .018f) return new Color(.78f, .69f, .44f);
            if (latitude > .80f || h > .60f) return new Color(.92f, .96f, 1f);
            if (h > .42f) return new Color(.34f, .33f, .31f);
            if (latitude < .30f && moisture < .48f) return new Color(.67f, .48f, .22f);
            if (moisture > .63f) return new Color(.08f, .34f, .12f);
            if (moisture < .38f) return new Color(.52f, .58f, .24f);
            return new Color(.16f, .48f, .17f);
        }

        private void CreateOcean(Transform root)
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ocean.name = "Ocean";
            ocean.transform.SetParent(root, false);
            float diameter = (settings.radius + settings.seaLevel * settings.maxTerrainHeight) * 2f;
            ocean.transform.localScale = Vector3.one * diameter;
            Collider collider = ocean.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            ocean.GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;
        }
    }
}
