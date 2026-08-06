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
            if (generateOnEnable && transform.Find("Generated Terrain") == null)
                Regenerate();
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
            Vector3[] normals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            for (int i = 0; i < normals.Length; i++) CreateFace(root, normals[i], i);
            CreateOcean(root);
        }

        private void CreateFace(Transform root, Vector3 localUp, int index)
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
            mesh.RecalculateBounds();

            var go = new GameObject($"Terrain Face {index}");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private float SampleHeight(Vector3 p)
        {
            Vector3 offset = new Vector3(settings.seed * .0013f, settings.seed * .0021f, settings.seed * .0037f);
            float continent = Fractal(p * settings.radius * settings.continentFrequency + offset, settings.octaves);
            float mountains = Fractal(p * settings.radius * settings.mountainFrequency - offset, Mathf.Max(2, settings.octaves - 1));
            mountains = Mathf.Pow(Mathf.Clamp01(mountains), 2.4f);
            return Mathf.Clamp(continent * .72f + mountains * .55f - .45f, -1f, 1f);
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
            if (h < settings.seaLevel + .03f) return new Color(.76f, .68f, .42f);
            if (latitude > .78f || h > .52f) return new Color(.92f, .95f, 1f);
            if (latitude < .28f && h < .25f) return new Color(.66f, .48f, .21f);
            if (h > .35f) return new Color(.38f, .36f, .33f);
            return new Color(.18f, .48f, .16f);
        }

        private void CreateOcean(Transform root)
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ocean.name = "Ocean";
            ocean.transform.SetParent(root, false);
            float diameter = (settings.radius + settings.seaLevel * settings.maxTerrainHeight) * 2f;
            ocean.transform.localScale = Vector3.one * diameter;
            var collider = ocean.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            ocean.GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;
        }
    }
}
